using System;
using System.Linq;

namespace RaidRogue
{
    [Serializable] public sealed class DamagePendingSeal
    {
        public int target;
        public DamageSealTrigger trigger;
        public bool enhanced;
    }
    [Serializable] public sealed class DamageCombatSnapshot
    {
        public int version=3,route,meteorTarget;
        public float capturedAt;
        public string job="",core="";
        public bool fourthReady;
        public DamageResourceSnapshot resources;
        public DamageWeaknessSnapshot weakness;
        public DamageSealSnapshot seal;
        public DamageGrenadeSnapshot grenade;
        public DamageHandsSnapshot hands;
        public DamageSnipeSnapshot snipe;
        public DamageMeteorSnapshot meteor;
        public DamageBurstSnapshot burst;
        public DamageSeasonSnapshot season;
        public DamageInstantSnapshot instant;
        public DamageMemorySnapshot memory;
        public DamageProjectionSnapshot projection;
        public DamageArrowSnapshot arrows;
        public DamageReturnSnapshot anchor;
        public DamageSeedSnapshot seed;
        public DamageTempestPreparationSnapshot preparation;
        public DamageSpearSnapshot spears;
        public DamageGravitySnapshot gravity;
        public DamagePrismSnapshot prism;
        public CharacterKitSnapshot effects;
        public DamagePendingSeal[] pendingSeals=Array.Empty<DamagePendingSeal>();
    }
    // Candidate states share the already-restored framework balance. Restore only
    // copies provenance: it never reserves, spends, refunds, ticks or deals damage.
    public sealed class DamageCombatState
    {
        public NineKeyDamageResources resources;
        public DamageWeaknessState weakness=new DamageWeaknessState();
        public DamageSealState seal=new DamageSealState();
        public DamageGrenadeState grenade=new DamageGrenadeState();
        public DamageHandsState hands=new DamageHandsState();
        public DamageSnipeState snipe;
        public DamageMeteorState meteor;
        public DamageBurstState burst=new DamageBurstState();
        public DamageSeasonState season=new DamageSeasonState();
        public DamageInstantState instant=new DamageInstantState();
        public DamageMemoryState memory=new DamageMemoryState();
        public DamageProjectionState projection=new DamageProjectionState();
        public DamageArrowState arrows=new DamageArrowState();
        public DamageReturnState anchor=new DamageReturnState();
        public DamageSeedState seed=new DamageSeedState();
        public DamageTempestPreparationState preparation=new DamageTempestPreparationState();
        public DamageSpearState spears=new DamageSpearState();
        public DamageGravityState gravity=new DamageGravityState();
        public DamagePrismState prism=new DamagePrismState();
        public CharacterEffectState effects=new CharacterEffectState();
        public static bool TryRestore(CharacterRuntimeState runtime,RosterJob job,DamageCombatSnapshot v,out DamageCombatState result,float restoreTime=-1)
        {
            result=null;
            if(runtime==null||!NineKeyDamageData.Supports(job)||v==null||v.version!=3||v.job!=job.Id||runtime.Definition.id!=job.Id||
                !DamageTime.Nonnegative(v.capturedAt)||!CharacterDefinitionValidation.Finite(restoreTime)||restoreTime<0&&restoreTime!=-1)return false;
            int route=Array.FindIndex(runtime.Definition.routes,r=>r.id==runtime.RouteId)+1;
            if(v.route!=route||v.core!=runtime.Snapshot.coreTrait||v.pendingSeals==null||v.pendingSeals.Length>1||
                v.pendingSeals.Any(s=>s==null||s.target==0||(s.trigger!=DamageSealTrigger.CastEnd&&s.trigger!=DamageSealTrigger.PatternMoveEnd)))return false;
            bool Core(int r,int n)=>v.core==NineKeyDamageData.Core(job,r,n);
            bool Is(int j,int r)=>job.index==j&&route==r;
            // The generic reservation state permits several prototype costs;
            // a live combat snapshot must match the currently selected skill.
            // Otherwise a later cancel could refund more than its actual cost.
            if(v.resources==null||v.resources.reservation=="meteor"&&v.resources.reserved!=60||
                v.resources.reservation=="snipe"&&v.resources.reserved!=(Core(3,1)?5:3))return false;
            var c=new DamageCombatState();c.resources=new NineKeyDamageResources(runtime,job);
            c.snipe=new DamageSnipeState(c.resources);c.meteor=new DamageMeteorState(c.resources);
            if(!c.resources.Restore(v.resources)||!c.weakness.Restore(v.weakness,Is(6,1)&&Core(1,0)?1:Is(6,1)&&Core(1,1)?3:2)||
                !c.seal.Restore(v.seal)||!c.grenade.Restore(v.grenade)||!c.hands.Restore(v.hands)||
                !c.snipe.Restore(v.snipe)||!c.meteor.Restore(v.meteor)||!c.burst.Restore(v.burst)||
                !c.season.Restore(v.season)||!c.instant.Restore(v.instant)||!c.memory.Restore(v.memory,Is(11,1)&&Core(1,0)?1:2)||
                !c.projection.Restore(v.projection)||!c.arrows.Restore(v.arrows)||!c.anchor.Restore(v.anchor)||
                !c.seed.Restore(v.seed)||!c.preparation.Restore(v.preparation)||!c.spears.Restore(v.spears)||
                !c.gravity.Restore(v.gravity)||!c.prism.Restore(v.prism,Is(11,3)&&Core(3,0)?1:2)||!c.effects.Restore(v.effects))return false;
            if(v.weakness.targets.Length>0&&!Is(6,1)||(c.seal.Active||v.pendingSeals.Length>0)&&!Is(6,3)||
                c.grenade.Active&&!Is(8,2)||c.snipe.Active&&!Is(8,3)||c.meteor.Active&&!Is(10,1)||
                c.burst.Active&&!Is(9,2)||(c.instant.Active||v.instant.cooldown>0)&&!Is(10,2)||
                (v.memory.records.Length>0||v.memory.locked)&&!Is(11,1)||
                (c.projection.Active||v.projection.usedInCombo||v.projection.empoweredExecution)&&!Is(11,2)||
                (v.arrows.pending.Length>0||v.arrows.convergence>0)&&!Is(9,1)||
                v.spears.spears.Length>0&&!Is(7,3)||c.gravity.Active&&!Is(10,3)||v.prism.mirrors.Length>0&&!Is(11,3))return false;
            string Selected(CharacterSlot slot)=>runtime.Selected(slot)?.id;
            if((c.anchor.Active||v.anchor.cooldown>0)&&(job.index!=11||Selected(CharacterSlot.OffGcd2)!=NineKeyDamageData.Id(job,"f2"))||
                (c.seed.Active||v.seed.cooldown>0)&&(job.index!=7||Selected(CharacterSlot.OffGcd2)!=NineKeyDamageData.Id(job,"f2"))||
                (v.preparation.combo>0||v.preparation.cloud>0)&&job.index!=7||
                v.fourthReady&&(job.index!=7||!Core(2,0)||runtime.Snapshot.combo2!=1)||
                c.meteor.Active!=(v.meteorTarget!=0)||v.resources.projectedSecond!=v.projection.usedInCombo)return false;
            if(v.hands.capacity!=(Is(8,1)&&Core(1,1)?5:3)||
                !Is(8,1)&&(v.hands.left!=0||v.hands.right!=0||v.hands.reloading!=0||v.hands.cooldown>0))return false;
            if(c.meteor.Active&&(v.meteor.segments!=(Core(1,0)?5:3)||v.meteor.segmentTime!=(Core(1,0)?.6f:1)||v.meteor.remaining>16))return false;
            if(c.burst.Active&&(v.burst.remaining>(Core(2,1)?20:16)||v.burst.inputDelay>.25f))return false;
            if(Is(9,3))
            {
                float period=Core(3,0)?6:8;
                float maximumRemaining=period+(v.season.delayed?(Core(3,1)?6:Core(3,0)?2:4):0);
                // Period is fixed by the selected core. Remaining time may be
                // anywhere within this visit, including a paid one-time delay.
                if(v.season.period!=period||v.season.remaining>maximumRemaining+.001f)return false;
            }
            if(c.gravity.Active&&v.gravity.remaining>(Core(3,0)?16:12))return false;
            if(Is(10,2)&&(v.instant.rights>(Core(2,0)?2:Core(2,1)?5:3)||v.instant.remaining>20||
                v.instant.cooldown>(Core(2,0)?18:Core(2,1)?40:30)||v.instant.toggleDelay>.25f))return false;
            if(c.snipe.Active&&(v.snipe.shots>(Core(3,0)?3:1)||v.snipe.keep!=12||
                v.snipe.aiming>(Core(3,1)?2:1)))return false;
            if(v.memory.records.Any(r=>r.remaining>12)||
                c.projection.Active&&v.projection.remaining>(Core(2,0)?16:10))return false;
            float installationBonus=runtime.HasAuxiliary("tempest.aux.duration")?5:0;
            if(v.spears.spears.Any(s=>s.tickPower!=40||s.storageLimit!=400||s.compressed!=Core(3,1)||
                s.remaining>(Core(3,0)?14:Core(3,1)?26:20)+installationBonus))return false;
            bool floatingSeed=v.core==NineKeyDamageData.Id(job,"core30.f2"),compressedSeed=v.core==NineKeyDamageData.Id(job,"core31.f2");
            if(c.seed.Active&&(v.seed.storageLimit!=240||v.seed.compressed!=compressedSeed||v.seed.cooldown>16||
                v.seed.remaining>(floatingSeed?4:compressedSeed?8:6)+installationBonus))return false;
            var replays=v.effects.effects.Where(e=>e.kind=="memory.replay").ToArray();
            var ultimates=v.effects.effects.Where(e=>e.kind=="ultimate.sequence").ToArray();
            // A snapshot holds the unconsumed suffix of one paid ultimate.
            // Combo-only gauge income cannot prepare another V during its
            // sub-second sequence. Do not restore duplicate final strikes.
            if(ultimates.Length>(job.index==6?3:2)||ultimates.Select(e=>e.target).Distinct().Count()>1||
                job.index==6&&(ultimates.Count(e=>e.value==120)>2||ultimates.Count(e=>e.value==440)>1)||
                ultimates.Any(e=>e.end-v.capturedAt>(job.index==6?(e.value==120?.301f:.451f):.401f)))return false;
            if(replays.Length>0&&(!Is(11,1)||!Core(1,1)||replays.Length>2||replays.Count(e=>e.aux==1)>1||
                replays.Any(e=>e.end-v.capturedAt>.601f)))return false;
            if(v.effects.effects.Any(e=>!ValidEffect(e,job.index,route)||!ValidScheduledBudget(e,job.index,v.capturedAt)||Math.Abs((double)e.end-v.capturedAt)>3600||Math.Abs((double)e.next-v.capturedAt)>3600))return false;
            if(restoreTime>=0)
            {
                // Shift deadlines, not durations or tick counts. Preserve already
                // due ticks even if the new clock starts at zero (negative next).
                foreach(var e in c.effects.Data.effects)
                {
                    e.end=(float)((double)restoreTime+e.end-v.capturedAt);
                    e.next=(float)((double)restoreTime+e.next-v.capturedAt);
                    if(!CharacterDefinitionValidation.Finite(e.end)||!CharacterDefinitionValidation.Finite(e.next))return false;
                }
            }
            result=c;return true;
        }
        private static bool ValidScheduledBudget(CharacterTimedEffect e,int job,float capturedAt)
        {
            if(e.kind!="damage.dot"&&e.kind!="damage.followup")return true;
            bool periodic=e.kind=="damage.dot";
            int budget=periodic?4:1;
            float interval=periodic?1:job==8?1.5f:1;
            float power=periodic?30:job==8?210:e.skill=="g22"?100:110;
            if(e.value!=power||e.interval!=interval||e.ticks>budget)return false;
            // The consumed counter alone does not limit future callbacks. Bound
            // the remaining schedule too, using TakeTicks' rounding convention.
            // Do not require next >= capturedAt: overdue attacks remain valid
            // across suspension and clock rebasing, but never gain extra ticks.
            double remaining=e.next>e.end?0:Math.Floor(((double)e.end-e.next)/interval+.0001)+1;
            return remaining+e.ticks<=budget&&
                (double)e.next-capturedAt<=interval+.001&&
                (double)e.end-capturedAt<=interval*budget+.001;
        }
        private static bool ValidEffect(CharacterTimedEffect e,int job,int route)
        {
            if(e.value<0||e.interval<=0)return false;
            if(e.kind=="ultimate.sequence")return e.skill=="v"&&e.target!=0&&e.interval==1&&e.next==e.end&&e.ticks==0&&
                (job==6&&(e.value==120||e.value==440)||job==11&&e.value==300);
            if(e.kind=="memory.replay")return job==11&&route==1&&e.target!=0&&e.interval==1&&e.next==e.end&&e.ticks==0&&
                (e.aux==0&&e.value==220||e.aux==1&&e.value==280)&&
                (e.skill=="g1"||e.skill=="g20"||e.skill=="g21"||e.skill=="g22");
            if(e.kind=="residual.cloud")return job==7&&e.radius==4&&e.interval==1&&e.ticks<=3&&e.next<=e.end+1&&
                (e.skill=="r1"&&route==1||e.skill=="a0"||e.skill=="a2");
            if(e.kind=="damage.dot")return e.target!=0&&e.interval==1&&e.ticks<=4&&
                (job==6&&e.skill=="g20"||job==7&&e.skill=="g21"||(job==8||job==10)&&e.skill=="g22");
            if(e.kind=="damage.followup")return e.target!=0&&e.ticks<=1&&
                (job==9&&(e.skill=="g22"||e.skill=="a2")||job==8&&e.skill=="a2");
            if(e.target==0)return false;
            if(e.kind=="speed"||e.kind=="reduction"||e.kind=="healingReceived")return e.value<=1;
            if(e.kind=="range")return job==7&&e.skill=="tempest.reach";
            if(e.kind=="reach")return job==6&&route==2&&e.skill=="dance.reach";
            if(e.kind=="assault")return job==7&&route==1&&e.skill=="assault";
            if(e.kind=="acceleration"||e.kind=="tempest.response")return job==7&&route==2;
            if(e.kind=="fracture")return job==8&&route==2;
            if(e.kind=="hand.ready")return job==8&&route==1&&(e.skill=="hand.1"||e.skill=="hand.2");
            if(e.kind=="duet")return job==6||job==7;
            return e.kind=="cooperation"||e.kind=="gate"&&e.value==1;
        }
    }
}
