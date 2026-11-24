using System;
using UnityEngine;

namespace Dynamons
{
    public static class LanConst
    {
        public const int ProtocolVersion = 1;
        public const int DiscoveryPort = 41975;
        public const int BattlePortBase = 41976;
        public const int BattlePortSpan = 10;
        public const float TurnTimeoutSec = 30f;
        public const float AnimGraceSec = 6f;
        public const float PingIntervalSec = 3f;
        public const float PeerTimeoutSec = 12f;
        public const int MaxFrameBytes = 65536;
    }

    [Serializable]
    public sealed class LanWireEnvelope
    {
        public string type;
        public string payload;
    }

    [Serializable]
    public sealed class LanBeaconDto
    {
        public int protocolVersion;
        public string roomId;
        public string hostName;
        public string ruleMode;
        public bool itemsAllowed;
        public int tcpPort;
        public int teamCount;
    }

    [Serializable]
    public sealed class LanTeamMemberDto
    {
        public string monId;
        public int level;
    }

    [Serializable]
    public sealed class LanHelloDto
    {
        public int proto;
        public string playerName;
        public string avatarId;
        public int partySize;
        public LanTeamMemberDto[] mons;
    }

    [Serializable]
    public sealed class LanRoomConfigDto
    {
        public string ruleMode;
        public bool itemsAllowed;
    }

    [Serializable]
    public sealed class LanItemSlotDto
    {
        public string id;
        public int count;
    }

    [Serializable]
    public sealed class LanPvpMonDto
    {
        public string id;
        public string monId;
        public string kind;
        public int level;
        public int hp;
        public int maxHp;
        public int atk;
        public int def;
        public int aim;
        public int shield;
        public string[] moveIds;
    }

    [Serializable]
    public sealed class LanStartMatchDto
    {
        public string ruleMode;
        public bool itemsAllowed;
        public string startingSide;
        public string hostName;
        public string clientName;
        public LanPvpMonDto[] hostTeam;
        public LanPvpMonDto[] clientTeam;
        public LanItemSlotDto[] hostItems;
        public LanItemSlotDto[] clientItems;
    }

    [Serializable]
    public sealed class LanActionCmdDto
    {
        public int seq;
        public string actionType;
        public string id;
        public int index;
    }

    [Serializable]
    public sealed class LanCombatantSnapshotDto
    {
        public string id;
        public string monId;
        public string kind;
        public int level;
        public int hp;
        public int maxHp;
        public string status;
        public bool rage;
        public bool mega;
        public int shield;
        public int voidStacks;
        public int powerStacks;
        public int charges;
        public int atkMod;
        public int defMod;
        public int aimMod;
        public string[] cooldownIds;
        public int[] cooldownTurns;
    }

    [Serializable]
    public sealed class LanEventDto
    {
        public string type;
        public string target;
        public string text;
        public string stat;
        public string actor;
        public string abilityId;
        public string side;
        public string monId;
        public string variant;
        public string status;
        public bool fullPower;
        public bool backfire;
        public bool missed;
        public bool success;
        public bool ready;
        public int amount;
        public int hp = -1;
        public int shieldHits;
        public int stacks;
        public int bonusPct;
        public int charges;
        public int threshold;
        public VisualStep[] visualSteps;
    }

    [Serializable]
    public sealed class LanTurnResultDto
    {
        public int seq;
        public string actorSide;
        public string nextTurnSide;
        public string phase;
        public string forcedSwitchSide;
        public string outcome;
        public int activeHostSlot;
        public int activeClientSlot;
        public int turnNumber;
        public bool turnEnded;
        public LanEventDto[] events;
        public LanCombatantSnapshotDto[] hostTeam;
        public LanCombatantSnapshotDto[] clientTeam;
        public LanItemSlotDto[] hostItems;
        public LanItemSlotDto[] clientItems;
    }

    [Serializable]
    public sealed class LanAnimDoneDto
    {
        public int seq;
    }

    [Serializable]
    public sealed class LanPingDto
    {
        public long ts;
    }

    public static class LanCodec
    {
        public static string Encode<T>(string type, T payload)
        {
            return JsonUtility.ToJson(new LanWireEnvelope
            {
                type = type,
                payload = payload == null ? "{}" : JsonUtility.ToJson(payload)
            });
        }

        public static T Decode<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return default(T);
            try
            {
                return JsonUtility.FromJson<T>(json);
            }
            catch (ArgumentException)
            {
                return default(T);
            }
            catch (Exception)
            {
                return default(T);
            }
        }

        public static string FlipSide(string side) => side switch
        {
            "host" => "client",
            "client" => "host",
            "player" => "enemy",
            "enemy" => "player",
            _ => side
        };

        public static string ToLocalSide(string wireSide, string localSide)
        {
            if (wireSide != "host" && wireSide != "client") return wireSide;
            return wireSide == localSide ? "player" : "enemy";
        }

        public static LanEventDto EventToDto(BattleEvent ev, string wireSide)
        {
            if (ev == null) return null;
            string mappedSide = ev.Side == "player"
                ? (!string.IsNullOrEmpty(wireSide) ? wireSide : "player")
                : ev.Side == "enemy"
                    ? (!string.IsNullOrEmpty(wireSide) ? FlipSide(wireSide) : "enemy")
                    : ev.Side;

            return new LanEventDto
            {
                type = ev.Type,
                target = ev.Target,
                text = ev.Text,
                stat = ev.Stat,
                actor = ev.Actor,
                abilityId = ev.AbilityId,
                side = mappedSide,
                monId = ev.MonId,
                variant = ev.Variant,
                status = ev.Status,
                fullPower = ev.FullPower,
                backfire = ev.Backfire,
                missed = ev.Missed,
                success = ev.Success,
                ready = ev.Ready,
                amount = ev.Amount,
                hp = ev.Hp,
                shieldHits = ev.ShieldHits,
                stacks = ev.Stacks,
                bonusPct = ev.BonusPct,
                charges = ev.Charges,
                threshold = ev.Threshold,
                visualSteps = CloneVisualSteps(ev.Visuals)
            };
        }

        public static BattleEvent EventFromDto(LanEventDto dto, string localSide)
        {
            if (dto == null) return null;
            return new BattleEvent
            {
                Type = dto.type,
                Target = dto.target,
                Text = dto.text,
                Stat = dto.stat,
                Actor = dto.actor,
                AbilityId = dto.abilityId,
                Side = ToLocalSide(dto.side, localSide),
                MonId = dto.monId,
                Variant = dto.variant,
                Status = dto.status,
                FullPower = dto.fullPower,
                Backfire = dto.backfire,
                Missed = dto.missed,
                Success = dto.success,
                Ready = dto.ready,
                Amount = dto.amount,
                Hp = dto.hp,
                ShieldHits = dto.shieldHits,
                Stacks = dto.stacks,
                BonusPct = dto.bonusPct,
                Charges = dto.charges,
                Threshold = dto.threshold,
                Visuals = CloneVisualSteps(dto.visualSteps)
            };
        }

        private static VisualStep[] CloneVisualSteps(VisualStep[] steps)
        {
            if (steps == null) return null;
            var copy = new VisualStep[steps.Length];
            for (int i = 0; i < steps.Length; i++)
            {
                var step = steps[i];
                if (step == null) continue;
                copy[i] = new VisualStep
                {
                    action = step.action,
                    inHypno = step.inHypno,
                    at = step.at,
                    duration = step.duration
                };
            }
            return copy;
        }
    }
}
