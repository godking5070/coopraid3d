#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Astella.PrismV4;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace RaidRogue.Editor
{
    /// <summary>Explicit, read-only inspection of the active HUD asset. Writes reports under Logs only.</summary>
    public static class AstellaPrismHUDChecks
    {
        private const string ReportFolder = "Logs/PrismV4";

        [Serializable] public sealed class Reference
        {
            public string path = "", type = "", assetPath = "", guid = "";
            public long localId;
            public bool insideHud;
        }
        [Serializable] public sealed class FieldReference { public string field; public Reference value; }
        [Serializable] public sealed class Presenter
        {
            public string type;
            public Reference component;
            public FieldReference[] references;
        }
        [Serializable] public sealed class Slot
        {
            public int index;
            public string slot, bindingOverride;
            public bool sharedResurrection, bindAction;
            public FieldReference[] references;
            public string[] persistentClickMethods;
            public Reference[] persistentClickTargets;
        }
        [Serializable] public sealed class PartyRow
        {
            public int index, sourceIndex;
            public FieldReference[] references;
            public Status[] statuses;
        }
        [Serializable] public sealed class Status { public string key; public Reference root, remainingText; }
        [Serializable] public sealed class HotbarSlot { public int index; public FieldReference[] references; }
        [Serializable] public sealed class LayoutRoot
        {
            public string id;
            public Reference root;
            public Vector2 anchorMin, anchorMax, pivot, sizeDelta, anchoredPosition;
            public Vector3 localScale;
            public string[] children;
        }
        [Serializable] public sealed class CanvasDescription
        {
            public Reference canvas, scaler;
            public string renderMode, uiScaleMode;
            public int sortingOrder;
            public float scaleFactor, referencePixelsPerUnit, matchWidthOrHeight;
            public Vector2 referenceResolution;
        }
        [Serializable] public sealed class StructureReport
        {
            public string generatedUtc, profilePath, prefabPath;
            public Reference prefab, ornate;
            public int ornateCount, combatLayoutVersion;
            public FieldReference[] roots;
            public Presenter[] presenters;
            public Slot[] slots;
            public PartyRow[] partyRows;
            public HotbarSlot[] hotbarSlots;
            public LayoutRoot[] hudEditRoots;
            public CanvasDescription[] canvases;
        }
        [Serializable] public sealed class ValidationReport
        {
            public string generatedUtc, prefabPath, sourcePrefabPath;
            public int checks;
            public bool passed;
            public string[] errors;
            public string boundary = "Asset/reference inspection only. Does not certify rendering, pointer input, gameplay or multiplayer.";
        }

        [MenuItem("Tools/Astella/Prism V4/Describe Active HUD")]
        public static void DescribeActiveHud()
        {
            var profile = RaidUIPresentation.Settings;
            if (profile == null || profile.hudPrefab == null)
                throw new InvalidOperationException("The active RaidUIPresentation HUD is missing.");
            var root = profile.hudPrefab.gameObject;
            var huds = root.GetComponentsInChildren<AstellaOrnateHUD>(true);
            var hud = huds.FirstOrDefault();
            if (hud == null) throw new InvalidOperationException("The active HUD has no AstellaOrnateHUD presenter.");
            var party = root.GetComponentInChildren<AstellaPartyHUDV4>(true);
            var report = new StructureReport
            {
                generatedUtc = DateTime.UtcNow.ToString("O"), profilePath = AssetDatabase.GetAssetPath(profile),
                prefabPath = AssetDatabase.GetAssetPath(root), prefab = Ref(root, root), ornate = Ref(root, hud),
                ornateCount = huds.Length, combatLayoutVersion = hud.combatLayoutVersion,
                roots = Fields(root, "playerRoot", hud.playerRoot, "resourceRoot", hud.resourceRoot, "bossRoot", hud.bossRoot,
                    "castRoot", hud.castRoot, "secondaryResourceRoot", hud.secondaryResourceRoot, "partyRevivalRoot", hud.partyRevivalRoot,
                    "playerHpFill", hud.playerHpFill, "playerHpText", hud.playerHpText, "resourceFill", hud.resourceFill,
                    "resourceText", hud.resourceText, "secondaryResourceFill", hud.secondaryResourceFill,
                    "secondaryResourceText", hud.secondaryResourceText, "bossHpFill", hud.bossHpFill, "castFill", hud.castFill,
                    "hotbarSource", hud.hotbarSource, "tooltipRoot", hud.tooltipRoot, "tooltipText", hud.tooltipText),
                presenters = root.GetComponentsInChildren<MonoBehaviour>(true).Where(IsPresenter).Select(component => new Presenter
                { type = component.GetType().FullName, component = Ref(root, component), references = SerializedReferences(root, component) }).ToArray(),
                slots = (hud.slots ?? Array.Empty<AstellaOrnateHUD.SlotBinding>()).Select((slot, index) => DescribeSlot(root, slot, index)).ToArray(),
                partyRows = (party?.rows ?? Array.Empty<AstellaPartyHUDV4.Row>()).Select((row, index) => DescribeRow(root, row, index)).ToArray(),
                hotbarSlots = (hud.hotbarSource?.slots ?? Array.Empty<HotbarSlotView>()).Select((slot, index) => new HotbarSlot { index = index, references = slot == null ? Array.Empty<FieldReference>() :
                    Fields(root, "slotButton", slot.slotButton, "keybindBadgeText", slot.keybindBadgeText, "skillNameText", slot.skillNameText,
                        "cooldownOverlayImage", slot.cooldownOverlayImage, "cooldownNumberText", slot.cooldownNumberText,
                        "comboTimerBarRoot", slot.comboTimerBarRoot, "comboTimerFillImage", slot.comboTimerFillImage) }).ToArray(),
                hudEditRoots = DescribeLayoutRoots(root, hud),
                canvases = root.GetComponentsInChildren<Canvas>(true).Select(canvas => {
                    var scaler = canvas.GetComponent<CanvasScaler>();
                    return new CanvasDescription { canvas = Ref(root, canvas), scaler = Ref(root, scaler), renderMode = canvas.renderMode.ToString(),
                        sortingOrder = canvas.sortingOrder, scaleFactor = canvas.scaleFactor,
                        uiScaleMode = scaler != null ? scaler.uiScaleMode.ToString() : "", referencePixelsPerUnit = scaler != null ? scaler.referencePixelsPerUnit : 0,
                        referenceResolution = scaler != null ? scaler.referenceResolution : Vector2.zero, matchWidthOrHeight = scaler != null ? scaler.matchWidthOrHeight : 0 };
                }).ToArray()
            };
            string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, ReportFolder);
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "active-hud-structure.json");
            string baseline = Path.Combine(folder, "source-hud-structure.json");
            if (!File.Exists(baseline) && File.Exists(path))
            {
                var previous = JsonUtility.FromJson<StructureReport>(File.ReadAllText(path));
                if (previous != null && previous.prefabPath != report.prefabPath) File.Copy(path, baseline);
            }
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            Debug.Log("PRISM HUD STRUCTURE: " + Path.GetFullPath(path) + " | " + report.prefabPath + " | slots=" + report.slots.Length + " | partyRows=" + report.partyRows.Length);
        }

        [MenuItem("Tools/Astella/Prism V4/Validate Active HUD")]
        public static void ValidateActiveHud()
        {
            var prefab = RaidUIPresentation.Settings?.hudPrefab;
            if (prefab == null) throw new InvalidOperationException("The active RaidUIPresentation HUD is missing.");
            ValidatePrefab(AssetDatabase.GetAssetPath(prefab));
        }

        // A copy can be checked before the main owner decides to activate its profile.
        public static void ValidatePrefab(string prefabPath, string baselineReportPath = null)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (root == null) throw new InvalidOperationException("HUD prefab is missing: " + prefabPath);
            var errors = new List<string>();
            int checks = 0;
            void Check(bool condition, string reason) { checks++; if (!condition) errors.Add(reason); }
            void Inside(Object value, string field) { Check(value != null && Ref(root, value).insideHud, "Missing or external HUD reference: " + field); }
            void Gauge(PrismV4Gauge gauge, string field)
            {
                Inside(gauge, field);
                if (gauge == null) return;
                Check(gauge.sourceFill == null, field + " must use the bridge SetValue path, with no competing sourceFill.");
                Check(gauge.sprite != null && gauge.type == Image.Type.Simple, field + " must retain its full-size Simple artwork.");
                Check(!gauge.raycastTarget, field + " must not intercept pointer input.");
            }
            var baseline = LoadBaseline(baselineReportPath);
            Check(baseline != null, "Describe the original active HUD before validating a Prism copy.");
            if (baseline != null)
            {
                Check(prefabPath != baseline.prefabPath, "Prism integration must use a copy of the original HUD.");
                Check(AssetDatabase.LoadAssetAtPath<GameObject>(baseline.prefabPath) != null, "The original HUD prefab must remain available.");
            }
            Check(prefabPath.StartsWith("Assets/UI/AstellaPrismV4/", StringComparison.Ordinal), "The new HUD must stay inside the Prism V4 folder.");
            var bridges = root.GetComponentsInChildren<AstellaPrismHUDBridge>(true);
            var huds = root.GetComponentsInChildren<AstellaOrnateHUD>(true);
            Check(bridges.Length == 1 && huds.Length == 1, "Expected one Prism bridge and one original ornate presenter.");
            var bridge = bridges.FirstOrDefault(); var hud = huds.FirstOrDefault();
            if (bridge != null && hud != null)
            {
                Check(bridge.enabled && hud.enabled && hud.combatLayoutVersion >= 1, "Source presenters must remain enabled and must not trigger the legacy live layout migration.");
                Check(bridge.hud == hud, "Bridge must use the ornate presenter from the same copy.");
                Inside(bridge.jobSource, "bridge.jobSource"); Inside(bridge.ultimateSource, "bridge.ultimateSource");
                Inside(bridge.readinessSource, "bridge.readinessSource"); Inside(bridge.partySource, "bridge.partySource");
                if (bridge.jobSource != null) Check(bridge.jobSource.enabled && bridge.jobSource.hud == hud, "Primary resource source must remain enabled and bound to this HUD.");
                if (bridge.ultimateSource != null) Check(bridge.ultimateSource.enabled && bridge.ultimateSource.hud == hud && bridge.ultimateSource.readinessSource == bridge.readinessSource, "Ultimate state and readiness sources must remain independent and connected.");
                if (bridge.readinessSource != null) Check(bridge.readinessSource.enabled && bridge.readinessSource.hud == hud, "Readiness source must remain enabled and bound to this HUD.");
                Inside(hud.hotbarSource, "hud.hotbarSource");
                foreach (var source in new[] { hud.playerHpFill, hud.resourceFill, hud.bossHpFill, hud.castFill, hud.secondaryResourceFill })
                    Check(source != null && source is not PrismV4Gauge && Ref(root, source).insideHud, "Legacy fill images must remain separate data inputs inside the copied HUD.");
                Inside(bridge.playerView, "bridge.playerView"); Inside(bridge.bossView, "bridge.bossView"); Inside(bridge.centerView, "bridge.centerView");
                if (bridge.playerView != null && bridge.centerView != null)
                {
                    Gauge(bridge.playerView.healthFill, "playerView.healthFill"); Gauge(bridge.playerView.jobResourceFill, "playerView.jobResourceFill");
                    Gauge(bridge.centerView.ultimateChargeFill, "centerView.ultimateChargeFill");
                    Check(bridge.playerView.jobResourceFill != bridge.centerView.ultimateChargeFill &&
                        bridge.playerView.resourceText != bridge.centerView.valueText && bridge.playerView.resourceText != hud.secondaryResourceText,
                        "Primary job resource and central ultimate must use distinct gauge/text references.");
                    Check(IsWithin(bridge.playerView.jobResourceFill, EditRoot(hud, "identity", hud.resourceRoot)), "Primary resource artwork must follow the mapped identity HUD-edit root.");
                    Check(IsWithin(bridge.playerView.healthFill, EditRoot(hud, "hp", hud.playerHpText != null ? hud.playerHpText.transform.parent?.gameObject : null)), "HP artwork must follow the mapped HP HUD-edit root.");
                    var center = hud.playerRoot != null ? hud.playerRoot.transform.Find("Center - V") : null;
                    Check(IsWithin(bridge.centerView, EditRoot(hud, "ultimate", center != null ? center.gameObject : null)), "Central ultimate must preserve the mapped ultimate HUD-edit root.");
                    Inside(bridge.playerView.valueText, "playerView.valueText"); Inside(bridge.playerView.resourceText, "playerView.resourceText");
                    Inside(bridge.centerView.valueText, "centerView.valueText"); Inside(bridge.centerView.keyText, "centerView.keyText");
                }
                if (bridge.bossView != null)
                {
                    Gauge(bridge.bossView.bossFill, "bossView.bossFill"); Gauge(bridge.bossView.castFill, "bossView.castFill");
                    Check(IsWithin(bridge.bossView.bossFill, EditRoot(hud, "boss", hud.bossRoot)) && IsWithin(bridge.bossView.castFill, EditRoot(hud, "boss.cast", hud.castRoot)), "Boss and cast artwork must follow their mapped HUD-edit roots while retaining the original visibility sources.");
                    Inside(bridge.bossView.nameText, "bossView.nameText"); Inside(bridge.bossView.castNameText, "bossView.castNameText"); Inside(bridge.bossView.castTimeText, "bossView.castTimeText");
                }
                var mainSlots = new[] { CharacterSlot.Gcd1, CharacterSlot.Gcd2, CharacterSlot.OffGcd1, CharacterSlot.Counter,
                    CharacterSlot.Job1, CharacterSlot.OffGcd2, CharacterSlot.Identity, CharacterSlot.Movement };
                var mirrors = bridge.slots ?? Array.Empty<AstellaPrismHUDBridge.SlotMirror>();
                Check(mirrors.Length == 8 && mirrors.Where(m => m != null).Select(m => m.sourceIndex).Distinct().Count() == 8, "Expected eight distinct main skill-slot mirrors.");
                var mirroredSlots = new List<CharacterSlot>();
                foreach (var mirror in mirrors)
                {
                    bool validIndex = mirror != null && hud.slots != null && mirror.sourceIndex >= 0 && mirror.sourceIndex < hud.slots.Length && hud.slots[mirror.sourceIndex] != null;
                    Check(validIndex, "Each main skill mirror must refer to an existing original slot index.");
                    if (!validIndex) continue;
                    var source = hud.slots[mirror.sourceIndex]; mirroredSlots.Add(source.slot);
                    Inside(mirror.view, "slot mirror " + mirror.sourceIndex);
                    if (mirror.view == null) continue;
                    Check(mirror.view.button == source.button, "Skill mirror must reuse its original Button: " + mirror.sourceIndex);
                    Check(HasPointerSurface(source.button), "Skill Button must retain a visible pointer surface: " + mirror.sourceIndex);
                    Inside(mirror.view.keyText, "skill key " + mirror.sourceIndex); Inside(mirror.view.cooldownText, "skill cooldown " + mirror.sourceIndex);
                    Inside(mirror.view.artwork, "skill icon " + mirror.sourceIndex);
                    Check(mirror.view.frame != null && mirror.view.frame != mirror.view.artwork && mirror.view.cooldownShade != null && mirror.view.disabledOverlay != null,
                        "Skill frame/icon/cooldown/unavailable graphics must be distinct and connected: " + mirror.sourceIndex);
                }
                Check(mainSlots.All(slot => mirroredSlots.Count(value => value == slot) == 1), "Main mirrors must cover 1/2/3/4/Q/F/R/Space without replacing V, potion or shared revival.");
                var ultimate = Array.Find(hud.slots ?? Array.Empty<AstellaOrnateHUD.SlotBinding>(), slot => slot != null && !slot.sharedResurrection && slot.slot == CharacterSlot.Ultimate);
                Check(ultimate?.button != null && bridge.centerView != null && bridge.centerView.button == ultimate.button && HasPointerSurface(ultimate.button), "Central V must retain its original clickable Button.");
                Check((hud.slots ?? Array.Empty<AstellaOrnateHUD.SlotBinding>()).Any(slot => slot != null && slot.slot == CharacterSlot.Potion) &&
                    (hud.slots ?? Array.Empty<AstellaOrnateHUD.SlotBinding>()).Any(slot => slot != null && slot.sharedResurrection), "Potion and shared revival bindings must remain present.");
                if (baseline != null) ValidateOriginalSlots(root, hud, baseline, Check);

                var rows = bridge.partyRows ?? Array.Empty<AstellaPrismHUDBridge.PartyMirror>();
                Check(rows.Length == 4 && rows.Where(row => row != null).Select(row => row.sourceIndex).OrderBy(index => index).SequenceEqual(new[] { 0, 1, 2, 3 }), "Expected four distinct party mirrors with source indices 0..3.");
                foreach (var mirror in rows)
                {
                    var original = Array.Find(bridge.partySource?.rows ?? Array.Empty<AstellaPartyHUDV4.Row>(), row => row != null && mirror != null && row.sourceIndex == mirror.sourceIndex);
                    Check(original != null && mirror?.view != null, "Party mirror must retain its original source row.");
                    if (original == null || mirror?.view == null) continue;
                    Inside(mirror.view, "party view " + mirror.sourceIndex); Gauge(mirror.view.healthFill, "party health " + mirror.sourceIndex);
                    Check(mirror.view.button == original.button && HasPointerSurface(original.button), "Party selection must retain its original clickable Button: " + mirror.sourceIndex);
                    Inside(mirror.view.nameText, "party name " + mirror.sourceIndex); Inside(mirror.view.valueText, "party HP " + mirror.sourceIndex); Inside(mirror.view.keyText, "party key " + mirror.sourceIndex);
                    Check(mirror.view.selectedOverlay != null && mirror.view.deadOverlay != null, "Party selected/dead states must remain bound: " + mirror.sourceIndex);
                    Check(mirror.shieldFill != null || original.shieldGauge?.artwork != null && VisibleThroughGroups(original.shieldGauge.artwork.transform), "Party shield display must remain visible: " + mirror.sourceIndex);
                    if (mirror.shieldFill != null) Gauge(mirror.shieldFill, "party shield " + mirror.sourceIndex);
                    Check(mirror.aggroOverlay != null || original.aggro != null && VisibleThroughGroups(original.aggro.transform), "Party aggro state must remain visible: " + mirror.sourceIndex);
                    foreach (var status in original.statuses ?? Array.Empty<AstellaPartyHUDV4.StatusView>())
                        Check(status?.root != null && Ref(root, status.root).insideHud && VisibleThroughGroups(status.root.transform), "Existing party statuses must not remain beneath hidden legacy graphics: " + mirror.sourceIndex + "/" + status?.key);
                }
                foreach (var presenter in root.GetComponentsInChildren<MonoBehaviour>(true).Where(IsPresenter))
                    Check(presenter.enabled && VisibleThroughGroups(presenter.transform, false), "Data presenter must remain enabled beneath active source containers: " + presenter.GetType().Name);
                Check(DescribeLayoutRoots(root, hud).All(entry => entry.root.insideHud), "Existing HUD edit roots must remain connected.");
            }
            WriteValidation(new ValidationReport { generatedUtc = DateTime.UtcNow.ToString("O"), prefabPath = prefabPath,
                sourcePrefabPath = baseline?.prefabPath ?? "", checks = checks, passed = errors.Count == 0, errors = errors.ToArray() });
        }

        public static void ValidateLoadedSceneHud()
        {
            string expected = AssetDatabase.GetAssetPath(RaidUIPresentation.Settings?.hudPrefab);
            var roots = Object.FindObjectsByType<RaidUIRoot>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(root => root.gameObject.scene.IsValid() && root.gameObject.scene.isLoaded && !EditorUtility.IsPersistent(root)).ToArray();
            var active = roots.Where(root => root.gameObject.activeInHierarchy).ToArray();
            if (active.Length != 1 || PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(active[0].gameObject) != expected ||
                active[0].GetComponentInChildren<AstellaPrismHUDBridge>(true) == null)
                throw new InvalidOperationException("Expected one active scene HUD instance from the active Prism profile. Profile-only switching cannot replace an already active scene RaidUIRoot.");
            Debug.Log("PRISM SCENE HUD PASS: active instance=" + active[0].name + " | " + expected + " | inactive HUD roots retained=" + (roots.Length - 1));
        }

        private static void ValidateOriginalSlots(GameObject root, AstellaOrnateHUD hud, StructureReport baseline, Action<bool, string> check)
        {
            var current = hud.slots ?? Array.Empty<AstellaOrnateHUD.SlotBinding>();
            check(baseline.slots != null && baseline.slots.Length == current.Length, "The original complete SlotBinding array must retain its length and ordering.");
            for (int index = 0; index < Math.Min(baseline.slots?.Length ?? 0, current.Length); index++)
            {
                var before = baseline.slots[index]; var after = current[index];
                check(before != null && after != null && before.slot == after.slot.ToString() && before.sharedResurrection == after.sharedResurrection &&
                    before.bindAction == after.bindAction && before.bindingOverride == after.bindingOverride, "Original slot identity/action policy changed at index " + index);
                if (before == null || after == null) continue;
                var button = before.references?.FirstOrDefault(reference => reference.field == "button")?.value;
                check(button != null && after.button != null && button.localId == Ref(root, after.button).localId, "Original Button local file ID was not retained at slot " + index);
                int count = after.button != null ? after.button.onClick.GetPersistentEventCount() : 0;
                check((before.persistentClickMethods ?? Array.Empty<string>()).SequenceEqual(Enumerable.Range(0, count).Select(i => after.button.onClick.GetPersistentMethodName(i))), "Original persistent skill callbacks changed at slot " + index);
                foreach (string field in new[] { "sourceLabel", "sourceStatus", "sourceKey", "fallbackArt" })
                {
                    var oldReference = before.references?.FirstOrDefault(reference => reference.field == field)?.value;
                    Object value = field == "sourceLabel" ? after.sourceLabel : field == "sourceStatus" ? after.sourceStatus : field == "sourceKey" ? after.sourceKey : after.fallbackArt;
                    var actual = Ref(root, value);
                    check(oldReference != null && oldReference.localId == actual.localId, "Original source/asset reference changed: slot " + index + "/" + field);
                }
            }
        }
        private static bool HasPointerSurface(Button button) => button != null && button.gameObject.activeSelf &&
            button.GetComponentsInChildren<Graphic>(true).Any(graphic => graphic.enabled && graphic.raycastTarget &&
                IsActiveTo(graphic.transform, button.transform) && VisibleThroughGroups(graphic.transform));
        private static bool IsActiveTo(Transform target, Transform boundary)
        {
            for (var current = target; current != null; current = current.parent)
            { if (!current.gameObject.activeSelf) return false; if (current == boundary) return true; }
            return false;
        }
        private static bool VisibleThroughGroups(Transform target, bool checkAlpha = true)
        {
            bool ignore = false;
            for (var current = target; current != null; current = current.parent)
            {
                foreach (var group in current.GetComponents<CanvasGroup>())
                {
                    if (!group.enabled) continue;
                    if (!ignore && checkAlpha && group.alpha <= .001f) return false;
                    if (group.ignoreParentGroups) ignore = true;
                }
            }
            return true;
        }
        private static bool IsWithin(Object value, GameObject parent)
        {
            var target = value is GameObject go ? go.transform : value is Component component ? component.transform : null;
            return target != null && parent != null && (target == parent.transform || target.IsChildOf(parent.transform));
        }
        private static GameObject EditRoot(AstellaOrnateHUD hud, string id, GameObject fallback)
        {
            var layout = hud != null ? hud.GetComponent<AstellaPrismHUDLayout>() : null;
            var entry = layout != null ? layout.MappedEntries.FirstOrDefault(value => value.id == id) : null;
            return entry?.rect != null ? entry.rect.gameObject : fallback;
        }
        private static StructureReport LoadBaseline(string overridePath = null)
        {
            if (!string.IsNullOrEmpty(overridePath)) return JsonUtility.FromJson<StructureReport>(File.ReadAllText(overridePath));
            string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, ReportFolder);
            string source = Path.Combine(folder, "source-hud-structure.json");
            string active = Path.Combine(folder, "active-hud-structure.json");
            string shipped = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "UI-Production", "PrismV4", "main-source-bindings.json");
            string path = File.Exists(source) ? source : File.Exists(shipped) ? shipped : active;
            return File.Exists(path) ? JsonUtility.FromJson<StructureReport>(File.ReadAllText(path)) : null;
        }
        private static void WriteValidation(ValidationReport report)
        {
            string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, ReportFolder); Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "active-hud-validation.json"), JsonUtility.ToJson(report, true));
            if (!report.passed) throw new InvalidOperationException("Prism HUD validation failed (" + report.errors.Length + "/" + report.checks + "): " + string.Join("; ", report.errors));
            Debug.Log("PRISM HUD ASSET PASS: " + report.checks + " reference/ownership checks | " + report.prefabPath);
        }

        private static bool IsPresenter(MonoBehaviour component) => component != null &&
            (component is AstellaOrnateHUD || component is AstellaPlayerHUDV3 || component is AstellaClassJobHUD ||
             component is AstellaClassUltimateHUD || component is AstellaBossHUDV4 || component is AstellaPartyHUDV4 ||
             component is SkillHotbarUI || component is PartyListUI || component.GetType().Name == "AstellaPrismHUDBridge");

        private static Slot DescribeSlot(GameObject root, AstellaOrnateHUD.SlotBinding slot, int index)
        {
            if (slot == null) return new Slot { index = index, slot = "<null>" };
            int listeners = slot.button != null ? slot.button.onClick.GetPersistentEventCount() : 0;
            return new Slot { index = index, slot = slot.slot.ToString(), bindingOverride = slot.bindingOverride,
                sharedResurrection = slot.sharedResurrection, bindAction = slot.bindAction,
                references = Fields(root, "button", slot.button, "sourceLabel", slot.sourceLabel, "sourceStatus", slot.sourceStatus,
                    "sourceKey", slot.sourceKey, "keycap", slot.keycap, "cooldown", slot.cooldown, "cooldownShade", slot.cooldownShade,
                    "artwork", slot.artwork, "fallbackArt", slot.fallbackArt, "stateGroup", slot.stateGroup),
                persistentClickMethods = Enumerable.Range(0, listeners).Select(i => slot.button.onClick.GetPersistentMethodName(i)).ToArray(),
                persistentClickTargets = Enumerable.Range(0, listeners).Select(i => Ref(root, slot.button.onClick.GetPersistentTarget(i))).ToArray() };
        }
        private static PartyRow DescribeRow(GameObject root, AstellaPartyHUDV4.Row row, int index) => row == null
            ? new PartyRow { index = index, sourceIndex = -1 }
            : new PartyRow { index = index, sourceIndex = row.sourceIndex,
                references = Fields(root, "root", row.root, "button", row.button, "nameText", row.nameText, "hpText", row.hpText,
                    "keyText", row.keyText, "jobIcon", row.jobIcon, "gauge", row.gauge, "shieldGauge", row.shieldGauge,
                    "gauge.sourceFill", row.gauge != null ? row.gauge.sourceFill : null, "gauge.artwork", row.gauge != null ? row.gauge.artwork : null,
                    "shieldGauge.artwork", row.shieldGauge != null ? row.shieldGauge.artwork : null,
                    "dead", row.dead, "selected", row.selected, "aggro", row.aggro, "stateGroup", row.stateGroup),
                statuses = (row.statuses ?? Array.Empty<AstellaPartyHUDV4.StatusView>()).Where(s => s != null).Select(s =>
                    new Status { key = s.key, root = Ref(root, s.root), remainingText = Ref(root, s.remainingText) }).ToArray() };

        private static LayoutRoot[] DescribeLayoutRoots(GameObject root, AstellaOrnateHUD hud)
        {
            var player = hud.playerRoot != null ? hud.playerRoot.transform : null;
            var legacy = new object[] { "skills.left", player != null ? player.Find("Left - 1234") : null,
                "skills.right", player != null ? player.Find("Right - Q F R Space") : null,
                "identity", hud.resourceRoot, "ultimate", player != null ? player.Find("Center - V") : null,
                "hp", hud.playerHpText != null ? hud.playerHpText.transform.parent : null,
                "ultimate.resource", hud.secondaryResourceRoot, "revival", hud.partyRevivalRoot, "boss", hud.bossRoot, "boss.cast", hud.castRoot,
                "party", root.GetComponentInChildren<PartyListUI>(true)?.panelRoot };
            var pairs = new List<object>();
            var mapped = hud.GetComponent<AstellaPrismHUDLayout>();
            if (mapped != null) foreach (var entry in mapped.MappedEntries) { pairs.Add(entry.id); pairs.Add(entry.rect); }
            pairs.AddRange(legacy);
            var result = new List<LayoutRoot>();
            for (int i = 0; i < pairs.Count; i += 2)
            {
                if (result.Any(entry => entry.id == (string)pairs[i])) continue;
                var reference = pairs[i + 1] as Object;
                var transform = reference is GameObject go ? go.transform : reference is Component component ? component.transform : null;
                var rect = transform as RectTransform;
                result.Add(new LayoutRoot { id = (string)pairs[i], root = Ref(root, reference),
                    anchorMin = rect != null ? rect.anchorMin : Vector2.zero, anchorMax = rect != null ? rect.anchorMax : Vector2.zero,
                    pivot = rect != null ? rect.pivot : Vector2.zero, sizeDelta = rect != null ? rect.sizeDelta : Vector2.zero,
                    anchoredPosition = rect != null ? rect.anchoredPosition : Vector2.zero, localScale = transform != null ? transform.localScale : Vector3.zero,
                    children = transform != null ? transform.Cast<Transform>().Select(t => Ref(root, t).path).ToArray() : Array.Empty<string>() });
            }
            return result.ToArray();
        }

        private static FieldReference[] SerializedReferences(GameObject root, MonoBehaviour component)
        {
            var result = new List<FieldReference>();
            var iterator = new SerializedObject(component).GetIterator();
            while (iterator.Next(true))
                if (iterator.propertyType == SerializedPropertyType.ObjectReference && iterator.propertyPath != "m_Script")
                    result.Add(new FieldReference { field = iterator.propertyPath, value = Ref(root, iterator.objectReferenceValue) });
            return result.ToArray();
        }
        private static FieldReference[] Fields(GameObject root, params object[] pairs)
        {
            var result = new FieldReference[pairs.Length / 2];
            for (int i = 0; i < pairs.Length; i += 2) result[i / 2] = new FieldReference { field = (string)pairs[i], value = Ref(root, pairs[i + 1] as Object) };
            return result;
        }
        private static Reference Ref(GameObject root, Object value)
        {
            if (value == null) return new Reference();
            var transform = value is GameObject go ? go.transform : value is Component component ? component.transform : null;
            bool inside = transform != null && (transform == root.transform || transform.IsChildOf(root.transform));
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out string guid, out long localId);
            return new Reference { type = value.GetType().FullName, insideHud = inside, assetPath = AssetDatabase.GetAssetPath(value), guid = guid ?? "", localId = localId,
                path = inside ? transform == root.transform ? "$" : AnimationUtility.CalculateTransformPath(transform, root.transform) : value.name };
        }
    }
}
#endif
