using System;
using System.Globalization;
using UnityEngine;

namespace RaidRogue
{
    public sealed partial class NineKeyDamageKit
    {
        private readonly AegisRecallInput _identityInput=new AegisRecallInput();
        private void ResetInput()
        {
            _identityInput.Step(false,false,0,true);
            _prismPressed=false;_prismSelected=0;
        }
        public bool UsesPrismInput=>Active&&Job.index==11&&Route==3;
        public DamagePrismCommand AiPrismCommand(Transform enemy)
        {
            if(!UsesPrismInput||!CombatRange.LivingEnemy(enemy))return null;Bind();
            int maximum=Core(3,0)?1:2;
            if(DamagePrismState.ReflectionsToHit(PrismPath(),Point(enemy.position),CombatRange.Radius(enemy))>=maximum)return null;
            var command=_prism.Plan(Point(transform.position),Point(enemy.position),maximum);
            return command!=null&&CanUsePrism(command)&&(command.mirrorId!=0||_runtime.Resource(Job.Resource)>=1)?command:null;
        }
        private bool _prismPressed;
        private Vector3 _prismStart;
        private int _prismSelected;
        public bool CanUsePrism(DamagePrismCommand command)
        {
            if(!UsesPrismInput||command==null||!command.Valid)return false;Bind();
            if(!CanPlace(World(command.point)))return false;
            return command.mirrorId==0?_prism.Snapshot.nextId<int.MaxValue:_prism.CanAdjust(command.mirrorId);
        }
        private bool UsePrism(DamagePrismCommand command)
        {
            if(!CanUsePrism(command))return false;
            if(command.mirrorId!=0)
            {
                if(!_prism.Adjust(command.mirrorId,command.point,command.normal,.25f))return false;
                return _prism.SelectFirst(command.mirrorId);
            }
            if(!Spend(1))return false;
            return _prism.Place(command.point,command.normal,Core(3,0)?1:2,20);
        }
        private bool PrismCursor(out Vector3 point)
        {
            point=default;var camera=Camera.main;
            if(camera==null||LocalGameplayInput.PointerOverUI(LocalGameplayInput.MousePosition))return false;
            var ray=camera.ScreenPointToRay(LocalGameplayInput.MousePosition);
            if(!new Plane(Vector3.up,transform.position).Raycast(ray,out float distance))return false;
            point=ray.GetPoint(distance);return CanPlace(point);
        }
        private void UpdatePrismInput(bool blocked,string binding)
        {
            if(blocked){_prismPressed=false;return;}Bind();
            if(LocalInputBindings.Pressed(binding))
            {
                _prismPressed=PrismCursor(out _prismStart);_prismSelected=0;
                if(!_prismPressed)return;
                float closest=1.25f*1.25f;
                foreach(var mirror in _prism.Snapshot.mirrors)
                {
                    float distance=(World(mirror.point)-_prismStart).sqrMagnitude;
                    if(distance<closest){closest=distance;_prismSelected=mirror.id;}
                }
            }
            if(!_prismPressed||LocalInputBindings.Held(binding))return;
            _prismPressed=false;if(!PrismCursor(out var end))return;
            var command=PrismGesture(end);
            if(!CanUsePrism(command))return;
            if(RaidCombatAuthority.Active)RaidNetworkSession.Instance.RequestCharacterAction(10,_actor.Definition.id,(int)CharacterSlot.Identity,0,"damage.prism",command.Encode());
            else if(!_actor.Execute(CharacterSlot.Identity,null,out string failure,prism:command))GetComponent<PlayerController>()?.combatController?.ReportNetworkFailure(failure);
        }
        private DamagePrismCommand PrismGesture(Vector3 end)
        {
            Vector3 direction=end-_prismStart;direction.y=0;
            Vector3 point=_prismSelected==0?_prismStart:end;
            if(direction.sqrMagnitude<.04f)
            {
                direction=transform.forward;
                foreach(var mirror in _prism.Snapshot.mirrors)if(mirror.id==_prismSelected){point=World(mirror.point);direction=new Vector3(mirror.normal.x,0,mirror.normal.z);}
            }
            direction.Normalize();
            return new DamagePrismCommand{mirrorId=_prismSelected,point=Point(point),normal=Point(direction)};
        }
        private DamagePoint[] PrismPath()
        {
            return DamagePrismState.Clip(_prism.Trace(Point(transform.position),30),PrismObstruction);
        }
        private float? PrismObstruction(DamagePoint from,DamagePoint to)
        {
            Vector3 origin=World(from)+Vector3.up,delta=World(to)-World(from);
            float distance=delta.magnitude;if(distance<=.0001f)return null;
            float? nearest=null;
            foreach(var hit in Physics.RaycastAll(origin,delta/distance,distance,~0,QueryTriggerInteraction.Ignore))
            {
                // Combatants may receive the beam but do not become terrain walls.
                var collider=hit.collider;
                if(collider.GetComponentInParent<PartyLife>()!=null||collider.GetComponentInParent<BossController>()!=null||collider.GetComponentInParent<BossAddMinion>()!=null)continue;
                if(!nearest.HasValue||hit.distance<nearest.Value)nearest=hit.distance;
            }
            return nearest;
        }
        public bool UsesLongInput=>Active&&(Job.index==6&&Route==3||Job.index==7&&Route==3||Job.index==8&&Route==1||Job.index==9&&Route==3||Job.index==11&&(Route==1||Route==2));
        public bool UsesGround
        {
            get{if(!Active)return false;Bind();return Job.index==7&&(Route==1||Route==3)||Job.index==10&&Route==3&&!_gravity.Active||Job.index==11&&Route==3;}
        }
        public bool CanPlace(Vector3 point)
        {
            if(!Active||!CharacterDefinitionValidation.Finite(point.x)||!CharacterDefinitionValidation.Finite(point.z))return false;
            var player=GetComponent<PlayerController>();if(player==null)return false;
            Vector2 p=new Vector2(point.x,point.z),self=new Vector2(transform.position.x,transform.position.z);
            float range=Job.index==7&&Route==1?10:25;
            float arena=RaidApproachField.Instance?.Showing==true?RaidApproachField.Instance.FieldRadius:player.arenaBoundaryRadius;
            if((p-self).sqrMagnitude>range*range||p.magnitude>arena)return false;
            var boss=StageManager.Instance?.boss;
            if(boss?.JustitiaEncounter==true&&(JustitiaNormalRules.Clamp(point,boss.JustitiaHalfWidth,boss.JustitiaHalfDepth,0)-point).sqrMagnitude>.001f)return false;
            if(boss?.JanusEncounter==true&&p.magnitude>boss.janus.arenaRadius)return false;
            foreach(var hit in Physics.RaycastAll(new Vector3(point.x,transform.position.y+3,point.z),Vector3.down,6,~0,QueryTriggerInteraction.Ignore))
                if(hit.normal.y>.8f&&hit.collider.GetComponentInParent<PartyLife>()==null&&hit.collider.GetComponentInParent<BossController>()==null&&hit.collider.GetComponentInParent<BossAddMinion>()==null)return true;
            return false;
        }
        public void RequestIdentity(bool longInput=false)
        {
            if(!Active||!RaidCombatAuthority.Local(_actor)||RaidCombatAuthority.InputBlocked(_actor)||longInput&&!UsesLongInput)return;
            var targeting=TargetingManager.Instance;var support=targeting!=null&&targeting.HasSupportSelection?targeting.SupportTarget:transform;
            var enemy=targeting?.GetLivingOffensiveTarget(StageManager.Instance?.boss);Vector3? point=null;
            string rule=longInput?"damage.long":"support",payload=RaidCombatAuthority.Id(support).ToString(CultureInfo.InvariantCulture);
            if(UsesGround&&!longInput)
            {
                var camera=Camera.main;if(camera==null||LocalGameplayInput.PointerOverUI(LocalGameplayInput.MousePosition))return;
                var ray=camera.ScreenPointToRay(LocalGameplayInput.MousePosition);var plane=new Plane(Vector3.up,Vector3.zero);
                if(!plane.Raycast(ray,out float distance)||!CanPlace(ray.GetPoint(distance)))
                {GetComponent<PlayerController>()?.combatController?.ReportNetworkFailure("범위 안의 유효한 지상 위치를 가리키세요");return;}
                point=ray.GetPoint(distance);rule="damage.ground";
                payload=point.Value.x.ToString("R",CultureInfo.InvariantCulture)+"|"+point.Value.z.ToString("R",CultureInfo.InvariantCulture);
            }
            if(RaidCombatAuthority.Active)RaidNetworkSession.Instance.RequestCharacterAction(10,_actor.Definition.id,(int)CharacterSlot.Identity,RaidCombatAuthority.Id(enemy),rule,payload);
            else if(!_actor.Execute(CharacterSlot.Identity,enemy,out string failure,support,point,longInput))GetComponent<PlayerController>()?.combatController?.ReportNetworkFailure(failure);
        }
        public void UpdateInput()
        {
            if((!UsesLongInput&&!UsesPrismInput)||GetComponent<PlayerController>()==null||!RaidCombatAuthority.Local(_actor)){ResetInput();return;}
            bool blocked=RaidCombatAuthority.InputBlocked(_actor);
            foreach(var slot in new[]{CharacterSlot.Job1,CharacterSlot.Counter,CharacterSlot.Movement})
                if(LocalInputBindings.Pressed(CharacterSlots.Binding(_actor.Definition,slot)))blocked=true;
            string binding=CharacterSlots.Binding(_actor.Definition,CharacterSlot.Identity);
            if(UsesPrismInput){_identityInput.Step(false,false,0,true);UpdatePrismInput(blocked,binding);return;}
            int gesture=_identityInput.Step(LocalInputBindings.Pressed(binding),LocalInputBindings.Held(binding),Time.unscaledTime,blocked);
            if(gesture!=0)RequestIdentity(gesture==2);
        }
    }
}
