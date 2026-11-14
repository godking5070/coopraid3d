using System;
using System.Collections.Generic;
using UnityEngine;

namespace RaidRogue
{
    public sealed partial class NineKeyDamageKit
    {
        private void SignalTempestResponse(Transform ally)
        {
            if(Job.index!=7||Route!=2||ally==transform||!ValidAlly(ally)||Has("tempest.response.gate"))return;
            Effect("tempest.response","tempest.response",ally,1,6);
            Effect("tempest.response.gate","gate",transform,1,12);
        }
        private void RespondToTempest(Transform ally)
        {
            if(Job.index!=7||Route!=2||ally==transform||!ValidAlly(ally))return;
            var request=_effects.Find("tempest.response@"+CharacterKit.Id(ally),Now);
            if(request==null)return;
            _effects.Remove(request.id); // Claim before resource/shield callbacks.
            Effect("tempest.response.gate","gate",transform,1,12);
            _runtime.Gain(Job.Resource,5); // Not halved by accelerated combo income.
            Shield(ally,"tempest.response",Power*.03f);
        }
        public static void OnDirectHealing(Transform source,Transform target)
        {
            if(RaidCombatAuthority.Replica||StageManager.PlayerSkillsSuspended||!CombatRange.LivingPartyMember(source)||!CombatRange.LivingPartyMember(target)||source.GetComponent<CharacterActor>()?.OwnsHealerInput!=true)return;
            foreach(var owner in PartyLife.OrderedMembers())
            {
                var kit=owner.GetComponent<NineKeyDamageKit>();
                if(kit?.Active!=true||!CombatRange.LivingPartyMember(owner))continue;
                kit.Bind();kit.RespondToTempest(source);
            }
        }
        public static float SupportBonus(Transform recipient,string kind)
        {
            if(recipient==null)return 0;float result=0;
            foreach(var owner in PartyLife.OrderedMembers())
            {
                var kit=owner.GetComponent<NineKeyDamageKit>();if(kit?.Active!=true||!CombatRange.LivingPartyMember(owner))continue;
                foreach(var effect in kit._effects.Data.effects)
                    if(effect.target==CharacterKit.Id(recipient)&&effect.kind==kind&&effect.end>kit.Now)result=Mathf.Max(result,effect.value);
            }
            return result;
        }
        public static float Incoming(Transform recipient,float damage,CombatHitContext hit)
        {
            if(hit.Defense==CombatDefenseType.Bypass||hit.Kind==CombatDamageKind.System||hit.Kind==CombatDamageKind.Transfer||hit.Kind==CombatDamageKind.Deferred)return damage;
            var values=new Dictionary<string,float>();
            foreach(var owner in PartyLife.OrderedMembers())
            {
                var kit=owner.GetComponent<NineKeyDamageKit>();if(kit?.Active!=true||!CombatRange.LivingPartyMember(owner))continue;
                foreach(var effect in kit._effects.Data.effects)
                {
                    if(effect.target!=CharacterKit.Id(recipient)||effect.kind!="reduction"||effect.end<=kit.Now)continue;
                    values.TryGetValue(effect.skill,out float prior);values[effect.skill]=Mathf.Max(prior,effect.value);
                }
            }
            foreach(float reduction in values.Values)damage*=1-Mathf.Clamp01(reduction);return damage;
        }
        public static void OnDirectAttack(Transform source,Transform target,string origin,CombatDamageKind kind)
        {
            if(RaidCombatAuthority.Replica||StageManager.PlayerSkillsSuspended||kind!=CombatDamageKind.Direct||!CombatRange.LivingPartyMember(source)||!CombatRange.LivingEnemy(target)||source.GetComponent<CharacterActor>()?.OwnsDirectSkillHit(origin)!=true)return;
            foreach(var owner in PartyLife.OrderedMembers())
            {
                var kit=owner.GetComponent<NineKeyDamageKit>();if(kit?.Active!=true||!CombatRange.LivingPartyMember(owner))continue;
                kit.Bind();int targetId=CharacterKit.Id(target);
                kit.RespondToTempest(source);
                var duet=kit._effects.Find("duet@"+targetId,kit.Now);
                if(duet!=null)
                {
                    // Different categories, not hit count: one caster and one
                    // different teammate. One teammate cannot supply both halves.
                    duet.ticks|=owner==source?1:2;
                    if(duet.ticks==3)
                    {
                        kit._effects.Remove(duet.id);
                        kit.Attack(kit.Skill("f1"),target,duet.value,kit.Job.index==7?60:0,CombatDamageKind.Extra,launched:true);
                    }
                }
                if(owner==source)continue;
                var right=kit._effects.Find("coop@"+CharacterKit.Id(source),kit.Now);
                if(right!=null&&right.skill=="coop:"+targetId)
                {
                    kit._effects.Remove(right.id);
                    // The existing mark owns this followup. The caster moving
                    // away does not revoke it; living/immune checks still apply.
                    kit.Attack(kit.Skill("r"+kit.Route),target,right.value,0,CombatDamageKind.Extra,launched:true);
                }
                var fracture=kit._effects.Find("fracture@"+targetId,kit.Now);
                if(fracture!=null)
                {
                    kit._effects.Remove(fracture.id);
                    CombatCounterTarget.Ensure(target).AddStagger(fracture.value);
                }
            }
        }
    }
}
