using System;
using System.Linq;
using RaidRogue;
using System.Collections.Generic;
namespace RaidRogue
{
    public sealed class PartyLife {public bool IsTank;}
    public sealed class Transform
    {
        public bool alive=true;public PartyLife life=new PartyLife{IsTank=true};
        public T GetComponent<T>() where T:class=>life as T;
    }
    public static class CombatRange {public static bool LivingPartyMember(Transform t)=>t!=null&&t.alive;}
    public static class RaidCombatAuthority {public static bool Replica;}
    public sealed class BossController
    {
        public float currentHp=100;public Transform aggro,castTarget;
        public Dictionary<Transform,float> _combatThreat=new Dictionary<Transform,float>();
        public void SetAggroTarget(Transform target){aggro=target;}
        /* ACTUAL_THREAT_METHODS */
    }
}
internal static class NineKeyPaladinTests
{
    /* ACTUAL_COMBO_POLICIES */
    static int checks;
    static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
    static CharacterUseContext Ready=>new CharacterUseContext(true,true,true,0);
    private static void ReadOnlyStateAccess(CharacterDefinition definition)
    {
        var state=new CharacterRuntimeState(definition);
        foreach(string route in new[]{AegisRouteData.Bulwark,AegisRouteData.Sun,AegisRouteData.Mirror})
        {
            Check(state.ChooseRoute(route,true),"scalar reader route fixture");
            var rule=definition.loadouts.Single(r=>r.routeId==route);
            foreach(string core in rule.choices)
            {
                Check(state.ChangeLoadout(rule.id,core,true),"scalar reader core selection");
                var saved=state.Snapshot;
                Check(state.CoreTrait==core&&state.CoreTrait==saved.coreTrait,"core scalar reflects latest choice");
                saved.coreTrait="mutated detached snapshot";
                Check(state.CoreTrait==core,"scalar reads do not expose mutable snapshot");
                var restored=new CharacterRuntimeState(definition);
                Check(restored.Restore(state.Snapshot)&&restored.CoreTrait==core,"restore updates scalar reader");
                restored.Reset(CharacterReset.Retry);
                Check(restored.CoreTrait==core,"retry retains scalar core state");
            }
        }
        var castDefinition=new CharacterDefinition{id="test.scalar-cast",name="cast",playable=true,
            slots=new[]{new CharacterSlotDefinition{slot=CharacterSlot.Gcd1,skillId="test.cast"}},
            skills=new[]{new CharacterSkillDefinition{id="test.cast",name="cast",implemented=true,handlerId="test",castTime=1,target=SkillTargetRule.Self}}};
        var caster=new CharacterRuntimeState(castDefinition);
        Check(caster.TryUse(CharacterSlot.Gcd1,Ready,-7,s=>true,out _),"scalar cast starts");
        caster.Tick(.25f);
        var snapshot=caster.Snapshot;
        Check(caster.CastingSkillId==snapshot.castSkillId&&caster.CastingTarget==snapshot.castTarget&&caster.CastingRemaining==snapshot.castRemaining,"cast scalars match detached snapshot");
        snapshot.castTarget=999;
        Check(caster.CastingTarget==-7,"cast target scalar does not leak snapshot mutation");
        var pending=new CharacterRuntimeState(castDefinition);
        Check(pending.Restore(caster.Snapshot)&&pending.CastingTarget==-7&&pending.CastingRemaining==.75f,"pending cast restore exposes current scalars");
        pending.Tick(1);
        Check(pending.FinishCast(Ready,s=>true,out _)&&pending.CastingSkillId=="","cast completion clears scalar state");
        caster.CancelCast();
        Check(caster.CastingSkillId==""&&caster.CastingRemaining==0,"cast cancellation clears scalar state");

        int sink=0;
        for(int i=0;i<100;i++)sink+=state.CoreTrait.Length+state.CastingSkillId.Length+state.CastingTarget+(int)state.CastingRemaining;
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<1000;i++)sink+=state.CoreTrait.Length+state.CastingSkillId.Length+state.CastingTarget+(int)state.CastingRemaining;
        long scalarBytes=GC.GetAllocatedBytesForCurrentThread()-before;
        before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<1000;i++){var copy=state.Snapshot;sink+=copy.coreTrait.Length+copy.castSkillId.Length+copy.castTarget+(int)copy.castRemaining;}
        long snapshotBytes=GC.GetAllocatedBytesForCurrentThread()-before;
        Check(scalarBytes==0&&snapshotBytes>0&&sink>0,"frame-time scalar reads allocate zero while snapshots remain independent copies");
        Console.WriteLine("Managed allocation check, 1000 reads: scalars="+scalarBytes+" bytes; full snapshots="+snapshotBytes+" bytes. Not a Unity FPS measurement.");
    }
    public static int Main()
    {
        try
        {
            var shields=new CombatShieldLedger();
            shields.GrantTimed(1,"oath",100,1000,6,true);
            shields.GrantTimed(1,"oath",30,1000,8,true);
            Check(shields.RemainingFor(1,"oath")==100,"weaker oath refresh preserves larger remaining shield");
            shields.GrantTimed(2,"oath",50,1000,8,true);
            Check(shields.Remaining==150,"different casters retain independent protection");
            shields.GrantTimed(1,"oath",120,1000,8,true);
            Check(shields.Remaining==170,"stronger oath replaces rather than adds same caster budget");
            var sun=new AegisSunState();
            Check(!sun.Shield&&sun.FirstReward==30,"sun starts sword stance");
            sun.BeginSecond();sun.Toggle();
            Check(sun.Shield&&sun.FirstReward==20&&!sun.SecondShield,"second combo locks starting stance while first uses current stance");
            sun.FinishSecond();sun.BeginSecond();sun.Toggle();
            Check(sun.SecondShield&&!sun.Shield,"shield second remains shield after toggling");
            var savedSun=sun.Capture();var sunCopy=new AegisSunState();
            Check(sunCopy.Restore(savedSun)&&sunCopy.SecondShield,"stance snapshot restores locked second");
            foreach(int size in new[]{4,8})
            {
                sun.SetMark(99,1,Enumerable.Range(1,size),size);
                Check(sun.MarkRights==size-1&&sun.Consume(1,true)==0,"owner excluded from cooperation");
                Check(sun.Consume(2,false)==0&&sun.MarkRights==size-1,"automatic hit cannot consume rights");
                float total=0;for(int id=2;id<=size;id++){total+=sun.Consume(id,true);Check(sun.Consume(id,true)==0,"one claim per ally");}
                Check(Math.Abs(total-(size==4?AegisSunState.FourBudget:AegisSunState.EightBudget))<.001,"four/eight budgets separate");
            }
            sun.SetMark(99,1,new[]{1,2,2,3,0},4);Check(sun.MarkRights==2,"duplicate members deduplicated");
            var mark=sun.Capture();sunCopy.Restore(mark);mark.eligible[0]=100;
            Check(sunCopy.Consume(2,true)>0,"snapshot is deep copied");
            sun.SetMark(99,1,new[]{1,2},4);Check(sun.MarkRights==1,"reapply replaces unused rights");
            sun.Advance(float.NaN);Check(sun.MarkRights==1,"invalid elapsed time ignored");
            sun.Advance(AegisSunState.MarkDuration);Check(sun.MarkRights==0&&sun.Consume(2,true)==0,"expired mark cannot proc");
            Check(!sun.Restore(new AegisSunSnapshot{eligible=new[]{2,2}}),"duplicate snapshot rights rejected");
            Check(!sun.Restore(new AegisSunSnapshot{markRemaining=float.NaN}),"nonfinite snapshot rejected");
            sun.Clear();Check(!sun.Shield&&!sun.SecondRecorded&&sun.MarkRights==0,"stage/death reset clears sun state");
            var sunClaims=new CombatActionClaims(7,1);ulong sunAction=1;
            foreach(var policy in new[]{SunSwordResource,SunShieldResource})
                for(int chain=1;chain<=2;chain++)for(int step=1;step<=3;step++)
                {
                    var receipt=new CombatActionReceipt(7,1,sunAction++,CombatActionOrigin.SkillInput,CombatActionPurpose.Attack,chain,step,true,true,true);
                    bool granted=sunClaims.TryClaimResource(policy,receipt,out float gain);
                    Check(granted==(chain==1&&step==3)&&gain==(chain==1&&step==3?(policy==SunSwordResource?30:20):0),"sun actual policies never add normal second reward");
                }
            var definition=AegisPaladinData.BuildNineKey();
            ReadOnlyStateAccess(definition);
            foreach(string route in new[]{AegisRouteData.Bulwark,AegisRouteData.Mirror})
            {
                var coreState=new CharacterRuntimeState(definition);coreState.ChooseRoute(route,true);
                var coreRule=definition.loadouts.Single(r=>r.routeId==route);
                foreach(string choice in coreRule.choices)
                {
                    Check(!coreState.ChangeLoadout(coreRule.id,choice,false),"core cannot change in combat");
                    Check(coreState.ChangeLoadout(coreRule.id,choice,true)&&coreState.Snapshot.coreTrait==choice,"each route core selectable");
                    var savedCore=new CharacterRuntimeState(definition);Check(savedCore.Restore(coreState.Snapshot),"core save roundtrip");
                    savedCore.Reset(CharacterReset.Retry);Check(savedCore.Snapshot.coreTrait==choice,"retry retains core");
                }
                var previousCore=new CharacterRuntimeState(AegisPaladinData.BuildNineKey(true,false));previousCore.ChooseRoute(route,true);
                var oldCore=previousCore.Snapshot;Check(coreState.Restore(oldCore)&&oldCore.coreTrait=="","old core-less save upgraded without mutating source");
                Check(coreState.Snapshot.coreTrait==coreRule.choices[0],"migration picks one compatible default");
            }
            var gesture=new AegisRecallInput();
            Check(gesture.Step(true,true,0,false)==0&&gesture.Step(false,false,.2f,false)==1,"short R installs only on release");
            Check(gesture.Step(true,true,1,false)==0&&gesture.Step(false,true,1.5f,false)==2,"held R recalls instead of installs");
            Check(gesture.Step(false,true,2,false)==0&&gesture.Step(false,false,3,false)==0,"long hold and release do not repeat action");
            gesture.Step(true,true,4,false);Check(gesture.Step(false,true,4.2f,true)==0&&gesture.Step(false,false,5,false)==0,"defense/menu cancellation drops pending short cast");
            var traitGlyph=new AegisGlyphState();
            Check(traitGlyph.Install(0,0,1)&&!traitGlyph.Install(5,0,1),"citadel limits placement to one");
            Check(!traitGlyph.Visit(0,1,2.5f,0,2.5f,0)&&traitGlyph.Visit(0,1,2.5f,0,2.5f,0,3),"citadel radius reaches farther");
            Check(!traitGlyph.RecallUnused(),"used glyph cannot be refunded");
            traitGlyph.Install(5,0);Check(traitGlyph.RecallUnused()&&traitGlyph.Glyphs.Count==1&&traitGlyph.Glyphs[0].used.Length==1,"recall skips used glyph and removes only unused one");
            Check(!traitGlyph.RecallUnused(),"repeated recall cannot refund removed glyph");
            traitGlyph.Clear();traitGlyph.Install(0,0);var refundState=new CharacterRuntimeState(definition);refundState.ChooseRoute(AegisRouteData.Bulwark,true);
            var recallContext=new CharacterUseContext(true,true,true,0,costScale:0);
            Check(refundState.TryUse(CharacterSlot.Identity,recallContext,0,s=>{if(!traitGlyph.RecallUnused())return false;refundState.Gain(AegisPaladinData.Holy,20);return true;},out _)&&refundState.Resource(AegisPaladinData.Holy)==20,"zero-resource recall refunds twenty without installation cost");
            refundState.Tick(1,false,true);Check(!refundState.TryUse(CharacterSlot.Identity,recallContext,0,s=>traitGlyph.RecallUnused(),out _)&&refundState.Resource(AegisPaladinData.Holy)==20,"empty recall does not create resources");
            var fastResponse=new AegisMirrorState();fastResponse.Begin(11);fastResponse.Protect(CombatDamageKind.Direct,false,false,1,1);
            fastResponse.Begin(22);fastResponse.Protect(CombatDamageKind.Direct,false,false,1,1);Check(fastResponse.Count==1&&fastResponse.First==11,"flash keeps one original response");
            Check(AegisMirrorState.ResponsePower(AegisPaladinData.Flash,fastResponse.FirstAge)==270,"fresh response has fast bonus");
            fastResponse.Advance(4.1f);Check(AegisMirrorState.ResponsePower(AegisPaladinData.Flash,fastResponse.FirstAge)==180,"slow response retains baseline damage only");
            var agedCopy=new AegisMirrorState();Check(agedCopy.Restore(fastResponse.Capture())&&agedCopy.FirstAge==fastResponse.FirstAge,"response age survives snapshot");
            Check(AegisMirrorState.ResponsePower(AegisPaladinData.Reflection,0)==90,"reflector sacrifices half of attack bonus");
            var fifoReflector=new AegisMirrorState();fifoReflector.Begin(11);fifoReflector.Protect(CombatDamageKind.Direct,false,false,1);fifoReflector.Begin(22);fifoReflector.Protect(CombatDamageKind.Direct,false,false,1);
            Check(fifoReflector.Consume()==11&&fifoReflector.Consume()==22,"followup protection uses each recorded target in FIFO order");
            var mirror=new AegisMirrorState();
            Check(mirror.Begin(11),"mirror support selected");
            Check(mirror.Protect(CombatDamageKind.Direct,false,false,100)&&mirror.Count==1,"precise hit earns one response");
            Check(mirror.Protect(CombatDamageKind.Direct,false,false,500)&&mirror.Count==1,"self and ally repeated hits share one R reward");
            mirror.Consume();Check(mirror.Protect(CombatDamageKind.Direct,false,false,100)&&mirror.Count==0,"consuming response does not reopen same R reward");
            mirror.Advance(.7f);Check(mirror.Precise==0&&mirror.Protect(CombatDamageKind.Direct,false,false,100),"ordinary protection survives precise window");
            mirror.Advance(2);Check(!mirror.Protect(CombatDamageKind.Direct,false,false,100),"protection expires");
            foreach(var kind in new[]{CombatDamageKind.Area,CombatDamageKind.Periodic,CombatDamageKind.Reflection,CombatDamageKind.Extra,CombatDamageKind.Deferred,CombatDamageKind.System,CombatDamageKind.Transfer})
            {mirror.Clear();mirror.Begin(11);Check(!mirror.Protect(kind,false,false,100)&&mirror.Count==0,"non-guardable kind cannot mitigate or earn");}
            mirror.Clear();mirror.Begin(11);
            Check(!mirror.Protect(CombatDamageKind.Direct,true,false,100)&&!mirror.Protect(CombatDamageKind.Direct,false,true,100)&&!mirror.Protect(CombatDamageKind.Direct,false,false,0),"bypass and zero damage do not earn responses");
            mirror.Protect(CombatDamageKind.Direct,false,false,100);mirror.Advance(2);mirror.Begin(22);mirror.Protect(CombatDamageKind.Direct,false,false,1);
            Check(mirror.Count==2&&mirror.First==11,"queue stores cast support in order independent of damage amount");
            mirror.Begin(33);mirror.Protect(CombatDamageKind.Direct,false,false,100);Check(mirror.Count==2,"queue bounded at two");
            var mirrorSave=mirror.Capture();var mirrorCopy=new AegisMirrorState();Check(mirrorCopy.Restore(mirrorSave),"mirror snapshot restores");mirrorSave.responses[0]=44;
            Check(mirrorCopy.Consume()==11&&mirrorCopy.Consume()==22&&mirrorCopy.Consume()==0,"FIFO deep copy and empty consume");
            Check(mirrorCopy.Protect(CombatDamageKind.Direct,false,false,100)&&mirrorCopy.Count==0,"full-queue cast cannot grant after later consumption");
            mirrorCopy.Clear();Check(mirrorCopy.Count==0&&mirrorCopy.Remaining==0,"death/stage reset drops protection and queue");
            Check(mirrorCopy.Restore(null),"old snapshot restores no mirror state");
            Check(!mirrorCopy.Restore(new AegisMirrorSnapshot{remaining=float.NaN})&&!mirrorCopy.Restore(new AegisMirrorSnapshot{responses=new[]{0}}),"invalid mirror snapshots rejected");
            var mirrorActor=new CharacterRuntimeState(definition);mirrorActor.ChooseRoute(AegisRouteData.Mirror,true);
            Check(!mirrorActor.TryUse(CharacterSlot.Identity,Ready,0,s=>true,out _),"mirror requires thirty holy");
            mirrorActor.Gain(AegisPaladinData.Holy,30);
            Check(mirrorActor.TryUse(CharacterSlot.Identity,Ready,0,s=>mirrorCopy.Begin(11),out _)&&mirrorActor.Resource(AegisPaladinData.Holy)==0,"mirror spends thirty once");
            Check(mirrorActor.TryUse(CharacterSlot.Job1,Ready,0,s=>true,out _),"Q remains available at zero resource during mirror protection");
            var glyphs=new AegisGlyphState();
            Check(glyphs.Install(0,0)&&glyphs.Install(5,0)&&glyphs.Full,"two remote glyphs admitted");
            Check(!glyphs.Install(8,0)&&glyphs.Glyphs[0].x==0,"third placement blocked without replacing old glyph");
            for(int id=1;id<=8;id++)
            {
                Check(glyphs.Visit(0,id,-4,0,4,0),"fast movement segment crosses glyph for each ally");
                Check(!glyphs.Visit(0,id,4,0,0,0),"reentry never repeats same glyph reward");
                Check(glyphs.Visit(1,id,4,0,5,0),"other glyph tracks its own once-per-ally claim");
            }
            Check(!glyphs.Visit(0,9,0,0,0,0),"claims bounded to raid party size");
            var glyphSnapshot=glyphs.Capture();var glyphCopy=new AegisGlyphState();
            Check(glyphCopy.Restore(glyphSnapshot)&&!glyphCopy.Visit(0,1,0,0,0,0),"snapshot preserves already granted rights");
            glyphSnapshot[0].used[0]=99;Check(!glyphCopy.Visit(0,1,0,0,0,0),"snapshot arrays are deep copied");
            glyphCopy.Advance(19);Check(glyphCopy.Full,"unexpired glyph remains");
            glyphCopy.Advance(1);Check(!glyphCopy.Full&&glyphCopy.Glyphs.Count==0&&glyphCopy.Install(1,1),"expiry frees capacity");
            glyphCopy.Clear();Check(glyphCopy.Glyphs.Count==0,"death/stage reset clears glyphs");
            Check(!glyphCopy.Install(float.NaN,0),"invalid placement coordinates rejected");
            Check(!glyphCopy.Restore(new[]{new AegisGlyph{remaining=20,used=new[]{1,1}}}),"duplicate restored claim rejected");
            Check(glyphCopy.Restore(null),"pre-glyph snapshot restores empty state");
            glyphCopy.Install(0,0);Check(!glyphCopy.Visit(0,1,-4,3,4,3),"nearby path outside glyph does not trigger");
            Check(AegisGlyphState.DecodePoint("1.25|-2.5",out float gx,out float gz)&&gx==1.25f&&gz==-2.5f,"invariant placement coordinates decode");
            foreach(string invalid in new[]{null,"","1","1|2|3","NaN|0","0|Infinity","a|b"})Check(!AegisGlyphState.DecodePoint(invalid,out _,out _),"malformed placement rejected");
            var glyphActor=new CharacterRuntimeState(definition);glyphActor.ChooseRoute(AegisRouteData.Bulwark,true);
            glyphActor.Gain(AegisPaladinData.Holy,100);var liveGlyphs=new AegisGlyphState();
            Check(glyphActor.TryUse(CharacterSlot.Identity,Ready,0,s=>liveGlyphs.Install(0,0),out _)&&glyphActor.Resource(AegisPaladinData.Holy)==60,"real identity consumes 40 on install");
            glyphActor.Tick(1,false,true);
            Check(glyphActor.TryUse(CharacterSlot.Identity,Ready,0,s=>liveGlyphs.Install(3,0),out _)&&glyphActor.Resource(AegisPaladinData.Holy)==20,"second glyph consumes once");
            glyphActor.Tick(1,false,true);glyphActor.Gain(AegisPaladinData.Holy,80);
            Check(!glyphActor.TryUse(CharacterSlot.Identity,Ready,0,s=>liveGlyphs.Install(6,0),out _)&&glyphActor.Resource(AegisPaladinData.Holy)==100,"rejected full placement consumes no resource");
            Check(glyphActor.Snapshot.skills.First(s=>s.id==AegisRouteData.Identity(AegisRouteData.Bulwark)).charges==1,"rejected placement consumes no cooldown");
            var loadout=new CharacterRuntimeState(definition);
            var previousNine=new CharacterRuntimeState(AegisPaladinData.BuildNineKey(false));
            previousNine.ChooseRoute(AegisRouteData.Sun,true);
            Check(previousNine.PreviewSkillChoice(AegisExtensionData.Ground),"previous nine-key preview fixture");
            Check(previousNine.TryUse(CharacterSlot.OffGcd1,Ready,0,s=>true,out _),"previous selection cooldown fixture");
            var migratedLoadout=new CharacterRuntimeState(definition);
            Check(migratedLoadout.Restore(previousNine.Snapshot),"pre-loadout nine-key save migrates");
            Check(migratedLoadout.Selected(CharacterSlot.OffGcd1).id==AegisPaladinData.Id(CharacterSlot.OffGcd1)&&migratedLoadout.Snapshot.coreTrait==AegisPaladinData.Continuous,"old preview becomes default support and one sun core");
            Check(!migratedLoadout.TryUse(CharacterSlot.OffGcd1,Ready,0,s=>true,out _),"migration preserves outgoing skill cooldown");
            var invalidPrevious=previousNine.Snapshot;invalidPrevious.skills[0].charges=-1;
            Check(!new CharacterRuntimeState(definition).Restore(invalidPrevious),"loadout migration validates old snapshot before conversion");
            Check(!loadout.ChangeLoadout("loadout.core",AegisPaladinData.Guardian,true),"core rejected before compatible route");
            Check(loadout.ChooseRoute(AegisRouteData.Sun,true)&&loadout.Snapshot.coreTrait==AegisPaladinData.Continuous,"sun chooses one default core");
            Check(!loadout.ChangeLoadout("loadout.combo2",AegisPaladinData.Wave,false),"combat swap rejected");
            Check(!loadout.ChangeLoadout("loadout.combo2",AegisPaladinData.Hand,true),"foreign slot choice rejected");
            foreach(var option in definition.loadouts.Where(r=>string.IsNullOrEmpty(r.routeId)||r.routeId==AegisRouteData.Sun))
                foreach(string candidate in option.choices)
                {
                    Check(loadout.ChangeLoadout(option.id,candidate,true),"all lobby candidates directly selectable");
                    var roundTrip=new CharacterRuntimeState(definition);Check(roundTrip.Restore(loadout.Snapshot),"loadout survives snapshot");
                    Check((int)option.slot==0?roundTrip.Snapshot.coreTrait==candidate:roundTrip.Selected(option.slot).id==candidate,"selected build preserved");
                }
            Check(loadout.ChangeLoadout("loadout.support3",AegisPaladinData.Hand,true),"choose support");
            Check(loadout.TryUse(CharacterSlot.OffGcd1,Ready,0,s=>true,out _),"use hand starts cooldown");
            float remaining=loadout.Snapshot.skills.First(s=>s.id==AegisPaladinData.Hand).recharge;
            Check(loadout.ChangeLoadout("loadout.support3",AegisPaladinData.Slam,true),"swap while recharging allowed in lobby");
            Check(!loadout.TryUse(CharacterSlot.OffGcd1,Ready,0,s=>true,out _)&&loadout.Snapshot.skills.First(s=>s.id==AegisPaladinData.Slam).recharge==remaining,"swap cannot reset cooldown");
            loadout.ChooseRoute(AegisRouteData.Mirror,true);Check(loadout.Snapshot.coreTrait==AegisPaladinData.Flash,"route change selects compatible mirror core");
            var badCore=loadout.Snapshot;badCore.coreTrait=AegisPaladinData.Guardian;Check(!new CharacterRuntimeState(definition).Restore(badCore),"wrong route core rejected on restore");
            sun.Clear();sun.QueueProtection(2,100);sun.QueueProtection(2,100);sun.QueueProtection(3,100);
            Check(sun.Capture().followups.Length==2,"followups bounded by target not repeated casts");
            Check(sun.TakeProtection(2).Length==0,"followup does not fire early");
            var followCopy=new AegisSunState();Check(followCopy.Restore(sun.Capture()),"pending protection replicated");
            Check(followCopy.TakeProtection(1).Sum(x=>x.amount)==200&&followCopy.TakeProtection(10).Length==0,"due protection consumed exactly once");
            sun.Clear();Check(sun.TakeProtection(10).Length==0,"reset drops pending protection");
            Check(sun.Restore(new AegisSunSnapshot{followups=null}),"older sun snapshot without followup field restores");
            Check(!sun.Restore(new AegisSunSnapshot{followups=new[]{new AegisFollowupShield{target=2,amount=1,remaining=float.NaN}}}),"nonfinite pending protection rejected");
            sun.SetMark(99,1,new[]{1,2,3,4},4,AegisPaladinData.ContinuousScale);
            Check(sun.Consume(2,true)+sun.Consume(3,true)+sun.Consume(4,true)==60,"continuous halves cooperation budget");
            Check(CharacterDefinitionValidation.Check(new[]{definition}).Length==0,"live nine-key definition valid");
            Check(definition.controlLayout==CharacterControlLayout.NineKey&&definition.slots.Count(s=>s.slot!=CharacterSlot.Potion)==9,"nine actual class slots");
            Check(definition.slots.All(s=>s.slot!=CharacterSlot.Job2&&s.slot!=CharacterSlot.Active),"no E/duplicate R slots");
            var fresh=new CharacterRuntimeState(definition);
            var auxiliary=new CharacterRuntimeState(definition);
            Check(auxiliary.Snapshot.auxiliaryTraits.Length==2&&auxiliary.HasAuxiliary(AegisPaladinData.Steadfast)&&auxiliary.HasAuxiliary(AegisPaladinData.FarCall),"new tank has two distinct default auxiliary traits");
            Check(!auxiliary.ChangeAuxiliary("auxiliary.1",AegisPaladinData.FarCall,true),"duplicate auxiliary rejected");
            Check(!auxiliary.ChangeAuxiliary("auxiliary.1",AegisPaladinData.LastingSanctuary,false),"combat auxiliary swap rejected");
            Check(!auxiliary.ChangeAuxiliary("auxiliary.3",AegisPaladinData.LastingSanctuary,true)&&!auxiliary.ChangeAuxiliary("auxiliary.1","invented",true),"invalid slot and candidate rejected");
            var auxDefense=new AegisDamageState();
            float auxScale=AegisPaladinData.AuxiliaryCooldownScale(AegisPaladinData.Id(CharacterSlot.Job1),true);
            Check(auxiliary.TryUse(CharacterSlot.Job1,new CharacterUseContext(true,true,true,0,cooldownScale:auxScale),0,s=>{auxDefense.Activate(true,true);return true;},out _),"extended Q transaction");
            Check(auxDefense.FaithRemaining==8&&auxiliary.Snapshot.skills.First(s=>s.id==AegisPaladinData.Id(CharacterSlot.Job1)).recharge==30,"extended Q trades 8 seconds for 30 second cooldown");
            Check(auxiliary.ChangeAuxiliary("auxiliary.1",AegisPaladinData.LastingSanctuary,true),"distinct alternate allowed in lobby");
            Check(auxiliary.Snapshot.skills.First(s=>s.id==AegisPaladinData.Id(CharacterSlot.Job1)).recharge==30,"removing auxiliary does not reset or shorten running cooldown");
            var auxCopy=auxiliary.Snapshot;auxCopy.auxiliaryTraits[0]="invalid";
            Check(auxiliary.HasAuxiliary(AegisPaladinData.LastingSanctuary),"auxiliary snapshot owns its array");
            Check(!new CharacterRuntimeState(definition).Restore(auxCopy),"unknown saved auxiliary rejected");
            auxCopy=auxiliary.Snapshot;auxCopy.auxiliaryTraits[1]=auxCopy.auxiliaryTraits[0];
            Check(!new CharacterRuntimeState(definition).Restore(auxCopy),"duplicate saved auxiliary rejected");
            auxCopy=auxiliary.Snapshot;auxCopy.auxiliaryTraits=new[]{AegisPaladinData.FarCall};
            Check(!new CharacterRuntimeState(definition).Restore(auxCopy),"partial saved auxiliary rejected");
            auxCopy=auxiliary.Snapshot;auxCopy.auxiliaryTraits=null;
            var oldAux=new CharacterRuntimeState(definition);Check(oldAux.Restore(auxCopy)&&oldAux.HasAuxiliary(AegisPaladinData.Steadfast),"legacy missing auxiliary restores defaults");
            var auxRestored=new CharacterRuntimeState(definition);Check(auxRestored.Restore(auxiliary.Snapshot)&&auxRestored.HasAuxiliary(AegisPaladinData.LastingSanctuary),"selected auxiliary survives roundtrip");
            auxiliary.Reset(CharacterReset.Retry);Check(auxiliary.HasAuxiliary(AegisPaladinData.LastingSanctuary),"retry preserves auxiliary choice");
            auxiliary.ChooseRoute(AegisRouteData.Sun,true);Check(auxiliary.HasAuxiliary(AegisPaladinData.LastingSanctuary),"route switch preserves shared auxiliary choice");
            Check(AegisPaladinData.AuxiliaryThreatRange(true)==AegisPaladinData.CallRange+5&&AegisPaladinData.AuxiliaryThreatRange(false)==AegisPaladinData.CallRange,"F optional range only");
            Check(AegisPaladinData.AuxiliaryUltimateDuration(true)==10&&AegisPaladinData.AuxiliaryUltimateDuration(false)==8,"V optional duration only");
            Check(AegisPaladinData.AuxiliaryCooldownScale(AegisPaladinData.Id(CharacterSlot.Counter),true)==1,"Q trait never penalizes counter");
            foreach(var firstTrait in definition.auxiliaryTraits)foreach(var secondTrait in definition.auxiliaryTraits)
            {
                if(firstTrait.id==secondTrait.id)continue;
                foreach(var auxRoute in definition.routes)
                {
                    var pairState=new CharacterRuntimeState(definition);var pairSnapshot=pairState.Snapshot;
                    pairSnapshot.auxiliaryTraits=new[]{firstTrait.id,secondTrait.id};
                    Check(pairState.Restore(pairSnapshot)&&pairState.ChooseRoute(auxRoute.id,true),"all distinct auxiliary pairs valid on each route");
                    pairState.Reset(CharacterReset.NextStage);
                    Check(pairState.Snapshot.auxiliaryTraits.SequenceEqual(pairSnapshot.auxiliaryTraits),"stage boundary retains pair order");
                    var pairRestored=new CharacterRuntimeState(definition);
                    Check(pairRestored.Restore(pairState.Snapshot)&&pairRestored.Snapshot.auxiliaryTraits.SequenceEqual(pairSnapshot.auxiliaryTraits),"every route pair roundtrips independently");
                }
            }
            Check(fresh.Resource(AegisPaladinData.Holy)==0,"zero starting holy fixture");
            var defense=new AegisDamageState();
            Check(fresh.TryUse(CharacterSlot.Job1,Ready,0,s=>{defense.Activate(true);return true;},out _),"Q usable with zero holy");
            double reduced=defense.ReduceIncoming(1000,CombatDamageKind.Direct,CombatDefenseType.Physical);
            Check(Math.Abs(reduced-700)<.001,"Q mitigates before absorption");
            Check(Math.Abs(defense.Split(reduced-200)-500)<.001&&defense.Pending==0,"shield then HP with no second reduction or debt");
            Check(defense.Advance(1)==0&&defense.Pending==0,"new Q never schedules repayment");
            defense.Activate(true);Check(Math.Abs(defense.ReduceIncoming(1000,CombatDamageKind.Area,CombatDefenseType.Magic)-700)<.001,"refresh does not stack reduction");
            var restoredDefense=new AegisDamageState();Check(restoredDefense.Restore(defense.Capture())&&restoredDefense.DirectFaith,"Q mode replicated in job snapshot");
            foreach(var kind in new[]{CombatDamageKind.System,CombatDamageKind.Transfer,CombatDamageKind.Deferred})
                Check(restoredDefense.ReduceIncoming(1000,kind,CombatDefenseType.Physical)==1000,"no second mitigation of special damage");
            Check(restoredDefense.ReduceIncoming(1000,CombatDamageKind.Direct,CombatDefenseType.Bypass)==1000,"bypass mechanics retained");
            restoredDefense.Advance(AegisPaladinData.NineKeyFaithDuration);
            Check(restoredDefense.ReduceIncoming(1000,CombatDamageKind.Direct,CombatDefenseType.Physical)==1000,"Q expires");
            for(int i=0;i<5;i++){Check(!defense.ShieldAbsorbed(1000,false)&&defense.AbsorbLock==0,"absorption and guard have no new resource gate");defense.Advance(1);}
            var claims=new CombatActionClaims(1,1);ulong action=1;
            for(int chain=1;chain<=2;chain++)for(int step=1;step<=3;step++)
            {
                var receipt=new CombatActionReceipt(1,1,action++,CombatActionOrigin.SkillInput,CombatActionPurpose.Attack,chain,step,true,true,true);
                bool paid=claims.TryClaimResource(NineKeyComboResource,receipt,out float amount);
                Check(paid==(step==3)&&amount==(step==3?20:0),"both combo finishers alone grant 20");
                if(paid)Check(!claims.TryClaimResource(NineKeyComboResource,receipt,out _)&&!claims.TryClaimResource(LegacyComboResource,receipt,out _),"multi hit and old reward cannot add another grant");
            }
            Check(fresh.Selected(CharacterSlot.Gcd1).gcd==2&&fresh.Selected(CharacterSlot.Gcd2).gcd==2,"both combo base GCDs use prototype 2 seconds");
            Check(fresh.Selected(CharacterSlot.OffGcd2).id==AegisPaladinData.Id(CharacterSlot.Job2)&&fresh.Unlocked(CharacterSlot.OffGcd2),"F taunt available from start");
            Check(fresh.Selected(CharacterSlot.Counter).castTime==0&&fresh.Selected(CharacterSlot.Counter).movement==null,"counter no forced rush");
            Check(!fresh.Unlocked(CharacterSlot.Ultimate),"V story unlock retained");
            var ultimate=new CharacterRuntimeState(definition);
            ultimate.BeginRun("ultimate",CharacterRewardKey.CurrentChapter);ultimate.ClearedStage(3);
            Check(ultimate.ChooseSkill(definition.unlocks.First(r=>r.slot==CharacterSlot.Ultimate).id,AegisExtensionData.Morning),"stage 3 unlocks sanctuary");
            Check(!ultimate.TryUse(CharacterSlot.Ultimate,Ready,1,s=>true,out _),"empty dedicated gauge blocks V");
            ultimate.Gain(AegisPaladinData.UltimateGauge,150);
            Check(ultimate.Resource(AegisPaladinData.UltimateGauge)==100,"dedicated gauge capped at 100");
            Check(!ultimate.TryUse(CharacterSlot.Ultimate,Ready,1,s=>false,out _)&&ultimate.Resource(AegisPaladinData.UltimateGauge)==100,"rejected handler preserves gauge");
            Check(ultimate.Resource(AegisPaladinData.Holy)==0&&ultimate.TryUse(CharacterSlot.Ultimate,Ready,1,s=>true,out _),"V works without holy");
            Check(ultimate.Resource(AegisPaladinData.UltimateGauge)==0&&!ultimate.TryUse(CharacterSlot.Ultimate,Ready,1,s=>true,out _),"V spends gauge once and cannot repeat");
            ultimate.Gain(AegisPaladinData.UltimateGauge,40);
            var restoredUltimate=new CharacterRuntimeState(definition);
            Check(restoredUltimate.Restore(ultimate.Snapshot)&&restoredUltimate.Resource(AegisPaladinData.UltimateGauge)==40,"save retains dedicated gauge");
            ultimate.Reset(CharacterReset.Retry);
            Check(ultimate.Resource(AegisPaladinData.UltimateGauge)==0,"retry resets prototype gauge");
            var execution=new CombatActionExecution(1);
            for(int chain=1;chain<=2;chain++)for(int step=1;step<=3;step++)
            {
                execution.Begin(CombatActionPurpose.Attack,chain,step,true,true);
                Check(!execution.TryResolveFinisherGauge(false),"miss cannot grant gauge");
                Check(execution.TryResolveFinisherGauge(true)==(step==3),"only combo final hit grants gauge");
                Check(execution.TryResolveResource(NineKeyComboResource,true,out _)==(step==3),"holy claim independent of gauge");
                Check(!execution.TryResolveFinisherGauge(true),"multi hit cannot duplicate gauge");execution.End();
            }
            foreach(bool combat in new[]{false,true})foreach(bool target in new[]{false,true})
            {
                execution.Begin(CombatActionPurpose.Attack,1,3,combat,target);
                Check(execution.TryResolveFinisherGauge(true)==(combat&&target),"combat and valid target required");execution.End();
            }
            execution.Begin(CombatActionPurpose.Attack,0,3,true,true);
            Check(!execution.TryResolveFinisherGauge(true),"non combo V cannot regenerate gauge");execution.End();
            Check(!execution.TryResolveFinisherGauge(true),"late hit outside action cannot grant gauge");
            foreach(string route in new[]{AegisRouteData.Bulwark,AegisRouteData.Sun,AegisRouteData.Mirror})
            foreach(string r in new[]{AegisExtensionData.Spear,AegisExtensionData.Ground,AegisExtensionData.Wings})
            foreach(string f in new[]{AegisExtensionData.Intervention,AegisExtensionData.Brand,AegisExtensionData.Retribution})
            foreach(string v in new[]{AegisExtensionData.Excalibur,AegisExtensionData.Morning})
            {
                var old=new CharacterRuntimeState(AegisPaladinData.Build());
                old.ChooseRoute(route,true);old.BeginRun("migration",CharacterRewardKey.CurrentChapter);
                for(int stage=0;stage<=3;stage++)
                {
                    if(stage>0)
                    {
                        old.ClearedStage(stage);
                        var rule=old.Definition.unlocks[stage-1];
                        Check(old.ChooseSkill(rule.id,stage==1?r:stage==2?f:v),"old stage choice");
                    }
                    var source=old.Snapshot;var converted=new CharacterRuntimeState(definition);
                    Check(converted.Restore(source),"valid old save migrates");
                    Check(source.selections.Length==12&&source.selections.Any(s=>s.slot==CharacterSlot.Job2),"source save untouched");
                    Check(converted.RouteId==route&&converted.Snapshot.clearedStage==stage,"route and stage retained");
                    Check(converted.Selected(CharacterSlot.OffGcd2).id==AegisPaladinData.Id(CharacterSlot.Job2),"old F choice cannot replace taunt");
                    Check(converted.Selected(CharacterSlot.OffGcd1).id==AegisPaladinData.Id(CharacterSlot.OffGcd1),"old R replaced by new 3 default");
                    Check(stage==0||converted.HasBuildEffect("retired.loadout."+r),"old R choice archived without old effects");
                    Check(stage<3||converted.Selected(CharacterSlot.Ultimate).id==AegisExtensionData.Morning,"old V choice migrates to sanctuary");
                    Check(converted.Resource(AegisPaladinData.UltimateGauge)==0,"old save never grants free ultimate charge");
                    Check(stage<3||v!=AegisExtensionData.Excalibur||converted.HasBuildEffect("retired.ultimate."+v),"retired V choice kept as inert history");
                    Check(stage<2||converted.HasBuildEffect("retired.ninekey."+f),"old F choice archived as inert history");
                    Check(new CharacterRuntimeState(definition).Restore(converted.Snapshot),"migration is idempotent");
                }
            }
            var timed=new CharacterRuntimeState(AegisPaladinData.Build());
            Check(timed.TryUse(CharacterSlot.Job2,Ready,1,s=>true,out _),"old taunt cooldown fixture");
            var migrated=new CharacterRuntimeState(definition);Check(migrated.Restore(timed.Snapshot),"cooldown save migrates");
            var oldDash=timed.Snapshot;
            var oldMovement=oldDash.skills.First(s=>s.id==AegisPaladinData.Id(CharacterSlot.Movement));
            Check(oldMovement.charges==2,"old paladin had two charges");
            var dashMigration=new CharacterRuntimeState(definition);
            Check(dashMigration.Restore(oldDash),"old two-charge paladin save migrates to new movement");
            Check(dashMigration.Snapshot.skills.First(s=>s.id==oldMovement.id).charges==1,"new paladin is capped at one charge");
            Check(oldMovement.charges==2,"migration does not mutate source save");
            oldMovement.charges=3;
            Check(!new CharacterRuntimeState(definition).Restore(oldDash),"migration still rejects fabricated charges");
            Check(!migrated.TryUse(CharacterSlot.OffGcd2,Ready,1,s=>true,out _),"migration cannot reset taunt cooldown");
            var bad=timed.Snapshot;bad.skills[0].charges=-1;
            Check(!new CharacterRuntimeState(definition).Restore(bad),"malformed old save rejected before remap");
            Check(!fresh.TryUse(CharacterSlot.Job2,Ready,1,s=>true,out _),"server rejects old E slot");
            Check(fresh.TryUse(CharacterSlot.OffGcd2,Ready,1,s=>true,out _),"new F reaches effect handler");
            var tank=new Transform();var other=new Transform();var boss=new BossController{castTarget=tank,aggro=tank};
            boss._combatThreat[tank]=100;boss._combatThreat[other]=10;
            Check(boss.TransferCombatThreat(tank,other,.8f),"host transfer accepted");
            Check(boss.CombatThreatOf(tank)==20&&boss.CombatThreatOf(other)==90,"transfer conserves threat without tank gain multiplier");
            Check(boss.aggro==other&&boss.castTarget==tank,"future aggro changes without retargeting existing cast");
            Check(!boss.TransferCombatThreat(tank,tank,.8f),"self transfer rejected");
            Check(!boss.TransferCombatThreat(tank,other,float.NaN)&&!boss.TransferCombatThreat(tank,other,2),"invalid fractions rejected");
            other.life.IsTank=false;Check(!boss.TransferCombatThreat(tank,other,.8f),"non tank rejected");other.life.IsTank=true;
            other.alive=false;Check(!boss.TransferCombatThreat(tank,other,.8f),"dead tank rejected");other.alive=true;
            RaidCombatAuthority.Replica=true;Check(!boss.TransferCombatThreat(tank,other,.8f),"replica cannot transfer");RaidCombatAuthority.Replica=false;
            Check(!boss.TransferCombatThreat(new Transform(),other,.8f),"no own threat rejected");
            boss._combatThreat[tank]=float.NaN;Check(!boss.TransferCombatThreat(tank,other,.8f),"nonfinite stored threat rejected");
            Console.WriteLine("PASS: nine-key paladin "+checks+" checks.");return 0;
        }
        catch(Exception e){Console.WriteLine("FAIL: "+e);return 1;}
    }
}
