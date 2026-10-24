using System;
using System.Linq;
using RaidRogue;

class CharacterRosterTests
{
    static int count;
    static void Check(bool condition, string label)
    {
        count++;
        if (!condition)
            throw new Exception(label);
    }

    static void Near(float a, float b, string label) => Check(Math.Abs(a - b) < .001f, label + " " + a + " != " + b);
    static CharacterUseContext Context(bool valid = true, float range = 0, float cast = -1, float cost = 1) => new CharacterUseContext(true, true, valid, range, false, -1, 1, cast, cost);
    static void Main()
    {
        foreach (var job in CharacterRosterData.Jobs)
        {
            var d = CharacterRosterData.Definition(job);
            Check(CharacterDefinitionValidation.Check(new[] { d }).Length == 0, job.slug + " data");
            Check(d.slots.Length == 12 && d.routes.Length == 3 && d.unlocks.Select(r => r.choices.Length).SequenceEqual(new[] { 3, 3, 2 }), "complete candidate sets");
            var first = new CharacterRuntimeState(d);
            var second = new CharacterRuntimeState(d);
            string error;
            Check(!first.TryUse(CharacterSlot.Identity, Context(), 0, s => true, out error), "Z locked before origin");
            Check(!first.TryUse(CharacterSlot.Active, Context(), 0, s => true, out error), "R locked before clear");
            foreach (var route in d.routes)
            {
                Check(first.ChooseRoute(route.id, true), "origin selected");
                Check(!first.ChooseRoute(d.routes[(Array.IndexOf(d.routes, route) + 1) % 3].id, false), "no combat origin change");
                Check(first.Selected(CharacterSlot.Identity).id == route.identitySkillId, "origin identity");
            }

            first.Gain(job.Resource, job.capacity);
            Near(second.Resource(job.Resource), 0, "independent resource");
            var before = first.Snapshot;
            int effects = 0;
            Check(!first.TryUse(CharacterSlot.Gcd1, Context(false), 0, s =>
            {
                effects++;
                return true;
            }, out error), "invalid target");
            Check(!first.TryUse(CharacterSlot.Gcd1, Context(true, 10000), 0, s =>
            {
                effects++;
                return true;
            }, out error), "out of range");
            Check(effects == 0 && first.Snapshot.combo1 == 1 && first.Snapshot.gcd == 0, "invalid request atomic");
            Check(!first.TryUse(CharacterSlot.Gcd1, Context(), 0, s => false, out error), "effect failure");
            Check(first.TryUse(CharacterSlot.Gcd1, Context(), 0, s => true, out error), "first combo");
            Check(first.Snapshot.combo1 == 2 && first.Snapshot.combo2 == 1, "independent chains");
            Check(!first.TryUse(CharacterSlot.Gcd2, Context(), 0, s => true, out error), "shared GCD");
            first.Tick(3);
            Check(first.TryUse(CharacterSlot.Gcd2, Context(), 0, s => true, out error), "second chain");
            Check(first.Snapshot.combo1 == 2 && first.Snapshot.combo2 == 2, "two chain state");
            Check(second.Restore(first.Snapshot), "reconnect state");
            Near(second.Snapshot.gcd, first.Snapshot.gcd, "reconnect GCD");
            first.BeginRun("run-a", CharacterRewardKey.CurrentChapter);
            for (int stage = 1; stage <= 3; stage++)
            {
                var rule = d.unlocks[stage - 1];
                Check(first.ClearedStage("run-a", CharacterRewardKey.CurrentChapter, stage), "clear");
                Check(!first.ClearedStage("run-a", CharacterRewardKey.CurrentChapter, stage), "duplicate clear");
                Check(first.PendingChoices().Length == 1, "pending exact stage");
                Check(!first.ChooseSkill(rule.id, d.Skill(job.SkillId("a")).id), "foreign choice rejected");
                Check(first.ChooseSkill(rule.id, rule.choices[stage % rule.choices.Length]), "confirm");
                Check(!first.ChooseSkill(rule.id, rule.choices[0]), "locked choice");
            }

            string selected = first.Selected(CharacterSlot.Active).id;
            first.Reset(CharacterReset.Retry);
            Check(first.Selected(CharacterSlot.Active).id == selected && first.PendingChoices().Length == 0, "retry selection retained");
            first.Reset(CharacterReset.NewRun);
            Check(!first.Unlocked(CharacterSlot.Active) && first.RouteId == "", "new run reset");
            if (job.Role == 2)
            {
                Check(first.TryUse(CharacterSlot.Job1, Context(cast: 0, cost: 0), 1, s => true, out error), "instant awakening");
                Near(first.Resource(job.Id + ".mana"), 100, "free awakening");
                first.Tick(40);
                Check(first.TryUse(CharacterSlot.Job1, Context(cast: 2), 1, s => true, out error), "normal heal starts");
                first.Tick(1, true);
                Check(first.Snapshot.castSkillId == "", "moving cancels");
                Near(first.Resource(job.Id + ".mana"), 100, "cancel does not spend mana");
                Check(first.TryUse(CharacterSlot.Job1, Context(cast: 2), 1, s => true, out error), "normal heal restarts");
                first.Tick(2);
                Check(first.FinishCast(Context(), s => true, out error), "normal heal completes");
                Near(first.Resource(job.Id + ".mana"), 75, "normal mana once");
            }
        }

        var dragon = CharacterRosterData.Find("job.dragon-breaker");
        var ds = new CharacterRuntimeState(CharacterRosterData.Definition(dragon));
        ds.ChooseRoute(ds.Definition.routes[0].id, true);
        ds.Gain(dragon.Resource, 100);
        Near(ds.Resource(dragon.Resource), 6, "dragon route capacity");
        Check(new CharacterRuntimeState(ds.Definition).Restore(ds.Snapshot), "capacity reconnect");
        for (int n = 1; n <= 80; n++)
        {
            var effect = new CharacterTimedEffect
            {
                id = "hot",
                kind = "hot",
                interval = .125f,
                next = .125f,
                end = 8
            };
            int total = 0;
            for (int i = 1; i <= n; i++)
                total += CharacterEffectState.TakeTicks(effect, 8f * i / n);
            Check(total == 64, "variable frame tick conservation");
            Check(CharacterEffectState.TakeTicks(effect, 100) == 0, "no post end ticks");
            var state = new CharacterEffectState();
            state.Put(effect);
            var saved = state.Data.Copy();
            var restored = new CharacterEffectState();
            Check(restored.Restore(saved), "effect restore");
            Check(CharacterEffectState.TakeTicks(restored.Data.effects[0], 100) == 0, "no reconnect replay");
        }

        Near(CharacterEffectState.ReducedDamage(100, new[] { .6f, .2f }), 32, "multiplicative DR");
        Near(CharacterEffectState.Lifesteal(100, .3f, 10), 10, "effective healing cap");
        Check(!new CharacterEffectState().Restore(new CharacterKitSnapshot { effects = new[] { new CharacterTimedEffect { id = "bad", interval = float.NaN } } }), "corrupt timers");
        // Stateful one-shot effects: all callbacks are host side and resources remain owned by one actor.
        var iris = new IrisRescueState();
        iris.BeginAttempt();
        int revives = 0;
        Check(!iris.TryRescue(iris.AttemptId, "invalid", () => false, () =>
        {
            revives++;
            return true;
        }), "invalid NPC rescue never invokes revival");
        Check(!iris.TryRescue(iris.AttemptId, "failed", () => true, () => false) && iris.Coins == 4, "failed revival does not spend a coin");
        for (int i = 0; i < 4; i++)
            Check(iris.TryRescue(iris.AttemptId, "death-" + i, () => true, () =>
            {
                revives++;
                return true;
            }), "valid coin rescue");
        Check(!iris.TryRescue(iris.AttemptId, "empty", () => true, () =>
        {
            revives++;
            return true;
        }) && revives == 4, "coins bound successful revivals");
        iris.BeginAttempt();
        Check(iris.Coins == 4 && iris.TotalUsed == 4, "retry coin allowance, cumulative usage retained");
        var shield = new CombatShieldLedger();
        shield.GrantTimed(1, "bard", 100, 1000, 10);
        shield.GrantTimed(2, "oracle", 200, 1000, 20);
        shield.Absorb(30, null);
        Near((float)shield.RemainingFrom(1), 70, "only bard owned shield counts");
        Near((float)shield.RemainingFrom(2), 200, "other shield excluded");
        double expiry = 0;
        shield.Expire(10, (id, value) =>
        {
            Check(id == 1, "expiry owner");
            expiry += value;
        });
        Near((float)expiry, 70, "remaining expiry heal, not original grant");
        shield.Expire(10, (id, value) => expiry += value);
        Near((float)expiry, 70, "expiry callback never repeats");
        shield.GrantTimed(2, "oracle", 50, 1000, 25);
        Near((float)shield.RemainingFrom(2), 50, "refresh does not stack");
        shield.RemoveSource(2);
        Check(shield.Expire(100, (id, value) => expiry += value) == 0, "cleanup is not shield expiry");
        var stateJump = new CharacterRuntimeState(CharacterRosterData.Definition(CharacterRosterData.Find("job.tempest-lancer")));
        var jumpContext = new CharacterUseContext(true, true, true, 0, castTime: .8f, cooldownScale: .7f, allowMovingCast: true);
        Check(stateJump.TryUse(CharacterSlot.OffGcd1, jumpContext, 1, x => true, out _), "jump cast");
        stateJump.Tick(.8f, true);
        Check(stateJump.FinishCast(jumpContext, x => true, out _), "jump allows air movement");
        Near(stateJump.Snapshot.skills.First(x => x.id.EndsWith(".a")).recharge, 6.3f, "jump-only route cooldown");
        stateJump.Tick(6.3f);
        Check(stateJump.Snapshot.skills.First(x => x.id.EndsWith(".a")).charges == 1, "route recovery exact");
        Near(CharacterRosterRules.ArmorFactor(0, 0), 1, "armor compatibility baseline");
        Near(CharacterRosterRules.ArmorFactor(0, 1), 4f / 3, "full penetration");
        for (int i = 0; i <= 100; i++)
        {
            float a = i / 100f;
            Check(CharacterRosterRules.ArmorFactor(a, a) >= 1 && CharacterRosterRules.ArmorFactor(a, a) <= 4f / 3 + .0001, "armor curve bounded");
            Near(CharacterEffectState.ReducedDamage(100, new[] { a, .2f }), 80 * (1 - a), "stacked defense preserves nonnegative damage");
        }

        Near(CharacterRosterRules.RangerNearMultiplier(4, false, false), .75f, "near boundary inclusive");
        Near(CharacterRosterRules.RangerNearMultiplier(4.01f, false, false), 1, "outside near penalty");
        Near(CharacterRosterRules.RangerNearMultiplier(0, true, false), 1, "overcome near penalty");
        Near(CharacterRosterRules.RangerNearMultiplier(6, false, true), .75f, "maximized near boundary");
        Near(CharacterRosterRules.RangerReceivedMultiplier(false, true), .8f, "receiving penalty");
        Near(CharacterRosterRules.HeavyArmorSpeed(true, true, false), 1.15f, "paladin shield card speed");
        Near(CharacterRosterRules.HeavyArmorSpeed(false, true, false), .9f, "shield ending restores weight");
        Near(CharacterRosterRules.HeavyArmorSpeed(true, false, true), .85f, "stationary path penalty");
        Check(CharacterRosterRules.WallCrosses(1, -1, 3, 3) && !CharacterRosterRules.WallCrosses(-1, 1, 0, 3) && !CharacterRosterRules.WallCrosses(1, -1, 3.01f, 3), "directional wall boundary");
        foreach (float invalid in new[]
        {
            float.NaN,
            float.PositiveInfinity,
            float.NegativeInfinity
        }

        )
        {
            Check(!new CharacterEffectState().Restore(new CharacterKitSnapshot { returnX = invalid }), "invalid return position");
            Check(!new CharacterEffectState().Restore(new CharacterKitSnapshot { effects = new[] { new CharacterTimedEffect { id = "bad", x = invalid } } }), "invalid ground coordinate");
        }

        Check(!ds.Spend(dragon.Resource, .5f), "integer ammo cannot spend fractional");
        ds.ChooseRoute(ds.Definition.routes[1].id, true);
        Near(ds.Resource(dragon.Resource), 4, "lower route capacity clamps");
        var gate = new RaidActionGate();
        var cancel = new RaidActionRequest
        {
            actor = 2,
            epoch = 1,
            sequence = 1,
            skill = 13
        };
        Check(gate.Accept(2, 1, cancel) && !gate.Accept(2, 1, cancel), "channel cancellation uses existing duplicate gate");
        Check(CharacterRosterRules.IsCannon("g2") && !CharacterRosterRules.IsCannon("g1") && !CharacterRosterRules.IsCannon("r3"), "cannon bonus excludes spear skills");
        var spells = new[]
        {
            "g1",
            "g2",
            "a"
        }.Select(CharacterRosterData.Ninjutsu).ToArray();
        Check(spells.Select(s => s.name).Distinct().Count() == 3 && spells.Select(s => s.power).Distinct().Count() == 3, "three distinct ninjutsu follow-ups");
        Check(spells[0].radius == 5 && spells[1].radius == 0 && spells[2].shape == "line", "ninjutsu shapes");
        var badCap = CharacterRosterData.Definition(dragon);
        badCap.routes[0].capacityOverrides = new[]
        {
            new ResourceCost
            {
                resourceId = dragon.Resource,
                amount = 4.5f
            }
        };
        Check(CharacterDefinitionValidation.Check(new[] { badCap }).Length > 0, "fractional stack cap rejected");
        badCap.routes[0].capacityOverrides = new[]
        {
            new ResourceCost
            {
                resourceId = dragon.Resource,
                amount = 6
            },
            new ResourceCost
            {
                resourceId = dragon.Resource,
                amount = 5
            }
        };
        Check(CharacterDefinitionValidation.Check(new[] { badCap }).Length > 0, "duplicate cap rejected");
        Console.WriteLine("PASS: " + count + " character roster assertions");
    }
}
