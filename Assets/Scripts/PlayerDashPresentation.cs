using UnityEngine;
using UnityEngine.UI;

namespace RaidRogue
{
    // Added by PlayerController at runtime, shared by the 2D and 3D views.
    public sealed class PlayerDashPresentation : MonoBehaviour
    {
        private PlayerController _player;
        private TrailRenderer _trail;
        private Material _trailMaterial;
        private GameObject _slot;
        private Image _background;
        private RectTransform _overlay;
        private Text _state, _details, _key;
        private float _nextHotbarSearch;

        public void Initialize(PlayerController player)
        {
            _player = player;
            if (_trail != null) return;
            GameObject trailObject = new GameObject("CommonDash_Trail");
            trailObject.transform.SetParent(transform, false);
            _trail = trailObject.AddComponent<TrailRenderer>();
            _trail.time = 0.16f;
            _trail.emitting = false; // Explicit positions also render a whole dash on a slow frame.
            _trail.minVertexDistance = 0.05f;
            _trail.startWidth = 0.11f;
            _trail.endWidth = 0.025f;
            _trail.numCapVertices = 2;
            _trail.alignment = LineAlignment.View;
            _trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _trail.receiveShadows = false;
            _trail.startColor = new Color(0.7f, 0.92f, 1f, 0.5f);
            _trail.endColor = new Color(0.7f, 0.92f, 1f, 0f);
            _trailMaterial = new Material(Shader.Find("Sprites/Default"));
            _trail.sharedMaterial = _trailMaterial;
        }

        public void BeginDash()
        {
            if (_trail == null) return;
            _trail.Clear();
            _trail.AddPosition(TrailPoint(transform.position));
        }

        public void RecordMovement(Vector3 from, Vector3 to)
        {
            if (_trail != null && (to - from).sqrMagnitude > 0.000001f) _trail.AddPosition(TrailPoint(to));
        }

        public void ClearTrail() { if (_trail != null) _trail.Clear(); }
        private static Vector3 TrailPoint(Vector3 body) => body + Vector3.down * 0.82f;

        private void Update()
        {
            if (_player == null || !RaidCombatAuthority.Local(this)) return;
            if (_slot == null)
            {
                if (Time.unscaledTime < _nextHotbarSearch) return;
                _nextHotbarSearch = Time.unscaledTime + 0.5f;
                SkillHotbarUI hotbar = FindFirstObjectByType<SkillHotbarUI>();
                if (hotbar == null) return;
                CreateSlot(hotbar.transform);
            }

            var actor=_player.GetComponent<CharacterActor>();
            var snapshot=actor?.Snapshot;var selection=snapshot==null?null:System.Array.Find(snapshot.selections,x=>x.slot==CharacterSlot.Movement);
            var skill=selection==null?null:actor.Definition.Skill(selection.skillId);
            _key.text = LocalInputBindings.Label("Dash") + " " + (actor?.Selected(CharacterSlot.Movement)?.name??"대쉬");
            _slot.GetComponent<RaidUIIcon>()?.Show(skill?.id);
            bool ready = _player.CanDash;
            RaidUIStyle.Color(_background, ready ? new Color(0.13f, 0.32f, 0.38f, 0.96f) : new Color(0.12f, 0.14f, 0.18f, 0.96f));
            RaidUIFill.Set(_overlay, _player.DashCooldownFraction);
            _state.text = _player.IsDashing ? "대쉬 중" : _player.DashCooldownRemaining > 0
                ? $"{_player.DashCooldownRemaining:0.0}s" : ready ? "사용 가능" : "사용 불가";
            int maximum = skill != null ? actor.MaximumCharges(skill.id) : 1;
            if(skill!=null&&maximum>1)
            {
                var timing=System.Array.Find(snapshot.skills,x=>x.id==skill.id);
                _state.text=timing.charges+"/"+maximum+(timing.recharge>0?" · "+timing.recharge.ToString("0.0")+"s":"");
                RaidUIFill.Set(_overlay,timing.charges==0&&timing.rechargeTotal>0?Mathf.Clamp01(timing.recharge/timing.rechargeTotal):0);
            }
            RaidUIStyle.Color(_state, ready ? new Color(0.65f, 1f, 0.92f) : Color.white);
            DashSettings settings = CharacterActor.Ensure(_player.gameObject).MovementSettings(_player.dashSettings);
            var charges = _player.CaptureDash();
            if ((skill==null||maximum<=1) && charges.MaximumCharges > 1) _state.text += $" · {charges.Charges}/{charges.MaximumCharges}";
            _details.text = settings != null ? $"{settings.distance:0.#}m / {settings.duration:0.##}초\n"+(settings.backwardFromFacing?"바라보는 방향의 뒤로":"이동 입력 방향") : "설정 없음";
        }

        private void CreateSlot(Transform hotbar)
        {
            if(RaidUIView.Attach(this,"dash",hotbar))return;
            var viewParent=RaidUIView.Begin(this,"dash",hotbar);
            RectTransform rect = Rect("CommonDash_Slot", viewParent);
            rect.anchorMin = rect.anchorMax = new Vector2(1, 0);
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(12, 36);
            rect.sizeDelta = new Vector2(126, 124);
            _slot = rect.gameObject;
            _background = _slot.AddComponent<Image>();
            // It is a read-only keyboard slot, but still blocks clicks from reaching the battlefield.
            _background.raycastTarget = true;
            Outline outline = _slot.AddComponent<Outline>();
            outline.effectColor = new Color(0.45f, 0.7f, 0.8f, 0.75f);
            outline.effectDistance = new Vector2(1, -1);
            _overlay = Rect("CooldownOverlay", rect);
            _overlay.pivot = Vector2.zero;
            Image shade = _overlay.gameObject.AddComponent<Image>();
            shade.color = new Color(0, 0, 0, 0.6f);
            shade.raycastTarget = false;
            _key = Label("Key", rect, "", 18, new Vector2(0, 0.72f), Vector2.one);
            _state = Label("State", rect, "사용 가능", 21, new Vector2(0, 0.36f), new Vector2(1, 0.72f));
            _details = Label("Details", rect, "", 12, Vector2.zero, new Vector2(1, 0.36f));
        }

        private static RectTransform Rect(string name, Transform parent)
        {
            RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        private static Text Label(string name, Transform parent, string value, int size, Vector2 min, Vector2 max)
        {
            RectTransform rect = Rect(name, parent);
            rect.anchorMin = min; rect.anchorMax = max;
            Text text = rect.gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.text = value;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        private void OnDestroy()
        {
            if (_slot != null) Destroy(_slot);
            if (_trailMaterial != null) Destroy(_trailMaterial);
        }
    }
}
