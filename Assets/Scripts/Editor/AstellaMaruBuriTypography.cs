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
    // Explicit authoring only. Runtime presenters retain their text/value/listener ownership.
    public static class AstellaMaruBuriTypography
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string ProfilePath = "Assets/Resources/UI/RaidUIPresentation.asset";
        private const string FontPath = "Assets/UI/AstellaPrismV4/Typography/Fonts/MaruBuri-SemiBold.ttf";
        private const string Evidence = "Logs/MaruBuriTypography";
        private static readonly Color Ink = new Color(19 / 255f, 16 / 255f, 34 / 255f, 1);
        private static readonly HashSet<string> HeadingNames = new HashSet<string>
            { "Prism Intro Title", "Astella_Title", "ModalTitle", "Modal_HeaderText", "Card_TitleText", "BossClear_Text" };
        private static readonly HashSet<string> HeadingFields = new HashSet<string>
            { "_menuTitle", "_heading", "_resultTitle", "roleModalTitleText" };

        [Serializable] private sealed class Target
        { public string path, text, previousFont, font; public int size; public Vector2 outline; }
        [Serializable] private sealed class Record
        {
            public string source, copy, sourceHashBefore, sourceHashAfter;
            public int preservedOtherTexts;
            public Target[] targets;
        }
        [Serializable] private sealed class Report
        {
            public string utc, folder, backup, font, error;
            public bool passed, sceneSaved;
            public Record[] assets;
            public string boundary = "Heading/boss/cast fonts and outlines only. Numeric, cooldown, key and body fonts remain pending; Play rendering needs separate verification.";
        }

        [MenuItem("Tools/Astella/Apply MaruBuri Headings (Preserved Copies)")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Stop Play and close Prefab Mode before authoring typography.");
            if (!string.Equals(Path.GetFullPath(Application.dataPath).TrimEnd('/', '\\'), @"C:\Astella\Astella\Assets", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Run only in the original Astella Unity project.");
            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (scene.IsValid() && scene.isDirty) throw new InvalidOperationException("Save existing scene edits before typography authoring.");
            var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (font == null) throw new InvalidOperationException("Import the approved static MaruBuri SemiBold font first: " + FontPath);
            var profile = AssetDatabase.LoadAssetAtPath<RaidUIPresentation>(ProfilePath);
            if (profile?.hudPrefab == null) throw new InvalidOperationException("An active authored HUD is required.");
            var sources = new List<string> { AssetDatabase.GetAssetPath(profile.hudPrefab) };
            sources.AddRange(profile.views.Where(entry => entry.prefab != null && Headings(entry.prefab.gameObject).Length > 0)
                .Select(entry => AssetDatabase.GetAssetPath(entry.prefab)).Distinct());
            if (sources.All(path => Headings(AssetDatabase.LoadAssetAtPath<GameObject>(path)).All(text => IsApplied(text, font))))
            { ValidateActive(); Debug.Log("MARUBURI TYPOGRAPHY ALREADY APPLIED: authored headings preserved."); return; }

            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            var report = new Report { utc = DateTime.UtcNow.ToString("O"),
                folder = "Assets/UI/AstellaPrismV4/Typography/Applied-" + stamp,
                backup = Evidence + "/Backup-" + stamp, font = FontPath };
            Directory.CreateDirectory(report.backup);
            File.Copy(ProfilePath, report.backup + "/RaidUIPresentation.asset");
            File.Copy(ScenePath, report.backup + "/SampleScene.unity");
            string savedProfile = EditorJsonUtility.ToJson(profile);
            var records = new List<Record>();
            var disabled = new List<GameObject>();
            GameObject instance = null;
            try
            {
                Folder(report.folder);
                foreach (var source in sources.Distinct())
                {
                    var record = new Record { source = source,
                        copy = report.folder + "/" + Path.GetFileName(source), sourceHashBefore = Hash(source) };
                    records.Add(record);
                    if (!AssetDatabase.CopyAsset(source, record.copy)) throw new IOException("Could not preserve typography copy: " + source);
                    var contents = PrefabUtility.LoadPrefabContents(record.copy);
                    try
                    {
                        var targets = Headings(contents);
                        var otherTexts = contents.GetComponentsInChildren<Text>(true).Except(targets).ToArray();
                        string otherBefore = UnchangedTexts(otherTexts, contents.transform);
                        string geometryBefore = Geometry(contents);
                        record.targets = targets.Select(text => ApplyText(text, font, contents.transform)).ToArray();
                        if (UnchangedTexts(otherTexts, contents.transform) != otherBefore || Geometry(contents) != geometryBefore)
                            throw new InvalidOperationException("Typography must preserve other text and all authored RectTransforms.");
                        record.preservedOtherTexts = otherTexts.Length;
                        ValidateRoot(contents, font);
                        RaidUIAuthoring.ValidateScripts(contents);
                        if (PrefabUtility.SaveAsPrefabAsset(contents, record.copy) == null) throw new IOException("Could not save " + record.copy);
                    }
                    finally { PrefabUtility.UnloadPrefabContents(contents); }
                    record.sourceHashAfter = Hash(source);
                    if (record.sourceHashAfter != record.sourceHashBefore) throw new IOException("Prior authored source changed: " + source);
                }
                profile.hudPrefab = AssetDatabase.LoadAssetAtPath<RaidUIRoot>(records[0].copy);
                foreach (var entry in profile.views.Where(entry => entry.prefab != null))
                {
                    string source = AssetDatabase.GetAssetPath(entry.prefab);
                    var replacement = records.FirstOrDefault(record => record.source == source);
                    if (replacement != null) entry.prefab = AssetDatabase.LoadAssetAtPath<RaidUIView>(replacement.copy);
                }
                RaidUIAuthoring.Validate(profile);
                EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile);
                if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                foreach (var root in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<RaidUIRoot>(true)))
                    if (root.gameObject.activeInHierarchy)
                    {
                        disabled.Add(root.gameObject); root.gameObject.SetActive(false);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(root.gameObject);
                    }
                // Resolve again after opening the scene, which can unload unused prefab handles.
                var prefab = AssetDatabase.LoadAssetAtPath<RaidUIRoot>(records[0].copy);
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab.gameObject, scene);
                instance.name = "Astella_PrismV4_MaruBuri"; instance.SetActive(true);
                ValidateActive();
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save the main scene.");
                report.sceneSaved = true; report.passed = true;
                Debug.Log("MARUBURI TYPOGRAPHY PASS: " + records.Sum(record => record.targets.Length) +
                    " headings; numeric/body fonts and authored geometry preserved. " + report.folder);
            }
            catch (Exception error)
            {
                report.error = error.ToString();
                EditorJsonUtility.FromJsonOverwrite(savedProfile, profile);
                EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile);
                if (!report.sceneSaved)
                {
                    if (instance != null) Object.DestroyImmediate(instance);
                    foreach (var root in disabled) if (root != null) { root.SetActive(true); PrefabUtility.RecordPrefabInstancePropertyModifications(root); }
                }
                throw;
            }
            finally
            {
                report.assets = records.ToArray();
                Directory.CreateDirectory(Evidence);
                File.WriteAllText(Evidence + "/report.json", JsonUtility.ToJson(report, true));
            }
        }

        [MenuItem("Tools/Astella/Validate MaruBuri Typography")]
        public static void ValidateActive()
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            var profile = AssetDatabase.LoadAssetAtPath<RaidUIPresentation>(ProfilePath);
            if (font == null || profile?.hudPrefab == null) throw new InvalidOperationException("Approved typography assets missing.");
            RaidUIAuthoring.Validate(profile);
            ValidateRoot(profile.hudPrefab.gameObject, font);
            foreach (var entry in profile.views) if (entry.prefab != null) ValidateRoot(entry.prefab.gameObject, font);
            foreach (var root in Object.FindObjectsByType<RaidUIRoot>(FindObjectsSortMode.None)) ValidateRoot(root.gameObject, font);
        }

        private static Text[] Headings(GameObject root)
        {
            if (root == null) return Array.Empty<Text>();
            var result = new HashSet<Text>();
            void Add(Text text) { if (text != null && (text.transform == root.transform || text.transform.IsChildOf(root.transform))) result.Add(text); }
            foreach (var bridge in root.GetComponentsInChildren<AstellaPrismHUDBridge>(true))
            { Add(bridge.bossView?.nameText); Add(bridge.bossView?.castNameText); }
            var references = root.GetComponentsInChildren<RaidUIView>(true).SelectMany(view => view.references)
                .Concat(root.GetComponentsInChildren<RaidUIRoot>(true).SelectMany(hud => hud.presenters).SelectMany(binding => binding.references));
            foreach (var reference in references)
                if (reference.target is Text text && (HeadingFields.Contains(reference.path) || reference.path == "_title" && text.fontSize >= 24)) Add(text);
            foreach (var text in root.GetComponentsInChildren<Text>(true)) if (HeadingNames.Contains(text.name)) Add(text);
            foreach (var connection in root.GetComponentsInChildren<RaidUIView>(true).Where(view => view.viewId == "connection"))
                foreach (var text in connection.GetComponentsInChildren<Text>(true))
                    if (text.text == "방 만들기" || text.text == "들어가기") Add(text);
            return result.OrderBy(text => AnimationUtility.CalculateTransformPath(text.transform, root.transform), StringComparer.Ordinal).ToArray();
        }

        private static Target ApplyText(Text text, Font font, Transform root)
        {
            var target = new Target { path = AnimationUtility.CalculateTransformPath(text.transform, root), text = text.text,
                previousFont = AssetDatabase.GetAssetPath(text.font), font = FontPath, size = text.fontSize };
            ValidateGlyphs(text, font);
            text.font = font; text.fontStyle = FontStyle.Normal;
            foreach (var style in text.GetComponents<RaidUIStyle>()) style.useTheme = false;
            var outline = text.GetComponent<Outline>() ?? text.gameObject.AddComponent<Outline>();
            outline.enabled = true; outline.effectColor = Ink; outline.useGraphicAlpha = true;
            float distance = text.fontSize >= 30 ? 1.3f : 1f;
            outline.effectDistance = target.outline = new Vector2(distance, -distance);
            return target;
        }
        private static bool IsApplied(Text text, Font font)
        {
            var outline = text.GetComponent<Outline>();
            float distance = text.fontSize >= 30 ? 1.3f : 1f;
            return text.font == font && text.fontStyle == FontStyle.Normal && outline != null && outline.enabled &&
                outline.effectColor == Ink && outline.effectDistance == new Vector2(distance, -distance) &&
                !text.GetComponents<RaidUIStyle>().Any(style => style.useTheme);
        }
        private static void ValidateRoot(GameObject root, Font font)
        {
            foreach (var text in Headings(root))
            {
                if (!IsApplied(text, font)) throw new InvalidOperationException("Typography mismatch: " + root.name + "/" + AnimationUtility.CalculateTransformPath(text.transform, root.transform));
                if (text.GetComponents<Outline>().Length != 1) throw new InvalidOperationException("Duplicate heading outline: " + text.name);
                ValidateGlyphs(text, font);
            }
        }
        private static void ValidateGlyphs(Text text, Font font)
        {
            string plain = text.supportRichText ? Regex.Replace(text.text ?? "", "<[^>]+>", "") : text.text ?? "";
            foreach (char character in (plain + "마루 부리 마을 수호 석상 뒤틀린 집행").Distinct())
                if (!char.IsWhiteSpace(character) && !font.HasCharacter(character))
                    throw new InvalidOperationException("Approved font lacks U+" + ((int)character).ToString("X4") + " in heading " + text.name);
        }
        private static string UnchangedTexts(IEnumerable<Text> texts, Transform root) => string.Join("\n", texts.Select(text =>
            AnimationUtility.CalculateTransformPath(text.transform, root) + "|" + AssetDatabase.GetAssetPath(text.font) + "|" +
            text.fontStyle + "|" + text.fontSize + "|" + text.text + "|" + text.color + "|" + text.alignment + "|" +
            text.resizeTextForBestFit + "|" + text.resizeTextMinSize + "|" + text.resizeTextMaxSize));
        private static string Geometry(GameObject root) => string.Join("\n", root.GetComponentsInChildren<RectTransform>(true).Select(rect =>
            AnimationUtility.CalculateTransformPath(rect, root.transform) + "|" + EditorJsonUtility.ToJson(rect)));
        private static string Hash(string path)
        {
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
        }
        private static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = path.Substring(0, path.LastIndexOf('/'));
            Folder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
#endif
