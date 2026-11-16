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
using Object = UnityEngine.Object;

namespace RaidRogue.Editor
{
    // Explicit authoring migration: preserve previous painted assets and remove the retired menu subtree in copies.
    public static class AstellaTrainingRemoval
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string ProfilePath = "Assets/Resources/UI/RaidUIPresentation.asset";
        private const string Evidence = "Logs/TrainingRemoval";
        private static readonly HashSet<string> RetiredFields = new HashSet<string> { "_training", "_trainingAccess", "_trainingAction" };

        [Serializable] private sealed class Record
        {
            public string source, copy, sourceHashBefore, sourceHashAfter;
            public int removedRoots, removedReferences, removedActions, updatedHelpTexts;
        }
        [Serializable] private sealed class Report
        {
            public string utc, folder, backup, error;
            public bool passed, sceneSaved;
            public Record[] assets;
            public string boundary = "Explicit asset migration and binding validation; runtime menu input requires a separate Play check.";
        }

        [MenuItem("Tools/Astella/Remove Retired Training Room (Preserved Copies) %#F11")]
        public static void RemoveFromActiveUI()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Stop Play and close Prefab Mode before removing the training room.");
            if (!string.Equals(Path.GetFullPath(Application.dataPath).TrimEnd('/', '\\'), @"C:\Astella\Astella\Assets", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Run in the original Astella Unity project.");
            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (scene.IsValid() && scene.isDirty) throw new InvalidOperationException("Save existing scene edits first.");
            var profile = AssetDatabase.LoadAssetAtPath<RaidUIPresentation>(ProfilePath);
            var connection = profile?.views.FirstOrDefault(entry => entry.id == "connection");
            if (profile?.hudPrefab == null || connection?.prefab == null) throw new InvalidOperationException("Active HUD and connection view are required.");
            string hudSource = AssetDatabase.GetAssetPath(profile.hudPrefab);
            string connectionSource = AssetDatabase.GetAssetPath(connection.prefab);
            if (!HasRetiredReferences(profile.hudPrefab.gameObject) && !HasRetiredReferences(connection.prefab.gameObject))
            {
                ValidateActive();
                Debug.Log("TRAINING REMOVAL ALREADY APPLIED: active UI has no retired training bindings.");
                return;
            }
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            var report = new Report { utc = DateTime.UtcNow.ToString("O"),
                folder = "Assets/UI/AstellaPrismV4/WithoutTraining/Applied-" + stamp,
                backup = Evidence + "/Backup-" + stamp };
            Directory.CreateDirectory(report.backup);
            File.Copy(ProfilePath, report.backup + "/RaidUIPresentation.asset");
            File.Copy(ScenePath, report.backup + "/SampleScene.unity");
            string savedProfile = EditorJsonUtility.ToJson(profile);
            var records = new List<Record>();
            GameObject instance = null;
            var disabled = new List<GameObject>();
            try
            {
                Folder(report.folder);
                foreach (var pair in new[] { new[] { hudSource, "RaidHUD" }, new[] { connectionSource, "View-connection" } })
                {
                    var record = new Record { source = pair[0], copy = report.folder + "/" + pair[1] + ".prefab", sourceHashBefore = Hash(pair[0]) };
                    records.Add(record);
                    if (!AssetDatabase.CopyAsset(record.source, record.copy)) throw new IOException("Unable to preserve UI copy: " + record.source);
                    var contents = PrefabUtility.LoadPrefabContents(record.copy);
                    try
                    {
                        Prune(contents, record);
                        AssertNoRetiredReferences(contents);
                        RaidUIAuthoring.ValidateScripts(contents);
                        if (PrefabUtility.SaveAsPrefabAsset(contents, record.copy) == null) throw new IOException("Unable to save " + record.copy);
                    }
                    finally { PrefabUtility.UnloadPrefabContents(contents); }
                    record.sourceHashAfter = Hash(record.source);
                    if (record.sourceHashAfter != record.sourceHashBefore) throw new IOException("Original UI changed: " + record.source);
                }
                profile.hudPrefab = AssetDatabase.LoadAssetAtPath<RaidUIRoot>(records[0].copy);
                connection.prefab = AssetDatabase.LoadAssetAtPath<RaidUIView>(records[1].copy);
                ValidateProfile(profile);
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssetIfDirty(profile);
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
                instance.name = "Astella_PrismV4_WithoutTraining";
                instance.SetActive(true);
                AssertNoRetiredReferences(instance);
                ValidateActive();
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Unable to save the main scene.");
                report.sceneSaved = true; report.passed = true;
                Debug.Log("TRAINING REMOVAL PASS: preserved original HUD/menu; removed " + records.Sum(record => record.removedRoots) +
                    " training subtrees and " + records.Sum(record => record.removedReferences) + " retired references; updated " +
                    records.Sum(record => record.updatedHelpTexts) + " obsolete LAN help texts. " + report.folder);
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

        [MenuItem("Tools/Astella/Validate Training Room Removal")]
        public static void ValidateActive()
        {
            var profile = AssetDatabase.LoadAssetAtPath<RaidUIPresentation>(ProfilePath);
            ValidateProfile(profile);
            foreach (var root in Object.FindObjectsByType<RaidUIRoot>(FindObjectsSortMode.None)) AssertNoRetiredReferences(root.gameObject);
        }
        private static void ValidateProfile(RaidUIPresentation profile)
        {
            RaidUIAuthoring.Validate(profile);
            AssertNoRetiredReferences(profile.hudPrefab.gameObject);
            foreach (var view in profile.views) if (view.prefab != null) AssertNoRetiredReferences(view.prefab.gameObject);
        }

        private static IEnumerable<RaidUIView.Reference> References(GameObject root) =>
            root.GetComponentsInChildren<RaidUIView>(true).SelectMany(view => view.references)
                .Concat(root.GetComponentsInChildren<RaidUIRoot>(true).SelectMany(hud => hud.presenters).SelectMany(binding => binding.references));
        private static bool Retired(RaidUIView.Reference reference) => RetiredFields.Contains(reference.path.Split('/')[0]);
        private static bool HasRetiredReferences(GameObject root) => References(root).Any(Retired) ||
            root.GetComponentsInChildren<RaidUIView>(true).Any(view => view.actions.Any(action => action.id == "훈련소"));
        private static Transform TransformOf(Object target) => target is GameObject go ? go.transform : target is Component component ? component.transform : null;
        private static void Prune(GameObject root, Record record)
        {
            var roots = References(root).Where(reference => reference.path == "_training").Select(reference => TransformOf(reference.target))
                .Concat(root.GetComponentsInChildren<RaidUIView>(true).SelectMany(view => view.actions).Where(action => action.id == "훈련소")
                    .Select(action => action.button != null ? action.button.transform : null))
                .Where(node => node != null && node != root.transform && node.IsChildOf(root.transform)).Distinct().ToArray();
            bool Inside(Object target)
            {
                var node = TransformOf(target);
                return node != null && roots.Any(parent => node == parent || node.IsChildOf(parent));
            }
            foreach (var view in root.GetComponentsInChildren<RaidUIView>(true))
            {
                record.removedReferences += view.references.RemoveAll(reference => Retired(reference) || Inside(reference.target));
                record.removedActions += view.actions.RemoveAll(action => action.id == "훈련소" || Inside(action.button));
            }
            foreach (var binding in root.GetComponentsInChildren<RaidUIRoot>(true).SelectMany(hud => hud.presenters))
                record.removedReferences += binding.references.RemoveAll(reference => Retired(reference) || Inside(reference.target));
            foreach (var node in roots.Where(node => !roots.Any(parent => parent != node && node.IsChildOf(parent))).ToArray())
            { record.removedRoots++; Object.DestroyImmediate(node.gameObject); }
            foreach (var text in root.GetComponentsInChildren<UnityEngine.UI.Text>(true))
            {
                const string oldLanHelp = "같은 네트워크에서 직접 연결합니다.\nLAN 테스트는 진행 저장과 훈련소 해금에 포함되지 않습니다.";
                if (text.text != oldLanHelp) continue;
                text.text = "같은 네트워크에서 직접 연결합니다.\nLAN 테스트는 진행 저장에 포함되지 않습니다.";
                record.updatedHelpTexts++;
            }
        }
        private static void AssertNoRetiredReferences(GameObject root)
        {
            if (HasRetiredReferences(root)) throw new InvalidOperationException("Retired training UI binding remains: " + root.name);
            var stale = root.GetComponentsInChildren<UnityEngine.UI.Text>(true)
                .FirstOrDefault(text => text.text.Contains("훈련소") || text.text.Contains("훈련장"));
            if (stale != null)
                throw new InvalidOperationException("Retired training UI text remains: " + root.name + "/" +
                    AnimationUtility.CalculateTransformPath(stale.transform, root.transform) + " = " + stale.text);
        }
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
