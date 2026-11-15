using System;
using RaidRogue;

internal static class CombatActionRulesTests
{
    private static int _checks;
    private static void Check(bool result, string message)
    {
        _checks++;
        if (!result) { Console.Error.WriteLine("FAIL: " + message); Environment.Exit(1); }
    }
    private static CombatActionReceipt Action(ulong id = 1, int chain = 2, int step = 3,
        CombatActionOrigin origin = CombatActionOrigin.SkillInput,
        CombatActionPurpose purpose = CombatActionPurpose.Heal, int actor = 7, int epoch = 1,
        bool combat = true, bool target = true, bool resolved = true)
        => new CombatActionReceipt(actor, epoch, id, origin, purpose, chain, step, combat, target, resolved);

    private static void Main()
    {
        var claims = new CombatActionClaims(7, 1);
        var heal = Action();
        Check(claims.TryClaimCompletionResource(heal), "direct final heal qualifies without an effective-heal amount");
        Check(!claims.TryClaimCompletionResource(heal), "same completion cannot grant twice");
        for (int i = 0; i < 8; i++)
            Check(!claims.TryClaimCompletionResource(heal), "eight targets do not multiply completion resources");
        Check(claims.TryClaimDirectGrowth("oracle.sap", heal), "growth and completion resource are independent");
        Check(!claims.TryClaimDirectGrowth("oracle.sap", heal), "multi-hit growth once");
        Check(claims.TryClaimDirectGrowth("another-explicit-effect", heal), "distinct effect has its own claim");
        foreach (CombatActionOrigin origin in Enum.GetValues(typeof(CombatActionOrigin)))
        {
            if (origin == CombatActionOrigin.SkillInput) continue;
            var automatic = Action(2, origin: origin);
            Check(!claims.TryClaimCompletionResource(automatic), "automatic origin never creates resource: " + origin);
            Check(!claims.TryClaimDirectGrowth("oracle.sap", automatic), "automatic origin never grows route: " + origin);
        }
        Check(claims.TryClaimCompletionResource(Action(2)), "rejected automatic event does not consume a valid action claim");
        for (int step = 1; step <= 3; step++)
        {
            var fresh = new CombatActionClaims(7, 1);
            var attack = Action(chain: 1, step: step, purpose: CombatActionPurpose.Attack);
            Check(fresh.TryClaimDirectGrowth("oracle.sap", attack), "each resolved combo step can grow once");
            Check(fresh.TryClaimCompletionResource(attack) == (step == 3), "only last attack step creates resource");
            var healing = new CombatActionClaims(7, 1);
            Check(healing.TryClaimCompletionResource(Action(step: step)) == (step == 3), "same completion eligibility for healing");
        }
        var blocked = new[] {
            Action(actor: 0), Action(actor: 8), Action(epoch: 0), Action(epoch: 2), Action(id: 0),
            Action(combat: false), Action(target: false), Action(resolved: false), Action(chain: 0),
            Action(step: 0), Action(step: 4), Action(chain: 1, purpose: CombatActionPurpose.Heal),
            Action(purpose: CombatActionPurpose.Defense)
        };
        foreach (var receipt in blocked)
        {
            var fresh = new CombatActionClaims(7, 1);
            Check(!fresh.TryClaimCompletionResource(receipt), "invalid/missed/out-of-combat completion rejected");
            Check(!fresh.TryClaimDirectGrowth("sap", receipt), "invalid/missed/out-of-combat growth rejected");
            Check(fresh.TrackedActions == 0, "rejection does not reserve state");
        }
        var identity = new CombatActionClaims(7, 1);
        Check(identity.TryClaimDirectGrowth("sap", Action(step: 1)), "first action step recorded");
        Check(!identity.TryClaimCompletionResource(Action()), "same action cannot be promoted to combo finisher");
        Check(!identity.TryClaimDirectGrowth("other", Action(chain: 1, purpose: CombatActionPurpose.Attack)), "action cannot change purpose");
        Check(!identity.TryClaimDirectGrowth(" ", Action(2)), "empty effect identity rejected");
        Check(!identity.BeginEncounter(1) && !identity.BeginEncounter(0), "same/stale epoch cannot reset claims");
        Check(!identity.TryClaimDirectGrowth("sap", Action(step: 1)), "duplicate stays consumed after rejected reset");
        Check(identity.BeginEncounter(2), "new encounter clears transient state");
        Check(!identity.TryClaimCompletionResource(Action(epoch: 1)), "old encounter callback rejected");
        Check(identity.TryClaimCompletionResource(Action(epoch: 2)), "new encounter may restart action IDs");

        var bounded = new CombatActionClaims(7, 1, 3);
        Check(bounded.TryClaimCompletionResource(Action(10)), "new action accepted");
        Check(bounded.TryClaimCompletionResource(Action(12)), "newer action accepted");
        Check(bounded.TryClaimCompletionResource(Action(11)), "out-of-order outcome in window accepted");
        Check(!bounded.TryClaimCompletionResource(Action(9)), "older outcome cannot evict newer tracked action");
        for (ulong id = 13; id < 1000; id++)
        {
            Check(bounded.TryClaimCompletionResource(Action(id)), "new action remains accepted across eviction");
            Check(bounded.TrackedActions == 3, "history stays bounded");
            Check(!bounded.TryClaimCompletionResource(Action(id - 3)), "evicted action cannot replay resource");
            Check(!bounded.TryClaimDirectGrowth("sap", Action(id - 3)), "evicted action cannot replay growth");
        }
        Check(new CombatActionClaims(8, 1).TryClaimCompletionResource(Action(actor: 8)), "second healer owns separate claims");
        Check(new CombatActionClaims(-7, 1).TryClaimCompletionResource(Action(actor: -7)), "negative Unity instance actor IDs are supported");
        var finishPolicy=new CombatComboResourcePolicy("light",0,0,20,0,0,20);
        var beatPolicy=new CombatComboResourcePolicy("beat",1,1,1,1,1,1);
        var routeClaims=new CombatActionClaims(7,1);
        Check(!routeClaims.TryClaimResource(finishPolicy,Action(2000,step:1),out _),"completion policy does not reward first input");
        Check(routeClaims.TryClaimResource(beatPolicy,Action(2000,step:1),out float beat)&&beat==1,"replacement per-input policy rewards first input");
        Check(!routeClaims.TryClaimResource(beatPolicy,Action(2000,step:1),out _),"multi-hit cannot duplicate per-input resource");
        Check(routeClaims.TryClaimResource(finishPolicy,Action(2001),out float gain)&&gain==20,"completion amount supplied by one policy");
        Check(!routeClaims.TryClaimResource(beatPolicy,Action(2001),out _)&&!routeClaims.TryClaimCompletionResource(Action(2001)),"route/base policies share a single resource claim");
        Check(!routeClaims.TryClaimResource(null,Action(2002),out _),"no implicit default reward");
        var execution=new CombatActionExecution(7);
        Check(!execution.TryResolveDirectGrowth("healer.route",true),"out of action cannot grow healer preparation");
        execution.Begin(CombatActionPurpose.Heal,2,1,true,true);
        Check(execution.TryResolveDirectGrowth("healer.route",true)&&!execution.TryResolveDirectGrowth("healer.route",true),"one direct healing input claims growth once even with many recipients");
        Check(!execution.TryResolveFinisherGauge(true),"first direct heal does not give finisher resources");execution.End();
        execution.Begin(CombatActionPurpose.Heal,2,3,true,true);
        Check(execution.TryResolveDirectGrowth("healer.route",true)&&execution.TryResolveFinisherGauge(true),"last heal independently claims growth and completion once");execution.End();
        execution.Begin(CombatActionPurpose.Heal,2,3,false,true);
        Check(!execution.TryResolveDirectGrowth("healer.route",true)&&!execution.TryResolveFinisherGauge(true),"lobby healing grants neither route growth nor completion");execution.End();
        execution.Begin(CombatActionPurpose.Attack,1,3,true,true);
        Check(!execution.TryResolveDirectGrowth("healer.route",false),"missed attack cannot grow healer preparation");execution.End();
        Console.WriteLine("PASS: combat action rules " + _checks + " assertions; pure eligibility/deduplication, no Unity execution.");
    }
}
