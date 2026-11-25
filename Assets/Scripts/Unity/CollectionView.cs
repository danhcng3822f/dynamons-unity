using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Dynamons
{
    public sealed partial class GameRoot
    {
        private sealed class LevelUpPayload
        {
            public string MonId, StoneId;
            public int OldLevel, NewLevel;
            public Stats OldStats, NewStats;
            public MoveData[] NewAbilities;
        }

        private const float TeamDropY = 312f;

        private int teamPage, teamSelectedSkill, shopSelected, resultRewardPage, resultMonIdx, resultSuperlativeIdx = 1;
        private int? teamDetailIdx;
        private bool teamReleaseConfirm, teamDragActive, teamDragIsParty, shopLootOpen, resultActive;
        private int teamDragIdx = -1, teamDestPartyIdx = -1, teamDestBenchIdx = -1;
        private Vector2 teamDragPos;

        private RectTransform teamSlotsVisual, teamFreeHealLayer, teamDragGhostLayer;
        private VectorCanvas teamDragCueVec;

        private bool levelUpActive, levelUpPlayedSfx, levelUpRevealFinalized, levelUpSkipShown, levelUpButtonShown;
        private float levelUpElapsedMs;
        private LevelUpPayload levelUpPayload;
        private Action levelUpOnDone;
        private RectTransform levelUpHost, levelUpVisualHost, levelUpControlsHost;

        private Reward[] shopLastLoot = Array.Empty<Reward>();
        private Product shopLastProduct;

        private Reward[] resultLoot = Array.Empty<Reward>();
        private bool resultSaved = true, resultXpRevealDone, pvpResultActive, pvpResultShown;
        private string pvpResultOutcome = "win", pvpResultStatusNote = string.Empty;
        private int pvpResultCoins;
        private float resultElapsedMs;
        private List<(int teamIndex, CombatantView fallbackMon)> resultMonQueue = new List<(int, CombatantView)>();
        private List<Reward> resultDisplayRewards = new List<Reward>();
        private bool resultHasPreLevel;

        private void ResetCollectionState()
        {
            teamDragActive = false;
            teamDragIdx = -1;
            teamDestPartyIdx = -1;
            teamDestBenchIdx = -1;
            teamSlotsVisual = null;
            teamFreeHealLayer = null;
            teamDragGhostLayer = null;
            teamDragCueVec = null;

            levelUpActive = false;
            levelUpRevealFinalized = false;
            levelUpSkipShown = false;
            levelUpButtonShown = false;
            levelUpPayload = null;
            levelUpOnDone = null;
            levelUpHost = null;
            levelUpVisualHost = null;
            levelUpControlsHost = null;

            shopLootOpen = false;
            resultActive = false;
            pvpResultActive = false;
        }

        private void UpdateCollectionAnimations()
        {
            if (levelUpActive && levelUpHost != null)
            {
                levelUpElapsedMs += Time.unscaledDeltaTime * 1000f;
                float stoneEnd = !string.IsNullOrEmpty(levelUpPayload?.StoneId) ? 380f : 0f;
                float fadeEnd = stoneEnd + 1780f;
                if (!levelUpPlayedSfx && levelUpElapsedMs >= stoneEnd)
                {
                    levelUpPlayedSfx = true;
                    ui.Sound("level_up_animation", save);
                }
                if (!levelUpRevealFinalized)
                {
                    RenderLevelUpOverlay();
                    if (levelUpElapsedMs >= fadeEnd + 350f) levelUpRevealFinalized = true;
                }
            }

            if (teamDragActive && teamDragCueVec != null)
                DrawTeamDragCues();

            if (resultActive && !levelUpActive)
            {
                resultElapsedMs += Time.unscaledDeltaTime * 1000f;
                bool hasLeveledFx = resultHasPreLevel && battle?.Caught == null;
                if (hasLeveledFx && resultElapsedMs <= 1850f)
                {
                    RenderResultDynamicStage();
                }
                else if (!resultXpRevealDone)
                {
                    RenderResultDynamicStage();
                    if (resultElapsedMs >= 460f) resultXpRevealDone = true;
                }
            }
        }

        private string MatchingStoneFor(OwnedMon mon)
        {
            if (mon == null || mon.Level >= data.MaxLevel) return null;
            string kind = data.Mon(mon.MonId)?.kind;
            if (!string.IsNullOrEmpty(kind) && save.Count("levelup_" + kind) > 0) return "levelup_" + kind;
            if (save.Count("levelup_joker") > 0) return "levelup_joker";
            return null;
        }

        private bool TryTriggerStoneLevelUp(int teamIdx, string stoneId, Action onFinished)
        {
            if (teamIdx < 0 || teamIdx >= save.Team.Count || string.IsNullOrEmpty(stoneId)) return false;
            var mon = save.Team[teamIdx];
            int oldLevel = mon.Level;
            var oldStats = data.StatsAt(mon.MonId, oldLevel);
            var oldIds = new HashSet<string>(data.MovesAt(mon.MonId, oldLevel).Select(m => m.id));
            if (!flow.LevelUp(teamIdx, stoneId)) return false;
            Persist();
            int newLevel = mon.Level;
            var newStats = data.StatsAt(mon.MonId, newLevel);
            var unlocked = data.MovesAt(mon.MonId, newLevel).Where(m => !oldIds.Contains(m.id)).ToArray();
            StartLevelUpAnimation(new LevelUpPayload
            {
                MonId = mon.MonId,
                StoneId = stoneId,
                OldLevel = oldLevel,
                NewLevel = newLevel,
                OldStats = oldStats,
                NewStats = newStats,
                NewAbilities = unlocked,
            }, onFinished);
            return true;
        }

        private void StartLevelUpAnimation(LevelUpPayload payload, Action onDone)
        {
            if (page == null || payload == null) { onDone?.Invoke(); return; }
            if (levelUpHost != null) { levelUpHost.gameObject.SetActive(false); Destroy(levelUpHost.gameObject); }
            levelUpHost = GameUi.Box("LevelUpOverlay", page, 0f, 0f, GameUi.Width, GameUi.Height);
            levelUpVisualHost = GameUi.Box("LevelUpVisual", levelUpHost, 0f, 0f, GameUi.Width, GameUi.Height);
            levelUpControlsHost = GameUi.Box("LevelUpControls", levelUpHost, 0f, 0f, GameUi.Width, GameUi.Height);
            levelUpPayload = payload;
            levelUpOnDone = onDone;
            levelUpElapsedMs = 0f;
            levelUpPlayedSfx = false;
            levelUpRevealFinalized = false;
            levelUpSkipShown = false;
            levelUpButtonShown = false;
            levelUpActive = true;
            RenderLevelUpOverlay();
        }

        private void RenderLevelUpOverlay()
        {
            if (levelUpHost == null || levelUpVisualHost == null || levelUpControlsHost == null || levelUpPayload == null) return;
            GameUi.Clear(levelUpVisualHost);

            bool hasStone = !string.IsNullOrEmpty(levelUpPayload.StoneId);
            float stoneEnd = hasStone ? 380f : 0f;
            float startEnd = stoneEnd + 470f;
            float blastEnd = startEnd + 930f;
            float fadeEnd = blastEnd + 380f;

            string phase = levelUpElapsedMs < stoneEnd ? "stone" :
                levelUpElapsedMs < startEnd ? "start" :
                levelUpElapsedMs < blastEnd ? "blast" :
                levelUpElapsedMs < fadeEnd ? "fade" : "reveal";
            bool ready = phase == "reveal";

            float overlayAlpha = Mathf.Min(0.88f, (levelUpElapsedMs / 220f) * 0.88f);
            GameUi.Fill(levelUpVisualHost, new Color(0f, 0f, 0f, overlayAlpha), 0f, 0f, GameUi.Width, GameUi.Height, true);

            ui.Art(levelUpVisualHost, "general/pedestal", 44f, 198f, 232f, 141.5f, true);
            ui.Art(levelUpVisualHost, "session/mon_shadow", 102.5f, 228f, 115f, 28f, true, 0.65f);

            bool upgraded = levelUpElapsedMs >= startEnd + 450f;
            int displayLevel = upgraded ? levelUpPayload.NewLevel : levelUpPayload.OldLevel;
            float bounce = phase == "blast" ? Mathf.Sin(levelUpElapsedMs * 0.03f) * 3f : 0f;
            ui.Art(levelUpVisualHost, GameUi.Mon(levelUpPayload.MonId, "front"), 100f, 126f + bounce, 120f, 122f, true);

            if (phase == "stone" && hasStone)
            {
                float sp = Mathf.Clamp01(levelUpElapsedMs / stoneEnd);
                float sy = 310f - sp * 110f;
                float sz = 44f * (1f - sp * 0.35f);
                ui.Art(levelUpVisualHost, GameUi.Item(levelUpPayload.StoneId), GameUi.Width * 0.5f - sz * 0.5f, sy - sz * 0.5f, sz, sz, true, 1f - sp * 0.4f);
            }

            if (phase == "start")
            {
                float p = Mathf.Clamp01((levelUpElapsedMs - stoneEnd) / (startEnd - stoneEnd));
                BattleFxView.RenderLevelupBlastFrame(levelUpVisualHost, ui, "start", p, GameUi.Width * 0.5f, 254f, 1.12f, 0.96f);
            }
            else if (phase == "blast")
            {
                float p = Mathf.Clamp01((levelUpElapsedMs - startEnd) / (blastEnd - startEnd));
                BattleFxView.RenderLevelupBlastFrame(levelUpVisualHost, ui, "blast", p, GameUi.Width * 0.5f, 254f, 1.15f, 1f);
                if (p > 0.38f && p < 0.66f)
                {
                    float flash = Mathf.Sin(((p - 0.38f) / 0.28f) * Mathf.PI) * 0.65f;
                    GameUi.Fill(levelUpVisualHost, new Color(1f, 1f, 1f, flash), 0f, 0f, GameUi.Width, GameUi.Height);
                }
            }
            else if (phase == "fade")
            {
                float p = Mathf.Clamp01((levelUpElapsedMs - blastEnd) / (fadeEnd - blastEnd));
                BattleFxView.RenderLevelupBlastFrame(levelUpVisualHost, ui, "fade", p, GameUi.Width * 0.5f, 254f, 1.12f, 1f - p);
            }

            if (upgraded) fonts.Draw(levelUpVisualHost, "white_stroked_big", "LEVEL UP!", GameUi.Width * 0.5f, 34f, 24f, "center");

            if (phase == "fade" || phase == "reveal")
            {
                HudWidgets.DrawMonPane(ui, fonts, data, levelUpVisualHost, new MonHudView
                {
                    MonId = levelUpPayload.MonId, Level = displayLevel,
                    Hp = data.battleData.maxHP, MaxHp = data.battleData.maxHP,
                }, "tooltip", GameUi.Width * 0.5f - 50f, 74f, showHpBar: true);
                ui.Art(levelUpVisualHost, "general/badge_levelup", GameUi.Width * 0.5f + 38f, 70f, 26f, 26f, true);

                float revealP = ready ? Mathf.Clamp01((levelUpElapsedMs - fadeEnd) / 350f) : 0f;
                var curStats = new Stats
                {
                    atk = Mathf.RoundToInt(Mathf.Lerp(levelUpPayload.OldStats.atk, levelUpPayload.NewStats.atk, revealP)),
                    def = Mathf.RoundToInt(Mathf.Lerp(levelUpPayload.OldStats.def, levelUpPayload.NewStats.def, revealP)),
                    aim = Mathf.RoundToInt(Mathf.Lerp(levelUpPayload.OldStats.aim, levelUpPayload.NewStats.aim, revealP)),
                };
                HudWidgets.DrawStatsPane(ui, fonts, levelUpVisualHost, curStats, 78f, 344f);

                var newAbilities = levelUpPayload.NewAbilities ?? Array.Empty<MoveData>();
                if (newAbilities.Length > 0)
                {
                    fonts.Draw(levelUpVisualHost, "white_stroked_small", newAbilities.Length > 1 ? "NEW SKILLS!" : "NEW SKILL!", GameUi.Width * 0.5f, 282f, 11f, "center");
                    if (newAbilities.Length == 1)
                    {
                        var ab = newAbilities[0];
                        ui.Art(levelUpVisualHost, GameUi.Move(ab.id), GameUi.Width * 0.5f - 78f, 302f, 34f, 34f, true);
                        fonts.Draw(levelUpVisualHost, "white_stroked_small", (ab.title ?? ab.id).ToUpperInvariant(), GameUi.Width * 0.5f - 36f, 313f, 10f, "left", null, 155f);
                    }
                    else
                    {
                        var list = newAbilities.Take(3).ToArray();
                        int colW = Mathf.FloorToInt((GameUi.Width - 24f) / list.Length);
                        float startX = (GameUi.Width - colW * list.Length) * 0.5f;
                        for (int i = 0; i < list.Length; i++)
                        {
                            var ab = list[i];
                            float cx = startX + i * colW + colW * 0.5f;
                            ui.Art(levelUpVisualHost, GameUi.Move(ab.id), cx - 16f, 297f, 32f, 32f, true);
                            fonts.Draw(levelUpVisualHost, "white_stroked_small", (ab.title ?? ab.id).ToUpperInvariant(), cx, 331f, 7.5f, "center", null, colW - 4f);
                        }
                    }
                }
            }

            if (ready && !levelUpButtonShown)
            {
                levelUpButtonShown = true;
                GameUi.Clear(levelUpControlsHost);
                ui.ArtButton(levelUpControlsHost, "general/btn_cool", (GameUi.Width - 105f) * 0.5f, 474f, 105f, 45f, () =>
                {
                    ui.Sound("cool_button", save);
                    var cb = levelUpOnDone;
                    levelUpActive = false;
                    levelUpPayload = null;
                    levelUpOnDone = null;
                    if (levelUpHost != null) { levelUpHost.gameObject.SetActive(false); Destroy(levelUpHost.gameObject); }
                    levelUpHost = null;
                    levelUpVisualHost = null;
                    levelUpControlsHost = null;
                    cb?.Invoke();
                });
            }
            else if (!ready && levelUpElapsedMs >= 900f && !levelUpSkipShown)
            {
                levelUpSkipShown = true;
                GameUi.Clear(levelUpControlsHost);
                ui.HitBox(levelUpControlsHost, 0f, 0f, GameUi.Width, GameUi.Height, () =>
                {
                    levelUpElapsedMs = fadeEnd + 400f;
                    levelUpRevealFinalized = true;
                    RenderLevelUpOverlay();
                });
            }
        }

        private void ShowTeam() => OpenTeam(0, null);

        private void OpenTeam(int pageIndex, int? detailIdx = null)
        {
            teamPage = pageIndex;
            teamDetailIdx = detailIdx;
            teamSelectedSkill = 0;
            teamReleaseConfirm = false;
            RenderTeamView();
        }

        private static Vector2[] PartySlotPositions(int count)
        {
            float startX = GameUi.Width * 0.5f - (Mathf.Max(1, count) - 1) * 51f;
            var pts = new Vector2[count];
            for (int i = 0; i < count; i++) pts[i] = new Vector2(startX + i * 102f, 262f);
            return pts;
        }

        private void RenderTeamView()
        {
            BeginPage(null); Track("music_map_loop");
            ui.Art(page, "team/bg_team", 0f, 0f, GameUi.Width, GameUi.Height);

            int maxHp = data.battleData.maxHP;
            int benchCount = Math.Max(0, save.Team.Count - 3);
            int totalPages = Math.Max(1, (benchCount + 5) / 6);
            teamPage = GameData.Clamp(teamPage, 0, totalPages - 1);
            if (teamDetailIdx.HasValue && (teamDetailIdx.Value < 0 || teamDetailIdx.Value >= save.Team.Count))
            { teamDetailIdx = null; teamReleaseConfirm = false; }

            if (teamDetailIdx.HasValue && teamReleaseConfirm && save.Team.Count > 1)
            {
                int idx = teamDetailIdx.Value;
                var mon = save.Team[idx];
                HudWidgets.DrawTopHud(ui, fonts, data, save, page, true);
                GameUi.Fill(page, new Color(0f, 0f, 0f, 0.70f), 0f, 0f, GameUi.Width, GameUi.Height, true);
                const float mx = (GameUi.Width - 300f) * 0.5f, my = (GameUi.Height - 160f) * 0.5f;
                ui.Art(page, "team/info/modal_release", mx, my, 300f, 160f);
                HudWidgets.DrawMonPane(ui, fonts, data, page, MonHudView.FromOwned(data, mon), "thumb", mx + 10f, my + 49f, showHpBar: false);
                ui.ArtButton(page, "general/btn_cancel", mx + 40f, my + 132f, 100f, 45f, () =>
                {
                    ui.Sound("button_click", save);
                    teamReleaseConfirm = false;
                    RenderTeamView();
                });
                ui.ArtButton(page, "team/btn_confirm_release", mx + 160f, my + 132f, 100f, 45f, () =>
                {
                    ui.Sound("cool_button", save);
                    if (flow.Release(idx)) Persist();
                    teamReleaseConfirm = false;
                    teamDetailIdx = null;
                    RenderTeamView();
                });
                ui.ArtButton(page, "general/btn_close", mx + 265f, my - 5f, 40f, 40f, () =>
                {
                    ui.Sound("screen_close", save);
                    teamReleaseConfirm = false;
                    RenderTeamView();
                });
                return;
            }

            if (teamDetailIdx.HasValue)
            {
                RenderMonInfoView(teamDetailIdx.Value, maxHp);
                return;
            }

            ui.ArtButton(page, "team/btn_back_map", 6f, 2.5f, 80f, 35f, () =>
            {
                ui.Sound("screen_close", save);
                ShowMap();
            });
            HudWidgets.DrawTopHud(ui, fonts, data, save, page, true);
            HudWidgets.DrawMuteButton(ui, page, save, musicTrack, () => Persist(), RenderTeamView);

            teamDragCueVec = VectorCanvas.Create("TeamDragCue", page, 0f, 0f, GameUi.Width, GameUi.Height);
            teamSlotsVisual = GameUi.Box("TeamSlotsVisual", page, 0f, 0f, GameUi.Width, GameUi.Height);
            var hitSlots = GameUi.Box("TeamHitSlots", page, 0f, 0f, GameUi.Width, GameUi.Height);
            teamFreeHealLayer = GameUi.Box("TeamFreeHeals", page, 0f, 0f, GameUi.Width, GameUi.Height);
            teamDragGhostLayer = GameUi.Box("TeamDragGhost", page, 0f, 0f, GameUi.Width, GameUi.Height);

            int partyCount = Math.Min(3, save.Team.Count);
            var partyPts = PartySlotPositions(partyCount);
            for (int i = 0; i < partyCount; i++)
            {
                int slotIdx = i;
                Vector2 pos = partyPts[i];
                AttachDragTarget(hitSlots, pos.x - 50f, 82f, 100f, 186f, slotIdx, true);
                if (save.Team[i].Hp < maxHp)
                {
                    ui.ArtButton(teamFreeHealLayer, "team/btn_heal_small_kit", pos.x - 40f, 262f, 80f, 40f, () => HealTeamSlot(slotIdx));
                    fonts.Draw(teamFreeHealLayer, "white_stroked_small", "FREE", pos.x, 294f, 7f, "center");
                }
            }

            int start = teamPage * 6;
            int shownCount = Math.Min(6, Math.Max(0, save.Team.Count - 3 - start));
            for (int i = 0; i < shownCount; i++)
            {
                int globalIdx = 3 + start + i;
                float sx = 30f + (i % 2) * 140f, sy = 327f + (i / 2) * 71f;
                AttachDragTarget(hitSlots, sx, sy, 120f, 65f, globalIdx, false);
            }

            const float arrowCenterY = 314f + 235f * 0.5f;
            if (teamPage > 0)
            {
                var prevBtn = ui.ArtButton(hitSlots, "general/btn_arrow_right", 2f, arrowCenterY - 47.5f, 35f, 95f, () =>
                {
                    ui.Sound("button_click", save);
                    teamPage--;
                    RenderTeamView();
                });
                var img = prevBtn.GetComponentInChildren<RawImage>();
                if (img != null) img.uvRect = new Rect(1f, 0f, -1f, 1f);
            }
            if (teamPage < totalPages - 1)
            {
                ui.ArtButton(hitSlots, "general/btn_arrow_right", GameUi.Width - 37f, arrowCenterY - 47.5f, 35f, 95f, () =>
                {
                    ui.Sound("button_click", save);
                    teamPage++;
                    RenderTeamView();
                });
            }

            RedrawTeamRosterVisuals();
        }

        private void HealTeamSlot(int index)
        {
            if (index < 0 || index >= save.Team.Count) return;
            save.Team[index].Hp = data.battleData.maxHP;
            Persist();
            ui.Sound("use_healing_kit", save);
            RenderTeamView();
        }

        private void AttachDragTarget(Transform parent, float x, float y, float w, float h, int rosterIndex, bool isParty)
        {
            var hit = GameUi.Fill(parent, Color.clear, x, y, w, h, true);
            var drag = hit.gameObject.AddComponent<TeamSlotDrag>();
            drag.Frame = ui.Frame;
            drag.RosterIndex = rosterIndex;
            drag.IsParty = isParty;
            drag.OnTap = idx =>
            {
                ui.Sound("button_click", save);
                teamDetailIdx = idx;
                teamSelectedSkill = 0;
                RenderTeamView();
            };
            drag.OnStartDrag = (idx, partyFlag, pos) =>
            {
                teamDragActive = true;
                teamDragIdx = idx;
                teamDragIsParty = partyFlag;
                teamDragPos = pos;
                UpdateDragTargets(pos);
                if (teamFreeHealLayer != null) teamFreeHealLayer.gameObject.SetActive(false);
                RedrawTeamRosterVisuals();
            };
            drag.OnDragMove = pos =>
            {
                teamDragPos = pos;
                UpdateDragTargets(pos);
                RedrawTeamRosterVisuals();
            };
            drag.OnFinishDrag = pos =>
            {
                UpdateDragTargets(pos);
                int targetIdx = teamDestPartyIdx != -1 ? teamDestPartyIdx : teamDestBenchIdx;
                int srcIdx = teamDragIdx;
                teamDragActive = false;
                teamDragIdx = -1;
                teamDestPartyIdx = -1;
                teamDestBenchIdx = -1;
                if (targetIdx != -1 && targetIdx != srcIdx && flow.Swap(srcIdx, targetIdx))
                {
                    Persist();
                    ui.Sound("cool_button", save);
                    RenderTeamView();
                }
                else
                {
                    if (teamFreeHealLayer != null) teamFreeHealLayer.gameObject.SetActive(true);
                    RedrawTeamRosterVisuals();
                }
            };
            drag.OnCancelDrag = () =>
            {
                teamDragActive = false;
                teamDragIdx = -1;
                teamDestPartyIdx = -1;
                teamDestBenchIdx = -1;
                if (teamFreeHealLayer != null) teamFreeHealLayer.gameObject.SetActive(true);
                RedrawTeamRosterVisuals();
            };
        }

        private void UpdateDragTargets(Vector2 pos)
        {
            int partyCount = Math.Min(3, save.Team.Count);
            if (partyCount > 0 && pos.y < TeamDropY)
            {
                float part = GameUi.Width / partyCount;
                int ind = GameData.Clamp(Mathf.FloorToInt(pos.x / part), 0, partyCount - 1);
                teamDestPartyIdx = ind == teamDragIdx ? -1 : ind;
                teamDestBenchIdx = -1;
                return;
            }
            teamDestPartyIdx = -1;
            teamDestBenchIdx = -1;
            if (!teamDragIsParty || pos.y < TeamDropY) return;
            int start = teamPage * 6;
            int shownCount = Math.Min(6, Math.Max(0, save.Team.Count - 3 - start));
            for (int i = 0; i < shownCount; i++)
            {
                float sx = 30f + (i % 2) * 140f, sy = 327f + (i / 2) * 71f;
                if (pos.x >= sx && pos.x <= sx + 120f && pos.y >= sy && pos.y <= sy + 65f)
                {
                    int globalIdx = 3 + start + i;
                    if (globalIdx != teamDragIdx) teamDestBenchIdx = globalIdx;
                    return;
                }
            }
        }

        private void DrawTeamDragCues()
        {
            if (teamDragCueVec == null) return;
            teamDragCueVec.Clear();
            if (!teamDragActive) { teamDragCueVec.Commit(); return; }

            float pulse = 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 8f);
            int partyCount = Math.Min(3, save.Team.Count);
            var partyPts = PartySlotPositions(partyCount);
            Color cueHex = GameUi.HexColor("#ffe259");

            for (int i = 0; i < partyCount; i++)
            {
                Vector2 pos = partyPts[i];
                bool drawCue = (teamDragIsParty && teamDragIdx == i && teamDestPartyIdx == -1) ||
                               (teamDragIsParty && teamDestPartyIdx == i) ||
                               (!teamDragIsParty);
                if (!drawCue) continue;
                bool active = teamDestPartyIdx == i;
                teamDragCueVec.FillRoundRect(pos.x - 48f, pos.y - 112f, 96f, 122f, 14f,
                    new Color(cueHex.r, cueHex.g, cueHex.b, active ? 0.2f * pulse : 0.08f));
                teamDragCueVec.StrokeRect(pos.x - 48f, pos.y - 112f, 96f, 122f,
                    new Color(cueHex.r, cueHex.g, cueHex.b, active ? 0.95f : 0.45f), active ? 2.5f : 1.5f);
            }

            if (teamDragIsParty && teamDestBenchIdx != -1)
            {
                int local = teamDestBenchIdx - (3 + teamPage * 6);
                if (local >= 0 && local < 6)
                {
                    float sx = 30f + (local % 2) * 140f, sy = 327f + (local / 2) * 71f;
                    teamDragCueVec.StrokeRect(sx - 2f, sy - 2f, 124f, 69f, new Color(cueHex.r, cueHex.g, cueHex.b, 0.95f), 2.5f);
                }
            }
            teamDragCueVec.Commit();
        }

        private void RedrawTeamRosterVisuals()
        {
            if (teamSlotsVisual == null || teamDragGhostLayer == null) return;
            GameUi.Clear(teamSlotsVisual);
            GameUi.Clear(teamDragGhostLayer);
            DrawTeamDragCues();

            void DrawPartyCard(OwnedMon mon, Vector2 pos, float alpha)
            {
                ui.Art(teamSlotsVisual, "session/mon_shadow", pos.x - 42f, pos.y - 16f, 84f, 22f, true, alpha * 0.65f);
                ui.Art(teamSlotsVisual, GameUi.Mon(mon.MonId, "front"), pos.x - 44f, pos.y - 106f, 88f, 106f, true, alpha);
                HudWidgets.DrawMonPane(ui, fonts, data, teamSlotsVisual, MonHudView.FromOwned(data, mon), "tooltip", pos.x - 50f, 82f, alpha: alpha);
            }

            int partyCount = Math.Min(3, save.Team.Count);
            var partyPts = PartySlotPositions(partyCount);
            for (int i = 0; i < partyCount; i++)
            {
                Vector2 pos = partyPts[i];
                var mon = save.Team[i];
                if (teamDragActive && teamDragIsParty && teamDragIdx == i)
                {
                    if (teamDestPartyIdx != -1 && teamDestPartyIdx < partyCount) DrawPartyCard(save.Team[teamDestPartyIdx], pos, 0.85f);
                    continue;
                }
                if (teamDragActive && teamDragIsParty && teamDestPartyIdx == i) continue;
                if (teamDragActive && !teamDragIsParty)
                {
                    DrawPartyCard(mon, pos, teamDestPartyIdx == i ? 0.28f : 1f);
                    continue;
                }
                DrawPartyCard(mon, pos, 1f);
            }

            ui.Art(teamSlotsVisual, "team/bg_team_slots", 20f, 314f, 280f, 235f);
            int start = teamPage * 6;
            int benchRemain = Math.Max(0, save.Team.Count - 3 - start);
            for (int i = 0; i < 6; i++)
            {
                float sx = 30f + (i % 2) * 140f, sy = 327f + (i / 2) * 71f;
                int globalIdx = 3 + start + i;
                if (i < benchRemain)
                {
                    if (teamDragActive && !teamDragIsParty && teamDragIdx == globalIdx)
                        ui.Art(teamSlotsVisual, "team/team_slot", sx, sy, 120f, 65f);
                    else
                    {
                        float alpha = (teamDragActive && teamDragIsParty && teamDestBenchIdx == globalIdx) ? 0.35f : 1f;
                        HudWidgets.DrawMonPane(ui, fonts, data, teamSlotsVisual, MonHudView.FromOwned(data, save.Team[globalIdx]), "thumb", sx, sy, alpha: alpha);
                    }
                }
                else ui.Art(teamSlotsVisual, "team/team_slot", sx, sy, 120f, 65f);
            }

            if (teamDragActive && teamDragIdx >= 0 && teamDragIdx < save.Team.Count)
            {
                var dragged = save.Team[teamDragIdx];
                if (teamDragPos.y < TeamDropY)
                {
                    ui.Art(teamDragGhostLayer, "session/mon_shadow", teamDragPos.x - 44f, teamDragPos.y + 28f, 88f, 22f, true, 0.5f);
                    ui.Art(teamDragGhostLayer, GameUi.Mon(dragged.MonId, "front"), teamDragPos.x - 47f, teamDragPos.y - 62f, 94f, 112f, true);
                    float tipX = Mathf.Clamp(teamDragPos.x - 50f, 4f, GameUi.Width - 104f);
                    float tipY = Mathf.Max(40f, teamDragPos.y - 122f);
                    HudWidgets.DrawMonPane(ui, fonts, data, teamDragGhostLayer, MonHudView.FromOwned(data, dragged), "tooltip", tipX, tipY);
                }
                else
                {
                    float bx = Mathf.Clamp(teamDragPos.x - 60f, 4f, GameUi.Width - 124f);
                    float by = Mathf.Clamp(teamDragPos.y - 34f, TeamDropY - 12f, GameUi.Height - 69f);
                    var shadowVec = VectorCanvas.Create("GhostShadow", teamDragGhostLayer, 0f, 0f, GameUi.Width, GameUi.Height);
                    shadowVec.FillRoundRect(bx + 3f, by + 5f, 120f, 65f, 8f, new Color(0f, 0f, 0f, 0.35f));
                    shadowVec.Commit();
                    HudWidgets.DrawMonPane(ui, fonts, data, teamDragGhostLayer, MonHudView.FromOwned(data, dragged), "thumb", bx, by);
                }
            }
        }

        private void RenderMonInfoView(int idx, int maxHp)
        {
            var mon = save.Team[idx];
            var info = data.Mon(mon.MonId);
            var stats = data.StatsAt(mon.MonId, mon.Level);

            ui.ArtButton(page, "team/btn_back_all", 6f, 2.5f, 80f, 35f, () =>
            {
                ui.Sound("button_click", save);
                teamDetailIdx = null;
                RenderTeamView();
            });
            HudWidgets.DrawTopHud(ui, fonts, data, save, page, true);
            HudWidgets.DrawMuteButton(ui, page, save, musicTrack, () => Persist(), RenderTeamView);

            HudWidgets.DrawMonPane(ui, fonts, data, page, MonHudView.FromOwned(data, mon), "tooltip", 30f, 74f);
            fonts.DrawLines(page, "black_small", info?.desc ?? string.Empty, 80f, 146f, 8.5f, 134f, 12f, "center");

            int needXp = flow.NeededXp(mon);
            float xpRatio = mon.Level >= data.MaxLevel ? 1f : (needXp > 0 ? Mathf.Clamp01(mon.Xp / (float)needXp) : 1f);
            HudWidgets.DrawClippedBar(ui, page, "general/xpbar_bg", "general/xpbar_fill", 12f, 224f, 135f, 25f, xpRatio, 0.04f, 0.96f);
            if (xpRatio >= 1f && mon.Level < data.MaxLevel)
                ui.Art(page, "general/badge_levelup", 132f, 226f, 22f, 22f, true);

            ui.Art(page, "session/mon_shadow", 174f, 240f, 110f, 26f, true, 0.65f);
            ui.Art(page, GameUi.Mon(mon.MonId, "front"), 166f, 116f, 126f, 142f, true);

            if (GameData.HasPassive(info?.passive))
            {
                var p = info.passive;
                string pKey = GameData.PassiveKey(p);
                var (pBg, pAccent, pBorder) = HudWidgets.PassivePalette(pKey, false, pKey == "charge");
                const float cardX = 10f, cardY = 322f, cardW = 300f, cardH = 54f;
                var pVec = VectorCanvas.Create("MonPassiveCardVec", page, cardX, cardY, cardW, cardH);
                pVec.FillRoundRect(0f, 0f, cardW, cardH, 10f, pBg);
                pVec.FillRect(4f, 2f, cardW - 8f, 3f, new Color(pAccent.r, pAccent.g, pAccent.b, 0.35f));
                pVec.StrokeRoundRect(1f, 1f, cardW - 2f, cardH - 2f, 9f, pBorder, 1.8f);
                pVec.FillCircle(26f, cardH * 0.5f, 15f, GameUi.HexColor("#0c081c", 0.65f));
                pVec.StrokeCircle(26f, cardH * 0.5f, 15f, new Color(pAccent.r, pAccent.g, pAccent.b, 0.75f), 1.4f, 0f, Mathf.PI * 2f, 16);
                HudWidgets.DrawPassiveEmblem(pVec, pKey, 26f, cardH * 0.5f, 8.5f, pAccent, pBg);
                pVec.Commit();

                fonts.Draw(page, "white_stroked_small", GameData.PassiveTitle(p), cardX + 48f, cardY + 8f, 9.5f, "left", pAccent, 185f);
                fonts.Draw(page, "white_stroked_small", "PASSIVE", cardX + cardW - 10f, cardY + 8f, 7.5f, "right", GameUi.HexColor("#e2dcff"));
                fonts.DrawLines(page, "white_small", GameData.PassiveDesc(p), cardX + 48f, cardY + 23f, 8f, cardW - 58f, 11f, "left");
            }

            HudWidgets.DrawStatsPane(ui, fonts, page, stats, 5f, 384f);

            var allSkills = (info?.abilities ?? Array.Empty<MoveSlot>()).Take(8).ToArray();
            bool compact = allSkills.Length > 6;
            int cols = compact ? 4 : 3;
            float size = compact ? 36f : 48f, gapX = compact ? 39f : 51f, gapY = compact ? 50f : 56f;
            float startX = compact ? 161f : 162f, startY = compact ? 390f : 388f, selPad = compact ? 2f : 2.5f;
            int lockFontSize = compact ? 8 : 9;
            float lockOffY = compact ? 21f : 28f;
            var skills = compact ? allSkills : allSkills.Take(6).ToArray();
            teamSelectedSkill = GameData.Clamp(teamSelectedSkill, 0, Math.Max(0, skills.Length - 1));

            for (int i = 0; i < skills.Length; i++)
            {
                int si = i;
                var entry = skills[i];
                float sx = startX + (i % cols) * gapX, sy = startY + (i / cols) * gapY;
                bool unlocked = Math.Max(1, entry.getsAt) <= mon.Level;
                ui.ArtButton(page, GameUi.Move(entry.id), sx, sy, size, size, () =>
                {
                    ui.Sound("button_click", save);
                    teamSelectedSkill = si;
                    RenderTeamView();
                }, true, unlocked ? 1f : 0.65f);
                if (!unlocked)
                {
                    ui.Art(page, "team/info/skill_lock_overlay", sx, sy, size, size);
                    fonts.Draw(page, "black_medium", $"#{entry.getsAt}", sx + size * 0.5f, sy + lockOffY, lockFontSize, "center");
                }
                if (i == teamSelectedSkill)
                    ui.Art(page, "team/info/skill_select", sx - selPad, sy - selPad, size + selPad * 2f, size + selPad * 2f);
            }

            ui.Art(page, "team/info/skill_description", 7.5f, 513f, 305f, 50f);
            if (skills.Length > 0)
            {
                var sel = skills[teamSelectedSkill];
                var ab = data.Move(sel.id);
                if (ab != null)
                {
                    int reqLvl = Math.Max(1, sel.getsAt);
                    string heading = reqLvl > mon.Level ? $"{(ab.title ?? ab.id).ToUpperInvariant()} (LV {reqLvl})" : (ab.title ?? ab.id).ToUpperInvariant();
                    fonts.Draw(page, "white_stroked_small", heading, GameUi.Width * 0.5f, 519f, 9f, "center");
                    fonts.DrawLines(page, "white_small", ab.desc ?? string.Empty, GameUi.Width * 0.5f, 536f, 8f, 280f, 11f, "center");
                }
            }

            bool hasRelease = idx >= 3 && save.Team.Count > 1;
            bool hasLead = idx > 0;
            bool hasLevelUp = needXp > 0 && mon.Xp >= needXp;
            bool hasHeal = mon.Hp < maxHp;
            bool allFour = hasRelease && hasLead && hasLevelUp && hasHeal;

            if (hasRelease)
            {
                float relW = allFour ? 70f : 75f;
                ui.ArtButton(page, "team/info/btn_release", 5f, 278f, relW, 35f, () =>
                {
                    ui.Sound("button_click", save);
                    teamReleaseConfirm = true;
                    RenderTeamView();
                });
            }

            if (hasLead)
            {
                float leadW = allFour ? 62f : 74f;
                float swapX = hasRelease ? (allFour ? 78f : 84f) : 6f;
                var leadVec = VectorCanvas.Create("SetLeadVec", page, swapX, 281f, leadW, 28f);
                leadVec.FillRoundRect(0f, 0f, leadW, 28f, 7f, GameUi.HexColor("#2c8e43"));
                leadVec.StrokeRect(1f, 1f, leadW - 2f, 26f, GameUi.HexColor("#9cf284"), 1.5f);
                leadVec.Commit();
                fonts.Draw(page, "white_stroked_small", "SET LEAD", swapX + leadW * 0.5f, 290f, allFour ? 7f : 8f, "center", null, leadW - 6f);
                ui.HitBox(page, swapX, 281f, leadW, 28f, () =>
                {
                    if (flow.Swap(idx, 0)) Persist();
                    ui.Sound("cool_button", save);
                    teamDetailIdx = null;
                    teamPage = 0;
                    RenderTeamView();
                });
            }

            float rightX = GameUi.Width - 5f;
            if (hasLevelUp)
            {
                string stone = MatchingStoneFor(mon);
                float lvlW = allFour ? 104f : 112f;
                rightX -= lvlW;
                bool hasStone = !string.IsNullOrEmpty(stone);
                float bx = rightX;
                ui.ArtButton(page, "general/btn_levelup", bx, 272f, lvlW, 45f, () =>
                {
                    TryTriggerStoneLevelUp(idx, stone, RenderTeamView);
                }, hasStone);
                if (hasStone)
                {
                    ui.Art(page, GameUi.Item(stone), bx + (allFour ? 6f : 8f), 280f, 28f, 28f, true);
                    ui.Art(page, "general/btn_levelup_text", bx + (allFour ? 33f : 36f), 284f, allFour ? 65f : 68f, 18f, true);
                }
                else
                {
                    fonts.Draw(page, "white_stroked_small", "NEED STONE", bx + lvlW * 0.5f, 288f, allFour ? 7.5f : 8f, "center", null, lvlW - 8f);
                }
                rightX -= allFour ? 4f : 5f;
            }

            if (hasHeal)
            {
                float healW = allFour ? 64f : 100f;
                rightX -= healW;
                float hx = rightX;
                ui.ArtButton(page, "team/info/btn_heal_kit", hx, 272f, healW, 45f, () => HealTeamSlot(idx));
                fonts.Draw(page, "white_stroked_small", "FREE", hx + healW * 0.5f, 308f, 7f, "center");
            }
        }

        private void ShowInventory()
        {
            BeginPage(null); Track("music_map_loop");
            RenderMapBackdrop(page);
            var modalHost = GameUi.Box("InventoryOverlay", page, 0f, 0f, GameUi.Width, GameUi.Height);
            OpenItemsOverlay(false, false, false, selection =>
            {
                if (selection == "healing_kit")
                {
                    if (flow.UseItem("healing_kit")) { Persist(); ui.Sound("use_healing_kit", save); }
                    ShowInventory();
                    return;
                }
                if (selection.StartsWith("levelup_", StringComparison.Ordinal))
                {
                    var parts = selection.Split(':');
                    string stoneId = parts[0];
                    if (parts.Length > 1 && int.TryParse(parts[1], out int targetIdx))
                    {
                        var mon = targetIdx >= 0 && targetIdx < save.Team.Count ? save.Team[targetIdx] : null;
                        if (mon != null)
                        {
                            int oldLevel = mon.Level;
                            var oldStats = data.StatsAt(mon.MonId, oldLevel);
                            var oldIds = new HashSet<string>(data.MovesAt(mon.MonId, oldLevel).Select(m => m.id));
                            if (flow.UseItem(stoneId, targetIdx))
                            {
                                Persist();
                                var payload = new LevelUpPayload
                                {
                                    MonId = mon.MonId, StoneId = stoneId, OldLevel = oldLevel, NewLevel = mon.Level,
                                    OldStats = oldStats, NewStats = data.StatsAt(mon.MonId, mon.Level),
                                    NewAbilities = data.MovesAt(mon.MonId, mon.Level).Where(m => !oldIds.Contains(m.id)).ToArray(),
                                };
                                OpenTeam(0, targetIdx);
                                StartLevelUpAnimation(payload, RenderTeamView);
                                return;
                            }
                        }
                    }
                    ShowInventory();
                }
            }, ShowMap, modalHost);
        }

        private void ShowShop()
        {
            shopSelected = 0;
            shopLootOpen = false;
            shopLastLoot = Array.Empty<Reward>();
            shopLastProduct = null;
            RenderShopView();
        }

        private void RenderShopView()
        {
            BeginPage(null); Track("music_map_loop");
            RenderMapBackdrop(page);
            GameUi.Fill(page, new Color(0f, 0f, 0f, 0.60f), 0f, 0f, GameUi.Width, GameUi.Height, true);

            const float bgX = 17.5f, bgY = 106.5f, bgW = 285f, bgH = 355f;
            ui.Art(page, "shop/bg_shop", bgX, bgY, bgW, bgH);

            if (!shopLootOpen)
            {
                HudWidgets.DrawTopHud(ui, fonts, data, save, page, false);
                HudWidgets.DrawMuteButton(ui, page, save, musicTrack, () => Persist(), RenderShopView);
                ui.ArtButton(page, "general/btn_close", bgX + bgW - 24f, bgY - 16f, 40f, 40f, () =>
                {
                    ui.Sound("screen_close", save);
                    ShowMap();
                });
            }

            fonts.DrawLines(page, "black_small", string.IsNullOrEmpty(data.shopMessage) ? "I wonder what you'll get!" : data.shopMessage,
                bgX + 195f, bgY + 60f, 9.5f, 136f, 13f, "center");

            var products = data.products ?? Array.Empty<Product>();
            shopSelected = GameData.Clamp(shopSelected, 0, Math.Max(0, products.Length - 1));
            for (int i = 0; i < products.Length; i++)
            {
                int idx = i;
                var prod = products[i];
                float cx = GameUi.Width * 0.5f - 65f + i * 130f, cy = bgY + bgH * 0.5f - 20f;
                if (!shopLootOpen)
                {
                    ui.ArtButton(page, "shop/product_slot", cx - 62.5f, cy - 20f, 125f, 100f, () =>
                    {
                        ui.Sound("button_click", save);
                        shopSelected = idx;
                        RenderShopView();
                    });
                    if (i == shopSelected) ui.Art(page, "shop/product_select", cx - 65f, cy - 22.5f, 130f, 105f);
                }
                else
                {
                    ui.Art(page, "shop/product_slot", cx - 62.5f, cy - 20f, 125f, 100f);
                }
                bool isChest = prod.id == "chest";
                ui.Art(page, isChest ? "shop/products/product_chest" : "shop/products/product_bag",
                    cx - (isChest ? 37.5f : 25f), cy - 30f, isChest ? 75f : 50f, 56f, true);
                fonts.Draw(page, "white_stroked_small", prod.title, cx, cy + 32f, 9.5f, "center");
                fonts.Draw(page, "white_stroked_small", $"${prod.price}", cx, cy + 52f, 10f, "center");
            }

            if (products.Length > 0)
            {
                var selected = products[shopSelected];
                string divider = selected.lootTitle == "contains" ? "contains" : "maycontain";
                float divW = divider == "contains" ? 60f : 80f;
                ui.Art(page, $"shop/divider_{divider}", (GameUi.Width - divW) * 0.5f, bgY + bgH - 108f, divW, 15f);

                var disp = selected.displayRewards ?? Array.Empty<DisplayReward>();
                float totalW = disp.Length * 57f - 2f;
                float startX = (GameUi.Width - totalW) * 0.5f;
                for (int i = 0; i < disp.Length; i++)
                {
                    var rw = disp[i];
                    float rx = startX + i * 57f, ry = bgY + bgH - 90f;
                    ui.Art(page, "general/reward_slot", rx, ry, 55f, 55f);
                    ui.Art(page, GameUi.Item(rw.icon), rx + 8f, ry + 6f, 39f, 39f, true);
                    if (!string.IsNullOrEmpty(rw.note))
                        fonts.Draw(page, "white_stroked_small", rw.note, rx + 27.5f, ry + 45f, 8f, "center");
                }

                if (!shopLootOpen)
                {
                    bool canBuy = selected.price > 0 && save.Coins >= selected.price;
                    ui.ArtButton(page, canBuy ? "shop/btn_buy" : "shop/btn_buy_off", (GameUi.Width - 115f) * 0.5f, bgY + bgH - 25f, 115f, 50f, () =>
                    {
                        if (selected.price <= 0 || save.Coins < selected.price) return;
                        var bought = flow.Buy(selected.id);
                        if (bought.Length == 0) return;
                        PersistThen(() =>
                        {
                            ui.Sound(selected.id == "chest" ? "chest_opening" : "loot_bag_opening", save);
                            shopLastProduct = selected;
                            shopLastLoot = bought;
                            shopLootOpen = true;
                            RenderShopView();
                        });
                    }, canBuy);
                    fonts.Draw(page, "white_stroked_small", $"${selected.price}", GameUi.Width * 0.5f, bgY + bgH + 3f, 11f, "center");
                }
            }

            if (shopLootOpen && shopLastProduct != null)
            {
                GameUi.Fill(page, new Color(0f, 0f, 0f, 0.88f), 0f, 0f, GameUi.Width, GameUi.Height, true);
                ui.Art(page, "shop/loot_lights", 0f, 0f, GameUi.Width, GameUi.Height, true, 0.72f);
                fonts.Draw(page, "white_stroked_small", $"{shopLastProduct.title} OPENED!", GameUi.Width * 0.5f, 74f, 14f, "center");

                var shown = (shopLastLoot ?? Array.Empty<Reward>()).Take(6).ToArray();
                for (int i = 0; i < shown.Length; i++)
                {
                    var item = shown[i];
                    float ang = -Mathf.PI * 0.5f + (i * Mathf.PI * 2f) / shown.Length;
                    float cx = GameUi.Width * 0.5f + Mathf.Cos(ang) * 86f;
                    float cy = 278f + Mathf.Sin(ang) * 86f;
                    ui.Art(page, "general/reward_slot", cx - 27.5f, cy - 27.5f, 55f, 55f);
                    ui.Art(page, GameUi.Item(item.id), cx - 19f, cy - 21f, 38f, 38f, true);
                    fonts.Draw(page, "white_stroked_small", item.id == "coins" ? $"${item.amount}" : $"x{item.amount}", cx, cy + 16f, 9f, "center");
                }

                ui.ArtButton(page, "general/btn_cool", (GameUi.Width - 105f) * 0.5f, 472f, 105f, 45f, () =>
                {
                    ui.Sound("cool_button", save);
                    shopLootOpen = false;
                    shopLastLoot = Array.Empty<Reward>();
                    RenderShopView();
                });
            }
        }

        private RectTransform resultDynamicStage;

        private void ShowResult(Reward[] loot, bool saved)
        {
            resultLoot = loot ?? Array.Empty<Reward>();
            resultSaved = saved;
            resultRewardPage = 0;
            resultMonIdx = 0;
            resultElapsedMs = 0f;
            resultXpRevealDone = false;
            resultHasPreLevel = false;
            resultSuperlativeIdx = 1 + (int)((battleTimeMs + save.Coins) % 4);

            resultMonQueue.Clear();
            if (battle?.Caught == null && battle != null)
            {
                var seen = new HashSet<int>();
                foreach (var c in battle.Party)
                {
                    if (!battleXpEarned.ContainsKey(c.Id) && c != battle.Player) continue;
                    int idx = save.Team.FindIndex(m => m.Id == c.Id);
                    if (idx >= 0 && seen.Add(idx)) resultMonQueue.Add((idx, null));
                }
                if (resultMonQueue.Count == 0 && save.Team.Count > 0) resultMonQueue.Add((0, null));
            }

            resultDisplayRewards = new List<Reward>(resultLoot);
            bool allowXp = battle?.Caught == null && (battle?.Outcome == "player" || (battle?.Encounter.Kind == "training" && battleXpEarned.Count > 0));
            if (allowXp)
            {
                foreach (var kv in battleXpEarned)
                    if (kv.Value > 0) resultDisplayRewards.Add(new Reward { id = "xp", amount = kv.Value });
            }

            if (battle?.Outcome == "player") ui.Sound("win_battle", save);
            RenderResultView();
        }

        private void RenderResultView()
        {
            BeginPage(null); Track("music_map_loop");
            resultActive = true;
            ui.Art(page, "team/bg_team", 0f, 0f, GameUi.Width, GameUi.Height);
            GameUi.Fill(page, new Color(0f, 0f, 0f, 0.48f), 0f, 0f, GameUi.Width, GameUi.Height);

            string outcome = battle?.Outcome ?? "player";
            bool allowXp = battle?.Caught == null && (outcome == "player" || (battle?.Encounter.Kind == "training" && battleXpEarned.Count > 0));

            if (outcome == "player")
            {
                ui.Art(page, $"session/endscreen/superlative{resultSuperlativeIdx}", 92.5f, 18f, 135f, 30f, true);
                ui.Art(page, "session/endscreen/you_win", 40f, 50f, 240f, 55f, true);
            }
            else
            {
                fonts.Draw(page, "white_stroked_big", outcome == "enemy" ? "DEFEAT" : "ESCAPED", GameUi.Width * 0.5f, 56f, 24f, "center");
                if (outcome == "enemy" && allowXp)
                    fonts.Draw(page, "white_stroked_small", "TRAINING XP EARNED!", GameUi.Width * 0.5f, 81f, 9f, "center");
            }

            ui.Art(page, "general/pedestal", 44f, 206f, 232f, 141.5f, true);
            ui.Art(page, "session/mon_shadow", 102.5f, 236f, 115f, 28f, true, 0.65f);

            OwnedMon showcaseOwned = null;
            int showcaseTeamIdx = -1;
            if (battle?.Caught == null && resultMonIdx < resultMonQueue.Count)
            {
                showcaseTeamIdx = resultMonQueue[resultMonIdx].teamIndex;
                if (showcaseTeamIdx >= 0 && showcaseTeamIdx < save.Team.Count) showcaseOwned = save.Team[showcaseTeamIdx];
            }

            string showMonId = battle?.Caught != null ? battle.Caught.MonId : showcaseOwned?.MonId ?? battle?.Player?.MonId;
            int showLvl = battle?.Caught != null ? battle.Caught.Level : showcaseOwned?.Level ?? battle?.Player?.Level ?? 1;
            int showHp = battle?.Caught != null ? battle.Caught.MaxHp : showcaseOwned?.Hp ?? data.battleData.maxHP;
            int maxHp = battle?.Caught != null ? battle.Caught.MaxHp : data.battleData.maxHP;

            if (!string.IsNullOrEmpty(showMonId))
            {
                ui.Art(page, GameUi.Mon(showMonId, "front"), 108f, 158f, 104f, 96f, true);
                if (data.Mon(showMonId) != null)
                {
                    HudWidgets.DrawMonPane(ui, fonts, data, page, new MonHudView
                    {
                        MonId = showMonId, Level = showLvl, Hp = showHp, MaxHp = maxHp,
                    }, "tooltip", GameUi.Width * 0.5f - 50f, 96f, showHpBar: battle?.Caught == null);
                }
            }

            resultDynamicStage = GameUi.Box("ResultDynamicStage", page, 0f, 0f, GameUi.Width, GameUi.Height);
            RenderResultDynamicStage();

            int needXp = showcaseOwned != null ? flow.NeededXp(showcaseOwned) : 0;
            bool readyToLevel = allowXp && showcaseTeamIdx >= 0 && needXp > 0 && showcaseOwned.Xp >= needXp;

            if (battle?.Caught != null)
                fonts.Draw(page, "white_stroked_small", $"{(data.Mon(battle.Caught.MonId)?.title ?? battle.Caught.MonId).ToUpperInvariant()} CAUGHT!",
                    GameUi.Width * 0.5f, 268f, 11f, "center");
            else if (outcome != "player")
                fonts.Draw(page, "white_stroked_small", outcome == "enemy" ? "Heal for FREE at Team or Map!" : "Your team left the battle.",
                    GameUi.Width * 0.5f, allowXp ? 450f : 272f, 9f, "center");

            ui.Art(page, "session/endscreen/rewards_modal", 32.5f, 352f, 255f, 90f, true);

            if (readyToLevel)
            {
                string stone = MatchingStoneFor(showcaseOwned);
                bool hasStone = !string.IsNullOrEmpty(stone);
                const float lx = (GameUi.Width - 112f) * 0.5f, ly = 294f;
                ui.ArtButton(page, "general/btn_levelup", lx, ly, 112f, 45f, () =>
                {
                    TryTriggerStoneLevelUp(showcaseTeamIdx, stone, RenderResultView);
                }, hasStone);
                if (hasStone)
                {
                    ui.Art(page, GameUi.Item(stone), lx + 8f, ly + 8f, 28f, 28f, true);
                    ui.Art(page, "general/btn_levelup_text", lx + 36f, ly + 12f, 68f, 18f, true);
                }
                else fonts.Draw(page, "white_stroked_small", "NEED STONE", lx + 56f, ly + 16f, 8f, "center");
            }

            int totalPages = Math.Max(1, (resultDisplayRewards.Count + 3) / 4);
            resultRewardPage = GameData.Clamp(resultRewardPage, 0, totalPages - 1);
            var shown = resultDisplayRewards.Skip(resultRewardPage * 4).Take(4).ToArray();
            if (shown.Length > 0)
            {
                float totalW = shown.Length * 57f - 2f;
                float startX = (GameUi.Width - totalW) * 0.5f;
                for (int i = 0; i < shown.Length; i++)
                {
                    var rw = shown[i];
                    float rx = startX + i * 57f, ry = 374f;
                    ui.Art(page, "general/reward_slot", rx, ry, 55f, 55f);
                    if (rw.id == "xp")
                    {
                        ui.Art(page, "general/xpbar_bg", rx + 5f, ry + 18f, 45f, 10f);
                        fonts.Draw(page, "white_stroked_small", "XP", rx + 27.5f, ry + 14f, 11f, "center");
                        fonts.Draw(page, "white_stroked_small", rw.amount.ToString(), rx + 27.5f, ry + 41f, 9f, "center");
                    }
                    else
                    {
                        ui.Art(page, GameUi.Item(rw.id), rx + 9f, ry + 6f, 37f, 37f, true);
                        fonts.Draw(page, "white_stroked_small", rw.id == "coins" ? $"${rw.amount}" : $"x{rw.amount}", rx + 27.5f, ry + 41f, 9f, "center");
                    }
                }
            }
            else
            {
                fonts.Draw(page, "white_stroked_small", "See you in the next battle!", GameUi.Width * 0.5f, 392f, 9.5f, "center");
            }

            if (resultDisplayRewards.Count > 4)
            {
                if (resultRewardPage > 0)
                {
                    var prev = ui.ArtButton(page, "general/btn_arrow_right", 0f, 396f - 47.5f, 35f, 95f, () =>
                    {
                        ui.Sound("button_click", save);
                        resultRewardPage--;
                        RenderResultView();
                    });
                    var img = prev.GetComponentInChildren<RawImage>();
                    if (img != null) img.uvRect = new Rect(1f, 0f, -1f, 1f);
                }
                if (resultRewardPage < totalPages - 1)
                {
                    ui.ArtButton(page, "general/btn_arrow_right", GameUi.Width - 35f, 396f - 47.5f, 35f, 95f, () =>
                    {
                        ui.Sound("button_click", save);
                        resultRewardPage++;
                        RenderResultView();
                    });
                }
            }

            if (!resultSaved) fonts.Draw(page, "white_stroked_small", "SAVE FAILED - TAP TO RETRY", GameUi.Width * 0.5f, 444f, 9f, "center");

            bool hasNextMon = battle?.Caught == null && resultMonIdx + 1 < resultMonQueue.Count;
            ui.ArtButton(page, "general/btn_cool", (GameUi.Width - 105f) * 0.5f, 472f, 105f, 45f, () =>
            {
                ui.Sound("cool_button", save);
                if (!resultSaved && !Persist()) { Note("Could not save. Please retry."); return; }
                if (hasNextMon)
                {
                    resultMonIdx++;
                    resultElapsedMs = 0f;
                    resultXpRevealDone = false;
                    RenderResultView();
                    return;
                }
                string afterDialog = outcome == "player" ? battle?.Encounter.Dialogs?.dialog_win :
                    outcome == "enemy" ? battle?.Encounter.Dialogs?.dialog_lose : null;
                resultActive = false;
                battle = null;
                Dialogue(afterDialog, () =>
                {
                    if (outcome == "enemy") ShowTeam();
                    else if (save.Event.RewardPending) ShowStarterReward();
                    else ShowMap();
                });
            });
        }

        private void RenderResultDynamicStage()
        {
            if (resultDynamicStage == null) return;
            GameUi.Clear(resultDynamicStage);

            bool allowXp = battle?.Caught == null && (battle?.Outcome == "player" || (battle?.Encounter.Kind == "training" && battleXpEarned.Count > 0));
            if (!allowXp) return;

            OwnedMon showcaseOwned = resultMonIdx < resultMonQueue.Count && resultMonQueue[resultMonIdx].teamIndex >= 0
                ? save.Team[resultMonQueue[resultMonIdx].teamIndex]
                : null;
            if (showcaseOwned == null) return;

            int needXp = flow.NeededXp(showcaseOwned);
            bool readyToLevel = needXp > 0 && showcaseOwned.Xp >= needXp;
            float targetRatio = showcaseOwned.Level >= data.MaxLevel ? 1f : (needXp > 0 ? Mathf.Clamp01(showcaseOwned.Xp / (float)needXp) : 1f);
            float animRatio = readyToLevel ? Mathf.Clamp(resultElapsedMs / 450f, 0.25f, 1f) : targetRatio;
            HudWidgets.DrawClippedBar(ui, resultDynamicStage, "general/xpbar_bg", "general/xpbar_fill",
                GameUi.Width * 0.5f - 67.5f, 260f, 135f, 25f, animRatio, 0.04f, 0.96f);
            if (readyToLevel)
                ui.Art(resultDynamicStage, "general/badge_levelup", GameUi.Width * 0.5f + 54f, 262f, 24f, 24f, true);
        }

        private void ShowPvpResult(string outcome)
        {
            if (pvpResultShown) return;
            pvpResultShown = true;
            inPvpBattle = false;
            pvpActive = false;
            pvpResultOutcome = outcome == "win" ? "win" : "loss";
            pvpResultCoins = pvpResultOutcome == "win"
                ? (pvpResolvedTurns >= 1 ? 50 : 0)
                : (pvpResolvedTurns >= 2 ? 15 : 0);
            if (pvpResultCoins > 0)
            {
                save.Coins += pvpResultCoins;
                Persist();
            }
            resultSuperlativeIdx = 1 + (int)((battleTimeMs + save.Coins) % 4);
            if (pvpResultOutcome == "win") ui.Sound("win_battle", save);
            else ui.Sound("stat_lowered", save);
            RenderPvpResultView();
        }

        private void RenderPvpResultView()
        {
            BeginPage(null); Track("music_map_loop");
            pvpResultActive = true;
            ui.Art(page, "team/bg_team", 0f, 0f, GameUi.Width, GameUi.Height);
            GameUi.Fill(page, new Color(0f, 0f, 0f, 0.48f), 0f, 0f, GameUi.Width, GameUi.Height);

            if (pvpResultOutcome == "win")
            {
                ui.Art(page, $"session/endscreen/superlative{resultSuperlativeIdx}", 92.5f, 18f, 135f, 30f, true);
                ui.Art(page, "session/endscreen/you_win", 40f, 50f, 240f, 55f, true);
            }
            else
            {
                fonts.Draw(page, "white_stroked_big", "DEFEAT", GameUi.Width * 0.5f, 56f, 24f, "center");
            }

            ui.Art(page, "general/pedestal", 44f, 206f, 232f, 141.5f, true);
            ui.Art(page, "session/mon_shadow", 102.5f, 236f, 115f, 28f, true, 0.65f);

            var leadView = viewPlayer.Count > 0 ? viewPlayer[0] : null;
            var leadSaved = save.Team.FirstOrDefault();
            string showMonId = leadView?.MonId ?? leadSaved?.MonId;
            int showLvl = leadView?.Level ?? leadSaved?.Level ?? 1;
            int showHp = leadView?.Hp ?? data.battleData.maxHP;
            int maxHp = leadView?.MaxHp ?? data.battleData.maxHP;

            if (!string.IsNullOrEmpty(showMonId))
            {
                ui.Art(page, GameUi.Mon(showMonId, "front"), 108f, 158f, 104f, 96f, true);
                if (data.Mon(showMonId) != null)
                {
                    HudWidgets.DrawMonPane(ui, fonts, data, page, new MonHudView
                    {
                        MonId = showMonId, Level = showLvl, Hp = showHp, MaxHp = maxHp,
                    }, "tooltip", GameUi.Width * 0.5f - 50f, 96f, showHpBar: true);
                }
            }

            string vsLine = $"VS {pvpPeerName} - {pvpResolvedTurns} {(pvpResolvedTurns == 1 ? "TURN" : "TURNS")}";
            fonts.Draw(page, "white_stroked_small", vsLine, GameUi.Width * 0.5f, 270f, 9.5f, "center", null, 280f);
            if (!string.IsNullOrEmpty(pvpResultStatusNote))
            {
                fonts.Draw(page, "white_stroked_small", pvpResultStatusNote, GameUi.Width * 0.5f, 294f, 9f, "center",
                    pvpRemoteRematchRequested ? GameUi.Gold : GameUi.HexColor("#ff9aa3"), 280f);
            }

            ui.Art(page, "session/endscreen/rewards_modal", 32.5f, 352f, 255f, 90f, true);
            const float rx = (GameUi.Width - 55f) * 0.5f, ry = 368f;
            ui.Art(page, "general/reward_slot", rx, ry, 55f, 55f);
            ui.Art(page, GameUi.Item("coins"), rx + 9f, ry + 5f, 37f, 37f, true);
            string coinLabel = pvpResultCoins == 50 ? "50 COINS" : pvpResultCoins == 15 ? "15 COINS" : "0 COINS";
            fonts.Draw(page, "white_stroked_small", coinLabel, GameUi.Width * 0.5f, ry + 41f, 8.5f, "center",
                pvpResultCoins > 0 ? GameUi.Gold : new Color(0.84f, 0.84f, 0.92f, 0.85f));
            if (pvpResultCoins == 0)
            {
                fonts.Draw(page, "white_stroked_small", "PLAY MORE TURNS TO EARN COINS", GameUi.Width * 0.5f, 430f, 7.5f, "center",
                    new Color(0.84f, 0.88f, 1f, 0.9f), 240f);
            }

            bool canRematch = lanTransport != null && lanTransport.IsConnected;
            var btnVec = VectorCanvas.Create("PvpResultBtnsVec", page, 0f, 0f, GameUi.Width, GameUi.Height);
            if (canRematch)
            {
                btnVec.FillRoundRect(24f, 470f, 130f, 42f, 9f, GameUi.HexColor("#2b8a4e"));
                btnVec.StrokeRoundRect(24f, 470f, 130f, 42f, 9f, GameUi.HexColor("#ffe259"), 1.6f);
                btnVec.FillRoundRect(166f, 470f, 130f, 42f, 9f, GameUi.HexColor("#2b7cff"));
                btnVec.StrokeRoundRect(166f, 470f, 130f, 42f, 9f, GameUi.HexColor("#7be0ff"), 1.6f);
                btnVec.Commit();

                fonts.Draw(page, "white_stroked_small", "REMATCH", 89f, 484f, 12f, "center");
                ui.HitBox(page, 24f, 470f, 130f, 42f, () =>
                {
                    if (lanTransport == null || !lanTransport.IsConnected)
                    {
                        pvpResultStatusNote = "OPPONENT DISCONNECTED";
                        RenderPvpResultView();
                        return;
                    }
                    ui.Sound("cool_button", save);
                    lanTransport.Send("rematch", new LanPingDto { ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
                    ReopenLanReadyRoomForRematch();
                });

                fonts.Draw(page, "white_stroked_small", "TO ISLAND", 231f, 484f, 11.5f, "center");
                ui.HitBox(page, 166f, 470f, 130f, 42f, () =>
                {
                    ui.Sound("button_click", save);
                    pvpResultActive = false;
                    ReturnToLanIsland();
                });
            }
            else
            {
                btnVec.FillRoundRect(90f, 470f, 140f, 42f, 9f, GameUi.HexColor("#2b7cff"));
                btnVec.StrokeRoundRect(90f, 470f, 140f, 42f, 9f, GameUi.HexColor("#7be0ff"), 1.6f);
                btnVec.Commit();

                fonts.Draw(page, "white_stroked_small", "TO ISLAND", 160f, 484f, 12f, "center");
                ui.HitBox(page, 90f, 470f, 140f, 42f, () =>
                {
                    ui.Sound("button_click", save);
                    pvpResultActive = false;
                    ReturnToLanIsland();
                });
            }
        }
    }
}
