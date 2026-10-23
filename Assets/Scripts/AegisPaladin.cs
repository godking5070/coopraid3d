using System;
using UnityEngine;
namespace RaidRogue
{
    [DefaultExecutionOrder(450)]
    public sealed class AegisPaladin : MonoBehaviour
    {
        public readonly AegisDamageState DamageState=new AegisDamageState();
        private CharacterActor _actor;private PlayerController _player;private GameObject _gear;
        private Material _metal;private float _debtTextTimer,_debtTextAmount;
        private static readonly Color HolyColor=new Color(1,.88f,.4f);
        public bool IsActive=>_actor?.Definition?.id==AegisPaladinData.JobId;
        public static AegisPaladin Ensure(GameObject go)=>go.GetComponent<AegisPaladin>()??go.AddComponent<AegisPaladin>();
        private void Awake()
        {
            _actor=GetComponent<CharacterActor>();_player=GetComponent<PlayerController>();
            foreach(var slot in CharacterSlots.All)if(slot<CharacterSlot.Identity&&slot!=CharacterSlot.Movement)
            {var captured=slot;_actor.RegisterHandler("aegis."+slot,(skill,target)=>Execute(captured,skill,target));}
        }
        public void ClearTransient(){DamageState.Clear();_debtTextAmount=_debtTextTimer=0;}
        public void ShieldAbsorbed(double amount)
        {if(!RaidCombatAuthority.Replica&&IsActive&&CombatRange.LivingPartyMember(transform)&&DamageState.ShieldAbsorbed(amount))_actor.GainResource(AegisPaladinData.Holy,AegisPaladinData.AbsorbHoly);}
        private void Update()
        {
            if(_gear!=null)_gear.SetActive(IsActive);
            if(!IsActive)return;if(_gear==null)BuildGear();
            if(RaidCombatAuthority.Replica||StageManager.CombatSuspended||_player==null||_player.IsDead)return;
            float due=(float)DamageState.Advance(Time.deltaTime);
            if(due>0){_player.ApplyDeferredDamage(due);_debtTextAmount+=due;}
            _debtTextTimer+=Time.deltaTime;
            if(_debtTextTimer>=1){if(_debtTextAmount>.01f)FloatingTextPopup.Spawn(transform.position+Vector3.up*2,"지연 피해 -"+_debtTextAmount.ToString("0.#"),new Color(1,.65f,.2f),false);_debtTextTimer=_debtTextAmount=0;}
        }
        private bool Execute(CharacterSlot slot,CharacterSkillDefinition skill,Transform target)
        {
            if(!IsActive||RaidCombatAuthority.Replica||_player==null||_player.IsDead||StageManager.CombatSuspended)return false;
            if(slot==CharacterSlot.Job1){DamageState.Activate();FloatingTextPopup.Spawn(transform.position+Vector3.up*2,"불굴의 신념 · 피해 분산",HolyColor,false);return true;}
            if(!CombatRange.LivingEnemy(target)||!CombatRange.InRange(transform,target,skill.range))return false;
            if(slot==CharacterSlot.Job2)
            {
                var boss=target.GetComponent<BossController>();if(boss!=null){boss.AddCombatThreat(transform,0,true);boss.SetAggroTarget(transform);}
                else target.GetComponent<BossAddMinion>().TauntBy(transform,_actor.Definition.name);
                foreach(var member in PartyLife.OrderedMembers())
                {
                    Vector3 delta=member.position-transform.position;delta.y=0;
                    if(!CombatRange.LivingPartyMember(member)||delta.magnitude>AegisPaladinData.CallRadius)continue;
                    CombatStatusEffects.Ensure(member.gameObject).CleanseOne(AegisPaladinData.DispelTags);
                    float max=member.GetComponent<PlayerController>()?.maxHp??member.GetComponent<PartyBotAI>().maxHp;
                    CombatStatusEffects.Shield(member,transform,skill.id,max*AegisPaladinData.CallShieldRatio,AegisPaladinData.CallShieldDuration);
                }
                _player.PlaySkillLineVisual(target.position,HolyColor);return true;
            }
            if(slot==CharacterSlot.Counter)
            {
                Vector3 forwardTarget=target.position-transform.position;forwardTarget.y=0;
                if(Vector3.Dot(transform.forward,forwardTarget.normalized)<0)return false;
                bool counter=CombatCounterTarget.Ensure(target).CanCounter(transform,skill.range);
                if(!Hit(target,skill.name,AegisPaladinData.CounterPower,0,1))return false;
                if(!counter||!CombatCounterTarget.Ensure(target).TryCounter(transform,skill.range,AegisPaladinData.CounterStagger))
                    CombatCounterTarget.Ensure(target).AddStagger(AegisPaladinData.CounterStagger);
                return true;
            }
            if(slot==CharacterSlot.OffGcd1)
            {
                Vector3 center=target.position;
                if(!Hit(target,skill.name,AegisPaladinData.BurstPower,AegisPaladinData.BurstStagger,AegisPaladinData.BurstThreatMultiplier))return false;
                var boss=StageManager.Instance?.boss;
                if(boss!=null&&boss.transform!=target&&InsideBurst(center,boss.transform))Hit(boss.transform,skill.name,AegisPaladinData.BurstPower,AegisPaladinData.BurstStagger,AegisPaladinData.BurstThreatMultiplier);
                foreach(var add in FindObjectsByType<BossAddMinion>(FindObjectsSortMode.None))if(add.transform!=target&&InsideBurst(center,add.transform))Hit(add.transform,skill.name,AegisPaladinData.BurstPower,AegisPaladinData.BurstStagger,AegisPaladinData.BurstThreatMultiplier);
                Pulse(center,AegisPaladinData.BurstRadius);return true;
            }
            var state=_actor.Snapshot;bool sword=slot==CharacterSlot.Gcd1;int stage=sword?state.combo1:state.combo2;
            string name=(sword?AegisPaladinData.SwordNames:AegisPaladinData.ShieldNames)[stage-1];
            int power=(sword?AegisPaladinData.SwordPower:AegisPaladinData.ShieldPower)[stage-1];
            float stagger=(sword?AegisPaladinData.SwordStagger:AegisPaladinData.ShieldStagger)[stage-1];
            if(!Hit(target,name,power,stagger,1))return false;
            if(stage==3)
            {
                if(sword){_actor.GainResource(AegisPaladinData.Holy,AegisPaladinData.FinisherHoly);_actor.ReduceCooldowns(AegisPaladinData.ReducedSkills,AegisPaladinData.CooldownReduction);}
                else CombatStatusEffects.Shield(transform,transform,skill.id,_player.maxHp*AegisPaladinData.FinisherShieldRatio,AegisPaladinData.FinisherShieldDuration);
            }
            return true;
        }
        private bool InsideBurst(Vector3 center,Transform enemy)
        {Vector3 delta=enemy.position-center;delta.y=0;return CombatRange.LivingEnemy(enemy)&&delta.magnitude<=AegisPaladinData.BurstRadius+CombatRange.Radius(enemy);}
        private bool Hit(Transform enemy,string name,int power,float stagger,float threat)
        {
            if(!CombatRange.LivingEnemy(enemy))return false;
            int damage=Mathf.RoundToInt(power*_player.combatController.GetPlayerDamageBuffMultiplier());
            float before,after;var boss=enemy.GetComponent<BossController>();
            if(boss!=null){before=boss.currentHp;boss.ReceiveRoleSkillAttack("이지스 팔라딘",name,0,damage,HolyColor,false,source:transform);after=boss.currentHp;}
            else {var add=enemy.GetComponent<BossAddMinion>();before=add.currentHp;add.ReceivePlayerAttack("이지스 팔라딘",name,damage,HolyColor,false,source:transform);after=add.currentHp;}
            float actual=Mathf.Max(0,before-after);if(actual<=0)return false;
            if(boss!=null&&threat>1)boss.AddCombatThreat(transform,actual*(threat-1));
            if(stagger>0)CombatCounterTarget.Ensure(enemy).AddStagger(stagger);
            _player.FaceAttackTarget(enemy);_player.PlaySkillLineVisual(enemy.position,HolyColor);
            if(_player.hasVampiricBlade)_player.TriggerVampiricBladeHeal();return true;
        }
        private void Pulse(Vector3 center,float radius)
        {
            // Cosmetic lines only. Host relay preserves exactly-once damage separation.
            for(int i=0;i<16;i++){float a=i*Mathf.PI/8,b=(i+1)*Mathf.PI/8;Vector3 p=new Vector3(center.x,.15f,center.z)+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*radius,q=new Vector3(center.x,.15f,center.z)+new Vector3(Mathf.Cos(b),0,Mathf.Sin(b))*radius;
                if(RaidCombatAuthority.Host)RaidNetworkSession.Instance.BroadcastEffect(p,q,HolyColor,"",false,1);
                var obj=new GameObject("DawnlightPulse");var line=obj.AddComponent<LineRenderer>();line.positionCount=2;line.SetPosition(0,p);line.SetPosition(1,q);line.startWidth=line.endWidth=.06f;line.startColor=line.endColor=HolyColor;line.sharedMaterial=new Material(Shader.Find("Sprites/Default"));obj.AddComponent<RaidTransientVisual>();
            }
        }
        private void BuildGear()
        {
            _gear=new GameObject("AegisSwordAndShield");_gear.transform.SetParent(transform,false);
            _metal=new Material(Shader.Find("Sprites/Default"));_metal.color=HolyColor;
            Piece("Sword",new Vector3(.55f,.25f,.55f),new Vector3(.12f,.14f,1.15f));
            Piece("Guard",new Vector3(.55f,.25f,.05f),new Vector3(.45f,.16f,.12f));
            Piece("GreatShield",new Vector3(-.6f,.1f,.35f),new Vector3(.65f,1.1f,.16f));
        }
        private void Piece(string name,Vector3 position,Vector3 size)
        {var p=GameObject.CreatePrimitive(PrimitiveType.Cube);p.name=name;p.transform.SetParent(_gear.transform,false);p.transform.localPosition=position;p.transform.localScale=size;Destroy(p.GetComponent<Collider>());p.GetComponent<Renderer>().sharedMaterial=_metal;}
        private void OnDestroy(){if(_metal!=null)Destroy(_metal);}
    }
}
