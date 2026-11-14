using System;
using System.Linq;

namespace RaidRogue
{
    [Serializable] public sealed class NineKeyTankSnapshot
    {
        public int version=1, bossId, lastAmmo=1, linkedAmmo, chargeChain, splitShots, whirlPulses,lastCombo;
        public int[] ammo=Array.Empty<int>();
        public float reserved,chargeTime,chargeLimit,chargeDiscount,prepared,whirlTime,whirlTick,whirlWave,danceTime,danceReady,linkTime;
        public int danceShots;
        public bool charging,whirling;
        public NineKeyTankSnapshot Copy(){var n=(NineKeyTankSnapshot)MemberwiseClone();n.ammo=(int[])ammo.Clone();return n;}
    }
    public readonly struct TankShot
    {
        public readonly int First,Second;
        public bool Cooperation=>First==2&&Second==1;
        public bool Stagger=>Second==2;
        public TankShot(int first,int second){First=first;Second=second;}
    }
    // Pure authoritative state. Resources live only in CharacterRuntimeState;
    // reservations are removed immediately and returned at most once.
    public sealed class NineKeyTankState
    {
        private readonly CharacterRuntimeState _actor;
        private readonly RosterJob _job;
        private NineKeyTankSnapshot _data=new NineKeyTankSnapshot();
        public NineKeyTankSnapshot Snapshot=>_data.Copy();
        private int Route=>Array.FindIndex(_actor.Definition.routes,r=>r.id==_actor.RouteId)+1;
        private bool Blood=>_job.index==1;
        private bool Core(int route,int variant)=>_actor.Snapshot.coreTrait==NineKeyTankData.Core(_job,route,variant);
        public NineKeyTankState(CharacterRuntimeState actor,RosterJob job)
        {if(!NineKeyTankData.Supports(job))throw new ArgumentException("tank job");_actor=actor;_job=job;ResetAmmo();}
        private void ResetAmmo(){_data.ammo=Blood?Array.Empty<int>():Enumerable.Repeat(1,(int)_actor.Resource(_job.Resource)).ToArray();}
        // Explicit development-resource edits preserve the existing FIFO prefix.
        // Normal combat must use Load/Take so counts and ordering change together.
        public void SynchronizeExternalResources()
        {
            if(Blood)return;
            int count=Math.Max(0,Math.Min(4,(int)_actor.Resource(_job.Resource)));
            _data.ammo=_data.ammo.Take(count).Concat(Enumerable.Repeat(Route==1?_data.lastAmmo:1,Math.Max(0,count-_data.ammo.Length))).ToArray();
        }
        private void Load(int type,bool combo)
        {
            if(_actor.Resource(_job.Resource)>=4)return;
            if(_actor.Gain(_job.Resource,1))
            {
                _data.ammo=_data.ammo.Concat(new[]{type}).ToArray();
                if(combo)_data.lastAmmo=type; // Full magazine never completed a new reload.
            }
        }
        private bool Take(int count,out int[] ammo)
        {
            ammo=Array.Empty<int>();
            if(count<1||_data.ammo.Length<count||_actor.Resource(_job.Resource)<count)return false;
            if(!_actor.Spend(_job.Resource,count))return false;
            ammo=_data.ammo.Take(count).ToArray();_data.ammo=_data.ammo.Skip(count).ToArray();return true;
        }
        public bool EmergencyReload()
        {
            if(Blood||_data.ammo.Length>=4)return false;
            Load(Route==1?_data.lastAmmo:1,false);return true;
        }
        // Caller invokes once for a successful direct final hit, never a DoT/clone.
        public void FinishCombo(int chain)
        {
            if(chain!=1&&chain!=2)return;
            _data.lastCombo=chain;
            if(Blood)
            {
                _actor.Gain(_job.Resource,20);
                if(Route==2&&chain==1)_data.prepared=1;
            }
            else if(Route==1)Load(chain==1?1:2,true);
            else if(Route==2)Load(1,true);
            else if(Route==3&&chain==1)
            {
                Load(1,true);float heat=_actor.Resource(NineKeyTankData.Heat(_job));
                _actor.Spend(NineKeyTankData.Heat(_job),Math.Min(15,heat));
            }
            _actor.Gain(NineKeyTankData.Gauge(_job),10);
        }
        // C3 spends at attack emission, including misses. Never called at hit again.
        public bool FireFurnaceSecond()
        {
            if(Blood||Route!=3||!Take(1,out _))return false;
            _actor.Gain(NineKeyTankData.Heat(_job),30);return true;
        }
        public bool InComfortBand
        {
            get{float heat=_actor.Resource(NineKeyTankData.Heat(_job));return !Blood&&Route==3&&heat>=(Core(3,0)?30:40)&&heat<=(Core(3,0)?75:70);}
        }
        public bool Vent(out bool hot)
        {
            hot=false;if(Blood||Route!=3)return false;
            float heat=_actor.Resource(NineKeyTankData.Heat(_job));if(heat<40)return false;
            hot=heat>=80;return _actor.Spend(NineKeyTankData.Heat(_job),40);
        }
        public bool Barrage(out TankShot[] shots)
        {
            shots=Array.Empty<TankShot>();if(Blood||Route!=1)return false;
            int count=Core(1,0)?1:Core(1,1)?4:2;
            if(!Take(count,out var ammo))return false;
            if(count==1)
            {
                int previous=_data.linkTime>0?_data.linkedAmmo:0;
                shots=new[]{new TankShot(previous,ammo[0])};
                if(previous==0){_data.linkedAmmo=ammo[0];_data.linkTime=4;}
                else{_data.linkedAmmo=0;_data.linkTime=0;}
            }
            else shots=Enumerable.Range(0,count/2).Select(i=>new TankShot(ammo[i*2],ammo[i*2+1])).ToArray();
            return true;
        }
        public bool StartDance()
        {
            if(Blood||Route!=2||_data.danceTime>0||!Take(2,out _))return false;
            _data.danceTime=8;_data.danceShots=3;_data.danceReady=0;return true;
        }
        public void PrepareDance(bool directHit)
        {if(directHit&&_data.danceTime>0)_data.danceReady=Core(2,0)?4:Core(2,1)?2:3;}
        public bool FireDance(out bool last)
        {
            last=false;if(_data.danceTime<=0||_data.danceReady<=0||_data.danceShots<=0)return false;
            _data.danceReady=0;_data.danceShots--;last=_data.danceShots==0;
            if(last)_data.danceTime=0;return true;
        }
        public bool StartCharge(int chain)
        {
            if(!Blood||Route!=2||_data.charging||chain!=0&&chain!=2)return false;
            if(chain==0&&_data.reserved==0)
            {
                if(!_actor.Spend(_job.Resource,60))return false;
                _data.reserved=60;_data.splitShots=Core(2,0)?2:1;
            }
            _data.charging=true;_data.chargeChain=chain;_data.chargeTime=0;
            _data.chargeLimit=chain==2?.8f:Core(2,0)?1.2f:Core(2,1)?4:3;
            _data.chargeDiscount=_data.prepared>0?.35f:0;_data.prepared=0;return true;
        }
        public bool ReleaseCharge(out int stage,out int chain)
        {
            stage=chain=0;if(!_data.charging)return false;
            chain=_data.chargeChain;
            stage=1+(int)Math.Floor(Math.Max(0,_data.chargeTime+_data.chargeDiscount));
            stage=Math.Min(chain==2?2:Core(2,1)?4:3,stage);
            _data.charging=false;_data.chargeTime=0;
            if(chain==0)
            {
                float cost=_data.reserved/Math.Max(1,_data.splitShots);
                _data.reserved-=cost;_data.splitShots--; // A fired portion can never refund.
            }
            return true;
        }
        public void CancelCharge()
        {
            float refund=_data.reserved;_data.reserved=0;_data.splitShots=0;
            _data.charging=false;_data.chargeTime=0;_data.chargeDiscount=0;
            if(refund>0)_actor.Gain(_job.Resource,refund);
        }
        public bool StartWhirl()
        {
            if(!Blood||Route!=3||_data.whirling||_actor.Resource(_job.Resource)<3)return false;
            _data.whirling=true;_data.whirlTime=0;_data.whirlTick=0;_data.whirlWave=0;_data.whirlPulses=0;return true;
        }
        public void StopWhirl(){_data.whirling=false;_data.whirlTime=_data.whirlTick=_data.whirlWave=0;}
        // 0.2s attack intervals. A caller receives only ticks whose cost was paid.
        public void Advance(float delta,out int paidTicks,out int waves)
        {
            paidTicks=waves=0;if(!CharacterDefinitionValidation.Finite(delta)||delta<=0)return;
            if(_data.charging)_data.chargeTime=Math.Min(_data.chargeLimit,_data.chargeTime+delta);
            _data.linkTime=Math.Max(0,_data.linkTime-delta);if(_data.linkTime==0)_data.linkedAmmo=0;
            _data.danceTime=Math.Max(0,_data.danceTime-delta);_data.danceReady=Math.Max(0,_data.danceReady-delta);
            if(_data.danceTime==0){_data.danceShots=0;_data.danceReady=0;}
            if(!_data.whirling)return;
            float elapsed=Math.Min(delta,6-_data.whirlTime);_data.whirlTick+=elapsed;_data.whirlTime+=elapsed;
            while(_data.whirlTick+.00001f>=.2f)
            {
                _data.whirlTick=Math.Max(0,_data.whirlTick-.2f);
                if(!_actor.Spend(_job.Resource,3)){StopWhirl();break;}
                paidTicks++;_data.whirlWave+=.2f;
                float interval=Core(3,0)&&_data.whirlPulses==0?1:2;
                if(_data.whirlWave+.00001f>=interval){waves++;_data.whirlPulses++;_data.whirlWave=0;}
            }
            if(_data.whirlTime>=6)StopWhirl();
        }
        public void Clear(bool refund)
        {if(refund)CancelCharge();_data=new NineKeyTankSnapshot();ResetAmmo();}
        public bool Restore(NineKeyTankSnapshot value)
        {
            if(value==null||value.version!=1||value.ammo==null||value.ammo.Length>4||value.ammo.Any(a=>a!=1&&a!=2)||
                value.lastAmmo<1||value.lastAmmo>2||value.linkedAmmo<0||value.linkedAmmo>2||value.danceShots<0||value.danceShots>3||
                value.splitShots<0||value.splitShots>2||value.lastCombo<0||value.lastCombo>2||value.whirlPulses<0||value.whirlPulses>3||value.chargeChain!=0&&value.chargeChain!=2)return false;
            float[] numbers={value.reserved,value.chargeTime,value.chargeLimit,value.chargeDiscount,value.prepared,value.whirlTime,value.whirlTick,value.whirlWave,value.danceTime,value.danceReady,value.linkTime};
            if(numbers.Any(n=>!CharacterDefinitionValidation.Finite(n)||n<0)||value.reserved>60||value.chargeLimit>4||value.chargeTime>value.chargeLimit||value.chargeDiscount>.35f||value.prepared>1||value.whirlTime>6||value.whirlTick>=.2f||value.whirlWave>2||value.danceTime>8||value.danceReady>4||value.linkTime>4)return false;
            if(Blood?(value.ammo.Length!=0||value.danceTime>0||value.linkTime>0):(value.ammo.Length!=(int)_actor.Resource(_job.Resource)||value.reserved>0||value.charging||value.whirling))return false;
            if(value.charging&&(Route!=2||value.chargeChain==0&&value.reserved<=0)||value.whirling&&Route!=3)return false;
            if(value.reserved>0&&(Route!=2||value.splitShots==0)||value.reserved==0&&value.splitShots>0)return false;
            if(!Blood&&(Route!=1&&value.linkTime>0||Route!=2&&value.danceTime>0))return false;
            if(Blood&&(value.linkedAmmo!=0||value.danceShots!=0||value.danceReady!=0)||
                !Blood&&(value.chargeTime!=0||value.chargeLimit!=0||value.chargeDiscount!=0||value.prepared!=0||value.chargeChain!=0||value.whirlTime!=0||value.whirlTick!=0||value.whirlWave!=0||value.whirlPulses!=0))return false;
            if(value.reserved>0&&!(Core(2,0)?value.reserved==60&&value.splitShots==2||value.reserved==30&&value.splitShots==1:value.reserved==60&&value.splitShots==1))return false;
            if((value.danceTime>0)!=(value.danceShots>0)||value.danceTime==0&&value.danceReady>0||
                value.danceReady>(Core(2,0)?4:Core(2,1)?2:3))return false;
            if((value.linkTime>0)!=(value.linkedAmmo>0)||value.linkTime>0&&!Core(1,0))return false;
            _data=value.Copy();return true;
        }
    }
}
