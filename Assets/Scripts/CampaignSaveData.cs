using System;
using System.Collections.Generic;
using System.Linq;

namespace RaidRogue
{
    // Content availability is intentionally separate from the authored campaign unlock rules.
    // The shipped three-boss prototype must never masquerade as the six-stage first chapter.
    public static class CampaignCatalog
    {
        public const string Prototype = "main", PartOne = "campaign.part1", PartTwo = "campaign.part2";
        public const string Normal = "normal", ContentRevision = "prototype-3-roster12-v1";
        public static int StageCount(string chapter) => chapter == Prototype ? 3 : chapter == PartOne ? 6 : chapter == PartTwo ? 8 : 0;
        public static int PartySize(string chapter) => chapter == PartTwo ? 8 : StageCount(chapter) > 0 ? 4 : 0;
        public static bool Available(string chapter) => chapter == Prototype;
        public static string Name(string chapter) => chapter == Prototype ? "프로토타입 · 3스테이지" : chapter == PartOne ? "1부 · 6스테이지" : chapter == PartTwo ? "2부 · 8스테이지" : chapter;
        public static string DepartureRequirement(string chapter) => chapter == PartTwo ? "2부 콘텐츠·8인 파티 및 직접 출발 R/F/V 선택 정책 연결 필요" : "기원 선택 후 출발";
    }

    [Serializable] public sealed class CampaignClearReceipt
    {
        public string runId, chapterId, difficultyId, userId;
        public int stage;
        public bool final, eligible;
        public string Key => CharacterRewardKey.Stage(runId, chapterId, stage);
    }
    [Serializable] public sealed class CampaignPersonalProgress
    {
        public int version = 1;
        public string owner;
        public CampaignClearReceipt[] clears = Array.Empty<CampaignClearReceipt>();
        public string[] unlocks = Array.Empty<string>();
        public bool PartOneComplete => Enumerable.Range(1, 6).All(n => clears.Any(c => c.eligible && c.chapterId == CampaignCatalog.PartOne && c.stage == n && (n != 6 || c.final)));
        public bool PartTwoUnlocked => PartOneComplete;
        public bool Record(CampaignClearReceipt receipt)
        {
            if (!CampaignValidation.Receipt(receipt, owner) || !receipt.eligible || clears.Any(c => c.Key == receipt.Key)) return false;
            clears = clears.Concat(new[] { receipt }).ToArray();
            if (PartTwoUnlocked) unlocks = unlocks.Union(new[] { CampaignCatalog.PartTwo }).ToArray();
            return true;
        }
    }
    [Serializable] public sealed class CampaignCardStack { public string id; public int stacks; }
    [Serializable] public sealed class CampaignPartySlot
    {
        public string slotId, userId, name;
        public int role;
        public bool bot;
        public CharacterStateSnapshot character;
        public CampaignCardStack[] cards = Array.Empty<CampaignCardStack>();
        // Boundary policy v1: full health, full charges, no consumable stock (potion is unlimited).
        public float healthFraction = 1, potionCooldown;
    }
    [Serializable] public sealed class CampaignRewardRecord
    {
        public string key, slotId, selectedId;
        public string[] offeredIds = Array.Empty<string>();
    }
    [Serializable] public sealed class CampaignMetricMember
    {
        public string slotId, name, jobId;
        public int role, deaths;
        public double bossDamage, addDamage, healing, selfHealing, shield;
    }
    [Serializable] public sealed class CampaignMetricRecord
    {
        public int stage; public double seconds;
        public CampaignMetricMember[] members = Array.Empty<CampaignMetricMember>();
    }
    [Serializable] public sealed class CampaignCheckpoint
    {
        public int version = 1;
        public string content = CampaignCatalog.ContentRevision, owner, runId, chapterId = CampaignCatalog.Prototype, difficultyId = CampaignCatalog.Normal;
        public string savedUtc, transport = "single";
        public int nextStage = 1, retries, irisCoins = 3, seed;
        public uint randomState;
        public float runSeconds;
        public bool completed, eligible = true;
        public CampaignPartySlot[] party = Array.Empty<CampaignPartySlot>();
        public string[] processedStages = Array.Empty<string>();
        public CampaignRewardRecord[] rewards = Array.Empty<CampaignRewardRecord>();
        // Undelivered host-confirmed events for original remote users only. Loading a checkpoint
        // never records host clears or executes stage/reward callbacks from its history.
        public CampaignClearReceipt[] receipts = Array.Empty<CampaignClearReceipt>();
        public CampaignMetricRecord[] clears = Array.Empty<CampaignMetricRecord>(), failures = Array.Empty<CampaignMetricRecord>();
        public string BoundaryKey => runId + "/" + chapterId + "/start/" + nextStage + (completed ? "/complete" : "");
    }

    public static class CampaignIdentity
    {
        public static string ForSteam(ulong id) => id == 0 ? "offline-local" : "steam-" + id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        public static bool Valid(string id) => id == "offline-local" || id != null && id.StartsWith("steam-", StringComparison.Ordinal) && ulong.TryParse(id.Substring(6), out ulong value) && value > 0 && ForSteam(value) == id;
        public static ulong Steam(string id) => Valid(id) && id.StartsWith("steam-", StringComparison.Ordinal) ? ulong.Parse(id.Substring(6), System.Globalization.CultureInfo.InvariantCulture) : 0;
    }

    public static class CampaignCardIds
    {
        private static readonly string[] Base = { "card.rear", "card.ghost-step", "card.thorn", "card.rescue", "card.shout", "card.vampiric", "card.swift" };
        // Explicit stable job mapping, independent of UI names and enum ordinal persistence.
        private static readonly string[] Jobs = { "aegis-paladin", "berserker", "dragon-breaker", "sylvan-oracle", "seraphic-bard", "blood-apothecary", "nightblade", "tempest-lancer", "gunslinger", "spirit-ranger", "astromancer", "mirror-phantom" };
        public static string Id(int index) => index < 0 || index >= 31 ? null : index < 7 ? Base[index] : "card." + Jobs[(index - 7) / 2] + ((index - 7) % 2 == 0 ? ".overcome" : ".maximize");
        public static int Index(string id) { for (int i = 0; i < 31; i++) if (Id(i) == id) return i; return -1; }
        public static bool Fits(string id, string jobId) { int i = Index(id); return i >= 0 && (i < 7 || jobId == "job." + Jobs[(i - 7) / 2]); }
        public static JobBuildEffect[] Effects(CampaignCardStack[] cards)
        {
            var effects = cards.Select(c => new JobBuildEffect { id = c.id, stacks = c.stacks }).ToList();
            if (cards.Any(c => Index(c.id) >= 7 && (Index(c.id) - 7) % 2 == 0)) effects.Add(new JobBuildEffect { id = "evolution.overcome", stacks = 1 });
            if (cards.Any(c => Index(c.id) >= 7 && (Index(c.id) - 7) % 2 == 1)) effects.Add(new JobBuildEffect { id = "evolution.maximize", stacks = 1 });
            return effects.ToArray();
        }
    }

    // Own PRNG for reward offers. Combat cosmetics/random calls cannot change the next card draw.
    public sealed class CampaignRandom
    {
        public uint State;
        public CampaignRandom(uint state) { State = state == 0 ? 2463534242u : state; }
        public int Next(int count)
        {
            if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
            uint x = State; x ^= x << 13; x ^= x >> 17; x ^= x << 5; State = x;
            return (int)(x % (uint)count);
        }
    }

    public static class CampaignValidation
    {
        public static bool Receipt(CampaignClearReceipt r, string owner) => r != null && CampaignIdentity.Valid(owner) && r.userId == owner &&
            Guid.TryParseExact(r.runId, "N", out _) && r.difficultyId == CampaignCatalog.Normal && r.stage > 0 && r.stage <= CampaignCatalog.StageCount(r.chapterId) && r.final == (r.stage == CampaignCatalog.StageCount(r.chapterId));
        public static bool Personal(CampaignPersonalProgress p, string owner) => p != null && p.version == 1 && p.owner == owner && CampaignIdentity.Valid(owner) && p.clears != null && p.clears.Length <= 50000 &&
            p.clears.All(c => Receipt(c, owner) && c.eligible) && p.clears.Select(c => c.Key).Distinct().Count() == p.clears.Length && p.unlocks != null && p.unlocks.All(u => u == CampaignCatalog.PartTwo && p.PartOneComplete);
        public static string Checkpoint(CampaignCheckpoint c, string owner, Func<string, CharacterDefinition> find)
        {
            if (c == null || c.version != 1 || c.content != CampaignCatalog.ContentRevision) return "지원하지 않는 저장 버전 또는 콘텐츠입니다";
            if (!CampaignIdentity.Valid(owner) || c.owner != owner || !Guid.TryParseExact(c.runId, "N", out _) || !DateTimeOffset.TryParse(c.savedUtc, out _) ||
                !CampaignCatalog.Available(c.chapterId) || c.difficultyId != CampaignCatalog.Normal || c.transport != "single" && c.transport != "steam") return "계정/런/챕터/난이도 정보가 유효하지 않습니다";
            int last = CampaignCatalog.StageCount(c.chapterId);
            if (!c.eligible || c.nextStage < 1 || c.nextStage > last || c.completed && c.nextStage != last || c.retries < 0 || c.retries > 100000 || !Finite(c.runSeconds) || c.runSeconds < 0 || c.randomState == 0 || c.irisCoins != 3) return "스테이지 경계 상태가 유효하지 않습니다";
            if (c.party == null || c.party.Length != CampaignCatalog.PartySize(c.chapterId) || c.party.Any(p => p == null) || c.party.Select(p => p.slotId).Distinct().Count() != c.party.Length || c.party.Select(p => p.role).Distinct().Count() != c.party.Length || c.party.Count(p => p.userId == owner && !p.bot) != 1) return "원래 파티 구성 정보가 유효하지 않습니다";
            var people = c.party.Where(p => !p.bot).ToArray();
            if (people.Select(p => p.userId).Distinct().Count() != people.Length || c.transport == "single" && people.Length != 1 || c.transport == "steam" && people.Any(p => CampaignIdentity.Steam(p.userId) == 0)) return "사용자 식별 정보가 유효하지 않습니다";
            int finished = c.completed ? last : c.nextStage - 1;
            string[] expected = Enumerable.Range(1, finished).Select(n => CharacterRewardKey.Stage(c.runId, c.chapterId, n)).ToArray();
            if (c.processedStages == null || !expected.SequenceEqual(c.processedStages) || c.rewards == null || c.rewards.Length > 128 || c.receipts == null || c.receipts.Length > finished * people.Length || c.receipts.Any(r => !Receipt(r, r?.userId) || !r.eligible || r.userId == owner || r.runId != c.runId || r.chapterId != c.chapterId || !expected.Contains(r.Key) || !people.Any(p => p.userId == r.userId)) || c.receipts.Select(r => r.userId + "/" + r.Key).Distinct().Count() != c.receipts.Length) return "클리어/보상 기록이 일치하지 않습니다";
            foreach (var p in c.party)
            {
                if (!Guid.TryParseExact(p.slotId, "N", out _) || p.role < 1 || p.role > 4 || p.name == null || p.name.Length > 80 || p.bot && p.userId != "" || !p.bot && !CampaignIdentity.Valid(p.userId) || p.healthFraction != 1 || p.potionCooldown != 0) return "파티 슬롯 경계 정보가 유효하지 않습니다";
                var s = p.character; var d = s == null ? null : find(s.jobId);
                if (d == null || !d.playable || CharacterRosterData.Role(d) != p.role || p.bot && d.legacyRole == 0 || !new CharacterRuntimeState(d).Restore(s)) return "직업/루트/스킬 저장을 복구할 수 없습니다";
                if (s.runId != c.runId || s.chapterId != c.chapterId || s.clearedStage != finished || !s.clearedEncounters.OrderBy(x => x).SequenceEqual(expected.OrderBy(x => x)) || s.castSkillId != "" || s.castTarget != 0 || s.gcd != 0 || s.comboTime1 != 0 || s.comboTime2 != 0 || s.combo1 != 1 || s.combo2 != 1 || s.skills.Any(v => v.recharge != 0 || v.charges != d.Skill(v.id).maximumCharges)) return "시전/충전/클리어 상태가 안전한 경계가 아닙니다";
                if (d.routes.Length > 0 && !d.routes.Any(r => r.id == s.routeId)) return "기원 선택이 누락되었습니다";
                foreach (var rule in d.unlocks.Where(r => r.chapterId == c.chapterId))
                    if ((rule.afterStage <= finished) != s.chosenRules.Contains(rule.id)) return "필수 스킬 선택이 누락되었거나 선행 해금되었습니다";
                foreach (var rule in d.unlocks.Where(r => !s.chosenRules.Contains(r.id)))
                    if (s.selections.First(v => v.slot == rule.slot).skillId != d.slots.First(v => v.slot == rule.slot).skillId) return "잠긴 슬롯의 후보가 미리 변경되었습니다";
                var origin = d.routes.FirstOrDefault(r => r.id == s.routeId);
                var unlocked = d.slots.Where(v => v.initiallyUnlocked).Select(v => v.skillId)
                    .Concat(origin?.unlockSkills ?? Array.Empty<string>()).Concat(origin == null ? Array.Empty<string>() : new[] { origin.identitySkillId })
                    .Concat(d.unlocks.Where(r => s.chosenRules.Contains(r.id)).Select(r => s.selections.First(v => v.slot == r.slot).skillId)).Distinct().OrderBy(x => x);
                if (!unlocked.SequenceEqual(s.unlocked.OrderBy(x => x))) return "확정된 선택과 스킬 잠금 상태가 다릅니다";
                if (p.cards == null || p.cards.Length > 31 || p.cards.Any(k => k == null || !CampaignCardIds.Fits(k.id, d.id) || k.stacks < 1 || k.stacks > last) || p.cards.Select(k => k.id).Distinct().Count() != p.cards.Length) return "카드 ID 또는 중첩이 유효하지 않습니다";
                if (p.cards.Sum(k => k.stacks) != (p.bot ? 0 : Math.Min(finished, last - 1))) return "카드 보상 수량이 일치하지 않습니다";
                var effects = CampaignCardIds.Effects(p.cards);
                if (s.effects.Length != effects.Length || s.effects.Any(e => e.magnitude != 0 || !effects.Any(v => v.id == e.id && v.stacks == e.stacks))) return "카드와 영구 보정이 일치하지 않습니다";
            }
            if (c.rewards.Any(r => r == null || !c.party.Any(p => p.slotId == r.slotId) || r.offeredIds == null || r.offeredIds.Length != 3 || r.offeredIds.Distinct().Count() != 3 || r.offeredIds.Any(id => CampaignCardIds.Index(id) < 0) || !r.offeredIds.Contains(r.selectedId) || !Enumerable.Range(1, Math.Min(finished, last - 1)).Any(n => r.key == CharacterRewardKey.Stage(c.runId, c.chapterId, n) + "/card/" + r.slotId)) || c.rewards.Select(r => r.key).Distinct().Count() != c.rewards.Length) return "확정 카드 추첨 기록이 유효하지 않습니다";
            foreach (var p in people)
            {
                var records = c.rewards.Where(r => r.slotId == p.slotId).ToArray();
                if (records.Length != Math.Min(finished, last - 1) || p.cards.Any(k => records.Count(r => r.selectedId == k.id) != k.stacks)) return "선택 카드와 보상 원장이 다릅니다";
            }
            if (!Metrics(c.clears, c.party) || !Metrics(c.failures, c.party) || c.clears.Length != finished || !c.clears.Select(r => r.stage).SequenceEqual(Enumerable.Range(1, finished))) return "런 전투 기록이 유효하지 않습니다";
            return null;
        }
        private static bool Metrics(CampaignMetricRecord[] records, CampaignPartySlot[] slots) => records != null && records.Length <= 100000 && records.All(r => r != null && r.stage >= 1 && r.stage <= 3 && !double.IsNaN(r.seconds) && !double.IsInfinity(r.seconds) && r.seconds >= 0 && r.members != null && r.members.Length <= 4 && r.members.All(m => m != null && slots.Any(p => p.slotId == m.slotId && p.role == m.role && p.character.jobId == m.jobId) && m.deaths >= 0 && new[] { m.bossDamage, m.addDamage, m.healing, m.selfHealing, m.shield }.All(v => !double.IsNaN(v) && !double.IsInfinity(v) && v >= 0)) && r.members.Select(m => m.slotId).Distinct().Count() == r.members.Length);
        private static bool Finite(float n) => !float.IsNaN(n) && !float.IsInfinity(n);
    }
}
