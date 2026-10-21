using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace RaidRogue
{
    /// <summary>
    /// 플레이어를 X축 50도 각도 위에서 내려다보는 3D 쿼터뷰 카메라.
    /// 마우스 휠로 줌인/줌아웃이 가능합니다.
    /// </summary>
    public class QuarterViewCamera : MonoBehaviour
    {
        [Header("Target")]
        public Transform target;

        [Header("Quarter-View Angle & Follow")]
        [Tooltip("X축 내려다보는 각도 (기본 50도)")]
        public float pitchAngle = 50f;

        [Tooltip("Y축 회전 각도 (기본 0도 = 북쪽(+Z)을 바라봄)")]
        public float yawAngle = 0f;

        [Tooltip("타겟 추적 부드러움 정도")]
        public float followSmoothSpeed = 12f;

        [Tooltip("타겟 기준 시선 높이 오프셋")]
        public Vector3 targetOffset = new Vector3(0f, 0.8f, 0f);

        [Header("Zoom Settings")]
        [Tooltip("현재 카메라 거리")]
        public float distance = 15f;

        [Tooltip("최소 줌 거리")]
        public float minDistance = 6f;

        [Tooltip("최대 줌 거리")]
        public float maxDistance = 28f;

        [Tooltip("마우스 휠 줌 속도")]
        public float zoomSpeed = 8f;

        private float _targetDistance;

        private void Awake()
        {
            _targetDistance = Mathf.Clamp(distance, minDistance, maxDistance);
            distance = _targetDistance;
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

        private void LateUpdate()
        {
            HandleZoomInput();

            if (target == null)
            {
                return;
            }

            distance = Mathf.Lerp(distance, _targetDistance, Time.deltaTime * 12f);

            Quaternion desiredRotation = Quaternion.Euler(pitchAngle, yawAngle, 0f);
            Vector3 focusPoint = target.position + targetOffset;
            Vector3 desiredPosition = focusPoint - (desiredRotation * Vector3.forward * distance);

            transform.rotation = desiredRotation;
            transform.position = Vector3.Lerp(transform.position, desiredPosition, Time.deltaTime * followSmoothSpeed);
        }

        public void SnapToTargetImmediate()
        {
            Quaternion desiredRotation = Quaternion.Euler(pitchAngle, yawAngle, 0f);
            transform.rotation = desiredRotation;

            if (target != null)
            {
                Vector3 focusPoint = target.position + targetOffset;
                transform.position = focusPoint - (desiredRotation * Vector3.forward * distance);
            }
        }

        private void HandleZoomInput()
        {
            float scrollDelta = 0f;

#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                // New Input System scroll.y is typically around +/-120 per notch on Windows
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
