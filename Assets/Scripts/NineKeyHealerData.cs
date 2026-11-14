using System;
using System.Collections.Generic;
using System.Linq;

namespace RaidRogue
{
    // New healer contract. Catalog activation waits for the complete effect adapter.
    // Numerical values below are prototype tuning, not established raid balance.
    public static class NineKeyHealerData
    {
        public const string Handler="healer.nine";
        public const string Reserve="healer.aux.reserve",Reach="healer.aux.reach",Wide="healer.aux.wide",
            Safe="healer.aux.safe",Residual="healer.aux.residual",Mobile="healer.aux.mobile";
        public static bool Supports(RosterJob job)=>job!=null&&job.index>=3&&job.index<=5;
        public static string Id(RosterJob job,string key)=>job.Id+".nine."+key;
        public static string Gauge(RosterJob job)=>job.Id+".ultimate";
        public static string Core(RosterJob job,int route,int variant)=>Id(job,"core"+route+variant);
        public static readonly string[][] AttackNames={
            new[]{"빛의 잎사귀","정령의 숨결","세계수의 울림"},
            new[]{"스타카토","크레센도","천상의 관통음"},
            new[]{"은침 투척","경혈 자극","활력 추출"}};
        public static readonly string[][][] HealNames={
            new[]{new[]{"싹틔우기","잎새 감싸기","생명의 꽃"},new[]{"풀잎의 파동","뿌리의 고동","숲의 숨결"},new[]{"아침 이슬","맑은 물결","새벽의 샘"}},
            new[]{new[]{"첫 음","감싸는 선율","독주 종결"},new[]{"낮은 화음","퍼지는 성가","합창 종결"},new[]{"발맞춤","순풍의 선율","행진 종결"}},
            new[]{new[]{"활력액","회복액","재생 영약"},new[]{"약초 분무","생기 분무","치유 연무"},new[]{"진정액","보호액","완충 영약"}}};
        public static string ComboName(RosterJob job,string skill,int step)
        {
            if(!Supports(job)||step<1||step>3)return "";
            string key=CharacterRosterData.Key(skill);int i=job.index-3;
            if(key=="g1")return AttackNames[i][step-1];
            int variant=key=="g20"?0:key=="g21"?1:key=="g22"?2:-1;
            return variant<0?"":HealNames[i][variant][step-1];
        }
        public static CharacterDefinition Build(RosterJob job)
        {
            if(!Supports(job))throw new ArgumentException("healer job");
            int i=job.index-3;var d=CharacterRosterData.Definition(job);
            d.controlLayout=CharacterControlLayout.NineKey;
            d.description=job.theme+" · "+job.weapon+" · 9키 치유 / 핵심1·보조2 / 수치 초기안";
            d.resources=new[]{new JobResourceDefinition{id=job.Resource,name=i==0?"세계수의 꽃봉오리":i==1?"공명음":"진홍 영약",maximum=i==2?100:3,integer=true},
                new JobResourceDefinition{id=Gauge(job),name="필살기 게이지",maximum=100,integer=true}};
            var skills=d.skills.ToList();foreach(var old in skills)if(old.id!=job.SkillId("p")){old.implemented=false;old.costs=Array.Empty<ResourceCost>();}
            CharacterSkillDefinition Add(string key,string name,string description,SkillTargetRule target,float cooldown=0,float range=25,int chain=0)
            {
                var s=new CharacterSkillDefinition{id=Id(job,key),name=name,description=description,target=target,handlerId=Handler,
                    implemented=true,isCounter=key=="c",cooldown=cooldown,range=range,usesGcd=chain>0,gcd=2,comboChain=chain,comboWindow=3600};
                skills.Add(s);return s;
            }
            Add("g1",new[]{"신록의 주문","천상의 음률","은침술"}[i],string.Join(" → ",AttackNames[i])+" / 마지막 유효 적중에 자원",SkillTargetRule.LivingEnemy,chain:1);
            string[][] seconds={new[]{"생명의 새싹","숲의 맥박","이슬방울"},new[]{"독주 선율","합창 선율","행진 선율"},new[]{"회복 처방","분무 처방","완충 처방"}};
            for(int v=0;v<3;v++)Add("g2"+v,seconds[i][v],string.Join(" → ",HealNames[i][v])+" / 매 입력 직접 치유 / 완주 자원 / 과치유 인정 / 직업 자원 비용 없음",SkillTargetRule.LivingAlly,range:i==0&&v==2?30:25,chain:2);
            string[][] thirds={new[]{"가시덩굴","나무껍질","청명한 이슬"},new[]{"불협화음","앙코르","순풍의 행진"},new[]{"예비 약제","산성 비약","완충 연무"}};
            string[][] thirdDescriptions={new[]{"원거리 공격·무력화 / 카운터 아님","자신/지정 동료의 짧은 피해 감소","정화 가능한 효과1개 + 작은 지속 회복"},
                new[]{"즉발 공격·무력화 / 카운터 아님","직전 자기 Q/F의 기본 즉시 회복만 재현 / 정화·보호·HoT·특성·자원 복제 없음","주변 동료 이동 지원 / 강제 이동·이동기 초기화 없음"},
                new[]{"영약20 또는 마지막 콤보 성분1 / 기록 없으면 청색 / 실제 변화 없으면 소비 없음","원거리 공격·무력화 / 카운터 아님","지정 동료 보호막·지속 회복"}};
            for(int v=0;v<3;v++)
            {
                var target=i==0?(v==0?SkillTargetRule.LivingEnemy:SkillTargetRule.LivingAlly):i==1?(v==0?SkillTargetRule.LivingEnemy:SkillTargetRule.None):v==0?SkillTargetRule.Self:v==1?SkillTargetRule.LivingEnemy:SkillTargetRule.LivingAlly;
                Add("a"+v,thirds[i][v],thirdDescriptions[i][v],target,14);
            }
            Add("c",new[]{"고목의 뿌리","포르티시모","마비 약초탄"}[i],"카운터 신호 중 정면 적중 / 무력화와 별개 / 자원 비용 없음",SkillTargetRule.LivingEnemy,10,15).usableWhileCasting=true;
            var q=Add("q",new[]{"생명의 개화","수호천사의 독주","응급 영약"}[i],"단일 긴급 직접 회복 + 정화 가능한 효과1개 / 기본2충전 순차 회복",SkillTargetRule.LivingAlly,18);
            q.maximumCharges=2;q.usableWhileCasting=true;
            q.chargeTraitId=Reserve;q.chargeTraitBonus=1;q.chargeTraitCooldownScale=1.25f;
            var move=Add("m",new[]{"바람걸음","글리산도 스텝","안개 걸음"}[i],"입력 방향 지상 이동 / 기본 무적·강제 아군 이동 없음",SkillTargetRule.Self,8,0);
            move.handlerId="legacy.dash";move.usableWhileCasting=true;move.movement=new JobMovementDefinition{distance=4,duration=.2f};
            Add("f",new[]{"대자연의 축복","성가대의 합창","만병통치 연무"}[i],"기본 직접 광역 회복 + 직업별 후속 효과 / 적·직업 자원 불필요",SkillTargetRule.None,20,30).usableWhileCasting=true;
            var ultimate=Add("v",new[]{"세계수의 만개","영웅들의 행진곡","현자의 붉은 돌"}[i],"강한 광역 회복 + 직업별 후속 효과 / 부활·일반 자원·Q 초기화 없음",SkillTargetRule.None,.25f,30);
            ultimate.costs=new[]{new ResourceCost{resourceId=Gauge(job),amount=100}};
            string[][] roots={new[]{"생명의 묘목","계절의 중계","장미 수확"},new[]{"협연 지휘","수호 악절","영웅의 진혼가"},new[]{"혈류 연결","조합 처방","연속 시술"}};
            string[][] descriptions={new[]{"꽃봉오리1 / 최대3명 성장형 HoT / 자기2 주 대상1명만 촉진 / 개화 시 남은HoT 종료",
                "꽃봉오리1 / 계절꽃2곳·20초 / 2완주 중계1회 / 겹침중복 없음 / 오래된 꽃 교체 미리보기",
                "꽃봉오리1 / 저장12초·최대3단계 / 직접1·2로 성장 / R 수확 / 만료 시 절반 회복만"},
                new[]{"공명음1 / 다른 동료 요청 / 직접공격·치유·방어 응답 또는8초 만료 / 다음2완주 화답 소비",
                "공명음2 / 파티3층·12초 / 피해판정당1층 / R 자기 잔량 일부 회복 전환 / 남의 보호막 사용 금지",
                "이동 가능한 토글 연주 / 악절2초 최대3 / 악절마다 공명음1 예약 / 취소 시 미완료 예약만 반환"},
                new[]{"영약40 / 연결12초 / 직접1·2당 고정 추가 회복 / R 재배정은 비용·수명·초기회복 갱신 없음",
                "영약 대신 성분3칸 / 1 적색·2 청색 / 순서 아닌 비율 / 가득차면 반대 성분 교체 / R 모두 소비",
                "영약40 / 첫R 즉시 안정화 / 이후R 봉합·예후 / 전체12초·단계별 다른 환자 가능"}};
            string[][][] traits={new[]{new[]{"깊은 뿌리","새순 순환"},new[]{"깊은 숲","움직이는 계절"},new[]{"빠른 수확","완숙 장미"}},
                new[]{new[]{"독주 지휘","합창 지휘"},new[]{"두터운 악절","연속 악절"},new[]{"즉흥 후렴","완주 악장"}},
                new[]{new[]{"집중 혈류","분기 혈류"},new[]{"단일 성분 정제","복합 반응"},new[]{"전담 주치의","순회 진료"}}};
            string[][][] traitText={new[]{new[]{"묘목 최대2명·각 회복 강화","자연 성장 단축·묘목 총회복 감소"},new[]{"계절꽃1곳·범위와회복 강화","R 길게 오래된 꽃 이동 / 수명·비용 환급 없음"},new[]{"최대2단계·작고 빠른 수확","최대4단계·긴 저장·마지막 성장 보상"}},
                new[]{new[]{"요청+화답 상한1·초기보호 집중","상한3·초기보호 일부를 합창으로 이동"},new[]{"2층의 큰 보호","4층의 작은 보호"},new[]{"짧은 악절·최대2·악절당효과 감소","3악절·초기효과 일부를 마지막으로 이동"}},
                new[]{new[]{"한 연결의 고정 회복 강화","두 연결에 총 회복 예산 분할 / 짧게첫째·길게둘째 재지정"},new[]{"단일 성분3개 강화·혼합추가효과 감소","혼합 강화·단일추가효과 감소 / 기본직접회복 유지"},new[]{"같은 환자 세처치 후 마지막강화","2·3단계 환자 변경시 작은 주변회복"}}};
            var loadouts=new List<CharacterLoadoutDefinition>{new CharacterLoadoutDefinition{id=job.Id+".loadout.2",name="2 치유 콤보",slot=CharacterSlot.Gcd2,choices=Enumerable.Range(0,3).Select(v=>Id(job,"g2"+v)).ToArray()},
                new CharacterLoadoutDefinition{id=job.Id+".loadout.3",name="3 보조 기술",slot=CharacterSlot.OffGcd1,choices=Enumerable.Range(0,3).Select(v=>Id(job,"a"+v)).ToArray()}};
            for(int r=0;r<3;r++)
            {
                Add("r"+(r+1),roots[i][r],descriptions[i][r],SkillTargetRule.None,.25f,25);
                d.routes[r].identitySkillId=Id(job,"r"+(r+1));d.routes[r].unlockSkills=new[]{d.routes[r].identitySkillId};
                d.routes[r].capacityOverrides=i==2&&r==1?new[]{new ResourceCost{resourceId=job.Resource,amount=3}}:Array.Empty<ResourceCost>();
                for(int v=0;v<2;v++)Add("core"+(r+1)+v,traits[i][r][v],traitText[i][r][v],SkillTargetRule.None);
                loadouts.Add(new CharacterLoadoutDefinition{id=job.Id+".loadout.core"+(r+1),name="핵심 특성",routeId=d.routes[r].id,slot=0,choices=new[]{Core(job,r+1,0),Core(job,r+1,1)}});
            }
            d.skills=skills.ToArray();d.loadouts=loadouts.ToArray();
            var slots=new[]{("g1",CharacterSlot.Gcd1),("g20",CharacterSlot.Gcd2),("a0",CharacterSlot.OffGcd1),("c",CharacterSlot.Counter),("q",CharacterSlot.Job1),("m",CharacterSlot.Movement),("r1",CharacterSlot.Identity),("f",CharacterSlot.OffGcd2),("v",CharacterSlot.Ultimate)};
            d.slots=slots.Select(s=>new CharacterSlotDefinition{slot=s.Item2,skillId=Id(job,s.Item1),initiallyUnlocked=s.Item2!=CharacterSlot.Identity&&s.Item2!=CharacterSlot.Ultimate})
                .Concat(new[]{new CharacterSlotDefinition{slot=CharacterSlot.Potion,skillId=job.SkillId("p")}}).ToArray();
            // Preserve the existing V story gate until an explicit progression redesign.
            d.unlocks=new[]{new CharacterUnlockDefinition{id=job.Id+".stage3",afterStage=3,slot=CharacterSlot.Ultimate,choices=new[]{Id(job,"v")}}};
            string[] aux={Reserve,Reach,Wide,Safe,Residual,Mobile};
            string[] names={"비상 비축","긴 손길","넓은 돌봄","안전한 처치","잔여 처방","기동 지원"};
            string[] detail={"Q2→3충전 / 순차재충전+25% 초기안","2·Q 지원 사거리+5m / F·R 불변","F 범위 증가 / 재사용 증가","Q 작은 추가 보호 / 별도 발동제한","자기Q/F 기본직접 과치유 일부→상한·수명 있는HoT / 재귀없음","Space 자신+가까운 지원1명 이동속도 / 강제이동없음"};
            d.auxiliaryTraits=aux.Select((id,n)=>new CharacterAuxiliaryDefinition{id=id,name=names[n],description=detail[n]}).ToArray();
            CharacterRosterData.ApplyDashProfile(d,job.index);
            d.migrateSnapshot=s=>Migrate(job,s);return d;
        }
        public static CharacterStateSnapshot Migrate(RosterJob job,CharacterStateSnapshot source)
        {
            if(source?.jobId!=job.Id||source.selections==null||!source.selections.Any(s=>s?.slot==CharacterSlot.Job2))return source;
            if(!new CharacterRuntimeState(CharacterRosterData.Definition(job)).Restore(source))return null;
            var d=Build(job);var next=source.Copy();
            next.selections=d.slots.Select(s=>new JobSlotSelection{slot=s.slot,skillId=s.skillId}).ToArray();
            var route=d.routes.FirstOrDefault(r=>r.id==source.routeId);
            if(route!=null){next.selections.First(s=>s.slot==CharacterSlot.Identity).skillId=route.identitySkillId;next.coreTrait=d.loadouts.First(r=>r.routeId==route.id).choices[0];}
            next.skills=d.skills.Select(s=>next.skills.FirstOrDefault(old=>old.id==s.id)??new JobSkillState{id=s.id,
                charges=s.maximumCharges+(s.chargeTraitId==Reserve?s.chargeTraitBonus:0),rechargeTotal=s.cooldown*(s.chargeTraitId==Reserve?s.chargeTraitCooldownScale:1)}).ToArray();
            foreach(var selection in next.selections)
            {
                var prior=source.selections.First(s=>s.slot==(selection.slot==CharacterSlot.OffGcd2?CharacterSlot.Job2:selection.slot));
                var old=source.skills.First(s=>s.id==prior.skillId);var current=next.skills.First(s=>s.id==selection.skillId);
                if(old.recharge>current.recharge){current.charges=0;current.recharge=current.rechargeTotal=old.recharge;}
            }
            bool alchemy=job.index==5&&source.routeId==d.routes[1].id;
            next.resources=d.resources.Select(r=>new JobResourceValue{id=r.id,value=r.id==job.Resource&&!alchemy?Math.Min(r.maximum,(float)Math.Floor(source.resources.First(v=>v.id==r.id).value)):0}).ToArray();
            next.unlocked=d.slots.Where(s=>s.initiallyUnlocked).Select(s=>s.skillId).Concat(route==null?Array.Empty<string>():new[]{route.identitySkillId}).ToArray();
            int[] kept=Enumerable.Range(0,source.chosenRules.Length).Where(n=>source.chosenRules[n]==job.Id+".stage3").ToArray();
            next.chosenRules=kept.Select(n=>source.chosenRules[n]).ToArray();next.chosenRewardKeys=kept.Select(n=>source.chosenRewardKeys[n]).ToArray();
            if(kept.Length>0)next.unlocked=next.unlocked.Union(new[]{Id(job,"v")}).ToArray();
            next.automaticChoices=source.automaticChoices.Any(v=>v==job.SkillId("v1")||v==job.SkillId("v2"))?new[]{Id(job,"v")}:Array.Empty<string>();
            next.castSkillId="";next.castTarget=0;next.castRemaining=next.castTotal=0;next.castAllowsMovement=false;
            next.combo1=next.combo2=1;next.comboTime1=next.comboTime2=0;
            next.auxiliaryTraits=d.auxiliaryTraits.Take(2).Select(t=>t.id).ToArray();return next;
        }
    }
}
