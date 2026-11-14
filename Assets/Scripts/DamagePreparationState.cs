using System;

namespace RaidRogue
{
    internal static class DamageTime
    {
        public static bool Nonnegative(float x)=>CharacterDefinitionValidation.Finite(x)&&x>=0;
        public static bool Positive(float x)=>Nonnegative(x)&&x>0;
    }
    [Serializable] public sealed class DamageHandsSnapshot
    {
        public int left,right,capacity=3,lastHand=1,reloading,loadAmount;
        public float remaining,cooldown;
        public DamageHandsSnapshot Copy()=>(DamageHandsSnapshot)MemberwiseClone();
    }
    // Hand ammo is the Peacekeeper resource. The adapter exposes the sum in the
    // framework HUD; it must not also apply ordinary +2/+1 finisher ammunition.
    public sealed class DamageHandsState
    {
        private DamageHandsSnapshot _data=new DamageHandsSnapshot();
        public DamageHandsSnapshot Snapshot=>_data.Copy();
        public int Total=>_data.left+_data.right;
        public int Ammo(int hand)=>hand==1?_data.left:hand==2?_data.right:0;
        public bool Configure(int capacity,int startingPerHand=0)
        {
            if(capacity<1||capacity>6||startingPerHand<0||startingPerHand>capacity)return false;
            _data=new DamageHandsSnapshot{capacity=capacity,left=startingPerHand,right=startingPerHand};return true;
        }
        // Called at actual launch, even on a miss. Failed input preserves combo.
        public bool Fire(int hand)
        {
            if(hand<1||hand>2||_data.reloading==hand||Ammo(hand)<=0)return false;
            if(hand==1)_data.left--;else _data.right--;_data.lastHand=hand;return true;
        }
        public int ReloadTarget(bool opposite)
        {
            int target=opposite?3-_data.lastHand:_data.lastHand;
            if(!opposite&&Ammo(target)==_data.capacity)target=3-target;
            return Ammo(target)<_data.capacity?target:0;
        }
        public bool BeginReload(bool opposite,float duration,float cooldown,int amount)
        {
            int target=ReloadTarget(opposite);
            if(target==0||_data.reloading!=0||_data.cooldown>0||!DamageTime.Positive(duration)||!DamageTime.Nonnegative(cooldown)||amount<1||amount>_data.capacity)return false;
            _data.reloading=target;_data.remaining=duration;_data.cooldown=cooldown;_data.loadAmount=amount;return true;
        }
        // Return the completed hand once, for its first-hit synergy permission.
        public int Tick(float delta)
        {
            if(!DamageTime.Positive(delta))return 0;
            _data.cooldown=Math.Max(0,_data.cooldown-delta);
            if(_data.reloading==0)return 0;
            _data.remaining=Math.Max(0,_data.remaining-delta);if(_data.remaining>0)return 0;
            int hand=_data.reloading;
            if(hand==1)_data.left=Math.Min(_data.capacity,_data.left+_data.loadAmount);else _data.right=Math.Min(_data.capacity,_data.right+_data.loadAmount);
            _data.reloading=0;_data.loadAmount=0;return hand;
        }
        public bool Supply()
        {
            int before=Total;_data.left=Math.Min(_data.capacity,_data.left+1);_data.right=Math.Min(_data.capacity,_data.right+1);return Total>before;
        }
        public bool Restore(DamageHandsSnapshot v)
        {
            if(v==null||v.capacity<1||v.capacity>6||v.left<0||v.left>v.capacity||v.right<0||v.right>v.capacity||v.lastHand<1||v.lastHand>2||v.reloading<0||v.reloading>2||
                !DamageTime.Nonnegative(v.remaining)||!DamageTime.Nonnegative(v.cooldown)||v.loadAmount<0||v.loadAmount>v.capacity||
                (v.reloading!=0)!=(v.remaining>0)||(v.reloading!=0)!=(v.loadAmount>0))return false;
            _data=v.Copy();return true;
        }
    }
    [Serializable] public sealed class DamageSnipeSnapshot
    {
        public int target,shots;
        public float aiming,remaining,keep;
        public bool ready,markGranted,shotPending;
        public DamageSnipeSnapshot Copy()=>(DamageSnipeSnapshot)MemberwiseClone();
    }
    public sealed class DamageSnipeState
    {
        private readonly NineKeyDamageResources _resources;
        private DamageSnipeSnapshot _data=new DamageSnipeSnapshot();
        public DamageSnipeState(NineKeyDamageResources resources){_resources=resources;}
        public DamageSnipeSnapshot Snapshot=>_data.Copy();
        public bool Active=>_data.target!=0;
        public bool Ready=>Active&&_data.ready;
        public bool Begin(int target,float cost,float aim,float keep,int shots)
        {
            if(Active||target==0||!DamageTime.Positive(aim)||!DamageTime.Positive(keep)||shots<1||shots>3||!_resources.Reserve("snipe",cost))return false;
            _data=new DamageSnipeSnapshot{target=target,aiming=aim,keep=keep,shots=shots};return true;
        }
        public void Tick(float delta)
        {
            if(!Active||!DamageTime.Positive(delta))return;
            if(!_data.ready)
            {
                float used=Math.Min(delta,_data.aiming);_data.aiming-=used;delta-=used;
                if(_data.aiming>0)return;
                if(!_resources.Commit("snipe")){_data=new DamageSnipeSnapshot();return;}
                _data.ready=true;_data.remaining=_data.keep;
            }
            _data.remaining=Math.Max(0,_data.remaining-delta);
            if(_data.remaining==0)_data=new DamageSnipeSnapshot();
        }
        public bool Interrupt()
        {
            if(!Active||_data.ready)return false;
            _resources.Cancel("snipe");_data=new DamageSnipeSnapshot();return true;
        }
        // Validation precedes launch; an invalid/temporarily immune target keeps
        // preparation. Once launched a miss still consumes this shot.
        public bool Launch(bool validTarget,out int target)
        {
            target=0;if(!Ready||!validTarget||_data.shotPending||_data.shots<=0)return false;
            target=_data.target;_data.shots--;_data.shotPending=true;return true;
        }
        // Call once synchronously after the launched shot resolves. A set grants
        // one mark on its first effective hit, not one per split shot.
        public bool ResolveShot(bool hit)
        {
            if(!Ready||!_data.shotPending)return false;
            _data.shotPending=false;
            bool mark=hit&&!_data.markGranted;if(mark)_data.markGranted=true;
            if(_data.shots==0)_data=new DamageSnipeSnapshot();return mark;
        }
        public bool Restore(DamageSnipeSnapshot v)
        {
            if(v==null||!DamageTime.Nonnegative(v.aiming)||!DamageTime.Nonnegative(v.remaining)||!DamageTime.Nonnegative(v.keep)||v.shots<0||v.shots>3)return false;
            if(v.target==0){if(v.shots!=0||v.ready||v.aiming!=0||v.remaining!=0||v.keep!=0||v.markGranted||v.shotPending)return false;}
            else if(v.shots==0&&!v.shotPending||v.keep<=0||(v.ready?(v.aiming!=0||v.remaining<=0||v.remaining>v.keep):(v.aiming<=0||v.remaining!=0||v.markGranted||v.shotPending)))return false;
            bool reserved=_resources.Snapshot.reservation=="snipe";
            if(reserved!=(v.target!=0&&!v.ready))return false;
            _data=v.Copy();return true;
        }
    }
    [Serializable] public sealed class DamageMeteorSnapshot
    {
        public int completed,segments;
        public float segmentTime,progress,remaining;
        public bool casting,interrupted;
        public DamageMeteorSnapshot Copy()=>(DamageMeteorSnapshot)MemberwiseClone();
    }
    public sealed class DamageMeteorState
    {
        private readonly NineKeyDamageResources _resources;
        private DamageMeteorSnapshot _data=new DamageMeteorSnapshot();
        public DamageMeteorState(NineKeyDamageResources resources){_resources=resources;}
        public DamageMeteorSnapshot Snapshot=>_data.Copy();
        public bool Active=>_data.segments>0;
        public bool BeginOrResume(bool validTarget,float cost,int segments,float segmentTime,float keep)
        {
            if(!validTarget||_data.casting)return false;
            if(Active){_data.casting=true;return true;}
            if(segments<1||segments>8||!DamageTime.Positive(segmentTime)||!DamageTime.Positive(keep)||!_resources.Reserve("meteor",cost))return false;
            _data=new DamageMeteorSnapshot{segments=segments,segmentTime=segmentTime,remaining=keep,casting=true};return true;
        }
        public void Interrupt()
        {if(!Active)return;_data.progress=0;_data.casting=false;_data.interrupted=true;}
        // Return completion only once; caller immediately attacks its current valid
        // target. There is no completed-spell storage state or early segment hit.
        public bool Tick(float delta,bool validTarget)
        {return Tick(delta,validTarget,out _);}
        public bool Tick(float delta,bool validTarget,out bool uninterrupted)
        {
            uninterrupted=false;
            if(!Active||!DamageTime.Positive(delta))return false;
            if(!validTarget)Interrupt();
            float available=Math.Min(delta,_data.remaining);
            if(_data.casting)
            {
                _data.progress+=available;
                while(_data.progress>=_data.segmentTime)
                {
                    _data.progress-=_data.segmentTime;_data.completed++;
                    if(_data.completed==_data.segments)
                    {bool committed=_resources.Commit("meteor");uninterrupted=committed&&!_data.interrupted;_data=new DamageMeteorSnapshot();return committed;}
                }
            }
            _data.remaining=Math.Max(0,_data.remaining-delta);
            if(_data.remaining==0){_resources.Cancel("meteor");_data=new DamageMeteorSnapshot();}
            return false;
        }
        public bool Restore(DamageMeteorSnapshot v)
        {
            if(v==null||v.segments<0||v.segments>8||v.completed<0||!DamageTime.Nonnegative(v.segmentTime)||!DamageTime.Nonnegative(v.progress)||!DamageTime.Nonnegative(v.remaining))return false;
            if(v.segments==0){if(v.completed!=0||v.segmentTime!=0||v.progress!=0||v.remaining!=0||v.casting||v.interrupted)return false;}
            else if(v.completed>=v.segments||v.segmentTime<=0||v.progress>=v.segmentTime||v.remaining<=0||!v.casting&&(v.progress!=0||!v.interrupted))return false;
            if((_resources.Snapshot.reservation=="meteor")!=(v.segments>0))return false;
            _data=v.Copy();return true;
        }
    }
}
