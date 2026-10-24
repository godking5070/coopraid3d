using System;
using System.Collections.Generic;
using System.Linq;
namespace RaidRogue
{
    public enum SkillTestOperation { UnlockImplemented, FillResources, RefreshJobCooldowns }
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
        public string runId="local", chapterId=CharacterRewardKey.CurrentChapter;
        public string[] clearedEncounters=Array.Empty<string>(), chosenRewardKeys=Array.Empty<string>(), automaticChoices=Array.Empty<string>();
        public JobBuildEffect[] effects=Array.Empty<JobBuildEffect>();
        public CharacterStateSnapshot Copy()
        {
            var c=(CharacterStateSnapshot)MemberwiseClone();
            c.resources=Array.ConvertAll(resources,x=>new JobResourceValue {id=x.id,value=x.value});
            c.skills=Array.ConvertAll(skills,x=>new JobSkillState {id=x.id,charges=x.charges,recharge=x.recharge,rechargeTotal=x.rechargeTotal});
            c.selections=Array.ConvertAll(selections,x=>new JobSlotSelection {slot=x.slot,skillId=x.skillId});
            c.effects=Array.ConvertAll(effects,x=>new JobBuildEffect {id=x.id,stacks=x.stacks,magnitude=x.magnitude});
            c.unlocked=(string[])unlocked.Clone();c.chosenRules=(string[])chosenRules.Clone();
            c.clearedEncounters=(string[])clearedEncounters.Clone();c.chosenRewardKeys=(string[])chosenRewardKeys.Clone();c.automaticChoices=(string[])automaticChoices.Clone();return c;
        }
    }
    public readonly struct CharacterUseContext
    {
        public readonly bool Alive, CanAct, ValidTarget, Moving;
        public readonly float BoundaryDistance, EffectiveRange, GcdScale;
        public CharacterUseContext(bool alive,bool canAct,bool target,float distance,bool moving=false,float effectiveRange=-1,float gcdScale=1)
        { Alive=alive;CanAct=canAct;ValidTarget=target;BoundaryDistance=distance;Moving=moving;EffectiveRange=effectiveRange;GcdScale=gcdScale; }
    }
    // Mutable per actor, pure transaction state. Damage/healing/movement remain in existing effect handlers.
    public sealed class CharacterRuntimeState
    {
        public CharacterDefinition Definition { get; }
        private CharacterStateSnapshot _data;
        public CharacterStateSnapshot Snapshot => _data.Copy();
        public string RouteId=>_data.routeId;
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
        // The only live caller is CharacterActor's editor-only, local-authority gate.
        // Edit actor state, never shared definitions, progression, or combat effects.
        public bool ApplySkillTest(SkillTestOperation operation,out int affected)
        {
            affected=0;
            if(!Definition.playable)return false;
            switch(operation)
            {
                case SkillTestOperation.UnlockImplemented:
                    var unlocked=new HashSet<string>(_data.unlocked);
                    foreach(var selection in _data.selections)
                    {
                        var skill=Definition.Skill(selection.skillId);
                        if(skill==null||!skill.implemented||string.IsNullOrEmpty(skill.handlerId))continue;
                        // A route identity must still belong to the confirmed departure route.
                        if(selection.slot==CharacterSlot.Identity&&Definition.routes.Length>0&&
                            !Definition.routes.Any(r=>r.id==_data.routeId&&r.identitySkillId==skill.id))continue;
                        if(unlocked.Add(skill.id))affected++;
                    }
                    _data.unlocked=unlocked.ToArray();return true;
                case SkillTestOperation.FillResources:
                    foreach(var resource in _data.resources)
                    {
                        float maximum=Array.Find(Definition.resources,r=>r.id==resource.id).maximum;
                        if(resource.value<maximum){resource.value=maximum;affected++;}
                    }
                    return true;
                case SkillTestOperation.RefreshJobCooldowns:
                    // Legacy controllers and movement/potion components own their own timers.
                    // Never cancel a cast or reset active transformation/guard/deferred damage.
                    if(Definition.legacyRole>0||_data.castSkillId.Length>0)return false;
                    string movement=Selected(CharacterSlot.Movement)?.id,potion=Selected(CharacterSlot.Potion)?.id;
                    foreach(var state in _data.skills)
                    {
                        var skill=Definition.Skill(state.id);
                        if(!skill.implemented||state.id==movement||state.id==potion)continue;
                        if(state.charges<skill.maximumCharges||state.recharge>0)affected++;
                        state.charges=skill.maximumCharges;state.recharge=0;state.rechargeTotal=skill.cooldown;
                    }
                    if(_data.gcd>0)affected++;
                    _data.gcd=_data.gcdTotal=0;return true;
                default:return false;
            }
        }
        private Dictionary<string,float> Costs(CharacterSkillDefinition skill)
        { var result=new Dictionary<string,float>(); foreach(var c in skill.costs){result.TryGetValue(c.resourceId,out float v);result[c.resourceId]=v+c.amount;}return result; }
        public bool Validate(CharacterSlot slot,CharacterUseContext context,out string reason,bool completing=false)
        {
            reason=""; var skill=Selected(slot);
            if(!Definition.playable)reason="미구현 직업입니다";
            else if(!context.Alive||!context.CanAct)reason="현재 행동할 수 없습니다";
            else if(skill==null)reason="잠긴 슬롯입니다";
            else if(!skill.implemented)reason="아직 구현되지 않은 스킬입니다";
            else if(!Unlocked(slot))reason="잠긴 슬롯입니다";
            else if(!context.ValidTarget)reason="유효한 대상이 필요합니다";
            else if(!CharacterDefinitionValidation.Finite(context.BoundaryDistance)||context.BoundaryDistance>(context.EffectiveRange>=0?context.EffectiveRange:skill.range)+.0001f)reason="대상이 사거리 밖에 있습니다";
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
            return Commit(skill,context,apply,out reason);
        }
        private bool Commit(CharacterSkillDefinition skill,CharacterUseContext context,Func<CharacterSkillDefinition,bool> apply,out string reason)
        {
            reason="";
            if(apply==null||!apply(skill)){reason="스킬 효과를 적용할 수 없습니다";return false;}
            foreach(var c in Costs(skill)) Array.Find(_data.resources,r=>r.id==c.Key).value-=c.Value;
            var state=Array.Find(_data.skills,s=>s.id==skill.id);state.charges--;
            if(state.recharge<=0){state.recharge=state.rechargeTotal=skill.cooldown;}
            if(skill.cooldown==0)state.charges=skill.maximumCharges;
            if(skill.usesGcd)_data.gcd=_data.gcdTotal=skill.gcd*(CharacterDefinitionValidation.Finite(context.GcdScale)&&context.GcdScale>0?context.GcdScale:1);
            if(skill.comboChain==1){_data.combo1=_data.combo1%3+1;_data.comboTime1=_data.combo1==1?0:skill.comboWindow;}
            if(skill.comboChain==2){_data.combo2=_data.combo2%3+1;_data.comboTime2=_data.combo2==1?0:skill.comboWindow;}
            return true;
        }
        public void Tick(float seconds,bool moved=false,bool alive=true,float gcdRate=1)
        {
            if(!CharacterDefinitionValidation.Finite(seconds)||seconds<0)return;
            if(!CharacterDefinitionValidation.Finite(gcdRate)||gcdRate<=0)gcdRate=1;
            _data.gcd=Math.Max(0,_data.gcd-seconds*gcdRate);
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
            return valid&&Commit(skill,context,apply,out reason);
        }
        public void ClearOnDeath()
        {CancelCast();_data.combo1=_data.combo2=1;_data.comboTime1=_data.comboTime2=0;}
        public void ReduceCooldowns(IEnumerable<string> skillIds,float seconds)
        {
            if(!CharacterDefinitionValidation.Finite(seconds)||seconds<=0)return;
            foreach(var id in skillIds.Distinct())
            {
                var skill=Definition.Skill(id);var state=Array.Find(_data.skills,s=>s.id==id);
                // Do not grant spare charges to an already ready skill; never touch the shared GCD.
                if(skill==null||state==null||state.charges>0||state.recharge<=0)continue;
                state.recharge=Math.Max(0,state.recharge-seconds);
                if(state.recharge==0){state.charges=1;if(skill.maximumCharges>1)state.recharge=skill.cooldown;}
            }
        }
        public void CancelCast(){_data.castSkillId="";_data.castTarget=0;_data.castRemaining=_data.castTotal=0;}
        // Called only by the editor test window; does not fake stage clears, card rewards or confirmed choices.
        public bool PreviewSkillChoice(string skillId)
        {
            var rule=Array.Find(Definition.unlocks,r=>r.choices.Contains(skillId));
            if(rule==null||!Definition.playable||Definition.Skill(skillId)?.implemented!=true||_data.castSkillId.Length>0||_data.chosenRules.Contains(rule.id))return false;
            Array.Find(_data.selections,s=>s.slot==rule.slot).skillId=skillId;
            _data.unlocked=_data.unlocked.Except(rule.choices).Union(new[]{skillId}).ToArray();return true;
        }
        public void BeginRun(string run,string chapter)
        {if(!CharacterRewardKey.ValidId(run)||!CharacterRewardKey.ValidId(chapter))throw new ArgumentException("Invalid run/chapter identity");_data.runId=run;_data.chapterId=chapter;}
        public void ClearedStage(int stage)=>ClearedStage(_data.runId,_data.chapterId,stage);
        public bool ClearedStage(string run,string chapter,int stage)
        {
            if(run!=_data.runId||chapter!=_data.chapterId||stage<1)return false;
            string key=CharacterRewardKey.Stage(run,chapter,stage);if(_data.clearedEncounters.Contains(key))return false;
            _data.clearedEncounters=_data.clearedEncounters.Concat(new[]{key}).ToArray();_data.clearedStage=Math.Max(_data.clearedStage,stage);return true;
        }
        public string RewardKey(CharacterUnlockDefinition rule)=>CharacterRewardKey.Choice(_data.runId,rule.chapterId,rule.afterStage,rule.id);
        public bool Eligible(CharacterUnlockDefinition rule)=>rule!=null&&rule.chapterId==_data.chapterId&&_data.clearedEncounters.Contains(CharacterRewardKey.Stage(_data.runId,rule.chapterId,rule.afterStage));
        public CharacterUnlockDefinition[] PendingChoices()=>Definition.unlocks.Where(r=>Eligible(r)&&!_data.chosenRules.Contains(r.id)&&r.choices.All(id=>Definition.Skill(id)?.implemented==true)).ToArray();
        public bool ChooseRoute(string routeId,bool beforeDeparture)
        {
            var route=Array.Find(Definition.routes,r=>r.id==routeId);
            if(!beforeDeparture||!Definition.playable||route==null)return false;
            if(_data.routeId==routeId)return true;
            var previous=Array.Find(Definition.routes,r=>r.id==_data.routeId);
            if(previous!=null)
            {
                var removed=previous.unlockSkills.Concat(new[]{previous.identitySkillId});
                var retained=Definition.slots.Where(s=>s.initiallyUnlocked).Select(s=>s.skillId)
                    .Concat(Definition.unlocks.Where(r=>_data.chosenRules.Contains(r.id)).Select(r=>Selected(r.slot).id));
                _data.unlocked=_data.unlocked.Except(removed.Except(retained)).ToArray();
            }
            CancelCast();
            _data.routeId=routeId;
            var identity=Array.Find(_data.selections,s=>s.slot==CharacterSlot.Identity);
            if(identity!=null&&!string.IsNullOrEmpty(route.identitySkillId))identity.skillId=route.identitySkillId;
            _data.unlocked=_data.unlocked.Union(route.unlockSkills).Union(string.IsNullOrEmpty(route.identitySkillId)?Array.Empty<string>():new[]{route.identitySkillId}).ToArray();return true;
        }
        public bool ChooseSkill(string ruleId,string skillId,string rewardKey=null,bool automatic=false)
        {
            var rule=Array.Find(Definition.unlocks,r=>r.id==ruleId);
            if(!Definition.playable||!Eligible(rule)||Array.IndexOf(rule.choices,skillId)<0||Definition.Skill(skillId)?.implemented!=true||Array.IndexOf(_data.chosenRules,ruleId)>=0||rewardKey!=null&&rewardKey!=RewardKey(rule))return false;
            Array.Find(_data.selections,s=>s.slot==rule.slot).skillId=skillId;
            _data.unlocked=_data.unlocked.Except(rule.choices).Union(new[]{skillId}).ToArray();_data.chosenRules=_data.chosenRules.Concat(new[]{ruleId}).ToArray();
            _data.chosenRewardKeys=_data.chosenRewardKeys.Concat(new[]{RewardKey(rule)}).ToArray();
            if(automatic)_data.automaticChoices=_data.automaticChoices.Concat(new[]{skillId}).ToArray();return true;
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
            if(!CharacterRewardKey.ValidId(snapshot.runId)||!CharacterRewardKey.ValidId(snapshot.chapterId)||snapshot.clearedEncounters==null||snapshot.chosenRewardKeys==null||snapshot.automaticChoices==null)return false;
            if(snapshot.clearedEncounters.Distinct().Count()!=snapshot.clearedEncounters.Length||snapshot.chosenRewardKeys.Length!=snapshot.chosenRules.Length||snapshot.automaticChoices.Any(id=>Definition.Skill(id)?.implemented!=true))return false;
            foreach(string key in snapshot.clearedEncounters)
            {string prefix=snapshot.runId+"/"+snapshot.chapterId+"/";if(key==null||!key.StartsWith(prefix,StringComparison.Ordinal)||!int.TryParse(key.Substring(prefix.Length),out int n)||n<1)return false;}
            for(int i=0;i<snapshot.chosenRules.Length;i++)
            {
                var rule=Array.Find(Definition.unlocks,r=>r.id==snapshot.chosenRules[i]);
                if(rule==null||rule.chapterId!=snapshot.chapterId||snapshot.chosenRewardKeys[i]!=CharacterRewardKey.Choice(snapshot.runId,rule.chapterId,rule.afterStage,rule.id)||!snapshot.clearedEncounters.Contains(CharacterRewardKey.Stage(snapshot.runId,rule.chapterId,rule.afterStage)))return false;
                var selected=Array.Find(snapshot.selections,s=>s?.slot==rule.slot);
                if(selected==null||!rule.choices.Contains(selected.skillId)||!snapshot.unlocked.Contains(selected.skillId))return false;
            }
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
            var route=Array.Find(Definition.routes,r=>r.id==snapshot.routeId);
            var identity=Array.Find(snapshot.selections,s=>s.slot==CharacterSlot.Identity);
            if(route!=null&&identity?.skillId!=route.identitySkillId)return false;
            if(route==null&&identity!=null&&Definition.routes.Any(r=>r.identitySkillId==identity.skillId))
            {
                var initial=Array.Find(Definition.slots,s=>s.slot==CharacterSlot.Identity);
                if(initial==null||initial.skillId!=identity.skillId||!initial.initiallyUnlocked&&snapshot.unlocked.Contains(identity.skillId))return false;
            }
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
