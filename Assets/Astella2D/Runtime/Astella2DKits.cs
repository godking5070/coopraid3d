using System;
using System.Collections.Generic;

namespace Astella.TwoD
{
    public enum PixelRole
    {
        Tank,
        Healer,
        Melee,
        Ranged,
        Caster
    }

    /// <summary>One authored 2D skill. Every nonzero field is an effect applied in order.</summary>
    public sealed class SkillSpec
    {
        public string Name = "", Description = "";
        public float Cooldown, Range, Damage;
        public float Delay;            // Delayed area damage, shown as a friendly marker.
        public float PartyHeal;        // Fraction of each living member's maximum health.
        public float SelfHeal;         // Fraction of the user's maximum health.
        public bool Revive;            // Raises fallen allies at ReviveHealth.
        public float Guard, GuardTime; // Damage reduction fraction and seconds.
        public bool GuardParty;
        public float Empower, EmpowerTime; // Bonus damage fraction and seconds.
        public float Leap;             // Backstep distance away from the boss, invulnerable.
        public bool NeedsRange => Damage > 0;
    }

    public sealed class RoleKit
    {
        public PixelRole Role;
        public string RoleName = "", Summary = "";
        public float MaxHp, AllyDamage, AllyInterval, AllyDistance;
        public bool Ranged;
        public SkillSpec Basic = new SkillSpec(), Primary = new SkillSpec(), Utility = new SkillSpec(), Ultimate = new SkillSpec();

        /// <summary>Slots 0 basic, 1 Q, 2 E, 3 R. Slot 4 is the shared dash.</summary>
        public SkillSpec Skill(int slot)
        {
            switch (slot)
            {
                case 0: return Basic;
                case 1: return Primary;
                case 2: return Utility;
                case 3: return Ultimate;
                default: throw new ArgumentOutOfRangeException(nameof(slot), slot, "Only slots 0-3 are kit skills.");
            }
        }
    }

    public static class Astella2DKits
    {
        public const float DashCooldown = 2.2f;
        public const float DashTime = .22f;
        public const float DashSpeed = 12.8f;
        public const float MoveSpeed = 5.2f;
        public const float UltimateCost = 100f;
        public const float UltimatePerSecond = 1.2f;
        public const float UltimatePerBasic = 1f;
        public const float UltimatePerPrimary = 5f;
        public const float ReviveHealth = .4f;

        private static readonly RoleKit[] Kits =
        {
            new RoleKit
            {
                Role = PixelRole.Tank, RoleName = "수호", Ranged = false,
                MaxHp = 1700, AllyDamage = 58, AllyInterval = 1.2f, AllyDistance = 2.3f,
                Summary = "높은 체력과 방벽으로 파티의 피해를 줄입니다.",
                Basic = new SkillSpec { Name = "검격", Description = "근접 일격", Cooldown = .6f, Range = 2.8f, Damage = 140 },
                Primary = new SkillSpec { Name = "수호 강타", Description = "강타 후 3초간 받는 피해 35% 감소", Cooldown = 5f, Range = 3.4f, Damage = 380, Guard = .35f, GuardTime = 3 },
                Utility = new SkillSpec { Name = "결속의 방벽", Description = "4초간 파티 전원 받는 피해 50% 감소", Cooldown = 14f, Guard = .5f, GuardTime = 4, GuardParty = true },
                Ultimate = new SkillSpec { Name = "불굴의 맹세", Description = "강력한 일격과 3초간 파티 피해 80% 감소", Range = 4f, Damage = 1900, Guard = .8f, GuardTime = 3, GuardParty = true }
            },
            new RoleKit
            {
                Role = PixelRole.Healer, RoleName = "치유", Ranged = true,
                MaxHp = 1200, AllyDamage = 42, AllyInterval = 1.3f, AllyDistance = 4.6f,
                Summary = "파티를 회복하고 쓰러진 동료를 일으킵니다.",
                Basic = new SkillSpec { Name = "빛의 화살", Description = "원거리 사격", Cooldown = .5f, Range = 9f, Damage = 100 },
                Primary = new SkillSpec { Name = "정화의 빛", Description = "공격하며 파티 체력 12% 회복", Cooldown = 5f, Range = 9f, Damage = 300, PartyHeal = .12f },
                Utility = new SkillSpec { Name = "은빛 가호", Description = "파티 체력 40% 회복, 쓰러진 동료 소생", Cooldown = 12f, PartyHeal = .4f, Revive = true },
                Ultimate = new SkillSpec { Name = "별의 찬가", Description = "파티 완전 회복·전원 소생과 빛의 피해", Range = 99f, Damage = 1200, PartyHeal = 1f, Revive = true }
            },
            new RoleKit
            {
                Role = PixelRole.Melee, RoleName = "근접", Ranged = false,
                MaxHp = 1250, AllyDamage = 78, AllyInterval = 1.1f, AllyDistance = 2.5f,
                Summary = "가까이 붙어 가장 큰 피해를 줍니다.",
                Basic = new SkillSpec { Name = "연격", Description = "빠른 근접 연속 공격", Cooldown = .45f, Range = 2.6f, Damage = 150 },
                Primary = new SkillSpec { Name = "섬광 베기", Description = "강력한 베기", Cooldown = 5f, Range = 3.4f, Damage = 660 },
                Utility = new SkillSpec { Name = "전투 집중", Description = "6초간 피해 50% 증가, 체력 15% 회복", Cooldown = 15f, Empower = .5f, EmpowerTime = 6, SelfHeal = .15f },
                Ultimate = new SkillSpec { Name = "낙성 일섬", Description = "근접 거대 일격", Range = 3.8f, Damage = 2900 }
            },
            new RoleKit
            {
                Role = PixelRole.Ranged, RoleName = "원거리", Ranged = true,
                MaxHp = 1100, AllyDamage = 72, AllyInterval = 1.15f, AllyDistance = 4.8f,
                Summary = "먼 거리에서 꾸준히 공격하고 뒤로 물러납니다.",
                Basic = new SkillSpec { Name = "속사", Description = "원거리 연사", Cooldown = .4f, Range = 10f, Damage = 100 },
                Primary = new SkillSpec { Name = "관통 사격", Description = "강한 단일 사격", Cooldown = 5f, Range = 10f, Damage = 540 },
                Utility = new SkillSpec { Name = "후퇴 사격", Description = "보스 반대로 도약하며 사격, 도약 중 무적", Cooldown = 9f, Range = 10f, Damage = 280, Leap = 3.2f },
                Ultimate = new SkillSpec { Name = "유성우", Description = "하늘에서 쏟아지는 화살", Range = 99f, Damage = 2500 }
            },
            new RoleKit
            {
                Role = PixelRole.Caster, RoleName = "마법", Ranged = true,
                MaxHp = 1050, AllyDamage = 72, AllyInterval = 1.25f, AllyDistance = 4.4f,
                Summary = "지연 폭발로 큰 피해를 주고 보호막으로 버팁니다.",
                Basic = new SkillSpec { Name = "마력탄", Description = "원거리 마력 구체", Cooldown = .55f, Range = 9f, Damage = 95 },
                Primary = new SkillSpec { Name = "성운 폭발", Description = "0.7초 뒤 터지는 강한 폭발", Cooldown = 6f, Range = 9f, Damage = 860, Delay = .7f },
                Utility = new SkillSpec { Name = "마력 보호막", Description = "4초간 받는 피해 60% 감소", Cooldown = 13f, Guard = .6f, GuardTime = 4 },
                Ultimate = new SkillSpec { Name = "별의 붕괴", Description = "0.9초 뒤 거대한 폭발", Range = 99f, Damage = 3000, Delay = .9f }
            }
        };

        public static RoleKit For(PixelRole role)
        {
            foreach (var kit in Kits) if (kit.Role == role) return kit;
            throw new ArgumentOutOfRangeException(nameof(role), role, "No 2D kit for this role.");
        }

        public static IReadOnlyList<RoleKit> All => Kits;
    }

    public enum BossPattern
    {
        TargetCircle,
        Donut,
        TripleLine,
        Cone,
        StarFall,
        ChaseCircles,
        RotatingBeams,
        InOut,
        Cross
    }

    public sealed class EncounterSpec
    {
        public string Name = "", Title = "";
        public float BossHp, HazardDamage, CastScale, Interval, EnragedInterval;
        public float TintR = 1, TintG = 1, TintB = 1;
        public BossPattern[] Patterns = Array.Empty<BossPattern>();
    }

    public static class Astella2DEncounters
    {
        public const int Count = 3;

        private static readonly EncounterSpec[] Specs =
        {
            new EncounterSpec
            {
                Name = "공명 수호자", Title = "첫 번째 공명", BossHp = 30000, HazardDamage = 260, CastScale = 1f,
                Interval = 3.6f, EnragedInterval = 2.8f,
                Patterns = new[] { BossPattern.TargetCircle, BossPattern.Donut, BossPattern.TripleLine, BossPattern.Cone, BossPattern.StarFall }
            },
            new EncounterSpec
            {
                Name = "격앙한 수호자", Title = "두 번째 공명", BossHp = 42000, HazardDamage = 300, CastScale = .94f,
                Interval = 3.3f, EnragedInterval = 2.6f, TintR = 1f, TintG = .82f, TintB = .9f,
                Patterns = new[] { BossPattern.TargetCircle, BossPattern.Donut, BossPattern.TripleLine, BossPattern.Cone, BossPattern.StarFall, BossPattern.ChaseCircles, BossPattern.RotatingBeams }
            },
            new EncounterSpec
            {
                Name = "각성한 수호자", Title = "마지막 공명", BossHp = 56000, HazardDamage = 340, CastScale = .88f,
                Interval = 3.1f, EnragedInterval = 2.4f, TintR = .82f, TintG = .86f, TintB = 1.05f,
                Patterns = new[] { BossPattern.TargetCircle, BossPattern.Donut, BossPattern.TripleLine, BossPattern.Cone, BossPattern.StarFall, BossPattern.ChaseCircles, BossPattern.RotatingBeams, BossPattern.InOut, BossPattern.Cross }
            }
        };

        public static EncounterSpec Get(int encounter)
        {
            if (encounter < 0 || encounter >= Specs.Length)
                throw new ArgumentOutOfRangeException(nameof(encounter), encounter, "Encounters are 0-2.");
            return Specs[encounter];
        }

        /// <summary>The first cast of every encounter is the targeted circle; later casts never repeat the previous one.</summary>
        public static BossPattern Next(EncounterSpec spec, int castIndex, BossPattern previous, Random random)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            if (random == null) throw new ArgumentNullException(nameof(random));
            if (castIndex <= 0 || spec.Patterns.Length < 2) return spec.Patterns.Length > 0 ? spec.Patterns[0] : BossPattern.TargetCircle;
            BossPattern next;
            do next = spec.Patterns[random.Next(spec.Patterns.Length)];
            while (next == previous);
            return next;
        }
    }

    public sealed class Blessing
    {
        public int Id;
        public string Name = "", Description = "";
    }

    /// <summary>Run-long bonuses chosen after each won encounter.</summary>
    public sealed class RunModifiers
    {
        public float Damage = 1, Health = 1, DashCooldown = 1, UltimateGain = 1, DamageTaken = 1, AllyDamage = 1, Cooldown = 1;
        public int ExtraRevives;

        public void Apply(int blessing)
        {
            switch (blessing)
            {
                case 0: Damage *= 1.25f; break;
                case 1: Health *= 1.3f; break;
                case 2: DashCooldown *= .75f; break;
                case 3: UltimateGain *= 1.4f; break;
                case 4: DamageTaken *= .8f; break;
                case 5: ExtraRevives += 1; break;
                case 6: AllyDamage *= 1.4f; break;
                case 7: Cooldown *= .8f; break;
                default: throw new ArgumentOutOfRangeException(nameof(blessing), blessing, "Unknown blessing.");
            }
        }
    }

    public static class Astella2DBlessings
    {
        public const int StartingRevives = 2;

        private static readonly Blessing[] Catalog =
        {
            new Blessing { Id = 0, Name = "별빛의 칼날", Description = "공격력 +25%" },
            new Blessing { Id = 1, Name = "은빛 생명", Description = "최대 체력 +30%" },
            new Blessing { Id = 2, Name = "공명의 발걸음", Description = "대시 대기시간 -25%" },
            new Blessing { Id = 3, Name = "공명 가속", Description = "궁극기 충전 +40%" },
            new Blessing { Id = 4, Name = "수정 갑주", Description = "받는 피해 -20%" },
            new Blessing { Id = 5, Name = "별의 가호", Description = "전투마다 부활 +1회" },
            new Blessing { Id = 6, Name = "동료의 결의", Description = "동료 공격력 +40%" },
            new Blessing { Id = 7, Name = "신속한 손놀림", Description = "기술 대기시간 -20%" }
        };

        public static IReadOnlyList<Blessing> All => Catalog;

        /// <summary>Draws distinct blessings in random order.</summary>
        public static Blessing[] Draw(Random random, int count)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            if (count < 0 || count > Catalog.Length) throw new ArgumentOutOfRangeException(nameof(count));
            var pool = new List<Blessing>(Catalog);
            var result = new Blessing[count];
            for (int i = 0; i < count; i++)
            {
                int pick = random.Next(pool.Count);
                result[i] = pool[pick];
                pool.RemoveAt(pick);
            }
            return result;
        }
    }

    public static class Astella2DDamage
    {
        /// <summary>Incoming damage after the strongest active guard and run modifiers. Never negative.</summary>
        public static float Incoming(float amount, float guard, float damageTaken)
        {
            if (float.IsNaN(amount) || float.IsInfinity(amount) || amount <= 0) return 0;
            float g = float.IsNaN(guard) ? 0 : Math.Max(0, Math.Min(.95f, guard));
            float t = float.IsNaN(damageTaken) || damageTaken < 0 ? 1 : damageTaken;
            return amount * (1 - g) * t;
        }
    }
}
