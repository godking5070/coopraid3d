using UnityEngine;
using System.Collections.Generic;
using System.Globalization;

namespace RaidRogue
{
    /// <summary>
    /// 보스 머리 위에 떠오르는 3D 데미지/판정 텍스트 팝업 컴포넌트.
    /// 카메라를 향해 빌보드 회전하며 위로 부드럽게 떠올랐다가 사라집니다.
    /// </summary>
    public class FloatingTextPopup : MonoBehaviour
    {
        public float floatSpeed = 1.8f;
        public float lifetime = 1.25f;

        private TextMesh _mainText;
        private TextMesh _shadowText;
        private MeshRenderer _mainRenderer, _shadowRenderer;
        private Color _baseColor;
        private float _elapsed;
        private Vector3 _initialScale;
        private static int _spawnFrame = -1, _damageSpawnCount, _noticeSpawnCount, _importantSpawnCount;
        private static readonly List<FloatingTextPopup> DamagePopups = new List<FloatingTextPopup>();
        private static readonly List<FloatingTextPopup> NoticePopups = new List<FloatingTextPopup>();
        private Vector3 _damageOrigin;
        private int _damageLane = -1;
        private int _noticeLane = -1;
        private string _noticeMessage;
        private bool _important;

        private void OnDisable() { DamagePopups.Remove(this); NoticePopups.Remove(this); }

        public static FloatingTextPopup SpawnPartyNotice(Vector3 position, string message, Color color)
        {
            // A party-wide effect can be applied by multiple owners in the same pulse.
            // Coalesce presentation only; every caller still applies its gameplay effect.
            foreach (var notice in NoticePopups)
                if (notice != null && notice._noticeMessage == message && notice._elapsed < .35f) return null;
            return Spawn(position, message, color, true);
        }

        public static FloatingTextPopup SpawnDamage(Vector3 position, float damage, string detail, Color color, bool highlight)
        {
            if (damage <= 0) return null;
            bool compact = RaidUIPresentation.Settings?.theme?.compactDamageText == true;
            return Spawn(position, compact ? damage.ToString("0.#", CultureInfo.InvariantCulture) : detail, color, highlight);
        }

        public static FloatingTextPopup Spawn(Vector3 worldPosition, string message, Color color, bool isCrit, bool important = false)
        {
            bool numeric = float.TryParse(message, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount) && amount >= 0;
            if (_spawnFrame != Time.frameCount)
            { _spawnFrame = Time.frameCount; _damageSpawnCount = _noticeSpawnCount = _importantSpawnCount = 0; }
            // Damage bursts cannot consume the separate text-notice allowance.
            if (numeric)
            {
                if (DamagePopups.Count >= 48 || _damageSpawnCount >= 12) return null;
                int nearby = 0;
                foreach (var hit in DamagePopups)
                    if (hit != null && (hit._damageOrigin - worldPosition).sqrMagnitude < 16f) nearby++;
                // Every nearby hit needs a distinct lane; do not stack overflow on lane 11.
                if (nearby >= 12) return null;
                _damageSpawnCount++;
            }
            else
            {
                int matchingPriority = 0;
                foreach (var notice in NoticePopups)
                {
                    if (notice == null || notice._important != important) continue;
                    matchingPriority++;
                    if (notice._noticeMessage == message && notice._elapsed < .35f &&
                        (notice._damageOrigin - worldPosition).sqrMagnitude < 16f) return null;
                }
                // Two top lanes are reserved for explicit mechanic notices.
                if (important)
                {
                    if (matchingPriority >= 2 || _importantSpawnCount >= 2) return null;
                    _importantSpawnCount++;
                }
                else
                {
                    if (matchingPriority >= 6 || _noticeSpawnCount >= 3) return null;
                    _noticeSpawnCount++;
                }
            }
            if (RaidCombatAuthority.Host) RaidNetworkSession.Instance.BroadcastEffect(worldPosition, worldPosition, color, message, isCrit, 0, important);
            GameObject root = new GameObject($"Popup_{message}");
            // 연속 타격 시 겹치지 않도록 약간의 수평 랜덤 오프셋 부여
            bool is2D = ViewModeManager.Is2DViewActive;
            Vector3 jitter = new Vector3(
                Random.Range(-0.45f, 0.45f),
                Random.Range(-0.1f, 0.25f),
                Random.Range(-0.2f, 0.2f) + (is2D ? 1.1f : 0f)
            );
            root.transform.position = worldPosition + jitter;

            FloatingTextPopup popup = root.AddComponent<FloatingTextPopup>();
            popup._important = important;
            popup.Initialize(message, color, isCrit);
            // Numeric messages also arrive through the existing replicated text effect.
            // Reserve separate camera-facing lanes for nearby hits instead of random overlap.
            if (numeric)
                popup.PlaceDamage(worldPosition);
            else
                popup.PlaceNotice(worldPosition, message);
            return popup;
        }

        private void PlaceNotice(Vector3 origin, string message)
        {
            int used = 0;
            foreach (var notice in NoticePopups) if (notice != null) used |= 1 << notice._noticeLane;
            int lane = _important ? 0 : 2;
            int lastLane = _important ? 1 : 7;
            while (lane < lastLane && (used & (1 << lane)) != 0) lane++;
            _noticeLane = lane; _noticeMessage = message; _damageOrigin = origin;
            NoticePopups.Add(this);
        }

        private void PlaceDamage(Vector3 origin)
        {
            int used = 0;
            foreach (var other in DamagePopups)
                if (other != null && (other._damageOrigin - origin).sqrMagnitude < 16f)
                    used |= 1 << other._damageLane;
            int lane = 0;
            while (lane < 11 && (used & (1 << lane)) != 0) lane++;
            _damageLane = lane; _damageOrigin = origin;
            DamagePopups.Add(this);
            var camera = Camera.main;
            var right = camera != null ? camera.transform.right : Vector3.right;
            var up = camera != null ? camera.transform.up : Vector3.up;
            transform.position = origin + right * ((lane % 3 - 1) * 1.3f) + up * (.35f + lane / 3 * .65f);
            lifetime = .85f;
            floatSpeed = 1.1f;
        }

        private void Initialize(string message, Color color, bool isCrit)
        {
            message = LocalInputBindings.SkillText(message);
            _baseColor = color;
            float scale = isCrit ? 0.16f : 0.125f;
            scale *= RaidUIPresentation.Settings?.theme != null ? RaidUIPresentation.Settings.theme.combatTextScale : 1f;
            _initialScale = Vector3.one * scale;
            transform.localScale = _initialScale * (isCrit ? 1.35f : 1.1f);

            // 그림자 텍스트 (가독성 향상)
            GameObject shadowObj = new GameObject("Shadow");
            shadowObj.transform.SetParent(transform, false);
            shadowObj.transform.localPosition = new Vector3(0.18f, -0.18f, 0.05f);

            _shadowText = shadowObj.AddComponent<TextMesh>();
            _shadowText.text = message;
            _shadowText.fontSize = isCrit ? 72 : 60;
            _shadowText.fontStyle = FontStyle.Bold;
            _shadowText.anchor = TextAnchor.MiddleCenter;
            _shadowText.alignment = TextAlignment.Center;
            _shadowText.color = new Color(0f, 0f, 0f, 0.9f);

            // 메인 텍스트
            _mainText = gameObject.AddComponent<TextMesh>();
            _mainText.text = message;
            _mainText.fontSize = isCrit ? 72 : 60;
            _mainText.fontStyle = FontStyle.Bold;
            _mainText.anchor = TextAnchor.MiddleCenter;
            _mainText.alignment = TextAlignment.Center;
            _mainText.color = color;

            MeshRenderer mainRenderer = GetComponent<MeshRenderer>();
            _mainRenderer = mainRenderer;
            if (mainRenderer != null)
            {
                mainRenderer.sortingOrder = 50;
            }
            MeshRenderer shadowRenderer = shadowObj.GetComponent<MeshRenderer>();
            _shadowRenderer = shadowRenderer;
            if (shadowRenderer != null)
            {
                shadowRenderer.sortingOrder = 49;
            }
        }

        private void LateUpdate()
        {
            _elapsed += Time.deltaTime;

            // 카메라 정면 빌보드 정렬
            Camera cam = Camera.main;
            if (cam != null)
            {
                transform.rotation = cam.transform.rotation;
            }

            // 위로 부유 및 스케일 탄성 복원 (2D 직교 탑다운 뷰에서는 화면 위쪽(+Z)으로도 부유)
            Vector3 floatDir = ViewModeManager.Is2DViewActive
                ? new Vector3(0f, 0.5f, 1.0f)
                : Vector3.up;
            transform.position += floatDir * (floatSpeed * Time.deltaTime);
            transform.localScale = Vector3.Lerp(transform.localScale, _initialScale, Time.deltaTime * 10f);
            if (cam != null) KeepInsideCombatView(cam);

            // 후반부 알파 페이드 아웃
            float normalizedTime = Mathf.Clamp01(_elapsed / lifetime);
            if (normalizedTime > 0.55f)
            {
                float alpha = Mathf.InverseLerp(1f, 0.55f, normalizedTime);
                if (_mainText != null)
                {
                    Color c = _baseColor;
                    c.a = alpha;
                    _mainText.color = c;
                }
                if (_shadowText != null)
                {
                    _shadowText.color = new Color(0f, 0f, 0f, alpha * 0.9f);
                }
            }

            if (_elapsed >= lifetime)
            {
                Destroy(gameObject);
            }
        }

        private void KeepInsideCombatView(Camera camera)
        {
            if (_mainRenderer == null) return;
            var point = camera.WorldToScreenPoint(transform.position);
            bool visible = point.z > camera.nearClipPlane;
            _mainRenderer.enabled = visible;
            if (_shadowRenderer != null) _shadowRenderer.enabled = visible;
            if (!visible) return;

            // Billboard dimensions lie in the camera plane, including in orthographic view.
            // Fit generated world text only; authored HUD transforms remain untouched.
            var bounds = _mainRenderer.localBounds;
            var center = camera.WorldToScreenPoint(transform.TransformPoint(bounds.center));
            var edge = camera.WorldToScreenPoint(transform.TransformPoint(bounds.center + bounds.extents));
            float halfWidth = Mathf.Abs(edge.x - center.x) + 3f;
            float halfHeight = Mathf.Abs(edge.y - center.y) + 3f;
            var viewport = camera.pixelRect;
            float fit = Mathf.Min(1f, viewport.width * .58f / (halfWidth * 2f),
                viewport.height * .028f / (halfHeight * 2f));
            transform.localScale *= fit;
            center = camera.WorldToScreenPoint(transform.TransformPoint(bounds.center));
            halfWidth *= fit; halfHeight *= fit;
            float top = _damageLane >= 0 ? .40f - (_damageLane / 3) * .035f : .72f;
            float x = Mathf.Clamp(center.x, viewport.xMin + viewport.width * .18f + halfWidth,
                viewport.xMin + viewport.width * .82f - halfWidth);
            float y = Mathf.Clamp(center.y, viewport.yMin + viewport.height * .26f + halfHeight,
                viewport.yMin + viewport.height * top - halfHeight);
            if (_noticeLane >= 0) y = viewport.yMin + viewport.height * (.70f - _noticeLane * .04f);
            transform.position += camera.ScreenToWorldPoint(new Vector3(x, y, center.z))
                - camera.ScreenToWorldPoint(center);
        }
    }
}
