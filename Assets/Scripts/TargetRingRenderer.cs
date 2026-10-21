using System.Collections.Generic;
using UnityEngine;

namespace RaidRogue
{
    /// <summary>
    /// 보스 발밑 XZ 평면에 파판14(FFXIV) 스타일의 4분할 타겟 링을 LineRenderer들로 그리는 컴포넌트.
    /// - 원 테두리 및 영역별 아크 (정면 0~45도, 좌/우 측면 45~135도, 후면 135~180도)
    /// - 4분할 대각선 경계선 (+45도, -45도, +135도, -135도)
    /// - 보스 정면(0도, +Z)을 가리키는 대형 빨간색 화살표
    /// </summary>
    public class TargetRingRenderer : MonoBehaviour
    {
        [Header("Ring Dimensions (XZ Plane)")]
        [Tooltip("타겟 링 외곽 반지름")]
        public float outerRadius = 3.2f;

        [Tooltip("타겟 링 내측 반지름 (경계선 시작점)")]
        public float innerRadius = 1.6f;

        [Tooltip("바닥(Y=0)으로부터 띄울 높이 (Z-Fighting 방지)")]
        public float groundOffset = 0.05f;

        [Header("Colors")]
        public Color mainRingColor = new Color(1f, 0.35f, 0.35f, 0.95f);
        public Color frontArcColor = new Color(1f, 0.2f, 0.2f, 1f);
        public Color flankArcColor = new Color(0.2f, 0.95f, 0.45f, 1f);
        public Color rearArcColor = new Color(0.25f, 0.8f, 1f, 1f);
        public Color boundaryLineColor = new Color(1f, 0.92f, 0.4f, 0.95f);
        public Color frontArrowColor = new Color(1f, 0.12f, 0.12f, 1f);

        private readonly List<LineRenderer> _lineRenderers = new List<LineRenderer>();
        private static Material _sharedLineMaterial;

        private void Awake()
        {
            BuildTargetRing();
        }

        [ContextMenu("Rebuild Target Ring")]
        public void BuildTargetRing()
        {
            ClearExistingLines();

            // 1. 외곽 메인 링 (파판14 스타일: 정면~측면 -135° ~ +135° 구간은 굵은 메인 링)
            CreateArc("Ring_Outer_FrontAndFlanks", outerRadius, -135f, 135f, 72, 0.11f, mainRingColor, false);

            // 2. 후면 외곽 링 (135° ~ 225° 구간은 후면 구분을 위해 하늘색 이중/강조 링으로 표시)
            CreateArc("Ring_Outer_Rear", outerRadius, 135f, 225f, 32, 0.08f, rearArcColor, false);

            // 3. 내측 4분할 색상 가이드 아크 (정면 / 좌측면 / 우측면 / 후면을 한눈에 구분)
            float sectorArcRadius = outerRadius - 0.22f;
            // 정면 (-45° ~ +45°)
            CreateArc("Sector_Front", sectorArcRadius, -44f, 44f, 32, 0.08f, frontArcColor, false);
            // 우측면 (+45° ~ +135°)
            CreateArc("Sector_RightFlank", sectorArcRadius, 46f, 134f, 32, 0.08f, flankArcColor, false);
            // 좌측면 (-135° ~ -45°)
            CreateArc("Sector_LeftFlank", sectorArcRadius, -134f, -46f, 32, 0.08f, flankArcColor, false);
            // 후면 (+135° ~ +225° [= -135°])
            CreateArc("Sector_Rear", sectorArcRadius, 136f, 224f, 32, 0.08f, rearArcColor, false);

            // 4. 내측 코어 원 (보스 히트박스 중심 링)
            CreateArc("Ring_InnerCore", innerRadius, 0f, 360f, 64, 0.05f, new Color(0.85f, 0.75f, 1f, 0.55f), true);

            // 5. 4분할 경계선 (정면 0~45도 / 측면 45~135도 / 후면 135~180도 구분선)
            float[] boundaryAngles = { 45f, -45f, 135f, -135f };
            foreach (float angleDeg in boundaryAngles)
            {
                Vector3 startPt = AngleToLocalXZ(angleDeg, innerRadius);
                Vector3 endPt = AngleToLocalXZ(angleDeg, outerRadius + 0.28f);
                CreateSegment($"Boundary_{angleDeg}", new[] { startPt, endPt }, 0.085f, boundaryLineColor, false);
            }

            // 6. 측면(90°, -90°) 및 후면(180°) 눈금 마커
            CreateTickMark("Tick_Right90", 90f, outerRadius - 0.15f, outerRadius + 0.22f, 0.07f, flankArcColor);
            CreateTickMark("Tick_Left90", -90f, outerRadius - 0.15f, outerRadius + 0.22f, 0.07f, flankArcColor);
            CreateTickMark("Tick_Rear180", 180f, outerRadius - 0.2f, outerRadius + 0.25f, 0.07f, rearArcColor);

            // 7. 보스 정면(0도, +Z)을 가리키는 큰 빨간색 화살표 (파판14 스타일 셰브론 + 화살표 축)
            BuildFrontDirectionArrow();
        }

        private void BuildFrontDirectionArrow()
        {
            float y = groundOffset + 0.01f;

            // 중앙 정면 줄기 (보스 코어에서 정면 화살표 끝까지)
            Vector3 shaftStart = new Vector3(0f, y, innerRadius * 0.6f);
            Vector3 arrowTip = new Vector3(0f, y, outerRadius + 1.35f);
            CreateSegment("FrontArrow_Shaft", new[] { shaftStart, arrowTip }, 0.14f, frontArrowColor, false);

            // 1차 대형 화살촉 (Outer Chevron)
            Vector3 leftWing1 = new Vector3(-0.95f, y, outerRadius + 0.25f);
            Vector3 rightWing1 = new Vector3(0.95f, y, outerRadius + 0.25f);
            CreateSegment("FrontArrow_HeadOuter", new[] { leftWing1, arrowTip, rightWing1 }, 0.16f, frontArrowColor, false);

            // 2차 내부 화살촉 (Inner Chevron - 파판14 스타일 이중 셰브론)
            Vector3 arrowTip2 = new Vector3(0f, y, outerRadius + 0.85f);
            Vector3 leftWing2 = new Vector3(-0.65f, y, outerRadius + 0.05f);
            Vector3 rightWing2 = new Vector3(0.65f, y, outerRadius + 0.05f);
            CreateSegment("FrontArrow_HeadInner", new[] { leftWing2, arrowTip2, rightWing2 }, 0.11f, new Color(1f, 0.55f, 0.2f, 1f), false);
        }

        private void CreateTickMark(string lineName, float angleDeg, float rStart, float rEnd, float width, Color color)
        {
            Vector3 p0 = AngleToLocalXZ(angleDeg, rStart);
            Vector3 p1 = AngleToLocalXZ(angleDeg, rEnd);
            CreateSegment(lineName, new[] { p0, p1 }, width, color, false);
        }

        private void CreateArc(
            string lineName,
            float radius,
            float startAngleDeg,
            float endAngleDeg,
            int segments,
            float width,
            Color color,
            bool loop)
        {
            int pointCount = loop ? segments : segments + 1;
            Vector3[] points = new Vector3[pointCount];

            for (int i = 0; i < pointCount; i++)
            {
                float t = (float)i / segments;
                float angleDeg = Mathf.Lerp(startAngleDeg, endAngleDeg, t);
                points[i] = AngleToLocalXZ(angleDeg, radius);
            }

            CreateSegment(lineName, points, width, color, loop);
        }

        /// <summary>
        /// 보스 정면(+Z)을 0도로 기준 삼아 시계방향 각도(degree)를 로컬 XZ 좌표로 변환합니다.
        /// 0도 = (0, y, +r), 90도 = (+r, y, 0), 180도 = (0, y, -r), -90도 = (-r, y, 0)
        /// </summary>
        private Vector3 AngleToLocalXZ(float angleDeg, float radius)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            float x = Mathf.Sin(rad) * radius;
            float z = Mathf.Cos(rad) * radius;
            return new Vector3(x, groundOffset, z);
        }

        private LineRenderer CreateSegment(string lineName, Vector3[] points, float width, Color color, bool loop)
        {
            GameObject child = new GameObject(lineName);
            child.transform.SetParent(transform, false);
            child.transform.localPosition = Vector3.zero;
            child.transform.localRotation = Quaternion.identity;
            child.transform.localScale = Vector3.one;

            LineRenderer lr = child.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = loop;
            lr.positionCount = points.Length;
            lr.SetPositions(points);
            lr.startWidth = width;
            lr.endWidth = width;
            lr.startColor = color;
            lr.endColor = color;
            lr.numCapVertices = 4;
            lr.numCornerVertices = 4;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.sharedMaterial = GetOrCreateLineMaterial();

            _lineRenderers.Add(lr);
            return lr;
        }

        private void ClearExistingLines()
        {
            _lineRenderers.Clear();

            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child.name.StartsWith("Ring_") ||
                    child.name.StartsWith("Sector_") ||
                    child.name.StartsWith("Boundary_") ||
                    child.name.StartsWith("Tick_") ||
                    child.name.StartsWith("FrontArrow_"))
                {
                    if (Application.isPlaying)
                    {
                        Destroy(child.gameObject);
                    }
                    else
                    {
                        DestroyImmediate(child.gameObject);
                    }
                }
            }
        }

        private static Material GetOrCreateLineMaterial()
        {
            if (_sharedLineMaterial != null)
            {
                return _sharedLineMaterial;
            }

            // Sprites/Default는 Built-in과 URP 모두에서 LineRenderer의 정점 색상(startColor/endColor)을 그대로 출력합니다.
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            _sharedLineMaterial = new Material(shader)
            {
                name = "TargetRing_LineMaterial",
                color = Color.white
            };
            return _sharedLineMaterial;
        }
    }
}
