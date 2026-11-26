using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Dynamons
{
    /// <summary>Snapshot of a LAN host room discovered via UDP broadcast.</summary>
    public sealed class LanDiscoveredRoom
    {
        public string Key;
        public string HostIp;
        public string[] CandidateIps;
        public int TcpPort;
        public string HostName;
        public string RuleMode;
        public bool ItemsAllowed;
        public int TeamCount;
        public DateTime LastSeenUtc;
    }

    /// <summary>Main-thread-safe network message surfaced by <see cref="LanTransport"/>.</summary>
    public sealed class LanNetMessage
    {
        public string Type;
        public string JsonPayload;
        public string Reason;
    }

    /// <summary>
    /// Background-thread UDP LAN room discovery and newline-framed UTF-8 TCP transport.
    /// All <see cref="JsonUtility"/> / <see cref="LanCodec"/> calls stay strictly on the Unity main thread.
    /// </summary>
    public sealed class LanTransport : IDisposable
    {
        private const double RoomExpirySeconds = 3.0;
        private const int BeaconIntervalMs = 750;
        private const int ConnectTimeoutMs = 5000;
        private const int PollWaitUs = 100000;
        private const int ReadChunkBytes = 4096;
        private const int MaxRawBeaconQueue = 256;

        private const int RawKindConnected = 0;
        private const int RawKindDisconnected = 1;
        private const int RawKindLine = 2;

        private struct RawNetEvent
        {
            public int Gen;
            public int Kind;
            public string Text;
        }

        private struct RawBeaconPacket
        {
            public int Gen;
            public string RemoteIp;
            public string RawJson;
            public DateTime SeenUtc;
        }

        private struct OutgoingFrame
        {
            public int Gen;
            public byte[] Bytes;
        }

        private struct NicEndpoint
        {
            public IPAddress Ip;
            public IPAddress Mask;
            public IPAddress Broadcast;
            public string IpText;
            public int Rank;
            public bool IsVirtual;
        }

        private readonly object stateLock = new object();
        private readonly ConcurrentQueue<RawNetEvent> incomingQueue = new ConcurrentQueue<RawNetEvent>();
        private readonly ConcurrentQueue<RawBeaconPacket> rawBeaconQueue = new ConcurrentQueue<RawBeaconPacket>();
        private readonly ConcurrentQueue<OutgoingFrame> sendQueue = new ConcurrentQueue<OutgoingFrame>();
        private readonly AutoResetEvent sendSignal = new AutoResetEvent(false);
        private readonly Dictionary<string, LanDiscoveredRoom> discoveredRooms = new Dictionary<string, LanDiscoveredRoom>(StringComparer.Ordinal);

        private int sessionGen;
        private volatile bool disposed;
        private volatile bool isHost;
        private volatile bool isConnected;
        private volatile int boundPort;
        private bool disconnectEmitted;

        private byte[] beaconBytes;
        private NicEndpoint[] cachedNics = Array.Empty<NicEndpoint>();
        private TcpListener tcpListener;
        private TcpClient peerClient;
        private NetworkStream peerStream;
        private UdpClient udpHostClient;
        private UdpClient udpDiscoveryClient;
#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject androidMulticastLock;
        private bool androidProcessNetworkBound;
#endif

        private Thread udpBeaconThread;
        private Thread udpDiscoveryThread;
        private Thread tcpAcceptThread;
        private Thread tcpConnectThread;
        private Thread tcpReaderThread;
        private Thread tcpWriterThread;

        private long lastRecvUtcTicks;
        private bool hasHeartbeatBaseline;
        private float lastRecvUnscaled;
        private float lastPingSentUnscaled;

        public bool IsHost => isHost;
        public bool IsConnected => isConnected;
        public int BoundPort => boundPort;

        /// <summary>Binds a free TCP battle port (41976..41985) and starts broadcasting UDP room beacons.</summary>
        public void StartHost(LanBeaconDto initialBeacon)
        {
            if (disposed) return;
            Stop();

            int gen;
            TcpListener boundListener = null;
            int selectedPort = 0;

            for (int offset = 0; offset < LanConst.BattlePortSpan; offset++)
            {
                int candidatePort = LanConst.BattlePortBase + offset;
                TcpListener candidate = null;
                try
                {
                    candidate = new TcpListener(IPAddress.Any, candidatePort);
                    try { candidate.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true); }
                    catch (Exception) { }
                    candidate.Start();
                    boundListener = candidate;
                    selectedPort = candidatePort;
                    break;
                }
                catch (SocketException)
                {
                    CloseQuietly(candidate);
                }
                catch (Exception)
                {
                    CloseQuietly(candidate);
                }
            }

            lock (stateLock)
            {
                if (disposed)
                {
                    CloseQuietly(boundListener);
                    return;
                }
                gen = Volatile.Read(ref sessionGen);
                disconnectEmitted = false;
                if (boundListener == null || selectedPort == 0)
                {
                    isHost = false;
                    boundPort = 0;
                }
                else
                {
                    isHost = true;
                    boundPort = selectedPort;
                    tcpListener = boundListener;
                }
            }

            if (boundListener == null || selectedPort == 0)
            {
                EmitDisconnect(gen, "No free battle port (41976-41985)");
                return;
            }

            NicEndpoint[] nics = CollectLanInterfaces();
            Volatile.Write(ref cachedNics, nics);
            BindAndroidProcessToWifiNetwork(null);
            AcquireAndroidMulticastLock();

            UpdateBeacon(initialBeacon ?? new LanBeaconDto());

            UdpClient udp = null;
            try
            {
                udp = new UdpClient(AddressFamily.InterNetwork);
                udp.EnableBroadcast = true;
            }
            catch (Exception)
            {
                CloseQuietly(udp);
                udp = null;
            }

            lock (stateLock)
            {
                if (disposed || gen != sessionGen)
                {
                    CloseQuietly(udp);
                    return;
                }
                udpHostClient = udp;
                tcpAcceptThread = StartThread("LanTcpAccept", () => TcpAcceptLoop(gen, boundListener));
                udpBeaconThread = StartThread("LanUdpBeacon", () => UdpBeaconLoop(gen, udp));
            }
        }

        /// <summary>Updates the serialized UDP beacon bytes on the calling main thread.</summary>
        public void UpdateBeacon(LanBeaconDto beacon)
        {
            if (disposed || !isHost || beacon == null) return;
            string[] hostIps = SanitizeBeaconHostIps(beacon.hostIps);
            if (hostIps.Length == 0)
                hostIps = ExtractEndpointIps(Volatile.Read(ref cachedNics), 4);
            var normalized = new LanBeaconDto
            {
                protocolVersion = beacon.protocolVersion > 0 ? beacon.protocolVersion : LanConst.ProtocolVersion,
                roomId = beacon.roomId ?? string.Empty,
                hostName = string.IsNullOrEmpty(beacon.hostName) ? "HOST" : beacon.hostName,
                ruleMode = beacon.ruleMode == "real" ? "real" : "fair20",
                itemsAllowed = beacon.itemsAllowed,
                tcpPort = boundPort > 0 ? boundPort : beacon.tcpPort,
                teamCount = beacon.teamCount,
                hostIps = hostIps
            };
            string json = JsonUtility.ToJson(normalized);
            if (string.IsNullOrEmpty(json)) return;
            Volatile.Write(ref beaconBytes, Encoding.UTF8.GetBytes(json));
        }

        /// <summary>Starts listening for UDP room beacons on <see cref="LanConst.DiscoveryPort"/>.</summary>
        public void StartDiscovery()
        {
            if (disposed) return;
            Stop();

            int gen = Volatile.Read(ref sessionGen);
            lock (stateLock)
            {
                if (disposed || gen != sessionGen) return;
                disconnectEmitted = false;
            }

            NicEndpoint[] nics = CollectLanInterfaces();
            Volatile.Write(ref cachedNics, nics);
            BindAndroidProcessToWifiNetwork(null);
            AcquireAndroidMulticastLock();

            UdpClient udp = null;
            try
            {
                udp = new UdpClient(AddressFamily.InterNetwork);
                try { udp.ExclusiveAddressUse = false; } catch (Exception) { }
                udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udp.EnableBroadcast = true;
                udp.Client.Bind(new IPEndPoint(IPAddress.Any, LanConst.DiscoveryPort));
                if (IPAddress.TryParse(LanConst.DiscoveryMulticastIp, out var mcastIp))
                {
                    bool joinedAny = false;
                    for (int i = 0; i < nics.Length; i++)
                    {
                        if (nics[i].IsVirtual || nics[i].Ip == null) continue;
                        try
                        {
                            udp.JoinMulticastGroup(mcastIp, nics[i].Ip);
                            joinedAny = true;
                        }
                        catch (Exception) { }
                    }
                    if (!joinedAny)
                    {
                        try { udp.JoinMulticastGroup(mcastIp); } catch (Exception) { }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LanTransport] Discovery bind failed: " + ex.Message);
                CloseQuietly(udp);
                return;
            }

            lock (stateLock)
            {
                if (disposed || gen != sessionGen)
                {
                    CloseQuietly(udp);
                    return;
                }
                udpDiscoveryClient = udp;
                udpDiscoveryThread = StartThread("LanUdpDiscovery", () => UdpDiscoveryLoop(gen, udp));
            }
        }

        /// <summary>Decodes buffered discovery beacons on the main thread, deduplicates by roomId, prunes stale rooms (&gt;3.0s), and ranks LAN IPs.</summary>
        public List<LanDiscoveredRoom> GetRooms()
        {
            var result = new List<LanDiscoveredRoom>();
            if (disposed) return result;

            int activeGen = Volatile.Read(ref sessionGen);
            DateTime nowUtc = DateTime.UtcNow;
            BuildSubnetArrays(Volatile.Read(ref cachedNics), out string[] localIps, out string[] localMasks);

            while (rawBeaconQueue.TryDequeue(out var pkt))
            {
                if (pkt.Gen != activeGen) continue;
                var dto = LanCodec.Decode<LanBeaconDto>(pkt.RawJson);
                if (dto == null || dto.protocolVersion != LanConst.ProtocolVersion) continue;
                if (dto.tcpPort < 1 || dto.tcpPort > 65535 || string.IsNullOrEmpty(pkt.RemoteIp)) continue;

                string key = !string.IsNullOrEmpty(dto.roomId) ? dto.roomId : (pkt.RemoteIp + ":" + dto.tcpPort);
                discoveredRooms.TryGetValue(key, out var existing);

                var rawCandidates = new List<string>(8);
                rawCandidates.Add(pkt.RemoteIp);
                if (dto.hostIps != null) rawCandidates.AddRange(dto.hostIps);
                if (existing != null && existing.CandidateIps != null) rawCandidates.AddRange(existing.CandidateIps);

                string[] rankedIps = RankCandidateHostIps(pkt.RemoteIp, rawCandidates.ToArray(), localIps, localMasks);
                if (rankedIps.Length == 0) continue;

                bool isNewer = existing == null || pkt.SeenUtc >= existing.LastSeenUtc;
                discoveredRooms[key] = new LanDiscoveredRoom
                {
                    Key = key,
                    HostIp = rankedIps[0],
                    CandidateIps = rankedIps,
                    TcpPort = isNewer ? dto.tcpPort : existing.TcpPort,
                    HostName = isNewer ? (string.IsNullOrEmpty(dto.hostName) ? "HOST" : dto.hostName) : existing.HostName,
                    RuleMode = isNewer ? (dto.ruleMode == "real" ? "real" : "fair20") : existing.RuleMode,
                    ItemsAllowed = isNewer ? dto.itemsAllowed : existing.ItemsAllowed,
                    TeamCount = isNewer ? dto.teamCount : existing.TeamCount,
                    LastSeenUtc = isNewer ? pkt.SeenUtc : existing.LastSeenUtc
                };
            }

            List<string> expiredKeys = null;
            foreach (var kv in discoveredRooms)
            {
                if ((nowUtc - kv.Value.LastSeenUtc).TotalSeconds > RoomExpirySeconds)
                {
                    if (expiredKeys == null) expiredKeys = new List<string>();
                    expiredKeys.Add(kv.Key);
                }
                else
                {
                    result.Add(kv.Value);
                }
            }
            if (expiredKeys != null)
            {
                for (int i = 0; i < expiredKeys.Count; i++)
                    discoveredRooms.Remove(expiredKeys[i]);
            }

            result.Sort((a, b) =>
            {
                int aSub = MatchesAnyLocalSubnet(a.HostIp, localIps, localMasks) ? 0 : 1;
                int bSub = MatchesAnyLocalSubnet(b.HostIp, localIps, localMasks) ? 0 : 1;
                if (aSub != bSub) return aSub.CompareTo(bSub);
                int nameCmp = string.CompareOrdinal(a.HostName, b.HostName);
                return nameCmp != 0 ? nameCmp : string.CompareOrdinal(a.Key, b.Key);
            });
            return result;
        }

        /// <summary>Asynchronously connects to a host over TCP with a 5.0s timeout.</summary>
        public void ConnectDirect(string ip, int port) => ConnectDirect(ip, port, null);

        /// <summary>Asynchronously connects to a primary IP and optional fallback IPs over TCP with local-NIC retry.</summary>
        public void ConnectDirect(string ip, int port, string[] fallbackIps)
        {
            if (disposed) return;
            Stop();

            int gen;
            lock (stateLock)
            {
                if (disposed) return;
                gen = Volatile.Read(ref sessionGen);
                disconnectEmitted = false;
                isHost = false;
                boundPort = 0;
            }

            string trimmedIp = ip != null ? ip.Trim() : string.Empty;
            if (string.IsNullOrEmpty(trimmedIp) || port < 1 || port > 65535)
            {
                EmitDisconnect(gen, "Invalid IP address or port");
                return;
            }

            var candidateList = new List<string>(4) { trimmedIp };
            var seen = new HashSet<string>(StringComparer.Ordinal) { trimmedIp };
            if (fallbackIps != null)
            {
                for (int i = 0; i < fallbackIps.Length; i++)
                {
                    string fb = fallbackIps[i] != null ? fallbackIps[i].Trim() : string.Empty;
                    if (string.IsNullOrEmpty(fb) || seen.Contains(fb)) continue;
                    if (!TryParseValidLanIpv4(fb, out _)) continue;
                    seen.Add(fb);
                    candidateList.Add(fb);
                }
            }

            NicEndpoint[] localNics = CollectLanInterfaces();
            Volatile.Write(ref cachedNics, localNics);
            IPAddress.TryParse(trimmedIp, out IPAddress parsedPrimary);
            BindAndroidProcessToWifiNetwork(parsedPrimary);

            string[] candidates = candidateList.ToArray();
            lock (stateLock)
            {
                if (disposed || gen != sessionGen) return;
                tcpConnectThread = StartThread("LanTcpConnect", () => TcpConnectLoop(gen, candidates, port, localNics));
            }
        }

        /// <summary>Encodes the payload on the main thread and queues UTF-8 line bytes for non-blocking socket transmission.</summary>
        public void Send<T>(string type, T payload)
        {
            if (disposed || !isConnected || string.IsNullOrEmpty(type)) return;
            int gen = Volatile.Read(ref sessionGen);
            string json = LanCodec.Encode(type, payload);
            if (string.IsNullOrEmpty(json)) return;
            byte[] bytes = Encoding.UTF8.GetBytes(json + "\n");
            if (bytes.Length > LanConst.MaxFrameBytes)
            {
                Debug.LogWarning("[LanTransport] Dropping outgoing frame exceeding MaxFrameBytes: " + type);
                return;
            }
            sendQueue.Enqueue(new OutgoingFrame { Gen = gen, Bytes = bytes });
            WakeWriter();
        }

        /// <summary>Drains incoming frames on the main thread, filters ping/pong heartbeats, and enforces peer timeout.</summary>
        public List<LanNetMessage> DrainMainThreadMessages(float nowUnscaled)
        {
            var result = new List<LanNetMessage>();
            if (disposed) return result;

            int activeGen = Volatile.Read(ref sessionGen);
            bool sawDisconnectedThisDrain = false;

            while (incomingQueue.TryDequeue(out var raw))
            {
                if (raw.Gen != activeGen) continue;
                if (sawDisconnectedThisDrain) continue;

                if (raw.Kind == RawKindConnected)
                {
                    hasHeartbeatBaseline = true;
                    lastRecvUnscaled = nowUnscaled;
                    lastPingSentUnscaled = nowUnscaled;
                    result.Add(new LanNetMessage
                    {
                        Type = "connected",
                        JsonPayload = string.Empty,
                        Reason = string.Empty
                    });
                }
                else if (raw.Kind == RawKindDisconnected)
                {
                    hasHeartbeatBaseline = false;
                    sawDisconnectedThisDrain = true;
                    result.Add(new LanNetMessage
                    {
                        Type = "disconnected",
                        JsonPayload = string.Empty,
                        Reason = string.IsNullOrEmpty(raw.Text) ? "Disconnected" : raw.Text
                    });
                }
                else if (raw.Kind == RawKindLine)
                {
                    var env = LanCodec.Decode<LanWireEnvelope>(raw.Text);
                    if (env == null || string.IsNullOrEmpty(env.type)) continue;

                    if (!hasHeartbeatBaseline)
                    {
                        hasHeartbeatBaseline = true;
                        lastPingSentUnscaled = nowUnscaled;
                    }
                    lastRecvUnscaled = nowUnscaled;

                    if (env.type == "ping")
                    {
                        var ping = LanCodec.Decode<LanPingDto>(env.payload);
                        long ts = ping != null ? ping.ts : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                        Send("pong", new LanPingDto { ts = ts });
                        continue;
                    }
                    if (env.type == "pong")
                    {
                        continue;
                    }

                    result.Add(new LanNetMessage
                    {
                        Type = env.type,
                        JsonPayload = env.payload ?? "{}",
                        Reason = string.Empty
                    });
                }
            }

            if (isConnected && !sawDisconnectedThisDrain)
            {
                if (!hasHeartbeatBaseline)
                {
                    hasHeartbeatBaseline = true;
                    lastRecvUnscaled = nowUnscaled;
                    lastPingSentUnscaled = nowUnscaled;
                }
                if (nowUnscaled < lastRecvUnscaled) lastRecvUnscaled = nowUnscaled;
                if (nowUnscaled < lastPingSentUnscaled) lastPingSentUnscaled = nowUnscaled;

                double utcSilentSec = (DateTime.UtcNow.Ticks - Interlocked.Read(ref lastRecvUtcTicks)) / (double)TimeSpan.TicksPerSecond;
                if ((nowUnscaled - lastRecvUnscaled) >= LanConst.PeerTimeoutSec || utcSilentSec >= LanConst.PeerTimeoutSec)
                {
                    if (TryMarkDisconnectEmitted(activeGen))
                    {
                        CloseSocketsForDisconnect(activeGen);
                        hasHeartbeatBaseline = false;
                        result.Add(new LanNetMessage
                        {
                            Type = "disconnected",
                            JsonPayload = string.Empty,
                            Reason = "Connection timed out"
                        });
                    }
                }
                else if ((nowUnscaled - lastPingSentUnscaled) >= LanConst.PingIntervalSec)
                {
                    lastPingSentUnscaled = nowUnscaled;
                    Send("ping", new LanPingDto { ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
                }
            }

            return result;
        }

        /// <summary>Returns active non-loopback, non-link-local IPv4 addresses sorted with physical LAN subnets first.</summary>
        public static string[] LocalIpv4Addresses()
        {
            NicEndpoint[] nics = CollectLanInterfaces();
            var result = new string[nics.Length];
            for (int i = 0; i < nics.Length; i++)
                result[i] = nics[i].IpText;
            return result;
        }

        /// <summary>Idempotently shuts down active host, discovery, and TCP peer sockets.</summary>
        public void Stop()
        {
            TcpListener listenerToClose;
            NetworkStream streamToClose;
            TcpClient clientToClose;
            UdpClient udpHostToClose;
            UdpClient udpDiscToClose;

            lock (stateLock)
            {
                Volatile.Write(ref sessionGen, sessionGen + 1);
                disconnectEmitted = true;
                isHost = false;
                isConnected = false;
                boundPort = 0;
                Volatile.Write(ref beaconBytes, null);
                Volatile.Write(ref cachedNics, Array.Empty<NicEndpoint>());
                Interlocked.Exchange(ref lastRecvUtcTicks, 0);

                DetachAndCloseSocketsLocked(out listenerToClose, out streamToClose, out clientToClose,
                    out udpHostToClose, out udpDiscToClose);

                udpBeaconThread = null;
                udpDiscoveryThread = null;
                tcpAcceptThread = null;
                tcpConnectThread = null;
                tcpReaderThread = null;
                tcpWriterThread = null;
            }

            CloseDetachedSockets(listenerToClose, streamToClose, clientToClose, udpHostToClose, udpDiscToClose);
            ReleaseAndroidMulticastLock();
            ClearAndroidProcessNetworkBinding();

            hasHeartbeatBaseline = false;
            lastRecvUnscaled = 0f;
            lastPingSentUnscaled = 0f;
            discoveredRooms.Clear();

            while (incomingQueue.TryDequeue(out _)) { }
            while (rawBeaconQueue.TryDequeue(out _)) { }
            while (sendQueue.TryDequeue(out _)) { }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            Thread[] threads;
            lock (stateLock)
            {
                threads = new[]
                {
                    udpBeaconThread, udpDiscoveryThread, tcpAcceptThread,
                    tcpConnectThread, tcpReaderThread, tcpWriterThread
                };
            }

            Stop();

            for (int i = 0; i < threads.Length; i++)
            {
                Thread t = threads[i];
                if (t != null && t.IsAlive)
                {
                    try { t.Join(150); } catch (Exception) { }
                }
            }

            try { sendSignal.Dispose(); } catch (Exception) { }
        }

        private bool IsSessionCancelled(int gen)
        {
            return disposed || Volatile.Read(ref sessionGen) != gen;
        }

        private bool TryMarkDisconnectEmitted(int gen)
        {
            lock (stateLock)
            {
                if (disposed || gen != sessionGen || disconnectEmitted) return false;
                disconnectEmitted = true;
                isConnected = false;
                return true;
            }
        }

        private void EmitDisconnect(int gen, string reason)
        {
            if (!TryMarkDisconnectEmitted(gen)) return;
            CloseSocketsForDisconnect(gen);
            incomingQueue.Enqueue(new RawNetEvent
            {
                Gen = gen,
                Kind = RawKindDisconnected,
                Text = reason
            });
        }

        private void CloseSocketsForDisconnect(int gen)
        {
            TcpListener listenerToClose;
            NetworkStream streamToClose;
            TcpClient clientToClose;
            UdpClient udpHostToClose;
            UdpClient udpDiscToClose;

            lock (stateLock)
            {
                if (gen != Volatile.Read(ref sessionGen)) return;
                isConnected = false;
                isHost = false;
                boundPort = 0;
                Volatile.Write(ref beaconBytes, null);
                DetachAndCloseSocketsLocked(out listenerToClose, out streamToClose, out clientToClose,
                    out udpHostToClose, out udpDiscToClose);
            }

            CloseDetachedSockets(listenerToClose, streamToClose, clientToClose, udpHostToClose, udpDiscToClose);
        }

        private void DetachAndCloseSocketsLocked(out TcpListener listener, out NetworkStream stream,
            out TcpClient client, out UdpClient udpHost, out UdpClient udpDisc)
        {
            listener = tcpListener; tcpListener = null;
            stream = peerStream; peerStream = null;
            client = peerClient; peerClient = null;
            udpHost = udpHostClient; udpHostClient = null;
            udpDisc = udpDiscoveryClient; udpDiscoveryClient = null;
        }

        private void CloseDetachedSockets(TcpListener listener, NetworkStream stream,
            TcpClient client, UdpClient udpHost, UdpClient udpDisc)
        {
            CloseQuietly(listener);
            CloseQuietly(stream);
            CloseQuietly(client);
            CloseQuietly(udpHost);
            CloseQuietly(udpDisc);
            WakeWriter();
        }

        private void WakeWriter()
        {
            try { sendSignal.Set(); } catch (ObjectDisposedException) { }
        }

        private void UdpBeaconLoop(int gen, UdpClient fallbackUdp)
        {
            IPAddress.TryParse(LanConst.DiscoveryMulticastIp, out IPAddress mcastIp);
            try
            {
                while (!IsSessionCancelled(gen))
                {
                    if (!isConnected)
                    {
                        byte[] bytes = Volatile.Read(ref beaconBytes);
                        if (bytes != null && bytes.Length > 0)
                        {
                            NicEndpoint[] nics = Volatile.Read(ref cachedNics);
                            bool anySent = false;
                            if (nics != null)
                            {
                                for (int i = 0; i < nics.Length; i++)
                                {
                                    if (IsSessionCancelled(gen)) return;
                                    if (SendBeaconOnNic(nics[i], bytes, mcastIp))
                                        anySent = true;
                                }
                            }
                            if (fallbackUdp != null && !IsSessionCancelled(gen))
                            {
                                if (TrySendUdp(fallbackUdp, bytes, IPAddress.Broadcast))
                                    anySent = true;
                                if ((nics == null || nics.Length == 0) && mcastIp != null && TrySendUdp(fallbackUdp, bytes, mcastIp))
                                    anySent = true;
                            }
                            if (!anySent && fallbackUdp != null && !IsSessionCancelled(gen))
                            {
                                TrySendUdp(fallbackUdp, bytes, IPAddress.Loopback);
                            }
                        }
                    }

                    const int sleepSliceMs = 50;
                    for (int waited = 0; waited < BeaconIntervalMs; waited += sleepSliceMs)
                    {
                        if (IsSessionCancelled(gen)) return;
                        Thread.Sleep(sleepSliceMs);
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        private static bool SendBeaconOnNic(NicEndpoint nic, byte[] bytes, IPAddress mcastIp)
        {
            if (nic.Ip == null || nic.IsVirtual) return false;
            UdpClient perNic = null;
            bool sent = false;
            try
            {
                perNic = new UdpClient(new IPEndPoint(nic.Ip, 0));
                perNic.EnableBroadcast = true;
                try
                {
                    perNic.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, nic.Ip.GetAddressBytes());
                    perNic.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 2);
                }
                catch (Exception) { }

                // Only send directed subnet broadcast and LAN multicast on per-NIC sockets;
                // 255.255.255.255 goes via fallbackUdp (0.0.0.0) so Linux routes won't spoof a virtual source IP.
                if (nic.Broadcast != null && TrySendUdp(perNic, bytes, nic.Broadcast)) sent = true;
                if (mcastIp != null && TrySendUdp(perNic, bytes, mcastIp)) sent = true;
            }
            catch (SocketException)
            {
            }
            catch (Exception)
            {
            }
            finally
            {
                CloseQuietly(perNic);
            }
            return sent;
        }

        private static bool TrySendUdp(UdpClient udp, byte[] bytes, IPAddress dst)
        {
            if (udp == null || bytes == null || dst == null) return false;
            try
            {
                udp.Send(bytes, bytes.Length, new IPEndPoint(dst, LanConst.DiscoveryPort));
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }

        private void UdpDiscoveryLoop(int gen, UdpClient udp)
        {
            try
            {
                Socket sock = udp.Client;
                while (!IsSessionCancelled(gen))
                {
                    try
                    {
                        if (sock != null && !sock.Poll(PollWaitUs, SelectMode.SelectRead))
                            continue;
                        if (IsSessionCancelled(gen)) break;

                        IPEndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);
                        byte[] data = udp.Receive(ref remoteEp);
                        if (IsSessionCancelled(gen)) break;
                        if (data == null || data.Length == 0 || data.Length > LanConst.MaxFrameBytes) continue;
                        if (remoteEp == null || remoteEp.Address == null) continue;

                        IPAddress srcAddr = remoteEp.Address;
                        if (srcAddr.IsIPv4MappedToIPv6) srcAddr = srcAddr.MapToIPv4();
                        string remoteIp = srcAddr.ToString();
                        string rawJson = Encoding.UTF8.GetString(data);

                        while (rawBeaconQueue.Count >= MaxRawBeaconQueue && rawBeaconQueue.TryDequeue(out _)) { }
                        rawBeaconQueue.Enqueue(new RawBeaconPacket
                        {
                            Gen = gen,
                            RemoteIp = remoteIp,
                            RawJson = rawJson,
                            SeenUtc = DateTime.UtcNow
                        });
                    }
                    catch (SocketException)
                    {
                        if (IsSessionCancelled(gen)) break;
                        Thread.Sleep(20);
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        private void TcpAcceptLoop(int gen, TcpListener listener)
        {
            try
            {
                Socket serverSocket = listener.Server;
                while (!IsSessionCancelled(gen))
                {
                    TcpClient incoming;
                    try
                    {
                        if (serverSocket != null && !serverSocket.Poll(PollWaitUs, SelectMode.SelectRead))
                            continue;
                        if (IsSessionCancelled(gen)) break;
                        incoming = listener.AcceptTcpClient();
                    }
                    catch (SocketException)
                    {
                        if (IsSessionCancelled(gen)) break;
                        Thread.Sleep(20);
                        continue;
                    }
                    catch (Exception ex) when (ex is ObjectDisposedException || ex is InvalidOperationException)
                    {
                        break;
                    }

                    if (IsSessionCancelled(gen))
                    {
                        CloseQuietly(incoming);
                        break;
                    }

                    NetworkStream stream = null;
                    try
                    {
                        incoming.NoDelay = true;
                        stream = incoming.GetStream();
                    }
                    catch (Exception)
                    {
                        CloseQuietly(stream);
                        CloseQuietly(incoming);
                        continue;
                    }

                    lock (stateLock)
                    {
                        if (IsSessionCancelled(gen) || isConnected)
                        {
                            CloseQuietly(stream);
                            CloseQuietly(incoming);
                            continue;
                        }

                        peerClient = incoming;
                        peerStream = stream;
                        Interlocked.Exchange(ref lastRecvUtcTicks, DateTime.UtcNow.Ticks);
                        Thread.MemoryBarrier();
                        isConnected = true;
                        incomingQueue.Enqueue(new RawNetEvent { Gen = gen, Kind = RawKindConnected });
                        tcpReaderThread = StartThread("LanTcpReader", () => TcpReaderLoop(gen, incoming, stream));
                        tcpWriterThread = StartThread("LanTcpWriter", () => TcpWriterLoop(gen, stream));
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        private void TcpConnectLoop(int gen, string[] candidates, int port, NicEndpoint[] localNics)
        {
            if (candidates == null || candidates.Length == 0)
            {
                EmitDisconnect(gen, "Invalid IP address or port");
                return;
            }

            int totalBudgetMs = candidates.Length > 1 ? 7500 : ConnectTimeoutMs;
            var totalWatch = Stopwatch.StartNew();
            string bestError = null;

            for (int i = 0; i < candidates.Length; i++)
            {
                if (IsSessionCancelled(gen)) return;
                int remainingMs = totalBudgetMs - (int)totalWatch.ElapsedMilliseconds;
                if (remainingMs < 350) break;

                string ip = candidates[i];
                if (string.IsNullOrEmpty(ip)) continue;

                int attemptBudget = Math.Min(ConnectTimeoutMs, remainingMs);
                if (TryConnectOnce(gen, ip, port, null, attemptBudget,
                    out TcpClient connectedClient, out SocketError sockErr, out int nativeErr, out string rawErr))
                {
                    if (RegisterConnectedClient(gen, connectedClient)) return;
                    CloseQuietly(connectedClient);
                    return;
                }
                if (IsSessionCancelled(gen)) return;

                string diag = FormatSocketConnectError(ip, port, sockErr, nativeErr, rawErr);
                if (string.IsNullOrEmpty(bestError)) bestError = diag;

                bool isUnreachable = sockErr == SocketError.HostUnreachable ||
                                     sockErr == SocketError.NetworkUnreachable ||
                                     nativeErr == 113 || nativeErr == 101 ||
                                     nativeErr == 10065 || nativeErr == 10051 ||
                                     (rawErr != null && (rawErr.Contains("(113)") || rawErr.Contains("(101)")));
                if (isUnreachable)
                {
                    IPAddress localBindIp = FindSameSubnetLocalIp(ip, localNics);
                    int retryRemaining = totalBudgetMs - (int)totalWatch.ElapsedMilliseconds;
                    if (localBindIp != null && retryRemaining >= 350)
                    {
                        if (TryConnectOnce(gen, ip, port, localBindIp, Math.Min(2500, retryRemaining),
                            out TcpClient retryClient, out SocketError retryErr, out int retryNative, out string retryRaw))
                        {
                            if (RegisterConnectedClient(gen, retryClient)) return;
                            CloseQuietly(retryClient);
                            return;
                        }
                        if (IsSessionCancelled(gen)) return;
                        if (retryErr != SocketError.TimedOut)
                            bestError = FormatSocketConnectError(ip, port, retryErr, retryNative, retryRaw);
                    }
                }
            }

            if (IsSessionCancelled(gen)) return;
            string finalMsg = string.IsNullOrEmpty(bestError) ? "Connection failed" : bestError;
            Debug.LogWarning("[LanTransport] " + finalMsg);
            EmitDisconnect(gen, finalMsg);
        }

        private bool TryConnectOnce(int gen, string ip, int port, IPAddress localBindIp, int timeoutMs,
            out TcpClient connectedClient, out SocketError sockErr, out int nativeErr, out string rawErr)
        {
            connectedClient = null;
            sockErr = SocketError.Success;
            nativeErr = 0;
            rawErr = string.Empty;

            TcpClient client = null;
            try
            {
                IPAddress parsedIp;
                bool isLiteral = IPAddress.TryParse(ip, out parsedIp);
                client = new TcpClient(isLiteral ? parsedIp.AddressFamily : AddressFamily.InterNetwork);
                if (localBindIp != null)
                {
                    try { client.Client.Bind(new IPEndPoint(localBindIp, 0)); }
                    catch (Exception) { }
                }

                IAsyncResult ar = isLiteral
                    ? client.BeginConnect(parsedIp, port, null, null)
                    : client.BeginConnect(ip, port, null, null);

                var stopwatch = Stopwatch.StartNew();
                while (!ar.IsCompleted)
                {
                    if (IsSessionCancelled(gen))
                    {
                        CloseQuietly(client);
                        return false;
                    }
                    if (stopwatch.ElapsedMilliseconds >= timeoutMs)
                    {
                        CloseQuietly(client);
                        sockErr = SocketError.TimedOut;
                        nativeErr = 110;
                        rawErr = "Connection timed out";
                        return false;
                    }
                    ar.AsyncWaitHandle.WaitOne(50);
                }

                client.EndConnect(ar);
                if (IsSessionCancelled(gen))
                {
                    CloseQuietly(client);
                    return false;
                }

                connectedClient = client;
                return true;
            }
            catch (SocketException sex)
            {
                sockErr = sex.SocketErrorCode;
                nativeErr = sex.NativeErrorCode;
                rawErr = sex.Message;
                CloseQuietly(client);
                return false;
            }
            catch (Exception ex)
            {
                sockErr = SocketError.SocketError;
                rawErr = ex.Message;
                CloseQuietly(client);
                return false;
            }
        }

        private bool RegisterConnectedClient(int gen, TcpClient client)
        {
            if (client == null) return false;
            NetworkStream stream = null;
            try
            {
                client.NoDelay = true;
                stream = client.GetStream();
                lock (stateLock)
                {
                    if (IsSessionCancelled(gen))
                    {
                        CloseQuietly(stream);
                        return false;
                    }

                    peerClient = client;
                    peerStream = stream;
                    Interlocked.Exchange(ref lastRecvUtcTicks, DateTime.UtcNow.Ticks);
                    Thread.MemoryBarrier();
                    isConnected = true;
                    incomingQueue.Enqueue(new RawNetEvent { Gen = gen, Kind = RawKindConnected });
                    tcpReaderThread = StartThread("LanTcpReader", () => TcpReaderLoop(gen, client, stream));
                    tcpWriterThread = StartThread("LanTcpWriter", () => TcpWriterLoop(gen, stream));
                    return true;
                }
            }
            catch (Exception ex)
            {
                CloseQuietly(stream);
                EmitDisconnect(gen, string.IsNullOrEmpty(ex.Message) ? "Connection failed" : ex.Message);
                return false;
            }
        }

        private static IPAddress FindSameSubnetLocalIp(string targetIpText, NicEndpoint[] localNics)
        {
            if (localNics == null || localNics.Length == 0) return null;
            if (!TryParseValidLanIpv4(targetIpText, out IPAddress targetIp)) return null;
            for (int i = 0; i < localNics.Length; i++)
            {
                NicEndpoint nic = localNics[i];
                if (nic.IsVirtual || nic.Ip == null) continue;
                if (Equals(nic.Ip, targetIp)) continue;
                if (IsSameSubnet(targetIp, nic.Ip, nic.Mask)) return nic.Ip;
            }
            return null;
        }

        private void TcpReaderLoop(int gen, TcpClient client, NetworkStream stream)
        {
            byte[] readBuf = new byte[ReadChunkBytes];
            byte[] lineBuf = new byte[LanConst.MaxFrameBytes];
            int lineLen = 0;

            try
            {
                Socket sock = client.Client;
                while (!IsSessionCancelled(gen))
                {
                    if (sock != null && !sock.Poll(PollWaitUs, SelectMode.SelectRead))
                        continue;
                    if (IsSessionCancelled(gen)) break;

                    int bytesRead = stream.Read(readBuf, 0, readBuf.Length);
                    if (bytesRead <= 0)
                    {
                        EmitDisconnect(gen, "Peer closed connection");
                        break;
                    }

                    int offset = 0;
                    while (offset < bytesRead)
                    {
                        int nlIdx = Array.IndexOf(readBuf, (byte)'\n', offset, bytesRead - offset);
                        int segLen = (nlIdx >= 0 ? nlIdx : bytesRead) - offset;
                        if (lineLen + segLen > LanConst.MaxFrameBytes)
                        {
                            EmitDisconnect(gen, "Frame exceeded max size (65536 bytes)");
                            return;
                        }
                        if (segLen > 0)
                        {
                            Buffer.BlockCopy(readBuf, offset, lineBuf, lineLen, segLen);
                            lineLen += segLen;
                        }
                        if (nlIdx >= 0)
                        {
                            int msgLen = lineLen;
                            if (msgLen > 0 && lineBuf[msgLen - 1] == (byte)'\r') msgLen--;
                            if (msgLen > 0)
                            {
                                string json = Encoding.UTF8.GetString(lineBuf, 0, msgLen);
                                Interlocked.Exchange(ref lastRecvUtcTicks, DateTime.UtcNow.Ticks);
                                incomingQueue.Enqueue(new RawNetEvent
                                {
                                    Gen = gen,
                                    Kind = RawKindLine,
                                    Text = json
                                });
                            }
                            lineLen = 0;
                            offset = nlIdx + 1;
                        }
                        else
                        {
                            break;
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is SocketException || ex is ObjectDisposedException)
            {
                EmitDisconnect(gen, string.IsNullOrEmpty(ex.Message) ? "Connection lost" : ex.Message);
            }
            catch (Exception ex)
            {
                EmitDisconnect(gen, string.IsNullOrEmpty(ex.Message) ? "Connection error" : ex.Message);
            }
        }

        private void TcpWriterLoop(int gen, NetworkStream stream)
        {
            try
            {
                while (!IsSessionCancelled(gen))
                {
                    while (!IsSessionCancelled(gen) && sendQueue.TryDequeue(out var item))
                    {
                        if (item.Gen != gen || item.Bytes == null) continue;
                        stream.Write(item.Bytes, 0, item.Bytes.Length);
                    }
                    if (IsSessionCancelled(gen)) break;

                    try { sendSignal.WaitOne(200); }
                    catch (ObjectDisposedException) { break; }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is SocketException || ex is ObjectDisposedException)
            {
                EmitDisconnect(gen, string.IsNullOrEmpty(ex.Message) ? "Send failed" : ex.Message);
            }
            catch (Exception ex)
            {
                EmitDisconnect(gen, string.IsNullOrEmpty(ex.Message) ? "Send error" : ex.Message);
            }
        }

        private static NicEndpoint[] CollectLanInterfaces()
        {
            var byIp = new Dictionary<string, NicEndpoint>(StringComparer.Ordinal);

            void AddNic(IPAddress addr, IPAddress mask, int priorityBase, bool isVirtual)
            {
                if (addr == null || addr.AddressFamily != AddressFamily.InterNetwork) return;
                if (IPAddress.IsLoopback(addr)) return;
                byte[] ipBytes = addr.GetAddressBytes();
                if (ipBytes == null || ipBytes.Length != 4 || ipBytes[0] == 0) return;
                if (ipBytes[0] == 169 && ipBytes[1] == 254) return;

                string ipText = addr.ToString();
                int rank = priorityBase + PrivateSubnetRank(ipBytes);
                IPAddress resolvedMask = mask ?? PrefixLengthToMask(24);
                IPAddress bcast = ComputeBroadcastIp(addr, resolvedMask);

                if (byIp.TryGetValue(ipText, out var existing))
                {
                    bool keepVirtual = existing.IsVirtual && isVirtual;
                    if (existing.Rank <= rank && (existing.IsVirtual == keepVirtual))
                    {
                        if (existing.Mask == null && mask != null) existing.Mask = mask;
                        if (existing.Broadcast == null && bcast != null) existing.Broadcast = bcast;
                        byIp[ipText] = existing;
                        return;
                    }
                    byIp[ipText] = new NicEndpoint
                    {
                        Ip = addr,
                        Mask = mask ?? existing.Mask ?? resolvedMask,
                        Broadcast = bcast ?? existing.Broadcast,
                        IpText = ipText,
                        Rank = Math.Min(existing.Rank, rank),
                        IsVirtual = keepVirtual
                    };
                    return;
                }

                byIp[ipText] = new NicEndpoint
                {
                    Ip = addr,
                    Mask = resolvedMask,
                    Broadcast = bcast,
                    IpText = ipText,
                    Rank = rank,
                    IsVirtual = isVirtual
                };
            }

            CollectAndroidLanInterfaces(AddNic);
            EnumerateActiveIpv4((addr, mask, isVirtual) => AddNic(addr, mask, isVirtual ? 100 : 15, isVirtual));

            bool hasPhysical = false;
            foreach (var nic in byIp.Values)
            {
                if (!nic.IsVirtual) { hasPhysical = true; break; }
            }
            if (!hasPhysical)
            {
                ProbeOutboundLanIpv4(AddNic);
                foreach (var nic in byIp.Values)
                {
                    if (!nic.IsVirtual) { hasPhysical = true; break; }
                }
            }

            var list = new List<NicEndpoint>(byIp.Count);
            foreach (var nic in byIp.Values)
            {
                if (hasPhysical && nic.IsVirtual) continue;
                list.Add(nic);
            }
            list.Sort((a, b) =>
            {
                int cmp = a.Rank.CompareTo(b.Rank);
                return cmp != 0 ? cmp : StringComparer.Ordinal.Compare(a.IpText, b.IpText);
            });
            return list.ToArray();
        }

        private static void ProbeOutboundLanIpv4(Action<IPAddress, IPAddress, int, bool> addNic)
        {
            string[] probes = { "192.168.1.1", "192.168.0.1", "10.0.0.1", "172.16.0.1" };
            for (int i = 0; i < probes.Length; i++)
            {
                try
                {
                    using (var sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                    {
                        sock.Connect(IPAddress.Parse(probes[i]), 65530);
                        if (sock.LocalEndPoint is IPEndPoint localEp && localEp.Address != null)
                        {
                            addNic(localEp.Address, PrefixLengthToMask(24), 25, false);
                        }
                    }
                }
                catch (SocketException) { }
                catch (Exception) { }
            }
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static readonly jvalue[] EmptyJniArgs = Array.Empty<jvalue>();

        private static bool ClearPendingJniException()
        {
            if (AndroidJNI.ExceptionOccurred() != IntPtr.Zero)
            {
                AndroidJNI.ExceptionClear();
                return true;
            }
            return false;
        }

        private static void SafeDeleteLocalRef(ref IntPtr ptr)
        {
            if (ptr != IntPtr.Zero)
            {
                AndroidJNI.DeleteLocalRef(ptr);
                ptr = IntPtr.Zero;
            }
        }

        private static IntPtr SafeGetMethodID(IntPtr cls, string name, string sig)
        {
            if (cls == IntPtr.Zero) return IntPtr.Zero;
            IntPtr mid = AndroidJNI.GetMethodID(cls, name, sig);
            if (ClearPendingJniException()) return IntPtr.Zero;
            return mid;
        }

        private static IntPtr SafeCallObjectMethod(IntPtr obj, IntPtr mid, jvalue[] args)
        {
            if (obj == IntPtr.Zero || mid == IntPtr.Zero) return IntPtr.Zero;
            IntPtr res = AndroidJNI.CallObjectMethod(obj, mid, args ?? EmptyJniArgs);
            if (ClearPendingJniException())
            {
                SafeDeleteLocalRef(ref res);
                return IntPtr.Zero;
            }
            return res;
        }

        private static bool SafeCallBooleanMethod(IntPtr obj, IntPtr mid, jvalue[] args)
        {
            if (obj == IntPtr.Zero || mid == IntPtr.Zero) return false;
            bool res = AndroidJNI.CallBooleanMethod(obj, mid, args ?? EmptyJniArgs);
            if (ClearPendingJniException()) return false;
            return res;
        }

        private static int SafeCallIntMethod(IntPtr obj, IntPtr mid, jvalue[] args, int fallback = -1)
        {
            if (obj == IntPtr.Zero || mid == IntPtr.Zero) return fallback;
            int res = AndroidJNI.CallIntMethod(obj, mid, args ?? EmptyJniArgs);
            if (ClearPendingJniException()) return fallback;
            return res;
        }

        private static string SafeCallStringMethod(IntPtr obj, IntPtr mid, jvalue[] args)
        {
            if (obj == IntPtr.Zero || mid == IntPtr.Zero) return null;
            string res = AndroidJNI.CallStringMethod(obj, mid, args ?? EmptyJniArgs);
            if (ClearPendingJniException()) return null;
            return res;
        }

        private static void WithAndroidConnectivityManager(Action<IntPtr, IntPtr> callback)
        {
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var appContext = activity != null ? activity.Call<AndroidJavaObject>("getApplicationContext") : null)
                using (var connMgr = appContext != null ? appContext.Call<AndroidJavaObject>("getSystemService", "connectivity") : null)
                {
                    if (connMgr == null) return;
                    IntPtr connPtr = connMgr.GetRawObject();
                    if (connPtr == IntPtr.Zero) return;
                    IntPtr connCls = AndroidJNI.GetObjectClass(connPtr);
                    if (ClearPendingJniException() || connCls == IntPtr.Zero)
                    {
                        SafeDeleteLocalRef(ref connCls);
                        return;
                    }
                    try
                    {
                        callback(connPtr, connCls);
                    }
                    finally
                    {
                        SafeDeleteLocalRef(ref connCls);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LanTransport] Android ConnectivityManager error: " + ex.Message);
            }
        }

        private static void ForEachAndroidConnectivityNetwork(IntPtr connPtr, IntPtr connCls,
            Action<IntPtr, int, IPAddress, IPAddress, string> onAddress)
        {
            IntPtr midGetAll = SafeGetMethodID(connCls, "getAllNetworks", "()[Landroid/net/Network;");
            IntPtr midGetActive = SafeGetMethodID(connCls, "getActiveNetwork", "()Landroid/net/Network;");
            IntPtr midGetCaps = SafeGetMethodID(connCls, "getNetworkCapabilities", "(Landroid/net/Network;)Landroid/net/NetworkCapabilities;");
            IntPtr midGetLink = SafeGetMethodID(connCls, "getLinkProperties", "(Landroid/net/Network;)Landroid/net/LinkProperties;");
            if (midGetCaps == IntPtr.Zero || midGetLink == IntPtr.Zero) return;

            var netRefs = new List<IntPtr>(8);
            IntPtr capsCls = IntPtr.Zero;
            IntPtr lpCls = IntPtr.Zero;
            IntPtr listCls = IntPtr.Zero;
            IntPtr laCls = IntPtr.Zero;
            IntPtr inetCls = IntPtr.Zero;
            IntPtr midHasTransport = IntPtr.Zero;
            IntPtr midGetIfName = IntPtr.Zero;
            IntPtr midGetLinkAddrs = IntPtr.Zero;
            IntPtr midListSize = IntPtr.Zero;
            IntPtr midListGet = IntPtr.Zero;
            IntPtr midLaGetAddr = IntPtr.Zero;
            IntPtr midLaGetPrefix = IntPtr.Zero;
            IntPtr midInetGetHost = IntPtr.Zero;

            try
            {
                if (midGetAll != IntPtr.Zero)
                {
                    IntPtr netArrPtr = SafeCallObjectMethod(connPtr, midGetAll, EmptyJniArgs);
                    if (netArrPtr != IntPtr.Zero)
                    {
                        try
                        {
                            int count = AndroidJNI.GetArrayLength(netArrPtr);
                            if (ClearPendingJniException()) count = 0;
                            for (int i = 0; i < count; i++)
                            {
                                IntPtr el = AndroidJNI.GetObjectArrayElement(netArrPtr, i);
                                if (ClearPendingJniException() || el == IntPtr.Zero)
                                {
                                    SafeDeleteLocalRef(ref el);
                                    continue;
                                }
                                netRefs.Add(el);
                            }
                        }
                        finally
                        {
                            SafeDeleteLocalRef(ref netArrPtr);
                        }
                    }
                }
                if (netRefs.Count == 0 && midGetActive != IntPtr.Zero)
                {
                    IntPtr activeNet = SafeCallObjectMethod(connPtr, midGetActive, EmptyJniArgs);
                    if (activeNet != IntPtr.Zero) netRefs.Add(activeNet);
                }

                var netArg = new jvalue[1];
                var idxArg = new jvalue[1];
                for (int i = 0; i < netRefs.Count; i++)
                {
                    IntPtr netPtr = netRefs[i];
                    netArg[0].l = netPtr;
                    int netRank = 20;

                    IntPtr capsPtr = SafeCallObjectMethod(connPtr, midGetCaps, netArg);
                    if (capsPtr != IntPtr.Zero)
                    {
                        try
                        {
                            if (capsCls == IntPtr.Zero)
                            {
                                capsCls = AndroidJNI.GetObjectClass(capsPtr);
                                ClearPendingJniException();
                                midHasTransport = SafeGetMethodID(capsCls, "hasTransport", "(I)Z");
                            }
                            if (midHasTransport != IntPtr.Zero)
                            {
                                var trArg = new jvalue[1];
                                trArg[0].i = 0; // TRANSPORT_CELLULAR
                                bool isCellular = SafeCallBooleanMethod(capsPtr, midHasTransport, trArg);
                                trArg[0].i = 4; // TRANSPORT_VPN
                                bool isVpn = SafeCallBooleanMethod(capsPtr, midHasTransport, trArg);
                                if (isCellular || isVpn) continue;

                                trArg[0].i = 1; // TRANSPORT_WIFI
                                if (SafeCallBooleanMethod(capsPtr, midHasTransport, trArg)) netRank = 0;
                                else
                                {
                                    trArg[0].i = 3; // TRANSPORT_ETHERNET
                                    if (SafeCallBooleanMethod(capsPtr, midHasTransport, trArg)) netRank = 10;
                                }
                            }
                        }
                        finally
                        {
                            SafeDeleteLocalRef(ref capsPtr);
                        }
                    }

                    IntPtr lpPtr = SafeCallObjectMethod(connPtr, midGetLink, netArg);
                    if (lpPtr == IntPtr.Zero) continue;
                    try
                    {
                        if (lpCls == IntPtr.Zero)
                        {
                            lpCls = AndroidJNI.GetObjectClass(lpPtr);
                            ClearPendingJniException();
                            midGetIfName = SafeGetMethodID(lpCls, "getInterfaceName", "()Ljava/lang/String;");
                            midGetLinkAddrs = SafeGetMethodID(lpCls, "getLinkAddresses", "()Ljava/util/List;");
                        }
                        string ifName = midGetIfName != IntPtr.Zero ? (SafeCallStringMethod(lpPtr, midGetIfName, EmptyJniArgs) ?? string.Empty) : string.Empty;
                        if (IsVirtualInterfaceName(ifName)) continue;

                        IntPtr linksPtr = SafeCallObjectMethod(lpPtr, midGetLinkAddrs, EmptyJniArgs);
                        if (linksPtr == IntPtr.Zero) continue;
                        try
                        {
                            if (listCls == IntPtr.Zero)
                            {
                                listCls = AndroidJNI.GetObjectClass(linksPtr);
                                ClearPendingJniException();
                                midListSize = SafeGetMethodID(listCls, "size", "()I");
                                midListGet = SafeGetMethodID(listCls, "get", "(I)Ljava/lang/Object;");
                            }
                            int count = SafeCallIntMethod(linksPtr, midListSize, EmptyJniArgs, 0);
                            for (int j = 0; j < count; j++)
                            {
                                idxArg[0].i = j;
                                IntPtr laPtr = SafeCallObjectMethod(linksPtr, midListGet, idxArg);
                                if (laPtr == IntPtr.Zero) continue;
                                try
                                {
                                    if (laCls == IntPtr.Zero)
                                    {
                                        laCls = AndroidJNI.GetObjectClass(laPtr);
                                        ClearPendingJniException();
                                        midLaGetAddr = SafeGetMethodID(laCls, "getAddress", "()Ljava/net/InetAddress;");
                                        midLaGetPrefix = SafeGetMethodID(laCls, "getPrefixLength", "()I");
                                    }
                                    IntPtr inetPtr = SafeCallObjectMethod(laPtr, midLaGetAddr, EmptyJniArgs);
                                    if (inetPtr == IntPtr.Zero) continue;
                                    try
                                    {
                                        if (inetCls == IntPtr.Zero)
                                        {
                                            inetCls = AndroidJNI.GetObjectClass(inetPtr);
                                            ClearPendingJniException();
                                            midInetGetHost = SafeGetMethodID(inetCls, "getHostAddress", "()Ljava/lang/String;");
                                        }
                                        string hostAddr = SafeCallStringMethod(inetPtr, midInetGetHost, EmptyJniArgs);
                                        if (TryParseValidLanIpv4(hostAddr, out IPAddress parsedIp))
                                        {
                                            int prefix = SafeCallIntMethod(laPtr, midLaGetPrefix, EmptyJniArgs, 24);
                                            onAddress(netPtr, netRank, parsedIp, PrefixLengthToMask(prefix), ifName);
                                        }
                                    }
                                    finally
                                    {
                                        SafeDeleteLocalRef(ref inetPtr);
                                    }
                                }
                                finally
                                {
                                    SafeDeleteLocalRef(ref laPtr);
                                }
                            }
                        }
                        finally
                        {
                            SafeDeleteLocalRef(ref linksPtr);
                        }
                    }
                    finally
                    {
                        SafeDeleteLocalRef(ref lpPtr);
                    }
                }
            }
            finally
            {
                for (int i = 0; i < netRefs.Count; i++)
                {
                    IntPtr r = netRefs[i];
                    SafeDeleteLocalRef(ref r);
                }
                SafeDeleteLocalRef(ref capsCls);
                SafeDeleteLocalRef(ref lpCls);
                SafeDeleteLocalRef(ref listCls);
                SafeDeleteLocalRef(ref laCls);
                SafeDeleteLocalRef(ref inetCls);
            }
        }
#endif

        private void BindAndroidProcessToWifiNetwork(IPAddress targetIp)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (targetIp != null && IPAddress.IsLoopback(targetIp)) return;
            WithAndroidConnectivityManager((connPtr, connCls) =>
            {
                IntPtr midBind = SafeGetMethodID(connCls, "bindProcessToNetwork", "(Landroid/net/Network;)Z");
                if (midBind == IntPtr.Zero) return;

                IntPtr bestNet = IntPtr.Zero;
                int bestScore = int.MaxValue;
                try
                {
                    ForEachAndroidConnectivityNetwork(connPtr, connCls, (netPtr, netRank, ip, mask, ifName) =>
                    {
                        if (netPtr == IntPtr.Zero || netRank > 10) return;
                        bool subnetMatch = targetIp != null && IsSameSubnet(targetIp, ip, mask);
                        int score = (subnetMatch ? 0 : 100) + netRank + PrivateSubnetRank(ip.GetAddressBytes());
                        if (score >= bestScore) return;
                        SafeDeleteLocalRef(ref bestNet);
                        bestNet = AndroidJNI.NewLocalRef(netPtr);
                        bestScore = score;
                    });
                    if (bestNet != IntPtr.Zero)
                    {
                        var arg = new jvalue[1];
                        arg[0].l = bestNet;
                        if (SafeCallBooleanMethod(connPtr, midBind, arg))
                            androidProcessNetworkBound = true;
                    }
                }
                finally
                {
                    SafeDeleteLocalRef(ref bestNet);
                }
            });
#endif
        }

        private void ClearAndroidProcessNetworkBinding()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!androidProcessNetworkBound) return;
            androidProcessNetworkBound = false;
            WithAndroidConnectivityManager((connPtr, connCls) =>
            {
                IntPtr midBind = SafeGetMethodID(connCls, "bindProcessToNetwork", "(Landroid/net/Network;)Z");
                if (midBind == IntPtr.Zero) return;
                var arg = new jvalue[1];
                arg[0].l = IntPtr.Zero;
                SafeCallBooleanMethod(connPtr, midBind, arg);
            });
#endif
        }

        private static void CollectAndroidLanInterfaces(Action<IPAddress, IPAddress, int, bool> addNic)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            WithAndroidConnectivityManager((connPtr, connCls) =>
            {
                ForEachAndroidConnectivityNetwork(connPtr, connCls, (netPtr, netRank, ip, mask, ifName) =>
                {
                    addNic(ip, mask, netRank, false);
                });
            });

            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var appContext = activity != null ? activity.Call<AndroidJavaObject>("getApplicationContext") : null)
                {
                    if (appContext == null) return;

                    try
                    {
                        using (var wifiMgr = appContext.Call<AndroidJavaObject>("getSystemService", "wifi"))
                        using (var wifiInfo = wifiMgr != null ? wifiMgr.Call<AndroidJavaObject>("getConnectionInfo") : null)
                        {
                            int rawIp = wifiInfo != null ? wifiInfo.Call<int>("getIpAddress") : 0;
                            if (rawIp != 0)
                            {
                                var ip = new IPAddress(new[]
                                {
                                    (byte)(rawIp & 0xff),
                                    (byte)((rawIp >> 8) & 0xff),
                                    (byte)((rawIp >> 16) & 0xff),
                                    (byte)((rawIp >> 24) & 0xff)
                                });
                                IPAddress mask = null;
                                using (var dhcp = wifiMgr.Call<AndroidJavaObject>("getDhcpInfo"))
                                {
                                    int rawMask = dhcp != null ? dhcp.Get<int>("netmask") : 0;
                                    if (rawMask != 0)
                                    {
                                        mask = new IPAddress(new[]
                                        {
                                            (byte)(rawMask & 0xff),
                                            (byte)((rawMask >> 8) & 0xff),
                                            (byte)((rawMask >> 16) & 0xff),
                                            (byte)((rawMask >> 24) & 0xff)
                                        });
                                    }
                                }
                                addNic(ip, mask, 0, false);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[LanTransport] Android WifiManager fallback warning: " + ex.Message);
                    }

                    try
                    {
                        using (var netIfCls = new AndroidJavaClass("java.net.NetworkInterface"))
                        using (var enumIfs = netIfCls.CallStatic<AndroidJavaObject>("getNetworkInterfaces"))
                        {
                            while (enumIfs != null && enumIfs.Call<bool>("hasMoreElements"))
                            {
                                using (var jNic = enumIfs.Call<AndroidJavaObject>("nextElement"))
                                {
                                    if (jNic == null) continue;
                                    try
                                    {
                                        if (!jNic.Call<bool>("isUp") || jNic.Call<bool>("isLoopback")) continue;
                                        string ifName = jNic.Call<string>("getName") ?? string.Empty;
                                        if (IsVirtualInterfaceName(ifName)) continue;
                                        int ifRank = ifName.StartsWith("wlan", StringComparison.OrdinalIgnoreCase) ||
                                                     ifName.StartsWith("ap", StringComparison.OrdinalIgnoreCase) ||
                                                     ifName.StartsWith("swlan", StringComparison.OrdinalIgnoreCase)
                                            ? 5 : 12;
                                        using (var ifAddrs = jNic.Call<AndroidJavaObject>("getInterfaceAddresses"))
                                        {
                                            if (ifAddrs == null) continue;
                                            int addrCount = ifAddrs.Call<int>("size");
                                            for (int k = 0; k < addrCount; k++)
                                            {
                                                using (var ia = ifAddrs.Call<AndroidJavaObject>("get", k))
                                                using (var inet = ia != null ? ia.Call<AndroidJavaObject>("getAddress") : null)
                                                {
                                                    string hostAddr = inet != null ? inet.Call<string>("getHostAddress") : null;
                                                    if (TryParseValidLanIpv4(hostAddr, out IPAddress parsedIp))
                                                    {
                                                        short prefix = ia.Call<short>("getNetworkPrefixLength");
                                                        addNic(parsedIp, PrefixLengthToMask(prefix), ifRank, false);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    catch (Exception) { }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[LanTransport] Android NetworkInterface enumeration warning: " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LanTransport] Android context error: " + ex.Message);
            }
#endif
        }

        private void AcquireAndroidMulticastLock()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            ReleaseAndroidMulticastLock();
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var appContext = activity != null ? activity.Call<AndroidJavaObject>("getApplicationContext") : null)
                using (var wifiMgr = appContext != null ? appContext.Call<AndroidJavaObject>("getSystemService", "wifi") : null)
                {
                    if (wifiMgr == null) return;
                    var lockObj = wifiMgr.Call<AndroidJavaObject>("createMulticastLock", "DynamonsLanLock");
                    if (lockObj == null) return;
                    lockObj.Call("setReferenceCounted", false);
                    lockObj.Call("acquire");
                    androidMulticastLock = lockObj;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LanTransport] Failed to acquire Android MulticastLock: " + ex.Message);
                ReleaseAndroidMulticastLock();
            }
#endif
        }

        private void ReleaseAndroidMulticastLock()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (androidMulticastLock == null) return;
            try
            {
                if (androidMulticastLock.Call<bool>("isHeld"))
                    androidMulticastLock.Call("release");
            }
            catch (Exception) { }
            try { androidMulticastLock.Dispose(); } catch (Exception) { }
            androidMulticastLock = null;
#endif
        }

        private static string[] SanitizeBeaconHostIps(string[] raw)
        {
            if (raw == null || raw.Length == 0) return Array.Empty<string>();
            var list = new List<string>(Math.Min(4, raw.Length));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < raw.Length && list.Count < 4; i++)
            {
                string cand = raw[i] != null ? raw[i].Trim() : string.Empty;
                if (string.IsNullOrEmpty(cand) || seen.Contains(cand)) continue;
                if (!TryParseValidLanIpv4(cand, out _)) continue;
                seen.Add(cand);
                list.Add(cand);
            }
            return list.ToArray();
        }

        private static string[] ExtractEndpointIps(NicEndpoint[] nics, int limit)
        {
            if (nics == null || nics.Length == 0 || limit <= 0) return Array.Empty<string>();
            var list = new List<string>(Math.Min(limit, nics.Length));
            for (int i = 0; i < nics.Length && list.Count < limit; i++)
            {
                if (nics[i].IsVirtual || string.IsNullOrEmpty(nics[i].IpText)) continue;
                list.Add(nics[i].IpText);
            }
            if (list.Count == 0)
            {
                for (int i = 0; i < nics.Length && list.Count < limit; i++)
                {
                    if (!string.IsNullOrEmpty(nics[i].IpText)) list.Add(nics[i].IpText);
                }
            }
            return list.ToArray();
        }

        private static void BuildSubnetArrays(NicEndpoint[] nics, out string[] ips, out string[] masks)
        {
            if (nics == null || nics.Length == 0)
            {
                ips = Array.Empty<string>();
                masks = Array.Empty<string>();
                return;
            }
            bool hasPhysical = false;
            for (int i = 0; i < nics.Length; i++)
            {
                if (!nics[i].IsVirtual && !string.IsNullOrEmpty(nics[i].IpText)) { hasPhysical = true; break; }
            }
            var ipList = new List<string>(nics.Length);
            var maskList = new List<string>(nics.Length);
            for (int i = 0; i < nics.Length; i++)
            {
                if (hasPhysical && nics[i].IsVirtual) continue;
                if (string.IsNullOrEmpty(nics[i].IpText)) continue;
                ipList.Add(nics[i].IpText);
                maskList.Add(nics[i].Mask != null ? nics[i].Mask.ToString() : "255.255.255.0");
            }
            ips = ipList.ToArray();
            masks = maskList.ToArray();
        }

        private static bool TryParseValidLanIpv4(string text, out IPAddress addr)
        {
            addr = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (!IPAddress.TryParse(text.Trim(), out IPAddress parsed)) return false;
            if (parsed.AddressFamily != AddressFamily.InterNetwork) return false;
            if (IPAddress.IsLoopback(parsed)) return false;
            byte[] b = parsed.GetAddressBytes();
            if (b == null || b.Length != 4 || b[0] == 0) return false;
            if (b[0] == 169 && b[1] == 254) return false;
            addr = parsed;
            return true;
        }

        public static bool IsSameSubnet(IPAddress a, IPAddress b, IPAddress mask)
        {
            if (a == null || b == null ||
                a.AddressFamily != AddressFamily.InterNetwork ||
                b.AddressFamily != AddressFamily.InterNetwork)
                return false;
            byte[] ab = a.GetAddressBytes();
            byte[] bb = b.GetAddressBytes();
            if (ab == null || ab.Length != 4 || bb == null || bb.Length != 4) return false;
            byte[] mb = mask != null && mask.AddressFamily == AddressFamily.InterNetwork ? mask.GetAddressBytes() : null;
            if (mb == null || mb.Length != 4 || IsAllSameByte(mb, 0) || IsAllSameByte(mb, 255))
                mb = new byte[] { 255, 255, 255, 0 };
            for (int i = 0; i < 4; i++)
            {
                if ((ab[i] & mb[i]) != (bb[i] & mb[i])) return false;
            }
            return true;
        }

        private static bool MatchesAnyLocalSubnet(string ipText, string[] localIps, string[] localMasks)
        {
            if (!TryParseValidLanIpv4(ipText, out IPAddress target) || localIps == null) return false;
            for (int i = 0; i < localIps.Length; i++)
            {
                if (!TryParseValidLanIpv4(localIps[i], out IPAddress localIp)) continue;
                IPAddress mask = null;
                if (localMasks != null && i < localMasks.Length && !string.IsNullOrEmpty(localMasks[i]))
                    IPAddress.TryParse(localMasks[i], out mask);
                if (IsSameSubnet(target, localIp, mask)) return true;
            }
            return false;
        }

        public static string[] RankCandidateHostIps(string remoteIp, string[] advertisedIps, string[] localIps, string[] localMasks)
        {
            var entries = new List<(string ip, int score, int order)>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            string trimmedRemote = remoteIp != null ? remoteIp.Trim() : string.Empty;

            void AddCandidate(string raw)
            {
                if (string.IsNullOrWhiteSpace(raw)) return;
                string trimmed = raw.Trim();
                if (seen.Contains(trimmed) || !TryParseValidLanIpv4(trimmed, out IPAddress parsed)) return;
                seen.Add(trimmed);
                bool sameSub = MatchesAnyLocalSubnet(trimmed, localIps, localMasks);
                int subScore = sameSub ? 0 : (100 + PrivateSubnetRank(parsed.GetAddressBytes()));
                int remoteTie = string.Equals(trimmed, trimmedRemote, StringComparison.Ordinal) ? 0 : 1;
                entries.Add((trimmed, subScore * 2 + remoteTie, entries.Count));
            }

            AddCandidate(trimmedRemote);
            if (advertisedIps != null)
            {
                for (int i = 0; i < advertisedIps.Length; i++) AddCandidate(advertisedIps[i]);
            }

            entries.Sort((a, b) =>
            {
                int cmp = a.score.CompareTo(b.score);
                return cmp != 0 ? cmp : a.order.CompareTo(b.order);
            });

            var result = new string[entries.Count];
            for (int i = 0; i < entries.Count; i++) result[i] = entries[i].ip;
            return result;
        }

        public static string FormatSocketConnectError(string ip, int port, SocketError socketError, int nativeErrorCode, string rawMessage)
        {
            string endpoint = string.IsNullOrEmpty(ip) ? $"PORT {port}" : $"{ip}:{port}";
            string lower = rawMessage != null ? rawMessage.ToLowerInvariant() : string.Empty;

            if (socketError == SocketError.HostUnreachable || nativeErrorCode == 113 || nativeErrorCode == 10065 ||
                lower.Contains("(113)") || lower.Contains("no route"))
            {
                return $"No route to {endpoint} (113) - allow port {port} in firewall / check same Wi-Fi and VPN";
            }
            if (socketError == SocketError.NetworkUnreachable || nativeErrorCode == 101 || nativeErrorCode == 10051 ||
                lower.Contains("(101)") || lower.Contains("network is unreachable"))
            {
                return $"Network unreachable for {endpoint} (101) - check Wi-Fi or turn off cellular / VPN";
            }
            if (socketError == SocketError.ConnectionRefused || nativeErrorCode == 111 || nativeErrorCode == 10061 ||
                lower.Contains("(111)") || lower.Contains("refused"))
            {
                return $"Connection refused by {endpoint} - confirm Host room is open on port {port}";
            }
            if (socketError == SocketError.TimedOut || nativeErrorCode == 110 || nativeErrorCode == 10060 ||
                lower.Contains("timed out"))
            {
                return $"Timed out connecting to {endpoint} - allow port {port} or host on Android";
            }
            return !string.IsNullOrEmpty(rawMessage) ? $"{endpoint}: {rawMessage}" : $"Connection to {endpoint} failed";
        }

        public static IPAddress PrefixLengthToMask(int prefixLength)
        {
            if (prefixLength <= 0) return null;
            if (prefixLength >= 32) return new IPAddress(new byte[] { 255, 255, 255, 255 });
            uint mask = 0xFFFFFFFFu << (32 - prefixLength);
            return new IPAddress(new[]
            {
                (byte)(mask >> 24),
                (byte)(mask >> 16),
                (byte)(mask >> 8),
                (byte)mask
            });
        }

        public static IPAddress ComputeBroadcastIp(IPAddress ip, IPAddress mask)
        {
            if (ip == null || ip.AddressFamily != AddressFamily.InterNetwork) return null;
            byte[] ipBytes = ip.GetAddressBytes();
            if (ipBytes == null || ipBytes.Length != 4) return null;

            if (mask != null && mask.AddressFamily == AddressFamily.InterNetwork)
            {
                byte[] maskBytes = mask.GetAddressBytes();
                if (maskBytes != null && maskBytes.Length == 4 &&
                    !IsAllSameByte(maskBytes, 0) && !IsAllSameByte(maskBytes, 255))
                {
                    return new IPAddress(new[]
                    {
                        (byte)(ipBytes[0] | (byte)~maskBytes[0]),
                        (byte)(ipBytes[1] | (byte)~maskBytes[1]),
                        (byte)(ipBytes[2] | (byte)~maskBytes[2]),
                        (byte)(ipBytes[3] | (byte)~maskBytes[3])
                    });
                }
            }

            return new IPAddress(new[] { ipBytes[0], ipBytes[1], ipBytes[2], (byte)255 });
        }

        public static bool IsVirtualInterfaceName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string lower = name.ToLowerInvariant();
            string[] prefixes =
            {
                "docker", "br-", "veth", "virbr", "vmnet", "vboxnet",
                "waydroid", "anbox", "lxc", "lxd", "incus", "mpqemu", "libvirt",
                "tun", "tap", "utun", "wg", "zt", "podman", "cni",
                "flannel", "cali", "cilium", "kube", "dummy",
                "rmnet", "r_rmnet", "v4-", "ccmni", "pdp", "clat", "p2p",
                "nordlynx", "mullvad", "proton", "warp"
            };
            for (int i = 0; i < prefixes.Length; i++)
            {
                if (lower.StartsWith(prefixes[i], StringComparison.Ordinal)) return true;
            }
            return lower.Contains("tailscale") ||
                   lower.Contains("zerotier") ||
                   lower.Contains("cloudflare") ||
                   lower.Contains("wireguard");
        }

        private static void EnumerateActiveIpv4(Action<IPAddress, IPAddress, bool> onUnicast)
        {
            NetworkInterface[] nics;
            try
            {
                nics = NetworkInterface.GetAllNetworkInterfaces();
            }
            catch (Exception)
            {
                return;
            }
            if (nics == null) return;

            for (int i = 0; i < nics.Length; i++)
            {
                NetworkInterface nic = nics[i];
                if (nic == null) continue;
                try
                {
                    OperationalStatus status = nic.OperationalStatus;
                    if (status == OperationalStatus.Down || status == OperationalStatus.NotPresent ||
                        status == OperationalStatus.LowerLayerDown || status == OperationalStatus.Dormant)
                        continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    bool isVirtual = IsVirtualInterfaceName(nic.Name) || IsVirtualInterfaceName(nic.Description);
                    var props = nic.GetIPProperties();
                    if (props == null || props.UnicastAddresses == null) continue;

                    foreach (var ua in props.UnicastAddresses)
                    {
                        if (ua == null || ua.Address == null) continue;
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        if (IPAddress.IsLoopback(ua.Address)) continue;
                        byte[] ipBytes = ua.Address.GetAddressBytes();
                        if (ipBytes == null || ipBytes.Length != 4 || ipBytes[0] == 0) continue;
                        if (ipBytes[0] == 169 && ipBytes[1] == 254) continue;

                        IPAddress mask = null;
                        try { mask = ua.IPv4Mask; } catch (Exception) { mask = null; }
                        onUnicast(ua.Address, mask, isVirtual);
                    }
                }
                catch (Exception)
                {
                    // Android 11+ can throw NetworkInformationException on restricted/virtual interfaces.
                }
            }
        }

        private static int PrivateSubnetRank(byte[] ipBytes)
        {
            if (ipBytes == null || ipBytes.Length != 4) return 3;
            if (ipBytes[0] == 192 && ipBytes[1] == 168) return 0;
            if (ipBytes[0] == 10) return 1;
            if (ipBytes[0] == 172 && ipBytes[1] >= 16 && ipBytes[1] <= 31) return 2;
            return 3;
        }

        private static bool IsAllSameByte(byte[] bytes, byte val)
        {
            for (int i = 0; i < bytes.Length; i++)
                if (bytes[i] != val) return false;
            return true;
        }

        private static Thread StartThread(string name, ThreadStart entry)
        {
            var thread = new Thread(entry)
            {
                IsBackground = true,
                Name = name
            };
            thread.Start();
            return thread;
        }

        private static void CloseQuietly(TcpListener listener)
        {
            if (listener == null) return;
            try { listener.Stop(); } catch (Exception) { }
        }

        private static void CloseQuietly(TcpClient client)
        {
            if (client == null) return;
            try
            {
                Socket sock = client.Client;
                if (sock != null && sock.Connected) sock.Shutdown(SocketShutdown.Both);
            }
            catch (Exception) { }
            try { client.Close(); } catch (Exception) { }
        }

        private static void CloseQuietly(IDisposable resource)
        {
            if (resource == null) return;
            try { resource.Dispose(); } catch (Exception) { }
        }
    }
}
