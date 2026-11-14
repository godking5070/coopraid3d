using System.Collections.Generic;
using UnityEngine;

namespace RaidRogue
{
    public enum AoETelegraphType
    {
        Circle_Radius6,         // 원형 장판 (보스 주변 반경 6m)
        Donut_Inner4_Outer12,   // 도넛 장판 (내경 4m ~ 외경 12m, 보스 발밑 안전지대)
        Rectangle = 8,
        FrontCone_120Deg = 2        // 전방 120도 부채꼴 장판
    }

    /// <summary>
    /// 보스가 스킬을 시전할 때 바닥(XZ 평면)에 반투명한 붉은색 절차적 메쉬(Mesh)와
    /// 테두리 라인으로 공격 범위를 미리 보여주는 3D 예고 장판 컴포넌트.
    /// </summary>
    public partial class AoETelegraph : MonoBehaviour
    {
        [Header("Telegraph Settings")]
        public AoETelegraphType telegraphType = AoETelegraphType.Circle_Radius6;
        public float innerRadius = 0f;
        public float outerRadius = 6f;
        public float coneAngleDeg = 120f;
        public float groundY = 0.06f;

        [Header("Colors")]
        public Color baseFillColor = new Color(1.0f, 0.12f, 0.12f, 0.32f);
        public Color progressFillColor = new Color(1.0f, 0.18f, 0.10f, 0.55f);
        public Color borderColor = new Color(1.0f, 0.30f, 0.20f, 0.95f);
        public bool guideOnly, guideLocked, tankGuide;
        public bool thornStyle;

        public void SetGuideStyle(bool locked)
        {
            guideOnly = true; guideLocked = locked;
            baseFillColor = locked ? new Color(1f, .72f, .12f, .15f) : new Color(1f, .58f, .12f, .07f);
            progressFillColor = locked ? new Color(1f, .83f, .22f, .18f) : new Color(1f, .65f, .2f, .09f);
            borderColor = locked ? new Color(1f, .9f, .32f, 1f) : new Color(1f, .63f, .2f, .95f);
            if (_baseMeshRenderer != null) RefreshColors();
        }
        public void SetTankGuideStyle()
        {
            guideOnly = true; guideLocked = false; tankGuide = true;
            baseFillColor = new Color(.08f, .34f, 1f, .13f);
            progressFillColor = new Color(.12f, .7f, 1f, .42f);
            borderColor = new Color(.3f, .8f, 1f, 1f);
            if (_baseMeshRenderer != null) RefreshColors();
        }
        public void SetThornStyle()
        {
            thornStyle=true;
            baseFillColor = new Color(.08f,.04f,.12f,.55f);
            progressFillColor = new Color(.18f,.03f,.2f,.78f);
            borderColor = new Color(.4f,.12f,.45f,1f);
            if (_baseMeshRenderer != null) RefreshColors();
        }

        private MeshFilter _baseMeshFilter;
        private MeshRenderer _baseMeshRenderer;
        private MeshFilter _progressMeshFilter;
        private MeshRenderer _progressMeshRenderer;
        private readonly List<LineRenderer> _borderLines = new List<LineRenderer>();
        private readonly List<Material> _ownedMaterials = new List<Material>();
        private Mesh _baseMesh, _progressMesh;
        private Vector3[] _progressVertices;
        private Color[] _progressColors;
        private Vector2[] _progressUvs;
        private int[] _progressTriangles;
        private int _progressTopology = -1, _progressSegments = -1;

        private float _duration = 2.5f;
        private float _elapsed;
        private bool _active;

        public static AoETelegraph Spawn(
            AoETelegraphType type,
            Vector3 centerXZ,
            Vector3 forwardXZ,
            float duration,
            float customInnerRadius = 4f,
            float customOuterRadius = 6f,
            float customConeAngle = 120f)
        {
            GameObject go = new GameObject($"AoETelegraph_{type}");
            go.transform.position = new Vector3(centerXZ.x, 0f, centerXZ.z);

            Vector3 flatForward = new Vector3(forwardXZ.x, 0f, forwardXZ.z);
            if (flatForward.sqrMagnitude < 0.0001f)
            {
                flatForward = Vector3.forward;
            }
            go.transform.rotation = Quaternion.LookRotation(flatForward.normalized, Vector3.up);
            go.transform.localScale = Vector3.one;

            AoETelegraph telegraph = go.AddComponent<AoETelegraph>();
            telegraph.Setup(type, duration, customInnerRadius, customOuterRadius, customConeAngle);
            return telegraph;
        }

        public void Setup(
            AoETelegraphType type,
            float duration,
            float customInnerRadius,
            float customOuterRadius,
            float customConeAngle)
        {
            telegraphType = type;
            _duration = Mathf.Max(0.1f, duration);
            _networkStart = RaidCombatAuthority.ServerTime;
            _elapsed = 0f;
            _active = true;

            switch (type)
            {
                case AoETelegraphType.Rectangle:
                    innerRadius = customInnerRadius; outerRadius = customOuterRadius; coneAngleDeg = 0; break;
                case AoETelegraphType.Circle_Radius6:
                    innerRadius = 0f;
                    outerRadius = customOuterRadius > 0f ? customOuterRadius : 6f;
                    coneAngleDeg = 360f;
                    break;

                case AoETelegraphType.Donut_Inner4_Outer12:
                    innerRadius = customInnerRadius > 0f ? customInnerRadius : 4f;
                    outerRadius = customOuterRadius > 0f ? customOuterRadius : 12f;
                    coneAngleDeg = 360f;
                    break;

                case AoETelegraphType.FrontCone_120Deg:
                    innerRadius = 0f;
                    outerRadius = customOuterRadius > 0f ? customOuterRadius : 9.5f;
                    coneAngleDeg = customConeAngle > 0f ? customConeAngle : 120f;
                    break;
            }

            CreateMeshObjects();
            RebuildVisuals(0f);
        }

        private void Update()
        {
            if (!_active)
            {
                return;
            }

            _elapsed = _networkReplica ? RaidCombatAuthority.ServerTime - _networkStart : _elapsed + Time.deltaTime;
            float t = Mathf.Clamp01(_elapsed / _duration);
            UpdateProgressVisual(t);
        }

        public bool IsPointInsideXZ(Vector3 worldPos)
        {
            Vector3 toPointXZ = new Vector3(
                worldPos.x - transform.position.x,
                0f,
                worldPos.z - transform.position.z
            );

            float dist = toPointXZ.magnitude;

            switch (telegraphType)
            {
                case AoETelegraphType.Rectangle:
                    Vector3 local = transform.InverseTransformPoint(worldPos);
                    return GuardianNormalState.InRectangle(local.x, local.z, innerRadius, outerRadius);
                case AoETelegraphType.Circle_Radius6:
                    return dist <= outerRadius;

                case AoETelegraphType.Donut_Inner4_Outer12:
                    return dist >= innerRadius && dist <= outerRadius;

                case AoETelegraphType.FrontCone_120Deg:
                    if (dist > outerRadius)
                    {
                        return false;
                    }
                    if (dist < 0.001f)
                    {
                        return true;
                    }
                    Vector3 forwardXZ = new Vector3(transform.forward.x, 0f, transform.forward.z).normalized;
                    float angle = Vector3.Angle(forwardXZ, toPointXZ.normalized);
                    return angle <= (coneAngleDeg * 0.5f);

                default:
                    return false;
            }
        }

        private void CreateMeshObjects()
        {
            GameObject baseObj = new GameObject("Telegraph_BaseMesh");
            baseObj.transform.SetParent(transform, false);
            baseObj.transform.localPosition = new Vector3(0f, groundY, 0f);
            _baseMeshFilter = baseObj.AddComponent<MeshFilter>();
            _baseMeshRenderer = baseObj.AddComponent<MeshRenderer>();
            _baseMeshRenderer.sharedMaterial = CreateOwnedMaterial(baseFillColor);
            _baseMeshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _baseMeshRenderer.receiveShadows = false;

            GameObject progObj = new GameObject("Telegraph_ProgressMesh");
            progObj.transform.SetParent(transform, false);
            progObj.transform.localPosition = new Vector3(0f, groundY + 0.01f, 0f);
            _progressMeshFilter = progObj.AddComponent<MeshFilter>();
            _progressMeshRenderer = progObj.AddComponent<MeshRenderer>();
            _progressMeshRenderer.sharedMaterial = CreateOwnedMaterial(progressFillColor);
            _progressMeshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _progressMeshRenderer.receiveShadows = false;
        }

        private void RebuildVisuals(float progress)
        {
            int segments = telegraphType == AoETelegraphType.FrontCone_120Deg ? 48 : 72;
            float halfAngle = coneAngleDeg * 0.5f;

            Mesh fullMesh = telegraphType == AoETelegraphType.Rectangle ? RectangleMesh(innerRadius, outerRadius, baseFillColor) : BuildSectorOrRingMesh(innerRadius, outerRadius, -halfAngle, halfAngle, segments, baseFillColor);
            if (_baseMesh != null) Destroy(_baseMesh);
            _baseMesh = fullMesh;
            _baseMeshFilter.sharedMesh = _baseMesh;

            BuildBorders(segments, halfAngle);
            UpdateProgressVisual(progress);
        }

        public void RefreshColors()
        {
            if (_baseMeshRenderer != null) _baseMeshRenderer.sharedMaterial.color = baseFillColor;
            if (_progressMeshRenderer != null) _progressMeshRenderer.sharedMaterial.color = progressFillColor;
            RebuildVisuals(Mathf.Clamp01(_elapsed / _duration));
        }
        public void SetRectangleLength(float length)
        {
            if (telegraphType != AoETelegraphType.Rectangle || Mathf.Abs(outerRadius-length) < .001f) return;
            outerRadius = Mathf.Max(.1f, length);
            RebuildVisuals(Mathf.Clamp01(_elapsed / _duration));
        }

        private void UpdateProgressVisual(float progress)
        {
            if (_progressMeshFilter == null)
            {
                return;
            }

            int segments = telegraphType == AoETelegraphType.FrontCone_120Deg ? 36 : 60;
            float halfAngle = coneAngleDeg * 0.5f;
            float currentOuter = Mathf.Lerp(innerRadius, outerRadius, Mathf.Clamp01(progress));

            if (_progressMesh == null)
            {
                _progressMesh = new Mesh { name = "TelegraphProgressMesh" };
                _progressMesh.MarkDynamic();
                _progressMeshFilter.sharedMesh = _progressMesh;
                _progressTopology = -1;
            }

            bool rectangle = telegraphType == AoETelegraphType.Rectangle;
            // The old sector builder returned an empty mesh at zero thickness.
            // Hide it without discarding the mesh or its reusable buffers.
            bool visible = rectangle || currentOuter > innerRadius + .001f;
            _progressMeshRenderer.enabled = visible;
            if (!visible) return;

            int topology = rectangle ? 0 : innerRadius > .001f ? 2 : 1;
            bool topologyChanged = EnsureProgressBuffers(topology, segments);
            Color dynamicColor = progressFillColor;
            if (!guideOnly && progress > 0.8f)
            {
                float pulse = Mathf.PingPong(Time.time * 12f, 1f);
                dynamicColor = Color.Lerp(progressFillColor, new Color(1f, 0.45f, 0.2f, 0.72f), pulse);
            }

            if (rectangle)
            {
                float length = outerRadius * progress;
                _progressVertices[0] = new Vector3(-innerRadius, 0, 0);
                _progressVertices[1] = new Vector3(-innerRadius, 0, length);
                _progressVertices[2] = new Vector3(innerRadius, 0, length);
                _progressVertices[3] = new Vector3(innerRadius, 0, 0);
            }
            else
            {
                if (topology == 1)
                {
                    _progressVertices[0] = Vector3.zero;
                    _progressUvs[0] = new Vector2(.5f, .5f);
                }
                for (int i = 0; i <= segments; i++)
                {
                    float t = (float)i / segments;
                    float rad = Mathf.Lerp(-halfAngle, halfAngle, t) * Mathf.Deg2Rad;
                    float sin = Mathf.Sin(rad), cos = Mathf.Cos(rad);
                    if (topology == 2)
                    {
                        int inner = i * 2, outer = inner + 1;
                        _progressVertices[inner] = new Vector3(sin * innerRadius, 0, cos * innerRadius);
                        _progressVertices[outer] = new Vector3(sin * currentOuter, 0, cos * currentOuter);
                        _progressUvs[inner] = new Vector2(t, 0);
                        _progressUvs[outer] = new Vector2(t, 1);
                    }
                    else
                    {
                        float x = sin * currentOuter, z = cos * currentOuter;
                        _progressVertices[i + 1] = new Vector3(x, 0, z);
                        _progressUvs[i + 1] = new Vector2(x / (currentOuter * 2) + .5f, z / (currentOuter * 2) + .5f);
                    }
                }
            }
            for (int i = 0; i < _progressColors.Length; i++) _progressColors[i] = dynamicColor;
            _progressMesh.vertices = _progressVertices;
            _progressMesh.colors = _progressColors;
            if (!rectangle) _progressMesh.uv = _progressUvs;
            if (topologyChanged) _progressMesh.triangles = _progressTriangles;
            _progressMesh.RecalculateNormals();
            _progressMesh.RecalculateBounds();
        }

        private bool EnsureProgressBuffers(int topology, int segments)
        {
            if (_progressTopology == topology && _progressSegments == segments) return false;
            _progressTopology = topology;
            _progressSegments = segments;
            _progressMesh.Clear(false);
            int vertexCount = topology == 0 ? 4 : topology == 2 ? (segments + 1) * 2 : segments + 2;
            _progressVertices = new Vector3[vertexCount];
            _progressColors = new Color[vertexCount];
            _progressUvs = topology == 0 ? null : new Vector2[vertexCount];
            _progressTriangles = new int[topology == 0 ? 6 : segments * (topology == 2 ? 6 : 3)];
            if (topology == 0)
            {
                _progressTriangles[0] = 0; _progressTriangles[1] = 1; _progressTriangles[2] = 2;
                _progressTriangles[3] = 0; _progressTriangles[4] = 2; _progressTriangles[5] = 3;
            }
            else for (int i = 0; i < segments; i++)
            {
                if (topology == 2)
                {
                    int tri = i * 6, inner = i * 2, outer = inner + 1;
                    int nextInner = inner + 2, nextOuter = inner + 3;
                    _progressTriangles[tri] = inner;
                    _progressTriangles[tri + 1] = outer;
                    _progressTriangles[tri + 2] = nextOuter;
                    _progressTriangles[tri + 3] = inner;
                    _progressTriangles[tri + 4] = nextOuter;
                    _progressTriangles[tri + 5] = nextInner;
                }
                else
                {
                    int tri = i * 3;
                    _progressTriangles[tri] = 0;
                    _progressTriangles[tri + 1] = i + 1;
                    _progressTriangles[tri + 2] = i + 2;
                }
            }
            return true;
        }

        private static Mesh RectangleMesh(float width, float length, Color color)
        {
            var mesh = new Mesh { name = "ChargeRectangle" };
            mesh.vertices = new[] {new Vector3(-width,0,0), new Vector3(-width,0,length),new Vector3(width,0,length),new Vector3(width,0,0)};
            mesh.triangles = new[] {0,1,2,0,2,3}; mesh.colors = new[] {color,color,color,color};
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        public static Mesh BuildSectorOrRingMesh(
            float rInner,
            float rOuter,
            float startAngleDeg,
            float endAngleDeg,
            int segments,
            Color vertexColor)
        {
            Mesh mesh = new Mesh
            {
                name = "ProceduralAoEMesh"
            };

            if (rOuter <= rInner + 0.001f)
            {
                return mesh;
            }

            bool isRing = rInner > 0.001f;

            if (!isRing)
            {
                int vertCount = segments + 2;
                Vector3[] vertices = new Vector3[vertCount];
                Color[] colors = new Color[vertCount];
                Vector2[] uvs = new Vector2[vertCount];
                int[] triangles = new int[segments * 3];

                vertices[0] = Vector3.zero;
                colors[0] = vertexColor;
                uvs[0] = new Vector2(0.5f, 0.5f);

                for (int i = 0; i <= segments; i++)
                {
                    float t = (float)i / segments;
                    float deg = Mathf.Lerp(startAngleDeg, endAngleDeg, t);
                    float rad = deg * Mathf.Deg2Rad;
                    float x = Mathf.Sin(rad) * rOuter;
                    float z = Mathf.Cos(rad) * rOuter;

                    vertices[i + 1] = new Vector3(x, 0f, z);
                    colors[i + 1] = vertexColor;
                    uvs[i + 1] = new Vector2(x / (rOuter * 2f) + 0.5f, z / (rOuter * 2f) + 0.5f);

                    if (i < segments)
                    {
                        int triIdx = i * 3;
                        triangles[triIdx] = 0;
                        triangles[triIdx + 1] = i + 1;
                        triangles[triIdx + 2] = i + 2;
                    }
                }

                mesh.vertices = vertices;
                mesh.colors = colors;
                mesh.uv = uvs;
                mesh.triangles = triangles;
            }
            else
            {
                int vertCount = (segments + 1) * 2;
                Vector3[] vertices = new Vector3[vertCount];
                Color[] colors = new Color[vertCount];
                Vector2[] uvs = new Vector2[vertCount];
                int[] triangles = new int[segments * 6];

                for (int i = 0; i <= segments; i++)
                {
                    float t = (float)i / segments;
                    float deg = Mathf.Lerp(startAngleDeg, endAngleDeg, t);
                    float rad = deg * Mathf.Deg2Rad;
                    float sin = Mathf.Sin(rad);
                    float cos = Mathf.Cos(rad);

                    int innerIdx = i * 2;
                    int outerIdx = i * 2 + 1;

                    vertices[innerIdx] = new Vector3(sin * rInner, 0f, cos * rInner);
                    vertices[outerIdx] = new Vector3(sin * rOuter, 0f, cos * rOuter);
                    colors[innerIdx] = vertexColor;
                    colors[outerIdx] = vertexColor;
                    uvs[innerIdx] = new Vector2(t, 0f);
                    uvs[outerIdx] = new Vector2(t, 1f);

                    if (i < segments)
                    {
                        int triIdx = i * 6;
                        int nextInner = (i + 1) * 2;
                        int nextOuter = (i + 1) * 2 + 1;

                        triangles[triIdx] = innerIdx;
                        triangles[triIdx + 1] = outerIdx;
                        triangles[triIdx + 2] = nextOuter;

                        triangles[triIdx + 3] = innerIdx;
                        triangles[triIdx + 4] = nextOuter;
                        triangles[triIdx + 5] = nextInner;
                    }
                }

                mesh.vertices = vertices;
                mesh.colors = colors;
                mesh.uv = uvs;
                mesh.triangles = triangles;
            }

            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void BuildBorders(int segments, float halfAngle)
        {
            foreach (LineRenderer lr in _borderLines)
            {
                if (lr != null)
                {
                    var material = lr.sharedMaterial;
                    if (material != null && _ownedMaterials.Remove(material)) Destroy(material);
                    Destroy(lr.gameObject);
                }
            }
            _borderLines.Clear();

            if (telegraphType == AoETelegraphType.Rectangle)
            {
                float y = groundY + .02f;
                CreateLineRendererObject("ChargeBorder",new[] {new Vector3(-innerRadius,y,0),new Vector3(-innerRadius,y,outerRadius),new Vector3(innerRadius,y,outerRadius),new Vector3(innerRadius,y,0)},true,borderColor,.11f);
            }
            else if (telegraphType == AoETelegraphType.Circle_Radius6)
            {
                CreateArcBorder("Border_Outer", outerRadius, 0f, 360f, segments, true, borderColor);
            }
            else if (telegraphType == AoETelegraphType.Donut_Inner4_Outer12)
            {
                CreateArcBorder("Border_Outer12m", outerRadius, 0f, 360f, segments, true, borderColor);
                CreateArcBorder("Border_SafeInner4m", innerRadius, 0f, 360f, segments, true, new Color(1f, 0.85f, 0.25f, 0.98f));
            }
            else if (telegraphType == AoETelegraphType.FrontCone_120Deg)
            {
                List<Vector3> pts = new List<Vector3>
                {
                    new Vector3(0f, groundY + 0.02f, 0f)
                };

                for (int i = 0; i <= segments; i++)
                {
                    float t = (float)i / segments;
                    float deg = Mathf.Lerp(-halfAngle, halfAngle, t);
                    float rad = deg * Mathf.Deg2Rad;
                    pts.Add(new Vector3(Mathf.Sin(rad) * outerRadius, groundY + 0.02f, Mathf.Cos(rad) * outerRadius));
                }

                pts.Add(new Vector3(0f, groundY + 0.02f, 0f));
                CreateLineRendererObject("Border_Cone120", pts.ToArray(), false, borderColor, 0.11f);
            }
        }

        private void CreateArcBorder(string objName, float radius, float startDeg, float endDeg, int segments, bool loop, Color color)
        {
            int count = loop ? segments : segments + 1;
            Vector3[] pts = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / segments;
                float rad = Mathf.Lerp(startDeg, endDeg, t) * Mathf.Deg2Rad;
                pts[i] = new Vector3(Mathf.Sin(rad) * radius, groundY + 0.02f, Mathf.Cos(rad) * radius);
            }
            CreateLineRendererObject(objName, pts, loop, color, 0.11f);
        }

        private void CreateLineRendererObject(string objName, Vector3[] points, bool loop, Color color, float width)
        {
            GameObject lineObj = new GameObject(objName);
            lineObj.transform.SetParent(transform, false);
            LineRenderer lr = lineObj.AddComponent<LineRenderer>();
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
            lr.sharedMaterial = CreateOwnedMaterial(Color.white);
            _borderLines.Add(lr);
        }

        private Material CreateOwnedMaterial(Color color)
        {
            var material = CreateTransparentTelegraphMaterial(color);
            _ownedMaterials.Add(material);
            return material;
        }

        private void OnDestroy()
        {
            // Keep ownership independent of child renderers, which may already be destroyed.
            if (_baseMesh != null) Destroy(_baseMesh);
            if (_progressMesh != null) Destroy(_progressMesh);
            foreach (var material in _ownedMaterials)
                if (material != null) Destroy(material);
            _ownedMaterials.Clear();
        }

        public static Material CreateTransparentTelegraphMaterial(Color color)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }
            return new Material(shader)
            {
                name = "Mat_AoETelegraph_Transparent",
                color = color
            };
        }
    }

    /// <summary>
    /// Step 6 신규 기믹:
    /// - ① [4인 산개 (Spread)]: 각 파티원 발밑을 따라다니는 분홍색 원형 장판
    /// - ② [4인 쉐어 (Stack)]: 플레이어를 따라다니는 노란색 원형 쉐어 장판 + 머리 위 노란색 화살표 마크
    /// </summary>
    public class AttachedPartyMechanicMarker : MonoBehaviour
    {
        public Transform followTarget;
        public float radius = 2.6f;
        public bool isStackMarker;
        public bool isTargetMarker;
        private Transform _overheadArrow;
        private Mesh _floorMesh;
        private readonly List<Material> _ownedMaterials = new List<Material>();

        public static AttachedPartyMechanicMarker SpawnSpreadMarker(Transform target, float radius, float duration = 0f)
        {
            GameObject go = new GameObject($"SpreadMarker_{target.name}");
            AttachedPartyMechanicMarker marker = go.AddComponent<AttachedPartyMechanicMarker>();
            marker.Initialize(
                target,
                radius,
                isStack: false,
                fillCol: new Color(1.0f, 0.25f, 0.78f, 0.36f),
                borderCol: new Color(1.0f, 0.40f, 0.88f, 0.98f)
            );
            return marker;
        }

        public static AttachedPartyMechanicMarker SpawnStackMarker(Transform target, float radius, float duration = 0f)
        {
            GameObject go = new GameObject($"StackMarker_{target.name}");
            AttachedPartyMechanicMarker marker = go.AddComponent<AttachedPartyMechanicMarker>();
            marker.Initialize(
                target,
                radius,
                isStack: true,
                fillCol: new Color(1.0f, 0.85f, 0.12f, 0.38f),
                borderCol: new Color(1.0f, 0.92f, 0.20f, 1.0f)
            );
            return marker;
        }

        public static AttachedPartyMechanicMarker SpawnTargetMarker(Transform target, float radius)
        {
            GameObject go = new GameObject($"TargetMarker_{target.name}");
            AttachedPartyMechanicMarker marker = go.AddComponent<AttachedPartyMechanicMarker>();
            marker.isTargetMarker = true;
            marker.Initialize(target, radius, false,
                new Color(1f, .12f, .12f, .3f), new Color(1f, .22f, .18f, 1f));
            return marker;
        }

        private void Initialize(Transform target, float r, bool isStack, Color fillCol, Color borderCol)
        {
            followTarget = target;
            radius = r;
            isStackMarker = isStack;

            if (followTarget != null)
            {
                transform.position = new Vector3(followTarget.position.x, 0.08f, followTarget.position.z);
            }

            if (isTargetMarker)
            {
                _overheadArrow = new GameObject("OverheadTargetMarker").transform;
                _overheadArrow.SetParent(transform, false);
                _overheadArrow.localPosition = new Vector3(0f, 3.2f, 0f);
                var arrow = _overheadArrow.gameObject.AddComponent<LineRenderer>();
                arrow.useWorldSpace = false; arrow.loop = true; arrow.positionCount = 4;
                arrow.SetPositions(new[] { new Vector3(0,.55f,0), new Vector3(.5f,0,0),
                    new Vector3(0,-.55f,0), new Vector3(-.5f,0,0) });
                arrow.startWidth = arrow.endWidth = .16f;
                arrow.startColor = arrow.endColor = new Color(1f,.18f,.12f,1f);
                arrow.sharedMaterial = CreateOwnedMaterial(Color.white);
                var label = new GameObject("TargetLabel").AddComponent<TextMesh>();
                label.transform.SetParent(_overheadArrow, false);
                label.transform.localPosition = new Vector3(0f,.9f,0);
                label.transform.localScale = Vector3.one * .1f;
                label.text = "표적"; label.fontSize = 44; label.fontStyle = FontStyle.Bold;
                label.anchor = TextAnchor.MiddleCenter; label.alignment = TextAlignment.Center;
                label.color = new Color(1f,.23f,.17f,1f);
                return;
            }

            // 바닥 원형 반투명 메쉬
            GameObject meshObj = new GameObject("FloorCircleMesh");
            meshObj.transform.SetParent(transform, false);
            MeshFilter mf = meshObj.AddComponent<MeshFilter>();
            MeshRenderer mr = meshObj.AddComponent<MeshRenderer>();
            _floorMesh = AoETelegraph.BuildSectorOrRingMesh(0f, radius, 0f, 360f, 48, fillCol);
            mf.sharedMesh = _floorMesh;
            mr.sharedMaterial = CreateOwnedMaterial(fillCol);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            // 바닥 원형 외곽 테두리 LineRenderer
            GameObject borderObj = new GameObject("FloorCircleBorder");
            borderObj.transform.SetParent(transform, false);
            LineRenderer lr = borderObj.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = true;
            const int segs = 48;
            lr.positionCount = segs;
            for (int i = 0; i < segs; i++)
            {
                float rad = (float)i / segs * Mathf.PI * 2f;
                lr.SetPosition(i, new Vector3(Mathf.Sin(rad) * radius, 0.02f, Mathf.Cos(rad) * radius));
            }
            lr.startWidth = 0.12f;
            lr.endWidth = 0.12f;
            lr.startColor = borderCol;
            lr.endColor = borderCol;
            lr.sharedMaterial = CreateOwnedMaterial(Color.white);

            // 4인 쉐어(Stack) 마커일 경우: 플레이어 머리 위에 아래를 가리키는 큼직한 노란색 화살표(Chevron) + 안쪽 모임 화살표 생성!
            if (isStack)
            {
                _overheadArrow = new GameObject("OverheadStackArrow").transform;
                _overheadArrow.SetParent(transform, false);
                _overheadArrow.localPosition = new Vector3(0f, 3.2f, 0f);

                LineRenderer arrowLr = _overheadArrow.gameObject.AddComponent<LineRenderer>();
                arrowLr.useWorldSpace = false;
                arrowLr.loop = false;
                arrowLr.positionCount = 5;
                arrowLr.SetPositions(new[]
                {
                    new Vector3(-0.85f, 0.85f, 0f),
                    new Vector3(0f, 0f, 0f),
                    new Vector3(0.85f, 0.85f, 0f),
                    new Vector3(0f, 0f, 0f),
                    new Vector3(0f, 1.35f, 0f)
                });
                arrowLr.startWidth = 0.22f;
                arrowLr.endWidth = 0.22f;
                Color yellowBright = new Color(1f, 0.92f, 0.15f, 1f);
                arrowLr.startColor = yellowBright;
                arrowLr.endColor = yellowBright;
                arrowLr.sharedMaterial = CreateOwnedMaterial(Color.white);

                // 텍스트 안내
                GameObject txtObj = new GameObject("StackLabel");
                txtObj.transform.SetParent(_overheadArrow, false);
                txtObj.transform.localPosition = new Vector3(0f, 1.65f, 0f);
                txtObj.transform.localScale = Vector3.one * 0.10f;
                TextMesh tm = txtObj.AddComponent<TextMesh>();
                tm.text = "▼ 4인 쉐어 (모이세요!) ▼";
                tm.fontSize = 48;
                tm.fontStyle = FontStyle.Bold;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.alignment = TextAlignment.Center;
                tm.color = yellowBright;
            }
        }

        private Material CreateOwnedMaterial(Color color)
        {
            var material = AoETelegraph.CreateTransparentTelegraphMaterial(color);
            _ownedMaterials.Add(material);
            return material;
        }

        private void OnDestroy()
        {
            if (_floorMesh != null) Destroy(_floorMesh);
            // TextMesh font materials are shared assets and are deliberately not owned here.
            foreach (var material in _ownedMaterials)
                if (material != null) Destroy(material);
            _ownedMaterials.Clear();
        }

        private void LateUpdate()
        {
            if (followTarget == null || !CombatRange.LivingPartyMember(followTarget) || StageManager.CombatSuspended)
            {
                Destroy(gameObject);
                return;
            }

            transform.position = new Vector3(followTarget.position.x, 0.08f, followTarget.position.z);

            if (_overheadArrow != null)
            {
                float bounce = Mathf.Sin(Time.time * 10f) * 0.22f;
                if (ViewModeManager.Is2DViewActive)
                {
                    _overheadArrow.localPosition = new Vector3(0f, 2.2f, 1.35f + bounce);
                }
                else
                {
                    _overheadArrow.localPosition = new Vector3(0f, 3.1f + bounce, 0f);
                }

                Camera cam = Camera.main;
                if (cam != null)
                {
                    _overheadArrow.rotation = cam.transform.rotation;
                }
            }
        }
    }
}
