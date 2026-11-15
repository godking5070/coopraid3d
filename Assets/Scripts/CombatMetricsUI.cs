using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RaidRogue
{
    // Uses the existing HUD canvas, uGUI buttons and EventSystem in either camera view.
    public sealed class CombatMetricsUI : MonoBehaviour
    {
        private static CombatMetricsUI _resultsOwner;
        public static bool ResultsOpen => _resultsOwner != null && _resultsOwner.isActiveAndEnabled &&
            _resultsOwner._results != null && _resultsOwner._results.activeInHierarchy;
        // Existing manual click fallbacks must also ignore the frame that closes the overlay.
        private static int _lastModalFrame = -1;
        public static bool BlocksUnderlyingInput => ResultsOpen || _lastModalFrame == Time.frameCount;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetModalState()
        {
            _resultsOwner = null;
            _lastModalFrame = -1;
        }
        private CombatMetricsManager _metrics;
        private StageManager _stage;
        private Canvas _canvas;
        private Font _font;
        private RectTransform _panel;
        public RectTransform Panel=>_panel;
        private GameObject _body;
        private Text _header;
        private Text _foldLabel;
        private readonly List<Text> _rows = new List<Text>();
        private GameObject _results;
        private RectTransform _resultBox;
        private Text _resultTitle;
        private Text _resultTime;
        private Text _resultEvents;
        private readonly List<Text[]> _resultRows = new List<Text[]>();
        private RectTransform _tabs;
        private bool _sortByHealing;
        private bool _collapsed;
        private float _nextRefresh;
        private Button _rewardButton;
        private Button _victoryButton;
        private Button _boundRewardButton, _boundVictoryButton;

        public void BindResultButtons()
        {
            if (_boundRewardButton != null) _boundRewardButton.onClick.RemoveListener(ShowLatestResults);
            if (_boundVictoryButton != null) _boundVictoryButton.onClick.RemoveListener(ShowRunResults);
            _boundRewardButton = _rewardButton;
            _boundVictoryButton = _victoryButton;
            if (_boundRewardButton != null)
            {
                _boundRewardButton.onClick.RemoveListener(ShowLatestResults);
                _boundRewardButton.onClick.AddListener(ShowLatestResults);
            }
            if (_boundVictoryButton != null)
            {
                _boundVictoryButton.onClick.RemoveListener(ShowRunResults);
                _boundVictoryButton.onClick.AddListener(ShowRunResults);
            }
        }

        private void ShowLatestResults() { ShowResults(false); }
        private void ShowRunResults() { ShowResults(true); }

        public void Initialize(CombatMetricsManager metrics, StageManager stage)
        {
            _metrics = metrics;
            _stage = stage;
            EnsureUI();
        }

        private void OnDisable() { CloseResults(); }

        private void OnDestroy()
        {
            CloseResults();
            if (_boundRewardButton != null) _boundRewardButton.onClick.RemoveListener(ShowLatestResults);
            if (_boundVictoryButton != null) _boundVictoryButton.onClick.RemoveListener(ShowRunResults);
            if (_panel != null) Destroy(_panel.gameObject);
            if (_results != null) Destroy(_results);
        }

        private void LateUpdate()
        {
            if (_metrics == null) return;
            if (ResultsOpen) _lastModalFrame = Time.frameCount;
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + 0.25f;
            EnsureUI();
            if (_panel == null) return;
            if (_resultBox != null)
            {
                var available = ((RectTransform)_canvas.transform).rect.size;
                float scale = Mathf.Min(1f, available.x / 1280f, available.y / 630f);
                if (!RaidUIStyle.Authored(_resultBox)) _resultBox.localScale = Vector3.one * scale;
            }
            if (_metrics.Ledger.Active) _metrics.RefreshRoster();
            RefreshMeter();
        }

        private RectTransform _viewParent;
        private void EnsureUI()
        {
            if (_canvas == null)
            {
                _canvas = _stage.stageHeaderText != null ? _stage.stageHeaderText.GetComponentInParent<Canvas>() : FindFirstObjectByType<Canvas>();
                if (_canvas == null) return;
            }
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (RaidUIView.For(this)==null)
            {
                if (RaidUIView.Attach(this,"metrics",_canvas.transform))
                {
                    RaidUIView.Bind(this,"Fold",ToggleFold);
                    RaidUIView.Bind(this,"DPS",()=>{_sortByHealing=false;RefreshMeter();});
                    RaidUIView.Bind(this,"HPS",()=>{_sortByHealing=true;RefreshMeter();});
                    RaidUIView.Bind(this,"Close",CloseResults);
                    BindResultButtons();
                    // An authored meter may start folded; retain its full saved size for expansion.
                    _collapsed = _body != null && !_body.activeSelf;
                    RaidUILayout.Fold(_panel, _collapsed);
                    if (_foldLabel != null) _foldLabel.text = _collapsed ? "펼치기" : "접기";
                    return;
                }
                _viewParent=RaidUIView.Begin(this,"metrics",_canvas.transform);
            }
            if (_panel == null) BuildMeter();
            if (_results == null) BuildResults();
            if (_rewardButton == null && _stage.cardManager != null && _stage.cardManager.cardSelectionModalRoot != null)
            {
                _rewardButton = AuthoredResultButton(nameof(_rewardButton)) ?? ButtonAt(_stage.cardManager.cardSelectionModalRoot.transform, "CombatResults", "전투 결과",
                    new Vector2(0.5f, 0.5f), new Vector2(0, -285), new Vector2(230, 46), ShowLatestResults);
                BindResultButtons();
            }
            if (_victoryButton == null && _stage.restartButton != null)
            {
                _victoryButton = AuthoredResultButton(nameof(_victoryButton)) ?? ButtonAt(_stage.restartButton.transform.parent, "RunResults", "전체 런 전투 결과",
                    new Vector2(0.5f, 0), new Vector2(325, 65), new Vector2(230, 66), ShowRunResults);
                BindResultButtons();
            }
        }

        private Button AuthoredResultButton(string field)
        {
            // Metrics can initialize one frame before RaidUIRoot's deferred restoration.
            // Reuse its external buttons now; a generated duplicate would remain on top
            // without a listener after BindResultButtons switches to the authored button.
            var root = _canvas != null ? _canvas.GetComponentInParent<RaidUIRoot>() : null;
            if (root == null) return null;
            foreach (var binding in root.presenters)
            {
                if (binding.typeName != GetType().AssemblyQualifiedName) continue;
                foreach (var reference in binding.references)
                    if (reference.path == field && reference.target is Button button && button != null) return button;
            }
            return null;
        }

        private void BuildMeter()
        {
            _panel = Box(_viewParent, "CombatMetrics_RightHUD", new Vector2(1, 0.5f),
                new Vector2(-18, 170), new Vector2(380, 440), new Color(0.035f, 0.05f, 0.09f, 0.91f));
            _panel.pivot = new Vector2(1, 1);
            // Keep modal overlays above the meter even if they were created before this component.
            _panel.SetAsFirstSibling();
            _header = Label(_panel, "Title", new Vector2(12, -7), new Vector2(285, 26), 17);
            ButtonAt(_panel, "Fold", "접기", new Vector2(1, 1), new Vector2(-38, -20), new Vector2(62, 28), ToggleFold);
            _foldLabel = _panel.Find("Fold").GetComponentInChildren<Text>();
            _body = Rect(_panel, "Body", new Vector2(0, 1), Vector2.zero, new Vector2(380, 440)).gameObject;
            _body.GetComponent<RectTransform>().pivot = new Vector2(0, 1);
            ButtonAt(_body.transform, "DPS", "DPS", new Vector2(0, 1), new Vector2(48, -51), new Vector2(72, 28), () => { _sortByHealing = false; RefreshMeter(); });
            ButtonAt(_body.transform, "HPS", "HPS", new Vector2(0, 1), new Vector2(128, -51), new Vector2(72, 28), () => { _sortByHealing = true; RefreshMeter(); });
            for (int i = 0; i < 4; i++)
                _rows.Add(Label(_body.transform, "Member" + i, new Vector2(12, -72 - i * 88), new Vector2(356, 86), 14));
        }

        public static bool BlocksPointer(Vector2 screenPosition)
        {
            if (BlocksUnderlyingInput) return true;
            var metrics = CombatMetricsManager.Instance;
            var ui = metrics != null ? metrics.GetComponent<CombatMetricsUI>() : null;
            if (ui == null || !ui.isActiveAndEnabled || ui._panel == null || !ui._panel.gameObject.activeInHierarchy || ui._canvas == null) return false;
            Camera camera = ui._canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : ui._canvas.worldCamera;
            return RectTransformUtility.RectangleContainsScreenPoint(ui._panel, screenPosition, camera);
        }

        private void ToggleFold()
        {
            _collapsed = !_collapsed;
            _body.SetActive(!_collapsed);
            RaidUILayout.Fold(_panel, _collapsed);
            _foldLabel.text = _collapsed ? "펼치기" : "접기";
            RefreshMeter();
        }

        private void RefreshMeter()
        {
            if (_header == null) return;
            var ledger = _metrics.Ledger;
            _header.text = $"S{ledger.Stage} · {ledger.Seconds:0.0}s · {(_sortByHealing ? "HPS" : "DPS")} · {(ledger.Active ? "전투" : "종료")}";
            if (_collapsed) return;
            var data = ledger.Snapshot();
            var sorted = Sorted(data);
            for (int i = 0; i < _rows.Count; i++)
            {
                if (i >= sorted.Count) { _rows[i].text = ""; continue; }
                var m = sorted[i];
                _rows[i].text = $"<color=#{RoleHex(m.Role)}><b>{m.Name}</b></color>\n" +
                    $"DPS {m.Dps(data.Seconds):N1} / 피해 {m.Damage:N0}\n" +
                    $"HPS {m.Hps(data.Seconds):N1} / 유효 회복 {m.Healing:N0}\n" +
                    $"보호막 흡수 {m.ShieldAbsorbed:N0} · 쓰러짐 {m.Deaths}\n" +
                    $"자가 회복(포션) {m.SelfHealing:N0}";
            }
        }

        private List<CombatMemberRecord> Sorted(CombatEncounterResult data)
        {
            var members = new List<CombatMemberRecord>(data.Members);
            members.Sort((a, b) =>
            {
                int order = (_sortByHealing ? b.Healing.CompareTo(a.Healing) : b.Damage.CompareTo(a.Damage));
                return order != 0 ? order : a.Id.CompareTo(b.Id);
            });
            return members;
        }

        private void BuildResults()
        {
            var overlay = Box(_viewParent, "CombatResults_Overlay", Vector2.zero, Vector2.zero, Vector2.zero, new Color(0.01f, 0.02f, 0.04f, 0.94f));
            overlay.anchorMax = Vector2.one;
            overlay.offsetMin = overlay.offsetMax = Vector2.zero;
            _results = overlay.gameObject;
            var box = Box(overlay, "Content", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1240, 590), new Color(0.06f, 0.09f, 0.15f, 1));
            _resultBox = box;
            _resultTitle = Label(box, "Title", new Vector2(28, -22), new Vector2(1180, 42), 28);
            _tabs = Rect(box, "Stages", new Vector2(0, 1), new Vector2(28, -84), new Vector2(1180, 40));
            _tabs.pivot = new Vector2(0, 0.5f);
            _resultTime = Label(box, "Time", new Vector2(28, -116), new Vector2(1180, 32), 20);
            _resultEvents = Label(box, "Events", new Vector2(28, -490), new Vector2(1180, 55), 15);
            string[] headers = { "파티원", "피해량", "DPS", "유효 회복", "HPS", "보호막 흡수", "쓰러짐", "보스 피해", "쫄 피해", "자가회복(포션)" };
            float[] widths = { 200, 120, 85, 120, 85, 120, 65, 115, 115, 145 };
            float x = 20;
            for (int c = 0; c < headers.Length; c++)
            {
                var title = Label(box, "Column" + c, new Vector2(x, -175), new Vector2(widths[c], 32), 17);
                title.text = headers[c];
                RaidUIStyle.Color(title, new Color(0.55f, 0.8f, 1f));
                x += widths[c];
            }
            for (int r = 0; r < 4; r++)
            {
                var cells = new Text[headers.Length];
                x = 20;
                for (int c = 0; c < headers.Length; c++)
                {
                    cells[c] = Label(box, $"Row{r}_{c}", new Vector2(x, -225 - r * 64), new Vector2(widths[c] - 8, 54), 18);
                    x += widths[c];
                }
                _resultRows.Add(cells);
            }
            ButtonAt(box, "Close", "닫기 · 이전 화면으로", new Vector2(0.5f, 0), new Vector2(0, 45), new Vector2(370, 48), CloseResults);
            _results.SetActive(false);
        }

        public void ShowResults(bool runTotal)
        {
            if (!isActiveAndEnabled) return;
            EnsureUI();
            if (_results == null || _metrics.Ledger.Stages.Count == 0) return;
            foreach (Transform child in _tabs) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            var stages = _metrics.Ledger.Stages;
            for (int i = 0; i < stages.Count; i++)
            {
                var captured = stages[i];
                ButtonAt(_tabs, "Stage" + captured.Stage, "STAGE " + captured.Stage,
                    new Vector2(0, 0.5f), new Vector2(70 + i * 155, 0), new Vector2(140, 36), () => DisplayResult(captured));
            }
            ButtonAt(_tabs, "Total", "전체 런", new Vector2(0, 0.5f), new Vector2(70 + stages.Count * 155, 0), new Vector2(140, 36), () => DisplayResult(_metrics.Ledger.RunTotal()));
            DisplayResult(runTotal ? _metrics.Ledger.RunTotal() : stages[stages.Count - 1]);
            OpenResultsOverlay();
        }

        private void OpenResultsOverlay()
        {
            if (!isActiveAndEnabled || _results == null) return;
            if (_resultsOwner != null && _resultsOwner != this) _resultsOwner.CloseResults();
            _resultsOwner = this;
            _lastModalFrame = Time.frameCount;
            _results.SetActive(true);
            RaidUIOrder.Raise(_results.transform);
        }

        public void CloseResults()
        {
            if (_resultsOwner == this)
            {
                _lastModalFrame = Time.frameCount;
                _resultsOwner = null;
            }
            if (_results != null) _results.SetActive(false);
        }

        public void ShowFailedResult()
        {
            if (!isActiveAndEnabled) return;
            EnsureUI();
            var failed = _metrics.Ledger.FailedAttempts;
            if (_results == null || failed.Count == 0) return;
            foreach (Transform child in _tabs) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            DisplayResult(failed[failed.Count - 1]);
            _resultTitle.text = $"STAGE {failed[failed.Count - 1].Stage} · 실패한 시도 (클리어 합산 제외)";
            OpenResultsOverlay();
        }

        private void DisplayResult(CombatEncounterResult data)
        {
            _resultTitle.text = data.Stage == 0 ? "전체 런 · 누적 전투 결과" : $"STAGE {data.Stage} · 전투 결과";
            _resultTime.text = $"전투 시간 {data.Seconds:0.00}초 · 전체 런 재도전 {_metrics.Ledger.RetryCount}회 · 포션/부활 HPS 제외";
            if (_resultEvents != null)
            {
                int first = Mathf.Max(0, data.Events.Count - 3);
                var recent = new List<string>();
                for (int i = first; i < data.Events.Count; i++) recent.Add(data.Events[i]);
                _resultEvents.text = recent.Count == 0 ? "" : "실제 사건 · " + string.Join("  |  ", recent);
            }
            var members = Sorted(data);
            for (int i = 0; i < _resultRows.Count; i++)
            {
                foreach (var cell in _resultRows[i]) cell.text = "";
                if (i >= members.Count) continue;
                var m = members[i];
                string[] values = { m.Name, m.Damage.ToString("N0"), m.Dps(data.Seconds).ToString("N1"), m.Healing.ToString("N0"),
                    m.Hps(data.Seconds).ToString("N1"), m.ShieldAbsorbed.ToString("N0"), m.Deaths.ToString(), m.BossDamage.ToString("N0"), m.AddDamage.ToString("N0"), m.SelfHealing.ToString("N0") };
                for (int c = 0; c < values.Length; c++) _resultRows[i][c].text = values[c];
                _resultRows[i][0].text = $"<color=#{RoleHex(m.Role)}>{m.Name}</color>";
            }
        }

        private static string RoleHex(int role) => role == 1 ? "4499FF" : role == 2 ? "44EE77" : role == 3 ? "FFE044" : "FF992B";

        private static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        private static RectTransform Box(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size, Color color)
        {
            var rect = Rect(parent, name, anchor, position, size);
            rect.gameObject.AddComponent<Image>().color = color;
            return rect;
        }

        private Text Label(Transform parent, string name, Vector2 position, Vector2 size, int fontSize)
        {
            var rect = Rect(parent, name, new Vector2(0, 1), position, size);
            rect.pivot = new Vector2(0, 1);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = _font;
            text.fontSize = fontSize;
            RaidUIStyle.Color(text, Color.white);
            text.supportRichText = true;
            text.raycastTarget = false;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            return text;
        }

        private Button ButtonAt(Transform parent, string name, string caption, Vector2 anchor, Vector2 position, Vector2 size, UnityEngine.Events.UnityAction action)
        {
            var rect = Box(parent, name, anchor, position, size, new Color(0.13f, 0.26f, 0.4f, 1));
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = rect.GetComponent<Image>();
            if (name=="Fold"||name=="DPS"||name=="HPS"||name=="Close") RaidUIView.Bind(this,name,action,button); else button.onClick.AddListener(action);
            var text = Label(rect, "Label", Vector2.zero, Vector2.zero, 17);
            var tr = text.rectTransform;
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.offsetMin = tr.offsetMax = Vector2.zero;
            text.alignment = TextAnchor.MiddleCenter;
            text.text = caption;
            return button;
        }
    }
}
