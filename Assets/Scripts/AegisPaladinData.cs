using System;
using System.Collections.Generic;
using System.Linq;
namespace RaidRogue
{
    // Initial tuning lives here. Definitions are shared; Debt/ICD live on each actor.
    public static class AegisPaladinData
    {
        public const string JobId="job.aegis-paladin", Holy="aegis.dawnlight";
        public static readonly string[] SwordNames={"섬광 베기","신성 교차베기","여명의 검격"};
        public static readonly string[] ShieldNames={"방패 강타","성스러운 내려찍기","단죄의 철퇴"};
        public static readonly int[] SwordPower={100,150,220}, ShieldPower={120,180,300};
        public static readonly float[] SwordStagger={10,15,20}, ShieldStagger={25,35,60};
        public const float MeleeRange=3, ComboWindow=8, SwordGcd=1.5f, ShieldGcd=2.5f;
        public const float HolyMaximum=100, FinisherHoly=15, AbsorbHoly=5, AbsorbInterval=1;
        public const float FinisherShieldRatio=.1f, FinisherShieldDuration=5, CooldownReduction=1;
        public const float BurstCooldown=8, BurstRadius=3, BurstThreatMultiplier=3, CounterCooldown=10;
        public const int BurstPower=180, CounterPower=160;
        public const float BurstStagger=20, CounterStagger=40, CounterBonusStagger=100, CounterFrontAngle=120, CounterGroggy=2, StaggerThreshold=1000;
        public const float FaithCooldown=24, FaithDuration=8, DeferredFraction=.5f, DeferredDuration=10;
        public const float CallCooldown=20, CallRange=20, CallRadius=8, CallShieldRatio=.05f, CallShieldDuration=6;
        public const float DashDistance=4, DashDuration=.2f, DashRecharge=10;
        public const int DashCharges=2;
        public static readonly string[] DispelTags={"poison","burn","slow","weakness"};
        public static readonly string[] ReducedSkills={Id(CharacterSlot.OffGcd1),Id(CharacterSlot.Job1)};
        public static string Id(CharacterSlot slot)=>JobId+".slot."+slot.ToString().ToLowerInvariant();
        public static CharacterDefinition Build()
        {
            var d=new CharacterDefinition{id=JobId,name="이지스 팔라딘",description="검과 대방패 · 근접 탱커 · 기원/Z 구현 · R/F/V 준비 중",playable=true,role=CharacterRole.Tank,classification=AttackClassification.Melee,baseHp=3000,combatRadius=.55f,moveSpeed=7.8f,visualKey="aegis.primitives"};
            var skills=new List<CharacterSkillDefinition>();var slots=new List<CharacterSlotDefinition>();
            foreach(var slot in CharacterSlots.All)
            {
                bool implemented=slot<CharacterSlot.Identity||slot==CharacterSlot.Potion;
                var s=new CharacterSkillDefinition{id=Id(slot),name=CharacterSlots.Name(slot),implemented=implemented,handlerId=implemented?"aegis."+slot:"",target=SkillTargetRule.Self,range=MeleeRange,comboWindow=ComboWindow};
                switch(slot)
                {
                    case CharacterSlot.Gcd1:s.name=SwordNames[0];s.usesGcd=true;s.gcd=SwordGcd;s.comboChain=1;s.target=SkillTargetRule.LivingEnemy;break;
                    case CharacterSlot.Gcd2:s.name=ShieldNames[0];s.usesGcd=true;s.gcd=ShieldGcd;s.comboChain=2;s.target=SkillTargetRule.LivingEnemy;break;
                    case CharacterSlot.OffGcd1:s.name="성광 방출";s.cooldown=BurstCooldown;s.target=SkillTargetRule.LivingEnemy;break;
                    case CharacterSlot.Counter:s.name="심판의 방패치기";s.cooldown=CounterCooldown;s.target=SkillTargetRule.LivingEnemy;break;
                    case CharacterSlot.Job1:s.name="불굴의 신념";s.cooldown=FaithCooldown;break;
                    case CharacterSlot.Job2:s.name="여명의 부름";s.cooldown=CallCooldown;s.range=CallRange;s.target=SkillTargetRule.LivingEnemy;break;
                    case CharacterSlot.Movement:s.name="빛의 쇄도";s.cooldown=DashRecharge;s.maximumCharges=DashCharges;s.handlerId="legacy.dash";s.usableWhileCasting=true;s.movement=new JobMovementDefinition{distance=DashDistance,duration=DashDuration,preserveCast=false};break;
                    case CharacterSlot.Potion:s.name="공통 포션";s.cooldown=30;s.legacyAction=6;s.handlerId="legacy.action.6";s.usableWhileCasting=true;break;
                }
                skills.Add(s);slots.Add(new CharacterSlotDefinition{slot=slot,skillId=s.id,initiallyUnlocked=implemented});
            }
            AegisRouteData.Attach(d,skills);d.skills=skills.ToArray();d.slots=slots.ToArray();
            d.resources=new[]{new JobResourceDefinition{id=Holy,name="여명의 성광",maximum=HolyMaximum,initial=0,integer=true}};
            // Origin identities are unlocked only by the host-confirmed departure choice.
            return d;
        }
    }
    [Serializable] public sealed class DeferredHit { public double remaining, seconds; }
    [Serializable] public sealed class AegisStateSnapshot
    {
        public float faith, absorbLock;
        public AegisRouteSnapshot route=new AegisRouteSnapshot();
        public DeferredHit[] hits=Array.Empty<DeferredHit>();
    }
    public sealed class AegisDamageState
    {
        public readonly AegisRouteState Route=new AegisRouteState();
        private readonly List<DeferredHit> _hits=new List<DeferredHit>();
        public float FaithRemaining{get;private set;}
        public float AbsorbLock{get;private set;}
        public double Pending=>_hits.Sum(h=>h.remaining);
        public void Activate()=>FaithRemaining=AegisPaladinData.FaithDuration;
        // Input is already mitigated and shield-absorbed. Repayment MUST bypass this method.
        public double Split(double confirmed)
        {
            if(double.IsNaN(confirmed)||double.IsInfinity(confirmed)||confirmed<=0)return 0;
            if(FaithRemaining<=0)return confirmed;
            double delayed=confirmed*AegisPaladinData.DeferredFraction;
            _hits.Add(new DeferredHit{remaining=delayed,seconds=AegisPaladinData.DeferredDuration});return confirmed-delayed;
        }
        public bool ShieldAbsorbed(double actual)
        {
            if(actual<=0||double.IsNaN(actual)||double.IsInfinity(actual)||AbsorbLock>0)return false;
            AbsorbLock=AegisPaladinData.AbsorbInterval;return true;
        }
        public double Advance(float seconds)
        {
            if(!CharacterDefinitionValidation.Finite(seconds)||seconds<0)return 0;
            Route.Advance(seconds);FaithRemaining=Math.Max(0,FaithRemaining-seconds);AbsorbLock=Math.Max(0,AbsorbLock-seconds);
            double due=0;
            for(int i=_hits.Count-1;i>=0;i--){var h=_hits[i];double pay=h.seconds<=seconds?h.remaining:h.remaining*seconds/h.seconds;due+=pay;h.remaining-=pay;h.seconds-=seconds;if(h.seconds<=0)_hits.RemoveAt(i);}
            return due;
        }
        public void Clear(){_hits.Clear();FaithRemaining=AbsorbLock=0;Route.Clear();}
        public AegisStateSnapshot Capture()=>new AegisStateSnapshot{faith=FaithRemaining,absorbLock=AbsorbLock,route=Route.Capture(),hits=_hits.Select(h=>new DeferredHit{remaining=h.remaining,seconds=h.seconds}).ToArray()};
        public bool Restore(AegisStateSnapshot s)
        {
            if(s==null||!AegisRouteState.Valid(s.route)||s.hits==null||!CharacterDefinitionValidation.Finite(s.faith)||s.faith<0||!CharacterDefinitionValidation.Finite(s.absorbLock)||s.absorbLock<0||s.hits.Any(h=>h==null||double.IsNaN(h.remaining)||double.IsInfinity(h.remaining)||h.remaining<0||double.IsNaN(h.seconds)||double.IsInfinity(h.seconds)||h.seconds<=0))return false;
            Clear();Route.Restore(s.route);FaithRemaining=s.faith;AbsorbLock=s.absorbLock;_hits.AddRange(s.hits.Select(h=>new DeferredHit{remaining=h.remaining,seconds=h.seconds}));return true;
        }
    }
    [Serializable] public sealed class CombatDebuff
    { public string id, tag; public bool dispellable, stun, root; public float remaining; }
    [Serializable] public sealed class CombatDebuffSnapshot { public float knockbackImmunity; public int knockbackSource; public CombatDebuff[] entries=Array.Empty<CombatDebuff>(); }
    public static class CombatDispel
    {
        public static int FirstAllowed(IList<CombatDebuff> entries,string[] tags)
        {for(int i=0;i<entries.Count;i++)if(entries[i].dispellable&&entries[i].remaining>0&&Array.IndexOf(tags,entries[i].tag)>=0)return i;return -1;}
    }
    public static class CounterRules
    {
        // Both vectors are XZ projections: enemy forward -> attacker; attacker forward -> enemy.
        public static bool Valid(bool open,bool alive,bool inRange,float enemyFacingDot,float attackerFacingDot,float fullAngle)
            =>open&&alive&&inRange&&enemyFacingDot>=Math.Cos(fullAngle*Math.PI/360)-.00001&&attackerFacingDot>=0;
    }
}
