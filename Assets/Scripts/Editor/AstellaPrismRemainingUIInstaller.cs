#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Astella.PrismV4;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace RaidRogue.Editor
{
    /// <summary>Explicit authoring of preserved UI copies; never runs on import or Play transitions.</summary>
    public static class AstellaPrismRemainingUIInstaller
    {
        private const string Base = "Assets/UI/AstellaPrismV4";
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string ProfilePath = "Assets/Resources/UI/RaidUIPresentation.asset";
        private const string Evidence = "C:/Astella/prism-v4-remaining-ui-20261116";

        [Serializable] private sealed class AssetRecord
        {
            public string source, destination, sha256Before, sha256After;
            public int styledGraphics, bindingChecks;
            public string[] layoutChanges;
        }
        [Serializable] private sealed class Report
        {
            public string utc, folder, backup, previousHud, appliedHud, theme, error = "";
            public bool passed, sourcesPreserved, profileValidated, sceneSaved;
            public int styledGraphics, assetCount;
            public AssetRecord[] assets;
            public string boundary = "Editor authoring and binding preservation. Actual UI flow and GPU checks are separate evidence.";
        }

        [MenuItem("Tools/Astella/Prism V4/Apply Remaining Prism UI (Preserved Copies) %#F2")]
        public static void ApplyRemaining()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Stop Play and close Prefab Mode before authoring UI copies.");
            if (!string.Equals(Path.GetFullPath(Application.dataPath).TrimEnd('/', '\\'),
                @"C:\Astella\Astella\Assets", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Run only in the original Astella project.");
            var profile = RaidUIPresentation.Settings;
            if (profile?.hudPrefab == null) throw new InvalidOperationException("Active UI profile is missing.");
            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (scene.IsValid() && scene.isDirty) throw new InvalidOperationException("Save the existing scene edits first.");
            RaidUIAuthoring.Validate(profile);
            CleanupFailedDraft();
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            var report = new Report { utc = DateTime.UtcNow.ToString("O"),
                folder = Base + "/AppliedRemaining/Applied-" + stamp,
                backup = Evidence + "/Backup-" + stamp,
                previousHud = AssetDatabase.GetAssetPath(profile.hudPrefab) };
            Directory.CreateDirectory(report.backup);
            File.Copy(ProfilePath, report.backup + "/RaidUIPresentation.asset");
            File.Copy(ScenePath, report.backup + "/SampleScene.unity");
            string oldProfile = EditorJsonUtility.ToJson(profile);
            var records = new List<AssetRecord>();
            var copies = new Dictionary<string, string>();
            try
            {
                EnsureFolder(report.folder);
                var panelFace = Surface("panel-face", "Skills/slot-background");
                var panelFrame = Surface("panel-frame", "Skills/slot-frame-normal");
                var buttonFace = Surface("button-face", "Skills/slot-background");
                var buttonFrame = Surface("button-frame", "Skills/slot-frame-normal");
                var theme = profile.theme != null ? Object.Instantiate(profile.theme) : ScriptableObject.CreateInstance<RaidUITheme>();
                theme.name = "Prism Painted UI Theme";
                theme.panel = new Color32(19, 17, 34, 248);
                theme.text = new Color32(242, 237, 250, 255);
                theme.accent = new Color32(185, 157, 239, 255);
                theme.ready = Color.white; theme.unavailable = new Color32(136, 128, 155, 255);
                theme.selected = new Color32(196, 171, 250, 255); theme.locked = new Color32(94, 86, 112, 255);
                theme.panelSprite = panelFace; theme.buttonSprite = buttonFace; theme.borderSprite = panelFrame;
                theme.prismSurfaces = true;
                report.theme = report.folder + "/PrismTheme.asset";
                AssetDatabase.CreateAsset(theme, report.theme);

                string Copy(Object source, string name)
                {
                    if (source == null) return null;
                    string path = AssetDatabase.GetAssetPath(source);
                    if (copies.TryGetValue(path, out string prior)) return prior;
                    string destination = report.folder + "/" + name + ".prefab";
                    if (copies.ContainsValue(destination)) throw new InvalidOperationException("Duplicate UI copy name: " + name);
                    var record = new AssetRecord { source = path, destination = destination, sha256Before = Hash(path) };
                    if (!AssetDatabase.CopyAsset(path, destination)) throw new IOException("Copy failed: " + path);
                    records.Add(record); copies.Add(path, destination); return destination;
                }
                report.appliedHud = Copy(profile.hudPrefab, "RaidHUD");
                foreach (var view in profile.views) Copy(view.prefab, "View-" + view.id);
                var templateNames = new[] { "Skill", "Hotbar", "Party", "Resource", "Status", "FormButton", "FormText" };
                var templates = new[] { profile.skillTemplate, profile.hotbarTemplate, profile.partyTemplate,
                    profile.resourceTemplate, profile.statusTemplate, profile.formButtonTemplate, profile.formTextTemplate };
                for (int i = 0; i < templates.Length; i++) Copy(templates[i], "Template-" + templateNames[i]);
                var station = profile.buildStationPrefab != null ? profile.buildStationPrefab :
                    AssetDatabase.LoadAssetAtPath<RaidBuildStationView>("Assets/Resources/UI/ConstellationBuild.prefab");
                string stationCopy = Copy(station, "BuildStation");
                foreach (var record in records)
                {
                    var contents = PrefabUtility.LoadPrefabContents(record.destination);
                    try
                    {
                        var snapshot = AstellaPrismRemainingChecks.Capture(contents);
                        record.styledGraphics = AstellaPrismSurfaceStyler.Style(contents, theme, panelFace, panelFrame, buttonFace, buttonFrame);
                        AstellaPrismRemainingLayouts.Refine(contents);
                        AstellaPrismRemainingChecks.AssertPreserved(snapshot, contents);
                        record.bindingChecks = snapshot.report.checks;
                        File.WriteAllText(report.backup + "/checks-" + Path.GetFileNameWithoutExtension(record.destination) + ".json",
                            AstellaPrismRemainingChecks.ReportJson(snapshot));
                        RaidUIAuthoring.ValidateScripts(contents);
                        contents.GetComponent<RaidBuildStationView>()?.ValidateBindings();
                        var bridge = contents.GetComponentInChildren<AstellaPrismHUDBridge>(true);
                        if (bridge != null && bridge.presentationRoot != null) bridge.presentationRoot.SetActive(false);
                        record.layoutChanges = AstellaPrismRemainingLayouts.Report(contents);
                        if (PrefabUtility.SaveAsPrefabAsset(contents, record.destination) == null)
                            throw new IOException("Save failed: " + record.destination);
                        report.styledGraphics += record.styledGraphics;
                    }
                    finally { PrefabUtility.UnloadPrefabContents(contents); }
                    record.sha256After = Hash(record.source);
                    if (record.sha256After != record.sha256Before) throw new IOException("Source UI changed: " + record.source);
                }

                T Remap<T>(T original) where T : Component => original == null ? null :
                    AssetDatabase.LoadAssetAtPath<GameObject>(copies[AssetDatabase.GetAssetPath(original)]).GetComponent<T>();
                profile.hudPrefab = Remap(profile.hudPrefab);
                foreach (var view in profile.views) view.prefab = Remap(view.prefab);
                profile.skillTemplate = Remap(templates[0]); profile.hotbarTemplate = Remap(templates[1]);
                profile.partyTemplate = Remap(templates[2]); profile.resourceTemplate = Remap(templates[3]);
                profile.statusTemplate = Remap(templates[4]); profile.formButtonTemplate = Remap(templates[5]);
                profile.formTextTemplate = Remap(templates[6]); profile.theme = theme;
                profile.buildStationPrefab = stationCopy != null ? AssetDatabase.LoadAssetAtPath<RaidBuildStationView>(stationCopy) : null;
                RaidUIAuthoring.Validate(profile);
                profile.buildStationPrefab?.ValidateBindings();
                report.profileValidated = true;
                AstellaPrism1920Evidence.SetGameView1920();
                EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile);

                if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                foreach (var owner in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<RaidUIRoot>(true)))
                    if (owner.gameObject.activeInHierarchy)
                    { owner.gameObject.SetActive(false); PrefabUtility.RecordPrefabInstancePropertyModifications(owner.gameObject); }
                var hud = AssetDatabase.LoadAssetAtPath<RaidUIRoot>(report.appliedHud);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(hud.gameObject, scene);
                instance.name = "Astella_PrismV4_AllUI"; instance.SetActive(true);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Main scene save failed.");
                report.sceneSaved = true; report.sourcesPreserved = true; report.passed = true;
            }
            catch (Exception exception)
            {
                report.error = exception.ToString();
                EditorJsonUtility.FromJsonOverwrite(oldProfile, profile);
                EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile);
                throw;
            }
            finally
            {
                report.assets = records.ToArray(); report.assetCount = records.Count;
                Directory.CreateDirectory(Evidence);
                File.WriteAllText(Evidence + "/authoring-" + stamp + ".json", JsonUtility.ToJson(report, true));
                File.WriteAllText(Evidence + "/authoring-latest.json", JsonUtility.ToJson(report, true));
            }
            Debug.Log("PRISM REMAINING UI APPLIED: " + report.folder + " | " + report.assetCount +
                " preserved asset copies, " + report.styledGraphics + " styled graphics. " + Evidence + "/authoring-latest.json");
        }

        private static Sprite Surface(string name, string original)
        {
            const string folder = Base + "/Surfaces";
            EnsureFolder(folder);
            string path = folder + "/" + name + ".png";
            if (!File.Exists(path))
            {
                if (!AssetDatabase.CopyAsset(Base + "/Sprites/" + original + ".png", path)) throw new IOException("Surface copy failed: " + name);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) throw new InvalidOperationException("Surface importer missing.");
                importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
                importer.spriteBorder = new Vector4(32, 32, 32, 32); importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                var settings = new TextureImporterSettings(); importer.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.FullRect; importer.SetTextureSettings(settings);
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path) ?? throw new InvalidOperationException("Surface sprite missing: " + path);
        }

        private static void CleanupFailedDraft()
        {
            string latest = Evidence + "/authoring-latest.json";
            if (!File.Exists(latest)) return;
            var draft = JsonUtility.FromJson<Report>(File.ReadAllText(latest));
            if (draft == null || draft.passed || draft.sceneSaved || string.IsNullOrEmpty(draft.folder)) return;
            string prefix = Base + "/AppliedRemaining/Applied-";
            if (!draft.folder.StartsWith(prefix, StringComparison.Ordinal) || draft.folder.Contains("..")) return;
            if (AssetDatabase.GetDependencies(ProfilePath, true).Any(path => path.StartsWith(draft.folder + "/", StringComparison.Ordinal))) return;
            // Only the unique, unsuccessful draft created by this explicit command.
            // Its source assets and external diagnostic report remain preserved.
            if (AssetDatabase.IsValidFolder(draft.folder)) AssetDatabase.DeleteAsset(draft.folder);
        }

        [MenuItem("Tools/Astella/Prism V4/Refine Remaining Prism Draft")]
        public static void RefineAppliedDraft()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                !string.Equals(Path.GetFullPath(Application.dataPath).TrimEnd('/', '\\'),
                    @"C:\Astella\Astella\Assets", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Refine the current draft in the original editor, outside Play.");
            var report = JsonUtility.FromJson<Report>(File.ReadAllText(Evidence + "/authoring-latest.json"));
            var profile = RaidUIPresentation.Settings;
            if (report == null || !report.passed || AssetDatabase.GetAssetPath(profile.hudPrefab) != report.appliedHud ||
                !report.folder.StartsWith(Base + "/AppliedRemaining/Applied-", StringComparison.Ordinal) || report.folder.Contains(".."))
                throw new InvalidOperationException("No active, successful Remaining Prism draft is available.");
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            string backup = Evidence + "/Refine-" + stamp;
            Directory.CreateDirectory(backup);
            var face = AssetDatabase.LoadAssetAtPath<Sprite>(Base + "/Surfaces/panel-face.png");
            var frame = AssetDatabase.LoadAssetAtPath<Sprite>(Base + "/Surfaces/panel-frame.png");
            var buttonFace = AssetDatabase.LoadAssetAtPath<Sprite>(Base + "/Surfaces/button-face.png");
            var buttonFrame = AssetDatabase.LoadAssetAtPath<Sprite>(Base + "/Surfaces/button-frame.png");
            foreach (var record in report.assets)
            {
                if (!record.destination.StartsWith(report.folder + "/", StringComparison.Ordinal))
                    throw new InvalidOperationException("A draft asset is outside its preserved folder.");
                File.Copy(record.destination, backup + "/" + Path.GetFileName(record.destination));
                var root = PrefabUtility.LoadPrefabContents(record.destination);
                try
                {
                    var snapshot = AstellaPrismRemainingChecks.Capture(root);
                    AstellaPrismSurfaceStyler.Style(root, profile.theme, face, frame, buttonFace, buttonFrame);
                    AstellaPrismRemainingChecks.AssertPreserved(snapshot, root);
                    File.WriteAllText(backup + "/checks-" + Path.GetFileNameWithoutExtension(record.destination) + ".json",
                        AstellaPrismRemainingChecks.ReportJson(snapshot));
                    if (PrefabUtility.SaveAsPrefabAsset(root, record.destination) == null) throw new IOException(record.destination);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
                if (Hash(record.source) != record.sha256Before) throw new IOException("Original source changed: " + record.source);
            }
            File.WriteAllText(backup + "/complete.txt", "Authored Prism draft refinements saved; original source assets and binding contracts preserved.");
            Debug.Log("PRISM REMAINING DRAFT REFINED: " + backup);
        }

        private static string Hash(string path)
        {
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
        }
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = path.Substring(0, path.LastIndexOf('/'));
            EnsureFolder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
#endif
