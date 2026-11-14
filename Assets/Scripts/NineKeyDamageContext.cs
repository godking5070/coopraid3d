using System;
using System.Linq;
using UnityEngine;

namespace RaidRogue
{
    public sealed partial class NineKeyDamageKit
    {
        public bool AiPreparing { get { Bind();return _meteor?.Snapshot.casting==true||_snipe?.Active==true&&!_snipe.Ready||GroundAssaultActive; } }
        public CharacterSlot AiCombo(Transform enemy,bool moving=false)
        {
            Bind();var s=_actor.Snapshot;
            if(Job.index==10&&Route==2)
            {
                var rights=_instant.Snapshot;
                if(_instant.CanConsume&&(moving||rights.remaining<=rights.rights*2+1))return CharacterSlot.Gcd2;
                if(moving)return CharacterSlot.Gcd1;
            }
            if(Job.index==8&&Route==1)
                return Unavailable(CharacterSlot.Gcd1).Length==0&&(Unavailable(CharacterSlot.Gcd2).Length>0||s.combo1>1)?CharacterSlot.Gcd1:CharacterSlot.Gcd2;
            if(Job.index==9&&Route==1)return _arrows.Snapshot.pending.Any(p=>p.target==CharacterKit.Id(enemy)&&p.straightStep==0&&p.remaining<=(Core(1,0)?3:2.5f))?CharacterSlot.Gcd1:CharacterSlot.Gcd2;
            if(IsFourth(_actor.Selected(CharacterSlot.Gcd2)))return _runtime.Resource(Job.Resource)>=20?CharacterSlot.Gcd2:CharacterSlot.Gcd1;
            if(Job.index==10&&Route==3&&_gravity.Active&&CombatRange.LivingEnemy(enemy))
            {
                float radius=_gravity.Snapshot.expanded?(Core(3,0)?9:7):(Core(3,1)?2:3);
                if(AiGravityDistanceSquared(enemy)>radius*radius)return CharacterSlot.Gcd2;
            }
            if(Job.index==11&&Route==2)
            {
                if(_projection.Active)return CharacterSlot.Gcd2;
                if(!CombatRange.InRange(transform,enemy,EffectiveRange(_actor.Selected(CharacterSlot.Gcd2))))return CharacterSlot.Gcd1;
            }
            return s.combo1>1?CharacterSlot.Gcd1:s.combo2>1?CharacterSlot.Gcd2:_resources.Snapshot.lastCompletedChain==1?CharacterSlot.Gcd2:CharacterSlot.Gcd1;
        }
        public bool AiWantsIdentity(out bool longInput,Transform enemy=null,bool moving=false)
        {
            longInput=false;if(!Active)return false;Bind();if(AiPreparing)return false;
            float resource=_runtime.Resource(Job.Resource);
            if(Job.index==6)return Route==1?resource>=2:Route==2?resource>=_runtime.Maximum(Job.Resource):!_seal.Active&&resource>=2;
            if(Job.index==7)
            {
                if(Route==1)return resource>=(Core(1,0)?30:50);
                if(Route==2)return !Has("acceleration")&&resource>=80;
                var spears=_spears.Snapshot.spears;longInput=spears.Length>0&&(spears.All(s=>s.charge==3)||spears.Any(s=>s.remaining<2));
                return longInput||_spears.CanPlace&&resource>=40;
            }
            if(Job.index==8)
            {
                if(Route==1)
                {
                    var s=_hands.Snapshot;
                    if(s.reloading!=0||s.cooldown>0||s.left>0&&s.right>0)return false;
                    // Short R normally chooses the last fired hand, even when
                    // it still has ammo. Prefer the empty opposite hand in that
                    // case so the loaded hand can keep firing during reload.
                    int shortHand=_hands.ReloadTarget(false),opposite=_hands.ReloadTarget(true);
                    longInput=opposite!=0&&_hands.Ammo(opposite)==0&&shortHand!=0&&_hands.Ammo(shortHand)>0;
                    return true;
                }
                if(Route==2)return !_grenade.Active&&resource>=3;
                return _snipe.Ready||!_snipe.Active&&resource>=(Core(3,1)?5:3);
            }
            if(Job.index==9)return Route==1?resource>=(Core(1,1)?30:40)&&_arrows.Snapshot.convergence>0:
                Route==2?(_burst.Active? _burst.Snapshot.inputDelay<=0&&(_burst.Snapshot.precise||_burst.Snapshot.remaining<3):resource>=50):resource>=(Core(3,1)?50:40);
            if(Job.index==10)
            {
                if(Route==1)return _meteor.Active||resource>=60;
                if(Route==2)
                {
                    var rights=_instant.Snapshot;
                    if(!_instant.Active)return rights.cooldown<=0&&resource>=(Core(2,0)?40:60);
                    // Save rights during safe stationary casts; use them while
                    // moving or before the remaining GCD opportunities expire.
                    // Never change the mode of a cast already in progress.
                    if(_actor.Snapshot.castSkillId.Length>0||rights.toggleDelay>0)return false;
                    bool consume=moving||rights.remaining<=rights.rights*2+1;
                    return rights.enabled!=consume;
                }
                if(!_gravity.Active)return resource>=60;
                if(!CombatRange.LivingEnemy(enemy))return false;
                float compressed=Core(3,1)?2:3,expanded=Core(3,0)?9:7;
                double distance=AiGravityDistanceSquared(enemy);
                // A small return margin avoids alternating modes at one boundary.
                return _gravity.Snapshot.expanded?distance<(compressed-.25f)*(compressed-.25f):
                    distance>compressed*compressed&&distance<=expanded*expanded;
            }
            if(Job.index==11)
            {
                if(Route==1)
                {
                    int count=Core(1,0)?1:2;
                    // A player can disconnect with locked, partially expired records.
                    // Unlock an incomplete set before gathering replacements.
                    if(_memory.Snapshot.locked&&!_memory.CanReplay(count)){longInput=true;return true;}
                    return resource>=count&&_memory.CanReplay(count);
                }
                if(Route==2)
                {
                    if(_projection.Active)return false;
                    if(CombatRange.LivingEnemy(enemy)&&!CombatRange.InRange(transform,enemy,EffectiveRange(_actor.Selected(CharacterSlot.Gcd2)))&&
                        CombatRange.InRange(transform,enemy,Core(2,0)?25:20))return resource>=1;
                    longInput=true;return resource>=3;
                }
                return AiPrismCommand(enemy)!=null;
            }
            return false;
        }
        private double AiGravityDistanceSquared(Transform enemy)
        {
            if(enemy==null)return double.PositiveInfinity;
            var point=Point(enemy.position);var center=_gravity.Snapshot.point;
            if(!point.Valid||!center.Valid)return double.PositiveInfinity;
            double x=point.x-center.x,z=point.z-center.z;return x*x+z*z;
        }
        public float EffectiveRange(CharacterSkillDefinition skill)
        {
            if(skill==null)return 0;Bind();float range=skill.range;
            if(Job.index==11&&Route==2&&skill.comboChain==2)range=_projection.Active?(Core(2,0)?25:20):3;
            if(skill.comboChain==2&&_actor.HasAuxiliary(NineKeyDamageData.Reach))range+=3;
            if(skill.comboChain>0&&Has("dance.reach"))range=Mathf.Max(range,12);
            if(skill.comboChain>0&&Has("tempest.reach"))range=Mathf.Max(range,10);
            return range;
        }
        public float GcdScale(CharacterSkillDefinition skill)
        {Bind();return Job.index==7&&Route==2&&Has("acceleration")&&skill.comboChain>0?.75f:1;}
        public float CastTime(CharacterSkillDefinition skill)
        {
            Bind();return Job.index==10&&Route==2&&skill.comboChain==2&&_instant.CanConsume?0:skill.castTime;
        }
        public float CooldownScale(CharacterSkillDefinition skill)
        {
            if(skill.id==NineKeyDamageData.Id(Job,"q")&&_actor.HasAuxiliary(NineKeyDamageData.Guard))return 1.25f;
            return 1;
        }
        public float CostScale(CharacterSkillDefinition skill)
        {
            if(ThirdAssaultCore&&skill.id==NineKeyDamageData.Id(Job,"a0"))return .6f;
            return Job.index==7&&skill.id==NineKeyDamageData.Id(Job,"a1")&&Core(2,1)?3:1;
        }
        private float InstallationDuration(float duration)
        {return duration+(Job.index==7&&_actor.HasAuxiliary("tempest.aux.duration")?5:0);}
        private float TempestMarkDuration(float duration)
        {return duration+(Job.index==7&&_actor.HasAuxiliary("tempest.aux.mark")?3:0);}
        private void ProtectAfterRoot(bool used,float before)
        {
            if(!used||Job.index!=7||!_actor.HasAuxiliary("tempest.aux.guard")||_runtime.Resource(Job.Resource)>=before||Has("tempest.guard.gate"))return;
            Effect("tempest.guard.gate","gate",transform,1,12);
            Shield(transform,"tempest.guard",Power*.04f);
        }
        public void MovementStarted()
        {
            if(!Active||RaidCombatAuthority.Replica)return;Bind();
            if(Job.index==7&&_actor.HasAuxiliary("tempest.aux.reach"))Effect("tempest.reach","range",transform,1,8);
        }
        public string Unavailable(CharacterSlot slot)
        {
            if(!Active)return "";Bind();
            if(CounterRecovering(slot))return "카운터 후딜 중 · 방어·이동 사용 가능";
            if(slot==CharacterSlot.Gcd2&&IsFourth(_actor.Selected(slot))&&_runtime.Resource(Job.Resource)<20)return "사단연격 전류20 필요 · 1번으로 다음 콤보 시작 가능";
            if(Job.index==7&&slot==CharacterSlot.OffGcd2&&_actor.Selected(slot)?.id==NineKeyDamageData.Id(Job,"f2")&&!_seed.Active&&_seed.Snapshot.cooldown>0)return "폭풍의 씨앗 재사용 대기 중";
            if(Job.index==11&&slot==CharacterSlot.OffGcd2&&_actor.Selected(slot)?.id==NineKeyDamageData.Id(Job,"f2")&&!_return.Active&&_return.Snapshot.cooldown>0)return "잔상 귀환 재사용 대기 중";
            if(Job.index==8&&Route==1&&(slot==CharacterSlot.Gcd1||slot==CharacterSlot.Gcd2))
            {
                int hand=slot==CharacterSlot.Gcd1?1:2;
                if(_hands.Snapshot.reloading==hand)return "선택한 손 장전 중";
                if(_hands.Ammo(hand)==0)return "선택한 손의 탄이 없습니다 · R 장전";
            }
            return "";
        }
        // Ordinary walking cancels unfinished aim / the current meteor segment.
        private bool CounterRecovering(CharacterSlot slot)
        {return Has("counter.recovery")&&slot!=CharacterSlot.Job1&&slot!=CharacterSlot.Counter&&slot!=CharacterSlot.Movement&&slot!=CharacterSlot.Potion;}
        // Holding a completed sniper shot does not block walking or other skills.
        public void Moved(){if(!Active||RaidCombatAuthority.Replica)return;Bind();_snipe.Interrupt();_meteor.Interrupt();}
    }
}
