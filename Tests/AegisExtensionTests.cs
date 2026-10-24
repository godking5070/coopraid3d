using System;
using System.Linq;
using RaidRogue;

// Executes production pure state/math, not Unity behaviours or a simulated Steam connection.
public static class AegisExtensionTests
{
    static int checks;
    static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
    static void Near(double a,double b,string message,double tolerance=.0001)=>Check(Math.Abs(a-b)<tolerance,message+" actual="+a+" expected="+b);
    static CharacterRuntimeState Actor()=>new CharacterRuntimeState(AegisPaladinData.Build());
    static readonly CharacterSlot[] Slots={CharacterSlot.Active,CharacterSlot.OffGcd2,CharacterSlot.Ultimate};
    static readonly CharacterUseContext Ready=new CharacterUseContext(true,true,true,0);
    static bool Use(CharacterRuntimeState actor,CharacterSlot slot,Func<CharacterSkillDefinition,bool> apply=null)=>actor.TryUse(slot,Ready,42,apply??(_=>true),out _);
    static JobSkillState Timing(CharacterRuntimeState actor,CharacterSlot slot)=>actor.Snapshot.skills.Single(s=>s.id==actor.Selected(slot).id);
    public static void Main()
    {
        SelectionTransactions(); Timelines(); DamageAndGeometry(); RewardsAndRecovery();
        Console.WriteLine("PASS: "+checks+" Aegis extension choice, encounter identity, duplicate/race, effect timeline, geometry, damage conservation and reward-flow checks (no Unity/Steam execution).");
    }
    static void SelectionTransactions()
    {
        var a=Actor();a.BeginRun("run-a","main");var b=Actor();b.BeginRun("run-b","main");
        Check(CharacterDefinitionValidation.Check(new[]{a.Definition}).Length==0,"definitions validate");
        Check(a.Definition.unlocks.Select(r=>r.choices.Length).SequenceEqual(new[]{3,3,2}),"all deterministic candidates present");
        Check(a.Definition.unlocks.SelectMany(r=>r.choices).Distinct().Count()==8,"eight stable candidate IDs");
        foreach(var slot in Slots)Check(!Use(a,slot)&&!a.Unlocked(slot),"unselected slot cannot execute");
        Check(!a.ClearedStage("run-b","main",1)&&!a.ClearedStage("run-a","other",1),"other run/chapter cannot grant");
        Check(a.PendingChoices().Length==0,"no pending before clear");
        for(int stage=1;stage<=3;stage++)
        {
            var rule=a.Definition.unlocks[stage-1];
            Check(!a.ChooseSkill(rule.id,rule.choices[0]),"cannot choose before exact clear");
            Check(a.ClearedStage("run-a","main",stage)&&!a.ClearedStage("run-a","main",stage),"clear consumed once");
            Check(a.PendingChoices().Length==1&&a.PendingChoices()[0].choices.SequenceEqual(rule.choices),"full choices waiting");
            Check(!a.ChooseSkill(rule.id,"invented")&&!a.ChooseSkill(rule.id,rule.choices[0],CharacterRewardKey.Choice("run-b","main",stage,rule.id)),"unoffered and stale run rejected");
            Check(a.ChooseSkill(rule.id,rule.choices[1],a.RewardKey(rule)),"one candidate confirmed");
            Check(!a.ChooseSkill(rule.id,rule.choices[0],a.RewardKey(rule),true),"auto cannot overwrite human choice");
            Check(!a.ChooseSkill(rule.id,rule.choices[1],a.RewardKey(rule)),"replay is side-effect free rejection");
            Check(a.Selected(rule.slot).id==rule.choices[1]&&a.Snapshot.unlocked.Intersect(rule.choices).Count()==1,"only selected candidate equipped/unlocked");
            Check(a.PendingChoices().Length==0,"choice barrier cleared");
            Check(!b.Unlocked(rule.slot),"another same-job actor remains locked");
        }
        var clone=Actor();Check(clone.Restore(a.Snapshot),"reconnect restores confirmed choices");
        Check(clone.Snapshot.chosenRewardKeys.SequenceEqual(a.Snapshot.chosenRewardKeys),"run/chapter/stage grant keys restored");
        Check(Use(clone,CharacterSlot.Ultimate),"chosen ultimate executes without Holy");
        Near(clone.Resource(AegisPaladinData.Holy),0,"ultimate has no invented Holy cost");Near(Timing(clone,CharacterSlot.Ultimate).recharge,120,"provisional ultimate CD");
        Check(Timing(a,CharacterSlot.Ultimate).charges==1,"cooldowns independent");
        var snapshot=clone.Snapshot;var remote=Actor();Check(remote.Restore(snapshot),"reconnect restores spent cooldown");
        Check(!Use(remote,CharacterSlot.Ultimate),"no ultimate replay after reconnect");
        snapshot.chosenRewardKeys[0]="other/main/1/wrong";Check(!remote.Restore(snapshot),"bad reward identity rejected atomically");Near(Timing(remote,CharacterSlot.Ultimate).recharge,120,"invalid restore retains live CD");
        clone.Reset(CharacterReset.Retry);Check(clone.Snapshot.chosenRules.Length==3&&clone.PendingChoices().Length==0,"retry retains choices without reward replay");
        Check(Timing(clone,CharacterSlot.Ultimate).charges==1,"retry common cooldown reset");
        clone.Reset(CharacterReset.NewRun);clone.BeginRun("new","main");Check(clone.Snapshot.chosenRules.Length==0&&clone.Snapshot.clearedEncounters.Length==0,"new run clears rewards");
        foreach(var slot in Slots)Check(!clone.Unlocked(slot),"new run locks extension");
        var skip=Actor();skip.ClearedStage(3);Check(skip.PendingChoices().Length==1&&skip.PendingChoices()[0].slot==CharacterSlot.Ultimate,"same high-water stage is not proof of previous stage clear");
        var chapter=Actor();chapter.BeginRun("run","second");chapter.ClearedStage(1);Check(chapter.PendingChoices().Length==0,"another chapter's Stage1 does not unlock main-stage rule");
        var modified=AegisPaladinData.Build();modified.unlocks[0].chapterId="second";var second=new CharacterRuntimeState(modified);second.BeginRun("run","second");second.ClearedStage(1);Check(second.PendingChoices().Length==1,"definition can explicitly target a chapter without adding progression policy");
        foreach(var rule in a.Definition.unlocks)foreach(string choice in rule.choices)
        {
            var trial=Actor();trial.ClearedStage(rule.afterStage);Check(trial.ChooseSkill(rule.id,choice,null,true),"automatic first commit accepted");
            Check(!trial.ChooseSkill(rule.id,choice)&&trial.Snapshot.automaticChoices.SequenceEqual(new[]{choice}),"human-after-auto race cannot duplicate");
            Check(trial.Selected(rule.slot).implemented&&!trial.Selected(rule.slot).usesGcd,"all extension candidates implemented and oGCD");
            var unusable=new CharacterUseContext(true,true,false,0);
            Check(!trial.TryUse(rule.slot,unusable,42,_=>throw new Exception("invalid use applied"),out _),"invalid target rejects before effects");
            Check(!trial.TryUse(rule.slot,new CharacterUseContext(true,true,true,trial.Selected(rule.slot).range+1),42,_=>true,out _),"out of range rejects");
            Check(!trial.TryUse(rule.slot,new CharacterUseContext(false,true,true,0),42,_=>true,out _),"dead owner rejects");
            Check(!Use(trial,rule.slot,_=>false)&&Timing(trial,rule.slot).charges==1,"blocked approach/no shield handler rejection spends nothing");
            Check(Use(trial,CharacterSlot.Gcd1)&&Use(trial,rule.slot),"extension usable during shared GCD");
            Near(trial.Snapshot.gcd,1.5,"extension does not reset shared GCD");Check(!Use(trial,rule.slot),"cooldown blocks replay");
            float cd=Timing(trial,rule.slot).recharge;trial.ReduceCooldowns(AegisPaladinData.ReducedSkills,1);Near(Timing(trial,rule.slot).recharge,cd,"basic finisher doesn't silently reduce R/F/V");
            Check(AegisExtensionData.TriggersSun(choice)==(choice==AegisExtensionData.Spear||choice==AegisExtensionData.Brand||choice==AegisExtensionData.Retribution||choice==AegisExtensionData.Excalibur||choice==AegisExtensionData.Morning),"Sun direct extension whitelist, periodic and support excluded");
        }
        var preview=Actor();Check(preview.PreviewSkillChoice(AegisExtensionData.Excalibur)&&Use(preview,CharacterSlot.Ultimate),"editor preview can test final ultimate before Stage3");
        Check(preview.Snapshot.clearedEncounters.Length==0&&preview.Snapshot.chosenRules.Length==0,"preview does not fake card/clear progression");
        Check(!preview.PreviewSkillChoice("unoffered"),"editor cannot equip arbitrary ID");preview.ClearedStage(3);Check(preview.PendingChoices().Length==1&&preview.ChooseSkill(AegisExtensionData.Rule(CharacterSlot.Ultimate),AegisExtensionData.Morning),"real deterministic choice still required after preview");
        Check(!preview.PreviewSkillChoice(AegisExtensionData.Excalibur),"editor respects confirmed run choice");
    }
    static void Timelines()
    {
        var fx=new AegisExtensionState();fx.StartGround(100,4,-3);int ticks=0;
        for(int i=0;i<=900;i++){float now=100+i*.01f;int due=fx.TakeGroundTicks(now);ticks+=due;Check(fx.TakeGroundTicks(now)==0,"one host tick only even repeated update");}
        Check(ticks==8,"8 ticks at +1..+8 seconds");Near(fx.Data.groundX,4,"fixed ground X");Near(fx.Data.groundZ,-3,"fixed ground Z");
        Check(fx.TakeGroundTicks(200)==0,"expired ground cannot replay");
        fx.StartGround(10,1,2);Check(fx.TakeGroundTicks(13)==3,"catch up elapsed authoritative ticks");
        var peer=new AegisExtensionState();Check(peer.Restore(fx.Data),"absolute timeline restored");Near(peer.Data.groundEnd,18,"arrival does not restart eight seconds");
        Check(peer.TakeGroundTicks(13)==0&&peer.TakeGroundTicks(18)==5,"only remaining ticks after restore");
        Near(fx.Data.groundNext,14,"replica copy doesn't mutate host");
        fx.StartWings(20,0,0,33);fx.Link(123,20);Near(fx.Data.wingEnd,23,"three-second channel");Near(fx.Data.interventionEnd,24,"four-second link");
        Check(peer.Restore(fx.Data),"channel and link restore together");Near(peer.Data.wingYaw,33,"facing locked at cast");Check(peer.Data.interventionTarget==123,"link keeps actor ID");
        var bad=fx.Data.Copy();bad.groundX=float.NaN;Check(!peer.Restore(bad)&&peer.Data.interventionTarget==123,"invalid effect frame atomic rejection");
        fx.CancelWings();Check(fx.Data.wingEnd==0&&fx.Data.interventionTarget==123,"dash ends channel only");
        fx.Unlink();Check(fx.Data.interventionTarget==0,"removed/dead target unlink");fx.Clear();Check(fx.Data.groundEnd==0&&fx.Data.wingEnd==0&&fx.Data.interventionEnd==0,"retry/death/end clears all transients");
        var debt=new AegisDamageState();debt.Activate();debt.Split(400);debt.Extensions.StartGround(5,2,3);debt.Extensions.StartWings(5,2,3,180);debt.Extensions.Link(42,5);
        var state=new AegisDamageState();Check(state.Restore(debt.Capture()),"same job snapshot carries Q and extension state");Near(state.Pending,200,"Q debt not lost on extension restore");Near(state.Extensions.Data.groundEnd,13,"ground restored without recast");
        state.Clear();Near(state.Pending,0,"retry clears debt");Near(state.Extensions.Data.interventionEnd,0,"retry clears link");
    }
    static void DamageAndGeometry()
    {
        Near(AegisExtensionData.DamageScale(true,true),.32,"60% and20% DR multiply, never add to invulnerability");
        Near(AegisExtensionData.CriticalChance(.25f,true),.35,"mark adds percentage points");Near(AegisExtensionData.CriticalChance(.95f,true),1,"crit clamped");
        Near(AegisExtensionData.AttackAmount(100,1.1f,false),110,"attack power bonus before target mitigation");Near(AegisExtensionData.AttackAmount(100,1.1f,true),165,"single crit multiplier");
        foreach(CombatDamageKind kind in Enum.GetValues(typeof(CombatDamageKind)))
        {
            double shared=AegisExtensionData.SharedDamage(400,kind,true);
            if(kind==CombatDamageKind.Transfer||kind==CombatDamageKind.Deferred||kind==CombatDamageKind.System)Near(shared,0,"no recursive/system share "+kind);
            Near(AegisExtensionData.SharedDamage(400,kind,false),0,"dead guardian cannot share");
        }
        Check(!CombatDamageRules.CanReact(CombatDamageKind.Transfer),"transferred hit cannot reflect or gain Holy");
        var random=new Random(140018);
        for(int i=0;i<1000;i++)
        {
            double confirmed=random.NextDouble()*10000,share=AegisExtensionData.SharedDamage(confirmed,CombatDamageKind.Direct,true);
            double recipientShield=Math.Min(confirmed-share,random.NextDouble()*100),guardianShield=Math.Min(share,random.NextDouble()*100);
            var target=new AegisDamageState();var guardian=new AegisDamageState();target.Activate();guardian.Activate();
            double paid=target.Split(confirmed-share-recipientShield)+guardian.Split(share-guardianShield);
            Near(paid+target.Pending+guardian.Pending+recipientShield+guardianShield,confirmed,"transfer/shield/two Q debts conserve original confirmed total");
            paid+=target.Advance(10)+guardian.Advance(10);
            Near(paid+recipientShield+guardianShield,confirmed,"repayment conserves and cannot transfer again");
            Near(AegisExtensionData.SharedDamage(share,CombatDamageKind.Transfer,true),0,"mutual intervention terminates");
            double angle=random.NextDouble()*360-180,dist=random.NextDouble()*12;
            bool inside=AegisExtensionData.InCone(Math.Sin(angle*Math.PI/180)*dist,Math.Cos(angle*Math.PI/180)*dist,8,0,120);
            Check(inside==(dist<=8&&Math.Abs(angle)<=60),"wings math matches fixed visible cone");
        }
        Check(AegisExtensionData.InCone(0,-5,8,180,120)&&!AegisExtensionData.InCone(0,5,8,180,120),"behind caster protected, front not protected");
        var shields=new CombatShieldLedger();shields.GrantTimed(1,"shield",300*AegisRouteData.ShieldScale(AegisRouteData.Bulwark),3000,5);
        double amount=shields.Remaining*2;Near(amount,900,"retribution snapshots already amplified shield exactly once");Near(shields.Remaining,450,"snapshot doesn't consume shield");shields.Expire(5);Near(shields.Remaining,0,"expired shield cannot power attack");
    }
    static void RewardsAndRecovery()
    {
        var flow=new RaidRunFlow();var actor=Actor();actor.BeginRun("run","main");var cards=new RaidRewardRound();
        for(int stage=1;stage<=3;stage++)
        {
            Check(flow.TryPrepare(stage,3,false)&&flow.TryCountdown(flow.Revision)&&flow.TryStart(flow.Revision),"prepare once");
            Check(flow.Resolve(flow.Revision,true,0,4)==EncounterOutcome.Cleared,"boss clear wins simultaneous wipe");actor.ClearedStage(stage);
            Check(flow.FinishClear(flow.Revision,3,actor.PendingChoices().Length>0)&&flow.Phase==RaidBattlePhase.Reward,"final stage waits for V too");
            Check(!flow.FinishClear(flow.Revision,3,true),"duplicate clear callback refused");
            cards.Begin(flow.Revision);if(stage<3)cards.Offer(1,new[]{4,6,8});
            Check(cards.Cards(1).Length==(stage<3?3:0),"existing card count preserved; final no invented extra card");
            if(stage<3){Check(cards.Choose(flow.Revision,1,4)&&!cards.Choose(flow.Revision,1,4),"card committed once");Check(actor.PendingChoices().Length==1,"card is not a skill choice");}
            var rule=actor.PendingChoices()[0];Check(actor.ChooseSkill(rule.id,rule.choices[0],actor.RewardKey(rule)),"mandatory selection clears barrier");
            if(stage<3)Check(!flow.FinishFinalRewards(flow.Revision,3),"early final reward callback rejected");
        }
        Check(flow.FinishFinalRewards(flow.Revision,3)&&flow.Phase==RaidBattlePhase.Victory,"V then victory without Stage4");
        Check(!flow.TryPrepare(4,3,false)&&!flow.FinishFinalRewards(flow.Revision,3),"no fourth stage or duplicate results");
        flow.End();Check(!flow.FinishClear(flow.Revision-1,3,true),"late reward callback after close cannot resume");
        var gate=new RaidActionGate();var request=new RaidActionRequest{actor=1,epoch=4,sequence=1,skill=11,jobId=AegisPaladinData.JobId,ruleId="r",choiceId=AegisExtensionData.Spear,rewardKey="run/main/1/r"};
        Check(!gate.Accept(2,4,request)&&gate.Accept(1,4,request)&&!gate.Accept(1,4,request),"owner and duplicate gate shared with choice requests");
    }
}
