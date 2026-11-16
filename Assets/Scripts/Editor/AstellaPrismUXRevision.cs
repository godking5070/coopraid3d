#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace RaidRogue.Editor
{
    // Explicit, reviewable authoring. No startup hook or runtime design defaults.
    public static partial class AstellaPrismUXRevision
    {
        const string Evidence = "C:/Astella/ui-ux-revision-20261116";
        const string ProfilePath = "Assets/Resources/UI/RaidUIPresentation.asset";
        const string ScenePath = "Assets/Scenes/SampleScene.unity";
        const string Fonts = "Assets/UI/AstellaPrismV4/Typography/Fonts/";
        static readonly HashSet<string> RevisedViews = new HashSet<string>
            { "connection", "settings", "origin", "metrics", "wipe", "resurrection", "skill-reward" };
        [Serializable] sealed class AssetRecord
        {
            public string source, copy, sourceHashBefore, sourceHashAfter;
            public bool combatPreserved;
            public string[] addedActions;
            public AstellaPrismRemainingChecks.Report preservation;
        }
        [Serializable] sealed class RevisionReport
        {
            public string utc, folder, backup, error;
            public bool passed, sceneSaved;
            public AssetRecord[] assets;
        }

        [MenuItem("Tools/Astella/UX Revision/Preview Current Screens")]
        public static void PreviewCurrent() => AstellaPrismRemainingPreview.RenderCatalogTo(Evidence + "/before");
        [MenuItem("Tools/Astella/UX Revision/Preview Revised Screens")]
        public static void PreviewRevised() => AstellaPrismRemainingPreview.RenderCatalogTo(Evidence + "/after");

        [MenuItem("Tools/Astella/UX Revision/Restore Scene After Rejected Revision")]
        public static void RestoreRejectedScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play first.");
            var report = JsonUtility.FromJson<RevisionReport>(File.ReadAllText(Evidence + "/application-report.json"));
            var profile = AssetDatabase.LoadAssetAtPath<RaidUIPresentation>(ProfilePath);
            if (report == null || report.passed || report.sceneSaved || AssetDatabase.GetAssetPath(profile.hudPrefab).Contains("/UXRevision/") ||
                Hash(ScenePath) != Hash(report.backup + "/SampleScene.unity"))
                throw new InvalidOperationException("The preserved scene/profile cannot be restored by this recovery action.");
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Debug.Log("PRISM UX RECOVERY: reloaded the unchanged on-disk scene after rejected authoring.");
        }

        [Serializable] sealed class DetailRecord
        {
            public string asset, backup, beforeHash, afterHash;
            public bool combatPreserved;
            public AstellaPrismRemainingChecks.Report preservation;
        }

        [MenuItem("Tools/Astella/UX Revision/Refine Revised Menu Details")]
        public static void RefineDetails()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Stop Play and close Prefab Mode before authoring.");
            if (!string.Equals(Path.GetFullPath(Application.dataPath).TrimEnd('/', '\\'), @"C:\Astella\Astella\Assets", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Use the original project.");
            var profile = AssetDatabase.LoadAssetAtPath<RaidUIPresentation>(ProfilePath);
            var paths = new[] { AssetDatabase.GetAssetPath(profile?.hudPrefab),
                AssetDatabase.GetAssetPath(profile?.View("connection")), AssetDatabase.GetAssetPath(profile?.View("settings")),
                AssetDatabase.GetAssetPath(profile?.View("metrics")) }.Distinct().ToArray();
            if (paths.Any(path => string.IsNullOrEmpty(path) || !path.StartsWith("Assets/UI/AstellaPrismV4/UXRevision/Applied-", StringComparison.Ordinal)))
                throw new InvalidOperationException("Refinement requires reviewed UX copies.");
            string backup = Evidence + "/DetailBackup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            Directory.CreateDirectory(backup);
            var records = new List<DetailRecord>();
            var heading = AssetDatabase.LoadAssetAtPath<Font>(Fonts + "MaruBuri-SemiBold.ttf");
            var body = AssetDatabase.LoadAssetAtPath<Font>(Fonts + "Pretendard-Medium.ttf");
            foreach (var path in paths)
            {
                var record = new DetailRecord { asset = path, backup = backup + "/" + Path.GetFileName(path), beforeHash = Hash(path) };
                File.Copy(path, record.backup);
                var contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var before = AstellaPrismRemainingChecks.Capture(contents); string combat = CombatSnapshot(contents);
                    RepairMenuDetails(contents, heading, body); RepairResultRules(contents); NormalizeHeadings(TextTargets(contents), heading);
                    AstellaPrismRemainingChecks.AssertPreserved(before, contents, TextTargets(contents));
                    record.preservation = before.report; record.combatPreserved = combat == CombatSnapshot(contents);
                    if (!record.combatPreserved) throw new InvalidOperationException("Combat design changed during refinement.");
                    if (PrefabUtility.SaveAsPrefabAsset(contents, path) == null) throw new IOException("Unable to save refined copy.");
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
                record.afterHash = Hash(path); records.Add(record);
            }
            File.WriteAllText(Evidence + "/details-report.json", "[" + string.Join(",\n", records.Select(record => JsonUtility.ToJson(record, true))) + "]");
            Debug.Log("PRISM UX DETAILS PASS: " + records.Count + " revised copies; combat and original control contracts preserved.");
        }

        [MenuItem("Tools/Astella/UX Revision/Apply Screen Hierarchy (Preserved Copies)")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Stop Play and close Prefab Mode before authoring.");
            if (!string.Equals(Path.GetFullPath(Application.dataPath).TrimEnd('/', '\\'), @"C:\Astella\Astella\Assets", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Use the original Unity project.");
            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (scene.IsValid() && scene.isDirty) throw new InvalidOperationException("Preserve unsaved scene edits before authoring.");
            var profile = AssetDatabase.LoadAssetAtPath<RaidUIPresentation>(ProfilePath);
            if (profile?.hudPrefab == null) throw new InvalidOperationException("No active authored HUD.");
            if (AssetDatabase.GetAssetPath(profile.hudPrefab).Contains("/UXRevision/"))
                throw new InvalidOperationException("UX revision is already active; refine its reviewed copies explicitly.");
            var heading = AssetDatabase.LoadAssetAtPath<Font>(Fonts + "MaruBuri-SemiBold.ttf");
            var body = AssetDatabase.LoadAssetAtPath<Font>(Fonts + "Pretendard-Medium.ttf");
            if (heading == null || body == null) throw new InvalidOperationException("Approved fonts are missing.");
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            var report = new RevisionReport { utc = DateTime.UtcNow.ToString("O"),
                folder = "Assets/UI/AstellaPrismV4/UXRevision/Applied-" + stamp, backup = Evidence + "/Backup-" + stamp };
            Directory.CreateDirectory(report.backup);
            File.Copy(ProfilePath, report.backup + "/RaidUIPresentation.asset");
            File.Copy(ScenePath, report.backup + "/SampleScene.unity");
            string savedProfile = EditorJsonUtility.ToJson(profile);
            var sources = new List<string> { AssetDatabase.GetAssetPath(profile.hudPrefab) };
            sources.AddRange(profile.views.Where(view => view.prefab != null && RevisedViews.Contains(view.id)).Select(view => AssetDatabase.GetAssetPath(view.prefab)));
            if (profile.buildStationPrefab != null) sources.Add(AssetDatabase.GetAssetPath(profile.buildStationPrefab));
            if (profile.formButtonTemplate != null) sources.Add(AssetDatabase.GetAssetPath(profile.formButtonTemplate));
            if (profile.formTextTemplate != null) sources.Add(AssetDatabase.GetAssetPath(profile.formTextTemplate));
            var records = new List<AssetRecord>(); var disabled = new List<GameObject>(); GameObject instance = null;
            try
            {
                EnsureFolder(report.folder);
                foreach (string source in sources.Distinct())
                {
                    var record = new AssetRecord { source = source, copy = report.folder + "/" + Path.GetFileName(source), sourceHashBefore = Hash(source) };
                    records.Add(record);
                    if (!AssetDatabase.CopyAsset(source, record.copy)) throw new IOException("Unable to copy " + source);
                    var contents = PrefabUtility.LoadPrefabContents(record.copy);
                    try
                    {
                        var originalActions = contents.GetComponentsInChildren<RaidUIView>(true)
                            .SelectMany(view => view.actions.Select(action => view.viewId + ":" + action.id)).ToArray();
                        EnsureMenuUXControls(contents);
                        record.addedActions = contents.GetComponentsInChildren<RaidUIView>(true)
                            .SelectMany(view => view.actions.Select(action => view.viewId + ":" + action.id)).Except(originalActions).ToArray();
                        var before = AstellaPrismRemainingChecks.Capture(contents);
                        string combat = CombatSnapshot(contents);
                        var texts = TextTargets(contents);
                        if (source == AssetDatabase.GetAssetPath(profile.formTextTemplate) || source == AssetDatabase.GetAssetPath(profile.formButtonTemplate))
                            texts.UnionWith(contents.GetComponentsInChildren<Text>(true));
                        RedesignMenus(contents, heading, body);
                        RedesignChoices(contents, heading, body);
                        RedesignAuxiliary(contents, heading, body);
                        NormalizeHeadings(TextTargets(contents), heading);
                        if (source == AssetDatabase.GetAssetPath(profile.formButtonTemplate))
                            foreach (var item in contents.GetComponentsInChildren<RaidUIRepeatedItem>(true)) if (item.button != null) AuxButton(item.button, false);
                        var tooltipRects = new HashSet<RectTransform>(contents.GetComponentsInChildren<AstellaOrnateHUD>(true)
                            .Where(hud => hud.tooltipText != null).Select(hud => hud.tooltipText.rectTransform));
                        AstellaPrismRemainingChecks.AssertPreserved(before, contents, texts, tooltipRects);
                        record.preservation = before.report;
                        record.combatPreserved = combat == CombatSnapshot(contents);
                        if (!record.combatPreserved) throw new InvalidOperationException("Approved combat HUD changed.");
                        foreach (var station in contents.GetComponentsInChildren<RaidBuildStationView>(true)) station.ValidateBindings();
                        if (PrefabUtility.SaveAsPrefabAsset(contents, record.copy) == null) throw new IOException("Unable to save reviewed copy.");
                    }
                    finally { PrefabUtility.UnloadPrefabContents(contents); }
                    record.sourceHashAfter = Hash(source);
                    if (record.sourceHashAfter != record.sourceHashBefore) throw new IOException("Source changed: " + source);
                }
                profile.hudPrefab = AssetDatabase.LoadAssetAtPath<RaidUIRoot>(records[0].copy);
                foreach (var view in profile.views.Where(view => view.prefab != null))
                {
                    var record = records.FirstOrDefault(record => record.source == AssetDatabase.GetAssetPath(view.prefab));
                    if (record != null) view.prefab = AssetDatabase.LoadAssetAtPath<RaidUIView>(record.copy);
                }
                var stationRecord = records.FirstOrDefault(record => record.source == AssetDatabase.GetAssetPath(profile.buildStationPrefab));
                if (stationRecord != null) profile.buildStationPrefab = AssetDatabase.LoadAssetAtPath<RaidBuildStationView>(stationRecord.copy);
                var buttonRecord = records.FirstOrDefault(record => record.source == AssetDatabase.GetAssetPath(profile.formButtonTemplate));
                if (buttonRecord != null) profile.formButtonTemplate = AssetDatabase.LoadAssetAtPath<RaidUIRepeatedItem>(buttonRecord.copy);
                var textRecord = records.FirstOrDefault(record => record.source == AssetDatabase.GetAssetPath(profile.formTextTemplate));
                if (textRecord != null) profile.formTextTemplate = AssetDatabase.LoadAssetAtPath<RaidUIRepeatedItem>(textRecord.copy);
                RaidUIAuthoring.Validate(profile); EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile);
                if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                foreach (var old in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<RaidUIRoot>(true)))
                    if (old.gameObject.activeInHierarchy)
                    { disabled.Add(old.gameObject); old.gameObject.SetActive(false); PrefabUtility.RecordPrefabInstancePropertyModifications(old.gameObject); }
                instance = (GameObject)PrefabUtility.InstantiatePrefab(profile.hudPrefab.gameObject, scene);
                instance.name = "Astella_PrismV4_UXRevision"; instance.SetActive(true);
                AstellaOxaniumNumerals.ValidateNumericOutlines(); AstellaOxaniumNumerals.ValidatePlayerCaptions();
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Unable to save scene.");
                report.sceneSaved = report.passed = true;
                Debug.Log("PRISM UX REVISION PASS: " + records.Count + " copied assets; action contracts and combat design preserved.");
            }
            catch (Exception error)
            {
                report.error = error.ToString();
                EditorJsonUtility.FromJsonOverwrite(savedProfile, profile); EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile);
                if (!report.sceneSaved)
                {
                    if (instance != null) Object.DestroyImmediate(instance);
                    foreach (var old in disabled) if (old != null) { old.SetActive(true); PrefabUtility.RecordPrefabInstancePropertyModifications(old); }
                    if (disabled.Count > 0) EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                }
                throw;
            }
            finally
            {
                report.assets = records.ToArray(); Directory.CreateDirectory(Evidence);
                File.WriteAllText(Evidence + "/application-report.json", JsonUtility.ToJson(report, true));
            }
        }

        static HashSet<Text> TextTargets(GameObject root)
        {
            var result = new HashSet<Text>();
            foreach (var view in root.GetComponentsInChildren<RaidUIView>(true)) if (RevisedViews.Contains(view.viewId)) result.UnionWith(view.GetComponentsInChildren<Text>(true));
            foreach (var hud in root.GetComponentsInChildren<AstellaOrnateHUD>(true)) if (hud.tooltipText != null) result.Add(hud.tooltipText);
            foreach (var hud in root.GetComponentsInChildren<RaidUIRoot>(true))
                foreach (var binding in hud.presenters.Where(binding => binding.typeName?.Split(',')[0] == typeof(RoguelikeCardManager).FullName))
                    foreach (var reference in binding.references)
                    {
                        if (reference.target is Text text) result.Add(text);
                        if (reference.path == "cardSelectionModalRoot" && reference.target is GameObject modal)
                            result.UnionWith(modal.GetComponentsInChildren<Text>(true));
                    }
            foreach (var station in root.GetComponentsInChildren<RaidBuildStationView>(true)) result.UnionWith(station.GetComponentsInChildren<Text>(true));
            return result;
        }
        static string CombatSnapshot(GameObject root)
        {
            var combat = root.GetComponentInChildren<AstellaPrismHUDBridge>(true)?.presentationRoot;
            var tooltips = root.GetComponentsInChildren<AstellaOrnateHUD>(true).Where(hud => hud.tooltipRoot != null).Select(hud => hud.tooltipRoot.transform).ToArray();
            return combat == null ? "" : string.Join("\n", combat.GetComponentsInChildren<Transform>(true)
                .Where(transform => !tooltips.Any(tooltip => transform == tooltip || transform.IsChildOf(tooltip))).SelectMany(transform =>
                    new Object[] { transform.gameObject }.Concat(transform.GetComponents<Component>())).Select(value => value.GetType().FullName + "|" + EditorJsonUtility.ToJson(value)));
        }
        static void NormalizeHeadings(IEnumerable<Text> texts, Font heading)
        {
            foreach (var text in texts.Where(text => text.font == heading))
            {
                var outline = text.GetComponent<Outline>() ?? text.gameObject.AddComponent<Outline>();
                outline.enabled = true; outline.useGraphicAlpha = true;
                outline.effectColor = new Color(19 / 255f, 16 / 255f, 34 / 255f, 1);
                float distance = text.fontSize >= 30 ? 1.3f : 1;
                outline.effectDistance = new Vector2(distance, -distance);
                foreach (var style in text.GetComponents<RaidUIStyle>()) style.useTheme = false;
            }
        }
        static string Hash(string path) { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant(); }
        static void EnsureFolder(string path)
        { if (AssetDatabase.IsValidFolder(path)) return; string parent = path.Substring(0, path.LastIndexOf('/')); EnsureFolder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path)); }
    }
}
#endif
