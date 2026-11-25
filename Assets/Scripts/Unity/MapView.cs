using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Dynamons
{
    public sealed partial class GameRoot
    {
        private const float MapMinX = -1035f;
        private const float BoatCrossingDuration = 0.9f;
        private static readonly Vector2 PierPos = new Vector2(206f, 468f);
        private static readonly Vector2 PierBoatPos = new Vector2(214f, 478f);
        private static readonly Vector2 LanIslandPos = new Vector2(348f, 486f);
        private static readonly Vector2 IslandBoatPos = new Vector2(316f, 492f);
        private static readonly Vector2 LanNpcPos = new Vector2(358f, 466f);
        private static readonly Vector2 LanIslandAvatarPos = new Vector2(336f, 482f);
        private static readonly Vector2 LanCameraFocus = new Vector2(280f, 460f);

        private readonly List<(RectTransform rt, float baseY)> mapMarkers = new List<(RectTransform, float)>();
        private RectTransform mapLayer, mapAvatar, boatRect, lanNpcMarkerBox, lanHudLayer, freeHealBox, mapTicketBadge;
        private VectorCanvas boatWakeVec;
        private RawImage arenaFlashImage;
        private MapDrag mapDrag;
        private Dictionary<int, Encounter> cachedMapRolls;
        private Coroutine walkRoutine;
        private float mapOffset, minOffset, arenaFlashTimer;
        private bool walking, traveling;
        // Session flag intentionally survives ResetMapVisualRefs() / ShowMap() rebuilds; cleared on Splash(), Choose(), and island departure.
        private bool onLanIsland;
        private Vector2 avatarLogicalPos;
        private int mapAdvance = 1;

        private void ResetMapVisualRefs()
        {
            mapMarkers.Clear();
            mapLayer = null;
            mapAvatar = null;
            boatRect = null;
            boatWakeVec = null;
            lanNpcMarkerBox = null;
            lanHudLayer = null;
            freeHealBox = null;
            mapTicketBadge = null;
            arenaFlashImage = null;
            mapDrag = null;
            cachedMapRolls = null;
            walkRoutine = null;
            arenaFlashTimer = 0f;
            walking = false;
            traveling = false;
        }

        private void UpdateMapAnimations()
        {
            if (mapLayer == null) return;
            float hoverY = Mathf.Sin(Time.unscaledTime * 5f) * 3f;
            for (int i = 0; i < mapMarkers.Count; i++)
            {
                var (rt, baseY) = mapMarkers[i];
                if (rt != null) rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, -(baseY + hoverY));
            }
            if (arenaFlashImage != null && arenaFlashTimer > 0f)
            {
                arenaFlashTimer = Mathf.Max(0f, arenaFlashTimer - Time.unscaledDeltaTime);
                float a = (arenaFlashTimer / 0.14f) * 0.45f;
                arenaFlashImage.color = new Color(1f, 1f, 1f, a);
                arenaFlashImage.enabled = a > 0.01f;
            }
        }

        private void ShowMap()
        {
            walking = false;
            traveling = false;
            if (save.Team.Count == 0 || save.NodeId != 1) onLanIsland = false;
            if (save.Event.Sync(DateTime.Today)) Persist();
            if (save.Event.RewardPending) { ShowStarterReward(); return; }
            BeginPage(null); Track("music_map_loop");

            var current = data.Node(save.NodeId) ?? data.mapNodes[0];
            mapAdvance = Math.Max(save.Advance, data.Pack(current.pack)?.advance ?? 1);
            var visibility = flow.World.Visibility(save);
            int maxScroll = visibility?.scrollMax ?? 0;
            minOffset = maxScroll > 0 ? Mathf.Max(MapMinX, -(maxScroll - GameUi.Width)) : MapMinX;

            cachedMapRolls = flow.World.RollMap(save);
            var rolls = cachedMapRolls;
            mapLayer = GameUi.Box("WorldMap", page, 0f, 0f, 1355f, GameUi.Height);
            mapDrag = mapLayer.gameObject.AddComponent<MapDrag>();
            mapDrag.Frame = ui.Frame;
            mapDrag.Move = delta =>
            {
                if (overlay == null) Pan(mapOffset + delta);
            };
            GameUi.Fill(mapLayer, GameUi.HexColor("#56bcd4"), -75f, 0f, 1430f, GameUi.Height, true);

            RenderMapContents(mapLayer, rolls, visibility, true);
            var rest = onLanIsland ? LanIslandAvatarPos : NodeAvatarPos(current, rolls);
            avatarLogicalPos = rest;
            PlaceMapAvatar(rest.x, rest.y, 0f);
            float focusX = onLanIsland ? LanCameraFocus.x : current.pos[0];
            Pan(GameUi.Width * 0.5f - focusX);

            HudWidgets.DrawTopHud(ui, fonts, data, save, page, false);
            HudWidgets.DrawMuteButton(ui, page, save, musicTrack, () => Persist(), null);

            DrawMapTopLeftControls();
            DrawMapBottomDock();
            lanHudLayer = GameUi.Box("LanIslandHud", page, 0f, 0f, GameUi.Width, GameUi.Height);
            RefreshLanIslandHud();
        }

        private void RenderMapBackdrop(Transform parent)
        {
            var current = data.Node(save.NodeId) ?? data.mapNodes[0];
            mapAdvance = Math.Max(save.Advance, data.Pack(current.pack)?.advance ?? 1);
            var visibility = flow.World.Visibility(save);
            int maxScroll = visibility?.scrollMax ?? 0;
            minOffset = maxScroll > 0 ? Mathf.Max(MapMinX, -(maxScroll - GameUi.Width)) : MapMinX;
            var rolls = cachedMapRolls ?? flow.World.RollMap(save);

            var backdrop = GameUi.Box("WorldBackdrop", parent, 0f, 0f, 1355f, GameUi.Height);
            GameUi.Fill(backdrop, GameUi.HexColor("#56bcd4"), -75f, 0f, 1430f, GameUi.Height);
            RenderMapContents(backdrop, rolls, visibility, false);
            var rest = onLanIsland ? LanIslandAvatarPos : NodeAvatarPos(current, rolls);
            var av = ui.Art(backdrop, "map/avatar", rest.x - 12.5f, rest.y - 30f, 25f, 30f, true);
            av.transform.SetAsLastSibling();
            float focusX = onLanIsland ? LanCameraFocus.x : current.pos[0];
            backdrop.anchoredPosition = new Vector2(Mathf.Clamp(GameUi.Width * 0.5f - focusX, minOffset, 0f), 0f);
        }

        private void DrawPathDots(Transform container, Vector2 from, Vector2 to, float alpha, int skipStart = 2, int skipEnd = 1, Color? tint = null)
        {
            float dx = to.x - from.x, dy = to.y - from.y;
            float dist = Mathf.Sqrt(dx * dx + dy * dy);
            int steps = Math.Max(3, Mathf.RoundToInt(dist / 13f));
            for (int i = skipStart; i < steps - skipEnd; i++)
            {
                float t = i / (float)steps;
                var dot = ui.Art(container, "map/path_dot", from.x + dx * t - 2.5f, from.y + dy * t - 2.5f, 5f, 5f, false, alpha);
                if (tint.HasValue && dot != null)
                    dot.color = new Color(tint.Value.r, tint.Value.g, tint.Value.b, alpha);
            }
        }

        private void RenderMapContents(Transform container, Dictionary<int, Encounter> rolls, Visibility visibility, bool interactive)
        {
            DrawMapStrips(container, 0f, false);

            var availableIds = new HashSet<int>(flow.World.Available(save).Select(n => n.id));
            var visiblePacks = new HashSet<string>(visibility?.visible ?? Array.Empty<string>());
            foreach (int id in availableIds)
            {
                var n = data.Node(id);
                if (n != null && !string.IsNullOrEmpty(n.pack)) visiblePacks.Add(n.pack);
            }
            var cleared = new HashSet<int>(save.ClearedNodes);

            foreach (var node in data.mapNodes)
            {
                if (!visiblePacks.Contains(node.pack)) continue;
                var parent = data.Node(node.connect);
                if (parent == null || !visiblePacks.Contains(parent.pack)) continue;
                bool unlocked = availableIds.Contains(node.id) && availableIds.Contains(parent.id);
                DrawPathDots(container, new Vector2(parent.pos[0], parent.pos[1]), new Vector2(node.pos[0], node.pos[1]), unlocked ? 0.95f : 0.3f);
            }

            if (save.Team.Count > 0)
            {
                var buoyTint = GameUi.HexColor("#d9f7ff");
                var node1 = data.Node(1);
                if (node1 != null)
                    DrawPathDots(container, new Vector2(node1.pos[0], node1.pos[1]), PierPos, 0.82f, 2, 1, buoyTint);
                DrawPathDots(container, PierPos, IslandBoatPos, 0.85f, 1, 1, buoyTint);
            }

            foreach (var node in data.mapNodes)
            {
                if (!visiblePacks.Contains(node.pack)) continue;
                float x = node.pos[0], y = node.pos[1];
                bool open = availableIds.Contains(node.id);
                bool isCleared = cleared.Contains(node.id);
                bool arena = IsArenaNode(node);
                rolls.TryGetValue(node.id, out var enc);
                bool actionable = open && enc != null && enc.Kind != "none";

                if (arena)
                {
                    var flash = ui.Art(container, "map/node_arena", x - 60f, y - 40f, 120f, 80f, true, 0f);
                    flash.enabled = false;
                    if (interactive) arenaFlashImage = flash;
                }
                else
                {
                    ui.Art(container, "map/node", x - 25f, y - 20f, 50f, 40f, false, open ? 1f : 0.38f);
                }

                string enemyId = null;
                float enemyAlpha = 1f;
                if (open && enc != null && (enc.Kind == "boss" || enc.Kind == "captain" || enc.Kind == "training") && !string.IsNullOrEmpty(enc.Captain))
                {
                    enemyId = enc.Captain;
                }
                else if (!open && node.bossBattle != null && !string.IsNullOrEmpty(node.bossBattle.enemy) && !isCleared)
                {
                    enemyId = node.bossBattle.enemy;
                    enemyAlpha = 0.65f;
                }

                if (!string.IsNullOrEmpty(enemyId))
                    ui.Art(container, $"general/enemies/{enemyId}_avatar", x - 24f, y - 34f, 30f, 36f, true, enemyAlpha);

                if (actionable)
                {
                    float markerY = enemyId != null ? y - 54f : y - 44f;
                    var marker = ui.Art(container, arena ? "map/marker_arena" : "map/marker_battle", x - 17.5f, markerY, 35f, 40f);
                    if (interactive) mapMarkers.Add((marker.rectTransform, markerY));
                }

                if (open && enc != null && enc.EventNpc)
                {
                    var badgeVec = VectorCanvas.Create("EventBadge", container, x - 18f, y - 65f, 36f, 12f);
                    badgeVec.FillRoundRect(0f, 0f, 36f, 12f, 5f, GameUi.HexColor("#a64f37"));
                    badgeVec.Commit();
                    fonts.Draw(container, "white_stroked_small", "EVENT", x, y - 63f, 8f, "center");
                }

                if (interactive && open)
                {
                    float hitW = arena ? 104f : 52f, hitH = arena ? 72f : actionable ? 68f : 44f;
                    float hitY = arena ? y - 36f : actionable ? y - 48f : y - 22f;
                    var targetNode = node;
                    var targetEnc = enc;
                    AttachMapTapAndDrag(container, x - hitW * 0.5f, hitY, hitW, hitH, () =>
                    {
                        if (arena) arenaFlashTimer = 0.14f;
                        Travel(targetNode, targetEnc);
                    });
                }
            }

            if (save.Team.Count > 0)
            {
                ui.Art(container, "map/island_lan", LanIslandPos.x - 46f, LanIslandPos.y - 32f, 92f, 64f, true);
                ui.Art(container, "general/enemies/lan_npc_avatar", LanNpcPos.x - 16f, LanNpcPos.y - 36f, 32f, 38f, true);

                if (!onLanIsland || interactive)
                {
                    var markerContainer = GameUi.Box("LanNpcMarkerBox", container, 0f, 0f, 1355f, GameUi.Height);
                    float markerY = LanNpcPos.y - 68f;
                    var marker = ui.Art(markerContainer, "map/marker_battle", LanNpcPos.x - 14f, markerY, 28f, 32f, true);
                    if (interactive)
                    {
                        lanNpcMarkerBox = markerContainer;
                        mapMarkers.Add((marker.rectTransform, markerY));
                    }

                    float pillX = LanNpcPos.x - 16f, pillY = LanNpcPos.y - 80f;
                    var lanPill = VectorCanvas.Create("LanNpcPill", markerContainer, pillX, pillY, 32f, 12f);
                    lanPill.FillRoundRect(0f, 0f, 32f, 12f, 5f, GameUi.HexColor("#2b7cff"));
                    lanPill.StrokeRoundRect(0f, 0f, 32f, 12f, 5f, GameUi.HexColor("#ffffff", 0.9f), 1f);
                    lanPill.Commit();
                    fonts.Draw(markerContainer, "white_stroked_small", "LAN", LanNpcPos.x, pillY + 2.5f, 8f, "center");
                    if (interactive) markerContainer.gameObject.SetActive(!onLanIsland);
                }

                if (interactive)
                    boatWakeVec = VectorCanvas.Create("BoatWake", container, 0f, 0f, 1355f, GameUi.Height);

                var boatPos = onLanIsland ? IslandBoatPos : PierBoatPos;
                var boatImg = ui.Art(container, "map/boat", boatPos.x - 17f, boatPos.y - 14f, 34f, 28f, true);
                if (interactive)
                {
                    boatRect = boatImg.rectTransform;
                    boatRect.pivot = new Vector2(0.5f, 0.5f);
                    PlaceBoat(boatPos.x, boatPos.y, 0f, 0f);
                    AttachMapTapAndDrag(container, 300f, 422f, 98f, 96f, SailToLanIsland);
                }
            }

            if (interactive)
            {
                var avImg = ui.Art(container, "map/avatar", 0f, 0f, 25f, 30f, true);
                mapAvatar = avImg.rectTransform;
                mapAvatar.pivot = new Vector2(0.5f, 0.5f);
            }
        }

        private void AttachMapTapAndDrag(Transform parent, float x, float y, float w, float h, Action onTap)
        {
            var hit = GameUi.Fill(parent, Color.clear, x, y, w, h, true);
            var trigger = hit.gameObject.AddComponent<EventTrigger>();

            void Add(EventTriggerType type, Action<PointerEventData> cb)
            {
                var entry = new EventTrigger.Entry { eventID = type };
                entry.callback.AddListener(ev => cb((PointerEventData)ev));
                trigger.triggers.Add(entry);
            }

            bool dragStarted = false;
            Add(EventTriggerType.PointerDown, _ => dragStarted = false);
            Add(EventTriggerType.BeginDrag, pe => { dragStarted = true; mapDrag?.OnBeginDrag(pe); });
            Add(EventTriggerType.Drag, pe => mapDrag?.OnDrag(pe));
            Add(EventTriggerType.EndDrag, pe => mapDrag?.OnEndDrag(pe));
            Add(EventTriggerType.PointerClick, pe =>
            {
                if (overlay != null || dragStarted || pe.dragging || Vector2.Distance(pe.pressPosition, pe.position) > 10f)
                {
                    dragStarted = false;
                    return;
                }
                onTap?.Invoke();
            });
        }

        private void DrawMapTopLeftControls()
        {
            freeHealBox = GameUi.Box("FreeHeal", page, 0f, 0f, GameUi.Width, 60f);
            int maxHp = data.battleData.maxHP;
            if (save.Team.Any(m => m.Hp < maxHp))
            {
                ui.ArtButton(freeHealBox, "team/btn_heal_small_kit", 6f, 4f, 80f, 40f, () =>
                {
                    flow.HealAll();
                    PersistThen(() =>
                    {
                        ui.Sound("use_healing_kit", save);
                        if (freeHealBox != null) GameUi.Clear(freeHealBox);
                    });
                }, true, 1f, "FreeHealBtn");
                fonts.Draw(freeHealBox, "white_stroked_small", "FREE", 46f, 36f, 7f, "center");
            }

            const float ex = 6f, ey = 48f, ew = 122f, eh = 30f;
            var vec = VectorCanvas.Create("StarterEventBtnBg", page, ex, ey, ew, eh);
            vec.FillRoundRect(0f, 0f, ew, eh, 6f, GameUi.HexColor("#362155"));
            vec.StrokeRect(1f, 1f, ew - 2f, eh - 2f, GameUi.HexColor("#e8b35a"), 1f);
            vec.FillRoundRect(97f, 5f, 21f, 20f, 5f, GameUi.HexColor("#78512a"));
            vec.Commit();

            ui.Art(page, GameUi.Item("starter_ticket"), ex + 5f, ey + 5f, 20f, 20f, true);
            fonts.Draw(page, "white_stroked_small", "STARTER EVENT", ex + 63f, ey + 10f, 8f, "center");
            mapTicketBadge = GameUi.Box("TicketCountBadge", page, ex + 97f, ey, 21f, eh);
            RefreshTickets();
            ui.HitBox(page, ex, ey, ew, eh, OpenStarterEventModal);
        }

        private void DrawMapBottomDock()
        {
            ui.ArtButton(page, "map/btn_shop", 10f, GameUi.Height - 5f - 65f, 60f, 65f, () =>
            {
                if (traveling) return;
                ui.Sound("button_click", save);
                ShowShop();
            }, true, 1f, "ShopBtn");
            ui.ArtButton(page, "map/btn_items", GameUi.Width - 10f - 120f, GameUi.Height - 5f - 65f, 60f, 65f, () =>
            {
                if (traveling) return;
                ui.Sound("button_click", save);
                ShowInventory();
            }, true, 1f, "ItemsBtn");
            ui.ArtButton(page, "map/btn_team", GameUi.Width - 5f - 60f, GameUi.Height - 5f - 65f, 60f, 65f, () =>
            {
                if (traveling) return;
                ui.Sound("button_click", save);
                ShowTeam();
            }, true, 1f, "TeamBtn");
        }

        private void RefreshLanIslandHud()
        {
            if (lanNpcMarkerBox != null) lanNpcMarkerBox.gameObject.SetActive(!onLanIsland);
            if (lanHudLayer == null) return;
            GameUi.Clear(lanHudLayer);
            if (!onLanIsland || overlay != null) return;

            const float px = 110f, py = GameUi.Height - 54f, pw = 100f, ph = 38f;
            var pill = VectorCanvas.Create("LanVsHudPill", lanHudLayer, px, py, pw, ph);
            pill.FillRoundRect(0f, 0f, pw, ph, 10f, GameUi.HexColor("#ffe259"));
            pill.FillRoundRect(2f, 2f, pw - 4f, ph - 4f, 8f, GameUi.HexColor("#2b7cff"));
            pill.FillRoundRect(4f, 4f, pw - 8f, 11f, 5f, new Color(1f, 1f, 1f, 0.22f));
            pill.Commit();

            fonts.Draw(lanHudLayer, "white_stroked_big", "LAN VS", px + pw * 0.5f, py + 12f, 13f, "center");
            ui.HitBox(lanHudLayer, px, py, pw, ph, () =>
            {
                if (traveling || overlay != null || !onLanIsland) return;
                ui.Sound("button_click", save);
                OpenLanLobby();
            });
        }

        private void RefreshTickets()
        {
            if (mapTicketBadge != null)
            {
                GameUi.Clear(mapTicketBadge);
                int t = save.Event.Tickets;
                fonts.Draw(mapTicketBadge, "white_stroked_small", t > 99 ? "99..." : t.ToString(), 10.5f, 10f, 8f, "center");
            }
            RefreshStarterModalDynamic();
        }

        private static bool IsArenaNode(MapNode node) => node != null && (node.arenaBattle || (node.arenaDialogs != null && !string.IsNullOrEmpty(node.arenaDialogs.dialog_start)));

        private Vector2 NodeAvatarPos(MapNode node, Dictionary<int, Encounter> rolls)
        {
            bool actionable = rolls != null && rolls.TryGetValue(node.id, out var enc) && enc.Kind != "none";
            return new Vector2(node.pos[0] + (actionable ? 12f : 0f), node.pos[1] + (actionable ? 6f : 0f));
        }

        private void PlaceMapAvatar(float ax, float ay, float bob, float tiltDeg = 0f)
        {
            if (mapAvatar == null) return;
            mapAvatar.anchoredPosition = new Vector2(ax, -(ay + bob - 15f));
            mapAvatar.localEulerAngles = new Vector3(0f, 0f, -tiltDeg);
        }

        private void PlaceBoat(float bx, float by, float bob = 0f, float tiltDeg = 0f)
        {
            if (boatRect == null) return;
            boatRect.anchoredPosition = new Vector2(bx, -(by + bob));
            boatRect.localEulerAngles = new Vector3(0f, 0f, -tiltDeg);
        }

        private void DrawBoatWake(Vector2 boatPos, Vector2 dir, float elapsed)
        {
            if (boatWakeVec == null) return;
            boatWakeVec.Clear();
            Vector2 stern = boatPos - dir * 13f + new Vector2(0f, 6f);
            Vector2 perp = new Vector2(-dir.y, dir.x);
            for (int i = 0; i < 3; i++)
            {
                float phase = Mathf.Repeat(elapsed * 2.2f + i * 0.33f, 1f);
                Vector2 center = stern - dir * (phase * 18f);
                float rx = 4f + phase * 8.5f;
                float ry = rx * 0.44f;
                float alpha = (1f - phase) * 0.72f;
                boatWakeVec.StrokeCircle(center.x, center.y, rx, new Color(1f, 1f, 1f, alpha), 1.5f, 0f, Mathf.PI * 2f, 14, ry);

                float vSpread = 3f + phase * 6f;
                var vPts = new[]
                {
                    stern - dir * (2f + phase * 10f) + perp * vSpread,
                    stern - dir * (phase * 4f),
                    stern - dir * (2f + phase * 10f) - perp * vSpread,
                };
                boatWakeVec.StrokePoly(vPts, new Color(0.88f, 0.98f, 1f, alpha * 0.7f), 1.3f, false);
            }
            boatWakeVec.Commit();
        }

        private void ClearBoatWake()
        {
            if (boatWakeVec == null) return;
            boatWakeVec.Clear();
            boatWakeVec.Commit();
        }

        private void Pan(float position)
        {
            mapOffset = Mathf.Clamp(position, minOffset, 0f);
            if (mapLayer != null) mapLayer.anchoredPosition = new Vector2(mapOffset, 0f);
        }

        private int NearestUnlockedNodeId(IEnumerable<MapNode> unlocked)
        {
            int bestId = save.NodeId;
            float bestDist = float.MaxValue;
            foreach (var n in unlocked)
            {
                float d = Vector2.Distance(new Vector2(n.pos[0], n.pos[1]), avatarLogicalPos);
                if (d < bestDist) { bestDist = d; bestId = n.id; }
            }
            return bestId;
        }

        private int[] FindVisualPath(HashSet<int> unlocked, int fromId, int targetId)
        {
            var queue = new Queue<List<int>>();
            var seen = new HashSet<int> { fromId };
            queue.Enqueue(new List<int> { fromId });
            while (queue.Count > 0)
            {
                var path = queue.Dequeue();
                int cur = path[path.Count - 1];
                if (cur == targetId) return path.ToArray();
                var curNode = data.Node(cur);
                if (curNode == null) continue;
                foreach (var n in data.mapNodes)
                {
                    if (!unlocked.Contains(n.id)) continue;
                    if ((n.id == curNode.connect || n.connect == cur) && seen.Add(n.id))
                    {
                        var next = new List<int>(path) { n.id };
                        queue.Enqueue(next);
                    }
                }
            }
            return Array.Empty<int>();
        }

        private List<Vector2> BuildLandWaypoints(int[] path, Vector2 finalPos, bool includeFromNode, bool wasWalking)
        {
            var waypoints = new List<Vector2>();
            int startIdx = includeFromNode ? 0 : 1;
            for (int i = startIdx; i < path.Length; i++)
            {
                var wp = data.Node(path[i]);
                if (wp != null) waypoints.Add(i == path.Length - 1 ? finalPos : new Vector2(wp.pos[0], wp.pos[1]));
            }
            if (wasWalking && waypoints.Count >= 2)
            {
                float d0 = Vector2.Distance(waypoints[0], avatarLogicalPos);
                float d1 = Vector2.Distance(waypoints[1], avatarLogicalPos);
                float seg = Vector2.Distance(waypoints[1], waypoints[0]);
                if (d0 < 8f || d1 < seg) waypoints.RemoveAt(0);
            }
            if (waypoints.Count == 0) waypoints.Add(finalPos);
            return waypoints;
        }

        private void Travel(MapNode node, Encounter encounter)
        {
            if (overlay != null || traveling || flow.World.Path(save, node.id).Length == 0) return;
            var rolls = cachedMapRolls ?? flow.World.RollMap(save);
            if (encounter == null) rolls.TryGetValue(node.id, out encounter);
            encounter ??= new Encounter { Node = node };

            bool wasWalking = walking;
            if (walkRoutine != null) { StopCoroutine(walkRoutine); walkRoutine = null; }

            var unlockedList = flow.World.Available(save).ToArray();
            var unlocked = new HashSet<int>(unlockedList.Select(n => n.id)) { node.id };
            Vector2 finalPos = NodeAvatarPos(node, rolls);

            if (onLanIsland)
            {
                onLanIsland = false;
                RefreshLanIslandHud();
                unlocked.Add(1);
                int[] returnPath = FindVisualPath(unlocked, 1, node.id);
                var returnWaypoints = returnPath.Length > 0
                    ? BuildLandWaypoints(returnPath, finalPos, true, false)
                    : new List<Vector2> { finalPos };
                walkRoutine = StartCoroutine(SailFromIslandRoutine(node, encounter, finalPos, returnWaypoints));
                return;
            }

            int fromId = wasWalking ? NearestUnlockedNodeId(unlockedList) : save.NodeId;
            unlocked.Add(fromId);
            int[] path = FindVisualPath(unlocked, fromId, node.id);

            if ((!wasWalking && fromId == node.id) || Vector2.Distance(finalPos, avatarLogicalPos) < 2f || path.Length == 0)
            {
                ArriveAtNode(node, encounter, finalPos);
                return;
            }

            var waypoints = BuildLandWaypoints(path, finalPos, wasWalking && path.Length > 1, wasWalking);
            walkRoutine = StartCoroutine(Walk(node, encounter, finalPos, waypoints));
        }

        private void SailToLanIsland()
        {
            if (overlay != null || traveling || save.Team.Count == 0) return;
            if (onLanIsland)
            {
                ui.Sound("screen_open", save);
                OpenLanNpcModal();
                return;
            }

            bool wasWalking = walking;
            if (walkRoutine != null) { StopCoroutine(walkRoutine); walkRoutine = null; }
            walking = false;

            var unlockedList = flow.World.Available(save).ToArray();
            var unlocked = new HashSet<int>(unlockedList.Select(n => n.id)) { 1 };
            int fromId = wasWalking ? NearestUnlockedNodeId(unlockedList) : save.NodeId;
            unlocked.Add(fromId);

            int[] path = FindVisualPath(unlocked, fromId, 1);
            var node1 = data.Node(1);
            Vector2 node1Pos = node1 != null ? new Vector2(node1.pos[0], node1.pos[1]) : PierPos;
            var waypoints = new List<Vector2>();
            if (path.Length > 0 && (wasWalking || fromId != 1))
                waypoints = BuildLandWaypoints(path, node1Pos, wasWalking && path.Length > 1, wasWalking);

            Vector2 lastPos = waypoints.Count > 0 ? waypoints[waypoints.Count - 1] : avatarLogicalPos;
            if (Vector2.Distance(lastPos, PierPos) >= 2f)
                waypoints.Add(PierPos);

            walkRoutine = StartCoroutine(SailToIslandRoutine(waypoints));
        }

        private IEnumerator WalkAlongWaypoints(List<Vector2> waypoints)
        {
            float walkClock = 0f;
            for (int i = 0; i < waypoints.Count; i++)
            {
                Vector2 from = avatarLogicalPos, to = waypoints[i];
                float dist = Vector2.Distance(from, to);
                if (dist < 0.5f) continue;
                float dur = Mathf.Clamp(dist * 0.0045f, 0.08f, 0.36f);
                float elapsed = 0f;
                while (elapsed < dur)
                {
                    float dt = Time.unscaledDeltaTime;
                    elapsed += dt;
                    walkClock += dt;
                    float p = Mathf.Clamp01(elapsed / dur);
                    avatarLogicalPos = Vector2.Lerp(from, to, p);
                    float bob = -Mathf.Abs(Mathf.Sin(walkClock * 22f)) * 4f;
                    float tiltDeg = Mathf.Sin(walkClock * 22f) * 8.6f;
                    PlaceMapAvatar(avatarLogicalPos.x, avatarLogicalPos.y, bob, tiltDeg);
                    yield return null;
                }
            }
        }

        private IEnumerator AnimateBoatCrossing(Vector2 from, Vector2 to, float duration)
        {
            Vector2 delta = to - from;
            Vector2 dir = delta.sqrMagnitude > 1e-4f ? delta.normalized : Vector2.right;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float p = Mathf.Clamp01(elapsed / duration);
                float smooth = Mathf.SmoothStep(0f, 1f, p);
                Vector2 pos = Vector2.Lerp(from, to, smooth);
                float bob = Mathf.Sin(elapsed * 10f) * 2.4f;
                float tilt = Mathf.Sin(elapsed * 7.5f) * 5.5f;
                PlaceBoat(pos.x, pos.y, bob, tilt);
                DrawBoatWake(pos, dir, elapsed);
                yield return null;
            }
            ClearBoatWake();
            PlaceBoat(to.x, to.y, 0f, 0f);
        }

        private IEnumerator CrossByBoat(Vector2 from, Vector2 to, Vector2 disembarkPos)
        {
            ui.Sound("attack_bubble_burst", save);
            if (mapAvatar != null) mapAvatar.gameObject.SetActive(false);
            yield return AnimateBoatCrossing(from, to, BoatCrossingDuration);
            if (mapAvatar != null) mapAvatar.gameObject.SetActive(true);
            avatarLogicalPos = disembarkPos;
            PlaceMapAvatar(disembarkPos.x, disembarkPos.y, 0f, 0f);
        }

        private IEnumerator SailToIslandRoutine(List<Vector2> waypoints)
        {
            traveling = true;
            walking = true;
            if (waypoints.Count > 0)
                yield return WalkAlongWaypoints(waypoints);

            avatarLogicalPos = PierPos;
            PlaceMapAvatar(PierPos.x, PierPos.y, 0f, 0f);
            flow.MoveTo(1);
            Persist();

            yield return CrossByBoat(PierBoatPos, IslandBoatPos, LanIslandAvatarPos);

            onLanIsland = true;
            traveling = false;
            walking = false;
            walkRoutine = null;
            RefreshLanIslandHud();
            ui.Sound("screen_open", save);
            OpenLanNpcModal();
        }

        private IEnumerator SailFromIslandRoutine(MapNode node, Encounter encounter, Vector2 finalPos, List<Vector2> waypoints)
        {
            traveling = true;
            walking = true;

            yield return CrossByBoat(IslandBoatPos, PierBoatPos, PierPos);

            traveling = false;
            yield return WalkAlongWaypoints(waypoints);
            walkRoutine = null;
            ArriveAtNode(node, encounter, finalPos);
        }

        private IEnumerator Walk(MapNode node, Encounter encounter, Vector2 finalPos, List<Vector2> waypoints)
        {
            walking = true;
            yield return WalkAlongWaypoints(waypoints);
            walkRoutine = null;
            ArriveAtNode(node, encounter, finalPos);
        }

        private void ArriveAtNode(MapNode node, Encounter encounter, Vector2 finalPos)
        {
            walking = false;
            avatarLogicalPos = finalPos;
            PlaceMapAvatar(finalPos.x, finalPos.y, 0f, 0f);
            if (!flow.MoveTo(node.id)) return;
            Persist();
            if (encounter.Kind == "none" || encounter.Members == null || encounter.Members.Length == 0) return;
            if (encounter.Kind == "wild") { BeginBattle(encounter); return; }
            ui.Sound("screen_open", save);
            OpenEnemyModal(node, encounter);
        }

        private void OpenLanNpcModal()
        {
            OpenOverlayShell(0.55f);
            const float w = 276f, h = 230f;
            float bx = (GameUi.Width - w) * 0.5f, by = (GameUi.Height - h) * 0.5f;

            var card = VectorCanvas.Create("LanNpcCard", overlay, 0f, 0f, GameUi.Width, GameUi.Height);
            card.FillRoundRect(bx, by, w, h, 14f, GameUi.HexColor("#e8b35a"));
            card.FillRoundRect(bx + 3f, by + 3f, w - 6f, h - 6f, 12f, GameUi.HexColor("#231a38"));
            card.FillRoundRect(bx + 14f, by + 14f, w - 28f, 74f, 10f, GameUi.HexColor("#312554"));
            card.FillCircle(bx + 48f, by + 51f, 30f, GameUi.HexColor("#2b7cff", 0.35f), GameUi.HexColor("#2b7cff", 0f), 24);
            card.FillRoundRect(bx + 84f, by + 56f, 92f, 18f, 6f, GameUi.HexColor("#2b7cff"));
            card.FillRoundRect(bx + 18f, by + 98f, w - 36f, 60f, 10f, GameUi.HexColor("#f5f2ff"));
            card.StrokeRoundRect(bx + 18f, by + 98f, w - 36f, 60f, 10f, GameUi.HexColor("#8f7ec4"), 1.5f);

            const float btnW = 148f, btnH = 40f;
            float btnX = bx + (w - btnW) * 0.5f, btnY = by + h - 56f;
            card.FillRoundRect(btnX - 2f, btnY - 2f, btnW + 4f, btnH + 4f, 11f, GameUi.HexColor("#7be0ff"));
            card.FillRoundRect(btnX, btnY, btnW, btnH, 10f, GameUi.HexColor("#2b7cff"));
            card.FillRoundRect(btnX + 3f, btnY + 3f, btnW - 6f, 12f, 6f, new Color(1f, 1f, 1f, 0.22f));
            card.Commit();

            ui.Art(overlay, "general/enemies/lan_npc_icon", bx + 20f, by + 23f, 56f, 56f, true);
            fonts.Draw(overlay, "white_stroked_big", "ISLAND DUELIST", bx + 84f, by + 32f, 14.5f, "left", null, 166f);
            fonts.Draw(overlay, "white_stroked_small", "LOCAL WI-FI", bx + 130f, by + 60f, 8f, "center");
            fonts.DrawLines(overlay, "black_small", "DUEL TRAINERS ON YOUR WI-FI!", bx + w * 0.5f, by + 114f, 11f, w - 58f, 15f, "center");
            fonts.Draw(overlay, "white_stroked_big", "LAN VS", bx + w * 0.5f, btnY + 11f, 15f, "center");

            ui.HitBox(overlay, btnX, btnY, btnW, btnH, () =>
            {
                ui.Sound("button_click", save);
                CloseOverlay();
                OpenLanLobby();
            });

            ui.ArtButton(overlay, "general/btn_close", bx + w - 24f, by - 14f, 40f, 40f, () =>
            {
                ui.Sound("screen_close", save);
                CloseOverlay();
            }, true, 1f, "LanModalCloseBtn");
        }

        private void OpenEnemyModal(MapNode node, Encounter encounter)
        {
            OpenOverlayShell(0.55f);
            string kind = encounter.Kind;
            string modalKind = kind == "training" ? "arena" : kind;
            float w = 285f, h = modalKind == "arena" ? 355f : modalKind == "boss" ? 275f : 185f;
            float bgX = (GameUi.Width - w) * 0.5f, bgY = (GameUi.Height - h) * 0.5f;

            ui.Art(overlay, $"map/modals/enemy/bg_{modalKind}", bgX, bgY, w, h);
            VectorCanvas modalVec = null;
            if (kind == "training")
            {
                modalVec = VectorCanvas.Create("TrainingMasks", overlay, 0f, 0f, GameUi.Width, GameUi.Height);
                modalVec.FillRoundRect(bgX + 44f, bgY + 5f, 197f, 29f, 6f, GameUi.HexColor("#e04e00"));
                modalVec.FillRoundRect(bgX + 118f, bgY + 48f, 134f, 34f, 8f, GameUi.HexColor("#ffa812"));
                modalVec.FillRoundRect(bgX + 120f, bgY + 53f, 130f, 26f, 8f, GameUi.HexColor("#c92323"));
            }

            if (kind == "captain" || kind == "boss")
            {
                HudWidgets.DrawCaptainHud(ui, fonts, overlay, encounter.Captain, encounter.Members.Length, encounter.Members.Length, bgX + 75f, bgY + 46f);
                if (kind == "captain")
                {
                    string levels = string.Join(" / ", encounter.Members.Select(m => $"LV {m.Level}"));
                    fonts.Draw(overlay, "black_small", levels, bgX + 170f, bgY + 91f, 9f, "center");
                }
            }
            else if (kind == "arena" || kind == "training")
            {
                if (kind == "training") fonts.Draw(overlay, "white_stroked_big", "TRAINING BATTLE", bgX + w * 0.5f, bgY + 13f, 14f, "center");
                ui.Art(overlay, $"general/enemies/{encounter.Captain}_icon", bgX + 61f, bgY + 45f, 50f, 50f, true);
                string capUpper = (encounter.Captain ?? string.Empty).ToUpperInvariant();
                if (kind == "training") fonts.Draw(overlay, "white_stroked_big", capUpper, bgX + 185f, bgY + 58f, 15f, "center");
                else fonts.Draw(overlay, "white_stroked_big", capUpper, bgX + 178f, bgY + 68f, 16f, "center");

                int count = Math.Min(3, encounter.Members.Length);
                float totalW = count * 65f + Math.Max(0, count - 1) * 7f;
                float startX = GameUi.Width * 0.5f - totalW * 0.5f;
                int defaultHp = data.battleData.maxHP;
                for (int i = 0; i < count; i++)
                {
                    var spec = encounter.Members[i];
                    var monView = new MonHudView
                    {
                        MonId = spec.MonId,
                        Level = spec.Level,
                        MaxHp = spec.MaxHp > 0 ? spec.MaxHp : defaultHp,
                        Hp = spec.MaxHp > 0 ? spec.MaxHp : defaultHp,
                    };
                    HudWidgets.DrawMonPane(ui, fonts, data, overlay, monView, "mini", startX + i * 72f, bgY + 160f);
                }
                if (kind == "training" && count > 0)
                {
                    int trainingHp = encounter.Members[0].MaxHp > 0 ? encounter.Members[0].MaxHp : defaultHp;
                    fonts.Draw(overlay, "black_small", $"{trainingHp} HP / DYNAMON", bgX + 185f, bgY + 89f, 10f, "center");
                }
            }

            if (encounter.EventNpc)
            {
                int drops = save.Event.NpcDrops;
                string evLabel = drops >= 3 ? "DAILY NPC TICKETS FULL" : $"EVENT NPC: 20% TICKET DROP ({drops}/3)";
                fonts.Draw(overlay, "white_stroked_small", evLabel, GameUi.Width * 0.5f, bgY + 154f, 9f, "center");
            }

            if (!string.IsNullOrEmpty(encounter.Taunt))
            {
                float tauntY = kind == "training" ? bgY + 112f : bgY + 118f;
                fonts.DrawLines(overlay, "black_small", encounter.Taunt, bgX + 28f, tauntY, 10f, w - 56f, 13f, "left");
            }

            if ((kind == "boss" || kind == "arena" || kind == "training") && encounter.Rewards != null && encounter.Rewards.Length > 0)
            {
                var list = encounter.Rewards.Take(4).ToArray();
                float totalW = list.Length * 55f + Math.Max(0, list.Length - 1) * 2f;
                float startX = GameUi.Width * 0.5f - totalW * 0.5f;
                float rowY = bgY + h - 93f;
                for (int i = 0; i < list.Length; i++)
                {
                    var rw = list[i];
                    float sx = startX + i * 57f;
                    ui.Art(overlay, "general/reward_slot", sx, rowY, 55f, 55f);
                    ui.Art(overlay, GameUi.Item(rw.id), sx + 9f, rowY + 6f, 37f, 37f, true);
                    fonts.Draw(overlay, "white_stroked_small", rw.id == "coins" ? $"${rw.amount}" : rw.amount.ToString(), sx + 27.5f, rowY + 42f, 9f, "center");
                }
            }

            if (kind == "training")
            {
                if (modalVec == null) modalVec = VectorCanvas.Create("TrainingTierVec", overlay, 0f, 0f, GameUi.Width, GameUi.Height);
                var tiers = new[] { ("easy", "EASY"), ("normal", "NORMAL"), ("hard", "HARD") };
                string activeTier = string.IsNullOrEmpty(encounter.Tier) ? "easy" : encounter.Tier;
                for (int i = 0; i < tiers.Length; i++)
                {
                    var (tid, tlabel) = tiers[i];
                    bool active = activeTier == tid;
                    float bx = bgX + 28f + i * 79f, by = bgY + 132f;
                    modalVec.FillRect(bx, by, 71f, 22f, GameUi.HexColor(active ? "#e04e00" : "#2a223d"));
                    modalVec.StrokeRect(bx, by, 71f, 22f, GameUi.HexColor(active ? "#ffd447" : "#6b5e8a"), 1.5f);
                    fonts.Draw(overlay, "white_stroked_small", tlabel, bx + 35.5f, by + 6.5f, 9f, "center");
                    string nextTier = tid;
                    ui.HitBox(overlay, bx, by, 71f, 22f, () =>
                    {
                        if (activeTier == nextTier) return;
                        ui.Sound("cool_button", save);
                        OpenEnemyModal(node, flow.World.Training(nextTier));
                    });
                }
            }

            modalVec?.Commit();

            ui.ArtButton(overlay, "map/modals/enemy/btn_fight", bgX + w * 0.5f - 52.5f, bgY + h - 24.5f, 105f, 45f, () =>
            {
                ui.Sound("button_click", save);
                CloseOverlay();
                BeginBattle(encounter);
            }, true, 1f, "ModalFightBtn");

            ui.ArtButton(overlay, "general/btn_close", bgX + w - 26f, bgY - 14f, 40f, 40f, () =>
            {
                ui.Sound("screen_close", save);
                CloseOverlay();
            }, true, 1f, "ModalCloseBtn");
        }
    }
}
