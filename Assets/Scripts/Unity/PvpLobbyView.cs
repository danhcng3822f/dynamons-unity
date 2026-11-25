using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Dynamons
{
    public sealed partial class GameRoot
    {
        private const float RoomListPollIntervalSec = 0.25f;

        private readonly System.Random lanRng = new System.Random();
        private LanTransport lanTransport;
        private PvpBattleRules pvpRules;
        private LanStartMatchDto pvpStartMatch;
        private bool inPvpBattle, pvpIsHost;

        private bool lobbyOpen, lobbyHostMode = true, lobbyInReadyRoom, lobbyTcpConnected, sentLocalHello;
        private bool lobbyItemsAllowed, lobbyConnecting, lobbyStatusIsError, lobbyStartingMatch;
        private string lobbyRuleMode = "fair20";
        private string lobbyRoomId = string.Empty;
        private string lastEnteredIp = string.Empty;
        private string lobbyStatusMessage = string.Empty;
        private string lastRoomsSignature = string.Empty;
        private float roomListPollTimer;
        private LanHelloDto remoteHello;

        private RectTransform lobbyTabsLayer, lobbyDynamicLayer, lobbyManualJoinLayer, lobbyConnectStatusLayer;
        private InputField lobbyIpInput;
        private CanvasGroup lobbyWaitPulseGroup;

        private LanTransport EnsureLanTransport() => lanTransport ??= new LanTransport();

        private void ResetLanLobbyRefs()
        {
            lobbyTabsLayer = null;
            lobbyDynamicLayer = null;
            lobbyManualJoinLayer = null;
            lobbyConnectStatusLayer = null;
            lobbyIpInput = null;
            lobbyWaitPulseGroup = null;
        }

        private void StopLanSession()
        {
            inPvpBattle = false;
            pvpIsHost = false;
            pvpRules = null;
            pvpStartMatch = null;
            lobbyOpen = false;
            lobbyInReadyRoom = false;
            lobbyTcpConnected = false;
            sentLocalHello = false;
            lobbyConnecting = false;
            lobbyStartingMatch = false;
            remoteHello = null;
            lobbyStatusMessage = string.Empty;
            lobbyStatusIsError = false;
            lastRoomsSignature = string.Empty;
            ResetLanLobbyRefs();
            lanTransport?.Stop();
        }

        /// <summary>Opens the LAN PvP Host/Join lobby overlay from the LAN Island duel gate.</summary>
        internal void OpenLanLobby()
        {
            if (save == null || save.Team == null || save.Team.Count == 0)
            {
                Note("PICK A DYNAMON FIRST.");
                return;
            }

            inPvpBattle = false;
            pvpIsHost = false;
            pvpRules = null;
            pvpStartMatch = null;
            EnsureLanTransport().Stop();

            lobbyHostMode = true;
            lobbyRuleMode = "fair20";
            lobbyItemsAllowed = false;
            lobbyInReadyRoom = false;
            lobbyTcpConnected = false;
            sentLocalHello = false;
            lobbyConnecting = false;
            lobbyStartingMatch = false;
            lobbyStatusIsError = false;
            lobbyStatusMessage = string.Empty;
            lastRoomsSignature = string.Empty;
            remoteHello = null;
            lobbyRoomId = Guid.NewGuid().ToString("N").Substring(0, 8);

            ui.Sound("screen_open", save);
            OpenOverlayShell(0.75f);

            var cardVec = VectorCanvas.Create("LanLobbyCard", overlay, 0f, 0f, GameUi.Width, GameUi.Height);
            cardVec.FillRoundRect(10f, 24f, 300f, 520f, 14f, GameUi.HexColor("#e8b35a"));
            cardVec.FillRoundRect(13f, 27f, 294f, 514f, 12f, GameUi.HexColor("#231a38"));
            cardVec.FillRoundRect(22f, 33f, 276f, 30f, 8f, GameUi.HexColor("#2e214c"));
            cardVec.Commit();

            fonts.Draw(overlay, "white_stroked_big", "LAN DUEL LOBBY", 160f, 40f, 15f, "center", null, 252f);

            lobbyTabsLayer = GameUi.Box("LobbyTabs", overlay, 0f, 0f, GameUi.Width, GameUi.Height);
            lobbyDynamicLayer = GameUi.Box("LobbyDynamic", overlay, 0f, 0f, GameUi.Width, GameUi.Height);
            lobbyManualJoinLayer = GameUi.Box("LobbyManualJoin", overlay, 0f, 0f, GameUi.Width, GameUi.Height);
            lobbyConnectStatusLayer = GameUi.Box("LobbyConnectStatus", overlay, 0f, 0f, GameUi.Width, GameUi.Height);

            ui.ArtButton(overlay, "general/btn_close", 280f, 12f, 36f, 36f, () =>
            {
                ui.Sound("screen_close", save);
                CloseLanLobby();
            }, true, 1f, "LanLobbyCloseBtn");

            lobbyOpen = true;
            EnterHostMode();
        }

        /// <summary>Closes the LAN lobby overlay and stops active discovery or listener sockets.</summary>
        internal void CloseLanLobby(bool closeOverlay = true)
        {
            lobbyOpen = false;
            lobbyInReadyRoom = false;
            lobbyTcpConnected = false;
            sentLocalHello = false;
            lobbyConnecting = false;
            lobbyStartingMatch = false;
            remoteHello = null;
            lobbyStatusMessage = string.Empty;
            lobbyStatusIsError = false;
            lastRoomsSignature = string.Empty;
            ResetLanLobbyRefs();
            lanTransport?.Stop();
            if (closeOverlay) CloseOverlay();
        }

        private void CloseLanLobbyOverlayOnly()
        {
            lobbyOpen = false;
            lobbyInReadyRoom = false;
            lobbyTcpConnected = false;
            sentLocalHello = false;
            lobbyConnecting = false;
            lobbyStartingMatch = false;
            remoteHello = null;
            lobbyStatusMessage = string.Empty;
            lobbyStatusIsError = false;
            lastRoomsSignature = string.Empty;
            ResetLanLobbyRefs();
            CloseOverlay();
        }

        /// <summary>Drains LAN socket events on the Unity main thread and updates lobby discovery/animations.</summary>
        internal void UpdateLanNetworking()
        {
            if (lobbyOpen && overlay == null)
            {
                CloseLanLobby(false);
                return;
            }
            if (lanTransport == null) return;

            var messages = lanTransport.DrainMainThreadMessages(Time.unscaledTime);
            for (int i = 0; i < messages.Count; i++)
            {
                var msg = messages[i];
                if (msg == null || string.IsNullOrEmpty(msg.Type)) continue;
                if (inPvpBattle)
                {
                    OnPvpNetMessage(msg);
                    continue;
                }
                if (lobbyOpen) HandleLobbyNetMessage(msg);
            }

            if (!lobbyOpen || lobbyInReadyRoom) return;

            if (lobbyHostMode)
            {
                if (lobbyWaitPulseGroup != null)
                    lobbyWaitPulseGroup.alpha = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 4.5f);
            }
            else
            {
                roomListPollTimer += Time.unscaledDeltaTime;
                if (roomListPollTimer >= RoomListPollIntervalSec)
                {
                    roomListPollTimer = 0f;
                    RefreshDiscoveredRoomsIfChanged();
                }
            }
        }

        /// <summary>Disposes all LAN socket threads and buffers cleanly on quit or teardown.</summary>
        internal void ShutdownLanNetworking()
        {
            inPvpBattle = false;
            pvpIsHost = false;
            pvpRules = null;
            pvpStartMatch = null;
            lobbyOpen = false;
            lobbyInReadyRoom = false;
            lobbyTcpConnected = false;
            sentLocalHello = false;
            lobbyConnecting = false;
            lobbyStartingMatch = false;
            remoteHello = null;
            ResetLanLobbyRefs();
            if (lanTransport != null)
            {
                lanTransport.Dispose();
                lanTransport = null;
            }
        }

        // TODO(Task 7): Replace forward compilation hook in BattleView.cs.
        internal void BeginPvpBattle(LanStartMatchDto startMatch, bool isHost)
        {
            pvpStartMatch = startMatch;
            pvpIsHost = isHost;
            inPvpBattle = true;
            CloseLanLobbyOverlayOnly();
            Debug.Log($"[LAN] BeginPvpBattle ({(isHost ? "host" : "client")}) rule={startMatch?.ruleMode} items={startMatch?.itemsAllowed}");
            Note("DUEL READY! BATTLE VIEW WIRES IN NEXT TASK.");
        }

        // TODO(Task 7): Replace forward compilation hook in BattleView.cs.
        internal void OnPvpNetMessage(LanNetMessage msg)
        {
            if (msg == null) return;
            Debug.Log($"[LAN] OnPvpNetMessage type={msg.Type} reason={msg.Reason}");
            if (msg.Type == "disconnected")
            {
                string reason = SanitizeStatusText(msg.Reason, "OPPONENT DISCONNECTED");
                ReturnToLanIsland();
                Note(reason);
            }
        }

        private void EnterHostMode(bool preserveStatus = false)
        {
            lobbyHostMode = true;
            lobbyInReadyRoom = false;
            lobbyTcpConnected = false;
            sentLocalHello = false;
            lobbyConnecting = false;
            lobbyStartingMatch = false;
            remoteHello = null;
            if (!preserveStatus)
            {
                lobbyStatusMessage = string.Empty;
                lobbyStatusIsError = false;
            }
            lobbyRoomId = Guid.NewGuid().ToString("N").Substring(0, 8);
            EnsureLanTransport().StartHost(BuildHostBeacon());
            if (!lanTransport.IsHost && string.IsNullOrEmpty(lobbyStatusMessage))
            {
                lobbyStatusIsError = true;
                lobbyStatusMessage = "NO FREE BATTLE PORT (41976-41985)";
            }
            RebuildLobbyChrome();
        }

        private void EnterJoinMode(bool preserveStatus = false)
        {
            lobbyHostMode = false;
            lobbyInReadyRoom = false;
            lobbyTcpConnected = false;
            sentLocalHello = false;
            lobbyConnecting = false;
            lobbyStartingMatch = false;
            remoteHello = null;
            lastRoomsSignature = string.Empty;
            roomListPollTimer = 0f;
            if (!preserveStatus)
            {
                lobbyStatusMessage = string.Empty;
                lobbyStatusIsError = false;
            }
            EnsureLanTransport().StartDiscovery();
            RebuildLobbyChrome();
        }

        private void RebuildLobbyChrome()
        {
            if (!lobbyOpen || overlay == null) return;
            if (lobbyTabsLayer != null) GameUi.Clear(lobbyTabsLayer);
            if (lobbyManualJoinLayer != null) GameUi.Clear(lobbyManualJoinLayer);
            if (lobbyConnectStatusLayer != null) GameUi.Clear(lobbyConnectStatusLayer);
            if (lobbyDynamicLayer != null) GameUi.Clear(lobbyDynamicLayer);
            lobbyIpInput = null;
            lobbyWaitPulseGroup = null;

            if (lobbyInReadyRoom)
            {
                RenderReadyRoom();
                return;
            }

            DrawModeTabs();
            if (lobbyHostMode)
            {
                RenderHostLobby();
            }
            else
            {
                BuildManualJoinSection();
                RenderConnectStatus();
                var rooms = lanTransport != null ? lanTransport.GetRooms() : new List<LanDiscoveredRoom>();
                lastRoomsSignature = BuildRoomsSignature(rooms);
                RenderDiscoveredRooms(rooms);
            }
        }

        private void DrawModeTabs()
        {
            if (lobbyTabsLayer == null) return;
            var vec = VectorCanvas.Create("LobbyTabVec", lobbyTabsLayer, 0f, 0f, GameUi.Width, GameUi.Height);
            DrawTabPill(vec, 24f, 70f, 132f, 34f, lobbyHostMode);
            DrawTabPill(vec, 164f, 70f, 132f, 34f, !lobbyHostMode);
            vec.Commit();

            fonts.Draw(lobbyTabsLayer, "white_stroked_small", "HOST ROOM", 90f, 81f, 11f, "center",
                lobbyHostMode ? Color.white : new Color(0.84f, 0.82f, 0.94f, 0.8f));
            fonts.Draw(lobbyTabsLayer, "white_stroked_small", "JOIN ROOM", 230f, 81f, 11f, "center",
                !lobbyHostMode ? Color.white : new Color(0.84f, 0.82f, 0.94f, 0.8f));

            ui.HitBox(lobbyTabsLayer, 24f, 70f, 132f, 34f, () =>
            {
                if (lobbyHostMode) return;
                ui.Sound("cool_button", save);
                EnterHostMode();
            });
            ui.HitBox(lobbyTabsLayer, 164f, 70f, 132f, 34f, () =>
            {
                if (!lobbyHostMode) return;
                ui.Sound("cool_button", save);
                EnterJoinMode();
            });
        }

        private static void DrawTabPill(VectorCanvas vec, float x, float y, float w, float h, bool active)
        {
            vec.FillRoundRect(x, y, w, h, 8f, GameUi.HexColor(active ? "#2b7cff" : "#2f244a"));
            vec.StrokeRoundRect(x, y, w, h, 8f, GameUi.HexColor(active ? "#7be0ff" : "#6b5e8a"), 1.5f);
            if (active) vec.FillRoundRect(x + 3f, y + 3f, w - 6f, 10f, 5f, new Color(1f, 1f, 1f, 0.20f));
        }

        private static void DrawRulePill(VectorCanvas vec, float x, float y, float w, float h, bool active, string activeHex)
        {
            vec.FillRoundRect(x, y, w, h, 8f, GameUi.HexColor(active ? activeHex : "#2b2144"));
            vec.StrokeRoundRect(x, y, w, h, 8f, GameUi.HexColor(active ? "#ffe259" : "#6b5e8a"), active ? 2f : 1.2f);
        }

        private void RenderHostLobby()
        {
            if (lobbyDynamicLayer == null) return;
            GameUi.Clear(lobbyDynamicLayer);
            lobbyWaitPulseGroup = null;

            bool fair = lobbyRuleMode != "real";
            var vec = VectorCanvas.Create("HostLobbyVec", lobbyDynamicLayer, 0f, 0f, GameUi.Width, GameUi.Height);
            DrawRulePill(vec, 24f, 138f, 132f, 34f, fair, "#2b7cff");
            DrawRulePill(vec, 164f, 138f, 132f, 34f, !fair, "#c85a1c");
            DrawRulePill(vec, 24f, 180f, 132f, 34f, !lobbyItemsAllowed, "#4a3f6b");
            DrawRulePill(vec, 164f, 180f, 132f, 34f, lobbyItemsAllowed, "#2b8a4e");

            vec.FillRoundRect(24f, 258f, 272f, 106f, 10f, GameUi.HexColor("#171127"));
            vec.StrokeRoundRect(24f, 258f, 272f, 106f, 10f, GameUi.HexColor("#6b5e8a"), 1.5f);

            vec.FillRoundRect(24f, 376f, 272f, 92f, 10f, GameUi.HexColor("#2c204a"));
            vec.StrokeRoundRect(24f, 376f, 272f, 92f, 10f, GameUi.HexColor(lobbyStatusIsError ? "#ff6b6b" : "#7be0ff"), 1.5f);

            vec.FillRoundRect(90f, 486f, 140f, 38f, 9f, GameUi.HexColor("#a64f37"));
            vec.StrokeRoundRect(90f, 486f, 140f, 38f, 9f, GameUi.HexColor("#e8b35a"), 1.5f);
            vec.Commit();

            fonts.Draw(lobbyDynamicLayer, "white_stroked_small", "BATTLE RULES", 24f, 118f, 10f, "left");
            fonts.Draw(lobbyDynamicLayer, "white_stroked_small", "FAIR LV 20", 90f, 149f, 10f, "center");
            fonts.Draw(lobbyDynamicLayer, "white_stroked_small", "REAL TEAM", 230f, 149f, 10f, "center");
            fonts.Draw(lobbyDynamicLayer, "white_stroked_small", "ITEMS OFF", 90f, 191f, 10f, "center");
            fonts.Draw(lobbyDynamicLayer, "white_stroked_small", "ITEMS ON", 230f, 191f, 10f, "center");

            string ruleDesc = (fair ? "ALL DYNAMONS SET TO LV 20" : "USES SAVED DYNAMON LEVELS") +
                              (lobbyItemsAllowed ? ", STANDARD ITEM POUCH" : ", NO BATTLE ITEMS");
            fonts.DrawLines(lobbyDynamicLayer, "white_small", ruleDesc, 160f, 224f, 9f, 268f, 12f, "center");

            ui.HitBox(lobbyDynamicLayer, 24f, 138f, 132f, 34f, () => SetHostRuleMode("fair20"));
            ui.HitBox(lobbyDynamicLayer, 164f, 138f, 132f, 34f, () => SetHostRuleMode("real"));
            ui.HitBox(lobbyDynamicLayer, 24f, 180f, 132f, 34f, () => SetHostItemsAllowed(false));
            ui.HitBox(lobbyDynamicLayer, 164f, 180f, 132f, 34f, () => SetHostItemsAllowed(true));

            int port = lanTransport != null && lanTransport.BoundPort > 0 ? lanTransport.BoundPort : LanConst.BattlePortBase;
            string[] ips = LanTransport.LocalIpv4Addresses();
            string primaryIp = ips.Length > 0 ? ips[0] : "127.0.0.1";
            string secondaryLine = ips.Length > 1 ? $"ALT {ips[1]}:{port}" : $"LOCALHOST 127.0.0.1:{port}";

            fonts.Draw(lobbyDynamicLayer, "white_stroked_small", "SHARE IP WITH OPPONENT", 160f, 269f, 9.5f, "center");
            fonts.Draw(lobbyDynamicLayer, "white_small", $"IP {primaryIp} PORT {port}", 160f, 294f, 10.5f, "center", GameUi.Gold, 256f);
            fonts.Draw(lobbyDynamicLayer, "white_small", secondaryLine, 160f, 316f, 9f, "center", new Color(0.84f, 0.88f, 1f, 0.9f), 256f);
            fonts.Draw(lobbyDynamicLayer, "white_small", $"ROOM HOST: {LocalPlayerName()}", 160f, 338f, 9f, "center", null, 256f);

            var waitBox = GameUi.Box("HostWaitText", lobbyDynamicLayer, 0f, 0f, GameUi.Width, GameUi.Height);
            lobbyWaitPulseGroup = waitBox.gameObject.AddComponent<CanvasGroup>();
            if (lobbyTcpConnected)
                fonts.Draw(waitBox, "white_stroked_small", "PLAYER CONNECTED!", 160f, 398f, 11f, "center");
            else if (lanTransport != null && lanTransport.IsHost)
                fonts.Draw(waitBox, "white_stroked_small", "WAITING FOR PLAYER...", 160f, 398f, 11f, "center");
            else
                fonts.Draw(waitBox, "red_stroked_small", "HOST SOCKET UNAVAILABLE", 160f, 398f, 10.5f, "center");

            string subLine = !string.IsNullOrEmpty(lobbyStatusMessage)
                ? lobbyStatusMessage
                : (lobbyTcpConnected ? "EXCHANGING TEAM DATA" : "BROADCASTING ROOM ON WI-FI LAN");
            fonts.DrawLines(lobbyDynamicLayer, "white_small", subLine, 160f, 424f, 9f, 252f, 12f, "center",
                lobbyStatusIsError ? new Color(1f, 0.56f, 0.56f, 1f) : new Color(0.85f, 0.90f, 1f, 0.9f));

            fonts.Draw(lobbyDynamicLayer, "white_stroked_small", "CANCEL", 160f, 498f, 12f, "center");
            ui.HitBox(lobbyDynamicLayer, 90f, 486f, 140f, 38f, () =>
            {
                ui.Sound("screen_close", save);
                CloseLanLobby();
            });
        }

        private void SetHostRuleMode(string mode)
        {
            string normalized = mode == "real" ? "real" : "fair20";
            if (lobbyRuleMode == normalized) return;
            ui.Sound("cool_button", save);
            lobbyRuleMode = normalized;
            UpdateHostBeaconAndSync();
            if (lobbyInReadyRoom) RenderReadyRoom();
            else RenderHostLobby();
        }

        private void SetHostItemsAllowed(bool allowed)
        {
            if (lobbyItemsAllowed == allowed) return;
            ui.Sound("cool_button", save);
            lobbyItemsAllowed = allowed;
            UpdateHostBeaconAndSync();
            if (lobbyInReadyRoom) RenderReadyRoom();
            else RenderHostLobby();
        }

        private void UpdateHostBeaconAndSync()
        {
            if (lanTransport == null || !lobbyHostMode) return;
            lanTransport.UpdateBeacon(BuildHostBeacon());
            if (lanTransport.IsConnected)
            {
                lanTransport.Send("room_config", new LanRoomConfigDto
                {
                    ruleMode = lobbyRuleMode,
                    itemsAllowed = lobbyItemsAllowed
                });
            }
        }

        private LanBeaconDto BuildHostBeacon() => new LanBeaconDto
        {
            protocolVersion = LanConst.ProtocolVersion,
            roomId = string.IsNullOrEmpty(lobbyRoomId) ? "room" : lobbyRoomId,
            hostName = LocalPlayerName(),
            ruleMode = lobbyRuleMode == "real" ? "real" : "fair20",
            itemsAllowed = lobbyItemsAllowed,
            tcpPort = lanTransport != null && lanTransport.BoundPort > 0 ? lanTransport.BoundPort : LanConst.BattlePortBase,
            teamCount = Math.Min(3, save?.Team?.Count ?? 0)
        };

        private void BuildManualJoinSection()
        {
            if (lobbyManualJoinLayer == null) return;
            GameUi.Clear(lobbyManualJoinLayer);

            var vec = VectorCanvas.Create("ManualJoinVec", lobbyManualJoinLayer, 0f, 0f, GameUi.Width, GameUi.Height);
            vec.FillRoundRect(24f, 384f, 272f, 2f, 1f, GameUi.HexColor("#4c3c72"));
            vec.FillRoundRect(212f, 412f, 84f, 36f, 8f, GameUi.HexColor("#2b8a4e"));
            vec.StrokeRoundRect(212f, 412f, 84f, 36f, 8f, GameUi.HexColor("#ffe259"), 1.5f);
            vec.Commit();

            fonts.Draw(lobbyManualJoinLayer, "white_stroked_small", "DIRECT IP CONNECT", 24f, 392f, 10f, "left");
            lobbyIpInput = ui.TextField(lobbyManualJoinLayer, lastEnteredIp, "127.0.0.1:41976", 24f, 412f, 182f, 36f);
            lobbyIpInput.onValueChanged.AddListener(val =>
            {
                lastEnteredIp = val ?? string.Empty;
                if (lobbyStatusIsError)
                {
                    lobbyStatusIsError = false;
                    lobbyStatusMessage = string.Empty;
                    RenderConnectStatus();
                }
            });

            fonts.Draw(lobbyManualJoinLayer, "white_stroked_small", "CONNECT", 254f, 424f, 10.5f, "center");
            ui.HitBox(lobbyManualJoinLayer, 212f, 412f, 84f, 36f, OnManualConnectTapped);
        }

        private void RenderConnectStatus()
        {
            if (lobbyConnectStatusLayer == null) return;
            GameUi.Clear(lobbyConnectStatusLayer);

            if (lobbyConnecting)
            {
                var vec = VectorCanvas.Create("CancelConnectVec", lobbyConnectStatusLayer, 0f, 0f, GameUi.Width, GameUi.Height);
                vec.FillRoundRect(98f, 492f, 124f, 32f, 8f, GameUi.HexColor("#a64f37"));
                vec.StrokeRoundRect(98f, 492f, 124f, 32f, 8f, GameUi.HexColor("#e8b35a"), 1.2f);
                vec.Commit();

                fonts.DrawLines(lobbyConnectStatusLayer, "white_small", lobbyStatusMessage, 160f, 456f, 9.5f, 268f, 12f, "center", GameUi.Gold);
                fonts.Draw(lobbyConnectStatusLayer, "white_stroked_small", "CANCEL", 160f, 502f, 10f, "center");
                ui.HitBox(lobbyConnectStatusLayer, 98f, 492f, 124f, 32f, CancelPendingJoin);
                return;
            }

            if (!string.IsNullOrEmpty(lobbyStatusMessage))
            {
                Color tint = lobbyStatusIsError ? new Color(1f, 0.52f, 0.52f, 1f) : new Color(0.84f, 0.90f, 1f, 0.95f);
                fonts.DrawLines(lobbyConnectStatusLayer, "white_small", lobbyStatusMessage, 160f, 458f, 9.5f, 268f, 12f, "center", tint);
            }
            else
            {
                fonts.DrawLines(lobbyConnectStatusLayer, "white_small", "DEFAULT PORT 41976 IF OMITTED", 160f, 462f, 8.5f, 268f, 11f, "center",
                    new Color(0.74f, 0.74f, 0.88f, 0.85f));
            }
        }

        private void CancelPendingJoin()
        {
            ui.Sound("screen_close", save);
            lobbyConnecting = false;
            lobbyTcpConnected = false;
            sentLocalHello = false;
            lobbyStatusIsError = false;
            lobbyStatusMessage = "CONNECTION CANCELLED";
            EnsureLanTransport().StartDiscovery();
            RenderConnectStatus();
            RefreshDiscoveredRoomsIfChanged(true);
        }

        private void RefreshDiscoveredRoomsIfChanged(bool force = false)
        {
            if (lanTransport == null || !lobbyOpen || lobbyHostMode || lobbyInReadyRoom) return;
            var rooms = lanTransport.GetRooms();
            string signature = BuildRoomsSignature(rooms);
            if (!force && signature == lastRoomsSignature) return;
            lastRoomsSignature = signature;
            RenderDiscoveredRooms(rooms);
        }

        private string BuildRoomsSignature(List<LanDiscoveredRoom> rooms)
        {
            var sb = new StringBuilder(128);
            sb.Append(lobbyConnecting ? "1|" : "0|");
            if (rooms != null)
            {
                int count = Math.Min(4, rooms.Count);
                sb.Append(rooms.Count).Append('|');
                for (int i = 0; i < count; i++)
                {
                    var r = rooms[i];
                    if (r == null) continue;
                    sb.Append(r.Key).Append(':')
                      .Append(r.HostName).Append(':')
                      .Append(r.RuleMode).Append(':')
                      .Append(r.ItemsAllowed ? '1' : '0').Append(':')
                      .Append(r.TeamCount).Append(';');
                }
            }
            return sb.ToString();
        }

        private void RenderDiscoveredRooms(List<LanDiscoveredRoom> rooms)
        {
            if (lobbyDynamicLayer == null) return;
            GameUi.Clear(lobbyDynamicLayer);

            fonts.Draw(lobbyDynamicLayer, "white_stroked_small", "NEARBY LAN ROOMS", 24f, 116f, 10f, "left");
            fonts.Draw(lobbyDynamicLayer, "white_small", "SCANNING", 296f, 117f, 8.5f, "right", new Color(0.55f, 0.90f, 1f, 0.9f));

            if (rooms == null || rooms.Count == 0)
            {
                var emptyVec = VectorCanvas.Create("EmptyRoomsVec", lobbyDynamicLayer, 0f, 0f, GameUi.Width, GameUi.Height);
                emptyVec.FillRoundRect(24f, 142f, 272f, 224f, 10f, GameUi.HexColor("#1a132c"));
                emptyVec.StrokeRoundRect(24f, 142f, 272f, 224f, 10f, GameUi.HexColor("#4f3e75"), 1.2f);
                emptyVec.Commit();

                fonts.Draw(lobbyDynamicLayer, "white_stroked_small", "NO ROOMS FOUND YET", 160f, 212f, 11f, "center");
                fonts.DrawLines(lobbyDynamicLayer, "white_small",
                    "JOIN THE SAME WI-FI NETWORK OR ENTER THE HOST IP ADDRESS BELOW.",
                    160f, 240f, 9f, 236f, 13f, "center", new Color(0.80f, 0.80f, 0.94f, 0.85f));
                return;
            }

            int shown = Math.Min(4, rooms.Count);
            var vec = VectorCanvas.Create("RoomsListVec", lobbyDynamicLayer, 0f, 0f, GameUi.Width, GameUi.Height);
            for (int i = 0; i < shown; i++)
            {
                var room = rooms[i];
                float ry = 138f + i * 58f;
                bool fair = room.RuleMode != "real";

                vec.FillRoundRect(24f, ry, 272f, 52f, 9f, GameUi.HexColor("#2d2149"));
                vec.StrokeRoundRect(24f, ry, 272f, 52f, 9f, GameUi.HexColor("#6b5e8a"), 1.3f);

                vec.FillRoundRect(32f, ry + 29f, 68f, 16f, 5f, GameUi.HexColor(fair ? "#2b7cff" : "#c85a1c"));
                vec.FillRoundRect(104f, ry + 29f, 62f, 16f, 5f, GameUi.HexColor(room.ItemsAllowed ? "#2b8a4e" : "#493f66"));

                vec.FillRoundRect(234f, ry + 10f, 54f, 32f, 7f, GameUi.HexColor(lobbyConnecting ? "#4b5a52" : "#2b8a4e"));
                vec.StrokeRoundRect(234f, ry + 10f, 54f, 32f, 7f, GameUi.HexColor("#ffe259"), 1.2f);
            }
            vec.Commit();

            for (int i = 0; i < shown; i++)
            {
                var room = rooms[i];
                float ry = 138f + i * 58f;
                bool fair = room.RuleMode != "real";
                string hostLabel = SanitizeDisplayName(room.HostName, "HOST");
                string monsLabel = room.TeamCount == 1 ? "1 MON" : $"{Math.Max(1, room.TeamCount)} MONS";

                fonts.Draw(lobbyDynamicLayer, "white_stroked_small", hostLabel, 32f, ry + 8f, 10.5f, "left", null, 134f);
                fonts.Draw(lobbyDynamicLayer, "white_small", monsLabel, 224f, ry + 10f, 8.5f, "right", GameUi.Gold);
                fonts.Draw(lobbyDynamicLayer, "white_stroked_small", fair ? "FAIR LV 20" : "REAL TEAM", 66f, ry + 32f, 7.2f, "center", null, 62f);
                fonts.Draw(lobbyDynamicLayer, "white_stroked_small", room.ItemsAllowed ? "ITEMS ON" : "ITEMS OFF", 135f, ry + 32f, 7.2f, "center", null, 56f);
                fonts.Draw(lobbyDynamicLayer, "white_small", $"{room.HostIp}:{room.TcpPort}", 170f, ry + 32f, 7.5f, "left",
                    new Color(0.84f, 0.88f, 1f, 0.9f), 60f);

                fonts.Draw(lobbyDynamicLayer, "white_stroked_small", "JOIN", 261f, ry + 20f, 10f, "center",
                    lobbyConnecting ? new Color(1f, 1f, 1f, 0.55f) : Color.white);

                var selectedRoom = room;
                ui.HitBox(lobbyDynamicLayer, 234f, ry + 10f, 54f, 32f, () => ConnectToRoom(selectedRoom));
            }
        }

        private void ConnectToRoom(LanDiscoveredRoom room)
        {
            if (lobbyHostMode || room == null || lobbyConnecting || lobbyInReadyRoom) return;
            ui.Sound("button_click", save);
            lastEnteredIp = $"{room.HostIp}:{room.TcpPort}";
            if (lobbyIpInput != null) lobbyIpInput.text = lastEnteredIp;
            lobbyRuleMode = room.RuleMode == "real" ? "real" : "fair20";
            lobbyItemsAllowed = room.ItemsAllowed;
            BeginClientConnect(room.HostIp, room.TcpPort);
        }

        private void OnManualConnectTapped()
        {
            if (lobbyConnecting || lobbyInReadyRoom) return;
            string raw = lobbyIpInput != null ? lobbyIpInput.text : lastEnteredIp;
            if (!TryParseIpAndPort(raw, out string ip, out int port, out string error))
            {
                ui.Sound("stat_lowered", save);
                lobbyStatusIsError = true;
                lobbyStatusMessage = error;
                RenderConnectStatus();
                return;
            }
            ui.Sound("button_click", save);
            lastEnteredIp = $"{ip}:{port}";
            BeginClientConnect(ip, port);
        }

        private void BeginClientConnect(string ip, int port)
        {
            lobbyConnecting = true;
            lobbyTcpConnected = false;
            sentLocalHello = false;
            remoteHello = null;
            lobbyStatusIsError = false;
            lobbyStatusMessage = $"CONNECTING TO {ip}:{port}...";
            EnsureLanTransport().ConnectDirect(ip, port);
            RenderConnectStatus();
            RefreshDiscoveredRoomsIfChanged(true);
        }

        private void RenderReadyRoom()
        {
            if (lobbyDynamicLayer == null) return;
            GameUi.Clear(lobbyDynamicLayer);
            lobbyWaitPulseGroup = null;

            bool fair = lobbyRuleMode != "real";
            var vec = VectorCanvas.Create("ReadyRoomVec", lobbyDynamicLayer, 0f, 0f, GameUi.Width, GameUi.Height);

            DrawRulePill(vec, 44f, 70f, 110f, 26f, true, fair ? "#2b7cff" : "#c85a1c");
            DrawRulePill(vec, 166f, 70f, 110f, 26f, true, lobbyItemsAllowed ? "#2b8a4e" : "#4a3f6b");

            vec.FillRoundRect(24f, 106f, 272f, 114f, 10f, GameUi.HexColor("#1b1330"));
            vec.StrokeRoundRect(24f, 106f, 272f, 114f, 10f, GameUi.HexColor("#6b5e8a"), 1.3f);

            vec.FillRoundRect(24f, 254f, 272f, 114f, 10f, GameUi.HexColor("#1b1330"));
            vec.StrokeRoundRect(24f, 254f, 272f, 114f, 10f, GameUi.HexColor("#e8b35a"), 1.3f);

            if (lobbyHostMode)
            {
                vec.FillRoundRect(48f, 408f, 224f, 44f, 10f, GameUi.HexColor("#2b8a4e"));
                vec.StrokeRoundRect(48f, 408f, 224f, 44f, 10f, GameUi.HexColor("#ffe259"), 2f);
            }
            else
            {
                vec.FillRoundRect(34f, 408f, 252f, 44f, 10f, GameUi.HexColor("#2c204a"));
                vec.StrokeRoundRect(34f, 408f, 252f, 44f, 10f, GameUi.HexColor("#7be0ff"), 1.5f);
            }

            vec.FillRoundRect(90f, 472f, 140f, 36f, 9f, GameUi.HexColor("#a64f37"));
            vec.StrokeRoundRect(90f, 472f, 140f, 36f, 9f, GameUi.HexColor("#e8b35a"), 1.3f);
            vec.Commit();

            fonts.Draw(lobbyDynamicLayer, "white_stroked_small", fair ? "FAIR LV 20" : "REAL TEAM", 99f, 78f, 9f, "center");
            fonts.Draw(lobbyDynamicLayer, "white_stroked_small", lobbyItemsAllowed ? "ITEMS ON" : "ITEMS OFF", 221f, 78f, 9f, "center");

            if (lobbyHostMode)
            {
                ui.HitBox(lobbyDynamicLayer, 44f, 70f, 110f, 26f, () => SetHostRuleMode(fair ? "real" : "fair20"));
                ui.HitBox(lobbyDynamicLayer, 166f, 70f, 110f, 26f, () => SetHostItemsAllowed(!lobbyItemsAllowed));
            }

            string rivalName = SanitizeDisplayName(remoteHello?.playerName, lobbyHostMode ? "RIVAL" : "HOST");
            fonts.Draw(lobbyDynamicLayer, "white_stroked_small", $"RIVAL: {rivalName}", 160f, 114f, 10f, "center", null, 252f);
            DrawReadyRoster(SanitizeRemoteRoster(remoteHello?.mons), 132f, fair);

            fonts.Draw(lobbyDynamicLayer, "white_stroked_big", "VS", 160f, 229f, 16f, "center");

            fonts.Draw(lobbyDynamicLayer, "white_stroked_small", $"YOU: {LocalPlayerName()}", 160f, 262f, 10f, "center", GameUi.Gold, 252f);
            DrawReadyRoster(BuildLocalTeamRoster(), 280f, fair);

            if (!string.IsNullOrEmpty(lobbyStatusMessage))
                fonts.Draw(lobbyDynamicLayer, "white_small", lobbyStatusMessage, 160f, 382f, 9f, "center",
                    lobbyStatusIsError ? new Color(1f, 0.55f, 0.55f, 1f) : GameUi.Gold, 268f);

            if (lobbyHostMode)
            {
                fonts.Draw(lobbyDynamicLayer, "white_stroked_big", "START DUEL", 160f, 422f, 15f, "center");
                ui.HitBox(lobbyDynamicLayer, 48f, 408f, 224f, 44f, OnHostStartDuel);
            }
            else
            {
                fonts.Draw(lobbyDynamicLayer, "white_stroked_small", "WAITING FOR HOST", 160f, 424f, 11.5f, "center");
            }

            fonts.Draw(lobbyDynamicLayer, "white_stroked_small", "LEAVE", 160f, 484f, 11f, "center");
            ui.HitBox(lobbyDynamicLayer, 90f, 472f, 140f, 36f, () =>
            {
                ui.Sound("screen_close", save);
                if (lobbyHostMode) EnterHostMode();
                else EnterJoinMode();
            });
        }

        private void DrawReadyRoster(LanTeamMemberDto[] roster, float rowY, bool fair)
        {
            if (lobbyDynamicLayer == null || roster == null || roster.Length == 0) return;
            int count = Math.Min(3, roster.Length);
            float totalW = count * 65f + Math.Max(0, count - 1) * 10f;
            float startX = GameUi.Width * 0.5f - totalW * 0.5f;
            int maxHp = data?.battleData?.maxHP ?? 100;

            for (int i = 0; i < count; i++)
            {
                var slot = roster[i];
                if (slot == null || string.IsNullOrEmpty(slot.monId) || data.Mon(slot.monId) == null) continue;
                int effectiveLevel = fair ? data.MaxLevel : GameData.Clamp(slot.level, 1, data.MaxLevel);
                var monView = new MonHudView
                {
                    MonId = slot.monId,
                    Level = effectiveLevel,
                    MaxHp = maxHp,
                    Hp = maxHp
                };
                HudWidgets.DrawMonPane(ui, fonts, data, lobbyDynamicLayer, monView, "mini", startX + i * 75f, rowY);
            }
        }

        private void HandleLobbyNetMessage(LanNetMessage msg)
        {
            switch (msg.Type)
            {
                case "connected":
                {
                    lobbyTcpConnected = true;
                    lobbyConnecting = false;
                    lobbyStatusIsError = false;
                    lobbyStatusMessage = "CONNECTED! SYNCING TEAMS...";
                    SendLocalHello();
                    if (lobbyHostMode && lanTransport != null)
                    {
                        lanTransport.Send("room_config", new LanRoomConfigDto
                        {
                            ruleMode = lobbyRuleMode,
                            itemsAllowed = lobbyItemsAllowed
                        });
                    }
                    if (!lobbyInReadyRoom)
                    {
                        if (lobbyHostMode) RenderHostLobby();
                        else RenderConnectStatus();
                    }
                    break;
                }
                case "hello":
                {
                    var hello = LanCodec.Decode<LanHelloDto>(msg.JsonPayload);
                    var validMons = SanitizeRemoteRoster(hello?.mons);
                    if (hello == null || hello.proto != LanConst.ProtocolVersion || validMons.Length == 0)
                    {
                        HandleLobbyPeerDisconnect("INCOMPATIBLE PLAYER DATA");
                        return;
                    }
                    hello.mons = validMons;
                    hello.playerName = SanitizeDisplayName(hello.playerName, lobbyHostMode ? "RIVAL" : "HOST");
                    remoteHello = hello;
                    if (!sentLocalHello) SendLocalHello();
                    lobbyInReadyRoom = true;
                    lobbyConnecting = false;
                    lobbyStatusIsError = false;
                    lobbyStatusMessage = string.Empty;
                    ui.Sound("screen_open", save);
                    RebuildLobbyChrome();
                    break;
                }
                case "room_config":
                {
                    if (lobbyHostMode) break;
                    var cfg = LanCodec.Decode<LanRoomConfigDto>(msg.JsonPayload);
                    if (cfg == null) break;
                    lobbyRuleMode = cfg.ruleMode == "real" ? "real" : "fair20";
                    lobbyItemsAllowed = cfg.itemsAllowed;
                    if (lobbyInReadyRoom) RenderReadyRoom();
                    break;
                }
                case "start":
                {
                    if (lobbyHostMode) break;
                    var startMatch = LanCodec.Decode<LanStartMatchDto>(msg.JsonPayload);
                    if (!IsValidStartMatch(startMatch))
                    {
                        HandleLobbyPeerDisconnect("INVALID MATCH DATA");
                        return;
                    }
                    BeginPvpBattle(startMatch, false);
                    break;
                }
                case "disconnected":
                {
                    HandleLobbyPeerDisconnect(SanitizeStatusText(msg.Reason, "DISCONNECTED"));
                    break;
                }
            }
        }

        private void HandleLobbyPeerDisconnect(string reason)
        {
            ui.Sound("stat_lowered", save);
            lobbyStatusIsError = true;
            lobbyStatusMessage = string.IsNullOrEmpty(reason) ? "DISCONNECTED" : reason;

            if (lobbyHostMode)
            {
                EnterHostMode(preserveStatus: true);
                return;
            }

            if (lobbyInReadyRoom)
            {
                EnterJoinMode(preserveStatus: true);
                return;
            }

            lobbyConnecting = false;
            lobbyTcpConnected = false;
            sentLocalHello = false;
            remoteHello = null;
            EnsureLanTransport().StartDiscovery();
            RenderConnectStatus();
            RefreshDiscoveredRoomsIfChanged(true);
        }

        private void SendLocalHello()
        {
            if (lanTransport == null || !lanTransport.IsConnected || sentLocalHello) return;
            var roster = BuildLocalTeamRoster();
            if (roster.Length == 0) return;
            sentLocalHello = true;
            lanTransport.Send("hello", new LanHelloDto
            {
                proto = LanConst.ProtocolVersion,
                playerName = LocalPlayerName(),
                avatarId = "hero",
                partySize = roster.Length,
                mons = roster
            });
        }

        private void OnHostStartDuel()
        {
            if (!lobbyHostMode || !lobbyInReadyRoom || lobbyStartingMatch) return;
            if (lanTransport == null || !lanTransport.IsConnected || remoteHello == null) return;

            var hostRoster = BuildLocalTeamRoster();
            var clientRoster = SanitizeRemoteRoster(remoteHello.mons);
            if (hostRoster.Length == 0 || clientRoster.Length == 0)
            {
                lobbyStatusIsError = true;
                lobbyStatusMessage = "BOTH PLAYERS NEED AT LEAST 1 DYNAMON";
                RenderReadyRoom();
                return;
            }

            lobbyStartingMatch = true;
            ui.Sound("button_click", save);
            pvpRules = new PvpBattleRules(data, hostRoster, clientRoster, lobbyRuleMode, lobbyItemsAllowed, lanRng.NextDouble);
            var startMatch = pvpRules.BuildStartMatch(LocalPlayerName(), remoteHello.playerName);
            lanTransport.Send("start", startMatch);
            BeginPvpBattle(startMatch, true);
        }

        private LanTeamMemberDto[] BuildLocalTeamRoster()
        {
            var list = new List<LanTeamMemberDto>(3);
            if (save?.Team == null || data == null) return Array.Empty<LanTeamMemberDto>();
            for (int i = 0; i < save.Team.Count && list.Count < 3; i++)
            {
                var owned = save.Team[i];
                if (owned == null || string.IsNullOrEmpty(owned.MonId) || data.Mon(owned.MonId) == null) continue;
                list.Add(new LanTeamMemberDto
                {
                    monId = owned.MonId,
                    level = GameData.Clamp(owned.Level, 1, data.MaxLevel)
                });
            }
            return list.ToArray();
        }

        private LanTeamMemberDto[] SanitizeRemoteRoster(LanTeamMemberDto[] mons)
        {
            var list = new List<LanTeamMemberDto>(3);
            if (mons == null || data == null) return Array.Empty<LanTeamMemberDto>();
            for (int i = 0; i < mons.Length && list.Count < 3; i++)
            {
                var m = mons[i];
                if (m == null || string.IsNullOrEmpty(m.monId) || data.Mon(m.monId) == null) continue;
                list.Add(new LanTeamMemberDto
                {
                    monId = m.monId,
                    level = GameData.Clamp(m.level, 1, data.MaxLevel)
                });
            }
            return list.ToArray();
        }

        private bool IsValidStartMatch(LanStartMatchDto dto)
        {
            if (dto == null || data == null) return false;
            if (dto.hostTeam == null || dto.hostTeam.Length == 0 || dto.hostTeam.Length > 3) return false;
            if (dto.clientTeam == null || dto.clientTeam.Length == 0 || dto.clientTeam.Length > 3) return false;
            for (int i = 0; i < dto.hostTeam.Length; i++)
            {
                var m = dto.hostTeam[i];
                if (m == null || string.IsNullOrEmpty(m.monId) || data.Mon(m.monId) == null) return false;
            }
            for (int i = 0; i < dto.clientTeam.Length; i++)
            {
                var m = dto.clientTeam[i];
                if (m == null || string.IsNullOrEmpty(m.monId) || data.Mon(m.monId) == null) return false;
            }
            return true;
        }

        private static bool TryParseIpAndPort(string raw, out string ip, out int port, out string error)
        {
            ip = string.Empty;
            port = LanConst.BattlePortBase;
            error = string.Empty;

            string trimmed = raw != null ? raw.Trim() : string.Empty;
            if (string.IsNullOrEmpty(trimmed))
            {
                error = "ENTER HOST IP ADDRESS";
                return false;
            }

            int colon = trimmed.IndexOf(':');
            string hostPart = trimmed;
            if (colon >= 0)
            {
                if (trimmed.IndexOf(':', colon + 1) >= 0)
                {
                    error = "USE IPV4:PORT (EX 192.168.1.10:41976)";
                    return false;
                }
                hostPart = trimmed.Substring(0, colon).Trim();
                string portPart = trimmed.Substring(colon + 1).Trim();
                if (string.IsNullOrEmpty(portPart) ||
                    !int.TryParse(portPart, NumberStyles.None, CultureInfo.InvariantCulture, out port) ||
                    port < 1 || port > 65535)
                {
                    error = "PORT MUST BE 1 TO 65535";
                    return false;
                }
            }

            if (string.IsNullOrEmpty(hostPart))
            {
                error = "ENTER HOST IP ADDRESS";
                return false;
            }

            if (string.Equals(hostPart, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                ip = "127.0.0.1";
                return true;
            }

            string[] octets = hostPart.Split('.');
            if (octets.Length != 4)
            {
                error = "INVALID IPV4 ADDRESS";
                return false;
            }
            for (int i = 0; i < 4; i++)
            {
                if (string.IsNullOrEmpty(octets[i]) ||
                    !byte.TryParse(octets[i], NumberStyles.None, CultureInfo.InvariantCulture, out _))
                {
                    error = "INVALID IPV4 ADDRESS";
                    return false;
                }
            }

            ip = hostPart;
            return true;
        }

        private static string LocalPlayerName()
        {
            string raw = SystemInfo.deviceName;
            if (string.IsNullOrWhiteSpace(raw) || string.Equals(raw.Trim(), "<unknown>", StringComparison.OrdinalIgnoreCase))
                raw = "TRAINER";
            return SanitizeDisplayName(raw, "TRAINER");
        }

        private static string SanitizeDisplayName(string raw, string fallback)
        {
            string cleaned = SanitizeStatusText(raw, fallback);
            return cleaned.Length > 12 ? cleaned.Substring(0, 12).TrimEnd() : cleaned;
        }

        private static string SanitizeStatusText(string raw, string fallback)
        {
            if (string.IsNullOrWhiteSpace(raw)) return fallback;
            var sb = new StringBuilder(raw.Length);
            bool prevSpace = true;
            for (int i = 0; i < raw.Length && sb.Length < 78; i++)
            {
                char ch = char.ToUpperInvariant(raw[i]);
                if (ch == '+' || ch == '&') ch = ' ';
                bool ok = (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') ||
                          ch == ' ' || ch == '.' || ch == ':' || ch == '-' ||
                          ch == '/' || ch == '!' || ch == '?' || ch == '(' || ch == ')';
                if (!ok) continue;
                if (ch == ' ')
                {
                    if (prevSpace) continue;
                    prevSpace = true;
                }
                else prevSpace = false;
                sb.Append(ch);
            }
            string result = sb.ToString().Trim();
            return string.IsNullOrEmpty(result) ? fallback : result;
        }
    }
}
