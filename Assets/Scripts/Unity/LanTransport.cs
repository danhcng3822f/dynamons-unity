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
        private TcpListener tcpListener;
        private TcpClient peerClient;
        private NetworkStream peerStream;
        private UdpClient udpHostClient;
        private UdpClient udpDiscoveryClient;

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
                if (udp != null)
                    udpBeaconThread = StartThread("LanUdpBeacon", () => UdpBeaconLoop(gen, udp));
            }
        }

        /// <summary>Updates the serialized UDP beacon bytes on the calling main thread.</summary>
        public void UpdateBeacon(LanBeaconDto beacon)
        {
            if (disposed || !isHost || beacon == null) return;
            var normalized = new LanBeaconDto
            {
                protocolVersion = beacon.protocolVersion > 0 ? beacon.protocolVersion : LanConst.ProtocolVersion,
                roomId = beacon.roomId ?? string.Empty,
                hostName = string.IsNullOrEmpty(beacon.hostName) ? "HOST" : beacon.hostName,
                ruleMode = beacon.ruleMode == "real" ? "real" : "fair20",
                itemsAllowed = beacon.itemsAllowed,
                tcpPort = boundPort > 0 ? boundPort : beacon.tcpPort,
                teamCount = beacon.teamCount
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

            UdpClient udp = null;
            try
            {
                udp = new UdpClient(AddressFamily.InterNetwork);
                try { udp.ExclusiveAddressUse = false; } catch (Exception) { }
                udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udp.EnableBroadcast = true;
                udp.Client.Bind(new IPEndPoint(IPAddress.Any, LanConst.DiscoveryPort));
            }
            catch (Exception)
            {
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

        /// <summary>Decodes buffered discovery beacons on the main thread, prunes stale rooms (&gt;3.0s), and returns active rooms.</summary>
        public List<LanDiscoveredRoom> GetRooms()
        {
            var result = new List<LanDiscoveredRoom>();
            if (disposed) return result;

            int activeGen = Volatile.Read(ref sessionGen);
            DateTime nowUtc = DateTime.UtcNow;

            while (rawBeaconQueue.TryDequeue(out var pkt))
            {
                if (pkt.Gen != activeGen) continue;
                var dto = LanCodec.Decode<LanBeaconDto>(pkt.RawJson);
                if (dto == null || dto.protocolVersion != LanConst.ProtocolVersion) continue;
                if (dto.tcpPort < 1 || dto.tcpPort > 65535 || string.IsNullOrEmpty(pkt.RemoteIp)) continue;

                string key = pkt.RemoteIp + ":" + dto.tcpPort;
                if (discoveredRooms.TryGetValue(key, out var existing) && existing.LastSeenUtc > pkt.SeenUtc)
                    continue;

                discoveredRooms[key] = new LanDiscoveredRoom
                {
                    Key = key,
                    HostIp = pkt.RemoteIp,
                    TcpPort = dto.tcpPort,
                    HostName = string.IsNullOrEmpty(dto.hostName) ? "HOST" : dto.hostName,
                    RuleMode = dto.ruleMode == "real" ? "real" : "fair20",
                    ItemsAllowed = dto.itemsAllowed,
                    TeamCount = dto.teamCount,
                    LastSeenUtc = pkt.SeenUtc
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

            result.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            return result;
        }

        /// <summary>Asynchronously connects to a host over TCP with a 5.0s timeout.</summary>
        public void ConnectDirect(string ip, int port)
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

            lock (stateLock)
            {
                if (disposed || gen != sessionGen) return;
                tcpConnectThread = StartThread("LanTcpConnect", () => TcpConnectLoop(gen, trimmedIp, port));
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

        /// <summary>Returns active non-loopback, non-link-local IPv4 addresses sorted with private LAN subnets first.</summary>
        public static string[] LocalIpv4Addresses()
        {
            var found = new List<(string ip, int rank)>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            EnumerateActiveIpv4((addr, _) =>
            {
                string ip = addr.ToString();
                if (!seen.Add(ip)) return;
                found.Add((ip, PrivateSubnetRank(addr.GetAddressBytes())));
            });

            found.Sort((a, b) =>
            {
                int cmp = a.rank.CompareTo(b.rank);
                return cmp != 0 ? cmp : StringComparer.Ordinal.Compare(a.ip, b.ip);
            });

            var result = new string[found.Count];
            for (int i = 0; i < found.Count; i++)
                result[i] = found[i].ip;
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

        private void UdpBeaconLoop(int gen, UdpClient udp)
        {
            try
            {
                while (!IsSessionCancelled(gen))
                {
                    if (!isConnected)
                    {
                        byte[] bytes = Volatile.Read(ref beaconBytes);
                        if (bytes != null && bytes.Length > 0)
                        {
                            List<IPEndPoint> targets = GetBroadcastEndPoints();
                            bool anySent = false;
                            for (int i = 0; i < targets.Count; i++)
                            {
                                if (IsSessionCancelled(gen)) return;
                                try
                                {
                                    udp.Send(bytes, bytes.Length, targets[i]);
                                    anySent = true;
                                }
                                catch (SocketException)
                                {
                                    // Per-interface broadcast failures must not stop remaining targets.
                                }
                                catch (ObjectDisposedException)
                                {
                                    return;
                                }
                            }
                            if (!anySent && !IsSessionCancelled(gen))
                            {
                                try
                                {
                                    udp.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Loopback, LanConst.DiscoveryPort));
                                }
                                catch (Exception) { }
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

        private void TcpConnectLoop(int gen, string ip, int port)
        {
            TcpClient client = null;
            try
            {
                IPAddress parsedIp;
                bool isLiteral = IPAddress.TryParse(ip, out parsedIp);
                client = new TcpClient(isLiteral ? parsedIp.AddressFamily : AddressFamily.InterNetwork);

                IAsyncResult ar = isLiteral
                    ? client.BeginConnect(parsedIp, port, null, null)
                    : client.BeginConnect(ip, port, null, null);

                var stopwatch = Stopwatch.StartNew();
                while (!ar.IsCompleted)
                {
                    if (IsSessionCancelled(gen))
                    {
                        CloseQuietly(client);
                        return;
                    }
                    if (stopwatch.ElapsedMilliseconds >= ConnectTimeoutMs)
                    {
                        CloseQuietly(client);
                        EmitDisconnect(gen, "Connection timed out");
                        return;
                    }
                    ar.AsyncWaitHandle.WaitOne(50);
                }

                client.EndConnect(ar);
                if (IsSessionCancelled(gen))
                {
                    CloseQuietly(client);
                    return;
                }

                client.NoDelay = true;
                NetworkStream stream = client.GetStream();
                lock (stateLock)
                {
                    if (IsSessionCancelled(gen))
                    {
                        CloseQuietly(stream);
                        CloseQuietly(client);
                        return;
                    }

                    peerClient = client;
                    peerStream = stream;
                    Interlocked.Exchange(ref lastRecvUtcTicks, DateTime.UtcNow.Ticks);
                    Thread.MemoryBarrier();
                    isConnected = true;
                    incomingQueue.Enqueue(new RawNetEvent { Gen = gen, Kind = RawKindConnected });
                    tcpReaderThread = StartThread("LanTcpReader", () => TcpReaderLoop(gen, client, stream));
                    tcpWriterThread = StartThread("LanTcpWriter", () => TcpWriterLoop(gen, stream));
                }
            }
            catch (Exception ex)
            {
                CloseQuietly(client);
                EmitDisconnect(gen, string.IsNullOrEmpty(ex.Message) ? "Connection failed" : ex.Message);
            }
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

        private static List<IPEndPoint> GetBroadcastEndPoints()
        {
            var targets = new List<IPEndPoint> { new IPEndPoint(IPAddress.Broadcast, LanConst.DiscoveryPort) };
            var seen = new HashSet<string>(StringComparer.Ordinal) { IPAddress.Broadcast.ToString() };

            EnumerateActiveIpv4((addr, mask) =>
            {
                if (mask == null || mask.AddressFamily != AddressFamily.InterNetwork) return;
                byte[] ipBytes = addr.GetAddressBytes();
                byte[] maskBytes = mask.GetAddressBytes();
                if (ipBytes == null || maskBytes == null || ipBytes.Length != 4 || maskBytes.Length != 4) return;
                if (IsAllSameByte(maskBytes, 0) || IsAllSameByte(maskBytes, 255)) return;

                byte[] bcastBytes = new byte[4];
                for (int i = 0; i < 4; i++)
                    bcastBytes[i] = (byte)(ipBytes[i] | (byte)~maskBytes[i]);

                var bcastAddr = new IPAddress(bcastBytes);
                if (seen.Add(bcastAddr.ToString()))
                    targets.Add(new IPEndPoint(bcastAddr, LanConst.DiscoveryPort));
            });

            return targets;
        }

        private static void EnumerateActiveIpv4(Action<IPAddress, IPAddress> onUnicast)
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
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    var props = nic.GetIPProperties();
                    if (props == null || props.UnicastAddresses == null) continue;

                    foreach (var ua in props.UnicastAddresses)
                    {
                        if (ua == null || ua.Address == null) continue;
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        if (IPAddress.IsLoopback(ua.Address)) continue;
                        byte[] ipBytes = ua.Address.GetAddressBytes();
                        if (ipBytes == null || ipBytes.Length != 4) continue;
                        if (ipBytes[0] == 169 && ipBytes[1] == 254) continue;

                        IPAddress mask = null;
                        try { mask = ua.IPv4Mask; } catch (Exception) { mask = null; }
                        onUnicast(ua.Address, mask);
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
