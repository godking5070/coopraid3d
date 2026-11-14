using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace RaidRogue
{
    /// <summary>
    /// Data-only adapter for the twelve authored job instruments. The manifest supplies
    /// artwork geometry; current actor resources supply all values and capacities.
    /// </summary>
    [DefaultExecutionOrder(10100)]
    public sealed class AstellaClassJobHUD : MonoBehaviour
    {
        public enum FillDirection { BottomToTop, TopToBottom, LeftToRight, RightToLeft }

        [Serializable]
        public sealed class View
        {
            public string jobId;
            public GameObject root;
            [Tooltip("Authored RectMask2D rectangle at the manifest's fillBounds. Only its reveal extent changes.")]
            public RectTransform fillMask;
            [Tooltip("Full-canvas fill artwork, directly parented under fillMask. It remains stationary and never stretches.")]
            public RectTransform fillArtwork;
            public FillDirection fillDirection = FillDirection.BottomToTop;
            [Tooltip("Whole active tokens in manifest order. Full-canvas token layers must already be placed at origin.")]
            public GameObject[] tokens = Array.Empty<GameObject>();
            public GameObject[] emptyTokens = Array.Empty<GameObject>();
            public Text valueText, labelText;
            public bool showMaximum = true;

            [NonSerialized] internal bool geometryCaptured;
            [NonSerialized] internal Vector2 maskSize, maskSizeDelta, maskPosition, artworkPosition, artworkSizeDelta;
        }

        public AstellaOrnateHUD hud;
        public View[] views = Array.Empty<View>();

        private CharacterActor _actor;
        private NineKeyDamageKit _damage;
        private float _nextResolve;

        private void OnEnable() { _nextResolve = 0; }
        private void OnDisable()
        {
            foreach (var view in views ?? Array.Empty<View>()) RestoreGeometry(view);
        }

        private void Resolve()
        {
            if (Time.unscaledTime < _nextResolve) return;
            _nextResolve = Time.unscaledTime + .5f;
            if (hud == null) hud = GetComponentInParent<AstellaOrnateHUD>();
            CharacterActor actor = null;
            if (RaidNetworkSession.Multiplayer && RaidNetworkSession.Instance != null)
                foreach (var avatar in RaidNetworkSession.Instance.Avatars.Values)
                    if (avatar != null && avatar.IsLocal) { actor = avatar.GetComponent<CharacterActor>(); break; }
            if (actor == null && hud?.hotbarSource?.combatController != null)
                actor = hud.hotbarSource.combatController.GetComponent<CharacterActor>();
            if (actor == null)
            {
                var player = FindFirstObjectByType<PlayerController>();
                if (player != null) actor = player.GetComponent<CharacterActor>();
            }
            _actor = actor;
            _damage = actor != null ? actor.GetComponent<NineKeyDamageKit>() : null;
        }

        private void LateUpdate()
        {
            Resolve();
            var definition = _actor?.Definition;
            JobResourceDefinition resource = null;
            if (definition != null)
            {
                var tank = NineKeyTankKit.Supports(definition) ? CharacterRosterData.Find(definition.id) : null;
                foreach (var candidate in definition.resources ?? Array.Empty<JobResourceDefinition>())
                {
                    if (candidate == null || string.IsNullOrEmpty(candidate.id) ||
                        candidate.id.EndsWith(".ultimate", StringComparison.Ordinal)) continue;
                    // Dragon Breaker's heat exists only on its third route. The earlier ammo resource stays primary.
                    if (tank?.index == 2 && candidate.id == NineKeyTankData.Heat(tank) &&
                        (definition.routes.Length < 3 || _actor.RouteId != definition.routes[2].id)) continue;
                    if (_actor.ResourceMaximum(candidate.id) <= 0) continue;
                    resource = candidate;
                    break;
                }
            }
            float current = 0, maximum = 0;
            if (resource != null)
            {
                current = _actor.Resource(resource.id);
                maximum = _actor.ResourceMaximum(resource.id);
                if (_damage != null && _damage.Active)
                {
                    // Includes route-specific display units and Gunslinger's two-hand ammo capacity.
                    current = _damage.DisplayResource(resource.id, current);
                    maximum = _damage.DisplayMaximum(resource.id, maximum);
                }
            }
            bool available = resource != null && maximum > 0;
            foreach (var view in views ?? Array.Empty<View>())
            {
                if (view?.root == null) continue;
                bool selected = available && definition != null && view.jobId == definition.id;
                SetVisible(view.root, selected);
                if (!selected) continue;
                UpdateView(view, resource, current, maximum);
            }
        }

        private static void UpdateView(View view, JobResourceDefinition resource, float current, float maximum)
        {
            float ratio = float.IsNaN(current) || float.IsNaN(maximum) || maximum <= 0 ? 0 : Mathf.Clamp01(current / maximum);
            Reveal(view, ratio);
            int count = Math.Max(view.tokens?.Length ?? 0, view.emptyTokens?.Length ?? 0);
            int capacity = SafeCount(maximum), active = SafeCount(current);
            for (int i = 0; i < count; i++)
            {
                bool present = i < capacity;
                if (view.tokens != null && i < view.tokens.Length) SetVisible(view.tokens[i], present && i < active);
                if (view.emptyTokens != null && i < view.emptyTokens.Length) SetVisible(view.emptyTokens[i], present && i >= active);
            }
            // Never report an artwork sample maximum as game data. When a route has more
            // resources than authored tokens, keep the complete actual count in the label.
            bool includeMaximum = view.showMaximum || count > 0 && maximum > count;
            string format = resource.integer ? "0" : "0.#";
            string value = current.ToString(format, CultureInfo.InvariantCulture);
            if (includeMaximum) value += " / " + maximum.ToString("0.#", CultureInfo.InvariantCulture);
            SetText(view.valueText, value);
            SetText(view.labelText, resource.name ?? "");
        }

        private static void Reveal(View view, float ratio)
        {
            if (view.fillMask == null) return;
            if (!view.geometryCaptured)
            {
                view.maskSize = view.fillMask.rect.size;
                view.maskSizeDelta = view.fillMask.sizeDelta;
                view.maskPosition = view.fillMask.anchoredPosition;
                view.artworkPosition = view.fillArtwork != null ? view.fillArtwork.anchoredPosition : Vector2.zero;
                view.artworkSizeDelta = view.fillArtwork != null ? view.fillArtwork.sizeDelta : Vector2.zero;
                view.geometryCaptured = true;
            }
            Vector2 size = view.maskSize;
            Vector2 position = view.maskPosition;
            switch (view.fillDirection)
            {
                case FillDirection.BottomToTop:
                    size.y *= ratio; position.y += (size.y - view.maskSize.y) * view.fillMask.pivot.y; break;
                case FillDirection.TopToBottom:
                    size.y *= ratio; position.y += (size.y - view.maskSize.y) * (view.fillMask.pivot.y - 1); break;
                case FillDirection.LeftToRight:
                    size.x *= ratio; position.x += (size.x - view.maskSize.x) * view.fillMask.pivot.x; break;
                case FillDirection.RightToLeft:
                    size.x *= ratio; position.x += (size.x - view.maskSize.x) * (view.fillMask.pivot.x - 1); break;
            }
            Vector2 delta = size - view.maskSize;
            view.fillMask.sizeDelta = view.maskSizeDelta + delta;
            view.fillMask.anchoredPosition = position;
            if (view.fillArtwork != null && view.fillArtwork.parent == view.fillMask)
            {
                var art = view.fillArtwork;
                Vector2 reference = new Vector2(Mathf.Lerp(art.anchorMin.x, art.anchorMax.x, art.pivot.x),
                    Mathf.Lerp(art.anchorMin.y, art.anchorMax.y, art.pivot.y));
                art.anchoredPosition = view.artworkPosition - (position - view.maskPosition) -
                    Vector2.Scale(delta, reference - view.fillMask.pivot);
                art.sizeDelta = view.artworkSizeDelta - Vector2.Scale(delta, art.anchorMax - art.anchorMin);
            }
        }

        private static void RestoreGeometry(View view)
        {
            if (view == null || !view.geometryCaptured) return;
            if (view.fillMask != null)
            {
                view.fillMask.sizeDelta = view.maskSizeDelta;
                view.fillMask.anchoredPosition = view.maskPosition;
            }
            if (view.fillArtwork != null)
            {
                view.fillArtwork.anchoredPosition = view.artworkPosition;
                view.fillArtwork.sizeDelta = view.artworkSizeDelta;
            }
            view.geometryCaptured = false;
        }

        private static int SafeCount(float value)
        {
            if (float.IsNaN(value) || value <= 0) return 0;
            if (float.IsPositiveInfinity(value) || value >= int.MaxValue) return int.MaxValue;
            return Mathf.FloorToInt(value + .0001f);
        }
        private static void SetText(Text text, string value) { if (text != null && text.text != value) text.text = value; }
        private static void SetVisible(GameObject target, bool value) { if (target != null && target.activeSelf != value) target.SetActive(value); }
    }
}
