using System;
using System.Linq;

namespace RaidRogue
{
    [Serializable] public sealed class DamageTempestPreparationSnapshot
    {
        public float combo,cloud;
        public DamageTempestPreparationSnapshot Copy()=>(DamageTempestPreparationSnapshot)MemberwiseClone();
    }
    public sealed class DamageTempestPreparationState
    {
        private DamageTempestPreparationSnapshot _data=new DamageTempestPreparationSnapshot();
        public DamageTempestPreparationSnapshot Snapshot=>_data.Copy();
        public void Grant(int secondChoice,float duration)
        {
            if(!DamageTime.Positive(duration))return;
            if(secondChoice==0)_data.combo=duration;
            if(secondChoice==2)_data.cloud=duration;
        }
        public float TakeManualThird(bool assault)
        {
            float scale=_data.combo>0?1.25f:1;_data.combo=0;
            return scale*(assault?TakeRelease():1);
        }
        public float TakeRelease()
        {float scale=_data.cloud>0?1.25f:1;_data.cloud=0;return scale;}
        public void Tick(float delta)
        {if(DamageTime.Positive(delta)){_data.combo=Math.Max(0,_data.combo-delta);_data.cloud=Math.Max(0,_data.cloud-delta);}}
        public bool Restore(DamageTempestPreparationSnapshot value)
        {
            if(value==null||!DamageTime.Nonnegative(value.combo)||!DamageTime.Nonnegative(value.cloud))return false;
            _data=value.Copy();return true;
        }
    }
    [Serializable] public struct DamagePoint
    {
        public float x,z;
        public DamagePoint(float x,float z){this.x=x;this.z=z;}
        public bool Valid=>CharacterDefinitionValidation.Finite(x)&&CharacterDefinitionValidation.Finite(z);
    }
    [Serializable] public sealed class DamageSpear
    {
        public int id,charge;
        public DamagePoint point;
        public float remaining,nextTick=1,stored,storageLimit,tickPower;
        public bool compressed;
        public DamageSpear Copy()=>(DamageSpear)MemberwiseClone();
    }
    [Serializable] public sealed class DamageSpearSnapshot
    {
        public int nextId=1;
        public DamageSpear[] spears=Array.Empty<DamageSpear>();
        public DamageSpearSnapshot Copy()=>new DamageSpearSnapshot{nextId=nextId,spears=spears.Select(s=>s.Copy()).ToArray()};
    }
    public readonly struct DamageSpearTick
    {
        public readonly DamagePoint Point;
        public readonly float Power;
        public DamageSpearTick(DamagePoint point,float power){Point=point;Power=power;}
    }
    public sealed class DamageSpearState
    {
        private DamageSpearSnapshot _data=new DamageSpearSnapshot();
        public DamageSpearSnapshot Snapshot=>_data.Copy();
        public bool CanPlace=>_data.spears.Length<2;
        // Maximum-capacity behavior is a prototype block, never silently deleting
        // a charged spear. F seed uses a different state instance and cost path.
        public bool Place(DamagePoint point,float duration,float tickPower,bool compressed,float storageLimit)
        {
            if(!CanPlace||!point.Valid||!DamageTime.Positive(duration)||duration>3600||!DamageTime.Nonnegative(tickPower)||!DamageTime.Nonnegative(storageLimit)||_data.nextId==int.MaxValue)return false;
            _data.spears=_data.spears.Concat(new[]{new DamageSpear{id=_data.nextId++,point=point,remaining=duration,tickPower=tickPower,compressed=compressed,storageLimit=storageLimit}}).ToArray();return true;
        }
        public void Charge(int chain,int step,bool directHit)
        {
            if(!directHit||chain!=2||step<1||step>3)return;
            foreach(var spear in _data.spears)spear.charge=Math.Min(3,spear.charge+1);
        }
        public void Move(DamagePoint point)
        {if(point.Valid)foreach(var spear in _data.spears)spear.point=point;}
        public DamageSpearTick[] Tick(float delta)
        {
            if(!DamageTime.Positive(delta))return Array.Empty<DamageSpearTick>();
            var result=new System.Collections.Generic.List<DamageSpearTick>();
            foreach(var spear in _data.spears)
            {
                float elapsed=Math.Min(delta,spear.remaining);
                int ticks=elapsed<spear.nextTick?0:1+(int)Math.Min(3600,Math.Floor(elapsed-spear.nextTick));
                spear.nextTick=spear.nextTick-elapsed+ticks;
                spear.remaining=Math.Max(0,spear.remaining-delta);
                if(spear.compressed)spear.stored=Math.Min(spear.storageLimit,spear.stored+ticks*spear.tickPower*.75f);
                else for(int n=0;n<ticks;n++)result.Add(new DamageSpearTick(spear.point,spear.tickPower));
            }
            // Expiry does not recall, refund, explode or release compressed storage.
            _data.spears=_data.spears.Where(s=>s.remaining>0).ToArray();return result.ToArray();
        }
        public DamageSpear[] Recall()
        {
            var result=Snapshot.spears;_data.spears=Array.Empty<DamageSpear>();return result;
        }
        public bool Restore(DamageSpearSnapshot v)
        {
            if(v?.spears==null||v.nextId<1||v.spears.Length>2||v.spears.Any(s=>s==null||s.id<1||s.id>=v.nextId||s.charge<0||s.charge>3||!s.point.Valid||
                !DamageTime.Positive(s.remaining)||s.remaining>3600||!DamageTime.Positive(s.nextTick)||s.nextTick>1||!DamageTime.Nonnegative(s.stored)||!DamageTime.Nonnegative(s.storageLimit)||
                !DamageTime.Nonnegative(s.tickPower)||s.stored>s.storageLimit||!s.compressed&&s.stored!=0)||v.spears.Select(s=>s.id).Distinct().Count()!=v.spears.Length)return false;
            _data=v.Copy();return true;
        }
    }
    [Serializable] public sealed class DamageGravitySnapshot
    {
        public DamagePoint point;
        public float remaining,nextTick;
        public bool expanded,protectionUsed;
        public DamageGravitySnapshot Copy()=>(DamageGravitySnapshot)MemberwiseClone();
    }
    [Serializable] public sealed class DamageSeedSnapshot
    {
        public DamagePoint point;
        public float remaining,nextTick,cooldown;
        public bool compressed;
        public float stored,storageLimit;
        public DamageSeedSnapshot Copy()=>(DamageSeedSnapshot)MemberwiseClone();
    }
    // F seed has neither R spear charges nor resource refunds. Its own cooldown
    // starts at deployment, while re-input remains available for early detonation.
    public sealed class DamageSeedState
    {
        private DamageSeedSnapshot _data=new DamageSeedSnapshot();
        public DamageSeedSnapshot Snapshot=>_data.Copy();
        public bool Active=>_data.remaining>0;
        public bool Place(DamagePoint point,float duration,float cooldown,bool compressed=false,float storageLimit=240)
        {
            if(Active||_data.cooldown>0||!point.Valid||!DamageTime.Positive(duration)||duration>3600||!DamageTime.Positive(cooldown)||cooldown>3600||!DamageTime.Nonnegative(storageLimit))return false;
            _data=new DamageSeedSnapshot{point=point,remaining=duration,nextTick=1,cooldown=cooldown,compressed=compressed,storageLimit=storageLimit};return true;
        }
        public void Move(DamagePoint point){if(Active&&point.Valid)_data.point=point;}
        public bool Detonate(out DamagePoint point)
        {
            point=_data.point;if(!Active)return false;
            _data.remaining=0;_data.nextTick=0;_data.stored=0;return true;
        }
        public int Tick(float delta,out bool exploded)
        {
            exploded=false;if(!DamageTime.Positive(delta))return 0;
            _data.cooldown=Math.Max(0,_data.cooldown-delta);
            if(!Active)return 0;
            float elapsed=Math.Min(delta,_data.remaining);
            int ticks=elapsed<_data.nextTick?0:1+(int)Math.Min(3600,Math.Floor(elapsed-_data.nextTick));
            _data.nextTick=_data.nextTick-elapsed+ticks;
            _data.remaining=Math.Max(0,_data.remaining-delta);
            if(!Active){exploded=true;_data.nextTick=0;}
            if(_data.compressed){_data.stored=Math.Min(_data.storageLimit,_data.stored+ticks*30);return 0;}
            return ticks;
        }
        public bool Restore(DamageSeedSnapshot v)
        {
            if(v==null||!v.point.Valid||!DamageTime.Nonnegative(v.remaining)||v.remaining>3600||!DamageTime.Nonnegative(v.cooldown)||v.cooldown>3600||!DamageTime.Nonnegative(v.nextTick)||v.nextTick>1||v.remaining>0&&v.nextTick==0||v.remaining==0&&v.nextTick!=0||!DamageTime.Nonnegative(v.stored)||!DamageTime.Nonnegative(v.storageLimit)||v.stored>v.storageLimit||!v.compressed&&v.stored!=0)return false;
            _data=v.Copy();return true;
        }
    }
    public sealed class DamageGravityState
    {
        private DamageGravitySnapshot _data=new DamageGravitySnapshot();
        public DamageGravitySnapshot Snapshot=>_data.Copy();
        public bool Active=>_data.remaining>0;
        public bool Begin(DamagePoint point,float duration)
        {
            if(Active||!point.Valid||!DamageTime.Positive(duration)||duration>3600)return false;
            _data=new DamageGravitySnapshot{point=point,remaining=duration,nextTick=1};return true;
        }
        public bool Toggle(out bool grantProtection)
        {
            grantProtection=false;if(!Active)return false;
            _data.expanded=!_data.expanded;
            if(_data.expanded&&!_data.protectionUsed){_data.protectionUsed=true;grantProtection=true;}return true;
        }
        public bool Follow(DamagePoint point,bool secondFinisherHit)
        {if(!Active||!secondFinisherHit||!point.Valid)return false;_data.point=point;return true;}
        public int Tick(float delta)
        {
            if(!Active||!DamageTime.Positive(delta))return 0;
            float elapsed=Math.Min(delta,_data.remaining);
            int ticks=elapsed<_data.nextTick?0:1+(int)Math.Min(3600,Math.Floor(elapsed-_data.nextTick));
            _data.nextTick=_data.nextTick-elapsed+ticks;_data.remaining=Math.Max(0,_data.remaining-delta);
            // Preserve final position/mode until caller processes this tick result.
            return ticks;
        }
        public bool Restore(DamageGravitySnapshot v)
        {
            if(v==null||!v.point.Valid||!DamageTime.Nonnegative(v.remaining)||v.remaining>3600||!DamageTime.Nonnegative(v.nextTick)||v.nextTick>1||v.remaining>0&&v.nextTick==0||v.expanded&&!v.protectionUsed)return false;
            _data=v.Copy();return true;
        }
    }
}
