using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace RaidRogue
{
    public enum CameraViewMode { View3D_Quarter = 0, View2D_TopDown = 1 }

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
        private Vector3 _focus;
        private readonly CameraDragState _drag = new CameraDragState();
        private Vector2 _cursorBeforeDrag;
        private bool _ownsCursor;
        private bool _restoreCursorPending;
        private Transform _spectatorTarget;
        public Transform SpectatorTarget => _spectatorTarget;

        // Presentation only: never replace target, the controlled player or the combat selection.
        public void Spectate(Transform member)
        {
            if (_player == null || !_player.IsDead || !CombatRange.LivingPartyMember(member)) member = null;
            if (_spectatorTarget == member) return;
            _spectatorTarget = member;
            ReleaseOrbit();
            SnapToTargetImmediate();
        }

        public Camera UnityCamera => _cam != null ? _cam : (_cam = GetComponent<Camera>());
        public bool Is2DMode => currentViewMode == CameraViewMode.View2D_TopDown;
        public bool IsOrbitDragging => _drag.Active;
        public float MovementYaw => (float)CameraControlMath.MovementYaw(Is2DMode, yawAngle);
        private float ArenaRadius => _player != null ? _player.arenaBoundaryRadius : 18f;
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
            bool blocked = LocalGameplayInput.Blocked;
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
                SaveLocalPreferences();
            }
            transform.rotation = DesiredRotation();
        }

        private void LateUpdate()
        {
            if (_spectatorTarget != null && (_player == null || !_player.IsDead || !CombatRange.LivingPartyMember(_spectatorTarget) || StageManager.CombatSuspended))
                Spectate(null);
            // A menu can open after this camera's Update, including on a UI click.
            if (LocalGameplayInput.Blocked) ReleaseOrbit();
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
            var focusTarget = _spectatorTarget != null ? _spectatorTarget : target;
            Vector3 playerPosition = focusTarget != null ? focusTarget.position : Vector3.zero;
            if (!Is2DMode) return playerPosition + targetOffset;
            float visibleZoom = Mathf.InverseLerp(DefaultOrthoSize, minimumOrthoSize, _orthoSize);
            float follow = (float)CameraControlMath.FollowBlend(visibleZoom);
            return ClampTopDownFocus(new Vector3(playerPosition.x * follow, 0, playerPosition.z * follow));
        }
        private Vector3 ClampTopDownFocus(Vector3 point) => new Vector3(
            (float)CameraControlMath.ClampFocusAxis(point.x, ArenaRadius + arenaPadding, _orthoSize * UnityCamera.aspect), 0,
            (float)CameraControlMath.ClampFocusAxis(point.z, ArenaRadius + arenaPadding, _orthoSize));
        private void ApplyCameraPose()
        {
            UnityCamera.orthographic = Is2DMode;
            if (Is2DMode) UnityCamera.orthographicSize = _orthoSize;
            transform.rotation = DesiredRotation();
            transform.position = _focus - transform.forward * (Is2DMode ? 45f : distance);
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
            if (!Application.isPlaying || !_initialized) return;
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
        private void OnDestroy() { ReleaseOrbit(); if (LocalCamera == this) LocalCamera = null; }
        private void OnApplicationQuit() { ReleaseOrbit(); SaveLocalPreferences(); }

        private static void ReadPointer(out bool pressed, out bool held, out Vector2 delta, out float scroll, out bool escape)
        {
            pressed = held = escape = false; delta = Vector2.zero; scroll = 0;
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                pressed = Mouse.current.rightButton.wasPressedThisFrame;
                held = Mouse.current.rightButton.isPressed;
                delta = Mouse.current.delta.ReadValue();
                float raw = Mouse.current.scroll.ReadValue().y;
                scroll = Mathf.Abs(raw) > 10f ? raw / 120f : raw;
            }
            escape = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            pressed = Input.GetMouseButtonDown(1); held = Input.GetMouseButton(1);
            delta = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 10;
            scroll = Input.GetAxis("Mouse ScrollWheel") * 10;
            escape = Input.GetKeyDown(KeyCode.Escape);
#endif
        }
    }
}
