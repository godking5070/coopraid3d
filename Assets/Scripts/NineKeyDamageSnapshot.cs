using System;
using System.Linq;
using UnityEngine;

namespace RaidRogue
{
    public sealed partial class NineKeyDamageKit
    {
        public string PartyPreparationText()
        {
            if(!Active||Job.index!=10||Route!=1)return "";
            Bind();var state=_meteor?.Snapshot;
            float delay=MeteorCompletionDelay(state);
            if(delay<0)return "";
            return "메테오 "+(state.casting?"완성 ":"중단 · 재개 후 ")+delay.ToString("0.0")+"초"+
                (delay>state.remaining?" · 보존 시간 부족":"");
        }
        // This is the uninterrupted time still required, not the expiration
        // timer. Reading a replica's preparation must never resume or spend it.
        private static float MeteorCompletionDelay(DamageMeteorSnapshot state)
        {
            if(state==null||state.segments<=0||state.completed<0||state.completed>=state.segments||
                !DamageTime.Positive(state.remaining)||!DamageTime.Positive(state.segmentTime)||
                !DamageTime.Nonnegative(state.progress)||state.progress>=state.segmentTime)return -1;
            return (state.segments-state.completed)*state.segmentTime-state.progress;
        }
        public float DisplayResource(string id,float fallback)
        {if(!Active)return fallback;Bind();return Job.index==8&&Route==1&&id==Job.Resource?_hands.Total:fallback;}
        public float DisplayMaximum(string id,float fallback)
        {if(!Active)return fallback;Bind();return Job.index==8&&Route==1&&id==Job.Resource?_hands.Snapshot.capacity*2:fallback;}
        public string ComboLabel(CharacterSkillDefinition skill,int step)
        {
            if(skill==null)return "빈 슬롯";
            if(IsFourth(skill))return "사단연격 · 전류20";
            if(skill.comboChain==1)return NineKeyDamageData.First[Job.index-6][Math.Max(0,Math.Min(2,step-1))];
            return skill.name+" · "+step+"타";
        }
        public string StatusText()
        {
            string text=RouteStatusText();if(!Active||_runtime==null)return text;
            if(Job.index==7&&_actor.Selected(CharacterSlot.OffGcd2)?.id==NineKeyDamageData.Id(Job,"f2"))
                text+="\nF 씨앗: "+(_seed.Active?"조기 기폭 / "+_seed.Snapshot.remaining.ToString("0.0")+"초":_seed.Snapshot.cooldown>0?"재사용 "+_seed.Snapshot.cooldown.ToString("0.0")+"초":"설치 가능");
            if(Job.index==11&&_actor.Selected(CharacterSlot.OffGcd2)?.id==NineKeyDamageData.Id(Job,"f2"))
                text+="\nF 귀환: "+(_return.Active?"저장 위치로 복귀 / "+_return.Snapshot.remaining.ToString("0.0")+"초":_return.Snapshot.cooldown>0?"재사용 "+_return.Snapshot.cooldown.ToString("0.0")+"초":"위치 저장 가능");
            return text;
        }
        private string RouteStatusText()
        {
            if(!Active)return "";Bind();if(_runtime==null)return "";
            if(Route==0)return "루트를 선택하세요";
            string Seconds(float t)=>Math.Max(0,t).ToString("0.0")+"초";
            string Target(int id){var t=Resolve(id);return t!=null?t.name:"대상 없음";}
            string extra=_fourthReady?" · 2: 사단연격 / 전류20":"";
            if(Job.index==6)
            {
                if(Route==1)
                {
                    var target=TargetingManager.Instance?.AttackTarget;
                    string facing="공격 대상 없음";
                    if(CombatRange.LivingEnemy(target))
                    {
                        var boss=target.GetComponent<BossController>();
                        var zone=boss!=null?boss.GetPositionalZone(transform.position,out _):PositionalZone.Rear;
                        facing=boss==null?"방향 없음 · 완주 정보 획득":zone==PositionalZone.Front?
                            "정면 · 정보는 측면/후방 완주로 획득":zone==PositionalZone.Flank?"측면 · 완주 정보 획득":"후방 · 완주 정보 획득";
                        facing+=" · R "+(_weakness.Count(CharacterKit.Id(target))>0?"강화 가능":"기본 처형");
                    }
                    return "약점 정보 "+_weakness.Snapshot.targets.Length+"/"+(Core(1,0)?1:Core(1,1)?3:2)+" · "+
                        string.Join(" / ",_weakness.Snapshot.targets.Distinct().Select(t=>Target(t)+"×"+_weakness.Count(t)))+
                        " · R: 인장2\n"+facing;
                }
                if(Route==2)return "R: 현재 박자 전부 소비 · 직전 콤보 종결";
                var s=_seal.Snapshot;return _seal.Active?Target(s.target)+" · "+(s.trigger==DamageSealTrigger.CastEnd?"시전 종료":"패턴 이동 종료")+" 대기 "+Seconds(s.remaining):"R: 인장2 · 짧게 종언부 / 길게 추적부";
            }
            if(Job.index==7)
            {
                if(Route==1)return (GroundAssaultActive?"강습 진행":"R: 지상 강습 · 전류"+(Core(1,0)?30:50))+extra;
                if(Route==2){var e=_effects.Find("acceleration@"+CharacterKit.Id(transform),Now);return (e==null?"R: 가속 시작 · 전류80":"가속 "+Seconds(e.end-Now))+extra;}
                return "폭풍창 "+_spears.Snapshot.spears.Length+"/2 · "+string.Join(" / ",_spears.Snapshot.spears.Select(s=>"충전"+s.charge+"/3 "+Seconds(s.remaining)))+" · R 짧게 설치40 / 길게 회수"+extra;
            }
            if(Job.index==8)
            {
                if(Route==1)
                {
                    var s=_hands.Snapshot;
                    string Hand(int hand)=>hand==1?"왼손":hand==2?"오른손":"장전 불가(가득 참)";
                    return "왼손 "+s.left+"/"+s.capacity+" · 오른손 "+s.right+"/"+s.capacity+" · "+
                        (s.reloading!=0?Hand(s.reloading)+" 장전 "+Seconds(s.remaining):
                        "R 짧게 "+Hand(_hands.ReloadTarget(false))+" / 길게 "+Hand(_hands.ReloadTarget(true)))+" · 재사용 "+Seconds(s.cooldown);
                }
                if(Route==2)return _grenade.Active?"자동 기폭 "+Seconds(_grenade.Snapshot.remaining)+" · 1 완주 후 "+Seconds(_grenade.Predicted(1))+" / 2 완주 후 "+Seconds(_grenade.Predicted(2)):"R: 유탄 부착 · 마탄3";
                var shot=_snipe.Snapshot;return _snipe.Active?Target(shot.target)+" · "+(shot.ready?"R: 저격 발사 "+shot.shots+"회 / "+Seconds(shot.remaining):"조준 "+Seconds(shot.aiming)+" · 마탄 예약") :"R: 조준 시작 · 마탄"+(Core(3,1)?5:3);
            }
            if(Job.index==9)
            {
                if(Route==1)
                {
                    var arrows=_arrows.Snapshot;float window=Core(1,0)?3:2.5f;
                    return "합류 "+arrows.convergence+"/3 · "+string.Join(" / ",arrows.pending.Select(p=>
                        Target(p.target)+" 탄착 "+Seconds(p.remaining)+(p.straightStep>0?" · 직사 예약됨":
                        p.remaining<=window?" · 지금 직사 예약 가능":" · 예약까지 "+Seconds(p.remaining-window))))+
                        " · R: 풍압"+(Core(1,1)?"30 · 합류 최대1":"40 · 합류 전부");
                }
                if(Route==2)
                {
                    var burst=_burst.Snapshot;
                    return _burst.Active?"R: 후속 묶음 "+burst.bundles+"회 · "+(burst.precise?"강화 준비 · ":"")+Seconds(burst.remaining)+
                        (burst.inputDelay>0?" · 재입력까지 "+Seconds(burst.inputDelay):" · 추가 풍압0"):"R: 연사 시작 · 풍압50";
                }
                var s=_season.Snapshot;return new[]{"봄 보호","여름 협공","가을 무력화","겨울 이동"}[s.season]+" · "+Seconds(s.remaining)+" · R 발도 "+(Core(3,1)?50:40)+" / 길게 유예20"+(s.delayed?" 사용됨":" 가능");
            }
            if(Job.index==10)
            {
                if(Route==1){var s=_meteor.Snapshot;return _meteor.Active?"영창 "+s.completed+"/"+s.segments+" · "+(s.casting?"현재 구간 "+Seconds(s.segmentTime-s.progress):"R: 이어서 영창")+" · 보존 "+Seconds(s.remaining):"R: 분할 영창 · 마력60 예약";}
                if(Route==2){var s=_instant.Snapshot;return _instant.Active?"즉발권 "+s.rights+"회 · "+(s.enabled?"소비 ON":"소비 OFF")+" · "+Seconds(s.remaining)+" · R: 전환0"+(s.toggleDelay>0?" / 대기 "+Seconds(s.toggleDelay):""):"R: 즉발권 준비 · 마력"+(Core(2,0)?40:60)+" · 재사용 "+Seconds(s.cooldown);}
                return _gravity.Active?"중력장 "+(_gravity.Snapshot.expanded?"확산":"압축")+" · "+Seconds(_gravity.Snapshot.remaining)+" · R: 전환 / 2 완주: 추적":"R: 중력장 설치 · 마력60";
            }
            if(Job.index==11)
            {
                if(Route==1)return "기록 "+string.Join(" / ",_memory.Snapshot.records.Select(r=>(Skill(r.key)?.name??r.key)+" "+Seconds(r.remaining)))+" · "+(_memory.Snapshot.locked?"잠금":"갱신 중")+" · R 재연 파편"+(Core(1,0)?1:2)+" / 길게 잠금 전환";
                if(Route==2)return _projection.Active?"원격 투영 "+Seconds(_projection.Snapshot.remaining)+" · 2 완주 자원 없음":"R 짧게 투영1 / 길게 참수3"+(_projection.Snapshot.usedInCombo?" · 현재 2 완주 수급 없음":"")+(_projection.Snapshot.empoweredExecution&&Core(2,1)?" · 참수 강화 준비":"");
                return "거울 "+_prism.Snapshot.mirrors.Length+"/"+(Core(3,0)?1:2)+" · "+string.Join(" / ",_prism.Snapshot.mirrors.Select(m=>(m.id==_prism.Snapshot.firstId?"★":"")+m.id+":"+Seconds(m.remaining)))+" · R 드래그: 배치1 / 기존 거울 조정0"+_prismHint;
            }
            return "루트를 선택하세요";
        }
        public string Capture()
        {
            if(!Active)return "";Bind();if(_runtime==null)return "";
            return JsonUtility.ToJson(new DamageCombatSnapshot
            {
                job=Job.Id,route=Route,core=_actor.Snapshot.coreTrait,fourthReady=_fourthReady,capturedAt=Now,
                meteorTarget=_meteor.Active?_meteorTarget:0,resources=_resources.Snapshot,
                weakness=_weakness.Snapshot,seal=_seal.Snapshot,grenade=_grenade.Snapshot,hands=_hands.Snapshot,
                snipe=_snipe.Snapshot,meteor=_meteor.Snapshot,burst=_burst.Snapshot,season=_season.Snapshot,
                instant=_instant.Snapshot,memory=_memory.Snapshot,projection=_projection.Snapshot,arrows=_arrows.Snapshot,
                anchor=_return.Snapshot,seed=_seed.Snapshot,preparation=_tempestPreparation.Snapshot,
                spears=_spears.Snapshot,gravity=_gravity.Snapshot,prism=_prism.Snapshot,effects=_effects.Data.Copy(),
                pendingSeals=_pendingSeals.Select(s=>new DamagePendingSeal{target=s.Target,trigger=s.Trigger,enhanced=s.Enhanced}).ToArray()
            });
        }
        public bool Restore(string json,bool rebaseClock=false)
        {
            if(!Active||string.IsNullOrEmpty(json)||json.Length>262144)return false;Bind();
            DamageCombatSnapshot v;
            try{v=JsonUtility.FromJson<DamageCombatSnapshot>(json);}catch(ArgumentException){return false;}
            if(!DamageCombatState.TryRestore(_runtime,Job,v,out var c,rebaseClock?Now:-1))return false;
            // No gameplay callback on restore: use the already deducted framework
            // balance; deferred completions are processed only by the host tick.
            _resources=c.resources;_weakness=c.weakness;_seal=c.seal;_grenade=c.grenade;_hands=c.hands;
            _snipe=c.snipe;_meteor=c.meteor;_burst=c.burst;_season=c.season;_instant=c.instant;
            _memory=c.memory;_projection=c.projection;_arrows=c.arrows;_return=c.anchor;_seed=c.seed;
            _tempestPreparation=c.preparation;_spears=c.spears;_gravity=c.gravity;_prism=c.prism;
            _effects.Restore(c.effects.Data);_fourthReady=v.fourthReady;_meteorTarget=v.meteorTarget;_deferredOrigin="";
            _pendingSeals.Clear();
            foreach(var s in v.pendingSeals)_pendingSeals.Add(new DamageSealResolution(s.target,s.trigger,s.enhanced));
            return true;
        }
    }
}
