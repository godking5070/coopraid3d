using System;
using System.Collections.Generic;
using System.Linq;
namespace RaidRogue
{
    [Serializable] public sealed class JobResourceValue { public string id; public float value; }
    [Serializable] public sealed class JobSkillState { public string id; public int charges; public float recharge, rechargeTotal; }
    [Serializable] public sealed class JobSlotSelection { public CharacterSlot slot; public string skillId; }
    [Serializable] public sealed class JobBuildEffect { public string id; public int stacks; public float magnitude; }
    [Serializable] public sealed class CharacterStateSnapshot
    {
        public int version=1, clearedStage, combo1=1, combo2=1;
        public string jobId, routeId="", castSkillId=""; public int castTarget;
        public float gcd, gcdTotal, comboTime1, comboTime2, castRemaining, castTotal;
        public JobResourceValue[] resources=Array.Empty<JobResourceValue>();
        public JobSkillState[] skills=Array.Empty<JobSkillState>();
        public JobSlotSelection[] selections=Array.Empty<JobSlotSelection>();
        public string[] unlocked=Array.Empty<string>(), chosenRules=Array.Empty<string>();
        public JobBuildEffect[] effects=Array.Empty<JobBuildEffect>();
        public CharacterStateSnapshot Copy()
        {
            var c=(CharacterStateSnapshot)MemberwiseClone();
            c.resources=Array.ConvertAll(resources,x=>new JobResourceValue {id=x.id,value=x.value});
            c.skills=Array.ConvertAll(skills,x=>new JobSkillState {id=x.id,charges=x.charges,recharge=x.recharge,rechargeTotal=x.rechargeTotal});
            c.selections=Array.ConvertAll(selections,x=>new JobSlotSelection {slot=x.slot,skillId=x.skillId});
            c.effects=Array.ConvertAll(effects,x=>new JobBuildEffect {id=x.id,stacks=x.stacks,magnitude=x.magnitude});
            c.unlocked=(string[])unlocked.Clone();c.chosenRules=(string[])chosenRules.Clone();return c;
        }
    }
    public readonly struct CharacterUseContext
    {
        public readonly bool Alive, CanAct, ValidTarget, Moving;
        public readonly float BoundaryDistance;
        public CharacterUseContext(bool alive,bool canAct,bool target,float distance,bool moving=false)
        { Alive=alive;CanAct=canAct;ValidTarget=target;BoundaryDistance=distance;Moving=moving; }
    }
    // Mutable per actor, pure transaction state. Damage/healing/movement remain in existing effect handlers.
    public sealed class CharacterRuntimeState
    {
        public CharacterDefinition Definition { get; }
        private CharacterStateSnapshot _data;
        public CharacterStateSnapshot Snapshot => _data.Copy();
        public CharacterRuntimeState(CharacterDefinition definition)
        {
            if (definition==null) throw new ArgumentNullException(nameof(definition));
            var errors=CharacterDefinitionValidation.Check(new[]{definition}); if(errors.Length>0)throw new ArgumentException(string.Join(";",errors));
            Definition=definition; _data=new CharacterStateSnapshot {jobId=definition.id}; Reset(CharacterReset.NewRun);
        }
        public CharacterSkillDefinition Selected(CharacterSlot slot)
        { var selected=Array.Find(_data.selections,s=>s.slot==slot);return selected==null?null:Definition.Skill(selected.skillId); }
        public bool Unlocked(CharacterSlot slot) { var skill=Selected(slot);return skill!=null&&Array.IndexOf(_data.unlocked,skill.id)>=0; }
        public float Resource(string id) => Array.Find(_data.resources,r=>r.id==id)?.value??0;
        public bool Gain(string id,float amount)
        {
            var definition=Array.Find(Definition.resources,r=>r.id==id);var state=Array.Find(_data.resources,r=>r.id==id);
            if(definition==null||state==null||!CharacterDefinitionValidation.Finite(amount)||amount<0||definition.integer&&amount!=Math.Floor(amount))return false;
            state.value=Math.Min(definition.maximum,state.value+amount);return true;
        }
        private Dictionary<string,float> Costs(CharacterSkillDefinition skill)
        { var result=new Dictionary<string,float>(); foreach(var c in skill.costs){result.TryGetValue(c.resourceId,out float v);result[c.resourceId]=v+c.amount;}return result; }
        public bool Validate(CharacterSlot slot,CharacterUseContext context,out string reason,bool completing=false)
        {
            reason=""; var skill=Selected(slot);
            if(!Definition.playable)reason="미구현 직업입니다";
            else if(!context.Alive||!context.CanAct)reason="현재 행동할 수 없습니다";
            else if(skill==null||!Unlocked(slot))reason="잠긴 슬롯입니다";
            else if(!skill.implemented)reason="아직 구현되지 않은 스킬입니다";
            else if(!context.ValidTarget)reason="유효한 대상이 필요합니다";
            else if(!CharacterDefinitionValidation.Finite(context.BoundaryDistance)||context.BoundaryDistance>skill.range+.0001f)reason="대상이 사거리 밖에 있습니다";
            else if(skill.usesGcd&&_data.gcd>0)reason="공유 GCD 중입니다";
            else if(!completing&&_data.castSkillId.Length>0&&(!skill.usableWhileCasting||skill.castTime>0))reason="시전 중입니다";
            else if(Array.Find(_data.skills,s=>s.id==skill.id).charges<1)reason="충전/쿨타임 대기 중입니다";
            else if(skill.castTime>0&&skill.cancelOnMovement&&context.Moving)reason="이동 중 시전할 수 없습니다";
            else foreach(var cost in Costs(skill))
            {
                var resource=Array.Find(Definition.resources,r=>r.id==cost.Key);
                if(resource==null||!CharacterDefinitionValidation.Finite(cost.Value)||cost.Value<0||resource.integer&&cost.Value!=Math.Floor(cost.Value)||Resource(cost.Key)<cost.Value){reason="직업 자원이 부족하거나 비용이 잘못되었습니다";break;}
            }
            return reason.Length==0;
        }
        public bool TryUse(CharacterSlot slot,CharacterUseContext context,int target,Func<CharacterSkillDefinition,bool> apply,out string reason)
        {
            if(!Validate(slot,context,out reason))return false;
            var skill=Selected(slot);
            if(skill.castTime>0){_data.castSkillId=skill.id;_data.castTarget=target;_data.castRemaining=_data.castTotal=skill.castTime;return true;}
            return Commit(skill,apply,out reason);
        }
        private bool Commit(CharacterSkillDefinition skill,Func<CharacterSkillDefinition,bool> apply,out string reason)
        {
            reason="";
            if(apply==null||!apply(skill)){reason="스킬 효과를 적용할 수 없습니다";return false;}
            foreach(var c in Costs(skill)) Array.Find(_data.resources,r=>r.id==c.Key).value-=c.Value;
            var state=Array.Find(_data.skills,s=>s.id==skill.id);state.charges--;
            if(state.recharge<=0){state.recharge=state.rechargeTotal=skill.cooldown;}
            if(skill.cooldown==0)state.charges=skill.maximumCharges;
            if(skill.usesGcd)_data.gcd=_data.gcdTotal=skill.gcd;
            if(skill.comboChain==1){_data.combo1=_data.combo1%3+1;_data.comboTime1=_data.combo1==1?0:skill.comboWindow;}
            if(skill.comboChain==2){_data.combo2=_data.combo2%3+1;_data.comboTime2=_data.combo2==1?0:skill.comboWindow;}
            return true;
        }
        public void Tick(float seconds,bool moved=false,bool alive=true)
        {
            if(!CharacterDefinitionValidation.Finite(seconds)||seconds<0)return;
            _data.gcd=Math.Max(0,_data.gcd-seconds);
            _data.comboTime1=Math.Max(0,_data.comboTime1-seconds);if(_data.comboTime1==0)_data.combo1=1;
            _data.comboTime2=Math.Max(0,_data.comboTime2-seconds);if(_data.comboTime2==0)_data.combo2=1;
            foreach(var state in _data.skills)
            {
                var skill=Definition.Skill(state.id); if(state.charges>=skill.maximumCharges)continue;
                state.recharge-=seconds;
                while(state.recharge<=0&&state.charges<skill.maximumCharges){state.charges++;state.recharge+=skill.cooldown;if(skill.cooldown<=0){state.charges=skill.maximumCharges;break;}}
                if(state.charges==skill.maximumCharges)state.recharge=0;
            }
            if(_data.castSkillId.Length==0)return;
            var cast=Definition.Skill(_data.castSkillId);
            if(!alive||moved&&cast.cancelOnMovement){CancelCast();return;}
            _data.castRemaining=Math.Max(0,_data.castRemaining-seconds);
        }
        public bool FinishCast(CharacterUseContext context,Func<CharacterSkillDefinition,bool> apply,out string reason)
        {
            reason="시전 완료 전입니다";
            if(_data.castSkillId.Length==0||_data.castRemaining>0)return false;
            string id=_data.castSkillId;var slot=Array.Find(_data.selections,s=>s.skillId==id);var skill=Definition.Skill(id);
            bool valid=slot!=null&&Validate(slot.slot,context,out reason,true);CancelCast();
            return valid&&Commit(skill,apply,out reason);
        }
        public void CancelCast(){_data.castSkillId="";_data.castTarget=0;_data.castRemaining=_data.castTotal=0;}
        public void ClearedStage(int stage){if(stage>0)_data.clearedStage=Math.Max(_data.clearedStage,stage);}
        public bool ChooseRoute(string routeId,bool beforeDeparture)
        {
            var route=Array.Find(Definition.routes,r=>r.id==routeId);
            if(!beforeDeparture||!Definition.playable||route==null||_data.routeId.Length>0)return false;
            _data.routeId=routeId;
            var identity=Array.Find(_data.selections,s=>s.slot==CharacterSlot.Identity);
            if(identity!=null&&!string.IsNullOrEmpty(route.identitySkillId))identity.skillId=route.identitySkillId;
            _data.unlocked=_data.unlocked.Union(route.unlockSkills).Union(string.IsNullOrEmpty(route.identitySkillId)?Array.Empty<string>():new[]{route.identitySkillId}).ToArray();return true;
        }
        public bool ChooseSkill(string ruleId,string skillId)
        {
            var rule=Array.Find(Definition.unlocks,r=>r.id==ruleId);
            if(!Definition.playable||rule==null||_data.clearedStage<rule.afterStage||Array.IndexOf(rule.choices,skillId)<0||Array.IndexOf(_data.chosenRules,ruleId)>=0)return false;
            Array.Find(_data.selections,s=>s.slot==rule.slot).skillId=skillId;
            _data.unlocked=_data.unlocked.Union(new[]{skillId}).ToArray();_data.chosenRules=_data.chosenRules.Concat(new[]{ruleId}).ToArray();return true;
        }
        public void SetBuildEffect(string id,int stacks,float magnitude)
        {
            if(string.IsNullOrWhiteSpace(id)||stacks<0||!CharacterDefinitionValidation.Finite(magnitude))return;
            var effects=_data.effects.Where(e=>e.id!=id).ToList();if(stacks>0)effects.Add(new JobBuildEffect{id=id,stacks=stacks,magnitude=magnitude});_data.effects=effects.ToArray();
        }
        public void Reset(CharacterReset reason)
        {
            if(reason==CharacterReset.NewRun)
            {
                _data=new CharacterStateSnapshot{jobId=Definition.id,selections=Array.ConvertAll(Definition.slots,s=>new JobSlotSelection{slot=s.slot,skillId=s.skillId}),unlocked=Definition.slots.Where(s=>s.initiallyUnlocked).Select(s=>s.skillId).ToArray()};
            }
            var previous=_data.resources;
            _data.resources=Array.ConvertAll(Definition.resources,r=>new JobResourceValue{id=r.id,value=reason==CharacterReset.NewRun||(reason==CharacterReset.Retry?r.resetOnRetry:r.resetOnStage)?r.initial:Array.Find(previous,v=>v.id==r.id)?.value??r.initial});
            _data.skills=Array.ConvertAll(Definition.skills,s=>new JobSkillState{id=s.id,charges=s.maximumCharges,rechargeTotal=s.cooldown});
            _data.gcd=_data.gcdTotal=_data.comboTime1=_data.comboTime2=0;_data.combo1=_data.combo2=1;CancelCast();
        }
        public bool Restore(CharacterStateSnapshot snapshot)
        {
            if(snapshot==null||snapshot.version!=1||snapshot.jobId!=Definition.id||snapshot.resources==null||snapshot.skills==null||snapshot.selections==null||snapshot.effects==null||snapshot.unlocked==null||snapshot.chosenRules==null)return false;
            if(snapshot.clearedStage<0||snapshot.unlocked.Distinct().Count()!=snapshot.unlocked.Length||snapshot.chosenRules.Distinct().Count()!=snapshot.chosenRules.Length||snapshot.effects.Select(e=>e?.id).Distinct().Count()!=snapshot.effects.Length)return false;
            if(snapshot.chosenRules.Any(id=>!Definition.unlocks.Any(r=>r.id==id)))return false;
            if(snapshot.resources.Length!=Definition.resources.Length||snapshot.skills.Length!=Definition.skills.Length||snapshot.selections.Length!=Definition.slots.Length)return false;
            if(snapshot.resources.Select(r=>r?.id).Distinct().Count()!=snapshot.resources.Length||snapshot.skills.Select(s=>s?.id).Distinct().Count()!=snapshot.skills.Length||snapshot.selections.Select(s=>s?.slot).Distinct().Count()!=snapshot.selections.Length)return false;
            foreach(var r in snapshot.resources){var d=Array.Find(Definition.resources,v=>v.id==r?.id);if(d==null||!CharacterDefinitionValidation.Finite(r.value)||r.value<0||r.value>d.maximum||d.integer&&r.value!=Math.Floor(r.value))return false;}
            foreach(var s in snapshot.skills){var d=Definition.Skill(s?.id);if(d==null||s.charges<0||s.charges>d.maximumCharges||!Nonnegative(s.recharge)||!Nonnegative(s.rechargeTotal))return false;}
            foreach(var s in snapshot.selections)if(s==null||Definition.Skill(s.skillId)==null||!Definition.slots.Any(d=>d.slot==s.slot&&d.skillId==s.skillId)&&!Definition.unlocks.Any(r=>r.slot==s.slot&&Array.IndexOf(r.choices,s.skillId)>=0)&&!(s.slot==CharacterSlot.Identity&&Definition.routes.Any(r=>r.identitySkillId==s.skillId)))return false;
            if(snapshot.unlocked.Any(s=>Definition.Skill(s)==null)||snapshot.effects.Any(e=>e==null||string.IsNullOrEmpty(e.id)||e.stacks<0||!CharacterDefinitionValidation.Finite(e.magnitude)))return false;
            if(snapshot.castSkillId==null||snapshot.castSkillId.Length>0&&Definition.Skill(snapshot.castSkillId)==null||snapshot.routeId==null||snapshot.routeId.Length>0&&!Definition.routes.Any(r=>r.id==snapshot.routeId))return false;
            if(!Nonnegative(snapshot.gcd)||!Nonnegative(snapshot.gcdTotal)||!Nonnegative(snapshot.comboTime1)||!Nonnegative(snapshot.comboTime2)||!Nonnegative(snapshot.castRemaining)||!Nonnegative(snapshot.castTotal)||snapshot.combo1<1||snapshot.combo1>3||snapshot.combo2<1||snapshot.combo2>3)return false;
            _data=snapshot.Copy();return true;
        }
        private static bool Nonnegative(float n)=>CharacterDefinitionValidation.Finite(n)&&n>=0;
        // Legacy controllers remain the one source of combat timing; the framework exposes their confirmed state.
        public void MirrorLegacy(float gcd,float gcdTotal,int combo1,int combo2,float time1,float time2,string cast,float remaining,float total)
        { _data.gcd=gcd;_data.gcdTotal=gcdTotal;_data.combo1=combo1;_data.combo2=combo2;_data.comboTime1=time1;_data.comboTime2=time2;_data.castSkillId=string.IsNullOrEmpty(cast)?"":Selected(CharacterSlot.Gcd1).id;_data.castRemaining=remaining;_data.castTotal=total; }
        public void MirrorCharges(CharacterSlot slot,int charges,float remaining,float total)
        {var skill=Selected(slot);if(skill==null)return;var state=Array.Find(_data.skills,s=>s.id==skill.id);state.charges=Math.Max(0,Math.Min(skill.maximumCharges,charges));state.recharge=Math.Max(0,remaining);state.rechargeTotal=Math.Max(0,total);}
        public void MirrorCooldown(CharacterSlot slot,float remaining,float total)
        { var skill=Selected(slot);if(skill==null)return;var s=Array.Find(_data.skills,v=>v.id==skill.id);s.charges=remaining>0?0:skill.maximumCharges;s.recharge=remaining;s.rechargeTotal=total; }
    }
}
