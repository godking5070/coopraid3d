using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace RaidRogue
{
    public static class LocalMovementPreferences
    {
        public static bool RightClick=>
            (LocalSettingsRuntime.Instance?.Previewed??LocalSettingsRuntime.Instance?.Applied)?.movementMode==1;
        public static string Hint(bool is2D)=>RightClick?
            "우클릭 이동 · 좌클릭 대상 선택 · "+(is2D?"휠 줌":"휠 버튼 드래그 회전 · 휠 줌"):
            LocalInputBindings.MovementLabels+(is2D?" 화면 기준 이동 · 휠 줌":" 카메라 기준 이동 · 우클릭 드래그 회전 · 휠 줌");
    }

    public partial class PlayerController
    {
        private readonly ClickMoveState _clickMove=new ClickMoveState();
        private readonly RaycastHit[] _clickMoveHits=new RaycastHit[24];
        private Vector3 _clickMoveDirection,_clickMarkerPoint;
        private float _nextClickPick;
        private bool _clickModeBefore;
        private LineRenderer _clickMarker;
        private Material _clickMarkerMaterial;
        private static readonly string[] ClickStopActions={"Skill1","Skill2","Skill3","Skill4","Skill5",
            "Counter","JobSkill2","Identity","SelectedActive","SelectedOffGcd","Ultimate","Interact"};

        private void UpdateClickMovement()
        {
            ReadClickButtons(out bool pressed,out bool held,out bool orbitHeld);
            bool enabled=LocalMovementPreferences.RightClick;
            if(enabled!=_clickModeBefore){_clickModeBefore=enabled;_clickMove.Suspend(held);}
            bool local=!RaidNetworkSession.Multiplayer||RaidCombatAuthority.Local(this);
            bool blocked=!enabled||!local||!CanMoveVoluntarily||LocalGameplayInput.Blocked||LocalInputBindings.Modified||orbitHeld||
                GetComponent<NetworkRaidAvatar>()?.MovementLocked==true||GetComponent<AegisExtensions>()?.Channeling==true||GetComponent<CharacterKit>()?.MovementBlocked==true;
            if(blocked){_clickMove.Suspend(held);_clickMoveDirection=Vector3.zero;UpdateClickMarker();return;}
            bool stop=ReadMoveInput().sqrMagnitude>.0001f;
            foreach(string action in ClickStopActions)if(LocalInputBindings.Pressed(action)){stop=true;break;}
            if(stop){_clickMove.Suspend(held);_clickMoveDirection=Vector3.zero;UpdateClickMarker();return;}

            Vector2 screen=LocalGameplayInput.MousePosition;
            bool overUI=(held||pressed||ClickSelectionPressed())&&LocalGameplayInput.PointerOverUI(screen);
            if(_clickMove.Pointer(pressed,held,overUI)&&(pressed||Time.unscaledTime>=_nextClickPick))
            {
                _nextClickPick=Time.unscaledTime+.05f;
                if(TryPickClickFloor(screen,out Vector3 point))
                {
                    _clickMove.SetDestination(point.x,point.z,transform.position.x,transform.position.z,Time.unscaledTimeAsDouble);
                    _clickMarkerPoint=point+Vector3.up*.06f;
                }
                else _clickMove.Cancel();
            }
            float step=RaidNetworkSession.Multiplayer?Mathf.Max(Time.deltaTime,RaidWire.Step):Time.deltaTime;
            float speed=moveSpeed*(_rescueSpeedBoostTimer>0?1.35f:1f);
            _clickMove.Direction(transform.position.x,transform.position.z,speed,step,Time.unscaledTimeAsDouble,out double x,out double z);
            _clickMoveDirection=new Vector3((float)x,0,(float)z);
            UpdateClickMarker();
        }
        private Vector3 ReadDesiredMovementXZ()
        {
            Vector2 keys=ReadMoveInput();
            if(keys.sqrMagnitude>.0001f||!LocalMovementPreferences.RightClick)return GetMoveDirectionXZ(keys);
            return _clickMoveDirection;
        }
        private bool TryPickClickFloor(Vector2 screen,out Vector3 point)
        {
            point=default;
            if(screen.x<0||screen.y<0||screen.x>Screen.width||screen.y>Screen.height)return false;
            var camera=QuarterViewCamera.LocalCamera!=null?QuarterViewCamera.LocalCamera.GetComponent<Camera>():Camera.main;
            if(camera==null)camera=Camera.main;
            if(camera==null)return false;
            Ray ray=camera.ScreenPointToRay(screen);
            int count=Physics.RaycastNonAlloc(ray,_clickMoveHits,250,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
            RaycastHit[] hits=count==_clickMoveHits.Length?Physics.RaycastAll(ray,250,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore):_clickMoveHits;
            if(hits!=_clickMoveHits)count=hits.Length;
            float nearest=float.MaxValue;RaycastHit best=default;bool found=false;
            for(int i=0;i<count;i++)
            {
                var hit=hits[i];var collider=hit.collider;
                if(collider==null||collider.GetComponentInParent<PlayerController>()!=null||collider.GetComponentInParent<PartyBotAI>()!=null||
                    collider.GetComponentInParent<NetworkRaidAvatar>()!=null||collider.GetComponentInParent<BossController>()!=null||
                    collider.GetComponentInParent<BossAddMinion>()!=null||hit.distance>=nearest)continue;
                nearest=hit.distance;best=hit;found=true;
            }
            if(!found||best.normal.y<.65f)return false;
            point=best.point;return true;
        }
        private static void ReadClickButtons(out bool pressed,out bool held,out bool orbitHeld)
        {
            pressed=held=orbitHeld=false;
#if ENABLE_INPUT_SYSTEM
            if(Mouse.current!=null)
            {pressed=Mouse.current.rightButton.wasPressedThisFrame;held=Mouse.current.rightButton.isPressed;orbitHeld=Mouse.current.middleButton.isPressed;}
#elif ENABLE_LEGACY_INPUT_MANAGER
            pressed=Input.GetMouseButtonDown(1);held=Input.GetMouseButton(1);orbitHeld=Input.GetMouseButton(2);
#endif
        }
        private static bool ClickSelectionPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current?.leftButton.wasPressedThisFrame==true;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButtonDown(0);
#else
            return false;
#endif
        }
        private void CancelClickMovement()
        {ReadClickButtons(out _,out bool held,out _);_clickMove.Suspend(held);_clickMoveDirection=Vector3.zero;if(_clickMarker!=null)_clickMarker.enabled=false;}
        private void UpdateClickMarker()
        {
            if(!_clickMove.Active){if(_clickMarker!=null)_clickMarker.enabled=false;return;}
            if(_clickMarker==null)
            {
                var shader=Shader.Find("Sprites/Default")??Shader.Find("Universal Render Pipeline/Unlit");
                if(shader==null)return;
                var marker=new GameObject("Local click movement destination");marker.transform.SetParent(transform,false);
                _clickMarker=marker.AddComponent<LineRenderer>();_clickMarker.useWorldSpace=true;_clickMarker.loop=true;
                _clickMarker.positionCount=24;_clickMarker.startWidth=_clickMarker.endWidth=.045f;
                _clickMarker.startColor=_clickMarker.endColor=new Color(.3f,.85f,1,.85f);
                _clickMarkerMaterial=new Material(shader);_clickMarker.sharedMaterial=_clickMarkerMaterial;
                _clickMarker.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;_clickMarker.receiveShadows=false;
            }
            for(int i=0;i<24;i++)
            {float angle=i*Mathf.PI*2/24;_clickMarker.SetPosition(i,_clickMarkerPoint+new Vector3(Mathf.Cos(angle)*.25f,0,Mathf.Sin(angle)*.25f));}
            _clickMarker.enabled=true;
        }
        private void OnDestroy(){if(_clickMarkerMaterial!=null)Destroy(_clickMarkerMaterial);}
    }
}
