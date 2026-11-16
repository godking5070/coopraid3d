#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace RaidRogue.Editor
{
    public static class RaidCutsceneAuthoring
    {
        private const string Folder = "Assets/Cutscenes/Examples";
        private const string LayoutPath = "Assets/Resources/UI/RaidLobbyLayout.asset";

        [MenuItem("Tools/RaidRogue/Cutscenes/Use Authored Start Positions in Arrival Example")]
        public static void UseAuthoredArrivalStartPositions()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Stop Play and compilation before repairing the arrival example.");
            const string path = Folder + "/ArrivalExample.playable";
            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(path);
            if (timeline == null) throw new InvalidOperationException("The existing arrival example is missing.");
            var clips = new System.Collections.Generic.List<AnimationPlayableAsset>();
            foreach (var track in timeline.GetOutputTracks())
                if (track is AnimationTrack)
                    foreach (var clip in track.GetClips())
                        if (clip.asset is AnimationPlayableAsset animation && animation.removeStartOffset && animation.clip != null &&
                            (AssetDatabase.GetAssetPath(animation.clip) == Folder + "/ArrivalExampleCamera.anim" ||
                             AssetDatabase.GetAssetPath(animation.clip) == Folder + "/ArrivalExampleBoss.anim")) clips.Add(animation);
            if (clips.Count == 0) { Debug.Log("Arrival example already retains its authored start positions."); return; }
            string backup = System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName,
                "Logs", "CutsceneRepairBackups", "arrival-offset-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
            System.IO.Directory.CreateDirectory(backup);
            System.IO.File.Copy(path, System.IO.Path.Combine(backup, "ArrivalExample.playable"));
            System.IO.File.Copy(path + ".meta", System.IO.Path.Combine(backup, "ArrivalExample.playable.meta"));
            foreach (var clip in clips)
            {
                Undo.RecordObject(clip, "Retain arrival clip start position");
                clip.removeStartOffset = false;
                EditorUtility.SetDirty(clip);
                AssetDatabase.SaveAssetIfDirty(clip);
            }
            Debug.Log("Arrival example retains its authored camera and boss coordinates. Animation keyframes and GUIDs are unchanged. Backup: " + backup);
        }

        [MenuItem("Tools/RaidRogue/Cutscenes/Set Up Boss Motion for Active Preview")]
        public static void SetUpBossMotionForActivePreview()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            { Debug.LogWarning("Stop Play Mode before setting up boss motion."); return; }
            foreach (var window in Resources.FindObjectsOfTypeAll<AnimationWindow>())
                if (window.recording)
                { Debug.LogWarning("Stop Animation recording before setting up the boss track."); return; }
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != Folder + "/CutscenePreview.unity")
            { Debug.LogWarning("Open the isolated CutscenePreview scene first."); return; }
            RaidStageCutscenePresentation presentation = null;
            var selected = Selection.activeGameObject;
            if (selected != null && selected.scene == scene)
                presentation = selected.GetComponentInParent<RaidStageCutscenePresentation>() ??
                    selected.GetComponentInChildren<RaidStageCutscenePresentation>(true);
            if (presentation == null)
                foreach (var root in scene.GetRootGameObjects())
                {
                    var candidate = root.activeInHierarchy ? root.GetComponentInChildren<RaidStageCutscenePresentation>(true) : null;
                    if (candidate == null) continue;
                    if (presentation != null)
                    { Debug.LogWarning("Select one Arrival or Defeat preview instance in the Hierarchy."); return; }
                    presentation = candidate;
                }
            var director = presentation != null ? presentation.GetComponent<PlayableDirector>() : null;
            var timeline = director != null ? director.playableAsset as TimelineAsset : null;
            var timelinePath = AssetDatabase.GetAssetPath(timeline);
            if (timeline == null || !timelinePath.StartsWith(Folder + "/", StringComparison.Ordinal) ||
                !timelinePath.EndsWith("Example.playable", StringComparison.Ordinal))
            { Debug.LogWarning("Select the Arrival/Defeat example preview with its existing Timeline."); return; }
            var prefabPath = timelinePath.Substring(0, timelinePath.Length - ".playable".Length) + ".prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
            { Debug.LogWarning("Matching cutscene prefab is missing: " + prefabPath); return; }

            var dummy = Array.Find(scene.GetRootGameObjects(), root => root.name == "Dummy boss at cutscene local origin");
            if (dummy == null)
            { Debug.LogWarning("The preview scene has no dummy boss. Existing assets were left unchanged."); return; }
            var dummyAnimator = dummy.GetComponent<Animator>();
            // Unity's missing-object wrappers must use Unity null comparison, not ??.
            if (dummyAnimator == null) dummyAnimator = Undo.AddComponent<Animator>(dummy);
            if (dummyAnimator == null)
            { Debug.LogError("Could not add an Animator to the preview dummy boss."); return; }
            AnimationTrack track = null;
            foreach (var output in timeline.GetOutputTracks())
                if (output is AnimationTrack animation && output.name == "Boss Motion")
                { track = animation; break; }
            if (track == null) track = timeline.CreateTrack<AnimationTrack>(null, "Boss Motion");
            var clipPath = timelinePath.Substring(0, timelinePath.Length - ".playable".Length) + "Boss.anim";
            bool hasAnyClip = false;
            foreach (var existing in track.GetClips()) { hasAnyClip = true; break; }
            if (!hasAnyClip)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                if (clip == null)
                {
                    bool arrival = timelinePath.EndsWith("ArrivalExample.playable", StringComparison.Ordinal);
                    clip = new AnimationClip { name = arrival ? "Arrival Boss Motion" : "Defeat Boss Motion", frameRate = 30 };
                    float from = arrival ? .75f : 1f, to = arrival ? 1f : .75f;
                    foreach (var axis in new[] { "x", "y", "z" })
                        Animate(clip, "m_LocalScale." + axis, from, to, 1f);
                    AssetDatabase.CreateAsset(clip, clipPath);
                }
                var shot = track.CreateClip(clip);
                if (shot.asset is AnimationPlayableAsset bossMotion) bossMotion.removeStartOffset = false;
                shot.start = timelinePath.EndsWith("ArrivalExample.playable", StringComparison.Ordinal) ? 1f : 0f;
                shot.duration = 1f;
            }
            director.SetGenericBinding(track, dummyAnimator);

            var prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var prefabPresentation = prefabRoot.GetComponent<RaidStageCutscenePresentation>();
                if (prefabPresentation == null) throw new InvalidOperationException("Cutscene prefab has no presentation component.");
                var serialized = new SerializedObject(prefabPresentation);
                var bindings = serialized.FindProperty("bindings");
                bool bound = false;
                for (int i = 0; i < bindings.arraySize; i++)
                    if (bindings.GetArrayElementAtIndex(i).FindPropertyRelative("track").objectReferenceValue == track)
                    {
                        bound = true;
                        if (bindings.GetArrayElementAtIndex(i).FindPropertyRelative("target").enumValueIndex != (int)RaidCutsceneBindingTarget.BossAnimator)
                            throw new InvalidOperationException("Boss Motion track already has a different binding; inspect it before changing.");
                        break;
                    }
                if (!bound)
                {
                    bindings.InsertArrayElementAtIndex(bindings.arraySize);
                    var binding = bindings.GetArrayElementAtIndex(bindings.arraySize - 1);
                    binding.FindPropertyRelative("track").objectReferenceValue = track;
                    binding.FindPropertyRelative("target").enumValueIndex = (int)RaidCutsceneBindingTarget.BossAnimator;
                    binding.FindPropertyRelative("explicitComponent").objectReferenceValue = null;
                    binding.FindPropertyRelative("explicitGameObject").objectReferenceValue = null;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(prefabRoot); }
            EditorUtility.SetDirty(timeline);
            EditorUtility.SetDirty(track);
            AssetDatabase.SaveAssetIfDirty(timeline);
            AssetDatabase.SaveAssetIfDirty(track);
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("Boss Motion is connected to the preview dummy and runtime boss. Edit " + clipPath +
                " in Timeline, then save the preview scene. Existing camera animation was preserved.");
        }

        [MenuItem("Tools/RaidRogue/Cutscenes/Create Editable Examples")]
        public static void CreateExamples()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("Stop Play Mode before creating cutscene assets."); return; }
            EnsureFolder("Assets/Cutscenes"); EnsureFolder(Folder);
            CreateExample("Arrival", 3f, new Vector3(-2, 5, -12), new Vector3(2, 4, -9));
            CreateExample("Defeat", 2.5f, new Vector3(2, 5, -10), new Vector3(0, 3, -7));
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Editable cutscene examples are in " + Folder + ". Existing assets were left unchanged.");
        }

        [MenuItem("Tools/RaidRogue/Cutscenes/Create Isolated Preview Scene")]
        public static void CreatePreviewScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("Stop Play Mode before creating the preview scene."); return; }
            CreateExamples();
            const string path = Folder + "/CutscenePreview.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null)
            { Debug.Log("Keeping existing preview scene: " + path); return; }
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var previous = EditorSceneManager.GetSceneManagerSetup();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            try
            {
                var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
                floor.name = "Preview floor (no gameplay)";
                SceneManager.MoveGameObjectToScene(floor, scene);
                floor.transform.localScale = new Vector3(3, 1, 3);
                var dummy = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                dummy.name = "Dummy boss at cutscene local origin";
                SceneManager.MoveGameObjectToScene(dummy, scene);
                dummy.transform.position = new Vector3(0, 1, 0);
                dummy.AddComponent<Animator>(); // Preview-only binding for optional BossAnimator tracks.
                foreach (var name in new[] { "Arrival", "Defeat" })
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/" + name + "Example.prefab");
                    if (prefab == null) continue;
                    var instance = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
                    if (instance != null)
                    {
                        instance.name = name + " preview (select its PlayableDirector)";
                        instance.transform.position = dummy.transform.position;
                        instance.SetActive(name == "Arrival");
                        ConfigureDirectAnimation(instance.GetComponent<RaidStageCutscenePresentation>());
                    }
                }
                EditorSceneManager.SaveScene(scene, path);
                Debug.Log("Isolated, save-free preview scene created: " + path);
            }
            finally
            {
                if (Application.isBatchMode) EditorSceneManager.CloseScene(scene, true);
                else EditorSceneManager.RestoreSceneManagerSetup(previous);
            }
        }

        [MenuItem("Tools/RaidRogue/Cutscenes/Prepare Direct .anim Editing in Preview")]
        public static void PrepareDirectAnimationEditing()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            { Debug.LogWarning("Stop Play Mode before editing cutscene animation."); return; }
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != Folder + "/CutscenePreview.unity")
            { Debug.LogWarning("Open Assets/Cutscenes/Examples/CutscenePreview.unity first."); return; }
            var selected = Selection.activeGameObject;
            var presentation = selected != null && selected.scene == scene
                ? selected.GetComponentInParent<RaidStageCutscenePresentation>() ??
                  selected.GetComponentInChildren<RaidStageCutscenePresentation>(true)
                : null;
            if (presentation == null)
            {
                foreach (var root in scene.GetRootGameObjects())
                {
                    var candidate = root.GetComponentInChildren<RaidStageCutscenePresentation>(true);
                    if (candidate == null || !candidate.gameObject.activeInHierarchy) continue;
                    if (presentation != null)
                    {
                        Debug.LogWarning("More than one cutscene preview is active. Select the Arrival or Defeat preview instance in the Hierarchy.");
                        return;
                    }
                    presentation = candidate;
                }
            }
            if (!ReleasePreviewForDirectRecording()) return;
            if (!ConfigureDirectAnimation(presentation)) return;
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("The preview camera now uses the same .anim as Timeline. Select Presentation Camera, then open Window > Animation > Animation. Turn Timeline Preview off while recording; save this preview scene with Ctrl+S.");
        }

        [MenuItem("Tools/RaidRogue/Cutscenes/Prepare Direct Boss .anim Editing in Preview")]
        public static void PrepareDirectBossAnimationEditing()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            { Debug.LogWarning("Stop Play Mode before editing boss animation."); return; }
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != Folder + "/CutscenePreview.unity")
            { Debug.LogWarning("Open the isolated CutscenePreview scene first."); return; }
            var selected = Selection.activeGameObject;
            var presentation = selected != null && selected.scene == scene
                ? selected.GetComponentInParent<RaidStageCutscenePresentation>() ??
                  selected.GetComponentInChildren<RaidStageCutscenePresentation>(true) : null;
            if (presentation == null)
            {
                foreach (var root in scene.GetRootGameObjects())
                {
                    var candidate = root.GetComponentInChildren<RaidStageCutscenePresentation>(true);
                    if (candidate == null || !candidate.gameObject.activeInHierarchy) continue;
                    if (presentation != null)
                    {
                        Debug.LogWarning("More than one cutscene preview is active. Select the Arrival or Defeat preview instance in the Hierarchy.");
                        return;
                    }
                    presentation = candidate;
                }
            }
            if (presentation == null)
            { Debug.LogWarning("Enable one Arrival or Defeat preview instance in the Hierarchy first."); return; }
            if (!presentation.gameObject.activeInHierarchy)
            { Debug.LogWarning("Enable the selected preview instance and disable the other preview first."); return; }
            var director = presentation.GetComponent<PlayableDirector>();
            var timeline = director != null ? director.playableAsset as TimelineAsset : null;
            AnimationTrack bossTrack = null;
            if (timeline != null)
                foreach (var output in timeline.GetOutputTracks())
                    if (output is AnimationTrack animation && output.name == "Boss Motion")
                    { bossTrack = animation; break; }
            AnimationClip clip = null;
            if (bossTrack != null)
                foreach (var shot in bossTrack.GetClips())
                    if (shot.asset is AnimationPlayableAsset animation && animation.clip != null)
                    { clip = animation.clip; break; }
            string clipPath = clip != null ? AssetDatabase.GetAssetPath(clip) : null;
            if (string.IsNullOrEmpty(clipPath) || !clipPath.EndsWith(".anim", StringComparison.OrdinalIgnoreCase))
            { Debug.LogWarning("Run Set Up Boss Motion for Active Preview first, or place an editable .anim clip on Boss Motion."); return; }
            var dummy = Array.Find(scene.GetRootGameObjects(), root => root.name == "Dummy boss at cutscene local origin");
            if (dummy == null)
            { Debug.LogWarning("The preview scene has no dummy boss."); return; }
            if (!ReleasePreviewForDirectRecording()) return;
            var animator = dummy.GetComponent<Animator>();
            // GetComponent may return a Unity missing-object wrapper in the Editor.
            if (animator == null) animator = Undo.AddComponent<Animator>(dummy);
            if (animator == null)
            { Debug.LogError("Could not add an Animator to the preview dummy boss."); return; }
            string controllerPath = System.IO.Path.ChangeExtension(clipPath, ".controller").Replace('\\', '/');
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
                controller = AnimatorController.CreateAnimatorControllerAtPathWithClip(controllerPath, clip);
            else if (Array.IndexOf(controller.animationClips, clip) < 0)
            { Debug.LogWarning("The existing boss preview Controller uses another clip; it was left unchanged: " + controllerPath); return; }
            Undo.RecordObject(animator, "Connect dummy boss to existing animation clip");
            animator.runtimeAnimatorController = controller;
            director.SetGenericBinding(bossTrack, animator);
            var camera = new SerializedObject(presentation).FindProperty("presentationCamera").objectReferenceValue as Camera;
            if (camera != null)
            { Undo.RecordObject(camera, "Show cutscene camera while editing boss motion"); camera.enabled = true; }
            Selection.activeGameObject = dummy;
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("Select a time in Animation, turn on the red record button, and scale/rotate the dummy boss in Scene view. Keys go directly to " + clipPath + ". Save when finished.");
        }

        private static bool ReleasePreviewForDirectRecording()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<AnimationWindow>())
                if (window.recording)
                { Debug.LogWarning("Stop the current Animation recording before switching to direct cutscene editing."); return false; }
            // Timeline uses its own driver. The parameterless overload only stops
            // Unity's dummy driver, so a Timeline preview can keep recording disabled.
            var timelineDriver = GetTimelinePreviewDriver();
            if (timelineDriver != null && AnimationMode.InAnimationMode(timelineDriver))
            {
                var windowType = Type.GetType("UnityEditor.Timeline.TimelineWindow, Unity.Timeline.Editor");
                var window = windowType?.GetProperty("instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null);
                var state = window != null ? windowType.GetProperty("state", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(window) : null;
                var preview = state?.GetType().GetProperty("previewMode", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (preview != null) preview.SetValue(state, false);
                else AnimationMode.StopAnimationMode(timelineDriver);
            }
            // Other editor previews can retain their own animation driver after
            // their window closes. Release those only for this explicit edit action.
            foreach (var driver in Resources.FindObjectsOfTypeAll<AnimationModeDriver>())
                if (driver != null && AnimationMode.InAnimationMode(driver))
                    AnimationMode.StopAnimationMode(driver);
            if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
            if (AnimationMode.InAnimationMode())
            { Debug.LogWarning("Another Animation preview still owns this scene. Turn off its Preview mode before recording."); return false; }
            return true;
        }

        private static bool ConfigureDirectAnimation(RaidStageCutscenePresentation presentation)
        {
            if (presentation == null || !presentation.gameObject.scene.IsValid())
            { Debug.LogWarning("Select the Arrival/Defeat preview instance in the Hierarchy."); return false; }
            var director = presentation.GetComponent<PlayableDirector>();
            var timeline = director != null ? director.playableAsset as TimelineAsset : null;
            var camera = new SerializedObject(presentation).FindProperty("presentationCamera").objectReferenceValue as Camera;
            var animator = camera != null ? camera.GetComponent<Animator>() : null;
            if (timeline == null || animator == null)
            { Debug.LogWarning("The preview instance needs its existing Timeline and Presentation Camera Animator."); return false; }
            AnimationClip clip = null;
            var configuredBindings = new SerializedObject(presentation).FindProperty("bindings");
            foreach (var track in timeline.GetOutputTracks())
            {
                if (!(track is AnimationTrack)) continue;
                bool isCameraTrack = director.GetGenericBinding(track) == animator;
                if (!isCameraTrack && configuredBindings != null)
                    for (int i = 0; i < configuredBindings.arraySize; i++)
                    {
                        var binding = configuredBindings.GetArrayElementAtIndex(i);
                        if (binding.FindPropertyRelative("track").objectReferenceValue != track ||
                            binding.FindPropertyRelative("target").enumValueIndex != (int)RaidCutsceneBindingTarget.CameraAnimator) continue;
                        isCameraTrack = true;
                        break;
                    }
                if (!isCameraTrack) continue;
                foreach (var shot in track.GetClips())
                    if (shot.asset is AnimationPlayableAsset animation && animation.clip != null)
                    { clip = animation.clip; break; }
                if (clip != null) break;
            }
            string clipPath = clip != null ? AssetDatabase.GetAssetPath(clip) : null;
            if (string.IsNullOrEmpty(clipPath) || !clipPath.EndsWith(".anim", StringComparison.OrdinalIgnoreCase))
            { Debug.LogWarning("The camera track must reference a saved, editable .anim clip."); return false; }
            string controllerPath = System.IO.Path.ChangeExtension(clipPath, ".controller").Replace('\\', '/');
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null && AssetDatabase.LoadMainAssetAtPath(controllerPath) != null)
            { Debug.LogWarning("An unrelated asset already occupies " + controllerPath); return false; }
            if (controller == null)
                controller = AnimatorController.CreateAnimatorControllerAtPathWithClip(controllerPath, clip);
            if (controller == null || Array.IndexOf(controller.animationClips, clip) < 0)
            { Debug.LogWarning("The preview Animator Controller does not reference " + clipPath + "; it was left unchanged."); return false; }
            Undo.RecordObject(animator, "Connect preview camera to existing animation clip");
            Undo.RecordObject(camera, "Show preview camera during animation editing");
            animator.runtimeAnimatorController = controller;
            camera.enabled = true;
            Selection.activeGameObject = camera.gameObject;
            Debug.Log("Direct Animation editing: " + clipPath + " on " + camera.gameObject.name + ". Runtime cutscene prefab was not changed.");
            return true;
        }

        [MenuItem("Tools/RaidRogue/Cutscenes/Diagnose Animation Recording")]
        public static void DiagnoseAnimationRecording()
        {
            var camera = Selection.activeGameObject;
            var animator = camera != null ? camera.GetComponent<Animator>() : null;
            var windows = Resources.FindObjectsOfTypeAll<AnimationWindow>();
            Debug.Log($"Animation recording: scene object={camera != null && camera.scene.IsValid()}, " +
                $"selected={camera?.name ?? "none"}, animator={animator != null}, " +
                $"controller={animator?.runtimeAnimatorController?.name ?? "none"}, " +
                $"animation windows={windows.Length}, animation mode={AnimationMode.InAnimationMode()}");
            foreach (var driver in Resources.FindObjectsOfTypeAll<AnimationModeDriver>())
                if (driver != null && AnimationMode.InAnimationMode(driver))
                    Debug.Log($"Active animation preview driver: {driver.name} ({driver.GetType().Name})");
            var timelineDriver = GetTimelinePreviewDriver();
            Debug.Log($"Timeline preview active={timelineDriver != null && AnimationMode.InAnimationMode(timelineDriver)}");
            foreach (var window in windows)
            {
                Debug.Log($"Animation window: clip={window.animationClip?.name ?? "none"}, " +
                    $"canPreview={window.canPreview}, canRecord={window.canRecord}, " +
                    $"previewing={window.previewing}, recording={window.recording}");
                var state = typeof(AnimationWindow).GetProperty("state", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window);
                var linked = state?.GetType().GetField("linkedWithSequencer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(state);
                var selection = state?.GetType().GetProperty("selection", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(state);
                Debug.Log($"Animation window binding: sequencerLinked={linked?.ToString() ?? "unknown"}, selection={selection?.GetType().Name ?? "none"}");
            }
        }

        private static AnimationModeDriver GetTimelinePreviewDriver()
        {
            var type = Type.GetType("UnityEditor.Timeline.WindowState, Unity.Timeline.Editor");
            return type?.GetProperty("previewDriver", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null) as AnimationModeDriver;
        }

        [MenuItem("Tools/RaidRogue/Cutscenes/Connect Examples to Stage 1 (Empty Slots Only)")]
        public static void ConnectStageOne()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("Stop Play Mode before changing the stage layout."); return; }
            CreateExamples();
            var layout = AssetDatabase.LoadAssetAtPath<RaidLobbyLayout>(LayoutPath);
            if (layout == null) { Debug.LogError("Missing " + LayoutPath + ". Generate the editable UI/lobby layout first."); return; }
            Undo.RecordObject(layout, "Connect cutscene examples to Stage 1");
            var entry = FindOrCreateArrival(layout, 1);
            var defeat = FindOrCreateDefeat(layout, 1);
            if (entry.presentationPrefab == null)
                entry.presentationPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/ArrivalExample.prefab")?.GetComponent<RaidStageCutscenePresentation>();
            if (defeat.presentationPrefab == null)
                defeat.presentationPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/DefeatExample.prefab")?.GetComponent<RaidStageCutscenePresentation>();
            EditorUtility.SetDirty(layout);
            AssetDatabase.SaveAssets();
            Debug.Log("Stage 1 uses the editable examples where its presentation slots were empty. Stage 2–6 slots remain available.");
        }

        [MenuItem("Tools/RaidRogue/Cutscenes/Validate Stage Bindings")]
        public static void Validate()
        {
            var layout = AssetDatabase.LoadAssetAtPath<RaidLobbyLayout>(LayoutPath);
            if (layout == null) { Debug.LogError("Missing " + LayoutPath); return; }
            int problems = 0;
            for (int stage = 1; stage <= 6; stage++)
            {
                var arrival = FindArrival(layout, stage);
                var defeat = FindDefeat(layout, stage);
                problems += Check(stage, "arrival", arrival?.presentationPrefab);
                problems += Check(stage, "defeat", defeat?.presentationPrefab);
            }
            Debug.Log(problems == 0 ? "Cutscene slots checked. Empty slots use the built-in fallback." :
                $"Cutscene validation found {problems} missing required reference(s). See warnings above.");
        }

        [MenuItem("Tools/RaidRogue/Cutscenes/Repair Missing Example Markers")]
        public static void RepairMissingExampleMarkers()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("Stop Play Mode before repairing examples."); return; }
            int repaired = 0;
            repaired += RepairMarker(Folder + "/ArrivalExample.playable", RaidCutsceneVisualEvent.RevealBoss, 1f);
            repaired += RepairMarker(Folder + "/DefeatExample.playable", RaidCutsceneVisualEvent.HideBoss, 1.8f);
            AssetDatabase.SaveAssets();
            Debug.Log($"Repaired {repaired} missing example visual marker(s); camera tracks and prefabs were preserved.");
        }

        private static int RepairMarker(string path, RaidCutsceneVisualEvent visualEvent, float time)
        {
            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(path);
            if (timeline == null) return 0;
            timeline.CreateMarkerTrack();
            var track = timeline.markerTrack;
            var serialized = new SerializedObject(track);
            var objects = serialized.FindProperty("m_Markers.m_Objects");
            if (objects == null) { Debug.LogWarning("Cannot inspect marker references in " + path); return 0; }
            for (int i = objects.arraySize - 1; i >= 0; i--)
                if (objects.GetArrayElementAtIndex(i).objectReferenceValue == null)
                {
                    int count = objects.arraySize;
                    objects.DeleteArrayElementAtIndex(i);
                    if (objects.arraySize == count) objects.DeleteArrayElementAtIndex(i);
                }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            foreach (var marker in track.GetMarkers())
                if (marker is RaidCutsceneVisualMarker visual && visual.visualEvent == visualEvent) return 0;
            var created = track.CreateMarker<RaidCutsceneVisualMarker>(time);
            created.visualEvent = visualEvent;
            EditorUtility.SetDirty(track);
            EditorUtility.SetDirty(timeline);
            return 1;
        }

        private static int Check(int stage, string kind, RaidStageCutscenePresentation prefab)
        {
            if (prefab == null) return 0;
            string error = prefab.ConfigurationError;
            if (error == null) return 0;
            Debug.LogWarning($"Stage {stage} {kind}: {error}. Runtime uses fallback.", prefab);
            return 1;
        }

        private static void CreateExample(string name, float seconds, Vector3 from, Vector3 to)
        {
            string stem = Folder + "/" + name + "Example";
            string prefabPath = stem + ".prefab";
            string timelinePath = stem + ".playable";
            string clipPath = stem + "Camera.anim";
            bool hasPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null;
            bool hasTimeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(timelinePath) != null;
            bool hasClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath) != null;
            if (hasPrefab || hasTimeline || hasClip)
            {
                if (!(hasPrefab && hasTimeline && hasClip)) Debug.LogWarning("Incomplete " + name + " example. Existing files are preserved; inspect " + stem);
                else Debug.Log("Keeping existing example assets for " + name + ".");
                return;
            }

            var clip = new AnimationClip { name = name + " Camera", legacy = false, frameRate = 30 };
            Animate(clip, "m_LocalPosition.x", from.x, to.x, seconds);
            Animate(clip, "m_LocalPosition.y", from.y, to.y, seconds);
            Animate(clip, "m_LocalPosition.z", from.z, to.z, seconds);
            AssetDatabase.CreateAsset(clip, clipPath);
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            timeline.name = name + " Example Timeline";
            AssetDatabase.CreateAsset(timeline, timelinePath);
            var track = timeline.CreateTrack<AnimationTrack>(null, "Presentation Camera Motion");
            var shot = track.CreateClip(clip);
            if (shot.asset is AnimationPlayableAsset cameraMotion) cameraMotion.removeStartOffset = false;
            shot.start = 0;
            shot.duration = seconds;
            timeline.CreateMarkerTrack();
            var visual = timeline.markerTrack.CreateMarker<RaidCutsceneVisualMarker>(name == "Arrival" ? 1f : 1.8f);
            visual.visualEvent = name == "Arrival" ? RaidCutsceneVisualEvent.RevealBoss : RaidCutsceneVisualEvent.HideBoss;

            var root = new GameObject(name + " Cutscene Example");
            try
            {
                var director = root.AddComponent<PlayableDirector>();
                director.playOnAwake = false;
                director.extrapolationMode = DirectorWrapMode.None;
                director.timeUpdateMode = DirectorUpdateMode.UnscaledGameTime;
                director.playableAsset = timeline;
                var cameraObject = new GameObject("Presentation Camera");
                cameraObject.transform.SetParent(root.transform, false);
                cameraObject.transform.localPosition = from;
                cameraObject.transform.localRotation = Quaternion.LookRotation(new Vector3(0, 1.5f, 0) - from);
                var camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.depth = 20;
                camera.fieldOfView = 55;
                var animator = cameraObject.AddComponent<Animator>();
                var presentation = root.AddComponent<RaidStageCutscenePresentation>();
                var serialized = new SerializedObject(presentation);
                serialized.FindProperty("director").objectReferenceValue = director;
                serialized.FindProperty("presentationCamera").objectReferenceValue = camera;
                serialized.FindProperty("cameraAnimator").objectReferenceValue = animator;
                var bindings = serialized.FindProperty("bindings");
                bindings.arraySize = 1;
                var binding = bindings.GetArrayElementAtIndex(0);
                binding.FindPropertyRelative("track").objectReferenceValue = track;
                binding.FindPropertyRelative("target").enumValueIndex = (int)RaidCutsceneBindingTarget.CameraAnimator;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                director.SetGenericBinding(track, animator);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void Animate(AnimationClip clip, string property, float from, float to, float seconds)
        {
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Transform), property),
                AnimationCurve.EaseInOut(0, from, seconds, to));
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }

        private static RaidArrivalStageLayout FindArrival(RaidLobbyLayout layout, int stage)
        {
            if (layout.bossArrivals != null) foreach (var item in layout.bossArrivals)
                if (item != null && item.stage == stage) return item;
            return null;
        }
        private static RaidDefeatStageLayout FindDefeat(RaidLobbyLayout layout, int stage)
        {
            if (layout.bossDefeats != null) foreach (var item in layout.bossDefeats)
                if (item != null && item.stage == stage) return item;
            return null;
        }
        private static RaidArrivalStageLayout FindOrCreateArrival(RaidLobbyLayout layout, int stage)
        {
            var found = FindArrival(layout, stage);
            if (found != null) return found;
            var entries = new System.Collections.Generic.List<RaidArrivalStageLayout>(layout.bossArrivals ?? Array.Empty<RaidArrivalStageLayout>());
            found = new RaidArrivalStageLayout { stage = stage };
            entries.Add(found); layout.bossArrivals = entries.ToArray(); return found;
        }
        private static RaidDefeatStageLayout FindOrCreateDefeat(RaidLobbyLayout layout, int stage)
        {
            var found = FindDefeat(layout, stage);
            if (found != null) return found;
            var entries = new System.Collections.Generic.List<RaidDefeatStageLayout>(layout.bossDefeats ?? Array.Empty<RaidDefeatStageLayout>());
            found = new RaidDefeatStageLayout { stage = stage };
            entries.Add(found); layout.bossDefeats = entries.ToArray(); return found;
        }
    }
}
#endif
