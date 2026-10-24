using System;
using System.Collections.Generic;
using UnityEngine;
namespace RaidRogue
{
    [DefaultExecutionOrder(460)]
    public sealed class AegisExtensions : MonoBehaviour
    {
        private AegisPaladin _paladin;private CharacterActor _actor;private PlayerController _player;
        private readonly PlayerMovementCollision _collision=new PlayerMovementCollision();
        private LineRenderer _ground,_wings,_link;private Material _material;
        private AegisExtensionState State=>_paladin.DamageState.Extensions;
        private AegisExtensionSnapshot Data=>State.Data;
        private float Now=>RaidCombatAuthority.ServerTime;
        public bool Active=>_paladin!=null&&_paladin.IsActive;
        public bool Channeling=>Active&&Data.wingEnd>Now&&CombatRange.LivingPartyMember(transform);
        public static AegisExtensions Ensure(GameObject go)=>go.GetComponent<AegisExtensions>()??go.AddComponent<AegisExtensions>();
        private void Awake()
        {_paladin=GetComponent<AegisPaladin>();_actor=GetComponent<CharacterActor>();_player=GetComponent<PlayerController>();_actor.RegisterHandler("aegis.extension",Execute);}
        public void CancelChannel(){if(!RaidCombatAuthority.Replica)State.CancelWings();}
        public static int PartyId(Transform unit)=>unit==null?0:RaidCombatAuthority.Active?RaidCombatAuthority.Id(unit):unit.GetInstanceID();
        public static Transform PartyTarget(int id)=>RaidCombatAuthority.Active?RaidCombatAuthority.Resolve(id):PartyLife.OrderedMembers().Find(t=>t.GetInstanceID()==id);
        private bool ValidChannel()
        {
            if(!Channeling||_player==null||_player.IsStunned||_player.IsMovementLocked||_player.IsBeingRescued||_player.IsBeingKnockedBack)return false;
            Vector2 delta=new Vector2(transform.position.x-Data.wingX,transform.position.z-Data.wingZ);return delta.sqrMagnitude<.01f;
        }
        public bool Protects(Transform member)
        {
            if(member==transform||!CombatRange.LivingPartyMember(member)||!ValidChannel())return false;
            return AegisExtensionData.InCone(member.position.x-Data.wingX,member.position.z-Data.wingZ,AegisExtensionData.WingsRadius,Data.wingYaw+180,AegisExtensionData.WingsAngle);
        }
        public Transform LinkedTarget=>Data.interventionEnd>Now?PartyTarget(Data.interventionTarget):null;
        public static float SharedDamage(Transform recipient,float confirmed,CombatHitContext hit)
        {
            if(RaidCombatAuthority.Replica||AegisExtensionData.SharedDamage(confirmed,hit.Kind,true)<=0)return confirmed;
            foreach(var member in PartyLife.OrderedMembers())
            {
                if(member==recipient||!CombatRange.LivingPartyMember(member))continue;
                var owner=member.GetComponent<AegisExtensions>();
                if(owner==null||!owner.Active||owner.LinkedTarget!=recipient||owner._player==null)continue;
                float transferred=(float)AegisExtensionData.SharedDamage(confirmed,hit.Kind,true);
                // Child damage is already mitigated. Its distinct kind prevents transfer/reflect/Holy recursion.
                owner._player.TakeDamage(transferred,new CombatHitContext(hit.Attacker,AegisExtensionData.Intervention+"/"+hit.SkillId,CombatDamageKind.Transfer));
                return confirmed-transferred; // Only one guardian; never additive multiple transfers.
            }
            return confirmed;
        }
        public static float IncomingScale(Transform member,CombatDamageKind kind)
        {
            if(kind==CombatDamageKind.System||kind==CombatDamageKind.Deferred||kind==CombatDamageKind.Transfer)return 1;
            bool wings=false;foreach(var unit in PartyLife.OrderedMembers())if(unit.GetComponent<AegisExtensions>()?.Protects(member)==true){wings=true;break;}
            return AegisExtensionData.DamageScale(wings,member.GetComponent<CombatStatusEffects>()?.MorningActive==true);
        }
        public string Unavailable(CharacterSkillDefinition skill,Transform target)
        {
            if(skill==null)return "선택한 스킬이 없습니다";
            if(skill.id==AegisExtensionData.Intervention&&target==transform)return "다른 살아 있는 아군을 선택하세요";
            if(skill.id==AegisExtensionData.Retribution&&(_player==null||_player.currentShield<=0))return "유효한 보호막이 필요합니다";
            if(skill.id==AegisExtensionData.Retribution&&EnemiesInCone().Count==0)return "전방 범위에 유효한 적이 없습니다";
            if(Channeling)return "결계의 날개 수호 중";return "";
        }
        private bool Execute(CharacterSkillDefinition skill,Transform target)
        {
            if(!Active||RaidCombatAuthority.Replica||StageManager.CombatSuspended||_player==null||_player.IsDead||_player.IsStunned||Channeling)return false;
            float now=Now;
            switch(skill.id)
            {
                case AegisExtensionData.Spear:
                    return Attack(target,skill,AegisExtensionData.SpearPower,AegisExtensionData.SpearStagger);
                case AegisExtensionData.Ground:
                    State.StartGround(now,transform.position.x,transform.position.z);Notice(skill.name);return true;
                case AegisExtensionData.Wings:
                    if(_player.IsMovementLocked||_player.IsBeingRescued||_player.IsBeingKnockedBack||_player.IsDashing||_player.IsSkillRushing)return false;
                    State.StartWings(now,transform.position.x,transform.position.z,transform.eulerAngles.y);Notice(skill.name);return true;
                case AegisExtensionData.Intervention:
                    if(!CombatRange.LivingPartyMember(target)||target==transform||!CombatRange.InRange(transform,target,skill.range)||!Approach(target,out Vector3 destination))return false;
                    Vector3 before=transform.position;transform.position=destination;
                    if(RaidCombatAuthority.Active)RaidNetworkSession.Instance.CommitSkillRelocation(transform);
                    _player.targetBoss?.TryInterceptPlayerMovement(_player,before,destination);
                    State.Link(PartyId(target),now);_player.PlaySkillLineVisual(target.position,Color.cyan);return true;
                case AegisExtensionData.Brand:
                    if(!Attack(target,skill,AegisExtensionData.BrandPower,0))return false;
                    if(CombatRange.LivingEnemy(target))CombatStatusEffects.Ensure(target.gameObject).ApplyBrand(now+AegisExtensionData.BrandDuration);return true;
                case AegisExtensionData.Retribution:
                    _player.ExpireTimedShields();float shield=_player.currentShield;
                    if(shield<=0)return false;var enemies=EnemiesInCone();if(enemies.Count==0)return false;
                    bool hit=false;foreach(var enemy in enemies)hit|=_paladin.DealAttack(enemy,skill.id,skill.name,shield*AegisExtensionData.ShieldDamageScale,0,1,CombatDamageKind.Direct,false)>0;
                    if(hit)ConePulse();return hit;
                case AegisExtensionData.Excalibur:
                    if(!Attack(target,skill,AegisExtensionData.ExcaliburPower,100))return false;
                    CombatStatusEffects.Ensure(gameObject).GrantCombatInvulnerability(now+AegisExtensionData.InvulnerabilityDuration);
                    HolyFall(target.position);return true;
                case AegisExtensionData.Morning:
                    if(!Attack(target,skill,AegisExtensionData.MorningPower,100))return false;
                    foreach(var member in PartyLife.OrderedMembers())if(CombatRange.LivingPartyMember(member))CombatStatusEffects.Ensure(member.gameObject).GrantMorning(now+AegisExtensionData.MorningDuration);
                    HolyFall(target.position);return true;
            }
            return false;
        }
        private bool Attack(Transform target,CharacterSkillDefinition skill,float power,float stagger)
        {
            if(!CombatRange.LivingEnemy(target)||!CombatRange.InRange(transform,target,skill.range))return false;
            return _paladin.DealAttack(target,skill.id,skill.name,power,stagger,1)>0;
        }
        private bool Approach(Transform target,out Vector3 end)
        {
            end=transform.position;if(!_player.CanMoveVoluntarily||_player.IsDashing)return false;
            Vector3 radial=transform.position-target.position;radial.y=0;if(radial.sqrMagnitude<.0001f)radial=-target.forward;
            float radius=CombatRange.Radius(transform)+CombatRange.Radius(target)+.2f;
            for(int i=0;i<8;i++)
            {
                Vector3 candidate=target.position+Quaternion.Euler(0,i*45,0)*radial.normalized*radius;candidate.y=transform.position.y;
                if(new Vector2(candidate.x,candidate.z).magnitude>_player.arenaBoundaryRadius-CombatRange.Radius(transform))continue;
                bool overlap=false;
                foreach(var other in PartyLife.OrderedMembers())if(other!=transform&&CombatRange.LivingPartyMember(other)&&Vector2.Distance(new Vector2(candidate.x,candidate.z),new Vector2(other.position.x,other.position.z))<CombatRange.Radius(transform)+CombatRange.Radius(other)+.1f){overlap=true;break;}
                foreach(var enemy in Enemies())if(FlatDistance(candidate,enemy.position)<CombatRange.Radius(transform)+CombatRange.Radius(enemy)+.1f){overlap=true;break;}
                if(overlap)continue;Vector3 delta=candidate-transform.position;
                Vector3 swept=_collision.Move(_player,delta,delta.magnitude,out bool blocked);
                if(blocked||(swept-candidate).sqrMagnitude>.001f)continue;end=candidate;return true;
            }
            return false;
        }
        private static List<Transform> Enemies()
        {
            var result=new List<Transform>();var boss=StageManager.Instance?.boss;
            if(boss!=null&&CombatRange.LivingEnemy(boss.transform))result.Add(boss.transform);
            foreach(var add in FindObjectsByType<BossAddMinion>(FindObjectsSortMode.None))if(CombatRange.LivingEnemy(add.transform))result.Add(add.transform);return result;
        }
        private List<Transform> EnemiesInCone()=>Enemies().FindAll(e=>AegisExtensionData.InCone(e.position.x-transform.position.x,e.position.z-transform.position.z,AegisExtensionData.RetributionRadius,transform.eulerAngles.y,AegisExtensionData.RetributionAngle));
        private void Update()
        {
            if(!Active){HideVisuals();return;}
            if(!RaidCombatAuthority.Replica&&!StageManager.CombatSuspended)
            {
                if(!ValidChannel())State.CancelWings();
                if(!CombatRange.LivingPartyMember(transform)||!CombatRange.LivingPartyMember(LinkedTarget))State.Unlink();
                int ticks=State.TakeGroundTicks(Now);for(int i=0;i<ticks;i++)GroundTick();
            }
            DrawVisuals();
        }
        private void GroundTick()
        {
            if(_player.IsDead||StageManager.CombatSuspended)return;
            Vector3 center=new Vector3(Data.groundX,transform.position.y,Data.groundZ);
            foreach(var enemy in Enemies())if(FlatDistance(center,enemy.position)<=AegisExtensionData.GroundRadius)
                _paladin.DealAttack(enemy,AegisExtensionData.Ground,"축성된 대지",AegisExtensionData.GroundPower,0,1,CombatDamageKind.Periodic);
            foreach(var member in PartyLife.OrderedMembers())if(CombatRange.LivingPartyMember(member)&&FlatDistance(center,member.position)<=AegisExtensionData.GroundRadius)
            {
                var pc=member.GetComponent<PlayerController>();var bot=member.GetComponent<PartyBotAI>();
                if(pc!=null)pc.ReceiveHeal(pc.maxHp*AegisExtensionData.GroundHeal,transform);
                else if(bot!=null)bot.ReceiveHeal(bot.maxHp*AegisExtensionData.GroundHeal,transform);
            }
        }
        private static float FlatDistance(Vector3 a,Vector3 b)=>Vector2.Distance(new Vector2(a.x,a.z),new Vector2(b.x,b.z));
        public string StatusText()
        {
            var items=new List<string>();if(Data.groundEnd>Now)items.Add("축성된 대지 "+(Data.groundEnd-Now).ToString("0.0")+"초");
            if(Channeling)items.Add("결계의 날개 수호 "+(Data.wingEnd-Now).ToString("0.0")+"초");
            if(CombatRange.LivingPartyMember(LinkedTarget))items.Add("수호자의 개입 연결 "+(Data.interventionEnd-Now).ToString("0.0")+"초");return string.Join(" · ",items);
        }
        private void DrawVisuals()
        {
            Shape(ref _ground,"ConsecratedGround",Data.groundEnd>Now,new Vector3(Data.groundX,.14f,Data.groundZ),AegisExtensionData.GroundRadius,0,360,new Color(1,.85f,.4f,.65f));
            Shape(ref _wings,"GuardianWings",Channeling,new Vector3(Data.wingX,.18f,Data.wingZ),AegisExtensionData.WingsRadius,Data.wingYaw+180,AegisExtensionData.WingsAngle,new Color(.4f,.9f,1,.8f));
            var target=LinkedTarget;bool linked=CombatRange.LivingPartyMember(transform)&&CombatRange.LivingPartyMember(target);
            if(linked&&_link==null)_link=Line("InterventionLink");if(_link!=null){_link.enabled=linked;if(linked){_link.positionCount=2;_link.SetPosition(0,transform.position+Vector3.up*.2f);_link.SetPosition(1,target.position+Vector3.up*.2f);_link.startColor=_link.endColor=Color.cyan;}}
        }
        private LineRenderer Line(string name)
        {
            if(_material==null)_material=new Material(Shader.Find("Sprites/Default"));
            var go=new GameObject(name);go.transform.SetParent(transform,false);var line=go.AddComponent<LineRenderer>();line.useWorldSpace=true;line.sharedMaterial=_material;line.startWidth=line.endWidth=.08f;return line;
        }
        private void Shape(ref LineRenderer line,string name,bool visible,Vector3 center,float radius,float yaw,float angle,Color color)
        {
            if(visible&&line==null)line=Line(name);if(line==null)return;line.enabled=visible;if(!visible)return;
            bool circle=angle>=360;line.positionCount=circle?49:51;line.startColor=line.endColor=color;
            int start=circle?0:1;if(!circle){line.SetPosition(0,center);line.SetPosition(50,center);}
            for(int i=0;i<=48;i++){float a=(yaw-angle*.5f+angle*i/48)*Mathf.Deg2Rad;line.SetPosition(i+start,center+new Vector3(Mathf.Sin(a),0,Mathf.Cos(a))*radius);}
        }
        private void HideVisuals(){if(_ground!=null)_ground.enabled=false;if(_wings!=null)_wings.enabled=false;if(_link!=null)_link.enabled=false;}
        private void OnDisable(){HideVisuals();if(!RaidCombatAuthority.Replica&&_paladin!=null)State.Clear();}
        private void OnDestroy(){if(_material!=null)Destroy(_material);}
        private void Notice(string text)=>FloatingTextPopup.Spawn(transform.position+Vector3.up*2,text,new Color(1,.9f,.5f),true);
        private void Pulse(Vector3 center,float radius)=>_paladin.ShowPulse(center,radius);
        private void ConePulse()
        {
            Vector3 center=transform.position;center.y=.17f;Vector3 last=center;
            for(int i=0;i<=13;i++)
            {
                float a=(transform.eulerAngles.y-AegisExtensionData.RetributionAngle*.5f+AegisExtensionData.RetributionAngle*Mathf.Min(i,12)/12)*Mathf.Deg2Rad;
                Vector3 next=i==13?center:center+new Vector3(Mathf.Sin(a),0,Mathf.Cos(a))*AegisExtensionData.RetributionRadius;
                if(RaidCombatAuthority.Host)RaidNetworkSession.Instance.BroadcastEffect(last,next,Color.yellow,"",false,1);
                var go=new GameObject("RetributionCone");var line=go.AddComponent<LineRenderer>();line.positionCount=2;line.SetPosition(0,last);line.SetPosition(1,next);line.startWidth=line.endWidth=.08f;line.startColor=line.endColor=Color.yellow;line.sharedMaterial=new Material(Shader.Find("Sprites/Default"));go.AddComponent<RaidTransientVisual>();last=next;
            }
        }
        private void HolyFall(Vector3 center)
        {
            Pulse(center,2);_player.PlaySkillLineVisual(center,Color.yellow);
            Vector3 top=center+Vector3.up*10;
            if(RaidCombatAuthority.Host)RaidNetworkSession.Instance.BroadcastEffect(top,center,Color.yellow,"",true,1);
            var go=new GameObject("DawnSwordFall");var line=go.AddComponent<LineRenderer>();line.positionCount=2;line.SetPosition(0,top);line.SetPosition(1,center);line.startWidth=.5f;line.endWidth=.08f;line.startColor=line.endColor=Color.yellow;line.sharedMaterial=new Material(Shader.Find("Sprites/Default"));go.AddComponent<RaidTransientVisual>();
        }
    }
    public static class CombatAttackModifiers
    {
        // Base attack power scaling precedes target mitigation. Confirmed secondary and shield-derived damage opt out.
        public static int Apply(Transform source,Transform target,float power,CombatDamageKind kind,bool scaleAttack=true)
        {
            bool primary=kind==CombatDamageKind.Direct||kind==CombatDamageKind.Periodic||kind==CombatDamageKind.Unclassified;
            float scale=primary&&scaleAttack&&source!=null&&source.GetComponent<CombatStatusEffects>()?.MorningActive==true?1+AegisExtensionData.MorningAttack:1;
            bool marked=target!=null&&target.GetComponent<CombatStatusEffects>()?.BrandActive==true;
            bool crit=source?.GetComponent<CharacterKit>()?.Active!=true&&!RaidCombatAuthority.Replica&&primary&&marked&&CombatRange.LivingPartyMember(source)&&UnityEngine.Random.value<AegisExtensionData.CriticalChance(0,true);
            return Mathf.RoundToInt(AegisExtensionData.AttackAmount(power,scale,crit)*CharacterKit.AttackScale(source,target,kind,scaleAttack));
        }
    }
}
