using System;
using System.Collections.Generic;
using System.Linq;

namespace RaidRogue
{
    // Latest DPS contract. Register only after its host effect adapter is connected.
    // All coefficients, timings and starting resources remain prototype settings.
    public static class NineKeyDamageData
    {
        public const string Handler = "damage.nine";
        public const string Reach="damage.aux.reach", Mobile="damage.aux.mobile", Guard="damage.aux.guard",
            Response="damage.aux.response", Support="damage.aux.support", Recovery="damage.aux.recovery";
        public static bool Supports(RosterJob job) => job != null && job.index >= 6 && job.index <= 11;
        public static string Id(RosterJob job,string key) => job.Id+".nine."+key;
        public static string Gauge(RosterJob job) => job.Id+".ultimate";
        public static string Core(RosterJob job,int route,int variant) => Id(job,"core"+route+variant);
        // Prototype extra stagger for explicitly designated second-combo choices.
        // This is not counter success and does not change resource completion rules.
        public static float ComboStagger(int jobIndex,int chain,int choice)
        {
            if(chain!=2)return 0;
            bool enhanced=(jobIndex==6||jobIndex==11)?choice==2:
                (jobIndex==8||jobIndex==9||jobIndex==10)&&choice==1;
            return enhanced?40:0;
        }
        public static readonly string[][] First = {
            new[]{"그림자 베기","초승달 교차","흑월 절개"},new[]{"질풍 찌르기","나선 관통","뇌전 일섬"},
            new[]{"섬광 발포","쌍탄 사격","은탄 관통"},new[]{"바람 화살","이중 시위","매의 눈 관통"},
            new[]{"별가루","혜성 꼬리","맥동성 방출"},new[]{"유리 파편","프리즘 광선","수정창"}};
        // Prototype gun combo footprints: straight penetration / 60-degree spread.
        // Radius is the enemy collision radius, not an extra target-count budget.
        public static int ComboArea(int jobIndex,int chain,int choice)
        {
            if(chain!=2)return 0;
            if(jobIndex==8&&choice>=0&&choice<2)return choice+1;
            if(jobIndex==9&&choice==0)return 1;
            return (jobIndex==9||jobIndex==10||jobIndex==11)&&choice==1?3:0;
        }
        public static bool ComboAreaHit(int shape,DamagePoint origin,DamagePoint aim,DamagePoint target,float range,float radius)
        {
            if(shape<1||shape>3||!origin.Valid||!aim.Valid||!target.Valid||!DamageTime.Positive(range)||!DamageTime.Nonnegative(radius))return false;
            if(shape==3)
            {
                double ox=target.x-origin.x,oz=target.z-origin.z,ax=target.x-aim.x,az=target.z-aim.z;
                return ox*ox+oz*oz<=(range+radius)*(range+radius)&&ax*ax+az*az<=(3+radius)*(3+radius);
            }
            double dx=aim.x-origin.x,dz=aim.z-origin.z,length=Math.Sqrt(dx*dx+dz*dz);
            if(length<.0001)return false;
            double x=target.x-origin.x,z=target.z-origin.z;
            double forward=(x*dx+z*dz)/length,side=Math.Abs(x*dz-z*dx)/length;
            if(forward < -radius||x*x+z*z>(range+radius)*(range+radius))return false;
            return side<=(shape==1?.6:Math.Max(0,forward)*.577350269)+radius;
        }
        public static readonly string[][] Second = {
            new[]{"절개 연격","비수 투척술","파쇄 연격"},new[]{"회전창술","뇌전투사","뇌운각인"},
            new[]{"관통 사격","확산 사격","은화 사격"},new[]{"관통시","갈래시","바람매듭시"},
            new[]{"항성 관통","중력 파동","성운 붕괴"},new[]{"환영 대낫","유리꽃 절단","파쇄 낫"}};
        public static readonly string[][] Third = {
            new[]{"순간 절개","급소 투척","연막 수호"},new[]{"뇌전 강습","전광 추격","천뢰 투창"},
            new[]{"곡예 사격","섬광탄","추적 은탄"},new[]{"유도 정령시","쐐기시","귀환시"},
            new[]{"유성 파편","천체 충격","성진 보호"},new[]{"파편 칼날","결정 파쇄","반사 보호"}};
        public static readonly string[][] Fourth = {
            new[]{"그림자 반전","힘줄 절단","밤의 장막"},new[]{"뇌룡 파쇄창","폭풍 분쇄","풍벽 수호"},
            new[]{"저지 산탄","충격 유탄","엄호탄"},new[]{"돌풍 파쇄시","뿌리 쐐기시","수호 깃털"},
            new[]{"중력 반전","천체 압쇄","별빛 보호"},new[]{"반사의 거울벽","결정 붕쇄","방패 투영"}};
        public static readonly string[][] Tactics = {
            new[]{"암기 주머니","유도 독침","야행로"},new[]{"초전도 축전","유도 표식","폭풍의 씨앗"},
            new[]{"예비탄","엄호 연막","전술 신호"},new[]{"깃털 축전","순풍의 이정표","숲의 위장막"},
            new[]{"성좌 충전","관측 표식","별빛 호위"},new[]{"파편 응집","굴절 표식","잔상 귀환"}};
        public static readonly string[][] Routes = {
            new[]{"사각의 집행자","무한 그림자 춤","인술의 달인"},new[]{"뇌룡의 선봉","뇌제의 창술","태풍의 핵"},
            new[]{"은탄의 쌍수","전술폭파수","은빛 관측자"},new[]{"천궁의 명사수","질풍의 연사수","사계의 마궁사"},
            new[]{"종말의 별","유성우 지휘자","중력 제어자"},new[]{"환영의 군단장","사신의 대낫","거울 설계자"}};
        public static readonly string[][] Roots = {
            new[]{"종언의 일격","흑월 종결","기폭 봉인"},new[]{"뇌룡 강습","뇌룡출수","폭풍창 설치·회수"},
            new[]{"교대 장전","파열 유탄","은빛 관측"},new[]{"쌍성 관통시","질풍노도","계절의 발도·유예"},
            new[]{"종말의 영창","별의 가속","중력 편향"},new[]{"거울의 재연","사신 투영·거울 참수","프리즘 배치"}};
        public static readonly string[][] RootText = {
            new[]{"인장2 / 대상 귀속 약점 정보1을 추가 소비해 강화 / 현재 방향 무관",
                "박자 전부 소비 / GCD 종결 / 직전 사용 콤보만 초기화 / 다른 콤보 보존",
                "인장2 / 짧게 시전 종료·길게 패턴 이동 종료 / 조건 기폭 또는 유한 만료"},
            new[]{"전류50 / 지정한 가까운 지상 지점 강습 / 동료별 후속 직접 공격1회",
                "전류80 / 12초 GCD 가속 / 교차 완주 / 상태 중 콤보 수급 절반",
                "짧게 전류40으로 창2개까지 / 선택2로 충전 / 길게 회수·충전당5 반환"},
            new[]{"양손 독립 탄창 / 짧게 최근 발사 손·길게 반대 손 장전 / 다른 손 공격 유지",
                "마탄3 / 부착6초 자동 기폭 / 1완주 앞당김·2완주 지연 / 최대10초",
                "마탄3 예약 조준 / 완료 후 저격 보관 / R 재입력 발사 / 미완성 취소만 반환"},
            new[]{"풍압40 + 합류 소비 / 같은 적 실제 탄착 한 쌍으로 합류 / 0합류 사용 가능",
                "풍압50 / 첫 묶음과 후속2회 / 사이 콤보 완주로 다음 묶음 강화 / 유한 보관",
                "봄부터 고정 순환 / 짧게 풍압40 발도·다음 계절 / 길게20 유예·방문당1회"},
            new[]{"마력60 예약 / 3구간 영창 / 중단 시 완료 구간만 보존 / 마지막 완료 즉시 발사",
                "마력60 / 선택2 즉발권3회 / 남은 권리 R 소비 ON·OFF / 중복 준비 없음",
                "마력60 / 장1개·12초 / R 압축·확산 / 2완주 중심 추적 / 첫 확산 보호1회"},
            new[]{"파편2 + 실제 완주 기록2개 / 길게 잠금·해제 / 짧게 재연 / 복제 자원·기록 없음",
                "짧게 파편1 남은2 원격 투영 / 길게 파편3 참수 / 투영한2 완주 수급 없음",
                "파편1 거울 최대2 / 위치·방향 조정 / 2완주 반사 최대2회 / 동일 적 피해 예산1회"}};
        public static readonly string[][] Traits = {
            new[]{"집중 처형","예비 암살","짧은 무곡","삼막의 춤","속결 인술","장기 봉인"},
            new[]{"연속 강습","잔류 뇌운","사단연격","집중 방출","유영 폭풍","압축 폭풍"},
            new[]{"교대 속장","심부 탄창","충격 신관","계획 폭파","연속 저격","종결탄"},
            new[]{"너른 합류","연속 탄착","속공 사수","추격 사수","빠른 윤회","머무는 계절"},
            new[]{"짧은 문장","완전한 주문","분산 가속","유성 행렬","안정된 궤도","극점 압축"},
            new[]{"선명한 잔상","연속된 기억","긴 그림자","실체의 칼날","단일 초점","다중 굴절"}};
        private static readonly string[][] TraitText = {
            new[]{"약점 정보 최대1개 / 정보 소비 처형 강화","약점 정보 최대3개 / 정보 하나의 처형 보상 감소",
                "박자 상한2 / 짧은 종결 강화","3박 종결 강화 / 1·2박 종결 피해 감소",
                "부착 즉시 피해 일부 지급 / 기폭 기본 피해 감소 / 총 피해 중복 없음","봉인 대기 시간 증가 / 조건 성공 기폭 강화"},
            new[]{"R 강습2충전 / 회당 비용·피해 감소","R 강습 피해 일부를 유한한 지면 피해로 분배",
                "선택2 완주 후 전류20의 선택적4타 / 다른 GCD 사용 시 기회 종료 / 추가 완주 수급 없음",
                "전광 추격의 비용·피해 증가 / 해당3번 장착 필요",
                "1 완주 적중 시 R 폭풍창을 대상 위치로 이동 / 기본 수명 감소",
                "R 폭풍창의 자동 피해 대신 상한 있는 회수 피해 저장 / 수명 증가 / 자동 피해 중복 없음"},
            new[]{"한 번에2발 장전 / 장전 시간·재사용 단축","손별 탄창5발 / 장전 시간 증가",
                "유탄 기본 타이머 단축 / 콤보별 시간 조정 폭 감소","유탄 기본 타이머·최대 수명 증가 / 조정 폭 증가 / 무한 연장 없음",
                "준비 저격을 작은3회로 분할 / 협공 표식은 준비 한 세트당1회","마탄5 예약 / 긴 조준 후 강한 한 발 / 완료 전 취소만 환급"},
            new[]{"직사 예약 구간 확대 / 합류당 추가 피해 감소","풍압 비용과 한 번에 쓰는 합류 감소 / 작은 R을 자주 사용",
                "첫 사격 묶음 강화 / 후속 묶음 보상 감소","후속 묶음 보관 시간 증가 / 콤보 완주 후 강화 사격 보상 증가",
                "기본 계절 주기 단축 / 유예 시간 감소","계절 유예 시간 증가 / 발도 풍압 비용 증가"},
            new[]{"작은 영창 구간으로 분할 / 전체 메테오 위력 감소","중단 없이 완성한 메테오 강화 / 중단 시 완료 구간 보존은 유지",
                "적은 즉발권을 낮은 비용·짧은 재사용으로 준비","많은 즉발권을 한꺼번에 준비 / 긴 재사용",
                "중력장 확산 범위·유지 시간 증가","중력장 압축 범위 감소 / 집중 피해·무력화 증가"},
            new[]{"기록1개·파편1개로 한 기술 재연 / 한 번의 재연에 집중","기록2개를 시간차로 재연 / 두 번째 공격 강화 / 재생 자원·기록 생성 없음",
                "원격 투영 사거리·보관 시간 증가","직접2 완주 후 다음 참수 강화 / 강화 보관 최대1회",
                "거울 최대1면 / 간단한 반사 경로","거울2면의 실제 반사 경로 성공 보상 강화 / 같은 적 피해 중복 없음"}};
        public static CharacterDefinition Build(RosterJob job)
        {
            if(!Supports(job))throw new ArgumentException("damage job");
            int i=job.index-6;var d=CharacterRosterData.Definition(job);
            d.controlLayout=CharacterControlLayout.NineKey;
            d.description=job.theme+" · "+job.weapon+" · 지상 9키 / 핵심1·보조2 / 수치 초기안";
            d.resources=new[]{new JobResourceDefinition{id=job.Resource,name=new[]{"그림자 인장","뇌룡의 전류","마탄","사계의 풍압","성좌 마력","거울상 파편"}[i],maximum=new[]{5,100,6,100,100,3}[i]},
                new JobResourceDefinition{id=Gauge(job),name="필살기 게이지",maximum=100}};
            var skills=d.skills.ToList();foreach(var old in skills)if(old.id!=job.SkillId("p")){old.implemented=false;old.costs=Array.Empty<ResourceCost>();}
            CharacterSkillDefinition Add(string key,string name,string text,SkillTargetRule target,float cd=0,float range=20,int chain=0)
            {
                var s=new CharacterSkillDefinition{id=Id(job,key),name=name,description=text,handlerId=Handler,implemented=true,
                    target=target,cooldown=cd,range=range,usesGcd=chain>0,gcd=2,comboChain=chain,comboWindow=3600};skills.Add(s);return s;
            }
            Add("g1",new[]{"암영 검술","기본 창술","은탄 속사","바람 궁술","성진술","수정 마법"}[i],string.Join(" → ",First[i]),SkillTargetRule.LivingEnemy,range:i<2?3:20,chain:1);
            for(int n=0;n<3;n++)
            {
                var two=Add("g2"+n,Second[i][n],"독립 3단 콤보 / 자원 처리는 루트 규칙 우선",SkillTargetRule.LivingEnemy,range:i<2?(n==1?15:3):20,chain:2);
                if(ComboStagger(job.index,2,n)>0)two.description+=" / 무력화 강화(초기안) · 카운터 아님";
                if(job.index==6&&n==1)two.description+=" / 중거리15m · 직접 피해80% 초기안 / 거리에 따른 추가 감소·자원 감소 없음";
                if(job.index==8&&n<2)two.description+=n==0?" / 조준 방향 직선 관통 폭1.2m 초기안":" / 조준 방향60도 확산 · 직접 피해80% 초기안";
                if((job.index==9||job.index==10||job.index==11)&&n==1)two.description+=" / 반경3m 범위 · 직접 피해80% 초기안";
                if(job.index==9&&n==0)two.description+=" / 발사 위치 기준 직선 관통 폭1.2m 초기안";
                if(i==4)two.castTime=1.5f;
                bool ally=n==2&&(i==0||i==4||i==5);
                var three=Add("a"+n,Third[i][n],ally?"지정 동료 보호 / 자원 불필요":"수동 보조 공격 / 추가타 자원 재귀 없음",ally?SkillTargetRule.LivingAlly:SkillTargetRule.LivingEnemy,12,ally?25:i<2&&n==0?3:20);
                if(i==1&&n==2)three.maximumCharges=2;
                if(i==1&&n<2)
                {
                    three.costs=new[]{new ResourceCost{resourceId=job.Resource,amount=n==0?10:20}};
                    three.description=n==0?"제자리 지상 강습 / 전류10 소비 초기안 / 체공·무적 없음":"추가 창격 / 전류20 소비 / 자동 뇌전격과 별도";
                }
                var four=Add("c"+n,Fourth[i][n],n==0?"신호 중 정면 적중 카운터 / 자동 위치 이동 없음":n==1?"무력화 / 카운터 대체 아님":"자신·지정 동료 보호 / 카운터 대체 아님",n==2?SkillTargetRule.LivingAlly:SkillTargetRule.LivingEnemy,10,n==2?25:i<2?3:15);
                four.usableWhileCasting=true;
                four.isCounter=n==0;
                bool tacticalAlly=(i==2&&n==1)||(i==3&&n==2)||(i==4&&n==2);
                bool tacticalEnemy=n==1&&(i==0||i==1||i==4||i==5);
                Add("f"+n,Tactics[i][n],i==5&&n==2?"현재 위치 저장 → F 재입력 귀환 / 유효한 지상 경로 필요 / 초기안 보관8초·재사용16초":i==1&&n==2?"현재 위치 씨앗 설치 → F 조기 기폭 / R 폭풍창과 독립 / 초기안 지속6초·재사용16초":n==0?"직접 자원 보충 / 상한 초과 보상 없음":"유한 전술 지원 / 자동 타격 재귀 없음",tacticalAlly?SkillTargetRule.LivingAlly:tacticalEnemy?SkillTargetRule.LivingEnemy:SkillTargetRule.None,(i==5||i==1)&&n==2?.25f:16,25);
            }
            var q=Add("q",new[]{"연막 외투","폭풍의 눈","사냥꾼의 외투","바람의 장막","별빛 장막","유리 장막"}[i],"개인 피해 감소 / 자원·공격 강화·기믹 해제 없음",SkillTargetRule.Self,18);q.usableWhileCasting=true;
            var move=Add("m",new[]{"사각 스텝","라이트닝 대시","전술 구르기","윈드 워커","성간 워프","거울 걸음"}[i],"지상 이동 / 기본 무적·장전·공격 자원 보상 없음",SkillTargetRule.Self,8,0);
            move.handlerId="legacy.dash";move.usableWhileCasting=true;move.maximumCharges=i==1?2:1;
            move.movement=new JobMovementDefinition{distance=4,duration=.2f,preserveCast=i==4};
            if(i!=1){move.chargeTraitId=Mobile;move.chargeTraitBonus=1;move.chargeTraitCooldownScale=1.25f;}
            var v=Add("v",new[]{"영겁의 절명","천뢰 강림","은빛 세례","사계의 종언","성좌의 종언","만화경 종막"}[i],"별도 필살기 게이지100 / 일반 자원·쿨타임 초기화·복제 없음",SkillTargetRule.LivingEnemy,.25f,25);
            v.costs=new[]{new ResourceCost{resourceId=Gauge(job),amount=100}};
            var loadouts=new List<CharacterLoadoutDefinition>();
            foreach(var item in new[]{("g2",CharacterSlot.Gcd2,"2 콤보"),("a",CharacterSlot.OffGcd1,"3 보조"),("c",CharacterSlot.Counter,"4 역할"),("f",CharacterSlot.OffGcd2,"F 전술")})
                loadouts.Add(new CharacterLoadoutDefinition{id=Id(job,"loadout."+item.Item1),name=item.Item3,slot=item.Item2,choices=Enumerable.Range(0,3).Select(n=>Id(job,item.Item1+n)).ToArray()});
            for(int r=0;r<3;r++)
            {
                var identity=Add("r"+(r+1),Roots[i][r],RootText[i][r],SkillTargetRule.None,.25f,25);
                if(i==0&&r==1){identity.usesGcd=true;identity.gcd=2;}
                d.routes[r].name=Routes[i][r];d.routes[r].identitySkillId=identity.id;d.routes[r].unlockSkills=new[]{identity.id};
                d.routes[r].capacityOverrides=i==0&&r==1?new[]{new ResourceCost{resourceId=job.Resource,amount=3}}:Array.Empty<ResourceCost>();
                for(int n=0;n<2;n++)Add("core"+(r+1)+n,Traits[i][r*2+n],TraitText[i][r*2+n]+" / 수치 초기안",SkillTargetRule.None);
                loadouts.Add(new CharacterLoadoutDefinition{id=Id(job,"loadout.core"+(r+1)),name="핵심 특성",routeId=d.routes[r].id,slot=0,choices=new[]{Core(job,r+1,0),Core(job,r+1,1)}});
            }
            // Tempest's variants are compatible-skill choices, NOT route-locked.
            if(i==1)
            {
                Add("core10.a0","연속 강습 · 3 뇌전 강습","3번2충전 / 비용·피해60% 초기안 / R에는 미적용",SkillTargetRule.None);
                Add("core11.a0","잔류 뇌운 · 3 뇌전 강습","3번 강습 피해 일부를 짧은 지면 효과로 분배 / R에는 미적용",SkillTargetRule.None);
                Add("core11.a2","잔류 뇌운 · 3 천뢰 투창","3번 투창 피해 일부를 짧은 지면 효과로 분배 / R에는 미적용",SkillTargetRule.None);
                Add("core30.f2","유영 폭풍 · F 씨앗","1 완주로 F 씨앗만 이동 / R 폭풍창에는 미적용",SkillTargetRule.None);
                Add("core31.f2","압축 폭풍 · F 씨앗","F 주기 피해를 유한 기폭 예산으로 저장 / R 폭풍창에는 미적용",SkillTargetRule.None);
                skills.First(s=>s.id==Core(job,1,1)).name+=" · R 뇌룡 강습";
                var all=Enumerable.Range(1,3).SelectMany(r=>Enumerable.Range(0,2).Select(n=>Core(job,r,n))).Concat(new[]{Id(job,"core10.a0"),Id(job,"core11.a0"),Id(job,"core11.a2"),Id(job,"core30.f2"),Id(job,"core31.f2")}).ToArray();
                foreach(var l in loadouts.Where(l=>l.slot==0))l.choices=l.choices.Concat(all).Distinct().ToArray();
                foreach(var s in skills.Where(s=>s.id==Core(job,3,0)||s.id==Core(job,3,1)))s.name+=" · R 폭풍창";
            }
            d.skills=skills.ToArray();d.loadouts=loadouts.ToArray();
            if(i==1)
            {
                var assault=d.Skill(Id(job,"r1"));assault.cooldown=12;assault.coreChargeTraitId=Core(job,1,0);assault.coreChargeBonus=1;
                var dive=d.Skill(Id(job,"a0"));dive.coreChargeTraitId=Id(job,"core10.a0");dive.coreChargeBonus=1;
                d.Skill(Core(job,1,0)).name="연속 강습 · R 뇌룡 강습";
                d.Skill(Core(job,1,0)).description="R2충전·전류30·회당 피해 감소 초기안 / 3번에는 미적용";
            }
            if(i==1)d.Skill(Core(job,2,1)).description="전광 추격 전류20→60 / 큰 추가 창격 / 호환되는 3번에만 적용 / 위력 초기안";
            if(i==5)
            {
                d.Skill(Core(job,3,0)).description="거울 최대1면 / 단순한 반사 경로 · 반사 피해160/무력화60 초기안";
                d.Skill(Core(job,3,1)).description="거울 최대2면 / 실제 두 번째 반사 구간 적중 시 피해220/무력화80 초기안 · 한 대상 중복 피해 없음";
            }
            if(i==3)
            {
                d.Skill(Core(job,1,0)).description="직사 예약 구간3초 / 합류당 R 추가 피해50 초기안";
                d.Skill(Core(job,1,1)).description="R 풍압30 / 합류 최대1개 소비·나머지 보관 / 기본 피해180+합류80 초기안";
            }
            d.slots=new[]{("g1",CharacterSlot.Gcd1),("g20",CharacterSlot.Gcd2),("a0",CharacterSlot.OffGcd1),("c0",CharacterSlot.Counter),("q",CharacterSlot.Job1),("m",CharacterSlot.Movement),("r1",CharacterSlot.Identity),("f0",CharacterSlot.OffGcd2),("v",CharacterSlot.Ultimate)}
                .Select(x=>new CharacterSlotDefinition{slot=x.Item2,skillId=Id(job,x.Item1),initiallyUnlocked=x.Item2!=CharacterSlot.Identity&&x.Item2!=CharacterSlot.Ultimate})
                .Concat(new[]{new CharacterSlotDefinition{slot=CharacterSlot.Potion,skillId=job.SkillId("p")}}).ToArray();
            d.unlocks=new[]{new CharacterUnlockDefinition{id=job.Id+".stage3",afterStage=3,slot=CharacterSlot.Ultimate,choices=new[]{Id(job,"v")}}};
            string[] aux=i==1?new[]{"tempest.aux.chain","tempest.aux.capacity","tempest.aux.reach","tempest.aux.duration","tempest.aux.mark","tempest.aux.guard"}:new[]{Reach,Mobile,Guard,Response,Support,Recovery};
            string[] names=i==1?new[]{"연계 축전","여분 축전지","이탈 연계","장기 기류","표식 보존","보호 기류"}:new[]{"사거리 조정","기동 예비","방어 여유","대응 전문","지원 지속","회복 호흡"};
            d.auxiliaryTraits=aux.Select((id,n)=>new CharacterAuxiliaryDefinition{id=id,name=names[n],description="기본 역할 보완 / 추가 키·무제한 자원 생성 없음 / 수치 초기안"}).ToArray();
            if(i!=1)
            {
                d.auxiliaryTraits.First(t=>t.id==Reach).description="선택2 사거리+3m / 해당 기술 피해95% 초기안 / 카운터·설치 범위 불변";
                d.auxiliaryTraits.First(t=>t.id==Mobile).description="Space 최대 충전+1 / 충전 재사용 시간25% 증가 초기안 / 공격 자원 획득 없음";
                d.auxiliaryTraits.First(t=>t.id==Guard).description="Q 피해 감소 지속4→6초 / 재사용 시간25% 증가 초기안";
                d.auxiliaryTraits.First(t=>t.id==Support).description="F의 표식·보호·이동 지원 시간+50% 초기안 / 발동권·위력·자원 불변";
                d.auxiliaryTraits.First(t=>t.id==Recovery).description="Q 사용 후6초 받는 회복+15% 초기안 / 저체력·피격 조건 없음";
                d.auxiliaryTraits.First(t=>t.id==Response).description="선택4만 강화:카운터 공격 후딜0.3→0.15초 / 무력화100→125 / 보호6→9초 초기안 · 신호·정면·재사용 불변";
            }
            if(i==1)d.auxiliaryTraits.First(t=>t.id=="tempest.aux.capacity").description="전류 상한100→120 초기안 / 장착 자체로 전류를 채우지 않음";
            if(i==1)
            {
                d.auxiliaryTraits.First(t=>t.id=="tempest.aux.chain").description="1 완주 전류+10 초기안 / 가속 중 추가분도 절반";
                d.auxiliaryTraits.First(t=>t.id=="tempest.aux.reach").description="Space 성공 후8초 안의 다음1·2 사거리 최소10m 초기안 / 자원 보상 없음";
                d.auxiliaryTraits.First(t=>t.id=="tempest.aux.duration").description="폭풍창·씨앗 수명+5초 초기안 / 압축 저장 상한 불변";
                d.auxiliaryTraits.First(t=>t.id=="tempest.aux.mark").description="협공 표식 시간+3초 초기안 / 동료별 발동권 수 불변";
                d.auxiliaryTraits.First(t=>t.id=="tempest.aux.guard").description="R 전류 소비 후 자기 보호4% 초기안 / 재발동12초 / 회수는 발동 안 함";
            }
            if(i==0)
            {
                d.Skill(Core(job,2,0)).description="춤의 박자 상한2 / 짧은 종결 피해+25% 초기안";
                d.Skill(Core(job,2,1)).description="박자 상한3 / 3박 피해+30%, 1·2박 피해-20% 초기안";
            }
            d.resourceCapacity=(state,id,basis)=>ResourceCapacity(job,state,id,basis);
            d.buildCompatibility=s=>BuildCompatibility(job,s);
            CharacterRosterData.ApplyDashProfile(d,job.index);
            d.migrateSnapshot=s=>Migrate(job,s);return d;
        }
        private static string BuildCompatibility(RosterJob job,CharacterStateSnapshot state)
        {
            string Selected(CharacterSlot slot)=>state.selections.FirstOrDefault(s=>s.slot==slot)?.skillId;
            if(job.index==7&&state.coreTrait==Core(job,2,1)&&Selected(CharacterSlot.OffGcd1)!=Id(job,"a1"))
                return "집중 방출은 3번 전광 추격을 장착해야 선택할 수 있습니다";
            if(job.index==7)
            {
                if(state.coreTrait==Core(job,1,1)&&Selected(CharacterSlot.Identity)!=Id(job,"r1"))return "R 잔류 뇌운은 뇌룡 강습가 필요합니다";
                foreach(string key in new[]{"a0","a2"})
                    if(state.coreTrait==Id(job,"core11."+key)&&Selected(CharacterSlot.OffGcd1)!=Id(job,key))return "잔류 뇌운 적용 대상으로 선택한 3번 기술을 장착해야 합니다";
                if(state.coreTrait==Core(job,1,0)&&Selected(CharacterSlot.Identity)!=Id(job,"r1"))return "R 연속 강습은 뇌룡 강습가 필요합니다. 3번 적용은 별도 선택하세요";
                if(state.coreTrait==Id(job,"core10.a0")&&Selected(CharacterSlot.OffGcd1)!=Id(job,"a0"))return "3번 연속 강습은 뇌전 강습이 필요합니다";
                if((state.coreTrait==Core(job,3,0)||state.coreTrait==Core(job,3,1))&&Selected(CharacterSlot.Identity)!=Id(job,"r3"))
                    return "R 폭풍창 적용 특성은 태풍의 핵 루트에서 선택할 수 있습니다. F 씨앗 적용은 별도 선택하세요";
                if((state.coreTrait==Id(job,"core30.f2")||state.coreTrait==Id(job,"core31.f2"))&&Selected(CharacterSlot.OffGcd2)!=Id(job,"f2"))
                    return "F 적용 특성은 폭풍의 씨앗을 장착해야 합니다";
            }
            if(state.auxiliaryTraits.Contains(Support))
            {
                string f=Selected(CharacterSlot.OffGcd2);
                if(f==Id(job,"f0")||job.index==11&&f==Id(job,"f2"))
                    return "지원 지속은 F에 동료 표식·보호·이동 지원이 있어야 장착할 수 있습니다";
            }
            return "";
        }
        private static float ResourceCapacity(RosterJob job,CharacterStateSnapshot state,string id,float basis)
        {
            if(id!=job.Resource)return basis;
            if(job.index==6&&state.coreTrait==Core(job,2,0))return 2;
            var traits=state.auxiliaryTraits;
            // Empty auxiliary arrays restore the catalog defaults: Tempest's first
            // two choices include spare capacity, just like a newly created build.
            if(job.index==7&&(traits==null||traits.Length==0||traits.Contains("tempest.aux.capacity")))return basis+20;
            return basis;
        }
        public static CharacterStateSnapshot Migrate(RosterJob job,CharacterStateSnapshot source)
        {
            if(source?.jobId!=job.Id||source.selections==null||!source.selections.Any(s=>s?.slot==CharacterSlot.Job2))return source;
            var legacy=new CharacterRuntimeState(CharacterRosterData.Definition(job));
            if(!legacy.Restore(source))return null;
            // A fully rested old checkpoint must stay rested after capacity changes.
            // Any pending cast/combo/cooldown keeps the conservative partial-charge migration.
            bool rested=source.castSkillId.Length==0&&source.gcd==0&&source.combo1==1&&source.combo2==1&&source.comboTime1==0&&source.comboTime2==0&&
                source.skills.All(s=>s.recharge==0&&s.charges==legacy.MaximumCharges(s.id));
            var d=Build(job);var next=source.Copy();
            next.selections=d.slots.Select(s=>new JobSlotSelection{slot=s.slot,skillId=s.skillId}).ToArray();
            var route=d.routes.FirstOrDefault(r=>r.id==source.routeId);
            if(route!=null){next.selections.First(s=>s.slot==CharacterSlot.Identity).skillId=route.identitySkillId;next.coreTrait=d.loadouts.First(l=>l.routeId==route.id).choices[0];}
            next.auxiliaryTraits=d.auxiliaryTraits.Take(2).Select(t=>t.id).ToArray();
            next.skills=d.skills.Select(s=>next.skills.FirstOrDefault(old=>old.id==s.id)??new JobSkillState{id=s.id,
                charges=s.maximumCharges+(s.chargeTraitId==Mobile&&next.auxiliaryTraits.Contains(Mobile)?s.chargeTraitBonus:0)+(rested&&s.coreChargeTraitId==next.coreTrait?s.coreChargeBonus:0),
                recharge=!rested&&s.coreChargeTraitId==next.coreTrait&&s.coreChargeBonus>0?s.cooldown:0,
                rechargeTotal=s.cooldown*(s.chargeTraitId==Mobile&&next.auxiliaryTraits.Contains(Mobile)?s.chargeTraitCooldownScale:1)}).ToArray();
            foreach(var selection in next.selections)
            {
                var oldSelection=source.selections.FirstOrDefault(s=>s.slot==selection.slot);if(oldSelection==null)continue;
                var prior=source.skills.First(s=>s.id==oldSelection.skillId);var current=next.skills.First(s=>s.id==selection.skillId);
                if(prior.recharge>0)
                {
                    current.charges=Math.Min(current.charges,prior.charges);
                    current.recharge=current.rechargeTotal=Math.Max(prior.recharge,current.recharge);
                }
            }
            // Old gauges have different semantics (hands, beats, currents). Never
            // reinterpret them as a prepared attack or a free new ultimate.
            next.resources=d.resources.Select(r=>new JobResourceValue{id=r.id,value=0}).ToArray();
            next.unlocked=d.slots.Where(s=>s.initiallyUnlocked).Select(s=>s.skillId).Concat(route==null?Array.Empty<string>():new[]{route.identitySkillId}).ToArray();
            int[] kept=Enumerable.Range(0,source.chosenRules.Length).Where(n=>source.chosenRules[n]==job.Id+".stage3").ToArray();
            next.chosenRules=kept.Select(n=>source.chosenRules[n]).ToArray();next.chosenRewardKeys=kept.Select(n=>source.chosenRewardKeys[n]).ToArray();
            if(kept.Length>0)next.unlocked=next.unlocked.Union(new[]{Id(job,"v")}).ToArray();
            next.automaticChoices=source.automaticChoices.Any(v=>v==job.SkillId("v1")||v==job.SkillId("v2"))?new[]{Id(job,"v")}:Array.Empty<string>();
            next.castSkillId="";next.castTarget=0;next.castRemaining=next.castTotal=0;next.castAllowsMovement=false;
            next.combo1=next.combo2=1;next.comboTime1=next.comboTime2=0;return next;
        }
    }
}
