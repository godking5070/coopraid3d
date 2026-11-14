using System;
using System.Collections.Generic;
using System.Linq;

namespace RaidRogue
{
    // Live definitions registered together with the new authoritative adapter.
    // All coefficients are prototype proposals, not measured raid balance.
    public static class NineKeyTankData
    {
        public const string Handler="tank.nine";
        public const string MeasuredStep="tank.aux.measured-step";
        public static bool Supports(RosterJob job)=>job!=null&&(job.index==1||job.index==2);
        public static string ComboName(RosterJob job,string skillId,int stage)
        {
            if(!Supports(job)||stage<1||stage>3)return "";
            bool blood=job.index==1;
            string key=CharacterRosterData.Key(skillId);
            if(key=="g1")return (blood?new[]{"단두대 가르기","흉골 찌르기","분노의 처형"}:new[]{"용아 찌르기","화염 할퀴기","용맥 장전"})[stage-1];
            string[][] choices=blood?new[]{new[]{"야수의 휩쓸기","갈증 가르기","포식참"},new[]{"대검 후려치기","지축 가르기","대지 분쇄"},new[]{"붉은 칼날","교차 절개","혈흔 폭발"}}:
                new[]{new[]{"영거리 분사","용미 후려치기","산탄 폭발"},new[]{"용아 직격","압축 분사","관통 작렬"},new[]{"용골 후려치기","지축 격파","화룡 파쇄"}};
            int variant=key=="g20"?0:key=="g21"?1:key=="g22"?2:-1;
            return variant<0?"":choices[variant][stage-1];
        }
        public static string Id(RosterJob job,string key)=>job.Id+".nine."+key;
        public static string Gauge(RosterJob job)=>job.Id+".ultimate";
        public static string Heat(RosterJob job)=>job.Id+".heat";
        public static string Core(RosterJob job,int route,int variant)=>Id(job,"core"+route+variant);
        public static CharacterDefinition Build(RosterJob job)
        {
            if(!Supports(job))throw new ArgumentException("Only berserker and dragon breaker use this definition");
            bool blood=job.index==1;
            var d=CharacterRosterData.Definition(job);
            d.controlLayout=CharacterControlLayout.NineKey;
            d.description=job.theme+" · "+job.weapon+" · 9키 / 핵심1·보조2 / 수치 초기안";
            d.resources=new[]{
                new JobResourceDefinition{id=job.Resource,name=blood?"혈기":"용염 탄창",maximum=blood?100:4,integer=true},
                new JobResourceDefinition{id=Gauge(job),name="필살기 게이지",maximum=100,integer=true}}
                .Concat(blood?Array.Empty<JobResourceDefinition>():new[]{new JobResourceDefinition{id=Heat(job),name="노심 온도",maximum=100,integer=true}}).ToArray();
            var skills=d.skills.ToList();
            foreach(var retired in skills)if(retired.id!=job.SkillId("p"))retired.implemented=false;
            CharacterSkillDefinition Add(string key,string name,string description,SkillTargetRule target,float range=3,float cooldown=0,int chain=0)
            {
                var skill=new CharacterSkillDefinition{id=Id(job,key),name=name,description=description,handlerId=Handler,
                    implemented=true,target=target,range=range,cooldown=cooldown,comboChain=chain,usesGcd=chain>0,gcd=2,comboWindow=3600};
                skills.Add(skill);return skill;
            }
            Add("g1",blood?"처형 검술":"용아 창술",blood?"단두대 가르기 → 흉골 찌르기 → 분노의 처형":"용아 찌르기 → 화염 할퀴기 → 용맥 장전",SkillTargetRule.LivingEnemy,3,0,1);
            string[] second=blood?new[]{"포식 연격","분쇄 연격","혈흔 연격"}:new[]{"산탄 연격","관통 연격","파쇄 연격"};
            string[] secondDescriptions=blood?new[]{"마지막 적중 고정 계수 자기 회복","무력화 강화 / 직접 피해 감소","마지막 적중 짧은 출혈 / 틱 자원 없음"}:new[]{"근거리 넓은 공격 / 자원은 루트 규칙만 적용","중거리 / 근접 피해 효율 감소","무력화 강화 / 직접 피해 감소"};
            for(int i=0;i<3;i++)Add("g2"+i,second[i],secondDescriptions[i],SkillTargetRule.LivingEnemy,!blood&&i==1?12:3,0,2);
            string[] third=blood?new[]{"갈증의 일격","분쇄 박치기","피의 격려"}:new[]{"긴급 장전","용격포 사출","비늘 방출"};
            string[] thirdDescriptions=blood?new[]{"즉발 근접 공격 + 고정 자기 회복","즉발 고무력화 / 카운터 아님","자신/지정 동료의 짧은 지속 회복"}:new[]{"탄1 / 포격은 마지막 콤보 탄종 복제·기록 없으면 관통 / 온도 불변","별도 용염 즉발 공격·무력화 / 탄 소비 없음","자신과 지정 동료 보호 / 자기 지정 중복 없음"};
            for(int i=0;i<3;i++)Add("a"+i,third[i],thirdDescriptions[i],i==2?SkillTargetRule.LivingAlly:!blood&&i==0?SkillTargetRule.Self:SkillTargetRule.LivingEnemy,i==2?20:!blood?12:3,14);
            Add("c",blood?"대지 퍼올리기":"용의 역린","신호 중 정면 적중 카운터 / 자원·강제 이동 없음",SkillTargetRule.LivingEnemy,3,10).isCounter=true;
            Add("q",blood?"야수의 심장":"화룡의 비늘 갑주","자기 피해 감소30%·6초 / 직업 자원·현재 체력 조건 없음 (초기안)",SkillTargetRule.Self,0,24).usableWhileCasting=true;
            var move=Add("m",blood?"맹수의 질주":"화염 추진","입력 방향 지상 이동 / 기본 무적·추적 없음",SkillTargetRule.Self,0,8);
            move.handlerId="legacy.dash";move.usableWhileCasting=true;move.movement=new JobMovementDefinition{distance=4,duration=.2f};
            Add("f",blood?"포식자의 포효":"황혼의 도전","적 도발 / 지정 다른 탱커에게 기억한 보스 적개심80% 인계 / 보스 대상 기믹 유지 (초기안)",SkillTargetRule.None,20,20).usableWhileCasting=true;
            var ultimate=Add("v",blood?"정오의 포효":"황혼의 종말",blood?"대검 충격파 + 짧은 파티 지속 회복 / 전용 게이지100":"용염 포격·무력화 / 카운터·탄창 초기화 없음 / 전용 게이지100",SkillTargetRule.None,20,.25f);
            ultimate.costs=new[]{new ResourceCost{resourceId=Gauge(job),amount=100}};
            string[] routes=blood?new[]{"불사신","폭군의 단두대","피의 폭풍"}:new[]{"포격의 화신","홍련의 연속검","불 꺼지지 않는 가마"};
            string[] roots=blood?new[]{"불굴의 맥동","처형자의 일격","적색 회오리"}:new[]{"연쇄 포격","황혼의 연무","노심 배출"};
            string[] routeDescriptions=blood?new[]{
                "혈기30 / 지원 대상 즉시 회복 / 무효 지정 동료는 소비 없이 거부",
                "혈기60 예약 / 유지 차징·놓기 방출 / Q·4·Space·F 취소 시 예약만 반환 / 1 완주 준비",
                "토글 이동 채널 / 초당혈기15·최대6초 / 비용 선지불 / 2초마다 강화 파동·파티 피해 감소"}:new[]{
                "1완주 관통탄·2완주 파쇄탄 / R은 앞2발 순서대로 소비 / 배열로 피해·무력화·협공 선택",
                "1·2완주 각각1발 / 탄2로8초 연무 / 직접1·2 적중 후 R격발 최대3회 / 준비3초·누적 없음",
                "1완주 장전1·냉각15 / 2마지막 발동시 탄1·가열30 / R온도40 배출 / 배출 전80 이상 강화"};
            string[][] traits=blood?new[]{new[]{"굳은 피","박동 분배"},new[]{"양단","최후의 참수"},new[]{"폭풍 박동","끝없는 격류"}}:
                new[]{new[]{"분할 포격","전탄 일제사격"},new[]{"유연한 연무","홍련 절정"},new[]{"온열 순환","노심 압축"}};
            string[][] traitDescriptions=blood?new[]{
                new[]{"자기 초과 회복 일부를 상한·수명이 있는 보호막으로 전환","자기 회복 일부를 지정 지원 동료에게 분배 / 무료 복제 없음"},
                new[]{"R을 두 짧은 차징으로 분할 / 총 자원·피해 예산 유지","마지막 차징 단계 추가 / 더 긴 준비"},
                new[]{"첫 파동 앞당김 / 후반 보상 감소 / 무료 파동 없음","후반 파동 강화 / 초반 효과 감소"}}:new[]{
                new[]{"R 앞1발 / 제한시간 내 다음1발과 한 쌍 / 쌍 완료시 기록 초기화","4발 필요 / 앞뒤 두 쌍 / 긴 발사 동작"},
                new[]{"격발 준비 유예 확대 / 마지막 격발 강화 감소","짧은 유예의 연속 격발 보상"},
                new[]{"적정 온도 구간 확대 / 고온 방출 보상 감소","고온 방출 보상 증가 / 반복 보호 감소"}};
            var loadouts=new List<CharacterLoadoutDefinition>{
                new CharacterLoadoutDefinition{id=job.Id+".loadout.2",name="2 치환 콤보",slot=CharacterSlot.Gcd2,choices=Enumerable.Range(0,3).Select(i=>Id(job,"g2"+i)).ToArray()},
                new CharacterLoadoutDefinition{id=job.Id+".loadout.3",name="3 보조 기술",slot=CharacterSlot.OffGcd1,choices=Enumerable.Range(0,3).Select(i=>Id(job,"a"+i)).ToArray()}};
            for(int i=0;i<3;i++)
            {
                Add("r"+(i+1),roots[i],routeDescriptions[i],SkillTargetRule.None,20,blood&&i==1?0:.25f);
                d.routes[i].name=routes[i];d.routes[i].identitySkillId=Id(job,"r"+(i+1));
                d.routes[i].unlockSkills=new[]{d.routes[i].identitySkillId};d.routes[i].capacityOverrides=Array.Empty<ResourceCost>();
                for(int variant=0;variant<2;variant++)Add("core"+(i+1)+variant,traits[i][variant],traitDescriptions[i][variant],SkillTargetRule.None,0);
                loadouts.Add(new CharacterLoadoutDefinition{id=job.Id+".loadout.core"+(i+1),name="핵심 특성",routeId=d.routes[i].id,slot=0,choices=new[]{Core(job,i+1,0),Core(job,i+1,1)}});
            }
            d.loadouts=loadouts.ToArray();d.skills=skills.ToArray();
            var entries=new[]{("g1",CharacterSlot.Gcd1),("g20",CharacterSlot.Gcd2),("a0",CharacterSlot.OffGcd1),("c",CharacterSlot.Counter),("q",CharacterSlot.Job1),("m",CharacterSlot.Movement),("r1",CharacterSlot.Identity),("f",CharacterSlot.OffGcd2),("v",CharacterSlot.Ultimate)};
            d.slots=entries.Select(p=>new CharacterSlotDefinition{slot=p.Item2,skillId=Id(job,p.Item1),initiallyUnlocked=p.Item2!=CharacterSlot.Identity&&p.Item2!=CharacterSlot.Ultimate})
                .Concat(new[]{new CharacterSlotDefinition{slot=CharacterSlot.Potion,skillId=job.SkillId("p"),initiallyUnlocked=true}}).ToArray();
            d.unlocks=new[]{new CharacterUnlockDefinition{id=job.Id+".stage3",afterStage=3,slot=CharacterSlot.Ultimate,choices=new[]{Id(job,"v")}}};
            d.auxiliaryTraits=AegisPaladinData.BuildNineKey().auxiliaryTraits.Where(t=>t.id!=AegisPaladinData.LastingSanctuary)
                .Select(t=>new CharacterAuxiliaryDefinition{id=t.id,name=t.name,description=t.description})
                .Concat(new[]{new CharacterAuxiliaryDefinition{id=MeasuredStep,name="신중한 보폭",description="Space 이동거리+1m / 충전 회복+2초 / 무적·공격 보상 없음 (초기안)"}}).ToArray();
            CharacterRosterData.ApplyDashProfile(d,job.index);
            d.migrateSnapshot=s=>Migrate(job,s);return d;
        }
        public static CharacterStateSnapshot Migrate(RosterJob job,CharacterStateSnapshot source)
        {
            if(source?.jobId!=job.Id||source.selections==null||!source.selections.Any(s=>s?.slot==CharacterSlot.Job2))return source;
            if(!new CharacterRuntimeState(CharacterRosterData.Definition(job)).Restore(source))return null;
            var definition=Build(job);var next=source.Copy();
            next.selections=definition.slots.Select(s=>new JobSlotSelection{slot=s.slot,skillId=s.skillId}).ToArray();
            if(!string.IsNullOrEmpty(next.routeId))
            {
                var route=definition.routes.First(r=>r.id==next.routeId);
                next.selections.First(s=>s.slot==CharacterSlot.Identity).skillId=route.identitySkillId;
                next.coreTrait=definition.loadouts.First(r=>r.routeId==route.id).choices[0];
            }
            next.skills=definition.skills.Select(s=>source.skills.FirstOrDefault(old=>old.id==s.id)??new JobSkillState{id=s.id,charges=s.maximumCharges,rechargeTotal=s.cooldown}).ToArray();
            foreach(var selection in next.selections)
            {
                var oldSlot=selection.slot==CharacterSlot.OffGcd2?CharacterSlot.Job2:selection.slot;
                var oldSelection=source.selections.First(s=>s.slot==oldSlot);
                var oldTime=source.skills.First(s=>s.id==oldSelection.skillId);var newTime=next.skills.First(s=>s.id==selection.skillId);
                if(oldTime.recharge>newTime.recharge){newTime.charges=0;newTime.recharge=newTime.rechargeTotal=oldTime.recharge;}
            }
            next.resources=definition.resources.Select(r=>new JobResourceValue{id=r.id,value=Math.Min(r.maximum,source.resources.FirstOrDefault(v=>v.id==r.id)?.value??r.initial)}).ToArray();
            next.unlocked=definition.slots.Where(s=>s.initiallyUnlocked).Select(s=>s.skillId).ToArray();
            if(next.routeId.Length>0)next.unlocked=next.unlocked.Union(new[]{definition.routes.First(r=>r.id==next.routeId).identitySkillId}).ToArray();
            int[] retained=Enumerable.Range(0,source.chosenRules.Length).Where(i=>source.chosenRules[i]==job.Id+".stage3").ToArray();
            next.chosenRules=retained.Select(i=>source.chosenRules[i]).ToArray();next.chosenRewardKeys=retained.Select(i=>source.chosenRewardKeys[i]).ToArray();
            if(next.chosenRules.Length>0)next.unlocked=next.unlocked.Union(new[]{Id(job,"v")}).ToArray();
            next.automaticChoices=source.automaticChoices.Any(id=>id==job.SkillId("v1")||id==job.SkillId("v2"))?new[]{Id(job,"v")}:Array.Empty<string>();
            var archive=source.selections.Where(s=>s.slot==CharacterSlot.Active||s.slot==CharacterSlot.OffGcd2||s.slot==CharacterSlot.Ultimate)
                .Select(s=>new JobBuildEffect{id="retired.tank."+s.skillId,stacks=1});
            next.effects=next.effects.Concat(archive.Where(a=>!next.effects.Any(e=>e.id==a.id))).ToArray();
            next.castSkillId="";next.castTarget=0;next.castRemaining=next.castTotal=0;next.castAllowsMovement=false;
            next.combo1=next.combo2=1;next.comboTime1=next.comboTime2=0;
            next.auxiliaryTraits=definition.auxiliaryTraits.Take(2).Select(t=>t.id).ToArray();return next;
        }
    }
}
