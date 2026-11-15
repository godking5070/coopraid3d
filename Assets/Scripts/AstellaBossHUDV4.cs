using UnityEngine;
using UnityEngine.UI;

namespace RaidRogue
{
    /// <summary>Actual boss-target line for the authored v4 boss HUD; only its cast/idle anchor is state-driven.</summary>
    [DefaultExecutionOrder(10500)]
    public sealed class AstellaBossHUDV4 : MonoBehaviour
    {
        public Text targetText;
        [Tooltip("A child object, not this presenter's GameObject, so visibility can recover after menus.")]
        public GameObject targetRoot;
        public RectTransform targetLine;
        public AstellaOrnateHUD hud;
        public Vector2 castingAnchor = new Vector2(.5f, 1f - 248f / 280f);
        public Vector2 idleAnchor = new Vector2(.5f, 1f - 176f / 280f);
        public bool repositionTargetWhenIdle = true;
        public bool scaleTargetWithComposition = true;
        public Vector2 referenceCompositionSize = new Vector2(1280, 280);
        public Vector2 referenceTargetSize = new Vector2(372, 32);
        public string targetPrefix = "대상 · ";

        private BossController _boss;
        private bool? _lastCasting;
        private float _nextResolve;

        private void Awake() { if (hud == null) hud = GetComponentInParent<AstellaOrnateHUD>(); }
        private void OnEnable() { _lastCasting = null; _nextResolve = 0; }
        private void OnValidate() { _lastCasting = null; }

        private void LateUpdate()
        {
            var stageBoss = StageManager.Instance != null ? StageManager.Instance.boss : null;
            if (stageBoss != null) _boss = stageBoss;
            else if (_boss == null && Time.unscaledTime >= _nextResolve)
            {
                _nextResolve = Time.unscaledTime + .5f;
                _boss = FindFirstObjectByType<BossController>();
            }
            bool editing = LocalHUDLayoutController.IsEditing;
            bool casting = _boss != null && (_boss.IsCasting || _boss.IsGroggy) || editing;
            if (repositionTargetWhenIdle && targetLine != null && _lastCasting != casting)
            {
                Vector2 anchor = casting ? castingAnchor : idleAnchor;
                targetLine.anchorMin = anchor;
                targetLine.anchorMax = anchor;
            }
            _lastCasting = casting;
            if (scaleTargetWithComposition && targetLine != null && targetLine.parent is RectTransform composition)
            {
                var size = new Vector2(referenceTargetSize.x * composition.rect.width / Mathf.Max(1, referenceCompositionSize.x),
                    referenceTargetSize.y * composition.rect.height / Mathf.Max(1, referenceCompositionSize.y));
                if ((targetLine.sizeDelta - size).sqrMagnitude > .01f) targetLine.sizeDelta = size;
            }
            bool visible = editing || RaidNetworkSession.Instance?.MenuOpen != true && !CombatMetricsUI.ResultsOpen;
            visible &= hud == null || hud.bossRoot == null || hud.bossRoot.activeInHierarchy;
            Transform target = _boss != null ? _boss.aggroTarget : null;
            visible &= target != null || editing;
            if (targetRoot != null && targetRoot.activeSelf != visible) targetRoot.SetActive(visible);
            if (targetText == null) return;
            string value = target != null ? TargetName(target) : editing ? "?" : "";
            value = value.Length > 0 ? (targetPrefix ?? "") + value : "";
            if (targetText.text != value) targetText.text = value;
        }

        private static string TargetName(Transform target)
        {
            // Do not replace a still-authoritative aggro reference with a guessed living target.
            var avatar = target.GetComponent<NetworkRaidAvatar>();
            string name = avatar != null && !string.IsNullOrEmpty(avatar.DisplayName) ? avatar.DisplayName :
                target.GetComponent<CharacterActor>()?.Definition?.name;
            if (string.IsNullOrEmpty(name)) name = target.name;
            bool bot = avatar != null ? avatar.IsBot : target.GetComponent<PartyBotAI>() != null;
            return bot && !name.StartsWith("AI ", System.StringComparison.Ordinal) ? "AI " + name : name;
        }
    }
}
