using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace RaidRogue
{
    public enum CameraViewMode { View3D_Quarter = 0, View2D_TopDown = 1 }
    public enum AiSpectatorView { Party, Boss, Arena, Free }

    [Serializable]
    public sealed class LocalCameraPreferences
    {
        public float rotationSensitivity = 0.16f;
        public bool invertVertical;
        public float zoomSensitivity3D = 2f;
        public float zoomSensitivity2D = 2f;
        public float distance3D = 17f;
        public float yaw3D;
        public float pitch3D = 50f;
        public float zoom2D;
        public CameraViewMode viewMode;
    }

    // Camera input precedes movement: held movement uses this frame's view and yaw.
    [DefaultExecutionOrder(-200)]
    [RequireComponent(typeof(Camera))]
    public class QuarterViewCamera : MonoBehaviour
    {
        public static QuarterViewCamera LocalCamera { get; private set; }
        public Transform target;
        public CameraViewMode currentViewMode = CameraViewMode.View3D_Quarter;
        public float pitchAngle3D = 50f;
        public float pitchAngle = 50f;
        public float yawAngle;
        public float followSmoothSpeed = 12f;
        public Vector3 targetOffset = new Vector3(0, 0.8f, 0);
        public float distance = 17f;
        public float minDistance = 6f;
        public float maxDistance = 30f;
        [Header("Local camera preferences")]
        public string localProfileId = "local-player";
        public LocalCameraPreferences preferences = new LocalCameraPreferences();
        [Header("2D arena framing")]
        public float minimumOrthoSize = 5f;
        public float arenaPadding = 2f;

        private Camera _cam;
        private PlayerController _player;
        private bool _initialized;
        private float _targetDistance;
        private float _zoom2D;
        private float _orthoSize;
        private float _lastAspect;
        private float _combatFramedDistance;
        private Vector3 _focus;
        private readonly CameraDragState _drag = new CameraDragState();
        private Vector2 _cursorBeforeDrag;
        private bool _ownsCursor;
        private bool _restoreCursorPending;
        private Transform _spectatorTarget;
        private AiSpectatorView _aiSpectatorView;
        private Vector3 _aiFreeFocus;
        private float _aiBeforeArenaDistance = -1f;
        public AiSpectatorView DevelopmentView => _aiSpectatorView;
        public void SetDevelopmentView(AiSpectatorView mode,Transform member=null)
        {
            if(RaidNetworkSession.Instance?.AiSpectatorActive!=true)return;
            if(mode==AiSpectatorView.Arena&&_aiSpectatorView!=AiSpectatorView.Arena)
            {_aiBeforeArenaDistance=_targetDistance;_targetDistance=maxDistance;}
            else if(mode!=AiSpectatorView.Arena&&_aiSpectatorView==AiSpectatorView.Arena&&_aiBeforeArenaDistance>0)
            {_targetDistance=_aiBeforeArenaDistance;_aiBeforeArenaDistance=-1f;}
            if(mode==AiSpectatorView.Free)_aiFreeFocus=_focus;
            _aiSpectatorView=mode;_spectatorTarget=mode==AiSpectatorView.Party?member:null;
            ReleaseOrbit();SnapToTargetImmediate();
        }
        public Transform SpectatorTarget => _spectatorTarget;

        // Presentation only: never replace target, the controlled player or the combat selection.
        public void Spectate(Transform member)
        {
            // Development viewing owns its target, including while the local AI is alive.
            // The resurrection UI also calls this method when clearing death spectating.
            if (RaidNetworkSession.Instance?.AiSpectatorActive == true) return;
            if (_player == null || !_player.IsDead || !CombatRange.LivingPartyMember(member)) member = null;
            if (_spectatorTarget == member) return;
            _spectatorTarget = member;
            ReleaseOrbit();
            SnapToTargetImmediate();
        }

        public Camera UnityCamera => _cam != null ? _cam : (_cam = GetComponent<Camera>());
        private bool _arrivalCamera;
        private Vector3 _arrivalPosition, _arrivalLookAt, _arrivalCurrentPosition;
        public void SetArrivalCamera(Vector3 position, Vector3 lookAt)
        {
            if ((lookAt - position).sqrMagnitude < .01f) lookAt = position + Vector3.forward;
            if (_arrivalCamera && _arrivalPosition == position && _arrivalLookAt == lookAt) return;
            ReleaseOrbit();
            _arrivalCamera = true; _arrivalPosition = position; _arrivalLookAt = lookAt;
            _arrivalCurrentPosition = position + new Vector3(0, 2, -4);
            // A Timeline LateUpdate can switch after this camera's own LateUpdate.
            // Apply projection and pose now so a previous 2D/gameplay frame cannot flash.
            ApplyArrivalCameraPose();
        }
        private void ApplyArrivalCameraPose()
        {
            UnityCamera.orthographic = false;
            transform.position = _arrivalCurrentPosition;
            transform.rotation = Quaternion.LookRotation(_arrivalLookAt - _arrivalCurrentPosition);
        }
        public void ClearArrivalCamera()
        {
            if (!_arrivalCamera) return;
            _arrivalCamera = false; SnapToTargetImmediate();
        }
        public bool Is2DMode => currentViewMode == CameraViewMode.View2D_TopDown;
        public bool IsOrbitDragging => _drag.Active;
        public float MovementYaw => (float)CameraControlMath.MovementYaw(Is2DMode, yawAngle);
        private float ArenaRadius => RaidApproachField.Instance?.Walking == true ? RaidApproachField.Instance.FieldRadius : _player != null ? _player.arenaBoundaryRadius : 18f;
        public float DefaultOrthoSize => (float)CameraControlMath.ArenaOrthoSize(ArenaRadius, UnityCamera.aspect, arenaPadding);

        private void Awake()
        {
            LocalCamera = this;
            EnsureInitialized();
        }

        private void OnEnable()
        {
            LocalCamera = this;
            EnsureInitialized();
        }

        private void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;
            _cam = GetComponent<Camera>();
            _player = target != null ? target.GetComponent<PlayerController>() : FindFirstObjectByType<PlayerController>();
            if (target == null && _player != null) target = _player.transform;
            if (LocalSettingsRuntime.Instance != null) ApplyLocalSettings(LocalSettingsRuntime.Instance.Previewed);
            _targetDistance = distance = Mathf.Clamp(distance, minDistance, maxDistance);
            yawAngle = (float)CameraControlMath.WrapYaw(yawAngle);
            pitchAngle3D = (float)CameraControlMath.ClampPitch(pitchAngle3D);
            _zoom2D = Mathf.Clamp01(preferences.zoom2D);
            _orthoSize = DesiredOrthoSize();
            _lastAspect = UnityCamera.aspect;
        }

        private void Start()
        {
            if (GetComponent<LocalCameraSettingsUI>() == null) gameObject.AddComponent<LocalCameraSettingsUI>();
            SnapToTargetImmediate();
        }

        public Vector3 GetMovementDirection(Vector2 input)
        {
            CameraControlMath.MovementXZ(input.x, input.y, MovementYaw, out double x, out double z);
            return new Vector3((float)x, 0, (float)z);
        }

        public void SetViewMode(CameraViewMode mode, bool immediate = false)
        {
            EnsureInitialized();
            ReleaseOrbit();
            currentViewMode = mode;
            // Change projection and orientation together; 3D yaw never leaks into 2D.
            SnapToTargetImmediate();
            SaveLocalPreferences();
        }

        private void Update()
        {
            if (_arrivalCamera) { ReleaseOrbit(); return; }
            bool blocked = RaidNetworkSession.Instance?.AiSpectatorActive==true ? LocalGameplayInput.ModalCovered : LocalGameplayInput.Blocked;
            bool overUI = !_drag.Active && LocalGameplayInput.PointerOverUI(LocalGameplayInput.MousePosition);
            ReadPointer(out bool pressed, out bool held, out Vector2 delta, out float scroll, out bool escape);
            if (_drag.Active && ((escape && !LocalGameplayInput.EscapeConsumed) || Cursor.lockState != CursorLockMode.Locked))
            {
                if (escape) LocalGameplayInput.ConsumeEscape();
                ReleaseOrbit();
            }
            bool wasDragging = _drag.Active;
            _drag.Step(pressed, held, blocked, Is2DMode, overUI);
            if (_drag.Active && !wasDragging)
            {
                _cursorBeforeDrag = LocalGameplayInput.MousePosition;
                _ownsCursor = true;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                delta = Vector2.zero;
            }
            else if (!_drag.Active && wasDragging) ReleaseOrbit(requireRightRelease: held || blocked || Is2DMode);
            if (_drag.Active)
            {
                float sensitivity = Mathf.Clamp(preferences.rotationSensitivity, 0.03f, 0.6f);
                yawAngle = (float)CameraControlMath.WrapYaw(yawAngle + delta.x * sensitivity);
                pitchAngle3D = (float)CameraControlMath.ClampPitch(pitchAngle3D + delta.y * sensitivity * (preferences.invertVertical ? 1 : -1));
            }
            if (!blocked && (!overUI || _drag.Active) && Mathf.Abs(scroll) > 0.0001f)
            {
                if (Is2DMode)
                {
                    float wide = DefaultOrthoSize;
                    float desired = (float)CameraControlMath.Zoom(DesiredOrthoSize(), scroll, Mathf.Clamp(preferences.zoomSensitivity2D, 0.25f, 5f), minimumOrthoSize, wide);
                    _zoom2D = Mathf.InverseLerp(wide, minimumOrthoSize, desired);
                }
                else _targetDistance = (float)CameraControlMath.Zoom(_targetDistance, scroll, Mathf.Clamp(preferences.zoomSensitivity3D, 0.25f, 5f), minDistance, maxDistance);
                if(RaidNetworkSession.Instance?.AiSpectatorActive!=true)SaveLocalPreferences();
            }
            transform.rotation = DesiredRotation();
            if(RaidNetworkSession.Instance?.AiSpectatorActive==true && _aiSpectatorView==AiSpectatorView.Free && !blocked)
                _aiFreeFocus+=GetMovementDirection(LocalInputBindings.Movement)*12f*Time.unscaledDeltaTime;
        }

        private void LateUpdate()
        {
            if (_arrivalCamera)
            {
                _arrivalCurrentPosition = Vector3.Lerp(_arrivalCurrentPosition, _arrivalPosition, 1f - Mathf.Exp(-1.8f * Time.unscaledDeltaTime));
                ApplyArrivalCameraPose();
                return;
            }
            if (_spectatorTarget != null && RaidNetworkSession.Instance?.AiSpectatorActive!=true &&
                (_player == null || !_player.IsDead || !CombatRange.LivingPartyMember(_spectatorTarget) || StageManager.CombatSuspended))
                Spectate(null);
            // A menu can open after this camera's Update, including on a UI click.
            if (RaidNetworkSession.Instance?.AiSpectatorActive==true ? LocalGameplayInput.ModalCovered : LocalGameplayInput.Blocked) ReleaseOrbit();
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            float blend = 1f - Mathf.Exp(-followSmoothSpeed * dt);
            distance = Mathf.Lerp(distance, _targetDistance, blend);
            // Refit immediately on a window/aspect change so the default arena view never crops.
            if (!Mathf.Approximately(_lastAspect, UnityCamera.aspect))
            {
                _lastAspect = UnityCamera.aspect;
                _orthoSize = DesiredOrthoSize();
            }
            else _orthoSize = Mathf.Lerp(_orthoSize, DesiredOrthoSize(), blend);
            if (Mathf.Abs(_orthoSize - DesiredOrthoSize()) < 0.001f) _orthoSize = DesiredOrthoSize();
            _focus = Vector3.Lerp(_focus, DesiredFocus(), blend);
            if (Is2DMode) _focus = ClampTopDownFocus(_focus);
            ApplyCameraPose();
        }

        private float DesiredOrthoSize() => Mathf.Lerp(DefaultOrthoSize, minimumOrthoSize, _zoom2D);
        private Quaternion DesiredRotation()
        {
            pitchAngle = Is2DMode ? 90 : pitchAngle3D;
            return Quaternion.Euler(pitchAngle, Is2DMode ? 0 : yawAngle, 0);
        }
        private Vector3 DesiredFocus()
        {
            if(RaidNetworkSession.Instance?.AiSpectatorActive==true)
            {
                if(_aiSpectatorView==AiSpectatorView.Free)return _aiFreeFocus;
                if(_aiSpectatorView==AiSpectatorView.Arena)return Vector3.zero;
                if(_aiSpectatorView==AiSpectatorView.Boss)return StageManager.Instance?.boss?.transform.position??Vector3.zero;
                if(_spectatorTarget!=null)return _spectatorTarget.position+targetOffset;
                return Vector3.zero;
            }
            var focusTarget = _spectatorTarget != null ? _spectatorTarget : target;
            Vector3 playerPosition = focusTarget != null ? focusTarget.position : Vector3.zero;
            if (RaidApproachField.Instance?.Walking == true) return new Vector3(playerPosition.x, 0, playerPosition.z);
            if (!Is2DMode)
            {
                var focus = playerPosition + targetOffset;
                if (TryCombatBoss(out var boss))
                {
                    var bossFocus = boss.transform.position + Vector3.up * 1.5f;
                    float gap = Vector2.Distance(new Vector2(focus.x, focus.z), new Vector2(bossFocus.x, bossFocus.z));
                    focus = Vector3.Lerp(focus, bossFocus, .5f * Mathf.InverseLerp(5, 12, gap));
                }
                return focus;
            }
            float visibleZoom = Mathf.InverseLerp(DefaultOrthoSize, minimumOrthoSize, _orthoSize);
            float follow = (float)CameraControlMath.FollowBlend(visibleZoom);
            return ClampTopDownFocus(new Vector3(playerPosition.x * follow, 0, playerPosition.z * follow));
        }
        private Vector3 ClampTopDownFocus(Vector3 point) => RaidApproachField.Instance?.Walking == true ? point : new Vector3(
            (float)CameraControlMath.ClampFocusAxis(point.x, ArenaRadius + arenaPadding, _orthoSize * UnityCamera.aspect), 0,
            (float)CameraControlMath.ClampFocusAxis(point.z, ArenaRadius + arenaPadding, _orthoSize));
        private void ApplyCameraPose()
        {
            UnityCamera.orthographic = Is2DMode;
            if (Is2DMode) UnityCamera.orthographicSize = _orthoSize;
            transform.rotation = DesiredRotation();
            float framedDistance = distance;
            if (!Is2DMode && TryCombatBoss(out var boss))
            {
                var subject = _spectatorTarget != null ? _spectatorTarget : target;
                if (subject != null) framedDistance = FitSubject(framedDistance, subject.position + targetOffset, .9f);
                framedDistance = FitSubject(framedDistance, boss.transform.position + Vector3.up * 1.5f, boss.CombatRadius);
                if (boss.JanusEncounter && boss.JanusOmegaTransform != null && boss.JanusOmegaTransform.gameObject.activeInHierarchy)
                    framedDistance = FitSubject(framedDistance, boss.JanusOmegaTransform.position + Vector3.up * 1.5f, boss.CombatRadius);
                // Widen immediately to retain moving/teleporting subjects; ease back in.
                _combatFramedDistance = Mathf.Max(framedDistance, Mathf.Lerp(_combatFramedDistance, framedDistance,
                    1f - Mathf.Exp(-followSmoothSpeed * Mathf.Min(Time.unscaledDeltaTime, .05f))));
                framedDistance = _combatFramedDistance;
            }
            else _combatFramedDistance = distance;
            transform.position = _focus - transform.forward * (Is2DMode ? 45f : framedDistance);
        }
        private bool TryCombatBoss(out BossController boss)
        {
            var stage = StageManager.Instance;
            boss = stage != null ? stage.boss : null;
            return RaidNetworkSession.Instance?.AiSpectatorActive != true && stage != null && stage.EncounterActive &&
                boss != null && boss.currentHp > 0 && boss.gameObject.activeInHierarchy && RaidApproachField.Instance?.BossVisible == true;
        }
        private float FitSubject(float current, Vector3 position, float radius)
        {
            var point = Quaternion.Inverse(transform.rotation) * (position - _focus);
            return Mathf.Max(current, (float)CameraControlMath.PerspectiveSubjectDistance(point.x, point.y, point.z,
                radius, UnityCamera.fieldOfView, UnityCamera.aspect, UnityCamera.nearClipPlane));
        }
        public void SnapToTargetImmediate()
        {
            EnsureInitialized();
            distance = _targetDistance;
            _orthoSize = DesiredOrthoSize();
            _focus = DesiredFocus();
            ApplyCameraPose();
        }
        public void BeginCombat(bool newRun)
        {
            EnsureInitialized();
            if (newRun) _zoom2D = 0;
            if (Is2DMode) SnapToTargetImmediate();
        }
        public void ResetCameraDefaults()
        {
            ReleaseOrbit();
            preferences = new LocalCameraPreferences();
            yawAngle = 0; pitchAngle3D = 50;
            _targetDistance = Mathf.Clamp(17, minDistance, maxDistance);
            _zoom2D = 0;
            SnapToTargetImmediate();
            SaveLocalPreferences();
        }
        public void ApplyLocalSettings(LocalSettingsData data)
        {
            if (data == null) return;
            preferences.rotationSensitivity = data.rotationSensitivity;
            preferences.invertVertical = data.invertVertical;
            preferences.zoomSensitivity3D = data.zoom3D;
            preferences.zoomSensitivity2D = data.zoom2D;
        }
        public LocalCameraPreferences CaptureLocalState()
        {
            SaveLocalPreferences();
            return JsonUtility.FromJson<LocalCameraPreferences>(JsonUtility.ToJson(preferences));
        }
        public void RestoreLocalState(LocalCameraPreferences state)
        {
            ReleaseOrbit(); preferences = state;
            yawAngle = state.yaw3D; pitchAngle3D = state.pitch3D;
            _targetDistance = state.distance3D; _zoom2D = state.zoom2D;
            currentViewMode = state.viewMode; SnapToTargetImmediate();
        }
        // Pose stays in memory; only preference values are persisted by LocalSettingsRuntime.
        public void SaveLocalPreferences()
        {
            if (!Application.isPlaying || !_initialized || RaidNetworkSession.Instance?.AiSpectatorActive==true) return;
            preferences.distance3D = _targetDistance;
            preferences.pitch3D = pitchAngle3D;
            preferences.yaw3D = yawAngle;
            preferences.zoom2D = _zoom2D;
            preferences.viewMode = currentViewMode;
        }
        public void ReleaseOrbit(bool restorePosition = true, bool requireRightRelease = true)
        {
            if (!_ownsCursor) return;
            _drag.Cancel(requireRightRelease);
            _ownsCursor = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _restoreCursorPending = true;
            if (restorePosition) RestoreCursor();
            LocalGameplayInput.SuppressPointerThisFrame();
            SaveLocalPreferences();
        }
        private void RestoreCursor()
        {
            if (!_restoreCursorPending || !Application.isFocused) return;
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null) Mouse.current.WarpCursorPosition(new Vector2(
                Mathf.Clamp(_cursorBeforeDrag.x, 0, Mathf.Max(0, Screen.width - 1)),
                Mathf.Clamp(_cursorBeforeDrag.y, 0, Mathf.Max(0, Screen.height - 1))));
#endif
            _restoreCursorPending = false;
        }
        private void OnApplicationFocus(bool focused)
        {
            if (!focused) { ReleaseOrbit(false); LocalGameplayInput.SuppressThisFrame(); }
            else RestoreCursor();
        }
        private void OnApplicationPause(bool paused) { if (paused) ReleaseOrbit(false); }
        private void OnDisable() { ReleaseOrbit(); SaveLocalPreferences(); }
        private void OnDestroy() { ClearArrivalCamera(); ReleaseOrbit(); if (LocalCamera == this) LocalCamera = null; }
        private void OnApplicationQuit() { ReleaseOrbit(); SaveLocalPreferences(); }

        private static void ReadPointer(out bool pressed, out bool held, out Vector2 delta, out float scroll, out bool escape)
        {
            pressed = held = escape = false; delta = Vector2.zero; scroll = 0;
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                var orbitButton=LocalMovementPreferences.RightClick?Mouse.current.middleButton:Mouse.current.rightButton;
                pressed = orbitButton.wasPressedThisFrame;
                held = orbitButton.isPressed;
                delta = Mouse.current.delta.ReadValue();
                float raw = Mouse.current.scroll.ReadValue().y;
                scroll = Mathf.Abs(raw) > 10f ? raw / 120f : raw;
            }
            escape = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            int orbitButton=LocalMovementPreferences.RightClick?2:1;
            pressed = Input.GetMouseButtonDown(orbitButton); held = Input.GetMouseButton(orbitButton);
            delta = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 10;
            scroll = Input.GetAxis("Mouse ScrollWheel") * 10;
            escape = Input.GetKeyDown(KeyCode.Escape);
#endif
        }
    }
}
