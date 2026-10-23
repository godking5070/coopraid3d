using System.Collections.Generic;
using UnityEngine;

namespace RaidRogue
{
    public enum VisualUnitKind
    {
        PlayerMelee = 0,
        BotTank = 1,
        BotHealer = 2,
        BotRangedDPS = 3,
        Boss = 4
    }

    /// <summary>
    /// Step 7: 클라이언트 독립형 2D / 3D 이중 외형 컨트롤러 (DualVisualController)
    /// - 각 유닛(플레이어, 탱커/힐러/원딜 봇, 보스)의 자식으로 [Visual_3D]와 [Visual_2D]를 자동 구성합니다.
    /// - [Visual_3D]: 기존 3D 캡슐/실린더 및 눈(바이저) 메쉬 렌더러들을 포함합니다.
    /// - [Visual_2D]: 외부 이미지 에셋 없이 코드로 생성한 고해상도 절차적 2D SpriteRenderer
    ///   (역할군 색상 + 외곽 테두리 + 바라보는 방향 표시 화살표 도형)를 바닥과 평행하게(X축 회전 90도) 배치합니다.
    /// - 물리/판정 좌표(XZ 평면)는 전혀 변경하지 않고 로컬 렌더러만 ON/OFF 전환합니다.
    /// </summary>
    [ExecuteAlways]
    public class DualVisualController : MonoBehaviour
    {
        public static readonly List<DualVisualController> ActiveInstances = new List<DualVisualController>();

        [Header("Unit Role & Visual Roots")]
        public VisualUnitKind unitKind = VisualUnitKind.PlayerMelee;
        public GameObject visual3DRoot;
        public GameObject visual2DRoot;

        [Header("3D Visual References")]
        public Renderer primary3DRenderer;
        public Renderer secondary3DRenderer;

        [Header("2D Sprite References (X-Rotation 90 deg on XZ Floor)")]
        public SpriteRenderer body2DSpriteRenderer;
        public SpriteRenderer directionArrow2DSpriteRenderer;
        public SpriteRenderer visorOrEmblem2DSpriteRenderer;

        [Header("Colors & Scale")]
        public Color primaryColor = new Color(1f, 0.85f, 0.12f, 1f);
        public Color accentColor = Color.white;
        public float base2DScale = 1.45f;
        public float currentScaleMultiplier = 1.0f;
        public float floorYOffset = 0.14f;

        // 런타임 절차적 스프라이트 캐시 (외부 이미지 파일 불필요)
        private static Sprite _cachedCircleTokenSprite;
        private static Sprite _cachedSquareTokenSprite;
        private static Sprite _cachedBossTokenSprite;
        private static Sprite _cachedForwardArrowSprite;
        private static Sprite _cachedBossVisorSprite;
        private static Sprite _cachedHealerCrossSprite;
        private static Sprite _cachedTankShieldInnerSprite;
        private static Sprite _cachedDpsInnerRingSprite;

        private void OnEnable()
        {
            if (!ActiveInstances.Contains(this))
            {
                ActiveInstances.Add(this);
            }

            EnsureDualVisualHierarchy();
            Refresh2DSpritesAndColors();

            if (Application.isPlaying && ViewModeManager.Instance != null)
            {
                ApplyViewMode(ViewModeManager.Instance.Is2DMode);
            }
        }

        private void OnDisable()
        {
            ActiveInstances.Remove(this);
        }

        private void Start()
        {
            EnsureDualVisualHierarchy();
            Refresh2DSpritesAndColors();

            if (Application.isPlaying && ViewModeManager.Instance != null)
            {
                ApplyViewMode(ViewModeManager.Instance.Is2DMode);
            }
        }

        private void LateUpdate()
        {
            if (visual2DRoot == null)
            {
                return;
            }

            // [Visual_2D]가 항상 바닥(XZ 평면, Y = floorYOffset)과 평행(X축 회전 90도)을 유지하도록 보장합니다.
            // (탱커 사망 시 루트가 기울어지거나, 캐릭터 루트 Y가 1.0f에 있어도 2D 스프라이트는 바닥에 정확히 밀착)
            Vector3 rootPos = transform.position;
            visual2DRoot.transform.position = new Vector3(rootPos.x, floorYOffset, rootPos.z);

            // 캐릭터가 바라보는 XZ 평면 방향(Y축 회전각)을 그대로 따라가되 X축은 90도로 바닥에 눕힘
            Vector3 forwardXZ = new Vector3(transform.forward.x, 0f, transform.forward.z);
            float yawDeg = forwardXZ.sqrMagnitude > 0.001f
                ? Mathf.Atan2(forwardXZ.x, forwardXZ.z) * Mathf.Rad2Deg
                : transform.eulerAngles.y;

            visual2DRoot.transform.rotation = Quaternion.Euler(90f, yawDeg, 0f);

            float finalScale = base2DScale * currentScaleMultiplier;
            visual2DRoot.transform.localScale = new Vector3(finalScale, finalScale, 1f);
        }

        /// <summary>
        /// 2D / 3D 뷰 모드에 따라 [Visual_3D]와 [Visual_2D]의 활성화 상태를 즉시 전환합니다.
        /// </summary>
        public void ApplyViewMode(bool is2DMode)
        {
            EnsureDualVisualHierarchy();

            if (visual3DRoot != null && visual3DRoot.activeSelf != !is2DMode)
            {
                visual3DRoot.SetActive(!is2DMode);
            }

            if (visual2DRoot != null && visual2DRoot.activeSelf != is2DMode)
            {
                visual2DRoot.SetActive(is2DMode);
            }
        }

        /// <summary>
        /// 캐릭터/보스의 색상 변경(생존기 ON, 그로기, 페이즈 전환, 스테이지 변경, 사망 등) 시
        /// 3D 메쉬와 2D 스프라이트 양쪽 모두에 색상을 동기화합니다.
        /// </summary>
        public void SetPrimaryColor(Color newColor)
        {
            primaryColor = newColor;

            if (primary3DRenderer != null)
            {
                Material mat = Application.isPlaying ? primary3DRenderer.material : primary3DRenderer.sharedMaterial;
                if (mat != null)
                {
                    mat.color = newColor;
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", newColor);
                    if (mat.HasProperty("_Color")) mat.SetColor("_Color", newColor);
                }
            }

            if (body2DSpriteRenderer != null)
            {
                body2DSpriteRenderer.color = newColor;
            }
        }

        /// <summary>
        /// 보스 등 스테이지별 크기 배율 변경 시 2D 외형 크기도 함께 갱신합니다.
        /// </summary>
        public void SetScaleMultiplier(float multiplier)
        {
            currentScaleMultiplier = Mathf.Max(0.2f, multiplier);
            if (visual2DRoot != null)
            {
                float finalScale = base2DScale * currentScaleMultiplier;
                visual2DRoot.transform.localScale = new Vector3(finalScale, finalScale, 1f);
            }
        }

        public void SetSecondaryColor(Color newAccentColor)
        {
            accentColor = newAccentColor;
            if (visorOrEmblem2DSpriteRenderer != null && unitKind == VisualUnitKind.Boss)
            {
                visorOrEmblem2DSpriteRenderer.color = newAccentColor;
            }
        }

        /// <summary>
        /// [Visual_3D]와 [Visual_2D] 자식 계층 구조를 자동으로 생성·정렬합니다.
        /// </summary>
        public void EnsureDualVisualHierarchy()
        {
            // 1. [Visual_3D] 루트 확인 및 생성
            if (visual3DRoot == null)
            {
                Transform existing3D = transform.Find("[Visual_3D]");
                if (existing3D == null)
                {
                    existing3D = transform.Find("Visual_3D");
                }

                if (existing3D != null)
                {
                    visual3DRoot = existing3D.gameObject;
                }
                else
                {
                    visual3DRoot = new GameObject("[Visual_3D]");
                    visual3DRoot.transform.SetParent(transform, false);
                    visual3DRoot.transform.localPosition = Vector3.zero;
                    visual3DRoot.transform.localRotation = Quaternion.identity;
                    visual3DRoot.transform.localScale = Vector3.one;
                }
            }

            // 만약 루트 오브젝트 자체에 캡슐 등 3D MeshFilter/MeshRenderer가 붙어있다면 [Visual_3D] 안으로 이동
            MigrateRootMeshIntoVisual3DIfNeeded();

            // 2. [Visual_2D] 루트 확인 및 생성 (바닥과 평행하도록 X축 90도 회전)
            if (visual2DRoot == null)
            {
                Transform existing2D = transform.Find("[Visual_2D]");
                if (existing2D == null)
                {
                    existing2D = transform.Find("Visual_2D");
                }

                if (existing2D != null)
                {
                    visual2DRoot = existing2D.gameObject;
                }
                else
                {
                    visual2DRoot = new GameObject("[Visual_2D]");
                    visual2DRoot.transform.SetParent(transform, false);
                    float localY = unitKind == VisualUnitKind.Boss ? floorYOffset : (floorYOffset - 1.0f);
                    visual2DRoot.transform.localPosition = new Vector3(0f, localY, 0f);
                    visual2DRoot.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    float initScale = base2DScale * currentScaleMultiplier;
                    visual2DRoot.transform.localScale = new Vector3(initScale, initScale, 1f);
                }
            }

            // 3. [Visual_2D] 내부 SpriteRenderer 자식들 생성/연결
            Ensure2DSpriteChildren();
        }

        private void MigrateRootMeshIntoVisual3DIfNeeded()
        {
            MeshRenderer rootMr = GetComponent<MeshRenderer>();
            MeshFilter rootMf = GetComponent<MeshFilter>();

            if (rootMr != null && rootMf != null && visual3DRoot != null)
            {
                GameObject bodyChild = new GameObject($"{gameObject.name}_Body3D");
                bodyChild.transform.SetParent(visual3DRoot.transform, false);
                bodyChild.transform.localPosition = Vector3.zero;
                bodyChild.transform.localRotation = Quaternion.identity;
                bodyChild.transform.localScale = Vector3.one;

                MeshFilter newMf = bodyChild.AddComponent<MeshFilter>();
                newMf.sharedMesh = rootMf.sharedMesh;

                MeshRenderer newMr = bodyChild.AddComponent<MeshRenderer>();
                newMr.sharedMaterial = rootMr.sharedMaterial;
                newMr.shadowCastingMode = rootMr.shadowCastingMode;
                newMr.receiveShadows = rootMr.receiveShadows;

                primary3DRenderer = newMr;

                PlayerController pc = GetComponent<PlayerController>();
                if (pc != null && pc.bodyRenderer == rootMr)
                {
                    pc.bodyRenderer = newMr;
                }

                PartyBotAI bot = GetComponent<PartyBotAI>();
                if (bot != null && bot.bodyRenderer == rootMr)
                {
                    bot.bodyRenderer = newMr;
                }

                if (Application.isPlaying)
                {
                    Destroy(rootMr);
                    Destroy(rootMf);
                }
                else
                {
                    DestroyImmediate(rootMr);
                    DestroyImmediate(rootMf);
                }
            }

            // 기존 눈(FrontEye)이나 보스 실린더/바이저가 루트 직속 자식으로 있으면 [Visual_3D] 안으로 이동
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child == visual3DRoot.transform || (visual2DRoot != null && child == visual2DRoot.transform))
                {
                    continue;
                }

                string n = child.name;
                if (n.Contains("FrontEye") || n.Contains("Boss_Body") || n.Contains("Boss_FrontVisor"))
                {
                    child.SetParent(visual3DRoot.transform, false);
                }
            }

            if (primary3DRenderer == null && visual3DRoot != null)
            {
                primary3DRenderer = visual3DRoot.GetComponentInChildren<Renderer>();
            }
        }

        private void Ensure2DSpriteChildren()
        {
            if (visual2DRoot == null)
            {
                return;
            }

            // 1) 본체 토큰 SpriteRenderer (원형 / 사각형 / 보스 대형 토큰 + 테두리)
            if (body2DSpriteRenderer == null)
            {
                Transform bodyTr = visual2DRoot.transform.Find("Sprite2D_Body");
                if (bodyTr == null)
                {
                    GameObject bodyObj = new GameObject("Sprite2D_Body");
                    bodyObj.transform.SetParent(visual2DRoot.transform, false);
                    bodyObj.transform.localPosition = Vector3.zero;
                    bodyObj.transform.localRotation = Quaternion.identity;
                    bodyObj.transform.localScale = Vector3.one;
                    body2DSpriteRenderer = bodyObj.AddComponent<SpriteRenderer>();
                }
                else
                {
                    body2DSpriteRenderer = bodyTr.GetComponent<SpriteRenderer>();
                }
            }

            // 2) 역할군 내부 엠블럼 / 보스 바이저 SpriteRenderer
            if (visorOrEmblem2DSpriteRenderer == null)
            {
                Transform emblemTr = visual2DRoot.transform.Find("Sprite2D_RoleDetail");
                if (emblemTr == null)
                {
                    GameObject emblemObj = new GameObject("Sprite2D_RoleDetail");
                    emblemObj.transform.SetParent(visual2DRoot.transform, false);
                    emblemObj.transform.localPosition = new Vector3(0f, 0f, -0.01f);
                    emblemObj.transform.localRotation = Quaternion.identity;
                    emblemObj.transform.localScale = Vector3.one;
                    visorOrEmblem2DSpriteRenderer = emblemObj.AddComponent<SpriteRenderer>();
                }
                else
                {
                    visorOrEmblem2DSpriteRenderer = emblemTr.GetComponent<SpriteRenderer>();
                }
            }

            // 3) 바라보는 정면 방향(+Z = 2D 스프라이트 로컬 +Y) 표시 화살표 도형 SpriteRenderer
            if (directionArrow2DSpriteRenderer == null)
            {
                Transform arrowTr = visual2DRoot.transform.Find("Sprite2D_DirectionArrow");
                if (arrowTr == null)
                {
                    GameObject arrowObj = new GameObject("Sprite2D_DirectionArrow");
                    arrowObj.transform.SetParent(visual2DRoot.transform, false);
                    arrowObj.transform.localPosition = new Vector3(0f, 0f, -0.02f);
                    arrowObj.transform.localRotation = Quaternion.identity;
                    arrowObj.transform.localScale = Vector3.one;
                    directionArrow2DSpriteRenderer = arrowObj.AddComponent<SpriteRenderer>();
                }
                else
                {
                    directionArrow2DSpriteRenderer = arrowTr.GetComponent<SpriteRenderer>();
                }
            }
        }

        public void Refresh2DSpritesAndColors()
        {
            EnsureProceduralSpritesCreated();
            Ensure2DSpriteChildren();

            if (body2DSpriteRenderer != null)
            {
                body2DSpriteRenderer.sprite = unitKind switch
                {
                    VisualUnitKind.BotTank => _cachedSquareTokenSprite, // 탱커: 견고한 사각형(방패형) 2D 스프라이트
                    VisualUnitKind.Boss => _cachedBossTokenSprite,      // 보스: 대형 이중 테두리 원형+팔각 코어 스프라이트
                    _ => _cachedCircleTokenSprite                       // 플레이어 / 힐러 / 원딜: 깔끔한 원형 2D 스프라이트
                };
                body2DSpriteRenderer.color = primaryColor;
                body2DSpriteRenderer.sortingOrder = unitKind == VisualUnitKind.Boss ? 6 : 8;
            }

            if (visorOrEmblem2DSpriteRenderer != null)
            {
                switch (unitKind)
                {
                    case VisualUnitKind.Boss:
                        visorOrEmblem2DSpriteRenderer.sprite = _cachedBossVisorSprite;
                        visorOrEmblem2DSpriteRenderer.color = accentColor;
                        visorOrEmblem2DSpriteRenderer.sortingOrder = 7;
                        break;

                    case VisualUnitKind.BotHealer:
                        visorOrEmblem2DSpriteRenderer.sprite = _cachedHealerCrossSprite;
                        visorOrEmblem2DSpriteRenderer.color = new Color(1f, 1f, 1f, 0.92f);
                        visorOrEmblem2DSpriteRenderer.sortingOrder = 9;
                        break;

                    case VisualUnitKind.BotTank:
                        visorOrEmblem2DSpriteRenderer.sprite = _cachedTankShieldInnerSprite;
                        visorOrEmblem2DSpriteRenderer.color = new Color(0.85f, 0.95f, 1f, 0.90f);
                        visorOrEmblem2DSpriteRenderer.sortingOrder = 9;
                        break;

                    default:
                        visorOrEmblem2DSpriteRenderer.sprite = _cachedDpsInnerRingSprite;
                        visorOrEmblem2DSpriteRenderer.color = new Color(1f, 1f, 1f, 0.85f);
                        visorOrEmblem2DSpriteRenderer.sortingOrder = 9;
                        break;
                }
            }

            if (directionArrow2DSpriteRenderer != null)
            {
                directionArrow2DSpriteRenderer.sprite = _cachedForwardArrowSprite;
                directionArrow2DSpriteRenderer.color = unitKind == VisualUnitKind.Boss
                    ? new Color(1f, 0.25f, 0.25f, 1f)
                    : Color.white;
                directionArrow2DSpriteRenderer.sortingOrder = 10;
            }
        }

        // =====================================================================
        // 외부 이미지 에셋 없이 코드로 깔끔한 고해상도 2D 스프라이트 생성
        // =====================================================================
        private static void EnsureProceduralSpritesCreated()
        {
            if (_cachedCircleTokenSprite == null)
            {
                _cachedCircleTokenSprite = CreateCircleTokenSprite(256);
            }
            if (_cachedSquareTokenSprite == null)
            {
                _cachedSquareTokenSprite = CreateRoundedSquareTokenSprite(256);
            }
            if (_cachedBossTokenSprite == null)
            {
                _cachedBossTokenSprite = CreateBossTokenSprite(256);
            }
            if (_cachedForwardArrowSprite == null)
            {
                _cachedForwardArrowSprite = CreateForwardDirectionArrowSprite(256);
            }
            if (_cachedBossVisorSprite == null)
            {
                _cachedBossVisorSprite = CreateBossVisorSprite(256);
            }
            if (_cachedHealerCrossSprite == null)
            {
                _cachedHealerCrossSprite = CreateHealerCrossSprite(256);
            }
            if (_cachedTankShieldInnerSprite == null)
            {
                _cachedTankShieldInnerSprite = CreateTankShieldInnerSprite(256);
            }
            if (_cachedDpsInnerRingSprite == null)
            {
                _cachedDpsInnerRingSprite = CreateDpsInnerRingSprite(256);
            }
        }

        /// <summary>
        /// 원형 캐릭터 토큰 (검은 외곽 테두리 + 밝은 내부 림 + 역할군 색상 채우기 영역)
        /// </summary>
        private static Sprite CreateCircleTokenSprite(int size)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Proc2D_CircleToken",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            Color[] pixels = new Color[size * size];
            float half = size * 0.5f;
            float outerRadius = half * 0.72f;
            float darkBorderInner = outerRadius - (size * 0.055f);
            float whiteRimInner = darkBorderInner - (size * 0.025f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) - half;
                    float dy = (y + 0.5f) - half;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    Color c = Color.clear;
                    if (dist <= outerRadius + 1.5f)
                    {
                        float outerAlpha = Mathf.Clamp01((outerRadius + 1.0f - dist) / 1.5f);
                        if (dist >= darkBorderInner)
                        {
                            // 진한 검은색 외곽 테두리 (SpriteRenderer.color 곱셈 시에도 어두운 테두리 유지)
                            c = new Color(0.12f, 0.12f, 0.15f, outerAlpha);
                        }
                        else if (dist >= whiteRimInner)
                        {
                            // 선명한 내부 림 테두리
                            c = new Color(1f, 1f, 1f, 1f);
                        }
                        else
                        {
                            // 중심부 (역할군 색상이 그대로 표현됨 + 은은한 입체 그라데이션)
                            float radialShade = Mathf.Lerp(1.0f, 0.84f, dist / whiteRimInner);
                            c = new Color(radialShade, radialShade, radialShade, 1f);
                        }
                    }

                    pixels[y * size + x] = c;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 128f);
        }

        /// <summary>
        /// 탱커용 둥근 사각형(Square/Shield) 토큰 (외곽 테두리 + 역할군 색상 채우기)
        /// </summary>
        private static Sprite CreateRoundedSquareTokenSprite(int size)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Proc2D_SquareToken",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            Color[] pixels = new Color[size * size];
            float half = size * 0.5f;
            float boxHalf = half * 0.54f;
            float cornerRadius = size * 0.10f;
            float outerLimit = cornerRadius;
            float darkInner = cornerRadius - (size * 0.055f);
            float rimInner = darkInner - (size * 0.025f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Abs((x + 0.5f) - half) - boxHalf;
                    float dy = Mathf.Abs((y + 0.5f) - half) - boxHalf;
                    float qx = Mathf.Max(dx, 0f);
                    float qy = Mathf.Max(dy, 0f);
                    float signedDist = Mathf.Sqrt(qx * qx + qy * qy) + Mathf.Min(Mathf.Max(dx, dy), 0f);

                    Color c = Color.clear;
                    if (signedDist <= outerLimit + 1.5f)
                    {
                        float alpha = Mathf.Clamp01((outerLimit + 1.0f - signedDist) / 1.5f);
                        if (signedDist >= darkInner)
                        {
                            c = new Color(0.10f, 0.12f, 0.16f, alpha);
                        }
                        else if (signedDist >= rimInner)
                        {
                            c = new Color(1f, 1f, 1f, 1f);
                        }
                        else
                        {
                            float centerDist = Mathf.Sqrt(
                                ((x + 0.5f - half) / half) * ((x + 0.5f - half) / half) +
                                ((y + 0.5f - half) / half) * ((y + 0.5f - half) / half)
                            );
                            float shade = Mathf.Lerp(1.0f, 0.85f, Mathf.Clamp01(centerDist));
                            c = new Color(shade, shade, shade, 1f);
                        }
                    }

                    pixels[y * size + x] = c;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 128f);
        }

        /// <summary>
        /// 보스용 대형 2D 토큰 (이중 테두리 원형 + 내부 코어 문양)
        /// </summary>
        private static Sprite CreateBossTokenSprite(int size)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Proc2D_BossToken",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            Color[] pixels = new Color[size * size];
            float half = size * 0.5f;
            float outerRadius = half * 0.82f;
            float borderInner = outerRadius - (size * 0.06f);
            float goldRimInner = borderInner - (size * 0.03f);
            float coreRingOuter = half * 0.42f;
            float coreRingInner = half * 0.36f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) - half;
                    float dy = (y + 0.5f) - half;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    Color c = Color.clear;
                    if (dist <= outerRadius + 1.5f)
                    {
                        float alpha = Mathf.Clamp01((outerRadius + 1.0f - dist) / 1.5f);
                        if (dist >= borderInner)
                        {
                            c = new Color(0.08f, 0.06f, 0.12f, alpha);
                        }
                        else if (dist >= goldRimInner)
                        {
                            c = new Color(1f, 0.95f, 0.80f, 1f);
                        }
                        else if (dist <= coreRingOuter && dist >= coreRingInner)
                        {
                            c = new Color(0.22f, 0.18f, 0.28f, 1f);
                        }
                        else
                        {
                            float shade = Mathf.Lerp(1.0f, 0.78f, dist / goldRimInner);
                            c = new Color(shade, shade, shade, 1f);
                        }
                    }

                    pixels[y * size + x] = c;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 128f);
        }

        /// <summary>
        /// 바라보는 정면 방향(+Z = 스프라이트 상단 +Y)을 선명하게 가리키는 화살표/셰브론 스프라이트
        /// </summary>
        private static Sprite CreateForwardDirectionArrowSprite(int size)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Proc2D_ForwardArrow",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            Color[] pixels = new Color[size * size];

            // 정규화 좌표계 (-1 ~ +1)에서 상단(+Y)에 삼각형 화살촉 + 셰브론 배치
            Vector2 tip = new Vector2(0f, 0.95f);
            Vector2 leftWing = new Vector2(-0.42f, 0.44f);
            Vector2 rightWing = new Vector2(0.42f, 0.44f);
            Vector2 innerNotch = new Vector2(0f, 0.58f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = ((x + 0.5f) / size) * 2f - 1f;
                    float ny = ((y + 0.5f) / size) * 2f - 1f;
                    Vector2 p = new Vector2(nx, ny);

                    bool insideLeftHalf = PointInTriangle(p, tip, leftWing, innerNotch);
                    bool insideRightHalf = PointInTriangle(p, tip, innerNotch, rightWing);

                    if (insideLeftHalf || insideRightHalf)
                    {
                        float dEdge = Mathf.Min(
                            DistanceToSegment(p, tip, leftWing),
                            DistanceToSegment(p, tip, rightWing)
                        );
                        dEdge = Mathf.Min(dEdge, Mathf.Min(
                            DistanceToSegment(p, leftWing, innerNotch),
                            DistanceToSegment(p, rightWing, innerNotch)
                        ));

                        if (dEdge < 0.035f)
                        {
                            pixels[y * size + x] = new Color(0.08f, 0.08f, 0.12f, 0.98f);
                        }
                        else
                        {
                            pixels[y * size + x] = Color.white;
                        }
                    }
                    else
                    {
                        pixels[y * size + x] = Color.clear;
                    }
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 128f);
        }

        /// <summary>
        /// 보스 정면 바이저(눈) 2D 스프라이트
        /// </summary>
        private static Sprite CreateBossVisorSprite(int size)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Proc2D_BossVisor",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = ((x + 0.5f) / size) * 2f - 1f;
                    float ny = ((y + 0.5f) / size) * 2f - 1f;

                    // 정면 상단(ny: 0.25 ~ 0.48, |nx| <= 0.38)에 날카로운 바이저 도형
                    float visorYCenter = 0.36f - Mathf.Abs(nx) * 0.12f;
                    float halfHeight = 0.085f;
                    if (Mathf.Abs(nx) <= 0.38f && Mathf.Abs(ny - visorYCenter) <= halfHeight)
                    {
                        bool border = Mathf.Abs(nx) > 0.34f || Mathf.Abs(ny - visorYCenter) > (halfHeight - 0.025f);
                        pixels[y * size + x] = border ? new Color(0.1f, 0.05f, 0.05f, 1f) : Color.white;
                    }
                    else
                    {
                        pixels[y * size + x] = Color.clear;
                    }
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 128f);
        }

        /// <summary>
        /// 힐러 봇 중앙 십자(+) 엠블럼 스프라이트
        /// </summary>
        private static Sprite CreateHealerCrossSprite(int size)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Proc2D_HealerCross",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = Mathf.Abs(((x + 0.5f) / size) * 2f - 1f);
                    float ny = Mathf.Abs(((y + 0.5f) / size) * 2f - 1f);

                    bool inCross = (nx <= 0.09f && ny <= 0.26f) || (ny <= 0.09f && nx <= 0.26f);
                    pixels[y * size + x] = inCross ? new Color(1f, 1f, 1f, 0.9f) : Color.clear;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 128f);
        }

        /// <summary>
        /// 탱커 봇 내부 사각 방패 라인 스프라이트
        /// </summary>
        private static Sprite CreateTankShieldInnerSprite(int size)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Proc2D_TankInner",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = Mathf.Abs(((x + 0.5f) / size) * 2f - 1f);
                    float ny = Mathf.Abs(((y + 0.5f) / size) * 2f - 1f);
                    float box = Mathf.Max(nx, ny);

                    bool onFrame = box >= 0.24f && box <= 0.31f;
                    pixels[y * size + x] = onFrame ? new Color(1f, 1f, 1f, 0.85f) : Color.clear;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 128f);
        }

        /// <summary>
        /// 딜러(플레이어/원딜) 내부 코어 링 스프라이트
        /// </summary>
        private static Sprite CreateDpsInnerRingSprite(int size)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Proc2D_DpsInner",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = ((x + 0.5f) / size) * 2f - 1f;
                    float ny = ((y + 0.5f) / size) * 2f - 1f;
                    float r = Mathf.Sqrt(nx * nx + ny * ny);

                    bool onRing = r >= 0.22f && r <= 0.29f;
                    pixels[y * size + x] = onRing ? new Color(1f, 1f, 1f, 0.80f) : Color.clear;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 128f);
        }

        private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Sign(p, a, b);
            float d2 = Sign(p, b, c);
            float d3 = Sign(p, c, a);

            bool hasNeg = (d1 < 0f) || (d2 < 0f) || (d3 < 0f);
            bool hasPos = (d1 > 0f) || (d2 > 0f) || (d3 > 0f);
            return !(hasNeg && hasPos);
        }

        private static float Sign(Vector2 p1, Vector2 p2, Vector2 p3)
        {
            return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lenSq = ab.sqrMagnitude;
            if (lenSq < 0.00001f) return Vector2.Distance(p, a);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq);
            return Vector2.Distance(p, a + t * ab);
        }
    }
}
