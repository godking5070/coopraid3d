using System;
using RaidRogue;

internal static class PotionTests
{
    private static int _checks;
    private static void Check(bool value, string label)
    {
        _checks++;
        if (!value) throw new Exception(label);
    }
    private static void Equal(double expected, double actual, string label) =>
        Check(!double.IsNaN(actual) && Math.Abs(actual - expected) < 0.0001, label + $": expected {expected}, got {actual}");

    private sealed class Actor
    {
        public readonly PotionState Potion = new PotionState();
        public readonly PotionSettings Settings;
        public float Hp, Max;
        public bool Alive = true, Blocked;
        public int Applications;
        public readonly CombatMetricsLedger Metrics = new CombatMetricsLedger();
        public Actor(float hp, float max, PotionSettings settings = null)
        {
            Hp = hp; Max = max;
            Settings = settings ?? new PotionSettings();
            Metrics.Register(1, "Owner", 3);
            Metrics.Register(2, "Healer", 2);
            Metrics.BeginStage(1);
        }
        public float Use() => Potion.TryUse(Settings, Hp, Max, Alive, Blocked, amount =>
        {
            Applications++;
            float before = Hp;
            Hp = Math.Min(Max, Hp + amount);
            Metrics.RecordHealing(1, before, Hp, HealingKind.Potion);
            return Hp - before;
        });
    }

    private static CombatMemberRecord Member(CombatEncounterResult data, int id)
    {
        foreach (var member in data.Members) if (member.Id == id) return member;
        throw new Exception("missing member");
    }

    private static void HealingAndRejections()
    {
        foreach (float maxHp in new[] { 1000f, 1100f, 1200f, 1600f, 2400f, 3000f })
        {
            var actor = new Actor(maxHp * 0.25f, maxHp);
            Equal(maxHp * 0.5f, actor.Use(), "every player/bot max HP gets 50% healing");
            Equal(maxHp * 0.75f, actor.Hp, "HP increment applied once");
            Equal(30, actor.Potion.CooldownRemaining, "default cooldown");
            Equal(0, actor.Use(), "immediate repeated activation rejected");
            Equal(1, actor.Applications, "no duplicate heal callback");
            Equal(maxHp * 0.5f, Member(actor.Metrics.Snapshot(), 1).SelfHealing, "self healing recorded");
            Equal(0, Member(actor.Metrics.Snapshot(), 1).Healing, "self heal excluded from ability healing");
            Equal(0, Member(actor.Metrics.Snapshot(), 2).Healing, "healer gets no potion credit");
        }
        var overflow = new Actor(900, 1000);
        Equal(100, overflow.Use(), "only effective 100 HP restored");
        Equal(1000, overflow.Hp, "overheal capped");
        Equal(100, Member(overflow.Metrics.Snapshot(), 1).SelfHealing, "overheal excluded from metrics");
        var small = new Actor(999.75f, 1000);
        Equal(0.25, small.Use(), "fractional missing HP is still a valid use");

        var full = new Actor(1000, 1000);
        full.Potion.GrantRevivalProtection(3);
        Equal(0, full.Use(), "full HP rejected");
        Equal(0, full.Potion.CooldownRemaining, "full HP spends no cooldown");
        Equal(3, full.Potion.RevivalProtectionRemaining, "failed full HP use preserves protection");
        var dead = new Actor(0, 1000) { Alive = false };
        Equal(0, dead.Use(), "dead actor rejected");
        Equal(0, dead.Potion.CooldownRemaining, "death rejection spends no cooldown");
        dead.Hp = 200;
        Equal(0, dead.Use(), "dead flag rejects positive HP");
        dead.Alive = true; dead.Hp = 0;
        Equal(0, dead.Use(), "zero HP rejects even if flag is alive");
        var blocked = new Actor(200, 1000) { Blocked = true };
        Equal(0, blocked.Use(), "item/menu blocked");
        Equal(0, blocked.Applications, "blocked item never reaches heal function");
        Equal(0, blocked.Potion.CooldownRemaining, "blocked use spends no cooldown");
        blocked.Blocked = false;
        Equal(500, blocked.Use(), "new allowed use succeeds");
        var noApplication = new PotionState();
        noApplication.GrantRevivalProtection(3);
        Equal(0, noApplication.TryUse(new PotionSettings(), 200, 1000, true, false, amount => 0), "actual heal failed");
        Equal(0, noApplication.CooldownRemaining, "failed application spends no cooldown");
        Equal(3, noApplication.RevivalProtectionRemaining, "failed application keeps protection");
    }

    private static void CooldownLifecycle()
    {
        var shared = new PotionSettings { cooldown = 9 };
        var actor = new Actor(100, 1000, shared);
        var bot = new Actor(100, 1000, shared);
        actor.Use(); bot.Use();
        Equal(9, actor.Potion.CooldownRemaining, "shared configured cooldown for player");
        Equal(9, bot.Potion.CooldownRemaining, "shared configured cooldown for bot");
        actor.Alive = false; actor.Hp = 0;
        actor.Potion.EndRevivalProtection();
        actor.Potion.Tick(2);
        Equal(7, actor.Potion.CooldownRemaining, "cooldown continues through death without reset");
        actor.Alive = true; actor.Hp = 300;
        actor.Potion.GrantRevivalProtection(3);
        Equal(7, actor.Potion.CooldownRemaining, "revival grants protection without resetting cooldown");
        Equal(0, actor.Use(), "revived actor still on cooldown");
        Equal(3, actor.Potion.RevivalProtectionRemaining, "cooldown rejection keeps protection");
        actor.Potion.Tick(0);
        Equal(7, actor.Potion.CooldownRemaining, "pause freezes cooldown");
        actor.Potion.Tick(7);
        actor.Potion.GrantRevivalProtection(3);
        Equal(500, actor.Use(), "injured protected actor drinks ready potion");
        Equal(0, actor.Potion.RevivalProtectionRemaining, "successful potion ends revival protection");
        Equal(9, actor.Potion.CooldownRemaining, "success commits configured cooldown");
        actor.Potion.ResetForBossRetry(); bot.Potion.ResetForBossRetry();
        Equal(0, actor.Potion.CooldownRemaining, "boss retry resets player cooldown");
        Equal(0, bot.Potion.CooldownRemaining, "boss retry resets bot cooldown");
        // No stock count: repeat many uses after cooldown, including fractional frame increments.
        for (int i = 0; i < 200; i++)
        {
            actor.Hp = 100;
            Equal(500, actor.Use(), "unlimited use after cooldown");
            actor.Potion.Tick(8.99);
            Equal(0, actor.Use(), "cannot consume just before cooldown expires");
            actor.Potion.Tick(0.011);
        }
    }

    private static void BotThresholdAndMetrics()
    {
        foreach (float max in new[] { 1100f, 1200f, 2400f })
        {
            Check(PotionState.NeedsAutomaticPotion(max * 0.4f, max, true), "40% threshold is inclusive");
            Check(!PotionState.NeedsAutomaticPotion(max * 0.4f + 1, max, true), "above 40% does not use");
            Check(PotionState.NeedsAutomaticPotion(1, max, true), "living critical bot uses");
            Check(!PotionState.NeedsAutomaticPotion(0, max, false), "dead bot cannot use");
            Check(!PotionState.NeedsAutomaticPotion(max * 0.2f, max, false), "dead flag overrides HP ratio");
        }
        var actor = new Actor(100, 1000);
        actor.Metrics.Advance(10, true);
        actor.Use();
        actor.Metrics.RecordHealing(2, 600, 650);
        actor.Metrics.RecordHealing(2, 900, 1000, HealingKind.Potion);
        actor.Metrics.EndStage();
        var frozen = actor.Metrics.Stages[0];
        Equal(500, Member(frozen, 1).SelfHealing, "stage result saves potion healing");
        Equal(0, Member(frozen, 1).Hps(10), "potion HPS remains zero");
        Equal(5, Member(frozen, 2).Hps(10), "healer HPS counts only ability heal");
        Equal(100, Member(frozen, 2).SelfHealing, "healer's own potion is also separate self healing");
        actor.Potion.Tick(30); actor.Hp = 900; actor.Use();
        Equal(500, Member(actor.Metrics.Snapshot(), 1).SelfHealing, "postcombat healing is not recorded");
        actor.Metrics.BeginStage(2);
        Equal(0, Member(actor.Metrics.Snapshot(), 1).SelfHealing, "new stage starts fresh metrics");
        actor.Metrics.Advance(20, true);
        actor.Potion.Tick(30); actor.Hp = 900; actor.Use();
        actor.Metrics.RecordHealing(2, 500, 550);
        actor.Metrics.EndStage();
        var total = actor.Metrics.RunTotal();
        Equal(600, Member(total, 1).SelfHealing, "whole run sums potion healing");
        Equal(0, Member(total, 1).Hps(total.Seconds), "whole run HPS excludes all potions");
        Equal(100.0 / 30, Member(total, 2).Hps(total.Seconds), "weighted run HPS unchanged");
        Equal(100, Member(total, 2).SelfHealing, "healer potion retained in run self healing only");
        Equal(500, Member(frozen, 1).SelfHealing, "previous snapshot remains immutable");
        actor.Metrics.ResetRun();
        Equal(0, actor.Metrics.RunTotal().Members.Count, "restart clears self healing history");
    }

    public static void Main()
    {
        HealingAndRejections(); CooldownLifecycle(); BotThresholdAndMetrics();
        Console.WriteLine($"PASS: {_checks} potion healing, cooldown, revival, AI and metrics checks.");
    }
}
