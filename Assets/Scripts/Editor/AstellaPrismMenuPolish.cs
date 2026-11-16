#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace RaidRogue.Editor
{
    // Explicit authoring on preserved copies; presenters continue to own their existing actions/data.
    public static partial class AstellaPrismMenuPolish
    {
        const string MenuEvidence = "C:/Astella/remaining-ui-polish-20261116";
        const string MenuProfile = "Assets/Resources/UI/RaidUIPresentation.asset";
        const string MenuScene = "Assets/Scenes/SampleScene.unity";
        const string MenuFonts = "Assets/UI/AstellaPrismV4/Typography/Fonts/";
        static readonly HashSet<string> MenuViews = new HashSet<string>
            { "connection", "settings", "origin", "metrics", "resurrection", "wipe", "skill-reward" };
        [Serializable] sealed class MenuAsset
        {
            public string source, copy, sourceHashBefore, sourceHashAfter;
            public int authorizedTexts;
            public string[] addedSettingsControls, retainedSymbolFonts, textEdits;
            public AstellaPrismRemainingChecks.Report preservation;
            public bool combatPreserved;
        }
        [Serializable] sealed class MenuReport
        {
            public string utc, folder, backup, error;
            public bool passed, sceneSaved;
            public MenuAsset[] assets;
            public string boundary = "Authored layout/font/control contracts only; catalog is a design preview. Actual input/flow checks are recorded separately.";
        }

        [MenuItem("Tools/Astella/Preview Remaining UI Before Polish")]
        public static void PreviewBefore() => AstellaPrismRemainingPreview.RenderCatalogTo(MenuEvidence + "/before");
        [MenuItem("Tools/Astella/Preview Remaining UI After Polish")]
        public static void PreviewAfter() => AstellaPrismRemainingPreview.RenderCatalogTo(MenuEvidence + "/after");

        [Serializable] sealed class MenuRefinement
        {
            public string path, backup, beforeSha, afterSha;
            public int contractChecks;
            public bool combatPreserved;
        }

        [MenuItem("Tools/Astella/Refine Polished Menu Details")]
        public static void RefineMenuDetails()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Stop Play and close Prefab Mode before menu authoring.");
            if (!string.Equals(Path.GetFullPath(Application.dataPath).TrimEnd('/', '\\'), @"C:\Astella\Astella\Assets", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Use the original Unity project only.");
            var profile = AssetDatabase.LoadAssetAtPath<RaidUIPresentation>(MenuProfile);
            var paths = new[] { AssetDatabase.GetAssetPath(profile?.hudPrefab),
                AssetDatabase.GetAssetPath(profile?.views.FirstOrDefault(view => view.id == "settings")?.prefab) }.Distinct().ToArray();
            if (paths.Any(path => string.IsNullOrEmpty(path) || !path.StartsWith("Assets/UI/AstellaPrismV4/MenuPolish/Applied-", StringComparison.Ordinal)))
                throw new InvalidOperationException("Refinement requires the reviewed menu copies.");
            string backup = MenuEvidence + "/DetailBackup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            Directory.CreateDirectory(backup);
            var reports = new List<MenuRefinement>();
            foreach (var path in paths)
            {
                var record = new MenuRefinement { path = path, backup = backup + "/" + Path.GetFileName(path), beforeSha = MenuHash(path) };
                File.Copy(path, record.backup);
                var contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    string combat = MenuCombatSnapshot(contents);
                    var before = AstellaPrismRemainingChecks.Capture(contents);
                    PolishSettingsSliders(contents);
                    PolishAuxiliary(contents, AssetDatabase.LoadAssetAtPath<Font>(MenuFonts + "MaruBuri-SemiBold.ttf"),
                        AssetDatabase.LoadAssetAtPath<Font>(MenuFonts + "Pretendard-Medium.ttf"),
                        AssetDatabase.LoadAssetAtPath<Font>(MenuFonts + "Oxanium-Medium.ttf"));
                    var tooltipRects = new HashSet<RectTransform>(contents.GetComponentsInChildren<AstellaOrnateHUD>(true)
                        .Where(hud => hud.tooltipText != null).Select(hud => hud.tooltipText.rectTransform));
                    AstellaPrismRemainingChecks.AssertPreserved(before, contents, MenuTextTargets(contents), tooltipRects);
                    record.contractChecks = before.report.checks;
                    record.combatPreserved = combat == MenuCombatSnapshot(contents);
                    if (!record.combatPreserved) throw new InvalidOperationException("Combat design changed during detail refinement.");
                    if (PrefabUtility.SaveAsPrefabAsset(contents, path) == null) throw new IOException("Unable to save refined menu copy.");
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
                record.afterSha = MenuHash(path); reports.Add(record);
            }
            File.WriteAllText(MenuEvidence + "/detail-refinement.json", "[" + string.Join(",\n", reports.Select(report => JsonUtility.ToJson(report, true))) + "]");
            Debug.Log("PRISM MENU DETAILS PASS: " + reports.Count + " copied assets; original assets and combat design preserved.");
        }

        [MenuItem("Tools/Astella/Polish Remaining Menus (Preserved Copies)")]
        public static void ApplyMenuPolish()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Stop Play and close Prefab Mode before menu authoring.");
            if (!string.Equals(Path.GetFullPath(Application.dataPath).TrimEnd('/', '\\'), @"C:\Astella\Astella\Assets", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Use the original Unity project only.");
            var scene = SceneManager.GetSceneByPath(MenuScene);
            if (scene.IsValid() && scene.isDirty) throw new InvalidOperationException("Preserve existing unsaved scene edits first.");
            var profile = AssetDatabase.LoadAssetAtPath<RaidUIPresentation>(MenuProfile);
            var heading = AssetDatabase.LoadAssetAtPath<Font>(MenuFonts + "MaruBuri-SemiBold.ttf");
            var body = AssetDatabase.LoadAssetAtPath<Font>(MenuFonts + "Pretendard-Medium.ttf");
            var numeric = AssetDatabase.LoadAssetAtPath<Font>(MenuFonts + "Oxanium-Medium.ttf");
            if (profile?.hudPrefab == null || heading == null || body == null || numeric == null)
                throw new InvalidOperationException("Current HUD and approved static fonts are required.");
            if (AssetDatabase.GetAssetPath(profile.hudPrefab).Contains("/MenuPolish/"))
                throw new InvalidOperationException("This migration is already active. Refine reviewed copies explicitly instead of duplicating them.");
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            var report = new MenuReport { utc = DateTime.UtcNow.ToString("O"),
                folder = "Assets/UI/AstellaPrismV4/MenuPolish/Applied-" + stamp,
                backup = MenuEvidence + "/Backup-" + stamp };
            Directory.CreateDirectory(report.backup);
            File.Copy(MenuProfile, report.backup + "/RaidUIPresentation.asset");
            File.Copy(MenuScene, report.backup + "/SampleScene.unity");
            string savedProfile = EditorJsonUtility.ToJson(profile);
            var sources = new List<string> { AssetDatabase.GetAssetPath(profile.hudPrefab) };
            sources.AddRange(profile.views.Where(view => view.prefab != null && (MenuViews.Contains(view.id) || view.id == "character"))
                .Select(view => AssetDatabase.GetAssetPath(view.prefab)));
            if (profile.buildStationPrefab != null) sources.Add(AssetDatabase.GetAssetPath(profile.buildStationPrefab));
            var forms = new[] { profile.formButtonTemplate, profile.formTextTemplate }.Where(item => item != null).ToArray();
            var formPaths = new HashSet<string>(forms.Select(AssetDatabase.GetAssetPath));
            sources.AddRange(formPaths);
            var records = new List<MenuAsset>(); var disabled = new List<GameObject>(); GameObject instance = null;
            try
            {
                MenuFolder(report.folder);
                foreach (string source in sources.Distinct())
                {
                    var record = new MenuAsset { source = source, copy = report.folder + "/" + Path.GetFileName(source), sourceHashBefore = MenuHash(source) };
                    records.Add(record);
                    if (!AssetDatabase.CopyAsset(source, record.copy)) throw new IOException("Unable to preserve menu copy: " + source);
                    var contents = PrefabUtility.LoadPrefabContents(record.copy);
                    try
                    {
                        string combat = MenuCombatSnapshot(contents);
                        var settingsBefore = MenuSettingsKeys(contents);
                        EnsureSelectionControls(contents);
                        record.addedSettingsControls = MenuSettingsKeys(contents).Except(settingsBefore).ToArray();
                        // Structural additions use the preexisting runtime registry contracts. Freeze all actions/refs before styling.
                        var before = AstellaPrismRemainingChecks.Capture(contents);
                        var permitted = MenuTextTargets(contents);
                        if (formPaths.Contains(source)) permitted.UnionWith(contents.GetComponentsInChildren<Text>(true));
                        if (profile.formButtonTemplate != null && source == AssetDatabase.GetAssetPath(profile.formButtonTemplate))
                        {
                            var item = contents.GetComponent<RaidUIRepeatedItem>();
                            if (item?.label == null) throw new InvalidOperationException("Form button template label missing.");
                            var labelRect = item.label.rectTransform;
                            labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one;
                            labelRect.offsetMin = new Vector2(8, 4); labelRect.offsetMax = new Vector2(-8, -4);
                        }
                        var originalText = permitted.ToDictionary(text => text, text => text.text);
                        record.authorizedTexts = permitted.Count;
                        record.retainedSymbolFonts = MenuBody(permitted, contents, heading, body, numeric);
                        PolishSelection(contents, heading, body);
                        PolishAuxiliary(contents, heading, body, numeric);
                        record.textEdits = permitted.Where(text => originalText[text] != text.text).Select(text =>
                            AnimationUtility.CalculateTransformPath(text.transform, contents.transform) + " : " + originalText[text] + " -> " + text.text).ToArray();
                        var tooltipRects = new HashSet<RectTransform>(contents.GetComponentsInChildren<AstellaOrnateHUD>(true)
                            .Where(hud => hud.tooltipText != null).Select(hud => hud.tooltipText.rectTransform));
                        AstellaPrismRemainingChecks.AssertPreserved(before, contents, permitted, tooltipRects);
                        record.preservation = before.report;
                        record.combatPreserved = combat == MenuCombatSnapshot(contents);
                        if (!record.combatPreserved) throw new InvalidOperationException("Approved combat HUD changed while polishing menus.");
                        foreach (var station in contents.GetComponentsInChildren<RaidBuildStationView>(true)) station.ValidateBindings();
                        if (PrefabUtility.SaveAsPrefabAsset(contents, record.copy) == null) throw new IOException("Unable to save menu copy.");
                    }
                    finally { PrefabUtility.UnloadPrefabContents(contents); }
                    record.sourceHashAfter = MenuHash(source);
                    if (record.sourceHashBefore != record.sourceHashAfter) throw new IOException("Original menu changed: " + source);
                }
                profile.hudPrefab = AssetDatabase.LoadAssetAtPath<RaidUIRoot>(records[0].copy);
                foreach (var view in profile.views.Where(view => view.prefab != null))
                {
                    var replacement = records.FirstOrDefault(record => record.source == AssetDatabase.GetAssetPath(view.prefab));
                    if (replacement != null) view.prefab = AssetDatabase.LoadAssetAtPath<RaidUIView>(replacement.copy);
                }
                if (profile.buildStationPrefab != null)
                {
                    var replacement = records.FirstOrDefault(record => record.source == AssetDatabase.GetAssetPath(profile.buildStationPrefab));
                    if (replacement != null) profile.buildStationPrefab = AssetDatabase.LoadAssetAtPath<RaidBuildStationView>(replacement.copy);
                }
                var formButton = records.FirstOrDefault(record => profile.formButtonTemplate != null && record.source == AssetDatabase.GetAssetPath(profile.formButtonTemplate));
                var formText = records.FirstOrDefault(record => profile.formTextTemplate != null && record.source == AssetDatabase.GetAssetPath(profile.formTextTemplate));
                if (formButton != null) profile.formButtonTemplate = AssetDatabase.LoadAssetAtPath<RaidUIRepeatedItem>(formButton.copy);
                if (formText != null) profile.formTextTemplate = AssetDatabase.LoadAssetAtPath<RaidUIRepeatedItem>(formText.copy);
                RaidUIAuthoring.Validate(profile);
                EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile);
                if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(MenuScene, OpenSceneMode.Single);
                foreach (var root in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<RaidUIRoot>(true)))
                    if (root.gameObject.activeInHierarchy)
                    { disabled.Add(root.gameObject); root.gameObject.SetActive(false); PrefabUtility.RecordPrefabInstancePropertyModifications(root.gameObject); }
                var prefab = AssetDatabase.LoadAssetAtPath<RaidUIRoot>(records[0].copy);
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab.gameObject, scene);
                instance.name = "Astella_PrismV4_MenuPolish"; instance.SetActive(true);
                AstellaOxaniumNumerals.ValidateNumericOutlines();
                AstellaOxaniumNumerals.ValidatePlayerCaptions();
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Unable to save main scene.");
                report.sceneSaved = true; report.passed = true;
                Debug.Log("PRISM MENU POLISH PASS: " + records.Count + " preserved menu assets; combat HUD, data and input contracts preserved.");
            }
            catch (Exception error)
            {
                report.error = error.ToString();
                EditorJsonUtility.FromJsonOverwrite(savedProfile, profile); EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile);
                if (!report.sceneSaved)
                {
                    if (instance != null) Object.DestroyImmediate(instance);
                    foreach (var root in disabled) if (root != null) { root.SetActive(true); PrefabUtility.RecordPrefabInstancePropertyModifications(root); }
                }
                throw;
            }
            finally
            {
                report.assets = records.ToArray(); Directory.CreateDirectory(MenuEvidence);
                File.WriteAllText(MenuEvidence + "/application-report.json", JsonUtility.ToJson(report, true));
            }
        }

        static HashSet<Text> MenuTextTargets(GameObject root)
        {
            var result = new HashSet<Text>();
            foreach (var view in root.GetComponentsInChildren<RaidUIView>(true))
            {
                if (MenuViews.Contains(view.viewId)) result.UnionWith(view.GetComponentsInChildren<Text>(true));
                else if (view.viewId == "character")
                {
                    var tooltip = view.references.FirstOrDefault(reference => reference.path == "_tooltip")?.target as Text;
                    if (tooltip != null) result.Add(tooltip);
                }
            }
            foreach (var hud in root.GetComponentsInChildren<AstellaOrnateHUD>(true)) if (hud.tooltipText != null) result.Add(hud.tooltipText);
            foreach (var hud in root.GetComponentsInChildren<RaidUIRoot>(true))
                foreach (var binding in hud.presenters.Where(binding => binding.typeName?.Split(',')[0] == typeof(RoguelikeCardManager).FullName))
                    foreach (var reference in binding.references) if (reference.target is Text text) result.Add(text);
            foreach (var station in root.GetComponentsInChildren<RaidBuildStationView>(true))
            {
                result.UnionWith(station.GetComponentsInChildren<Text>(true));
                var icons = station.choices.Select(choice => choice.icon).Concat(station.members.SelectMany(member => member.icons)).Append(station.ownRoute);
                foreach (var icon in icons) if (icon?.fallback != null) result.Remove(icon.fallback);
            }
            return result;
        }
        static string[] MenuBody(HashSet<Text> texts, GameObject root, Font heading, Font body, Font numeric)
        {
            var retained = new List<string>();
            foreach (var text in texts)
            {
                if (text.font == heading || text.font == numeric) continue;
                string plain = Regex.Replace(text.text ?? "", "<[^>]+>", "");
                var missing = plain.Where(character => !char.IsWhiteSpace(character) && !body.HasCharacter(character)).Distinct().ToArray();
                if (missing.Length > 0)
                { retained.Add(AnimationUtility.CalculateTransformPath(text.transform, root.transform) + " : " + string.Join(",", missing.Select(character => "U+" + ((int)character).ToString("X4")))); continue; }
                text.font = body; text.fontStyle = FontStyle.Normal;
                foreach (var style in text.GetComponents<RaidUIStyle>()) style.useTheme = false;
            }
            return retained.ToArray();
        }
        static string MenuCombatSnapshot(GameObject root)
        {
            var bridge = root.GetComponentInChildren<AstellaPrismHUDBridge>(true);
            var combat = bridge?.presentationRoot;
            var tooltips = root.GetComponentsInChildren<AstellaOrnateHUD>(true).Where(hud => hud.tooltipRoot != null).Select(hud => hud.tooltipRoot.transform).ToArray();
            return combat == null ? "" : string.Join("\n", combat.GetComponentsInChildren<Transform>(true)
                .Where(transform => !tooltips.Any(tooltip => transform == tooltip || transform.IsChildOf(tooltip))).SelectMany(transform =>
                new Object[] { transform.gameObject }.Concat(transform.GetComponents<Component>())).Select(value => value.GetType().FullName + "|" + EditorJsonUtility.ToJson(value)));
        }
        static string[] MenuSettingsKeys(GameObject root) => root.GetComponentsInChildren<RaidUIView>(true).Where(view => view.viewId == "settings")
            .SelectMany(view => view.actions.Select(action => "action:" + action.id).Concat(view.GetComponentsInChildren<RaidUIForm>(true)
                .SelectMany(form => form.controls.Select(control => form.id + ":" + control.id)))).ToArray();
        static string MenuHash(string path)
        { using var hash = SHA256.Create(); return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant(); }
        static void MenuFolder(string path)
        { if (AssetDatabase.IsValidFolder(path)) return; string parent = path.Substring(0, path.LastIndexOf('/')); MenuFolder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path)); }
    }
}
#endif
