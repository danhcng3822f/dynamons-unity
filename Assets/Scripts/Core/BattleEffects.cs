using System;
using System.Linq;

namespace Dynamons
{
    public sealed partial class BattleRules
    {
        private static bool Hostile(string op) => op == "attack" || op == "state_sick" || op == "state_hypno" || op == "state_sleep" || op.StartsWith("decr_", StringComparison.Ordinal);
        private static bool Charged(Combatant mon, MoveData move) => mon.Passive?.type == "charge" && mon.Passive.abilityId == move.id && mon.Charges >= Math.Max(1, mon.Passive.threshold);
        private static void ClearStatus(Combatant mon) { mon.Status = null; mon.SleepTurns = 0; mon.SleepFresh = false; }
        private void Hurt(Combatant target, int amount)
        {
            target.Hp = Math.Max(0, target.Hp - amount);
            Emit("damage", target, amount: amount, hp: true);
            if (target.Hp > 0) return;
            ClearStatus(target); target.Rage = false; target.RageTurns = 0;
            Emit("faint", target, "FAINTED!", hp: true);
        }

        private void Resolve(Combatant actor, Combatant defender, MoveData move, int? healOverride = null)
        {
            var ops = move.effect.Split(',').Select(value => value.Trim()).Where(value => value.Length != 0).ToArray();
            bool item = move.id.StartsWith("item-", StringComparison.Ordinal) || move.id.StartsWith("can-", StringComparison.Ordinal);
            bool full = !item && Charged(actor, move);
            bool Missable(string op) => data.battleData.missableEffects.Contains(op);
            bool missed = false, crit = false;
            if (!item && ops.Any(Missable))
            {
                var rows = data.battleData.aiming;
                int aim = actor.Stat("aim");
                var band = rows.FirstOrDefault(row => aim <= row.upTo) ?? rows.Last();
                missed = rng() < band.miss;
                if (actor.Rage || full || actor.ConsecutiveMisses >= 2) missed = false;
                if (!missed) crit = rng() < band.crit;
                actor.ConsecutiveMisses = missed ? actor.ConsecutiveMisses + 1 : 0;
            }
            bool backfire = !item && !missed && actor.Status == "hypno" && rng() < data.battleData.hypnoBackfireChance;
            bool pureMiss = missed && ops.All(Missable);
            var visuals = (pureMiss ? data.battleData.missVisualSteps : move.visualSteps) ?? Array.Empty<VisualStep>();
            var target = backfire ? actor : defender;
            events.Add(new BattleEvent
            {
                Type = "ability", Actor = actor.Id, Target = target.Id, Side = SideOf(actor), MonId = target.MonId,
                AbilityId = move.id, FullPower = full, Backfire = backfire, Missed = pureMiss,
                Text = pureMiss ? "MISS" : full ? actor.Passive.boostedTitle : move.title,
                Visuals = backfire ? visuals.Where(step => step.inHypno != "none").ToArray() : visuals
            });
            if (backfire) Emit("hypno", actor, "HYPNO BACKFIRE!", backfire: true);
            if (missed) Emit("miss", defender, "MISS", missed: true);
            bool immune = false;
            if (!item && !missed && !backfire && defender.Hp > 0 && defender.Status != "sleep" && ops.Any(Hostile))
            {
                if (defender.Shield > 0)
                {
                    defender.Shield--; immune = true;
                    Emit("immune", defender, "SHELL SHIELD SHATTERED!", variant: "shield", shieldHits: defender.Shield);
                }
                else if (defender.Passive?.chance > 0 && rng() < defender.Passive.chance)
                {
                    defender.VoidStacks++; immune = true;
                    Emit("immune", defender, "VOID EVADE!", variant: "void", stacks: defender.VoidStacks,
                        bonusPct: GameData.Round(defender.VoidStacks * (defender.Passive?.dmgPerStack ?? 0) * 100));
                }
            }
            bool targetWasAsleep = target.Status == "sleep";
            foreach (string op in ops)
            {
                if (missed && Missable(op) || immune && Hostile(op)) continue;
                switch (op)
                {
                    case "attack":
                        Hurt(target, Damage(actor, target, move, crit && !backfire, backfire, item));
                        if (crit && !backfire && !item) Emit("critical", target, "CRITICAL HIT!");
                        if (!backfire && GameData.Strong(move.kind, target.Kind)) Emit("strong", target, "SUPER EFFECTIVE!");
                        else if (!backfire && GameData.Strong(target.Kind, move.kind)) Emit("weak", target, "NOT VERY EFFECTIVE");
                        if (actor.Rage) { actor.Rage = false; actor.RageTurns = 0; Emit("rageSpent", actor); }
                        if (targetWasAsleep && target.Status == "sleep" && target.Hp > 0) { ClearStatus(target); Emit("wake", target, "WOKE UP!"); }
                        break;
                    case "heal":
                        int previous = actor.Hp;
                        actor.Hp = Math.Min(actor.MaxHp, actor.Hp + GameData.Round(actor.MaxHp * (healOverride ?? move.percent) / 100.0));
                        Emit("heal", actor, "HEALED", actor.Hp - previous, true);
                        break;
                    case "antidote":
                        ClearStatus(actor); Emit("antidote", actor, "STATUS RESTORED!");
                        break;
                    case "revive":
                        var down = (Party.Contains(actor) ? Party : Opponents).FirstOrDefault(mon => mon.Hp <= 0);
                        if (down != null) { down.Hp = GameData.Round(down.MaxHp / 2.0); Emit("revive", down, "REVIVED!", hp: true); }
                        break;
                    case "state_rage":
                        ClearStatus(actor); actor.Rage = true; actor.RageTurns = 1; Emit("rage", actor, "RAGE!");
                        break;
                    case "state_sick": case "state_hypno": case "state_sleep":
                        if ((full && op == "state_hypno" || move.chance >= 1 || rng() < move.chance) && target.Hp > 0)
                        {
                            target.Rage = false; target.RageTurns = 0; ClearStatus(target); target.Status = op.Substring(6);
                            if (target.Status == "sleep") { target.SleepTurns = 2; target.SleepFresh = backfire || Opponents.Contains(actor); }
                            Emit("status", target, target.Status.ToUpperInvariant(), status: target.Status);
                        }
                        break;
                    case "incr_atk": case "incr_def": case "incr_aim": case "decr_atk": case "decr_def": case "decr_aim":
                        bool downStat = op[0] == 'd';
                        var recipient = downStat ? target : actor;
                        if (recipient.Hp <= 0) break;
                        string key = op.Substring(op.Length - 3);
                        int delta = Math.Max(1, GameData.Round(recipient.Stat(key, true) * move.amount / 100.0)) * (downStat ? -1 : 1);
                        if (key == "atk") recipient.Mods.atk += delta;
                        else if (key == "def") recipient.Mods.def += delta;
                        else recipient.Mods.aim += delta;
                        Emit("stat", recipient, $"{key.ToUpperInvariant()} {(downStat ? "DOWN" : "UP")} {Math.Abs(delta)}", delta, stat: key);
                        break;
                }
            }
            if (!item && !backfire && actor.Hp > 0 && ops.Contains("attack"))
            {
                if (actor.Passive?.type == "charge")
                {
                    int threshold = Math.Max(1, actor.Passive.threshold);
                    actor.Charges = full ? 0 : Math.Min(threshold, actor.Charges + 1);
                    bool ready = actor.Charges >= threshold;
                    Emit("passive", actor, ready ? "SPORE FULL POWER READY!" : null, ready: ready, variant: "charge", charges: actor.Charges, threshold: threshold);
                }
                if (actor.Passive?.type == "ramp")
                {
                    actor.PowerStacks++;
                    Emit("passive", actor, variant: "ramp", stacks: actor.PowerStacks,
                        bonusPct: GameData.Round(actor.PowerStacks * actor.Passive.dmgPerStack * 100));
                }
            }
            if (!item && move.cooldown > 0) actor.Cooldowns[move.id] = move.cooldown + 1;
        }

        private double Matchup(Combatant mon, Combatant target)
        {
            double hp = mon.Hp / (double)mon.MaxHp;
            bool atkStrong = GameData.Strong(mon.Kind, target.Kind);
            bool atkWeak = GameData.Strong(target.Kind, mon.Kind);
            double score = (atkStrong ? 3.5 : atkWeak ? -2.5 : 0) + (atkWeak ? -2.5 : atkStrong ? 2.0 : 0);
            if (mon.Status == "sleep") score -= 1.5;
            else if (mon.Status == "sick" || mon.Status == "hypno") score -= 1.0;
            return score + (hp >= .7 ? 1 : hp < .35 ? -2 : 0);
        }

        private void EnemyTurn()
        {
            var actor = Enemy; var target = Player;
            if (actor.Status == "sleep") { Emit("sleep", actor, "FAST ASLEEP", status: "sleep"); return; }
            var cfg = Encounter.Ai;
            if (cfg != null)
            {
                if (enemyAntidotes > 0 && (actor.Status == "hypno" || actor.Status == "sick" && actor.Hp > actor.MaxHp * .25))
                { enemyAntidotes--; Resolve(actor, target, data.Move("item-antidote")); return; }
                if (enemyHeals > 0 && actor.Hp <= actor.MaxHp * cfg.HealTrigger)
                { enemyHeals--; Resolve(actor, target, data.Move("item-heal_spray"), actor.Sparring ? 25 : (int?)null); return; }
                if (Turn - lastSwitchTurn >= 2)
                {
                    var next = Opponents.Where(mon => mon != actor && mon.Hp > 0).OrderByDescending(mon => Matchup(mon, target)).FirstOrDefault();
                    if (next != null && Matchup(next, target) >= Matchup(actor, target) + 2.5)
                    {
                        EnemySlot = Array.IndexOf(Opponents, next); lastSwitchTurn = Turn; Emit("switch", next, next.MonId.ToUpperInvariant()); return;
                    }
                }
                if (!Opponents.Any(mon => mon.Mega) && (Turn >= 2 && actor.Hp <= actor.MaxHp * .85 || Opponents.Count(mon => mon.Hp > 0) == 1))
                { TriggerMegaEvolution(actor); }
                if (Turn <= 4 && actor.Hp >= actor.MaxHp * .78 && !actor.Moves.Any(move => actor.Ready(move) && move.power > 0 && Damage(actor, target, move) >= target.Hp))
                {
                    if (enemyBerries > 0 && GameData.Strong(target.Kind, actor.Kind)) { enemyBerries--; Resolve(actor, target, data.Move("item-def_berry")); return; }
                    if (enemyPeppers > 0 && rng() < cfg.PepperOdds) { enemyPeppers--; Resolve(actor, target, data.Move("item-atk_pepper")); return; }
                }
            }
            var options = actor.Moves.Where(actor.Ready).Select(move => (move, score: Score(move))).OrderByDescending(row => row.score).ToArray();
            if (options.Length == 0) return;
            var pool = cfg == null ? options.Take((options.Length + 1) / 2).ToArray() : options.Where(row => row.score >= options[0].score - 1.5).ToArray();
            Resolve(actor, target, pool[(int)(rng() * pool.Length)].move);

            double Score(MoveData move)
            {
                bool isAttack = move.power > 0 && move.effect.Contains("attack");
                double score = move.power * data.KindFactor(move.kind, target.Kind);
                if (cfg == null)
                {
                    if (move.effect.Contains("state_") && target.Status == null) score += 6;
                    if (move.effect.Contains("heal") && actor.Hp < actor.MaxHp * .45) score += 16;
                    return score;
                }
                int estDmg = isAttack ? Damage(actor, target, move) : 0;
                if (target.Shield > 0 && isAttack)
                {
                    score = move.cooldown > 0 ? score - 35 : 22;
                    return score;
                }
                if (estDmg >= target.Hp && target.Shield <= 0) score += 200 - move.cooldown * 6;
                if (Charged(actor, move)) score += 22;
                if (move.effect.Contains("heal") && actor.Hp < actor.MaxHp * .85) score += 28 * (actor.MaxHp - actor.Hp) / actor.MaxHp;
                if (target.Status == null && (move.effect.Contains("state_hypno") || move.effect.Contains("state_sick") || move.effect.Contains("state_sleep")))
                    score += 10 * (Charged(actor, move) ? 1.5 : move.chance);
                if (target.Status == "sleep")
                {
                    if (move.effect.Contains("incr_")) score += 16;
                    else if (isAttack && estDmg < target.Hp) score += estDmg >= 24 ? 12 : -8;
                }
                if (actor.Hp > actor.MaxHp * .45 && move.effect.Contains("incr_")) score += 8;
                if (target.Hp > 35 && move.effect.Contains("decr_") && target.Mods.atk > -20 && target.Mods.def > -20 && target.Mods.aim > -20) score += 6;
                return score;
            }
        }
    }
}
