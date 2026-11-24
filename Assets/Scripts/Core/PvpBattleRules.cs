using System;
using System.Collections.Generic;
using System.Linq;

namespace Dynamons
{
    public sealed class PvpBattleRules
    {
        private readonly GameData data;
        private readonly BattleRules rules;
        private readonly string startingSide;
        private readonly LanItemSlotDto[] hostItems;
        private readonly LanItemSlotDto[] clientItems;
        private string pendingTickSide;
        private string nextTurnAfterSwitch = string.Empty;

        public string RuleMode { get; }
        public bool ItemsAllowed { get; }
        public int Seq { get; private set; }
        public int TurnNumber { get; private set; } = 1;
        public string CurrentTurnSide { get; private set; }
        public string Phase { get; private set; } = "turn";
        public string ForcedSwitchSide { get; private set; } = string.Empty;
        public string Outcome { get; private set; } = string.Empty;
        public int ResolvedTurns { get; private set; }
        public Combatant[] HostTeam { get; }
        public Combatant[] ClientTeam { get; }
        public int HostSlot { get; private set; }
        public int ClientSlot { get; private set; }

        public PvpBattleRules(GameData data, IReadOnlyList<LanTeamMemberDto> hostRoster,
            IReadOnlyList<LanTeamMemberDto> clientRoster, string ruleMode, bool itemsAllowed, Func<double> rng)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            if (hostRoster == null || hostRoster.Count == 0 || clientRoster == null || clientRoster.Count == 0)
                throw new ArgumentException("Both sides require at least 1 Dynamon.");

            this.data = data;
            RuleMode = ruleMode == "real" ? "real" : "fair20";
            ItemsAllowed = itemsAllowed;
            HostTeam = BuildTeam(hostRoster, "host");
            ClientTeam = BuildTeam(clientRoster, "client");
            rules = new BattleRules(data, HostTeam, ClientTeam, rng, isPvp: true);

            hostItems = itemsAllowed ? CreateStandardPouch() : Array.Empty<LanItemSlotDto>();
            clientItems = itemsAllowed ? CreateStandardPouch() : Array.Empty<LanItemSlotDto>();

            startingSide = rng() < 0.5 ? "host" : "client";
            CurrentTurnSide = startingSide;
        }

        public LanStartMatchDto BuildStartMatch() => BuildStartMatch("HOST", "CLIENT");

        public LanStartMatchDto BuildStartMatch(string hostName, string clientName) => new LanStartMatchDto
        {
            ruleMode = RuleMode,
            itemsAllowed = ItemsAllowed,
            startingSide = startingSide,
            hostName = string.IsNullOrEmpty(hostName) ? "HOST" : hostName,
            clientName = string.IsNullOrEmpty(clientName) ? "CLIENT" : clientName,
            hostTeam = HostTeam.Select(ToStartMonDto).ToArray(),
            clientTeam = ClientTeam.Select(ToStartMonDto).ToArray(),
            hostItems = CloneItems(hostItems),
            clientItems = CloneItems(clientItems)
        };

        public bool CanMega(string side)
        {
            if (Phase == "ended" || !IsValidSide(side)) return false;
            var team = TeamOf(side);
            var active = ActiveOf(side);
            return active.Hp > 0 && active.Status != "sleep" && !team.Any(mon => mon.Mega);
        }

        public int PouchCount(string side, string itemId)
        {
            if (!ItemsAllowed || !IsValidSide(side) || string.IsNullOrEmpty(itemId)) return 0;
            var slot = Array.Find(PouchOf(side), s => s.id == itemId);
            return slot?.count ?? 0;
        }

        public LanTurnResultDto SubmitAction(string side, LanActionCmdDto cmd)
        {
            if (Phase == "ended" || !IsValidSide(side) || cmd == null || cmd.seq != Seq) return null;
            if (cmd.actionType == "forfeit") return ResolveForfeit(side);

            if (Phase == "forced_switch")
            {
                if (side != ForcedSwitchSide || cmd.actionType != "switch") return null;
                return ExecuteForcedSwitch(side, cmd.index);
            }

            if (side != CurrentTurnSide) return null;

            switch (cmd.actionType)
            {
                case "mega":
                    return ExecuteMega(side);
                case "switch":
                    return ExecuteVoluntarySwitch(side, cmd.index);
                case "ability":
                    return ExecuteAbility(side, cmd.id);
                case "item":
                    return ExecuteItem(side, cmd.id);
                default:
                    return null;
            }
        }

        public LanTurnResultDto ResolveTimeoutFallback()
        {
            if (Phase == "ended") return null;
            if (Phase == "forced_switch")
            {
                int slot = FirstAliveBenchSlot(ForcedSwitchSide);
                return slot >= 0
                    ? SubmitAction(ForcedSwitchSide, new LanActionCmdDto { seq = Seq, actionType = "switch", index = slot })
                    : Forfeit(ForcedSwitchSide);
            }

            var active = ActiveOf(CurrentTurnSide);
            var readyMove = active.Moves?.FirstOrDefault(m => active.Ready(m));
            if (readyMove != null)
                return SubmitAction(CurrentTurnSide, new LanActionCmdDto { seq = Seq, actionType = "ability", id = readyMove.id });

            int bench = FirstAliveBenchSlot(CurrentTurnSide);
            if (bench >= 0)
                return SubmitAction(CurrentTurnSide, new LanActionCmdDto { seq = Seq, actionType = "switch", index = bench });

            return Forfeit(CurrentTurnSide);
        }

        public LanTurnResultDto Forfeit(string side)
        {
            if (Phase == "ended" || !IsValidSide(side)) return null;
            return ResolveForfeit(side);
        }

        private Combatant[] BuildTeam(IReadOnlyList<LanTeamMemberDto> roster, string side)
        {
            int count = Math.Min(3, roster.Count);
            var team = new Combatant[count];
            bool fair = RuleMode == "fair20";
            for (int i = 0; i < count; i++)
            {
                var entry = roster[i];
                if (entry == null || string.IsNullOrEmpty(entry.monId) || data.Mon(entry.monId) == null)
                    throw new ArgumentException($"Invalid PvP Dynamon at {side} slot {i}.");
                int level = fair ? data.MaxLevel : GameData.Clamp(entry.level, 1, data.MaxLevel);
                var spec = new MemberSpec
                {
                    MonId = entry.monId,
                    Level = level,
                    MaxHp = data.battleData.maxHP,
                    StatCap = 150,
                    Stats = data.StatsAt(entry.monId, level, !fair)
                };
                team[i] = BattleRules.CreateCombatant(data, spec, side + "-" + i);
            }
            return team;
        }

        private static LanItemSlotDto[] CreateStandardPouch() => new[]
        {
            new LanItemSlotDto { id = "heal_spray", count = 2 },
            new LanItemSlotDto { id = "antidote", count = 1 },
            new LanItemSlotDto { id = "atk_pepper", count = 1 },
            new LanItemSlotDto { id = "def_berry", count = 1 }
        };

        private static LanItemSlotDto[] CloneItems(LanItemSlotDto[] items) =>
            items.Select(it => new LanItemSlotDto { id = it.id, count = it.count }).ToArray();

        private static bool IsValidSide(string side) => side == "host" || side == "client";
        private static string Other(string side) => side == "host" ? "client" : "host";
        private Combatant[] TeamOf(string side) => side == "host" ? HostTeam : ClientTeam;
        private int SlotOf(string side) => side == "host" ? HostSlot : ClientSlot;
        private void SetSlot(string side, int slot) { if (side == "host") HostSlot = slot; else ClientSlot = slot; }
        private Combatant ActiveOf(string side) => TeamOf(side)[SlotOf(side)];
        private LanItemSlotDto[] PouchOf(string side) => side == "host" ? hostItems : side == "client" ? clientItems : null;
        private static bool HasAlive(Combatant[] team) => team.Any(mon => mon.Hp > 0);

        private bool TakeFromPouch(string side, string itemId)
        {
            var pouch = PouchOf(side);
            if (pouch == null) return false;
            var slot = Array.Find(pouch, s => s.id == itemId);
            if (slot == null || slot.count < 1) return false;
            slot.count--;
            return true;
        }

        private int FirstAliveBenchSlot(string side)
        {
            var team = TeamOf(side);
            int activeSlot = SlotOf(side);
            for (int i = 0; i < team.Length; i++)
                if (i != activeSlot && team[i].Hp > 0) return i;
            return -1;
        }

        private LanTurnResultDto ExecuteMega(string side)
        {
            if (!CanMega(side)) return null;
            rules.DrainEvents();
            rules.TriggerMegaEvolution(ActiveOf(side));
            return CommitResult(side, false);
        }

        private LanTurnResultDto ExecuteVoluntarySwitch(string side, int index)
        {
            var team = TeamOf(side);
            if (index < 0 || index >= team.Length || index == SlotOf(side) || team[index].Hp <= 0) return null;
            rules.DrainEvents();
            SetSlot(side, index);
            var incoming = team[index];
            rules.Emit("switch", incoming, "GO " + incoming.MonId.ToUpperInvariant());
            ResolvedTurns++;
            rules.Tick(team, incoming, true);
            bool turnEnded = AdvanceAfterActorTick(side, incoming);
            return CommitResult(side, turnEnded);
        }

        private LanTurnResultDto ExecuteAbility(string side, string abilityId)
        {
            var actor = ActiveOf(side);
            var defender = ActiveOf(Other(side));
            var move = actor.Moves?.FirstOrDefault(m => m.id == abilityId);
            if (actor.Hp <= 0 || move == null || !actor.Ready(move)) return null;

            rules.DrainEvents();
            if (actor.Status == "sleep") rules.Emit("sleep", actor, "FAST ASLEEP", status: "sleep");
            else rules.Resolve(actor, defender, move);

            ResolvedTurns++;
            bool turnEnded = FinishCombatTurn(side);
            return CommitResult(side, turnEnded);
        }

        private LanTurnResultDto ExecuteItem(string side, string itemId)
        {
            if (!ItemsAllowed || string.IsNullOrEmpty(itemId) || itemId == "discatch") return null;
            if (data.Item(itemId)?.inBattle != true) return null;
            var actor = ActiveOf(side);
            if (actor.Hp <= 0 || (actor.Status == "sleep" && itemId.StartsWith("can_", StringComparison.Ordinal))) return null;

            string moveId = itemId.StartsWith("can_", StringComparison.Ordinal) ? "can-" + itemId.Substring(4) : "item-" + itemId;
            var move = data.Move(moveId);
            if (move == null || !TakeFromPouch(side, itemId)) return null;

            rules.DrainEvents();
            rules.Resolve(actor, ActiveOf(Other(side)), move);
            ResolvedTurns++;
            bool turnEnded = FinishCombatTurn(side);
            return CommitResult(side, turnEnded);
        }

        private LanTurnResultDto ExecuteForcedSwitch(string side, int index)
        {
            var team = TeamOf(side);
            if (index < 0 || index >= team.Length || index == SlotOf(side) || team[index].Hp <= 0) return null;

            rules.DrainEvents();
            SetSlot(side, index);
            var incoming = team[index];
            rules.Emit("switch", incoming, "GO " + incoming.MonId.ToUpperInvariant());

            if (!string.IsNullOrEmpty(pendingTickSide))
            {
                string tickSide = pendingTickSide;
                pendingTickSide = null;
                var tickTeam = TeamOf(tickSide);
                var tickActive = ActiveOf(tickSide);
                rules.Tick(tickTeam, tickActive, tickActive.Hp > 0);
                return CommitResult(side, AdvanceAfterActorTick(tickSide, tickActive));
            }

            string nextSide = string.IsNullOrEmpty(nextTurnAfterSwitch) ? side : nextTurnAfterSwitch;
            return CommitResult(side, TransitionToTurn(nextSide));
        }

        private bool FinishCombatTurn(string actorSide)
        {
            string defenderSide = Other(actorSide);
            var actorTeam = TeamOf(actorSide);
            var defenderTeam = TeamOf(defenderSide);
            var actor = ActiveOf(actorSide);
            var defender = ActiveOf(defenderSide);

            bool actorTeamAlive = HasAlive(actorTeam);
            bool defenderTeamAlive = HasAlive(defenderTeam);
            if (!actorTeamAlive && !defenderTeamAlive)
            {
                EndMatch(actorSide);
                return true;
            }
            if (!defenderTeamAlive)
            {
                EndMatch(actorSide);
                return true;
            }
            if (!actorTeamAlive)
            {
                EndMatch(defenderSide);
                return true;
            }

            if (defender.Hp <= 0) return EnterForcedSwitch(defenderSide, defenderSide, actorSide);

            if (actor.Hp <= 0)
            {
                rules.Tick(actorTeam, actor, false);
                return EnterForcedSwitch(actorSide, defenderSide);
            }

            rules.Tick(actorTeam, actor, true);
            return AdvanceAfterActorTick(actorSide, actor);
        }

        private bool AdvanceAfterActorTick(string actorSide, Combatant actor)
        {
            var actorTeam = TeamOf(actorSide);
            string defenderSide = Other(actorSide);
            if (!HasAlive(actorTeam))
            {
                EndMatch(defenderSide);
                return true;
            }
            if (actor.Hp <= 0) return EnterForcedSwitch(actorSide, defenderSide);
            return TransitionToTurn(defenderSide);
        }

        private bool EnterForcedSwitch(string switchSide, string nextTurnSide, string tickSide = null)
        {
            Phase = "forced_switch";
            ForcedSwitchSide = switchSide;
            pendingTickSide = tickSide;
            nextTurnAfterSwitch = nextTurnSide;
            return false;
        }

        private bool TransitionToTurn(string nextSide)
        {
            Phase = "turn";
            ForcedSwitchSide = string.Empty;
            CurrentTurnSide = nextSide;
            nextTurnAfterSwitch = string.Empty;
            TurnNumber++;
            return true;
        }

        private void EndMatch(string winnerSide)
        {
            Phase = "ended";
            ForcedSwitchSide = string.Empty;
            CurrentTurnSide = string.Empty;
            pendingTickSide = null;
            nextTurnAfterSwitch = string.Empty;
            Outcome = winnerSide;
            rules.Emit("end", text: winnerSide);
        }

        private LanTurnResultDto ResolveForfeit(string side)
        {
            rules.DrainEvents();
            rules.Emit("escape", ActiveOf(side), "FORFEIT!");
            EndMatch(Other(side));
            return CommitResult(side, true);
        }

        private LanTurnResultDto CommitResult(string actorSide, bool turnEnded)
        {
            int acceptedSeq = Seq++;
            var dtoEvents = rules.DrainEvents().Select(ev => LanCodec.EventToDto(ev, actorSide)).ToArray();
            return new LanTurnResultDto
            {
                seq = acceptedSeq,
                actorSide = actorSide,
                nextTurnSide = CurrentTurnSide,
                phase = Phase,
                forcedSwitchSide = ForcedSwitchSide,
                outcome = Outcome,
                activeHostSlot = HostSlot,
                activeClientSlot = ClientSlot,
                turnNumber = TurnNumber,
                turnEnded = turnEnded,
                events = dtoEvents,
                hostTeam = HostTeam.Select(ToSnapshotDto).ToArray(),
                clientTeam = ClientTeam.Select(ToSnapshotDto).ToArray(),
                hostItems = CloneItems(hostItems),
                clientItems = CloneItems(clientItems)
            };
        }

        private static LanPvpMonDto ToStartMonDto(Combatant c) => new LanPvpMonDto
        {
            id = c.Id,
            monId = c.MonId,
            kind = c.Kind,
            level = c.Level,
            hp = c.Hp,
            maxHp = c.MaxHp,
            atk = c.Base.atk,
            def = c.Base.def,
            aim = c.Base.aim,
            shield = c.Shield,
            moveIds = c.Moves.Select(m => m.id).ToArray()
        };

        private static LanCombatantSnapshotDto ToSnapshotDto(Combatant c)
        {
            var activeCooldowns = c.Cooldowns
                .Where(kv => kv.Value > 0)
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .ToArray();
            return new LanCombatantSnapshotDto
            {
                id = c.Id,
                monId = c.MonId,
                kind = c.Kind,
                level = c.Level,
                hp = c.Hp,
                maxHp = c.MaxHp,
                status = c.Status,
                rage = c.Rage,
                mega = c.Mega,
                shield = c.Shield,
                voidStacks = c.VoidStacks,
                powerStacks = c.PowerStacks,
                charges = c.Charges,
                atkMod = c.Mods.atk,
                defMod = c.Mods.def,
                aimMod = c.Mods.aim,
                cooldownIds = activeCooldowns.Select(kv => kv.Key).ToArray(),
                cooldownTurns = activeCooldowns.Select(kv => kv.Value).ToArray()
            };
        }
    }
}
