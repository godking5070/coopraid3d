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
        // Prototype values for the new baseline; route-specific stance overrides follow separately.
        public const float NineKeyFinisherHoly=20, NineKeyGcd=2, NineKeyFaithDuration=6, NineKeyFaithReduction=.3f;
        public const float FinisherShieldRatio=.1f, FinisherShieldDuration=5, CooldownReduction=1;
        public const float BurstCooldown=8, BurstRadius=3, BurstThreatMultiplier=3, CounterCooldown=10;
        public const int BurstPower=180, CounterPower=160;
        public const float BurstStagger=20, CounterStagger=40, CounterBonusStagger=100, CounterFrontAngle=120, CounterGroggy=2, StaggerThreshold=1000;
        public const float FaithCooldown=24, FaithDuration=8, DeferredFraction=.5f, DeferredDuration=10;
        public const float CallCooldown=20, CallRange=20, CallRadius=8, CallShieldRatio=.05f, CallShieldDuration=6;
        public const float DashDistance=4, DashDuration=.2f, DashRecharge=10;
        // Ratings preserve the previous tank's 25% base mitigation: 100 / (300 + 100).
        public const float BaseDefense=100, BaseTenacity=100, DefenseCurve=300, TenacityCurve=300;
        public const float MovePenalty=.9f, DashPenalty=.7f, CounterRushDistance=2, CounterRushDuration=.12f;
        public const int DashCharges=2;
        public static readonly string[] DispelTags={"poison","burn","slow","weakness"};
        public static readonly string[] ReducedSkills={Id(CharacterSlot.OffGcd1),Id(CharacterSlot.Job1)};
        public static string Id(CharacterSlot slot)=>JobId+".slot."+slot.ToString().ToLowerInvariant();
        public static CharacterDefinition Build()
        {
            var d=new CharacterDefinition{id=JobId,name="이지스 팔라딘",description="백금 판금 · 검과 대방패 · 이동 90% / 이동기 거리 70% · 기원/Z · Stage 1 R / Stage 2 F / Stage 3 V 확정 선택",playable=true,role=CharacterRole.Tank,classification=AttackClassification.Melee,baseHp=3000,combatRadius=.55f,moveSpeed=7.8f,moveSpeedMultiplier=MovePenalty,physicalDefense=BaseDefense,magicDefense=BaseDefense,tenacity=BaseTenacity,visualKey="aegis.primitives"};
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
                    case CharacterSlot.Counter:s.name="심판의 방패치기";s.isCounter=true;s.cooldown=CounterCooldown;s.target=SkillTargetRule.LivingEnemy;s.castTime=CounterRushDuration;s.cancelOnMovement=false;s.movement=new JobMovementDefinition{distance=CounterRushDistance,duration=CounterRushDuration};break;
                    case CharacterSlot.Job1:s.name="불굴의 신념";s.cooldown=FaithCooldown;break;
                    case CharacterSlot.Job2:s.name="여명의 부름";s.cooldown=CallCooldown;s.range=CallRange;s.target=SkillTargetRule.LivingEnemy;break;
                    case CharacterSlot.Movement:s.name="빛의 쇄도";s.cooldown=DashRecharge;s.maximumCharges=DashCharges;s.handlerId="legacy.dash";s.usableWhileCasting=true;s.movement=new JobMovementDefinition{distance=DashDistance,duration=DashDuration,useCommonDistance=true,commonDistanceMultiplier=DashPenalty,preserveCast=false};break;
                    case CharacterSlot.Potion:s.name="공통 포션";s.cooldown=30;s.legacyAction=6;s.handlerId="legacy.action.6";s.usableWhileCasting=true;break;
                }
                skills.Add(s);slots.Add(new CharacterSlotDefinition{slot=slot,skillId=s.id,initiallyUnlocked=implemented});
            }
            AegisRouteData.Attach(d,skills);AegisExtensionData.Attach(d,skills);d.skills=skills.ToArray();d.slots=slots.ToArray();
            d.resources=new[]{new JobResourceDefinition{id=Holy,name="여명의 성광",maximum=HolyMaximum,initial=0,integer=true}};
            // Origin identities are unlocked only by the host-confirmed departure choice.
            return d;
        }
        // Active input migration. Route effects remain the existing implementation
        // and are labelled as such until their new state machines replace them.
        public static CharacterDefinition BuildNineKey(bool includeLoadout=true,bool includeCoreTraits=true,bool includeUltimate=true)
        {
            var d=Build();d.controlLayout=CharacterControlLayout.NineKey;
            d.description="9키 · 기본 1/2 완주 성광 20 · 태양은 태세별 수급/소비 · Q 피해 감소 · 3루트 R 기본 전환 적용 · V 기존 효과";
            foreach(var slot in new[]{CharacterSlot.Gcd1,CharacterSlot.Gcd2})d.Skill(Id(slot)).gcd=NineKeyGcd;
            d.Skill(Id(CharacterSlot.Job1)).description=$"개인 피해 {NineKeyFaithReduction*100:0}% 감소 / {NineKeyFaithDuration:0.#}초 · 성광 무소비 (프로토타입 수치)";
            d.slots=d.slots.Where(s=>CharacterSlots.Enabled(d,s.slot)).ToArray();
            var taunt=d.slots.First(s=>s.slot==CharacterSlot.OffGcd2);
            taunt.skillId=Id(CharacterSlot.Job2);taunt.initiallyUnlocked=true;
            var counter=d.Skill(Id(CharacterSlot.Counter));counter.castTime=0;counter.movement=null;
            var call=d.Skill(taunt.skillId);call.target=SkillTargetRule.None;
            call.description="적: 도발 / 다른 아군 탱커: 최근 공격·도발 보스의 적개심 인계. 정화·공격 버프 없음.";
            d.unlocks=d.unlocks.Where(u=>u.slot!=CharacterSlot.OffGcd2).ToArray();
            foreach(var unlock in d.unlocks)if(unlock.slot==CharacterSlot.Active)unlock.slot=CharacterSlot.OffGcd1;
            foreach(var route in d.routes)d.Skill(route.identitySkillId).description="[기존 기원 효과 · 신규 루트 효과 전환 예정]\n"+d.Skill(route.identitySkillId).description;
            var sun=d.Skill(AegisRouteData.Identity(AegisRouteData.Sun));sun.name="여명의 맹세";sun.costs=Array.Empty<ResourceCost>();sun.cooldown=.25f;
            sun.description="검/방패 태세 전환 · 성광 무소비\n1 완주: 검 30 / 방패 20+자기 보호\n2 시작 태세 고정, 완주 시 성광 40으로 협공/선택 보호. 부족하면 기본 효과만 적용. (효과량은 초기 튜닝값)";
            if(includeLoadout)
            {
                var bulwark=d.Skill(AegisRouteData.Identity(AegisRouteData.Bulwark));
                bulwark.name="여명의 성채";bulwark.costs=new[]{new ResourceCost{resourceId=Holy,amount=AegisGlyphState.Cost}};bulwark.cooldown=.25f;
                bulwark.description="커서 지상 위치에 수호 문양 · 성광 40 / 최대 2개\n반경 2m / 수명 20초 / 동료별 1회 최대 HP 10% 보호막 6초\n20m 지정 · 최대 수에서는 설치 차단 (프로토타입)";
                var mirror=d.Skill(AegisRouteData.Identity(AegisRouteData.Mirror));mirror.name="거울 수호";
                mirror.description="성광 30 · 자신/유효 지원 동료 수호 2초\n첫 0.6초 정밀 방어에 응보 1개 (R당 1개/보관 2개)\n1 완주가 오래된 응보 1개 소비 · 피해 감소 30% / 반격 180 / 재사용 6초 (초기값)";
            }
            if(includeLoadout)AttachLoadout(d,includeCoreTraits);
            if(includeLoadout&&includeCoreTraits&&includeUltimate)
            {
                d.auxiliaryTraits=new[]{
                    new CharacterAuxiliaryDefinition{id=Steadfast,name="굳건한 신념",description="Q 피해 감소 6→8초 / 재사용 24→30초 (초기안)"},
                    new CharacterAuxiliaryDefinition{id=FarCall,name="멀리 닿는 부름",description="F 도발·인계 사거리 +5m / 인계 비율·재사용 유지 (초기안)"},
                    new CharacterAuxiliaryDefinition{id=LastingSanctuary,name="남은 여명",description="V 보호막 지속 8→10초 / 보호량·게이지 수급 유지 (초기안)"}};
                d.resources=d.resources.Concat(new[]{new JobResourceDefinition{id=UltimateGauge,name="필살기 게이지",maximum=100,initial=0,integer=true}}).ToArray();
                foreach(string id in new[]{AegisExtensionData.Morning,AegisExtensionData.Excalibur})
                {
                    var ultimate=d.Skill(id);ultimate.name="첫 번째 아침의 성역";ultimate.target=SkillTargetRule.None;ultimate.range=20;ultimate.cooldown=.25f;
                    ultimate.costs=new[]{new ResourceCost{resourceId=UltimateGauge,amount=100}};
                    ultimate.description="필살기 게이지100 소비 · 성광과 별개\n유효 공격 대상 위력1200 / 주변30m 파티 보호막 최대HP20%·8초\n1·2 완주 적중마다 게이지10 · 적이 없어도 보호 (초기값)";
                }
                d.unlocks.First(r=>r.slot==CharacterSlot.Ultimate).choices=new[]{AegisExtensionData.Morning};
                d.description=d.description.Replace("V 기존 효과","V 전용 게이지·파티 보호막");
            }
            var dashProfile=CharacterDashProfiles.ForJob(0);
            var dashSkill=d.Skill(Id(CharacterSlot.Movement));
            dashSkill.name=CharacterDashProfiles.Name(0);dashSkill.description=CharacterDashProfiles.Description(0);
            dashSkill.cooldown=dashProfile.cooldown;dashSkill.maximumCharges=dashProfile.maximumCharges;
            dashSkill.movement=new JobMovementDefinition{distance=dashProfile.distance,duration=dashProfile.duration,
                preserveCast=false,useCommonDistance=false,commonDistanceMultiplier=1};
            d.migrateSnapshot=includeLoadout?(includeCoreTraits?(includeUltimate?(Func<CharacterStateSnapshot,CharacterStateSnapshot>)MigrateUltimateSnapshot:MigrateCoreSnapshot):MigrateLoadoutSnapshot):MigrateNineKeySnapshot;return d;
        }
        public const string UltimateGauge="aegis.ultimate";
        public const string Steadfast="tank.aux.steadfast",FarCall="tank.aux.far-call",LastingSanctuary="tank.aux.lasting-sanctuary";
        public static float AuxiliaryCooldownScale(string skillId,bool steadfast)=>steadfast&&skillId==Id(CharacterSlot.Job1)?1.25f:1;
        public static float AuxiliaryThreatRange(bool farCall)=>CallRange+(farCall?5:0);
        public static float AuxiliaryUltimateDuration(bool lasting)=>UltimateDuration+(lasting?2:0);
        public const float UltimateGain=10,UltimatePower=1200,UltimateShieldRatio=.2f,UltimateDuration=8,UltimateRange=30;
        private static CharacterStateSnapshot MigrateUltimateSnapshot(CharacterStateSnapshot source)
        {
            var next=MigrateCoreSnapshot(source);if(next?.resources==null)return null;
            if(next.resources.Any(r=>r?.id==UltimateGauge))return next;
            if(!new CharacterRuntimeState(BuildNineKey(true,true,false)).Restore(next))return null;
            next=next.Copy();next.resources=next.resources.Concat(new[]{new JobResourceValue{id=UltimateGauge,value=0}}).ToArray();
            var selected=next.selections.First(s=>s.slot==CharacterSlot.Ultimate);
            if(selected.skillId==AegisExtensionData.Excalibur)
            {
                next.effects=next.effects.Concat(new[]{new JobBuildEffect{id="retired.ultimate."+selected.skillId,stacks=1}}).ToArray();
                var old=next.skills.First(s=>s.id==selected.skillId);var current=next.skills.First(s=>s.id==AegisExtensionData.Morning);
                if(old.recharge>current.recharge){current.charges=0;current.recharge=current.rechargeTotal=old.recharge;}
                selected.skillId=AegisExtensionData.Morning;
                next.unlocked=next.unlocked.Where(id=>id!=AegisExtensionData.Excalibur).Union(new[]{AegisExtensionData.Morning}).ToArray();
                next.automaticChoices=next.automaticChoices.Select(id=>id==AegisExtensionData.Excalibur?AegisExtensionData.Morning:id).Distinct().ToArray();
            }
            return next;
        }
        public const string Wave="aegis.combo.wave", Judgment="aegis.combo.judgment", Hand="aegis.support.hand", Slam="aegis.support.slam";
        public const string Continuous="aegis.core.continuous", Guardian="aegis.core.guardian";
        public const string Citadel="aegis.core.citadel", Patrol="aegis.core.patrol", Flash="aegis.core.flash", Reflection="aegis.core.reflection";
        public const float ContinuousCost=20, ContinuousScale=.5f, FollowupDelay=3;
        public static string SecondName(string id,int step)=>
            (id==Wave?new[]{"빛의 참격","교차 검기","여명 파동"}:id==Judgment?new[]{"중단 베기","방패 파쇄","심판 내려치기"}:ShieldNames)[Math.Max(0,Math.Min(2,step-1))];
        private static void AttachLoadout(CharacterDefinition d,bool includeCoreTraits)
        {
            var skills=d.skills.ToList();
            var shield=d.Skill(Id(CharacterSlot.Gcd2));shield.name="방패 연격";shield.description="근거리 콤보 · 완주 자기 보호막 · 자원은 루트 규칙 적용";
            foreach(var id in new[]{Wave,Judgment})skills.Add(new CharacterSkillDefinition{id=id,name=id==Wave?"성검 파동":"심판 연격",description=id==Wave?"중거리 12m · 피해 80% · 루트 완주 효과 유지 (초기값)":"근거리 · 피해 80% / 무력화 2배 · 루트 완주 효과 유지 (초기값)",implemented=true,handlerId="aegis.Gcd2",target=SkillTargetRule.LivingEnemy,range=id==Wave?12:MeleeRange,usesGcd=true,gcd=NineKeyGcd,comboChain=2,comboWindow=ComboWindow});
            var spear=d.Skill(Id(CharacterSlot.OffGcd1));spear.name="빛창 투척";spear.range=20;spear.cooldown=14;spear.description="중거리 즉발 · 위력 180 / 적중 성광 10 (초기값)";
            skills.Add(new CharacterSkillDefinition{id=Hand,name="수호자의 손길",description="자신/선택 동료 보호막 · 순간이동/피해 전가 없음 · 20m / 15초 (초기값)",implemented=true,handlerId="aegis.OffGcd1",target=SkillTargetRule.LivingAlly,range=20,cooldown=15});
            skills.Add(new CharacterSkillDefinition{id=Slam,name="심판 강타",description="근거리 즉발 · 위력 140 / 무력화 100 · 카운터 아님 (초기값)",implemented=true,handlerId="aegis.OffGcd1",target=SkillTargetRule.LivingEnemy,range=MeleeRange,cooldown=14});
            skills.Add(new CharacterSkillDefinition{id=Continuous,name="연속 판결",description="검 2 강화 비용 40→20, 강화 피해·협공 예산 50% (초기값)",implemented=true,handlerId="aegis.passive",target=SkillTargetRule.Self});
            skills.Add(new CharacterSkillDefinition{id=Guardian,name="수호 판결",description="방패 2 강화 보호량을 즉시 50%와 3초 뒤 50%로 분할 · 총량 증가 없음 (초기값)",implemented=true,handlerId="aegis.passive",target=SkillTargetRule.Self});
            d.skills=skills.ToArray();
            d.unlocks=d.unlocks.Where(r=>r.id!=AegisExtensionData.Rule(CharacterSlot.Active)).ToArray();
            d.loadouts=new[]{
                new CharacterLoadoutDefinition{id="loadout.combo2",name="2 교체 콤보",slot=CharacterSlot.Gcd2,choices=new[]{Id(CharacterSlot.Gcd2),Wave,Judgment}},
                new CharacterLoadoutDefinition{id="loadout.support3",name="3 교체 기술",slot=CharacterSlot.OffGcd1,choices=new[]{Id(CharacterSlot.OffGcd1),Hand,Slam}},
                new CharacterLoadoutDefinition{id="loadout.core",name="태양 핵심 특성",routeId=AegisRouteData.Sun,choices=new[]{Continuous,Guardian}}};
            if(includeCoreTraits)
            {
                var ids=new[]{Citadel,Patrol,Flash,Reflection};var names=new[]{"대성채","순회 수호자","일섬 반격","수호 반사경"};
                var descriptions=new[]{"문양 최대 1개 · 반경 3m / 보호량 최대 HP 15% (초기값)","R 짧게 설치 / 0.45초 길게: 가장 오래된 미사용 문양 회수 · 성광20 반환 (초기값)","응보 최대 1개 · 획득 후 4초 내 반격 보너스270, 이후180 (초기값)","반격 보너스180→90 · 해당 응보의 수호 대상에 보호막90/6초 · 무효 대상 재지정 없음 (초기값)"};
                for(int i=0;i<ids.Length;i++)skills.Add(new CharacterSkillDefinition{id=ids[i],name=names[i],description=descriptions[i],implemented=true,handlerId="aegis.passive",target=SkillTargetRule.Self});
                d.skills=skills.ToArray();d.loadouts=d.loadouts.Concat(new[]{
                    new CharacterLoadoutDefinition{id="loadout.bulwark.core",name="성벽 핵심 특성",routeId=AegisRouteData.Bulwark,choices=new[]{Citadel,Patrol}},
                    new CharacterLoadoutDefinition{id="loadout.mirror.core",name="거울 핵심 특성",routeId=AegisRouteData.Mirror,choices=new[]{Flash,Reflection}}}).ToArray();
            }
        }
        private static CharacterStateSnapshot MigrateCoreSnapshot(CharacterStateSnapshot source)
        {
            var next=MigrateLoadoutSnapshot(source);if(next?.skills==null)return null;
            if(!next.skills.Any(s=>s?.id==Citadel))
            {
                if(!new CharacterRuntimeState(BuildNineKey(true,false)).Restore(next))return null;
                next=next.Copy();var definition=BuildNineKey();
                next.skills=next.skills.Concat(definition.skills.Where(s=>!next.skills.Any(old=>old.id==s.id)).Select(s=>new JobSkillState{id=s.id,charges=s.maximumCharges})).ToArray();
            }
            if(string.IsNullOrEmpty(next.coreTrait)&&(next.routeId==AegisRouteData.Bulwark||next.routeId==AegisRouteData.Mirror))
            {next=next.Copy();next.coreTrait=next.routeId==AegisRouteData.Bulwark?Citadel:Flash;}
            return next;
        }
        private static CharacterStateSnapshot MigrateLoadoutSnapshot(CharacterStateSnapshot source)
        {
            var next=MigrateNineKeySnapshot(source);if(next==null)return null;
            if(next.skills==null||next.chosenRules==null)return null;
            bool oldLoadout=!next.skills.Any(s=>s?.id==Wave);
            if(oldLoadout)
            {
                if(!new CharacterRuntimeState(BuildNineKey(false)).Restore(next))return null;
                next=next.Copy();var definition=BuildNineKey();
                next.skills=next.skills.Concat(definition.skills.Where(s=>!next.skills.Any(old=>old.id==s.id)).Select(s=>new JobSkillState{id=s.id,charges=s.maximumCharges})).ToArray();
                if(next.routeId==AegisRouteData.Sun)next.coreTrait=Continuous;
            }
            string oldRule=AegisExtensionData.Rule(CharacterSlot.Active);
            if(!oldLoadout&&next.chosenRules.Contains(oldRule))return null;
            if(oldLoadout&&(next.chosenRules.Contains(oldRule)||new[]{AegisExtensionData.Spear,AegisExtensionData.Ground,AegisExtensionData.Wings}.Contains(next.selections.First(s=>s.slot==CharacterSlot.OffGcd1).skillId)))
            {
                next=next.Copy();var selected=next.selections.First(s=>s.slot==CharacterSlot.OffGcd1);
                next.effects=next.effects.Concat(new[]{new JobBuildEffect{id="retired.loadout."+selected.skillId,stacks=1}}).ToArray();
                var oldTiming=next.skills.First(s=>s.id==selected.skillId);var newTiming=next.skills.First(s=>s.id==Id(CharacterSlot.OffGcd1));
                if(oldTiming.recharge>newTiming.recharge){newTiming.charges=0;newTiming.recharge=newTiming.rechargeTotal=oldTiming.recharge;}
                selected.skillId=Id(CharacterSlot.OffGcd1);next.unlocked=next.unlocked.Union(new[]{selected.skillId}).ToArray();
                var keep=Enumerable.Range(0,next.chosenRules.Length).Where(i=>next.chosenRules[i]!=oldRule).ToArray();
                next.chosenRules=keep.Select(i=>next.chosenRules[i]).ToArray();next.chosenRewardKeys=keep.Select(i=>next.chosenRewardKeys[i]).ToArray();
            }
            return next;
        }
        public static CharacterStateSnapshot MigrateNineKeySnapshot(CharacterStateSnapshot source)
        {
            if(source?.jobId!=JobId||source.selections==null)return source;
            if(!source.selections.Any(s=>s?.slot==CharacterSlot.Job2))return MigrateDashCapacity(source);
            // Validate the entire old save before remapping it. A malformed packet
            // must not gain validity just because obsolete fields are removed.
            if(!new CharacterRuntimeState(Build()).Restore(source))return null;
            var next=source.Copy();
            var oldActive=next.selections.FirstOrDefault(s=>s.slot==CharacterSlot.Active);
            string oldTacticalId=next.selections.FirstOrDefault(s=>s.slot==CharacterSlot.OffGcd2)?.skillId;
            var list=next.selections.Where(s=>s.slot!=CharacterSlot.Job2&&s.slot!=CharacterSlot.Active).ToList();
            if(next.chosenRules.Contains(AegisExtensionData.Rule(CharacterSlot.Active))&&oldActive!=null)
                list.First(s=>s.slot==CharacterSlot.OffGcd1).skillId=oldActive.skillId;
            list.First(s=>s.slot==CharacterSlot.OffGcd2).skillId=Id(CharacterSlot.Job2);
            next.selections=list.ToArray();
            string retiredRule=AegisExtensionData.Rule(CharacterSlot.OffGcd2);
            var indices=Enumerable.Range(0,next.chosenRules.Length).Where(i=>next.chosenRules[i]!=retiredRule).ToArray();
            if(next.chosenRules.Contains(retiredRule)&&oldTacticalId!=null)
                next.effects=next.effects.Concat(new[]{new JobBuildEffect{id="retired.ninekey."+oldTacticalId,stacks=1}}).ToArray();
            next.chosenRules=indices.Select(i=>next.chosenRules[i]).ToArray();
            next.chosenRewardKeys=indices.Select(i=>next.chosenRewardKeys[i]).ToArray();
            // A cast saved under the old rush cannot finish after the input migration.
            next.castSkillId="";next.castTarget=0;next.castRemaining=next.castTotal=0;next.castAllowsMovement=false;
            return MigrateDashCapacity(next);
        }
        private static CharacterStateSnapshot MigrateDashCapacity(CharacterStateSnapshot source)
        {
            // Only the former two-charge paladin profile lost capacity. Do not
            // turn arbitrary malformed charge counts into valid save data.
            var movement=source.skills?.FirstOrDefault(s=>s?.id==Id(CharacterSlot.Movement));
            if(movement==null||movement.charges!=DashCharges)return source;
            var next=source.Copy();
            var migrated=next.skills.First(s=>s.id==movement.id);
            migrated.charges=CharacterDashProfiles.ForJob(0).maximumCharges;
            migrated.recharge=migrated.rechargeTotal=0;
            return next;
        }
    }
    [Serializable] public sealed class DeferredHit { public double remaining, seconds; }
    [Serializable] public sealed class AegisStateSnapshot
    {
        public int lastThreatBossId;
        public float faith, absorbLock;
        public bool directFaith;
        public AegisSunSnapshot sun=new AegisSunSnapshot();
        public AegisGlyph[] glyphs=Array.Empty<AegisGlyph>();
        public AegisMirrorSnapshot mirror;
        public AegisRouteSnapshot route=new AegisRouteSnapshot();
        public AegisExtensionSnapshot extensions=new AegisExtensionSnapshot();
        public DeferredHit[] hits=Array.Empty<DeferredHit>();
    }
    public sealed class AegisDamageState
    {
        public int LastThreatBossId;
        public readonly AegisRouteState Route=new AegisRouteState();
        public readonly AegisSunState Sun=new AegisSunState();
        public readonly AegisGlyphState Glyphs=new AegisGlyphState();
        public readonly AegisMirrorState Mirror=new AegisMirrorState();
        public readonly AegisExtensionState Extensions=new AegisExtensionState();
        private readonly List<DeferredHit> _hits=new List<DeferredHit>();
        public float FaithRemaining{get;private set;}
        public bool DirectFaith{get;private set;}
        public float AbsorbLock{get;private set;}
        public double Pending=>_hits.Sum(h=>h.remaining);
        public void Activate(bool directReduction=false,bool steadfast=false)
        {DirectFaith=directReduction;FaithRemaining=directReduction?AegisPaladinData.NineKeyFaithDuration+(steadfast?2:0):AegisPaladinData.FaithDuration;}
        // Apply once before shield absorption. Scripted/bypass damage and already
        // transferred/deferred damage retain their existing non-mitigation policy.
        public double ReduceIncoming(double damage,CombatDamageKind kind,CombatDefenseType defense)
        {
            if(!CombatDamageRules.FinitePositive(damage))return 0;
            if(!DirectFaith||FaithRemaining<=0||defense==CombatDefenseType.Bypass||kind==CombatDamageKind.System||
                kind==CombatDamageKind.Deferred||kind==CombatDamageKind.Transfer)return damage;
            return damage*(1-AegisPaladinData.NineKeyFaithReduction);
        }
        // Input is already mitigated and shield-absorbed. Repayment MUST bypass this method.
        public double Split(double confirmed)
        {
            if(double.IsNaN(confirmed)||double.IsInfinity(confirmed)||confirmed<=0)return 0;
            if(FaithRemaining<=0||DirectFaith)return confirmed;
            double delayed=confirmed*AegisPaladinData.DeferredFraction;
            _hits.Add(new DeferredHit{remaining=delayed,seconds=AegisPaladinData.DeferredDuration});return confirmed-delayed;
        }
        public bool ShieldAbsorbed(double actual,bool resourceEnabled=true)
        {
            if(!resourceEnabled)return false;
            if(actual<=0||double.IsNaN(actual)||double.IsInfinity(actual)||AbsorbLock>0)return false;
            AbsorbLock=AegisPaladinData.AbsorbInterval;return true;
        }
        public double Advance(float seconds)
        {
            if(!CharacterDefinitionValidation.Finite(seconds)||seconds<0)return 0;
            Route.Advance(seconds);Sun.Advance(seconds);Glyphs.Advance(seconds);Mirror.Advance(seconds);FaithRemaining=Math.Max(0,FaithRemaining-seconds);AbsorbLock=Math.Max(0,AbsorbLock-seconds);
            double due=0;
            for(int i=_hits.Count-1;i>=0;i--){var h=_hits[i];double pay=h.seconds<=seconds?h.remaining:h.remaining*seconds/h.seconds;due+=pay;h.remaining-=pay;h.seconds-=seconds;if(h.seconds<=0)_hits.RemoveAt(i);}
            return due;
        }
        public void Clear(){_hits.Clear();LastThreatBossId=0;DirectFaith=false;FaithRemaining=AbsorbLock=0;Route.Clear();Sun.Clear();Glyphs.Clear();Mirror.Clear();Extensions.Clear();}
        public AegisStateSnapshot Capture()=>new AegisStateSnapshot{lastThreatBossId=LastThreatBossId,directFaith=DirectFaith,faith=FaithRemaining,absorbLock=AbsorbLock,sun=Sun.Capture(),glyphs=Glyphs.Capture(),mirror=Mirror.Capture(),route=Route.Capture(),extensions=Extensions.Data.Copy(),hits=_hits.Select(h=>new DeferredHit{remaining=h.remaining,seconds=h.seconds}).ToArray()};
        public bool Restore(AegisStateSnapshot s)
        {
            if(s==null||!AegisRouteState.Valid(s.route)||s.hits==null||!CharacterDefinitionValidation.Finite(s.faith)||s.faith<0||!CharacterDefinitionValidation.Finite(s.absorbLock)||s.absorbLock<0||s.hits.Any(h=>h==null||double.IsNaN(h.remaining)||double.IsInfinity(h.remaining)||h.remaining<0||double.IsNaN(h.seconds)||double.IsInfinity(h.seconds)||h.seconds<=0))return false;
            if(!new AegisExtensionState().Restore(s.extensions))return false;
            if(!new AegisSunState().Restore(s.sun)||!new AegisGlyphState().Restore(s.glyphs)||!new AegisMirrorState().Restore(s.mirror))return false;
            Clear();LastThreatBossId=s.lastThreatBossId;DirectFaith=s.directFaith;Sun.Restore(s.sun);Glyphs.Restore(s.glyphs);Mirror.Restore(s.mirror);Route.Restore(s.route);Extensions.Restore(s.extensions);FaithRemaining=s.faith;AbsorbLock=s.absorbLock;_hits.AddRange(s.hits.Select(h=>new DeferredHit{remaining=h.remaining,seconds=h.seconds}));return true;
        }
    }
    [Serializable] public sealed class CombatDebuff
    { public string id, tag; public bool dispellable, stun, root; public float remaining; }
    [Serializable] public sealed class CombatDebuffSnapshot { public float knockbackImmunity, controlImmunity, morningEnd,invulnerabilityEnd,floraAttackEnd,floraAttackBonus; public int knockbackSource, controlSource; public CombatDebuff[] entries=Array.Empty<CombatDebuff>(); }
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
