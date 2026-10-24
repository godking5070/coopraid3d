using UnityEngine;

namespace RaidRogue
{
    public sealed partial class RaidNetworkSession
    {
        private void StepReplacementAI()
        {
            foreach (var avatar in _avatars.Values)
            {
                if (!avatar.Automated || avatar.IsBot) continue;
                var pc = avatar.GetComponent<PlayerController>(); var combat = pc.combatController;
                var input = new RaidMoveInput { MotionVersion = avatar.MotionVersion };
                var boss = StageManager.Instance.boss;
                if (BattlePhase != RaidBattlePhase.Combat || pc.IsDead || pc.IsStunned || pc.IsMovementLocked || !CombatRange.LivingEnemy(boss.transform))
                { StepHostAvatar(avatar, input); continue; }
                if (pc.Potion != null && pc.HpRatio <= .4f && pc.Potion.CanUse) AutomaticAction(avatar, 6, avatar.transform);
                var character=CharacterActor.Ensure(pc.gameObject); bool aegis=character.Definition.id==AegisPaladinData.JobId;
                float range = combat.PrimaryMeleeRange;
                float arena = Mathf.Max(0, pc.arenaBoundaryRadius - pc.combatRadius - .5f);
                var add = BossAddSpawner.Instance?.GetPriorityAddForBot((PartyBotRole)(avatar.Role - 1));
                Transform enemy = add != null ? add.transform : boss.transform;
                bool mechanic = PartyAiTactics.PartyMechanic(pc.transform, avatar.Role, boss, out Vector3 desired);
                if (!mechanic) mechanic = PartyAiTactics.HazardDestination(pc.transform, boss, range, out desired);
                bool intercept = avatar.Role == 3 && boss.IsTetherActive && CombatRange.LivingPartyMember(boss.TetherPrimaryTarget) && boss.TetherPrimaryTarget != avatar.transform;
                if (intercept) { desired = Vector3.Lerp(boss.transform.position, boss.TetherPrimaryTarget.position, .52f); mechanic = true; }
                var extensions=aegis?pc.GetComponent<AegisExtensions>():null;
                if(mechanic)extensions?.CancelChannel();
                var resurrection = pc.GetComponent<BattleResurrection>();
                Transform wounded = PartyAiTactics.LowestHealth(out float lowest);
                Transform corpse = avatar.Role == 2 && resurrection.Cooldown <= 0 ? resurrection.ChooseBotTarget() : null;
                bool danger = boss.ActiveTelegraph != null && boss.ActiveTelegraph.IsPointInsideXZ(pc.transform.position);
                bool resurrect = avatar.Role == 2 && !mechanic && lowest > .25f && (corpse != null || resurrection.IsCasting);
                if (!mechanic)
                {
                    if (resurrect)
                    {
                        desired = pc.transform.position;
                        if (corpse != null && BattleResurrection.FlatDistance(pc.transform.position, corpse.position) > resurrection.Settings.range)
                            PartyAiTactics.ResurrectionApproach(pc.transform, corpse, resurrection.Settings.range, arena, boss.ActiveTelegraph, out desired);
                    }
                    else if (avatar.Role == 1) desired = PartyAiTactics.TankPosition(pc.transform, boss, aegis?AegisPaladinData.MeleeRange:range, 4.8f);
                    else if (avatar.Role == 3) desired = CombatRange.AttackPosition(pc.transform, enemy, enemy == boss.transform ? -boss.transform.forward : boss.transform.position - enemy.position, range);
                    else desired = boss.transform.position + (avatar.Role == 2 ? new Vector3(-6.8f, 0, -6.5f) : new Vector3(6.6f, 0, -6));
                }
                if (resurrection.IsCasting && (!resurrect || danger)) resurrection.Cancel();
                // Ordinary casting stays still; mechanic avoidance retains priority and cancels through real movement.
                if ((!mechanic && combat.IsCastingSpell) || (resurrect && resurrection.IsCasting)) desired = pc.transform.position;
                if(!mechanic&&extensions?.Channeling==true)desired=pc.transform.position;
                desired = CombatRange.ClampToArena(desired, arena);
                Vector3 delta = desired - pc.transform.position; delta.y = 0;
                Vector3 direction = delta.magnitude > .12f ? Vector3.ClampMagnitude(delta / Mathf.Max(.01f, pc.NetworkMoveSpeed * RaidWire.Step), 1) : Vector3.zero;
                input.X = direction.x; input.Z = direction.z;
                if(aegis&&delta.magnitude>character.MovementSettings(pc.dashSettings).distance&&(mechanic||CombatRange.BoundaryDistance(pc.transform,enemy)>range+4))input.Dash=true;
                StepHostAvatar(avatar, input); // Collision, forced movement, facing, dash CD and tether swept path are shared.
                if (pc.IsBeingRescued || pc.IsBeingKnockedBack) continue;
                if (resurrect)
                { if (corpse != null && !pc.IsMovingXZ && !resurrection.IsCasting && BattleResurrection.FlatDistance(pc.transform.position, corpse.position) <= resurrection.Settings.range) AutomaticAction(avatar, 5, corpse); continue; }
                if (resurrection.IsCasting) continue;
                if(aegis)
                {
                    if(extensions?.Channeling==true)continue;
                    if(!mechanic)UsePaladinExtensions(avatar,character,enemy,boss,wounded,lowest,danger);
                    if(extensions?.Channeling==true)continue;
                    var paladin=pc.GetComponent<AegisPaladin>();
                    bool defenseNeeded=lowest<.65f||boss.IsCasting;
                    Vector3 toward=boss.transform.position-pc.transform.position;toward.y=0;
                    bool facing=Vector3.Dot(pc.transform.forward,toward.normalized)>=Mathf.Cos(AegisRouteData.GuardAngle*.5f*Mathf.Deg2Rad);
                    bool identity=character.RouteId==AegisRouteData.Bulwark&&defenseNeeded||character.RouteId==AegisRouteData.Sun&&!mechanic&&CombatRange.InRange(pc.transform,enemy,AegisRouteData.SunRange)||character.RouteId==AegisRouteData.Mirror&&!danger&&facing&&boss.GuardableAttackImminent(pc.transform,AegisRouteData.GuardDuration*.8f);
                    if(identity&&!paladin.IdentityBlocked)AutomaticCharacterAction(avatar,CharacterSlot.Identity,pc.transform);
                    if(pc.HpRatio<.75f||boss.IsCasting)AutomaticCharacterAction(avatar,CharacterSlot.Job1,pc.transform);
                    if(boss.ResolveLivingAggroTarget()!=pc.transform||pc.currentShield<pc.maxHp*.05f)AutomaticCharacterAction(avatar,CharacterSlot.Job2,enemy);
                    AutomaticCharacterAction(avatar,CharacterSlot.OffGcd1,enemy);
                    if(enemy.GetComponent<CombatCounterTarget>()?.WindowOpen==true)AutomaticCharacterAction(avatar,CharacterSlot.Counter,enemy);
                    var state=character.Snapshot;
                    var slot=state.combo1>1?CharacterSlot.Gcd1:state.combo2>1?CharacterSlot.Gcd2:pc.currentShield<pc.maxHp*.1f?CharacterSlot.Gcd2:CharacterSlot.Gcd1;
                    AutomaticCharacterAction(avatar,slot,enemy);continue;
                }
                if (avatar.Role == 2)
                {
                    if (!danger && boss.CastProgress >= .8f && boss.ActiveTelegraph != null)
                    {
                        var rescue = PartyLife.OrderedMembers().Find(m => m != pc.transform && CombatRange.LivingPartyMember(m) && boss.ActiveTelegraph.IsPointInsideXZ(m.position));
                        if (rescue != null) AutomaticAction(avatar, 4, rescue);
                    }
                    if (lowest < .65f) AutomaticAction(avatar, 3, pc.transform);
                    if (wounded != null && lowest < .85f) AutomaticAction(avatar, 2, wounded);
                    else AutomaticAction(avatar, 1, enemy);
                }
                else
                {
                    if (pc.HpRatio < .6f || intercept) AutomaticAction(avatar, 3, pc.transform);
                    if (avatar.Role == 1 && (add != null && add.aggroTarget != pc.transform || boss.ResolveLivingAggroTarget() != pc.transform)) AutomaticAction(avatar, 4, enemy);
                    if (avatar.Role == 4 && !combat.IsCastingSpell) AutomaticAction(avatar, 4, enemy);
                    if (avatar.Role == 3) AutomaticAction(avatar, 4, enemy);
                    AutomaticAction(avatar, avatar.Role == 3 ? 2 : avatar.Role == 4 && pc.IsMovingXZ ? 2 : 1, enemy);
                    if (avatar.Role == 1 && !CombatRange.InRange(pc.transform, enemy, range)) AutomaticAction(avatar, 2, enemy);
                }
            }
        }
        private void UsePaladinExtensions(NetworkRaidAvatar avatar,CharacterActor actor,Transform enemy,BossController boss,Transform wounded,float lowest,bool danger)
        {
            var pc=avatar.GetComponent<PlayerController>();var r=actor.Selected(CharacterSlot.Active);var f=actor.Selected(CharacterSlot.OffGcd2);
            if(r?.id==AegisExtensionData.Spear)AutomaticCharacterAction(avatar,CharacterSlot.Active,enemy);
            else if(r?.id==AegisExtensionData.Ground&&!danger&&(Vector3.Distance(pc.transform.position,enemy.position)<=AegisExtensionData.GroundRadius||lowest<.75f))
                AutomaticCharacterAction(avatar,CharacterSlot.Active,pc.transform);
            else if(r?.id==AegisExtensionData.Wings&&!danger&&boss.IsCasting)
            {
                bool covered=PartyLife.OrderedMembers().Exists(m=>m!=pc.transform&&CombatRange.LivingPartyMember(m)&&AegisExtensionData.InCone(m.position.x-pc.transform.position.x,m.position.z-pc.transform.position.z,AegisExtensionData.WingsRadius,pc.transform.eulerAngles.y+180,AegisExtensionData.WingsAngle));
                if(covered){AutomaticCharacterAction(avatar,CharacterSlot.Active,pc.transform);if(pc.GetComponent<AegisExtensions>().Channeling)return;}
            }
            if(f?.id==AegisExtensionData.Intervention&&wounded!=null&&wounded!=pc.transform&&lowest<.55f&&pc.HpRatio>.55f&&!(boss.ActiveTelegraph?.IsPointInsideXZ(wounded.position)??false))
                AutomaticCharacterAction(avatar,CharacterSlot.OffGcd2,wounded);
            else if(f?.id==AegisExtensionData.Brand||f?.id==AegisExtensionData.Retribution&&pc.currentShield>0)
                AutomaticCharacterAction(avatar,CharacterSlot.OffGcd2,enemy);
            if(!danger)AutomaticCharacterAction(avatar,CharacterSlot.Ultimate,enemy);
        }
        private void AutomaticCharacterAction(NetworkRaidAvatar avatar,CharacterSlot slot,Transform target)
        {
            var actor=CharacterActor.Ensure(avatar.gameObject);var state=actor.Snapshot;
            var skill=actor.Definition.Skill(System.Array.Find(state.selections,s=>s.slot==slot).skillId);
            var timing=System.Array.Find(state.skills,s=>s.id==skill.id);
            if(!actor.Available(slot,out _)||timing.charges<1||skill.usesGcd&&state.gcd>0||skill.target==SkillTargetRule.LivingEnemy&&(!CombatRange.LivingEnemy(target)||!CombatRange.InRange(avatar.transform,target,actor.EffectiveRange(skill))))return;
            if(skill.target==SkillTargetRule.LivingAlly&&(!CombatRange.LivingPartyMember(target)||!CombatRange.InRange(avatar.transform,target,actor.EffectiveRange(skill))))return;
            if(skill.handlerId=="aegis.extension"&&actor.GetComponent<AegisExtensions>()?.Unavailable(skill,target)!="")return;
            foreach(var cost in skill.costs)if((System.Array.Find(state.resources,r=>r.id==cost.resourceId)?.value??0)<cost.amount)return;
            ExecuteAction(avatar.Id,new RaidActionRequest{actor=avatar.Id,epoch=BattleEpoch,skill=10,slot=(int)slot,jobId=actor.Definition.id,target=CombatId(target)},true);
        }
        private void AutomaticAction(NetworkRaidAvatar avatar, int skill, Transform target)
        {
            avatar.CombatTarget = target;
            RaidCombatAuthority.ExecutingActor = avatar.Id;
            bool valid;
            try { valid = skill > 4 || avatar.GetComponent<PlayerCombatController>().ValidateCombatSkill(skill, out _); }
            finally { RaidCombatAuthority.ExecutingActor = 0; }
            if (valid) ExecuteAction(avatar.Id, new RaidActionRequest { actor = avatar.Id, epoch = BattleEpoch, skill = skill, target = CombatId(target) }, true);
        }
    }
}
