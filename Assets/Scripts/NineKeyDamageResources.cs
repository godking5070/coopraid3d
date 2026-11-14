using System;

namespace RaidRogue
{
    [Serializable] public sealed class DamageResourceSnapshot
    {
        public int version=1,lastCompletedChain,lastUsedChain;
        public bool projectedSecond;
        public string reservation="";
        public float reserved;
        public DamageResourceSnapshot Copy() => (DamageResourceSnapshot)MemberwiseClone();
    }
    // Framework balances are authoritative. This stores only provenance and an
    // already deducted reservation, never a second spendable copy of the balance.
    public sealed class NineKeyDamageResources
    {
        private readonly CharacterRuntimeState _actor;
        private readonly RosterJob _job;
        private DamageResourceSnapshot _state=new DamageResourceSnapshot();
        public DamageResourceSnapshot Snapshot => _state.Copy();
        private int Route => Array.FindIndex(_actor.Definition.routes,r=>r.id==_actor.RouteId)+1;
        public NineKeyDamageResources(CharacterRuntimeState actor,RosterJob job)
        {
            if(actor==null||!NineKeyDamageData.Supports(job)||actor.Definition.id!=job.Id)throw new ArgumentException("damage actor");
            _actor=actor;_job=job;
        }
        public void MarkProjectedSecond()
        {if(_job.index==11&&Route==2)_state.projectedSecond=true;}
        // A launched final step ends this combo even when it misses. Clearing
        // provenance must not grant resources, gauge or a successful completion.
        public void EndSecondCombo()
        {_state.projectedSecond=false;}
        public void RecordComboUse(int chain)
        {if(chain==1||chain==2)_state.lastUsedChain=chain;}
        public bool FinishDance(out int beats)
        {
            beats=0;if(_job.index!=6||Route!=2)return false;
            int current=(int)_actor.Resource(_job.Resource);
            if(current<=0||!_actor.Spend(_job.Resource,current))return false;
            beats=current;_actor.EndCombo(_state.lastUsedChain);_state.lastUsedChain=0;return true;
        }
        // Caller must first claim the valid direct action (including delayed
        // projectile receipts). Do not call once per hit or for automatic damage.
        public bool DirectCombo(int chain,int step,int secondChoice,bool accelerated=false)
        {
            if(chain<1||chain>2||step<1||step>3||secondChoice<0||secondChoice>2)return false;
            RecordComboUse(chain);
            float amount=0;
            if(_job.index==6)amount=Route==2?1:step==3?1:0;
            if(_job.index==7)
            {
                amount=chain==1||secondChoice==0?(step==3?20:10):secondChoice==1?(step==3?10:5):10;
                if(Route==2&&step==3&&_state.lastCompletedChain!=0&&_state.lastCompletedChain!=chain)amount+=10;
                if(chain==1&&step==3&&_actor.HasAuxiliary("tempest.aux.chain"))amount+=10;
                if(Route==2&&accelerated)amount*=.5f;
            }
            if(_job.index==8&&Route!=1&&step==3)amount=chain==1?2:1;
            if((_job.index==9||_job.index==10)&&step==3)amount=20;
            if(_job.index==11&&step==3)amount=chain==2&&Route==2&&_state.projectedSecond?0:1;
            if(amount>0)_actor.Gain(_job.Resource,amount);
            if(step==3)
            {
                _state.lastCompletedChain=chain;
                if(chain==2)_state.projectedSecond=false;
                _actor.Gain(NineKeyDamageData.Gauge(_job),10);
            }
            return true;
        }
        public bool Supply()
        {
            // Peacekeeper supplies each hand through its hand state instead.
            if(_job.index==8&&Route==1)return false;
            float amount=_job.index==6||_job.index==11?1:_job.index==8?2:_job.index==10?20:30;
            float before=_actor.Resource(_job.Resource);
            return _actor.Gain(_job.Resource,amount)&&_actor.Resource(_job.Resource)>before;
        }
        public bool Reserve(string key,float amount)
        {
            if(_state.reserved>0||!ValidReservation(key,amount)||!_actor.Spend(_job.Resource,amount))return false;
            _state.reservation=key;_state.reserved=amount;return true;
        }
        public bool Commit(string key)
        {
            if(_state.reserved<=0||key!=_state.reservation)return false;
            _state.reserved=0;_state.reservation="";return true;
        }
        public bool Cancel(string key)
        {
            float refund=_state.reserved;if(!Commit(key))return false;
            _actor.Gain(_job.Resource,refund);return true;
        }
        public bool Restore(DamageResourceSnapshot value)
        {
            if(value==null||value.version!=1||value.lastCompletedChain<0||value.lastCompletedChain>2||value.lastUsedChain<0||value.lastUsedChain>2||value.reservation==null||
                !CharacterDefinitionValidation.Finite(value.reserved)||value.reserved<0||value.reserved>100||
                (value.reserved>0)!=(!string.IsNullOrEmpty(value.reservation))||value.projectedSecond&&(_job.index!=11||Route!=2))return false;
            // Only these routes reserve main resource. Replica restore never refunds.
            if(value.reserved>0&&!ValidReservation(value.reservation,value.reserved))return false;
            _state=value.Copy();return true;
        }
        private bool ValidReservation(string key,float amount)=>CharacterDefinitionValidation.Finite(amount)&&amount>0&&
            (_job.index==8&&Route==3&&key=="snipe"&&amount<=6&&amount==Math.Floor(amount)||
             _job.index==10&&Route==1&&key=="meteor"&&amount<=100);
    }
}
