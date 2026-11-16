#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Astella.PrismV4;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace RaidRogue.Editor
{
    /// <summary>Explicit passive observer. No startup hooks, gameplay actions, source setters or asset saves.</summary>
    public static class AstellaPrismPlayChecks
    {
        private const double WatchSeconds = 180;
        private const int StableSamples = 3;
        private static bool _watching, _pending;
        private static double _started, _deadline, _nextSample;
        private static int _lastFrame = -1, _samples, _bridgeId;
        private static string _session, _path;
        private static readonly Dictionary<string, int> Faults = new Dictionary<string, int>();
        private static readonly HashSet<string> Reported = new HashSet<string>();

        [Serializable] private sealed class WatchEvent
        { public string kind, utc, session, reason; public int samples; }
        [Serializable] private sealed class Comparison
        { public string key, expected, actual; public float tolerance; public bool skipped, mismatch, stableMismatch; public int consecutiveSamples; }
        [Serializable] private sealed class Gauge
        { public string key; public float expected, actual; public bool visible; }
        [Serializable] private sealed class Slot
        {
            public int sourceIndex, runtimeClickListeners, persistentClickListeners;
            public string slot, key, sourceKey, cooldown, sourceCooldown, frameSprite, frameOverrideSprite, sourceGameObject;
            public bool active, enabled, interactable, effectiveInteractable, eventSelected, pointerInside;
            public float cooldownFill, sourceCooldownFill; public bool disabledOverlay;
        }
        [Serializable] private sealed class Status
        { public string key, path, remaining; public bool activeSelf, activeInHierarchy; }
        [Serializable] private sealed class Party
        {
            public int sourceIndex; public string target, name, hpText, key;
            public bool present, dead, selected, aggro, visible;
            public float health, sourceHealth, shield, sourceShield;
            public Status[] statuses;
        }
        [Serializable] private sealed class Bounds
        { public string key, path; public bool active, insideScreen; public float xMin, yMin, xMax, yMax; }
        [Serializable] private sealed class Sample
        {
            public string kind = "sample", utc, session, phase, timing = "Canvas.willRenderCanvases; mismatches require three consecutive samples", layoutFilePath;
            public int index, frame, renderedFrame, bridgeCount, width, height, stage;
            public float unscaledTime; public double elapsedSeconds;
            public bool applicationFocused, layoutEditing, menuOpen, resultsOpen, encounterActive, wiped, preparing, transitioning;
            public string primaryResourceId, primaryLabel, primaryText, ultimateResourceId, ultimateState, ultimateReason, readinessReason, ultimateText;
            public bool hasPrimary, primaryFinite, ultimateReady, readinessReady, readyGlow, unavailableOverlay;
            public float primaryCurrent, primaryMaximum, ultimateCharge;
            public bool castVisible, groggy; public string castName, castTime; public float castRatio;
            public Gauge[] gauges; public Slot[] slots; public Party[] partyRows; public Bounds[] bounds; public Comparison[] comparisons;
        }

        [MenuItem("Tools/Astella/Prism V4/Watch Live HUD %#F8")]
        public static void WatchLiveHud()
        {
            if (!EditorApplication.isPlaying)
            { Debug.LogWarning("Enter Play Mode before starting the passive Prism HUD watch."); return; }
            StopWatch("restarted");
            _session = Guid.NewGuid().ToString("N"); _started = EditorApplication.timeSinceStartup;
            _deadline = _started + WatchSeconds; _nextSample = _started; _samples = 0; _lastFrame = -1; _bridgeId = 0;
            Faults.Clear(); Reported.Clear();
            string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs", "PrismV4");
            Directory.CreateDirectory(folder); _path = Path.Combine(folder, "live-hud-samples.jsonl");
            _watching = true; _pending = true;
            Append(new WatchEvent { kind = "start", utc = DateTime.UtcNow.ToString("O"), session = _session,
                reason = "Explicit menu watch; 180 seconds, one rendered sample per second, no gameplay mutations." });
            EditorApplication.update -= WatchTick; EditorApplication.update += WatchTick;
            Canvas.willRenderCanvases -= CaptureRenderedFrame; Canvas.willRenderCanvases += CaptureRenderedFrame;
            Debug.Log("PRISM LIVE WATCH STARTED: " + _path + " | 180 seconds. Ctrl+Shift+F8 starts a new session.");
        }

        [MenuItem("Tools/Astella/Prism V4/Stop Live HUD Watch")]
        public static void StopLiveHudWatch() => StopWatch("explicit stop");

        private static void WatchTick()
        {
            if (!_watching) return;
            if (!EditorApplication.isPlaying) { StopWatch("Play Mode ended"); return; }
            if (EditorApplication.timeSinceStartup >= _deadline) { StopWatch("180-second window completed"); return; }
            if (EditorApplication.timeSinceStartup >= _nextSample) _pending = true;
        }
        private static void CaptureRenderedFrame()
        {
            if (!_watching || !_pending || !EditorApplication.isPlaying || EditorApplication.isPaused || Time.frameCount == _lastFrame) return;
            _pending = false; _lastFrame = Time.frameCount; _nextSample = EditorApplication.timeSinceStartup + 1;
            try
            {
                var bridges = Object.FindObjectsByType<AstellaPrismHUDBridge>(FindObjectsSortMode.None).Where(value => value.isActiveAndEnabled).ToArray();
                var bridge = bridges.FirstOrDefault();
                if ((bridge != null ? bridge.GetInstanceID() : 0) != _bridgeId)
                { Faults.Clear(); Reported.Clear(); _bridgeId = bridge != null ? bridge.GetInstanceID() : 0; }
                Append(Capture(bridge, bridges.Length)); _samples++;
            }
            catch (Exception exception)
            { Debug.LogError("Prism passive watch stopped: " + exception); StopWatch("observer exception"); }
        }
        private static void StopWatch(string reason)
        {
            EditorApplication.update -= WatchTick; Canvas.willRenderCanvases -= CaptureRenderedFrame;
            if (!_watching) return;
            _watching = false; _pending = false;
            Append(new WatchEvent { kind = "stop", utc = DateTime.UtcNow.ToString("O"), session = _session, reason = reason, samples = _samples });
            Debug.Log("PRISM LIVE WATCH STOPPED: " + reason + " | samples=" + _samples);
        }

        private static Sample Capture(AstellaPrismHUDBridge bridge, int count)
        {
            var checks = new List<Comparison>(); var gauges = new List<Gauge>(); var bounds = new List<Bounds>();
            var sample = new Sample { utc = DateTime.UtcNow.ToString("O"), session = _session, index = _samples,
                frame = Time.frameCount, renderedFrame = Time.renderedFrameCount, unscaledTime = Time.unscaledTime,
                elapsedSeconds = EditorApplication.timeSinceStartup - _started, bridgeCount = count, width = Screen.width, height = Screen.height,
                applicationFocused = Application.isFocused, layoutEditing = LocalHUDLayoutController.IsEditing,
                menuOpen = RaidNetworkSession.Instance?.MenuOpen == true, resultsOpen = CombatMetricsUI.ResultsOpen,
                stage = StageManager.Instance != null ? StageManager.Instance.currentStage : 0,
                phase = PublicPhase(), encounterActive = StageManager.Instance != null && StageManager.Instance.EncounterActive,
                wiped = StageManager.Instance != null && StageManager.Instance.IsWiped, preparing = StageManager.Instance != null && StageManager.Instance.IsPreparing,
                transitioning = StageManager.Instance != null && StageManager.Instance.IsStageTransitioning,
                layoutFilePath = LocalHUDLayoutController.Instance != null ? LocalHUDLayoutController.Instance.FilePath : "" };
            Compare(checks, "active bridge count", "1", count.ToString(), count == 1, true);
            if (bridge == null) { sample.comparisons = checks.ToArray(); return sample; }
            var hud = bridge.hud; var primary = bridge.jobSource; var ultimate = bridge.ultimateSource; var readiness = bridge.readinessSource;
            bool playerVisible = Active(bridge.playerView); bool centerVisible = Active(bridge.centerView); bool bossVisible = Active(bridge.bossView);
            sample.primaryResourceId = primary != null ? primary.ResourceId : ""; sample.primaryLabel = primary != null ? primary.ResourceLabel : "";
            sample.primaryCurrent = Safe(primary != null ? primary.CurrentValue : 0); sample.primaryMaximum = Safe(primary != null ? primary.Maximum : 0);
            sample.hasPrimary = primary != null && primary.HasResource;
            sample.primaryFinite = primary == null || Finite(primary.CurrentValue) && Finite(primary.Maximum);
            sample.primaryText = TextOf(bridge.playerView?.resourceText);
            sample.ultimateResourceId = ultimate != null ? ultimate.ResourceId : ""; sample.ultimateCharge = Safe(ultimate != null ? ultimate.ChargeRatio : 0);
            sample.ultimateState = ultimate != null ? ultimate.CurrentState.ToString() : "none"; sample.ultimateReason = ultimate != null ? ultimate.RestrictionReason : "";
            sample.readinessReason = readiness != null ? readiness.ReadinessReason : "";
            sample.ultimateReady = ultimate != null && ultimate.IsReady; sample.readinessReady = readiness != null && readiness.IsReady;
            sample.readyGlow = bridge.centerView?.readyGlow != null && bridge.centerView.readyGlow.activeSelf;
            sample.unavailableOverlay = bridge.centerView?.unavailableOverlay != null && bridge.centerView.unavailableOverlay.activeSelf;
            sample.ultimateText = TextOf(bridge.centerView?.valueText);
            bool validPrimary = primary != null && primary.isActiveAndEnabled && primary.HasResource && Finite(primary.CurrentValue) && Finite(primary.Maximum) &&
                primary.Maximum > 0 && !string.IsNullOrEmpty(primary.ResourceId) && !primary.ResourceId.EndsWith(".ultimate", StringComparison.Ordinal);
            GaugeCheck(checks, gauges, "player HP", hud?.playerHpFill != null ? hud.playerHpFill.fillAmount : 0, bridge.playerView?.healthFill, playerVisible);
            GaugeCheck(checks, gauges, "primary resource", validPrimary ? primary.CurrentValue / primary.Maximum : 0, bridge.playerView?.jobResourceFill, playerVisible);
            GaugeCheck(checks, gauges, "ultimate charge", ultimate != null && ultimate.isActiveAndEnabled ? ultimate.ChargeRatio : 0, bridge.centerView?.ultimateChargeFill, centerVisible);
            Compare(checks, "primary resource kind", "not .ultimate", sample.primaryResourceId, !sample.primaryResourceId.EndsWith(".ultimate", StringComparison.Ordinal), playerVisible && sample.hasPrimary);
            Compare(checks, "primary finite values", "finite", sample.primaryFinite.ToString(), sample.primaryFinite, playerVisible);
            bool ready = ultimate != null && ultimate.isActiveAndEnabled && ultimate.IsReady && readiness != null && readiness.isActiveAndEnabled && readiness.IsReady;
            Bool(checks, "ultimate ready glow", ready, sample.readyGlow, centerVisible);
            bool unavailable = ultimate == null || !ultimate.isActiveAndEnabled || ultimate.CurrentState == AstellaClassUltimateHUD.UltimateState.Unavailable || Ratio(ultimate.ChargeRatio) >= 1 && !ready;
            Bool(checks, "ultimate unavailable", unavailable, sample.unavailableOverlay, centerVisible);
            SameText(checks, "player HP text", hud?.playerHpText, bridge.playerView?.valueText, playerVisible);
            GaugeCheck(checks, gauges, "boss HP", hud?.bossHpFill != null ? hud.bossHpFill.fillAmount : 0, bridge.bossView?.bossFill, bossVisible);
            sample.castVisible = hud?.castRoot != null && hud.castRoot.activeInHierarchy;
            sample.castName = TextOf(hud?.castNameText); sample.castTime = TextOf(hud?.castTimeText); sample.castRatio = Safe(hud?.castFill != null ? hud.castFill.fillAmount : 0);
            sample.groggy = sample.castVisible && sample.castName.Contains("그로기");
            GaugeCheck(checks, gauges, "boss cast", sample.castRatio, bridge.bossView?.castFill, bossVisible && sample.castVisible);
            SameText(checks, "boss cast name", hud?.castNameText, bridge.bossView?.castNameText, bossVisible && sample.castVisible);
            SameText(checks, "boss cast time", hud?.castTimeText, bridge.bossView?.castTimeText, bossVisible && sample.castVisible);
            sample.slots = (bridge.slots ?? Array.Empty<AstellaPrismHUDBridge.SlotMirror>()).Where(value => value != null).Select(mirror => CaptureSlot(bridge, mirror, checks)).ToArray();
            sample.partyRows = (bridge.partyRows ?? Array.Empty<AstellaPrismHUDBridge.PartyMirror>()).Where(value => value != null).Select(mirror => CaptureParty(bridge, mirror, checks, gauges)).ToArray();
            AddBounds(bounds, "player", bridge.playerView); AddBounds(bounds, "player HP gauge", bridge.playerView?.healthFill);
            AddBounds(bounds, "primary resource", bridge.playerView?.jobResourceFill);
            AddBounds(bounds, "ultimate", bridge.centerView); AddBounds(bounds, "ultimate gauge", bridge.centerView?.ultimateChargeFill);
            AddBounds(bounds, "boss", bridge.bossView); AddBounds(bounds, "boss HP gauge", bridge.bossView?.bossFill); AddBounds(bounds, "boss cast gauge", bridge.bossView?.castFill);
            foreach (var mirror in bridge.slots ?? Array.Empty<AstellaPrismHUDBridge.SlotMirror>())
            {
                AddBounds(bounds, "slot " + mirror?.sourceIndex, mirror?.view);
                var source = hud?.slots != null && mirror != null && mirror.sourceIndex >= 0 && mirror.sourceIndex < hud.slots.Length ? hud.slots[mirror.sourceIndex] : null;
                AddBounds(bounds, "slot button " + mirror?.sourceIndex, source?.button);
                AddBounds(bounds, "slot cooldown " + mirror?.sourceIndex, mirror?.view?.cooldownShade);
            }
            var potion = Array.Find(hud?.slots ?? Array.Empty<AstellaOrnateHUD.SlotBinding>(), slot => slot != null && !slot.sharedResurrection && slot.slot == CharacterSlot.Potion);
            AddBounds(bounds, "potion button", potion?.button); AddBounds(bounds, "potion artwork", potion?.artwork);
            foreach (var mirror in bridge.partyRows ?? Array.Empty<AstellaPrismHUDBridge.PartyMirror>()) AddBounds(bounds, "party " + mirror?.sourceIndex, mirror?.view);
            sample.bounds = bounds.ToArray(); sample.gauges = gauges.ToArray(); sample.comparisons = checks.ToArray();
            return sample;
        }
        private static Slot CaptureSlot(AstellaPrismHUDBridge bridge, AstellaPrismHUDBridge.SlotMirror mirror, List<Comparison> checks)
        {
            var source = bridge.hud?.slots != null && mirror.sourceIndex >= 0 && mirror.sourceIndex < bridge.hud.slots.Length ? bridge.hud.slots[mirror.sourceIndex] : null;
            var view = mirror.view; var button = source?.button; bool visible = Active(view);
            var sample = new Slot { sourceIndex = mirror.sourceIndex, slot = source != null ? source.slot.ToString() : "none",
                sourceGameObject = button != null ? PathOf(button.transform) : "", runtimeClickListeners = RuntimeClickListeners(button),
                persistentClickListeners = button != null ? button.onClick.GetPersistentEventCount() : -1,
                key = TextOf(view?.keyText), sourceKey = TextOf(source?.keycap), cooldown = TextOf(view?.cooldownText), sourceCooldown = TextOf(source?.cooldown),
                active = button != null && button.gameObject.activeInHierarchy, enabled = button != null && button.enabled,
                interactable = button != null && button.interactable, effectiveInteractable = button != null && button.IsInteractable(),
                eventSelected = button != null && EventSystem.current != null && EventSystem.current.currentSelectedGameObject == button.gameObject,
                frameSprite = view?.frame?.sprite != null ? view.frame.sprite.name : "", frameOverrideSprite = view?.frame?.overrideSprite != null ? view.frame.overrideSprite.name : "",
                cooldownFill = Safe(view?.cooldownShade != null ? view.cooldownShade.fillAmount : 0),
                sourceCooldownFill = Safe(source?.cooldownShade != null && source.cooldownShade.gameObject.activeSelf ? source.cooldownShade.fillAmount : 0),
                disabledOverlay = view?.disabledOverlay != null && view.disabledOverlay.enabled };
            if (button != null && Mouse.current != null)
                sample.pointerInside = RectTransformUtility.RectangleContainsScreenPoint(button.transform as RectTransform, Mouse.current.position.ReadValue(), CameraFor(button.transform));
            SameText(checks, "slot " + mirror.sourceIndex + " key", source?.keycap, view?.keyText, visible);
            SameText(checks, "slot " + mirror.sourceIndex + " cooldown", source?.cooldown, view?.cooldownText, visible);
            Number(checks, "slot " + mirror.sourceIndex + " cooldown fill", sample.sourceCooldownFill, sample.cooldownFill, .01f, visible);
            Bool(checks, "slot " + mirror.sourceIndex + " unavailable", button == null || !button.IsInteractable(), sample.disabledOverlay, visible);
            return sample;
        }
        private static Party CaptureParty(AstellaPrismHUDBridge bridge, AstellaPrismHUDBridge.PartyMirror mirror, List<Comparison> checks, List<Gauge> gauges)
        {
            var source = Array.Find(bridge.partySource?.rows ?? Array.Empty<AstellaPartyHUDV4.Row>(), row => row != null && row.sourceIndex == mirror.sourceIndex);
            var originals = bridge.partySource?.source?.memberRows;
            var target = originals != null && mirror.sourceIndex >= 0 && mirror.sourceIndex < originals.Length ? originals[mirror.sourceIndex]?.boundTarget : null;
            var view = mirror.view; bool visible = Active(view);
            var sample = new Party { sourceIndex = mirror.sourceIndex, present = target != null, target = target != null ? target.name : "",
                name = TextOf(view?.nameText), hpText = TextOf(view?.valueText), key = TextOf(view?.keyText), visible = visible,
                dead = source?.dead != null && source.dead.activeSelf, selected = source?.selected != null && source.selected.activeSelf,
                aggro = source?.aggro != null && source.aggro.activeSelf, sourceHealth = Safe(source?.gauge != null ? source.gauge.Value : 0),
                health = Safe(view?.healthFill != null ? view.healthFill.Ratio : 0), sourceShield = Safe(source?.shieldGauge != null ? source.shieldGauge.Value : 0),
                shield = Safe(mirror.shieldFill != null ? mirror.shieldFill.Ratio : 0),
                statuses = (source?.statuses ?? Array.Empty<AstellaPartyHUDV4.StatusView>()).Where(status => status?.root != null).Select(status => new Status {
                    key = status.key, path = PathOf(status.root.transform), remaining = TextOf(status.remainingText), activeSelf = status.root.activeSelf, activeInHierarchy = status.root.activeInHierarchy }).ToArray() };
            string prefix = "party " + mirror.sourceIndex;
            GaugeCheck(checks, gauges, prefix + " HP", sample.sourceHealth, view?.healthFill, visible);
            GaugeCheck(checks, gauges, prefix + " shield", sample.sourceShield, mirror.shieldFill, visible && mirror.shieldFill != null);
            SameText(checks, prefix + " name", source?.nameText, view?.nameText, visible); SameText(checks, prefix + " HP text", source?.hpText, view?.valueText, visible);
            Bool(checks, prefix + " selected", sample.selected, view?.selectedOverlay != null && view.selectedOverlay.enabled, visible);
            Bool(checks, prefix + " dead", sample.dead, view?.deadOverlay != null && view.deadOverlay.enabled, visible);
            Bool(checks, prefix + " aggro", sample.aggro, mirror.aggroOverlay != null && mirror.aggroOverlay.activeSelf, visible && mirror.aggroOverlay != null);
            return sample;
        }

        private static void GaugeCheck(List<Comparison> checks, List<Gauge> gauges, string key, float expected, PrismV4Gauge actual, bool visible)
        { gauges.Add(new Gauge { key = key, expected = Safe(expected), actual = actual != null ? actual.Ratio : 0, visible = visible }); Number(checks, key, expected, actual != null ? actual.Ratio : 0, .005f, visible); }
        private static void SameText(List<Comparison> checks, string key, Text expected, Text actual, bool enabled)
            => Compare(checks, key, TextOf(expected), TextOf(actual), TextOf(expected) == TextOf(actual), enabled);
        private static void Bool(List<Comparison> checks, string key, bool expected, bool actual, bool enabled)
            => Compare(checks, key, expected.ToString(), actual.ToString(), expected == actual, enabled);
        private static void Number(List<Comparison> checks, string key, float expected, float actual, float tolerance, bool enabled)
            => Compare(checks, key, Safe(expected).ToString("0.#####", CultureInfo.InvariantCulture), Safe(actual).ToString("0.#####", CultureInfo.InvariantCulture),
                Finite(expected) && Finite(actual) && Mathf.Abs(expected - actual) <= tolerance, enabled, tolerance);
        private static void Compare(List<Comparison> checks, string key, string expected, string actual, bool matched, bool enabled, float tolerance = 0)
        {
            int count = enabled && !matched ? (Faults.TryGetValue(key, out int previous) ? previous : 0) + 1 : 0;
            Faults[key] = count;
            bool stable = count >= StableSamples;
            checks.Add(new Comparison { key = key, expected = expected, actual = actual, tolerance = tolerance, skipped = !enabled,
                mismatch = enabled && !matched, stableMismatch = stable, consecutiveSamples = count });
            if (stable && Reported.Add(key)) Debug.LogError("PRISM LIVE STABLE MISMATCH: " + key + " expected=" + expected + " actual=" + actual);
            if (count == 0) Reported.Remove(key);
        }
        private static void AddBounds(List<Bounds> output, string key, Component component)
        {
            if (component == null || component.transform is not RectTransform rect) return;
            var corners = new Vector3[4]; rect.GetWorldCorners(corners); var camera = CameraFor(rect);
            var points = corners.Select(point => RectTransformUtility.WorldToScreenPoint(camera, point)).ToArray();
            float left = points.Min(point => point.x), right = points.Max(point => point.x), bottom = points.Min(point => point.y), top = points.Max(point => point.y);
            output.Add(new Bounds { key = key, path = PathOf(rect), active = rect.gameObject.activeInHierarchy, xMin = left, xMax = right, yMin = bottom, yMax = top,
                insideScreen = left >= -2 && bottom >= -2 && right <= Screen.width + 2 && top <= Screen.height + 2 });
        }
        private static Camera CameraFor(Transform target)
        { var canvas = target != null ? target.GetComponentInParent<Canvas>() : null; return canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera ?? Camera.main : null; }
        private static bool Active(Component component) => component != null && component.gameObject.activeInHierarchy;
        // Inspection only. Do not call PrepareInvoke/Invoke or change any listener list.
        private static int RuntimeClickListeners(Button button)
        {
            if (button == null) return -1;
            try
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                var calls = typeof(UnityEngine.Events.UnityEventBase).GetField("m_Calls", flags)?.GetValue(button.onClick);
                var runtime = calls?.GetType().GetField("m_RuntimeCalls", flags)?.GetValue(calls);
                return runtime is System.Collections.ICollection collection ? collection.Count : -1;
            }
            catch { return -1; } // Unsupported Unity internals remain explicitly unknown.
        }
        private static string PublicPhase()
        {
            if (RaidNetworkSession.Multiplayer && RaidNetworkSession.Instance != null) return RaidNetworkSession.Instance.BattlePhase.ToString();
            var stage = StageManager.Instance;
            return stage == null ? "none" : stage.EncounterActive ? "Combat" : stage.IsWiped ? "Wiped" : stage.IsPreparing ? "Preparing" : stage.IsStageTransitioning ? "Transition" : "NonCombat";
        }
        private static string TextOf(Text text) => text != null ? text.text ?? "" : "";
        private static float Safe(float value) => Finite(value) ? value : 0;
        private static float Ratio(float value) => Mathf.Clamp01(Safe(value));
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static string PathOf(Transform target)
        { if (target == null) return ""; var names = new List<string>(); for (var current = target; current != null; current = current.parent) names.Add(current.name); names.Reverse(); return string.Join("/", names); }
        private static void Append<T>(T value) => File.AppendAllText(_path, JsonUtility.ToJson(value) + Environment.NewLine, new UTF8Encoding(false));

        [MenuItem("Tools/Astella/Prism V4/Run Integration Checks")]
        public static void RunIntegrationChecks()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before the explicit read-only integration checks.");
            for (int i = 0; i < SceneManager.sceneCount; i++) if (SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Save or discard existing scene edits before loading SampleScene for inspection.");
            const string scenePath = "Assets/Scenes/SampleScene.unity";
            string profilePath = AssetDatabase.GetAssetPath(RaidUIPresentation.Settings);
            string sceneHash = Hash(scenePath), profileHash = Hash(profilePath);
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            AstellaPrismHUDChecks.ValidateActiveHud(); AstellaPrismHUDChecks.ValidateLoadedSceneHud();
            var fixture = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try { SceneManager.SetActiveScene(fixture); RaidUIBindingChecks.Run(); }
            finally { SceneManager.SetActiveScene(scene); EditorSceneManager.CloseScene(fixture, true); }
            AstellaPrismRenderChecks.RenderFillValidation();
            if (sceneHash != Hash(scenePath) || profileHash != Hash(profilePath)) throw new InvalidOperationException("Read-only checks changed a persisted scene/profile.");
            Debug.Log("PRISM INTEGRATION CHECKS PASS: asset ownership, active scene/profile, temporary binding fixture and fill renders. Persisted scene/profile unchanged; no Play Mode or live input test.");
        }
        private static string Hash(string path)
        { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))); }
    }
}
#endif
