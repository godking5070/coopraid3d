using System;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RaidRogue
{
    public sealed class CharacterTrainingUI : MonoBehaviour
    {
        private CharacterTrainingSession _session;
        private RectTransform _root, _content;
        private GameObject _body;
        private Text _message, _live;
        private Font _font;
        private int _tab;
        private TrainingAttack _attack;
        private float _damage = 300, _interval = 4;
        private bool _repeat, _folded;
        private float _nextRefresh;
        private static readonly string[] AttackNames = { "일반 타격", "강한 타격(탱크버스터)", "파티 광역", "카운터 가능 공격", "정화 가능 속박", "일반 넉백 4m", "투사체 직접 타격", "시스템 피해(가드 제외)" };
        private void Start()
        {
            _session = GetComponent<CharacterTrainingSession>();
            var canvas = FindFirstObjectByType<SkillHotbarUI>()?.GetComponentInParent<Canvas>();
            if (canvas == null) return;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _root = Rect(canvas.transform, "Character training panel");
            _root.anchorMin = new Vector2(1, .24f); _root.anchorMax = Vector2.one; _root.pivot = Vector2.one;
            _root.offsetMin = new Vector2(-464, 8); _root.offsetMax = new Vector2(-12, -70);
            _root.gameObject.AddComponent<Image>().color = new Color(.025f, .045f, .07f, .97f);
            var column = _root.gameObject.AddComponent<VerticalLayoutGroup>(); column.padding = new RectOffset(10, 10, 8, 8); column.spacing = 5; column.childControlHeight = true; column.childForceExpandHeight = false;
            var header = Row(_root);
            Button(header, "훈련장 패널 접기 / 펼치기", () => { _folded = !_folded; _body.SetActive(!_folded); _root.anchorMin = new Vector2(1, _folded ? 1 : .24f); _root.offsetMin = new Vector2(-464, _folded ? -115 : 8); });
            Button(header, "메인 메뉴", () => { _session.StopAttacks(); RaidNetworkSession.Instance.Disconnect(); });
            _body = Rect(_root, "Training body").gameObject;
            var bodyLayout = _body.AddComponent<VerticalLayoutGroup>(); bodyLayout.spacing = 6; bodyLayout.childControlHeight = true; bodyLayout.childForceExpandHeight = false;
            _body.AddComponent<LayoutElement>().flexibleHeight = 1;
            var tabs = Row(_body.transform);
            Button(tabs, "구성", () => { _tab = 0; Rebuild(); }); Button(tabs, "시험 / 초기화", () => { _tab = 1; Rebuild(); }); Button(tabs, "실제 수치 / 기록", () => { _tab = 2; Rebuild(); });
            _message = Label(_body.transform, "", 16, 50);
            var scrollRoot = Rect(_body.transform, "Scroll"); scrollRoot.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
            scrollRoot.gameObject.AddComponent<Image>().color = new Color(.05f, .07f, .09f);
            var scroll = scrollRoot.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 28;
            var viewport = Rect(scrollRoot, "Viewport"); Stretch(viewport); viewport.gameObject.AddComponent<RectMask2D>(); scroll.viewport = viewport;
            _content = Rect(viewport, "Contents"); _content.anchorMin = new Vector2(0, 1); _content.anchorMax = Vector2.one; _content.pivot = new Vector2(.5f, 1); _content.sizeDelta = Vector2.zero;
            var layout = _content.gameObject.AddComponent<VerticalLayoutGroup>(); layout.spacing = 6; layout.padding = new RectOffset(6, 6, 6, 12); layout.childControlHeight = true; layout.childForceExpandHeight = false;
            _content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize; scroll.content = _content;
            Rebuild();
        }
        private void Rebuild()
        {
            foreach (Transform child in _content) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            _live = null; _content.anchoredPosition = Vector2.zero;
            if (_tab == 0) BuildChoices(); else if (_tab == 1) BuildStimuli(); else _live = Label(_content, "", 15);
        }
        private void BuildChoices()
        {
            Label(_content, "훈련 전용 · 변경 시 효과/HP/자원/쿨타임 초기화\n정규 런 해금과 보상 기록에는 반영되지 않습니다.", 14);
            for (int i = 0; i < _session.Jobs.Length; i += 2)
            {
                var row = Row(_content);
                foreach (var job in _session.Jobs.Skip(i).Take(2)) Button(row, (_session.Actor.Definition == job ? "● " : "") + job.name, () =>
                {
                    _session.SetBuild(job, job.routes[0].id, CharacterTrainingSession.FirstChoice(job, CharacterSlot.Active), CharacterTrainingSession.FirstChoice(job, CharacterSlot.OffGcd2), CharacterTrainingSession.FirstChoice(job, CharacterSlot.Ultimate), 0); Rebuild();
                });
            }
            var actor = _session.Actor; var d = actor.Definition;
            Label(_content, d.description, 14);
            Label(_content, "고유 제약: " + (CharacterRosterData.Find(d.id)?.disadvantage ?? "중갑의 무게 · 이동 속도 90% / 공통 이동기 거리 70%"), 14);
            Label(_content, "기원 루트 · 3택 1", 18);
            for (int i = 0; i < d.routes.Length; i++)
            {
                var route = d.routes[i]; var skill = d.Skill(route.identitySkillId);
                Button(_content, (actor.RouteId == route.id ? "● " : "") + route.name, () => { Apply(route: route.id); });
                string passive = d.id == AegisPaladinData.JobId ? AegisRouteData.PassiveSummary(route.id) : CharacterRosterData.Find(d.id).passives[i];
                Label(_content, passive + "\n" + Explain(skill), 14);
            }
            foreach (var slot in new[] { CharacterSlot.Active, CharacterSlot.OffGcd2, CharacterSlot.Ultimate })
            {
                Label(_content, "[" + LocalInputBindings.Label(CharacterSlots.Binding(slot)) + "] " + CharacterSlots.Name(slot), 18);
                foreach (var id in d.unlocks.First(x => x.slot == slot).choices)
                {
                    var skill = d.Skill(id); bool executable = actor.HasExecutableHandler(skill);
                    var button = Button(_content, (actor.Selected(slot).id == id ? "● " : "") + skill.name + (executable ? "" : " · 실행 불가"), () => Apply(slot: slot, skill: id));
                    button.interactable = executable;
                    Label(_content, Explain(skill), 14);
                }
            }
            Label(_content, "진화 카드 · 훈련 전용", 18);
            Button(_content, "카드 없음", () => Apply(evolution: 0));
            int index = CharacterEvolutionCards.JobIndex(d.id) * 2;
            for (int i = 0; i < 2; i++)
            {
                int evolution = i + 1;
                Button(_content, (_session.Evolution == evolution ? "● " : "") + CharacterEvolutionCards.Names[index + i], () => Apply(evolution: evolution));
                Label(_content, CharacterEvolutionCards.Descriptions[index + i], 14);
            }
        }
        private void Apply(string route = null, CharacterSlot slot = 0, string skill = null, int evolution = -1)
        {
            var actor = _session.Actor;
            _session.SetBuild(actor.Definition, route ?? actor.RouteId,
                slot == CharacterSlot.Active ? skill : actor.Selected(CharacterSlot.Active).id,
                slot == CharacterSlot.OffGcd2 ? skill : actor.Selected(CharacterSlot.OffGcd2).id,
                slot == CharacterSlot.Ultimate ? skill : actor.Selected(CharacterSlot.Ultimate).id,
                evolution < 0 ? _session.Evolution : evolution); Rebuild();
        }
        private string Explain(CharacterSkillDefinition skill)
        {
            var actor = _session.Actor;
            string cost = string.Join(" / ", skill.costs.Select(c => actor.Definition.resources.First(r => r.id == c.resourceId).name + " " + c.amount));
            var rosterSkill = CharacterRosterData.Find(actor.Definition.id)?.Ability(CharacterRosterData.Key(skill.id));
            return skill.description + "\n" + $"쿨 {skill.cooldown:0.#}s · 사거리 {actor.EffectiveRange(skill):0.#}m · {skill.maximumCharges}충전" +
                (skill.usesGcd ? $" · GCD {skill.gcd:0.#}s" : " · oGCD") + (cost.Length > 0 ? " · 비용 " + cost : "") +
                (rosterSkill != null ? $"\n기본 위력 {rosterSkill.power:0.#} · 무력화 {rosterSkill.stagger:0.#} · 범위 {rosterSkill.radius:0.#}m · 지속 {rosterSkill.duration:0.#}s" + (rosterSkill.hits > 1 ? $" · {rosterSkill.hits}회 분할 적중" : "") : "");
        }
        private void BuildStimuli()
        {
            Label(_content, "공격 대상: 선택한 살아 있는 아군 / 아군 선택이 없으면 자신\n광역: 파티 전원. 카운터는 표적 정면에서 공격하세요.", 14);
            for (int i = 0; i < AttackNames.Length; i++)
            { var kind = (TrainingAttack)i; Button(_content, (_attack == kind ? "● " : "") + AttackNames[i], () => { _attack = kind; _damage = kind == TrainingAttack.Heavy ? 1200 : 300; _session.StopAttacks(); Rebuild(); }); }
            Field("피해량 (1~10000)", _damage, 1, 10000, value => _damage = value);
            Field("반복 간격 (2~30초)", _interval, 2, 30, value => _interval = value);
            Button(_content, _repeat ? "반복: 켜짐" : "반복: 꺼짐 (1회)", () => { _repeat = !_repeat; Rebuild(); });
            var attackRow = Row(_content);
            Button(attackRow, "1.5초 예고 후 실행", () => _session.BeginAttack(_attack, _damage, _interval, _repeat));
            Button(attackRow, "■ 공격 정지", _session.StopAttacks);
            Label(_content, "표적 / 아군 배치", 18);
            Button(_content, _session.MultiTarget ? "표적: 3개 → 1개" : "표적: 1개 → 3개", () => { _session.SetMultipleTargets(!_session.MultiTarget); Rebuild(); });
            Button(_content, _session.MovingTargets ? "표적 이동 정지" : "표적 느린 이동 (±2m)", () => { _session.MovingTargets = !_session.MovingTargets; Rebuild(); });
            Button(_content, "표적·파티 위치 및 방향 초기화", _session.ResetPositions);
            Button(_content, _session.FarAllies ? "아군 가까이 배치" : "아군 멀리 배치", () => { _session.SetFarAllies(!_session.FarAllies); Rebuild(); });
            var hp = Row(_content);
            Button(hp, "아군 HP 25%", () => _session.SetAllyHealth(.25f, false, false)); Button(hp, "아군 HP 75%", () => _session.SetAllyHealth(.75f, false, false));
            Button(_content, "아군 보호막 제거 / HP 100%", () => _session.SetAllyHealth(1, true, false));
            Button(_content, "아군 HP 50% + 정화 가능 속박", () => _session.SetAllyHealth(.5f, false, true));
            Label(_content, "빠른 복원 · 보상/회복 미터에 미집계", 18);
            Button(_content, "HP·자원 복원 (사망자는 시험용 복원)", _session.RestoreHealthAndResources);
            Button(_content, "GCD·쿨타임·이동기 충전 초기화", _session.RefreshCooldowns);
            Button(_content, "전투 기록만 초기화 (진행 중 효과 유지)", _session.ResetRecords);
            Button(_content, "전체 훈련 초기화 (현재 구성 유지)", _session.ResetAll);
        }
        private void Update()
        {
            if (_root == null || !CharacterTrainingSession.Active) return;
            _root.gameObject.SetActive(RaidNetworkSession.Instance?.MenuOpen != true);
            if (Time.unscaledTime < _nextRefresh) return; _nextRefresh = Time.unscaledTime + .2f;
            _message.text = _session.Message + (_session.AttackClock.Telegraphing ? $"\n남은 예고 {Math.Max(0, _session.AttackClock.Due - Time.time):0.0}s" : "");
            if (_live == null) return;
            var actor = _session.Actor; var d = actor.Definition; var pc = _session.Player; var state = actor.Snapshot;
            var kit = actor.GetComponent<CharacterKit>(); var aegis = actor.GetComponent<AegisPaladin>();
            var b = new StringBuilder();
            b.AppendLine(d.name + " / " + d.routes.First(r => r.id == actor.RouteId).name);
            b.AppendLine(d.description);
            b.AppendLine("고유 제약: " + (CharacterRosterData.Find(d.id)?.disadvantage ?? "중갑의 무게 · 이동 속도 90% / 공통 이동기 거리 70%"));
            b.AppendLine($"HP 기본 {d.baseHp:0} → 최종 {pc.maxHp:0} / 현재 {pc.currentHp:0}");
            b.AppendLine($"방어 기본 {d.physicalDefense:0} / {d.magicDefense:0} → 최종 {(kit?.Active == true ? kit.Defense : aegis?.PhysicalDefense ?? 0):0} / {(kit?.Active == true ? kit.Defense : aegis?.MagicDefense ?? 0):0}");
            b.AppendLine($"이동 기본 {d.moveSpeed:0.##} → 최종 {pc.NetworkMoveSpeed:0.##}m/s · 보호막 {pc.currentShield:0}");
            var dash = actor.MovementSettings(pc.dashSettings); b.AppendLine($"이동기 {dash.distance:0.##}m · 최대 {dash.maximumCharges}충전");
            b.AppendLine($"콤보 A {state.combo1}/3 ({state.comboTime1:0.0}s) · B {state.combo2}/3 ({state.comboTime2:0.0}s)\n공유 GCD {state.gcd:0.00}s · 시전 {state.castRemaining:0.00}s");
            b.AppendLine(_session.Evolution == 0 ? "진화 보정 없음" : CharacterEvolutionCards.Descriptions[CharacterEvolutionCards.JobIndex(d.id) * 2 + _session.Evolution - 1]);
            if (kit?.Active == true) b.AppendLine(kit.Job.passives[kit.Route - 1] + "\n" + kit.StatusText());
            if (aegis?.IsActive == true) b.AppendLine(AegisRouteData.PassiveSummary(actor.RouteId) + $"\n남은 지연 피해 {aegis.DamageState.Pending:0.##}");
            foreach (var resource in d.resources)
            {
                _session.ResourceChanges.TryGetValue(resource.id, out var change);
                b.AppendLine($"{resource.name}: {actor.Resource(resource.id):0.#}/{actor.ResourceMaximum(resource.id):0.#} (획득 +{change.x:0.#} / 소모 -{change.y:0.#})");
            }
            var result = CombatMetricsManager.Instance?.Ledger.Snapshot();
            if (result != null)
            {
                b.AppendLine($"\n기록 {result.Seconds:0.0}s · 피해 {result.Members.Sum(m => m.Damage):0.##}\n유효 회복 {result.Members.Sum(m => m.Healing):0.##} / 포션 {result.Members.Sum(m => m.SelfHealing):0.##}\n초과 회복 {_session.Overheal:0.##} · 보호막 흡수 {result.Members.Sum(m => m.ShieldAbsorbed):0.##}\n무력화 누계 {_session.Stagger:0.##} · 카운터 성공 {_session.Counters}회");
                b.AppendLine("상세 개인 피해/HPS는 기존 전투 미터 사용");
            }
            foreach (var slot in CharacterSlots.All)
            {
                var skill = actor.Selected(slot); if (skill == null) continue;
                var cooldown = state.skills.First(s => s.id == skill.id);
                b.AppendLine($"\n[{LocalInputBindings.Label(CharacterSlots.Binding(slot))}] {skill.name}\n사거리 {actor.EffectiveRange(skill):0.#}m · 충전 {cooldown.charges}/{skill.maximumCharges} · 재사용 {cooldown.recharge:0.0}s" + (actor.HasExecutableHandler(skill) ? "" : " · 실행기 없음"));
            }
            foreach (var hit in (CombatMetricsManager.Instance?.RecentDamage ?? Array.Empty<CombatMetricsManager.DamageReceipt>()).Reverse().Take(8)) b.AppendLine($"{hit.OriginalSkill} [{hit.Kind}] {hit.Actual:0.##}");
            _live.text = b.ToString();
        }
        private void Field(string title, float value, float min, float max, Action<float> changed)
        {
            var row = Row(_content); Label(row, title, 15, 36);
            var rect = Rect(row, "Value"); rect.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            rect.gameObject.AddComponent<Image>().color = new Color(.15f, .18f, .23f);
            var field = rect.gameObject.AddComponent<InputField>();
            var text = Label(rect, "", 16); Stretch(text.rectTransform); text.supportRichText = false;
            field.textComponent = text; field.contentType = InputField.ContentType.DecimalNumber; field.characterLimit = 8;
            field.text = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            field.onEndEdit.AddListener(raw => { if (float.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsed) && CharacterDefinitionValidation.Finite(parsed)) changed(Mathf.Clamp(parsed, min, max)); LocalGameplayInput.SuppressThisFrame(); });
        }
        private static RectTransform Rect(Transform parent, string name)
        { var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); r.SetParent(parent, false); return r; }
        private static void Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }
        private Transform Row(Transform parent)
        { var row = Rect(parent, "Row"); var l = row.gameObject.AddComponent<HorizontalLayoutGroup>(); l.spacing = 4; l.childControlHeight = l.childControlWidth = true; l.childForceExpandWidth = true; row.gameObject.AddComponent<LayoutElement>().preferredHeight = 38; return row; }
        private Text Label(Transform parent, string value, int size, float height = -1)
        {
            var r = Rect(parent, "Text"); var t = r.gameObject.AddComponent<Text>(); t.font = _font; t.fontSize = size; t.color = Color.white; t.text = value; t.raycastTarget = false; t.supportRichText = false; t.alignment = TextAnchor.MiddleLeft;
            if (height > 0) r.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
            return t;
        }
        private Button Button(Transform parent, string text, UnityEngine.Events.UnityAction action)
        {
            var r = Rect(parent, text); r.gameObject.AddComponent<LayoutElement>().minHeight = 38;
            var image = r.gameObject.AddComponent<Image>(); image.color = new Color(.12f, .25f, .32f);
            var button = r.gameObject.AddComponent<Button>(); button.targetGraphic = image; button.navigation = new Navigation { mode = Navigation.Mode.None };
            var label = Label(r, text, 15); label.alignment = TextAnchor.MiddleCenter; Stretch(label.rectTransform); label.rectTransform.offsetMin = new Vector2(4, 2); label.rectTransform.offsetMax = new Vector2(-4, -2);
            button.onClick.AddListener(() => { LocalGameplayInput.SuppressThisFrame(); LocalInputBindings.QuarantineHeld(); EventSystem.current?.SetSelectedGameObject(null); action(); }); return button;
        }
        private void OnDestroy() { if (_root != null) Destroy(_root.gameObject); }
    }
}
