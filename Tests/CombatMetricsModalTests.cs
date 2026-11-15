using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace UnityEngine
{
    public enum RuntimeInitializeLoadType { SubsystemRegistration }
    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    { public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType type) { } }
    public static class Time { public static int frameCount; }
    public class GameObject
    {
        public bool activeSelf, parentActive = true;
        public bool activeInHierarchy => activeSelf && parentActive;
        public Transform transform = new Transform();
        public void SetActive(bool value) { activeSelf = value; }
    }
    public class Transform { public Transform parent; }
    public class RectTransform : Transform { }
    public sealed class Canvas
    {
        public RaidRogue.RaidUIRoot root;
        public Transform transform = new RectTransform();
        public T GetComponentInParent<T>() where T : class => root as T;
    }
    public sealed class Font { }
    public struct Vector2
    {
        public Vector2(float x, float y) { }
        public static Vector2 zero => new Vector2();
    }
    public struct Color { public Color(float r, float g, float b, float a = 1) { } }
    public static class Resources { public static T GetBuiltinResource<T>(string path) where T : new() => new T(); }
}
namespace UnityEngine.Events
{
    public delegate void UnityAction();
}
namespace UnityEngine.UI
{
    public sealed class Button
    {
        public string name;
        public bool destroyed;
        public Transform transform = new Transform();
        public readonly ClickEvent onClick = new ClickEvent();
        public static bool operator ==(Button a, Button b)
        {
            bool missingA = ReferenceEquals(a, null) || a.destroyed, missingB = ReferenceEquals(b, null) || b.destroyed;
            return missingA || missingB ? missingA && missingB : ReferenceEquals(a, b);
        }
        public static bool operator !=(Button a, Button b) => !(a == b);
        public override bool Equals(object value) => ReferenceEquals(this, value);
        public override int GetHashCode() => base.GetHashCode();
    }
    public sealed class ClickEvent
    {
        private readonly List<UnityEngine.Events.UnityAction> _listeners = new List<UnityEngine.Events.UnityAction>();
        public int Count => _listeners.Count;
        public void AddListener(UnityEngine.Events.UnityAction listener) => _listeners.Add(listener);
        public void RemoveListener(UnityEngine.Events.UnityAction listener) => _listeners.RemoveAll(value => value == listener);
        public void Invoke() { foreach (var listener in _listeners.ToArray()) listener(); }
    }
    public sealed class Text
    {
        public Canvas canvas;
        public string text;
        public T GetComponentInParent<T>() where T : class => canvas as T;
    }
}
namespace RaidRogue
{
    public static class RaidUIOrder { public static void Raise(object transform) { } }
    public static class RaidUILayout { public static void Fold(RectTransform panel, bool collapsed) { } }
    public sealed class RaidUIRoot
    {
        public sealed class PresenterBinding
        {
            public string typeName;
            public List<RaidUIView.Reference> references = new List<RaidUIView.Reference>();
        }
        public readonly List<PresenterBinding> presenters = new List<PresenterBinding>();
    }
    public static class RaidUIView
    {
        public sealed class Reference { public string path; public object target; }
        private static readonly HashSet<CombatMetricsUI> Owners = new HashSet<CombatMetricsUI>();
        public static object For(CombatMetricsUI ui) => Owners.Contains(ui) ? ui : null;
        public static bool Attach(CombatMetricsUI ui, string id, Transform parent) { Owners.Add(ui); return true; }
        public static RectTransform Begin(CombatMetricsUI ui, string id, Transform parent) => new RectTransform();
        public static void Bind(CombatMetricsUI ui, string id, UnityEngine.Events.UnityAction callback) { }
    }
    public sealed class RoguelikeCardManager { public GameObject cardSelectionModalRoot = new GameObject(); }
    public sealed class StageManager
    {
        public Text stageHeaderText;
        public RoguelikeCardManager cardManager = new RoguelikeCardManager();
        public Button restartButton = new Button();
    }
    public class CombatMetricsUI
    {
        /* ACTUAL_MODAL_STATE */
        public bool isActiveAndEnabled = true;
        private GameObject _results = new GameObject();
        private Canvas _canvas;
        private Font _font;
        private StageManager _stage;
        private RectTransform _viewParent, _panel = new RectTransform();
        private GameObject _body;
        private Text _foldLabel;
        private bool _collapsed, _sortByHealing;
        private Button _rewardButton, _victoryButton, _boundRewardButton, _boundVictoryButton;
        private int _generatedButtons, _runShows, _latestShows;
        private static T FindFirstObjectByType<T>() where T : class => null;
        private void ToggleFold() { }
        private void RefreshMeter() { }
        private void BuildMeter() { _panel = new RectTransform(); }
        private void BuildResults() { _results = new GameObject(); }
        private void ShowResults(bool runTotal) { if (runTotal) _runShows++; else _latestShows++; OpenResultsOverlay(); }
        private Button ButtonAt(Transform parent, string name, string caption, Vector2 anchor, Vector2 position, Vector2 size, UnityEngine.Events.UnityAction action)
        { _generatedButtons++; var button = new Button { name = name }; button.onClick.AddListener(action); return button; }
        /* ACTUAL_MODAL_METHODS */
        private static int checks;
        private static void Check(bool condition, string description)
        {
            if (!condition) { Console.WriteLine("FAIL: " + description); throw new Exception(description); }
            checks++;
        }
        private static void DeferredButtonBinding()
        {
            ResetModalState();
            var root = new RaidUIRoot(); var canvas = new Canvas { root = root };
            var victory = new Button { name = "Designer renamed total button" };
            var reward = new Button { name = "Designer renamed stage button" };
            var binding = new RaidUIRoot.PresenterBinding { typeName = typeof(CombatMetricsUI).AssemblyQualifiedName };
            binding.references.Add(new RaidUIView.Reference { path = "_victoryButton", target = victory });
            binding.references.Add(new RaidUIView.Reference { path = "_rewardButton", target = reward });
            root.presenters.Add(binding);
            var ui = new CombatMetricsUI { _stage = new StageManager { stageHeaderText = new Text { canvas = canvas } } };

            // Initialize attaches the inner metrics view before the root can restore external buttons.
            ui.EnsureUI();
            Check(ui._victoryButton == null && ui._generatedButtons == 0, "inner-view attachment must not fabricate external buttons");
            // LateUpdate can run before the root's next-frame deferred presenter restoration.
            ui.EnsureUI();
            Check(ui._victoryButton == victory && ui._rewardButton == reward, "pre-deferred setup must reuse the actual authored button instances");
            Check(ui._generatedButtons == 0, "an already-authored victory/reward panel must not receive overlapping generated buttons");
            Check(victory.onClick.Count == 1 && reward.onClick.Count == 1, "each authored result action must have one callback");
            // Match RaidUIRoot.RestorePresenter: restore the fields, then rebind the same instances.
            ui._victoryButton = victory; ui._rewardButton = reward; ui.BindResultButtons();
            Check(victory.onClick.Count == 1 && reward.onClick.Count == 1, "deferred restoration must not duplicate callbacks");
            victory.onClick.Invoke();
            Check(ui._runShows == 1 && ResultsOpen, "one victory-button click must reopen the run overlay once");
            ui.CloseResults(); Time.frameCount++;
            for (int i = 0; i < 5; i++) { ui.EnsureUI(); ui.BindResultButtons(); }
            victory.onClick.Invoke();
            Check(ui._runShows == 2 && ui._generatedButtons == 0 && victory.onClick.Count == 1, "later refreshes must retain one usable authored action");
            ui.CloseResults(); Time.frameCount++; reward.onClick.Invoke();
            Check(ui._latestShows == 1 && ui._runShows == 2, "the stage-results action must remain separate from run totals");
            ui.CloseResults(); Time.frameCount++;

            var fallback = new CombatMetricsUI { _stage = new StageManager { stageHeaderText = new Text { canvas = new Canvas() } } };
            fallback.EnsureUI(); fallback.EnsureUI();
            Check(fallback._generatedButtons == 2, "a HUD without authored external bindings still creates its two fallback actions");
            fallback.BindResultButtons(); fallback._victoryButton.onClick.Invoke();
            Check(fallback._runShows == 1 && fallback._victoryButton.onClick.Count == 1, "fallback action rebinding also invokes once");
            for (int i = 0; i < 5; i++) fallback.EnsureUI();
            Check(fallback._generatedButtons == 2, "fallback refreshes must not recreate result buttons");
            fallback.CloseResults(); Time.frameCount++;

            victory.destroyed = true;
            var replaced = new CombatMetricsUI { _stage = new StageManager { stageHeaderText = new Text { canvas = canvas } } };
            replaced.EnsureUI(); replaced.EnsureUI();
            Check(!ReferenceEquals(replaced._victoryButton, victory) && replaced._generatedButtons == 1,
                "a destroyed authored button must use a live fallback rather than a CLR-nonnull Unity object");
            Check(replaced._rewardButton == reward, "one destroyed reference must not discard a valid authored reward button");
            replaced._victoryButton.onClick.Invoke();
            Check(replaced._runShows == 1, "the replacement for a destroyed authored button must remain usable");
            replaced.CloseResults(); Time.frameCount++;
        }
        public static void Main()
        {
            Time.frameCount = 10;
            ResetModalState();
            Check(!ResultsOpen && !BlocksUnderlyingInput, "A new session must not inherit modal input blocking.");
            var first = new CombatMetricsUI();
            first.OpenResultsOverlay();
            Check(ResultsOpen && BlocksUnderlyingInput, "A visible results overlay must block gameplay.");
            first.CloseResults();
            Check(!ResultsOpen && BlocksUnderlyingInput && !first._results.activeSelf, "Closing must hide the overlay but guard the closing frame.");
            Time.frameCount++;
            Check(!BlocksUnderlyingInput, "Gameplay must unlock on the next frame.");

            first.OpenResultsOverlay();
            first._results.parentActive = false;
            Time.frameCount++;
            Check(!ResultsOpen && !BlocksUnderlyingInput, "A hidden HUD ancestor must not leave an invisible input blocker.");
            first._results.parentActive = true;
            Check(ResultsOpen, "Restoring a visible ancestor must restore modal blocking.");
            first.isActiveAndEnabled = false;
            Check(!ResultsOpen, "A disabled presenter is not an open modal even before its callback runs.");
            first.OnDisable();
            Check(!first._results.activeSelf && BlocksUnderlyingInput, "Disable must close the separate overlay and guard that frame.");
            Time.frameCount++;
            first.OpenResultsOverlay();
            Check(!ResultsOpen && !BlocksUnderlyingInput, "A disabled presenter must not reopen the modal.");

            first.isActiveAndEnabled = true;
            first.OpenResultsOverlay();
            var second = new CombatMetricsUI();
            second.OpenResultsOverlay();
            Check(!first._results.activeSelf && second._results.activeSelf && ResultsOpen, "Opening a new owner must close the previous owner's overlay.");
            first.CloseResults();
            Time.frameCount++;
            Check(ResultsOpen && BlocksUnderlyingInput, "Old owner cleanup must not unlock the new owner's modal.");
            second._results = null;
            Check(!ResultsOpen && !BlocksUnderlyingInput, "A removed overlay must not leave input locked.");
            ResetModalState();
            Check(!ResultsOpen && !BlocksUnderlyingInput, "Subsystem reset must clear owner and frame state.");
            DeferredButtonBinding();
            Console.WriteLine("PASS: combat results modal lifecycle checks " + checks);
        }
    }
}
