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
                if(AiSpectatorActive && BattlePhase==RaidBattlePhase.Reward && avatar.Id==_localId &&
                    RaidApproachField.Instance?.PortalActive==true && !AiSpectatorStopOnClear)
                {
                    Vector3 toward=new Vector3(0,pc.transform.position.y,0)-pc.transform.position;toward.y=0;
                    Vector3 portalDirection=toward.magnitude>.5f?Vector3.ClampMagnitude(toward/Mathf.Max(.01f,pc.NetworkMoveSpeed*RaidWire.Step),1):Vector3.zero;
                    input.X=portalDirection.x;input.Z=portalDirection.z;StepHostAvatar(avatar,input);continue;
                }
                if (BattlePhase == RaidBattlePhase.Approach && !pc.IsDead)
                {
                    var field = RaidApproachField.Instance;
                    Vector3 destination = field.RallyPoint(avatar.Role);
                    Vector3 toRally = destination - pc.transform.position;
                    toRally.y = 0;
                    Vector3 approachDirection = toRally.magnitude > .15f ? Vector3.ClampMagnitude(toRally / Mathf.Max(.01f, pc.NetworkMoveSpeed * RaidWire.Step), 1) : Vector3.zero;
                    input.X = approachDirection.x; input.Z = approachDirection.z;
                    StepHostAvatar(avatar, input);
                    continue;
                }
                var boss = StageManager.Instance.boss;
                if (BattlePhase != RaidBattlePhase.Combat || pc.IsDead || pc.IsStunned || pc.IsMovementLocked ||
                    (pc.GetComponent<NineKeyHealerKit>()?.Active!=true&&(boss.HardTransformationActive||!CombatRange.LivingEnemy(boss.transform))) || boss.CampanellaLockHolding(pc.transform) || boss.MarionetteControlled(pc.transform))
                { StepHostAvatar(avatar, input); continue; }
                if (pc.Potion != null && pc.HpRatio <= .4f && pc.Potion.CanUse) AutomaticAction(avatar, 6, avatar.transform);
                if(avatar.Role==2&&boss.GuardianLureRescueWindow)boss.TryGuardianLureRescue(pc.transform,out _);
                var character=CharacterActor.Ensure(pc.gameObject); bool aegis=character.Definition.id==AegisPaladinData.JobId;var kit=pc.GetComponent<CharacterKit>();bool roster=kit?.Active==true;
                float range = combat.PrimaryMeleeRange;
                var activeDamage=pc.GetComponent<NineKeyDamageKit>();
                if(activeDamage?.Active==true)range=character.PrimaryRange;
                float arena = Mathf.Max(0, pc.arenaBoundaryRadius - pc.combatRadius - .5f);
                var add = BossAddSpawner.Instance?.GetPriorityAddForBot((PartyBotRole)(avatar.Role - 1));
                Transform enemy = add != null ? add.transform : boss.transform;
                if(boss.JanusEncounter&&boss.JanusAiCounterTarget(pc.transform) is Transform janusCounter)
                    enemy=janusCounter;
                bool mechanic = PartyAiTactics.PartyMechanic(pc.transform, avatar.Role, boss, out Vector3 desired);
                if (!mechanic) mechanic = PartyAiTactics.HazardDestination(pc.transform, boss, range, out desired);
                bool intercept = avatar.Role == 3 && boss.IsTetherActive && CombatRange.LivingPartyMember(boss.TetherPrimaryTarget) && boss.TetherPrimaryTarget != avatar.transform;
                if (intercept) { desired = Vector3.Lerp(boss.transform.position, boss.TetherPrimaryTarget.position, .52f); mechanic = true; }
                var extensions=aegis?pc.GetComponent<AegisExtensions>():null;
                if(mechanic)extensions?.CancelChannel();
                var resurrection = pc.GetComponent<BattleResurrection>();
                Transform wounded = PartyAiTactics.LowestHealth(out float lowest);
                input.SupportId=CombatId(wounded??avatar.transform);
                Transform corpse = avatar.Role == 2 && StageManager.Instance.IrisCoins > 0 && resurrection.Cooldown <= 0 ? resurrection.ChooseBotTarget() : null;
                bool danger = boss.ActiveTelegraph != null && boss.ActiveTelegraph.IsPointInsideXZ(pc.transform.position);
                bool resurrect = avatar.Role == 2 && !roster && pc.GetComponent<NineKeyHealerKit>()?.Active!=true && !mechanic && lowest > .25f && (corpse != null || resurrection.IsCasting);
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
                if(!mechanic&&(extensions?.Channeling==true||roster&&(kit.MovementBlocked||character.Snapshot.castSkillId.Length>0)))desired=pc.transform.position;
                if(!mechanic&&!danger&&activeDamage?.Active==true&&(activeDamage.AiPreparing||character.Snapshot.castSkillId.Length>0))desired=pc.transform.position;
                desired = CombatRange.ClampToArena(desired, arena);
                if(boss.JanusEncounter)desired=JanusNormalRules.Clamp(desired,boss.janus.arenaRadius-pc.combatRadius-.5f);
                Vector3 delta = desired - pc.transform.position; delta.y = 0;
                Vector3 direction = delta.magnitude > .12f ? Vector3.ClampMagnitude(delta / Mathf.Max(.01f, pc.NetworkMoveSpeed * RaidWire.Step), 1) : Vector3.zero;
                input.X = direction.x; input.Z = direction.z;
                if(boss.GuardianAerialDashNeeded(pc.transform))input.Dash=true;
                if(!boss.FloraEncounter&&!boss.CampanellaEncounter&&!boss.MarionetteEncounter&&!boss.JustitiaEncounter&&!boss.JanusEncounter&&(aegis||roster||CharacterSlots.IsNineKey(character.Definition))&&delta.magnitude>character.MovementSettings(pc.dashSettings).distance&&(mechanic||CombatRange.BoundaryDistance(pc.transform,enemy)>range+4))input.Dash=true;
                StepHostAvatar(avatar, input); // Collision, forced movement, facing, dash CD and tether swept path are shared.
                if (pc.IsBeingRescued || pc.IsBeingKnockedBack) continue;
                if (resurrect)
                { if (corpse != null && !pc.IsMovingXZ && !resurrection.IsCasting && BattleResurrection.FlatDistance(pc.transform.position, corpse.position) <= resurrection.Settings.range) AutomaticAction(avatar, 5, corpse); continue; }
                if (resurrection.IsCasting) continue;
                var nineTank=pc.GetComponent<NineKeyTankKit>();
                var nineHealer=pc.GetComponent<NineKeyHealerKit>();
                var nineDamage=pc.GetComponent<NineKeyDamageKit>();
                if(nineDamage?.Active==true)
                {UseNineKeyDamage(avatar,character,nineDamage,enemy,boss,wounded,danger,mechanic);continue;}
                if(nineHealer?.Active==true)
                {UseNineKeyHealer(avatar,character,nineHealer,enemy,boss,wounded,lowest,danger,mechanic);continue;}
                if(nineTank?.Active==true)
                {
                    UseNineKeyTank(avatar,character,nineTank,enemy,boss,wounded,lowest,danger,mechanic);continue;
                }
                if(roster)
                {
                    if(mechanic)kit.CancelChannel();
                    if(kit.Channeling&&!kit.CanWeaveDuringChannel)continue;
                    Transform healTarget=wounded!=null?wounded:pc.transform;
                    if(!mechanic&&lowest>.3f&&StageManager.Instance.Iris.Coins>0){var fallen=resurrection.ChooseBotTarget();if(fallen!=null)AutomaticAction(avatar,5,fallen);}
                    if(lowest<.8f||pc.HpRatio<.7f||boss.IsCasting)AutomaticCharacterAction(avatar,CharacterSlot.Job1,kit.Job.Role==2?healTarget:pc.transform);
                    if(kit.Job.Role!=2||lowest<.8f)AutomaticCharacterAction(avatar,CharacterSlot.Job2,character.Selected(CharacterSlot.Job2).target==SkillTargetRule.LivingEnemy?enemy:pc.transform);
                    foreach(var slot in new[]{CharacterSlot.Identity,CharacterSlot.Active,CharacterSlot.OffGcd2,CharacterSlot.Ultimate})
                    {
                        var selected=character.Selected(slot);if(selected==null)continue;
                        if(selected.target==SkillTargetRule.LivingAlly)AutomaticCharacterAction(avatar,slot,healTarget);
                        else if(!mechanic)AutomaticCharacterAction(avatar,slot,selected.target==SkillTargetRule.Self?pc.transform:enemy);
                        if(kit.Channeling&&!kit.CanWeaveDuringChannel)break;
                    }
                    if(kit.Channeling&&!kit.CanWeaveDuringChannel)continue;
                    if(enemy.GetComponent<CombatCounterTarget>()?.WindowOpen==true && (!boss.GuardianNormal || boss.GuardianAiCounterReady(pc.transform)))AutomaticCharacterAction(avatar,CharacterSlot.Counter,enemy);
                    if(character.Selected(CharacterSlot.OffGcd1)?.handlerId!="aegis.extension")AutomaticCharacterAction(avatar,CharacterSlot.OffGcd1,character.Selected(CharacterSlot.OffGcd1)?.target==SkillTargetRule.LivingAlly?wounded:enemy);
                    var snapshot=character.Snapshot;
                    var combo=snapshot.combo1>1?CharacterSlot.Gcd1:snapshot.combo2>1?CharacterSlot.Gcd2:kit.State.Data.lastCombo==1?CharacterSlot.Gcd2:CharacterSlot.Gcd1;
                    AutomaticCharacterAction(avatar,combo,enemy);continue;
                }
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
                    if(paladin.NewSun)identity=paladin.DamageState.Sun.Shield!=defenseNeeded;
                    var mirrorSupport=boss.ResolveLivingAggroTarget()??wounded??pc.transform;
                    if(paladin.NewMirror)identity=boss.GuardableAttackImminent(mirrorSupport,AegisMirrorState.Precision*.8f);
                    if(identity&&!paladin.IdentityBlocked)AutomaticCharacterAction(avatar,CharacterSlot.Identity,paladin.NewBulwark?wounded??pc.transform:pc.transform,paladin.NewMirror?mirrorSupport:null);
                    if(pc.HpRatio<.75f||boss.IsCasting)AutomaticCharacterAction(avatar,CharacterSlot.Job1,pc.transform);
                    if(boss.ResolveLivingAggroTarget()!=pc.transform||!CharacterSlots.IsNineKey(character.Definition)&&pc.currentShield<pc.maxHp*.05f)AutomaticCharacterAction(avatar,CharacterSlots.IsNineKey(character.Definition)?CharacterSlot.OffGcd2:CharacterSlot.Job2,enemy);
                    AutomaticCharacterAction(avatar,CharacterSlot.OffGcd1,character.Selected(CharacterSlot.OffGcd1)?.target==SkillTargetRule.LivingAlly?wounded:enemy);
                    if(enemy.GetComponent<CombatCounterTarget>()?.WindowOpen==true && (!boss.GuardianNormal || boss.GuardianAiCounterReady(pc.transform)))AutomaticCharacterAction(avatar,CharacterSlot.Counter,enemy);
                    var state=character.Snapshot;
                    var slot=state.combo1>1?CharacterSlot.Gcd1:state.combo2>1?CharacterSlot.Gcd2:pc.currentShield<pc.maxHp*.1f?CharacterSlot.Gcd2:CharacterSlot.Gcd1;
                    if(paladin.NewSun&&state.combo1==1&&state.combo2==1)slot=character.Resource(AegisPaladinData.Holy)>=paladin.SecondCost?CharacterSlot.Gcd2:CharacterSlot.Gcd1;
                    AutomaticCharacterAction(avatar,slot,enemy,paladin.NewSun&&slot==CharacterSlot.Gcd2?wounded:null);continue;
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
            var pc=avatar.GetComponent<PlayerController>();var extraSlot=CharacterSlots.IsNineKey(actor.Definition)?CharacterSlot.OffGcd1:CharacterSlot.Active;var r=actor.Selected(extraSlot);var f=actor.Selected(CharacterSlot.OffGcd2);
            if(r?.id==AegisExtensionData.Spear)AutomaticCharacterAction(avatar,extraSlot,enemy);
            else if(r?.id==AegisExtensionData.Ground&&!danger&&(Vector3.Distance(pc.transform.position,enemy.position)<=AegisExtensionData.GroundRadius||lowest<.75f))
                AutomaticCharacterAction(avatar,extraSlot,pc.transform);
            else if(r?.id==AegisExtensionData.Wings&&!danger&&boss.IsCasting)
            {
                bool covered=PartyLife.OrderedMembers().Exists(m=>m!=pc.transform&&CombatRange.LivingPartyMember(m)&&AegisExtensionData.InCone(m.position.x-pc.transform.position.x,m.position.z-pc.transform.position.z,AegisExtensionData.WingsRadius,pc.transform.eulerAngles.y+180,AegisExtensionData.WingsAngle));
                if(covered){AutomaticCharacterAction(avatar,extraSlot,pc.transform);if(pc.GetComponent<AegisExtensions>().Channeling)return;}
            }
            if(f?.id==AegisExtensionData.Intervention&&wounded!=null&&wounded!=pc.transform&&lowest<.55f&&pc.HpRatio>.55f&&!(boss.ActiveTelegraph?.IsPointInsideXZ(wounded.position)??false))
                AutomaticCharacterAction(avatar,CharacterSlot.OffGcd2,wounded);
            else if(f?.id==AegisExtensionData.Brand||f?.id==AegisExtensionData.Retribution&&pc.currentShield>0)
                AutomaticCharacterAction(avatar,CharacterSlot.OffGcd2,enemy);
            if(!danger)AutomaticCharacterAction(avatar,CharacterSlot.Ultimate,enemy);
        }
        private void AutomaticCharacterAction(NetworkRaidAvatar avatar,CharacterSlot slot,Transform target,Transform supportTarget=null,bool longInput=false)
        {
            var actor=CharacterActor.Ensure(avatar.gameObject);var state=actor.Snapshot;
            var skill=actor.Selected(slot); if(skill==null)return;
            if(skill.target==SkillTargetRule.LivingEnemy&&target!=null&&target.GetComponent<BossController>()?.HpDamageBlocked==true)return;
            if(!actor.CanExecute(slot,target,out _))return;
            var timing=System.Array.Find(state.skills,s=>s.id==skill.id);
            if(!actor.Available(slot,out _)||timing.charges<1||skill.usesGcd&&state.gcd>0||skill.target==SkillTargetRule.LivingEnemy&&(!CombatRange.LivingEnemy(target)||!CombatRange.InRange(avatar.transform,target,actor.EffectiveRange(skill))))return;
            if(skill.target==SkillTargetRule.LivingAlly&&(!CombatRange.LivingPartyMember(target)||!CombatRange.InRange(avatar.transform,target,actor.EffectiveRange(skill))))return;
            if(skill.handlerId=="aegis.extension"&&actor.GetComponent<AegisExtensions>()?.Unavailable(skill,target)!="")return;
            var kit=actor.GetComponent<CharacterKit>();if(kit?.Active==true&&kit.Unavailable(skill,target).Length>0)return;
            var tank=actor.GetComponent<NineKeyTankKit>();if(tank?.Active==true&&tank.Unavailable(slot).Length>0)return;
            var damage=actor.GetComponent<NineKeyDamageKit>();if(damage?.Active==true&&damage.Unavailable(slot).Length>0)return;
            if(actor.GetComponent<NineKeyHealerKit>()?.Active==true&&actor.GetComponent<NineKeyHealerKit>().Unavailable(slot).Length>0)return;
            foreach(var cost in skill.costs)if((System.Array.Find(state.resources,r=>r.id==cost.resourceId)?.value??0)<cost.amount*(damage?.Active==true?damage.CostScale(skill):kit?.Active==true?kit.CostScale(skill):1))return;
            var request=new RaidActionRequest{actor=avatar.Id,epoch=BattleEpoch,skill=10,slot=(int)slot,jobId=actor.Definition.id,target=CombatId(target),ruleId=supportTarget!=null?"support":"",choiceId=supportTarget!=null?CombatId(supportTarget).ToString(System.Globalization.CultureInfo.InvariantCulture):""};
            var glyph=actor.GetComponent<AegisPaladin>();
            var healer=actor.GetComponent<NineKeyHealerKit>();
            if(slot==CharacterSlot.Identity&&damage?.Active==true)
            {
                if(damage.UsesPrismInput)
                {
                    var command=damage.AiPrismCommand(target);if(command==null)return;
                    request.ruleId="damage.prism";request.choiceId=command.Encode();
                }
                else if(longInput)
                {
                    if(!damage.UsesLongInput)return;
                    request.ruleId="damage.long";request.choiceId=CombatId(supportTarget??avatar.transform).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                else if(damage.UsesGround)
                {
                    if(target==null||!damage.CanPlace(target.position))return;
                    request.ruleId="damage.ground";request.choiceId=target.position.x.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"|"+target.position.z.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            if(slot==CharacterSlot.Identity&&healer?.UsesGround==true)
            {
                var placement=supportTarget??avatar.transform;if(!healer.CanPlaceFlower(placement.position))return;
                request.ruleId="healer.ground";request.choiceId=placement.position.x.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"|"+placement.position.z.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
            }
            if(slot==CharacterSlot.Identity&&glyph?.NewBulwark==true)
            {
                if(target==null||!glyph.CanPlaceGlyph(target.position))return;
                request.ruleId="ground";request.choiceId=target.position.x.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"|"+target.position.z.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
            }
            ExecuteAction(avatar.Id,request,true);
        }
        private void UseNineKeyDamage(NetworkRaidAvatar avatar,CharacterActor actor,NineKeyDamageKit damage,Transform enemy,BossController boss,Transform wounded,bool danger,bool mechanic)
        {
            var self=avatar.transform;var ally=wounded??self;
            if(danger||mechanic)damage.Interrupt();
            if(danger||avatar.GetComponent<PlayerController>().HpRatio<.65f)AutomaticCharacterAction(avatar,CharacterSlot.Job1,self,ally);
            var role=actor.Selected(CharacterSlot.Counter);
            FaceDamageCounter(actor,damage,enemy);
            if(DamageCounterReady(actor,damage,enemy,boss))
                AutomaticCharacterAction(avatar,CharacterSlot.Counter,enemy,ally);
            else if(role?.target==SkillTargetRule.LivingAlly&&(danger||boss.IsCasting))AutomaticCharacterAction(avatar,CharacterSlot.Counter,ally,ally);
            if(danger||mechanic||damage.AiPreparing)return;
            bool moving=avatar.GetComponent<PlayerController>().IsMovingXZ;
            if(damage.AiWantsIdentity(out bool longInput,enemy,moving))AutomaticCharacterAction(avatar,CharacterSlot.Identity,enemy,ally,longInput);
            if(damage.AiPreparing)return;
            foreach(var slot in new[]{CharacterSlot.OffGcd1,CharacterSlot.OffGcd2,CharacterSlot.Ultimate})
            {
                var selected=actor.Selected(slot);if(selected==null)continue;
                // A stored return anchor is a positioning decision, never an automatic damage weave.
                if(slot==CharacterSlot.OffGcd2&&damage.Job.index==11&&selected.id==NineKeyDamageData.Id(damage.Job,"f2"))continue;
                AutomaticCharacterAction(avatar,slot,selected.target==SkillTargetRule.LivingAlly?ally:selected.target==SkillTargetRule.Self?self:enemy,ally);
            }
            AutomaticCharacterAction(avatar,damage.AiCombo(enemy,moving),enemy,ally);
        }
        private static void FaceDamageCounter(CharacterActor actor,NineKeyDamageKit damage,Transform enemy)
        {
            if(actor.Selected(CharacterSlot.Counter)?.id!=NineKeyDamageData.Id(damage.Job,"c0")||
                enemy?.GetComponent<CombatCounterTarget>()?.WindowOpen!=true)return;
            // Reuse the ordinary stationary attack-facing rule. Movement, dash
            // and forced movement keep ownership of facing in PlayerController.
            actor.GetComponent<PlayerController>()?.FaceAttackTarget(enemy);
        }
        private static bool DamageCounterReady(CharacterActor actor,NineKeyDamageKit damage,Transform enemy,BossController boss)
        {
            var role=actor.Selected(CharacterSlot.Counter);
            if(role?.id!=NineKeyDamageData.Id(damage.Job,"c0")||enemy==null)return false;
            var counter=enemy.GetComponent<CombatCounterTarget>();
            return counter!=null&&counter.CanCounter(actor.transform,actor.EffectiveRange(role))&&
                (!boss.GuardianNormal||boss.GuardianAiCounterReady(actor.transform));
        }
        private void UseNineKeyHealer(NetworkRaidAvatar avatar,CharacterActor actor,NineKeyHealerKit healer,Transform enemy,BossController boss,Transform wounded,float lowest,bool danger,bool mechanic)
        {
            Transform patient=wounded??avatar.transform;
            if(danger||mechanic)healer.Interrupt();
            foreach(var member in PartyLife.OrderedMembers())
                if(CombatRange.LivingPartyMember(member)&&member.GetComponent<CombatStatusEffects>()?.HasDispellable(AegisPaladinData.DispelTags)==true)
                {AutomaticCharacterAction(avatar,CharacterSlot.Job1,member,member);break;}
            if(lowest<.5f)AutomaticCharacterAction(avatar,CharacterSlot.Job1,patient,patient);
            if(lowest<.75f)AutomaticCharacterAction(avatar,CharacterSlot.OffGcd2,enemy,patient);
            if(enemy!=null&&enemy.GetComponent<CombatCounterTarget>()?.WindowOpen==true&&(!boss.GuardianNormal||boss.GuardianAiCounterReady(avatar.transform)))AutomaticCharacterAction(avatar,CharacterSlot.Counter,enemy,patient);
            if(healer.Channeling)return;
            Transform routePatient=patient;
            if(healer.Job.index==4&&healer.Route==1&&routePatient==avatar.transform)
                foreach(var member in PartyLife.OrderedMembers())if(member!=avatar.transform&&CombatRange.LivingPartyMember(member)){routePatient=member;break;}
            if(!danger&&!mechanic&&healer.AiWantsIdentity(routePatient,lowest,boss.IsCasting))AutomaticCharacterAction(avatar,CharacterSlot.Identity,enemy,routePatient);
            if(lowest<.45f)AutomaticCharacterAction(avatar,CharacterSlot.Ultimate,enemy,patient);
            if(healer.Channeling)return;
            var utility=actor.Selected(CharacterSlot.OffGcd1);
            if(utility!=null&&(lowest<.85f||utility.target!=SkillTargetRule.LivingAlly))AutomaticCharacterAction(avatar,CharacterSlot.OffGcd1,utility.target==SkillTargetRule.LivingAlly?patient:utility.target==SkillTargetRule.Self?avatar.transform:enemy,patient);
            bool heal=lowest<.95f||!CombatRange.LivingEnemy(enemy)||enemy.GetComponent<BossController>()?.HpDamageBlocked==true;
            AutomaticCharacterAction(avatar,heal?CharacterSlot.Gcd2:CharacterSlot.Gcd1,heal?patient:enemy,patient);
        }
        private void UseNineKeyTank(NetworkRaidAvatar avatar,CharacterActor actor,NineKeyTankKit tank,Transform enemy,BossController boss,Transform wounded,float lowest,bool danger,bool mechanic)
        {
            var pc=avatar.GetComponent<PlayerController>();var ally=wounded??pc.transform;
            if(mechanic||danger)tank.Interrupt();
            if(pc.HpRatio<.75f||boss.IsCasting)AutomaticCharacterAction(avatar,CharacterSlot.Job1,pc.transform);
            if(enemy.GetComponent<CombatCounterTarget>()?.WindowOpen==true&&(!boss.GuardianNormal||boss.GuardianAiCounterReady(pc.transform)))AutomaticCharacterAction(avatar,CharacterSlot.Counter,enemy);
            if(boss.ResolveLivingAggroTarget()!=pc.transform&&tank.CanThreat(enemy))AutomaticCharacterAction(avatar,CharacterSlot.OffGcd2,enemy);
            var state=tank.State;float resource=actor.Resource(tank.Job.Resource);
            if(state.charging)
            {
                if(state.chargeTime+state.chargeDiscount>=state.chargeLimit)
                    ExecuteAction(avatar.Id,new RaidActionRequest{actor=avatar.Id,epoch=BattleEpoch,skill=10,slot=(int)(state.chargeChain==2?CharacterSlot.Gcd2:CharacterSlot.Identity),jobId=actor.Definition.id,target=CombatId(enemy),ruleId="tank.release",choiceId=CombatId(ally).ToString(System.Globalization.CultureInfo.InvariantCulture)},true);
                return;
            }
            if(state.whirling)return;
            if(!mechanic&&!danger)
            {
                if(tank.Blood)
                {
                    if(tank.Route==1&&lowest<.8f&&resource>=30)AutomaticCharacterAction(avatar,CharacterSlot.Identity,ally,ally);
                    if(tank.Route==2&&(resource>=60||state.reserved>0))AutomaticCharacterAction(avatar,CharacterSlot.Identity,enemy,ally);
                    if(tank.Route==3&&resource>=60)AutomaticCharacterAction(avatar,CharacterSlot.Identity,enemy,ally);
                }
                else
                {
                    bool identity=tank.Route==1&&resource>=(actor.Snapshot.coreTrait==NineKeyTankData.Core(tank.Job,1,1)?4:actor.Snapshot.coreTrait==NineKeyTankData.Core(tank.Job,1,0)?1:2)||
                        tank.Route==2&&(state.danceTime>0?state.danceReady>0:resource>=2)||tank.Route==3&&actor.Resource(NineKeyTankData.Heat(tank.Job))>=80;
                    if(identity)AutomaticCharacterAction(avatar,CharacterSlot.Identity,enemy,ally);
                }
                AutomaticCharacterAction(avatar,CharacterSlot.Ultimate,enemy,ally);
            }
            state=tank.State;if(state.charging||state.whirling||tank.Unavailable(CharacterSlot.Gcd1).Length>0)return;
            var third=actor.Selected(CharacterSlot.OffGcd1);
            AutomaticCharacterAction(avatar,CharacterSlot.OffGcd1,third.target==SkillTargetRule.LivingAlly?ally:third.target==SkillTargetRule.Self?pc.transform:enemy,ally);
            var snapshot=actor.Snapshot;
            CharacterSlot combo=snapshot.combo1>1?CharacterSlot.Gcd1:snapshot.combo2>1?CharacterSlot.Gcd2:state.lastCombo==1?CharacterSlot.Gcd2:CharacterSlot.Gcd1;
            if(!tank.Blood&&tank.Route==3&&snapshot.combo1==1&&snapshot.combo2==1)combo=resource<1?CharacterSlot.Gcd1:CharacterSlot.Gcd2;
            if(tank.Blood&&tank.Route==2&&(mechanic||danger)&&snapshot.combo2==3)return;
            AutomaticCharacterAction(avatar,combo,enemy,ally);
        }
        private void AutomaticAction(NetworkRaidAvatar avatar, int skill, Transform target)
        {
            if(skill<=4&&target!=null&&target.GetComponent<BossController>()?.HpDamageBlocked==true)return;
            avatar.CombatTarget = target;
            RaidCombatAuthority.ExecutingActor = avatar.Id;
            bool valid;
            try { valid = skill > 4 || avatar.GetComponent<PlayerCombatController>().ValidateCombatSkill(skill, out _); }
            finally { RaidCombatAuthority.ExecutingActor = 0; }
            if (valid)
            {
                var request = new RaidActionRequest { actor = avatar.Id, epoch = BattleEpoch, skill = skill, target = CombatId(target) };
                if (skill == 5) StampRescueRequest(request, target);
                ExecuteAction(avatar.Id, request, true);
            }
        }
    }
}
