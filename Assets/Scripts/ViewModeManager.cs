using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace RaidRogue
{
    /// <summary>
    /// Step 7: 클라이언트 독립형 2D / 3D 뷰 실시간 전환 매니저 (ViewModeManager)
    /// - 화면 우측 상단 [뷰 전환: 현재 3D (단축키 V)] 버튼 클릭 또는 키보드 [V] 단축키 입력으로
    ///   게임을 멈추지 않고 언제든 '3D 쿼터뷰(Perspective, X축 50도)' <-> '2D 탑다운 뷰(Orthographic, X축 90도)'를 실시간 전환합니다.
    /// - 향후 멀티플레이 연동 시 각 플레이어 클라이언트가 독립적으로 2D/3D를 선택할 수 있도록
    ///   게임의 물리/판정 좌표(XZ 평면)는 전혀 건드리지 않고 로컬 카메라와 [Visual_3D]/[Visual_2D] 렌더러만 전환합니다.
    /// </summary>
    public class ViewModeManager : MonoBehaviour
    {
        public static ViewModeManager Instance { get; private set; }
        public static bool Is2DViewActive => Instance != null && Instance.Is2DMode;

        [Header("Current View Mode State")]
        public CameraViewMode currentViewMode = CameraViewMode.View3D_Quarter;

        [Header("Scene References")]
        public QuarterViewCamera quarterViewCamera;
        public Light directionalLight;
        public PlayerController playerController;
        public BossController bossController;

        [Header("Top-Right View Toggle UI")]
        public Button viewToggleButton;
        public Image viewToggleButtonBg;
        public Text viewToggleButtonText;
        public Text viewToggleSubHintText;

        public bool Is2DMode => currentViewMode == CameraViewMode.View2D_TopDown;

        private float _lastToggleFrame = -1f;

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            if (quarterViewCamera == null)
            {
                quarterViewCamera = FindFirstObjectByType<QuarterViewCamera>();
            }
            if (directionalLight == null)
            {
                Light[] lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
                foreach (Light l in lights)
                {
                    if (l != null && l.type == LightType.Directional)
                    {
                        directionalLight = l;
                        break;
                    }
                }
            }
            if (playerController == null)
            {
                playerController = FindFirstObjectByType<PlayerController>();
            }
            if (bossController == null)
            {
                bossController = FindFirstObjectByType<BossController>();
            }

            if (viewToggleButton != null)
            {
                viewToggleButton.onClick.RemoveListener(ToggleViewMode);
                viewToggleButton.onClick.AddListener(ToggleViewMode);
            }

            ApplyViewMode(currentViewMode, immediateCameraSnap: true, showFeedbackPopup: false);
        }

        private void Update()
        {
            if (WasToggleShortcutPressed())
            {
                ToggleViewMode();
                return;
            }

            // 카드 선택 모달 등 Time.timeScale == 0 상태에서도 우측 상단 버튼 클릭이 항상 즉각 동작하도록 보조 체크
            if (Time.timeScale <= 0.001f)
            {
                HandleUnscaledButtonClickFallback();
            }
        }

        /// <summary>
        /// 2D 탑다운 뷰 <-> 3D 쿼터뷰 실시간 토글
        /// </summary>
        public void ToggleViewMode()
        {
            if (Time.frameCount == _lastToggleFrame)
            {
                return;
            }
            _lastToggleFrame = Time.frameCount;

            CameraViewMode nextMode = currentViewMode == CameraViewMode.View3D_Quarter
                ? CameraViewMode.View2D_TopDown
                : CameraViewMode.View3D_Quarter;

            ApplyViewMode(nextMode, immediateCameraSnap: false, showFeedbackPopup: true);
        }

        /// <summary>
        /// 지정한 뷰 모드(2D / 3D)를 로컬 카메라, 조명 그림자, 모든 DualVisualController 및 UI에 즉시 반영합니다.
        /// </summary>
        public void ApplyViewMode(CameraViewMode mode, bool immediateCameraSnap = false, bool showFeedbackPopup = true)
        {
            currentViewMode = mode;
            bool is2D = currentViewMode == CameraViewMode.View2D_TopDown;

            // 1. 로컬 카메라 투영 방식(Perspective <-> Orthographic) 및 목표 각도(50도 <-> 90도) 전환
            if (quarterViewCamera == null)
            {
                quarterViewCamera = FindFirstObjectByType<QuarterViewCamera>();
            }
            if (quarterViewCamera != null)
            {
                quarterViewCamera.SetViewMode(currentViewMode, immediateCameraSnap);
            }

            // 2. 3D 뷰에서는 입체적인 Soft 그림자 활성화, 2D 뷰에서는 깔끔한 평면 표현을 위해 그림자 비활성화
            if (directionalLight != null)
            {
                directionalLight.shadows = is2D ? LightShadows.None : LightShadows.Soft;
            }

            // 3. 플레이어, AI 파티원 봇 3명, 보스의 [Visual_3D] <-> [Visual_2D] 일괄 전환
            DualVisualController[] allVisuals = FindObjectsByType<DualVisualController>(FindObjectsSortMode.None);
            foreach (DualVisualController dualVis in allVisuals)
            {
                if (dualVis != null)
                {
                    dualVis.ApplyViewMode(is2D);
                }
            }

            // 4. 우측 상단 뷰 전환 버튼 UI 텍스트 및 색상 업데이트
            UpdateViewToggleUI(is2D);

            // 5. 전환 알림 피드백 표시 (게임 중단 없음)
            if (showFeedbackPopup && Application.isPlaying)
            {
                if (playerController != null)
                {
                    string popupMsg = is2D
                        ? "★ 2D 탑다운 뷰 (Orthographic 90°) 전환! ★"
                        : "★ 3D 쿼터뷰 (Perspective 50°) 전환! ★";
                    Color popupCol = is2D
                        ? new Color(0.30f, 1.0f, 0.85f, 1f)
                        : new Color(1.0f, 0.88f, 0.28f, 1f);

                    FloatingTextPopup.Spawn(
                        playerController.transform.position + Vector3.up * 2.4f,
                        popupMsg,
                        popupCol,
                        true
                    );
                }
            }
        }

        private void UpdateViewToggleUI(bool is2D)
        {
            if (viewToggleButtonText != null)
            {
                viewToggleButtonText.text = is2D
                    ? "[뷰 전환: 현재 2D (단축키 V)]"
                    : "[뷰 전환: 현재 3D (단축키 V)]";
                viewToggleButtonText.color = is2D
                    ? new Color(0.35f, 1.0f, 0.88f, 1f)
                    : new Color(1.0f, 0.94f, 0.45f, 1f);
            }

            if (viewToggleSubHintText != null)
            {
                viewToggleSubHintText.text = is2D
                    ? "세피리아 스타일 2D 직교(Orthographic) 탑다운  |  XZ 물리/판정 100% 동일"
                    : "입체 3D 원근(Perspective) 50° 쿼터뷰  |  클릭 또는 [V] 키로 즉시 전환";
            }

            if (viewToggleButtonBg != null)
            {
                viewToggleButtonBg.color = is2D
                    ? new Color(0.08f, 0.28f, 0.34f, 0.95f)
                    : new Color(0.14f, 0.18f, 0.30f, 0.95f);
            }
        }

        private static bool WasToggleShortcutPressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.vKey.wasPressedThisFrame)
            {
                return true;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.V))
            {
                return true;
            }
#endif
            return false;
        }

        private void HandleUnscaledButtonClickFallback()
        {
            if (viewToggleButton == null)
            {
                return;
            }

            bool clicked = false;
            Vector2 mousePos = Vector2.zero;

#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                clicked = true;
                mousePos = Mouse.current.position.ReadValue();
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (!clicked && Input.GetMouseButtonDown(0))
            {
                clicked = true;
                mousePos = Input.mousePosition;
            }
#endif

            if (clicked)
            {
                RectTransform rt = viewToggleButton.GetComponent<RectTransform>();
                if (rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, mousePos, null))
                {
                    ToggleViewMode();
                }
            }
        }
    }
}
