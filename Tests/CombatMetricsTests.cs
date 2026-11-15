using System;
using RaidRogue;

internal static class CombatMetricsTests
{
    private static int _checks;
    private static void Equal(double expected, double actual, string label)
    {
        _checks++;
        if (double.IsNaN(actual) || Math.Abs(expected - actual) > 0.00001)
            throw new Exception(label + ": expected " + expected + ", got " + actual);
    }

    private static CombatMemberRecord Member(CombatEncounterResult result, int id)
    {
        foreach (var member in result.Members) if (member.Id == id) return member;
        throw new Exception("Missing party member " + id);
    }

    private static CombatMetricsLedger Party()
    {
        var ledger = new CombatMetricsLedger();
        ledger.Register(1, "Player", 3);
        ledger.Register(2, "Tank bot", 1);
        ledger.Register(3, "Healer bot", 2);
        ledger.Register(4, "Caster bot", 4);
        return ledger;
    }
    private static void FactualEventsFollowAttempt()
    {
        var ledger = Party();
        ledger.BeginStage(1);
        ledger.RecordEvent("탱커 카운터 미성공");
        var failed = ledger.Snapshot();
        _checks++; if (failed.Events.Count != 1 || !failed.Events[0].Contains("미성공")) throw new Exception("factual event captured");
        ledger.FailAttempt(); ledger.BeginStage(1);
        _checks++; if (ledger.Snapshot().Events.Count != 0 || failed.Events.Count != 1) throw new Exception("retry clears only current events");
    }

    private static void AppliedAmountsAndOwnership()
    {
        var ledger = Party();
        ledger.Advance(30, true);
        ledger.RecordDamage(1, 100, 0, false);
        Equal(0, ledger.Seconds, "precombat duration");
        Equal(0, Member(ledger.Snapshot(), 1).Damage, "precombat damage");
        ledger.BeginStage(1);
        ledger.Advance(2, true);
        ledger.RecordDamage(1, 20, 0, false); // a 1000-damage hit against 20 remaining HP
        ledger.RecordDamage(1, 0, 0, false);  // another callback against the dead target
        ledger.RecordDamage(2, 100, 70, true);
        ledger.RecordDamage(4, 200, 155, false); // delayed projectile retains its caster
        ledger.RecordDamage(1, 150, 135, false); // player-owned reflection/card proc
        ledger.RecordDamage(2, 70, 70, true); // miss/canceled cast: no HP delta
        ledger.RecordDamage(0, 999, 0, false); // debug kill is not a party attack
        ledger.RecordDamage(1, 100, 120, false);
        ledger.RecordHealing(3, 95, 100); // 50 requested, only 5 effective
        ledger.RecordHealing(3, 100, 100);
        ledger.RecordHealing(1, 50, 60); // self-heal/card
        ledger.RecordHealing(1, 60, 65); // same owner's periodic heal on another recipient
        var snapshot = ledger.Snapshot();
        Equal(35, Member(snapshot, 1).BossDamage, "player direct + reflection");
        Equal(30, Member(snapshot, 2).AddDamage, "bot add damage");
        Equal(45, Member(snapshot, 4).BossDamage, "projectile owner");
        Equal(5, Member(snapshot, 3).Healing, "effective healing excludes overheal");
        Equal(15, Member(snapshot, 1).Healing, "self + periodic healing");
        Equal(17.5, Member(snapshot, 1).Dps(snapshot.Seconds), "DPS denominator");
        Equal(2.5, Member(snapshot, 3).Hps(snapshot.Seconds), "HPS denominator");
        Equal(0, Member(snapshot, 1).Dps(0), "zero duration is finite");
        ledger.RecordDamage(1, 10, -100, false);
        Equal(45, Member(ledger.Snapshot(), 1).BossDamage, "overkill defensive clamp");
        ledger.RecordDamage(1, double.NaN, 0, false);
        ledger.RecordDamage(1, 10, double.NegativeInfinity, false);
        Equal(45, Member(ledger.Snapshot(), 1).BossDamage, "invalid damage ignored");
    }

    private static void Shields()
    {
        var ledger = Party();
        ledger.BeginStage(1);
        var shields = new CombatShieldLedger();
        shields.Grant(3, 60);
        shields.Grant(1, 40); // second provider only received 40 remaining capacity
        shields.Grant(1, 0);  // shield at cap has no new ownership
        shields.Absorb(75, ledger.RecordAbsorption);
        Equal(60, Member(ledger.Snapshot(), 3).ShieldAbsorbed, "first shield provider");
        Equal(15, Member(ledger.Snapshot(), 1).ShieldAbsorbed, "second shield provider");
        Equal(0, Member(ledger.Snapshot(), 3).Healing, "absorption is not healing");
        Equal(25, shields.Remaining, "remaining shield");
        shields.Absorb(100, ledger.RecordAbsorption);
        shields.Absorb(100, ledger.RecordAbsorption);
        Equal(40, Member(ledger.Snapshot(), 1).ShieldAbsorbed, "shield cannot be counted twice");
        shields.Grant(3, 100);
        shields.Reconcile(0); // death, role change or stage reset
        shields.Absorb(100, ledger.RecordAbsorption);
        Equal(60, Member(ledger.Snapshot(), 3).ShieldAbsorbed, "removed shield earns no credit");
        shields.Reconcile(50); // preexisting unattributed shield
        shields.Grant(3, 10);
        shields.Absorb(55, ledger.RecordAbsorption);
        Equal(65, Member(ledger.Snapshot(), 3).ShieldAbsorbed, "unknown shield is not credited to healer");
        shields.Reconcile(2); // partial external removal also earns no absorption
        shields.Absorb(2, ledger.RecordAbsorption);
        Equal(67, Member(ledger.Snapshot(), 3).ShieldAbsorbed, "partial reconciliation");
    }

    private static void DurationSnapshotsAndReset()
    {
        var ledger = Party();
        ledger.BeginStage(1);
        int generation = ledger.Generation;
        ledger.Advance(10, true);
        ledger.Advance(900, false); // pause/role selection
        ledger.RecordDamage(1, 100, 0, false);
        ledger.RecordHealing(3, 0, 40);
        ledger.SetDead(2, true);
        ledger.SetDead(2, true); // same down event traverses multiple callbacks
        ledger.SetDead(2, false);
        ledger.SetDead(2, true);
        ledger.EndStage();
        ledger.EndStage();
        Equal(1, ledger.Stages.Count, "end is idempotent");
        Equal(2, Member(ledger.Stages[0], 2).Deaths, "death requires revival before another count");
        ledger.Advance(1000, true); // death animation and reward time
        ledger.RecordDamage(1, 50, 0, true);
        ledger.RecordHealing(3, 0, 500); // stage reset heal must not be credited
        ledger.RecordAbsorption(3, 500);
        Equal(10, ledger.Seconds, "finished duration frozen");
        Equal(100, Member(ledger.Snapshot(), 1).Damage, "finished damage frozen");
        Equal(40, Member(ledger.Snapshot(), 3).Healing, "finished healing frozen");
        Equal(0, Member(ledger.Snapshot(), 3).ShieldAbsorbed, "finished absorption frozen");
        ledger.BeginStage(2);
        Equal(generation + 1, ledger.Generation, "delayed effects get a new encounter token");
        ledger.Register(1, "Player changed role", 2);
        ledger.Advance(30, true);
        ledger.RecordDamage(1, 60, 0, true);
        ledger.RecordHealing(3, 0, 20);
        ledger.EndStage();
        Equal(100, Member(ledger.Stages[0], 1).Damage, "prior snapshot remains independent");
        Equal(3, Member(ledger.Stages[0], 1).Role, "prior role preserved");
        Equal(2, Member(ledger.Stages[1], 1).Role, "new role retained");
        Equal(0, Member(ledger.Stages[1], 2).Deaths, "new encounter counters reset");
        var total = ledger.RunTotal();
        Equal(40, total.Seconds, "sum of combat times");
        Equal(160, Member(total, 1).Damage, "run damage");
        Equal(4, Member(total, 1).Dps(total.Seconds), "weighted run DPS, not mean of 10 and 2");
        Equal(1.5, Member(total, 3).Hps(total.Seconds), "weighted run HPS");
        Equal(2, Member(total, 2).Deaths, "run deaths");
        ledger.ResetRun();
        Equal(0, ledger.Stages.Count, "restart clears history");
        Equal(0, ledger.RunTotal().Members.Count, "restart clears totals");
        Equal(0, ledger.Seconds, "restart clears time");
        Equal(160, Member(total, 1).Damage, "returned totals are independent");
    }

    private static void ShieldConservation()
    {
        var random = new Random(1022);
        var shields = new CombatShieldLedger();
        double granted = 0, consumed = 0;
        for (int i = 0; i < 1000; i++)
        {
            if (random.Next(2) == 0)
            {
                double amount = random.Next(1, 200);
                shields.Grant(random.Next(1, 5), amount);
                granted += amount;
            }
            else shields.Absorb(random.Next(1, 300), (id, amount) => consumed += amount);
            Equal(granted, consumed + shields.Remaining, "shield conservation at operation " + i);
        }
    }

    public static int Main()
    {
        var layered=new CombatShieldLedger();
        layered.GrantTimed(1,"other",25,1000,10);
        layered.GrantTimed(2,"layers",100,1000,12);
        layered.GrantTimed(3,"layers",100,1000,12);
        int callbacks=0;double actualLayerDamage=0;
        layered.Absorb(30,null,(source,key,amount)=>
        {
            callbacks++;
            if(source!=2)return;
            actualLayerDamage+=amount;
            Equal(95,layered.RemoveEffect(2,"layers"),"discard only current caster layer residue after hit");
            layered.GrantTimed(2,"layers",100,1000,12);
        });
        Equal(2,callbacks,"key callback only for effects actually absorbing");
        Equal(5,actualLayerDamage,"newly activated layer never absorbs same hit");
        Equal(100,layered.RemainingFor(3,"layers"),"same name other caster untouched");
        Equal(100,layered.RemainingFor(2,"layers"),"next layer remains for next hit");
        Equal(200,layered.Remaining,"ledger remains coherent after callback mutation");
        AppliedAmountsAndOwnership();
        Shields();
        DurationSnapshotsAndReset();
        FactualEventsFollowAttempt();
        ShieldConservation();
        Console.WriteLine("PASS: " + _checks + " combat metrics assertions.");
        return 0;
    }
}
