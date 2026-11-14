#if UNITY_EDITOR
using System;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RaidRogue.Editor
{
    // Explicit batch test for the ORIGINAL project only. Never runs on ordinary imports.
    public static class RaidUIGenerationChecks
    {
        private const string Key = "RaidRogue.UI.GenerationCheck.";

        [MenuItem("Tools/RaidRogue/UI/Check Settings Open and Cancel (Play Mode)")]
        public static async void CheckSettings()
        {
            if (!Application.isPlaying || LocalCameraSettingsUI.IsOpen)
            {
                Debug.LogWarning("Settings click check only runs in Play Mode from the main menu with Settings closed. For prefab organization, stop Play Mode and use Tools > RaidRogue > UI > Organize Active RaidHUD (Copy).");
                return;
            }
            var roots = UnityEngine.Object.FindObjectsByType<RaidUIRoot>(FindObjectsSortMode.None);
            var views = roots.Single().GetComponentsInChildren<RaidUIView>(true);
            var connection = views.Single(x => x.viewId == "connection");
            var settings = views.Single(x => x.viewId == "settings");
            var owner = (LocalCameraSettingsUI)settings.owner;
            var modal = settings.references.Single(x => x.path == "_modal").target as GameObject;
            var button = connection.actions.Single(x => x.id == "설정").button;
            try
            {
                if (EventSystem.current == null || button == null || !button.gameObject.activeInHierarchy)
                    throw new InvalidOperationException("Main menu Settings button or EventSystem unavailable.");
                var pointer = new PointerEventData(EventSystem.current)
                {
                    button = PointerEventData.InputButton.Left,
                    position = RectTransformUtility.WorldToScreenPoint(null, button.transform.TransformPoint(((RectTransform)button.transform).rect.center))
                };
                var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
                if (hits.Count == 0 || hits[0].gameObject.GetComponentInParent<Button>() != button)
                    throw new InvalidOperationException("Settings button is not the top pointer target.");
                pointer.pointerCurrentRaycast = hits[0];
                ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerDownHandler);
                ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerUpHandler);
                ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
                await System.Threading.Tasks.Task.Delay(300);
                if (!Application.isPlaying) return;
                if (!LocalCameraSettingsUI.IsOpen || modal == null || !modal.activeInHierarchy)
                    throw new InvalidOperationException("Settings did not open: open=" + LocalCameraSettingsUI.IsOpen +
                        ", runtime=" + (LocalSettingsRuntime.Instance != null) + ", owner=" + (owner != null) +
                        ", modal=" + (modal != null) + ", active=" + (modal != null && modal.activeInHierarchy) +
                        ", ending=" + RaidNetworkSession.Instance?.Ending + ", overlay=" + RaidSteamRuntime.Instance?.OverlayOpen +
                        ", invite=" + RaidSteamRuntime.Instance?.ConfirmInvite);
                hits.Clear(); EventSystem.current.RaycastAll(pointer, hits);
                if (hits.Count == 0 || !hits[0].gameObject.transform.IsChildOf(modal.transform))
                    throw new InvalidOperationException("Settings opened but is behind another UI at the menu button position.");
                settings.actions.Single(x => x.id == "Cancel").button.onClick.Invoke();
                if (LocalCameraSettingsUI.IsOpen || modal.activeSelf) throw new InvalidOperationException("Settings Cancel action did not close the modal.");
                Debug.Log("UI SETTINGS POINTER PASS: menu pointer routed to Settings, modal blocks the menu, and Cancel closed it; no settings were saved.");
            }
            finally { LocalCameraSettingsUI.CloseForSessionEnd(); }
        }

        [MenuItem("Tools/RaidRogue/UI/Validate Live Presentation (Play Mode)")]
        public static void ValidateLive()
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode before validating live UI bindings.");
            var roots = UnityEngine.Object.FindObjectsByType<RaidUIRoot>(FindObjectsSortMode.None);
            if (roots.Length != 1) throw new InvalidOperationException("Expected one active authored HUD, found " + roots.Length);
            RaidUIAuthoring.ValidateScripts(roots[0].gameObject);
            foreach (var binding in roots[0].presenters)
            {
                var type = Type.GetType(binding.typeName);
                var owner = binding.embeddedPresenter != null ? binding.embeddedPresenter : type != null ? UnityEngine.Object.FindFirstObjectByType(type) as MonoBehaviour : null;
                if (owner == null) throw new InvalidOperationException("Live UI presenter missing: " + binding.typeName);
            }
            foreach (var id in new[] { "connection", "character", "settings", "resurrection" })
            {
                var view = roots[0].GetComponentsInChildren<RaidUIView>(true).FirstOrDefault(x => x.viewId == id);
                if (view == null || view.owner == null) throw new InvalidOperationException("UI presenter was not rebound: " + id);
                if (view.actions.Any(x => x.button == null)) throw new InvalidOperationException("Missing restored button: " + id);
            }
            var connection = roots[0].GetComponentsInChildren<RaidUIView>(true).First(x => x.viewId == "connection");
            var settingsButton = connection.actions.FirstOrDefault(x => x.id == "설정")?.button;
            if (settingsButton != null && settingsButton.gameObject.activeInHierarchy)
            {
                if (EventSystem.current == null) throw new InvalidOperationException("UI EventSystem missing.");
                var rect = settingsButton.GetComponent<RectTransform>();
                var pointer = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center)) };
                var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
                if (hits.Count == 0 || hits[0].gameObject.GetComponentInParent<Button>() != settingsButton)
                    throw new InvalidOperationException("Settings button raycast blocked by: " + (hits.Count == 0 ? "no UI hit" : AnimationUtility.CalculateTransformPath(hits[0].gameObject.transform, roots[0].transform)));
            }
            Debug.Log("UI LIVE VALIDATION PASS: one HUD, all captured presenters available, four required views rebound, no missing scripts. This is not a combat or network test.");
        }

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use the explicit original-project batch command.");
            SessionState.SetString(Key + "phase", "enter");
            SessionState.SetFloat(Key + "deadline", (float)EditorApplication.timeSinceStartup + 180);
            SessionState.SetInt(Key + "errors", 0);
            EditorSceneManager.OpenScene(RaidRogue.EditorTools.RaidMultiplayerBuild.MainScene, OpenSceneMode.Single);
            Hook();
            EditorApplication.isPlaying = true;
        }

        [InitializeOnLoadMethod]
        private static void Hook()
        {
            if (!Application.isBatchMode || SessionState.GetString(Key + "phase", "").Length == 0) return;
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            Application.logMessageReceived -= OnLog; Application.logMessageReceived += OnLog;
        }
        private static void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
                SessionState.SetInt(Key + "errors", SessionState.GetInt(Key + "errors", 0) + 1);
        }
        private static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup > SessionState.GetFloat(Key + "deadline", 0)) throw new TimeoutException("UI generation smoke timed out.");
                string phase = SessionState.GetString(Key + "phase", "");
                if ((phase == "enter" || phase == "reenter") && EditorApplication.isPlaying && !EditorApplication.isCompiling)
                {
                    SessionState.SetString(Key + "phase", phase == "enter" ? "generate" : "verify");
                    SessionState.SetFloat(Key + "ready", (float)EditorApplication.timeSinceStartup + 2);
                }
                else if ((phase == "generate" || phase == "verify") && EditorApplication.timeSinceStartup > SessionState.GetFloat(Key + "ready", 0))
                {
                    if (SessionState.GetInt(Key + "errors", 0) != 0) throw new InvalidOperationException("Unity logged errors; see smoke log before continuing.");
                    if (phase == "generate") RaidUIAuthoring.Generate();
                    else
                    {
                        RaidUIAuthoring.ValidateActive();
                        ValidateLive();
                        Debug.Log("UI GENERATION RESTART VERIFIED: one authored HUD and required presenters rebound.");
                    }
                    SessionState.SetString(Key + "phase", phase == "generate" ? "stop" : "finish");
                    EditorApplication.isPlaying = false;
                }
                else if ((phase == "stop" || phase == "finish") && !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling)
                {
                    if (phase == "stop") { SessionState.SetString(Key + "phase", "reenter"); EditorApplication.isPlaying = true; }
                    else
                    {
                        if (SessionState.GetInt(Key + "errors", 0) != 0) throw new InvalidOperationException("Unity errors occurred during generation/restart.");
                        SessionState.SetString(Key + "phase", "");
                        Debug.Log("UI GENERATION SMOKE PASS: generated actual raid UI, exited Play, restarted Play and checked restored presenters. No scene was saved.");
                        EditorApplication.Exit(0);
                    }
                }
            }
            catch (Exception e)
            {
                SessionState.SetString(Key + "phase", "");
                EditorApplication.update -= Tick; Application.logMessageReceived -= OnLog;
                Debug.LogException(e); EditorApplication.Exit(1);
            }
        }
    }
}
#endif
