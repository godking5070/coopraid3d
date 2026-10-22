using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace RaidRogue
{
    public enum CameraViewMode
    {
        View3D_Quarter = 0, // 3D 쿼터뷰 (Perspective 원근 투영, X축 50도)
        View2D_TopDown = 1  // 2D 세피리아 스타일 탑다운 뷰 (Orthographic 직교 투영, X축 90도 수직)
    }

    /// <summary>
    /// 플레이어를 추적하는 로컬 카메라 컨트롤러 (Step 1 ~ Step 7 통합):
    /// - 3D 뷰 모드 (기본): Perspective 원근 투영(orthographic = false), X축 50도 쿼터뷰 각도
    /// - 2D 뷰 모드 (탑다운): Orthographic 직교 투영(orthographic = true), 캐릭터 정중앙 위에서 수직(X축 90도)으로 내려다보며 부드럽게 이동
    /// - 마우스 휠로 3D 거리(distance) 및 2D 직교 사이즈(orthographicSize) 줌인/줌아웃 지원
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class QuarterViewCamera : MonoBehaviour
    {
        [Header("Target")]
        public Transform target;

        [Header("2D / 3D View Mode Settings (Step 7)")]
        public CameraViewMode currentViewMode = CameraViewMode.View3D_Quarter;

        [Tooltip("3D 쿼터뷰일 때 X축 내려다보는 각도 (기본 50도)")]
        public float pitchAngle3D = 50f;

        [Tooltip("2D 탑다운 뷰일 때 캐릭터 정중앙 위 수직 각도 (기본 90도)")]
        public float pitchAngle2D = 90f;

        [Tooltip("2D <-> 3D 뷰 전환 시 카메라 각도/위치 보간 속도")]
        public float viewTransitionSmoothSpeed = 10f;

        [Tooltip("2D Orthographic 모드일 때 거리(distance) 대비 orthographicSize 비율")]
        public float orthoSizePerDistance = 0.62f;

        [Header("Quarter-View Angle & Follow")]
        [Tooltip("현재 카메라 X축 각도")]
        public float pitchAngle = 50f;

        [Tooltip("Y축 회전 각도 (기본 0도 = 북쪽(+Z)을 바라봄)")]
        public float yawAngle = 0f;

        [Tooltip("타겟 추적 부드러움 정도")]
        public float followSmoothSpeed = 12f;

        [Tooltip("3D 뷰 기준 타겟 시선 높이 오프셋")]
        public Vector3 targetOffset = new Vector3(0f, 0.8f, 0f);

        [Tooltip("2D 탑다운 뷰 기준 타겟 정중앙 오프셋")]
        public Vector3 targetOffset2D = Vector3.zero;

        [Header("Zoom Settings")]
        [Tooltip("현재 카메라 거리")]
        public float distance = 17f;

        [Tooltip("최소 줌 거리")]
        public float minDistance = 6f;

        [Tooltip("최대 줌 거리")]
        public float maxDistance = 30f;

        [Tooltip("마우스 휠 줌 속도")]
        public float zoomSpeed = 8f;

        private Camera _cam;
        private float _targetDistance;
        private Vector3 _currentOffset;

        public Camera UnityCamera
        {
            get
            {
                if (_cam == null) _cam = GetComponent<Camera>();
                return _cam;
            }
        }

        public bool Is2DMode => currentViewMode == CameraViewMode.View2D_TopDown;

        private void Awake()
        {
            _cam = GetComponent<Camera>();
            _targetDistance = Mathf.Clamp(distance, minDistance, maxDistance);
            distance = _targetDistance;
            _currentOffset = Is2DMode ? targetOffset2D : targetOffset;
            ApplyProjectionSettings(immediate: true);
        }

        private void Start()
        {
            if (target == null)
            {
                PlayerController player = FindFirstObjectByType<PlayerController>();
                if (player != null)
                {
                    target = player.transform;
                }
            }

            SnapToTargetImmediate();
        }

        /// <summary>
        /// ViewModeManager에서 호출하여 2D 탑다운(Orthographic) <-> 3D 쿼터뷰(Perspective)를 실시간 전환합니다.
        /// </summary>
        public void SetViewMode(CameraViewMode mode, bool immediate = false)
        {
            currentViewMode = mode;
            ApplyProjectionSettings(immediate);

            if (immediate)
            {
                SnapToTargetImmediate();
            }
        }

        private void ApplyProjectionSettings(bool immediate)
        {
            Camera cam = UnityCamera;
            if (cam == null)
            {
                return;
            }

            bool is2D = currentViewMode == CameraViewMode.View2D_TopDown;
            cam.orthographic = is2D;
            cam.orthographicSize = Mathf.Clamp(distance * orthoSizePerDistance, 3.8f, 20f);

            if (immediate)
            {
                pitchAngle = is2D ? pitchAngle2D : pitchAngle3D;
                _currentOffset = is2D ? targetOffset2D : targetOffset;
            }
        }

        private void LateUpdate()
        {
            HandleZoomInput();

            if (target == null)
            {
                return;
            }

            float dt = Time.unscaledDeltaTime > 0.0001f ? Mathf.Min(Time.unscaledDeltaTime, 0.05f) : 0.016f;

            distance = Mathf.Lerp(distance, _targetDistance, dt * 12f);

            bool is2D = currentViewMode == CameraViewMode.View2D_TopDown;
            float desiredPitch = is2D ? pitchAngle2D : pitchAngle3D;
            Vector3 desiredOffset = is2D ? targetOffset2D : targetOffset;

            pitchAngle = Mathf.Lerp(pitchAngle, desiredPitch, dt * viewTransitionSmoothSpeed);
            _currentOffset = Vector3.Lerp(_currentOffset, desiredOffset, dt * viewTransitionSmoothSpeed);

            Camera cam = UnityCamera;
            if (cam != null)
            {
                if (cam.orthographic != is2D)
                {
                    cam.orthographic = is2D;
                }
                if (is2D)
                {
                    // 2D 직교 투영 사이즈 업데이트 (원근감 완전 제거)
                    cam.orthographicSize = Mathf.Clamp(distance * orthoSizePerDistance, 3.8f, 20f);
                }
            }

            Quaternion desiredRotation = Quaternion.Euler(pitchAngle, yawAngle, 0f);
            Vector3 focusPoint = target.position + _currentOffset;
            // 2D 모드에서도 충분한 카메라 높이를 확보해 근평면(Near Clip) 잘림 방지
            float camDist = is2D ? Mathf.Max(distance, 24f) : distance;
            Vector3 desiredPosition = focusPoint - (desiredRotation * Vector3.forward * camDist);

            transform.rotation = desiredRotation;
            transform.position = Vector3.Lerp(transform.position, desiredPosition, dt * followSmoothSpeed);
        }

        public void SnapToTargetImmediate()
        {
            bool is2D = currentViewMode == CameraViewMode.View2D_TopDown;
            pitchAngle = is2D ? pitchAngle2D : pitchAngle3D;
            _currentOffset = is2D ? targetOffset2D : targetOffset;

            Camera cam = UnityCamera;
            if (cam != null)
            {
                cam.orthographic = is2D;
                cam.orthographicSize = Mathf.Clamp(distance * orthoSizePerDistance, 3.8f, 20f);
            }

            Quaternion desiredRotation = Quaternion.Euler(pitchAngle, yawAngle, 0f);
            transform.rotation = desiredRotation;

            if (target != null)
            {
                Vector3 focusPoint = target.position + _currentOffset;
                float camDist = is2D ? Mathf.Max(distance, 24f) : distance;
                transform.position = focusPoint - (desiredRotation * Vector3.forward * camDist);
            }
        }

        private void HandleZoomInput()
        {
            float scrollDelta = 0f;

#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                float rawScroll = Mouse.current.scroll.ReadValue().y;
                if (Mathf.Abs(rawScroll) > 0.01f)
                {
                    scrollDelta = Mathf.Sign(rawScroll) * (Mathf.Abs(rawScroll) > 10f ? rawScroll / 120f : rawScroll);
                }
            }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            if (Mathf.Approximately(scrollDelta, 0f))
            {
                scrollDelta = Input.GetAxis("Mouse ScrollWheel") * 10f;
            }
#endif

            if (Mathf.Abs(scrollDelta) > 0.0001f)
            {
                _targetDistance = Mathf.Clamp(_targetDistance - scrollDelta * zoomSpeed * 0.25f, minDistance, maxDistance);
            }
        }
    }
}
