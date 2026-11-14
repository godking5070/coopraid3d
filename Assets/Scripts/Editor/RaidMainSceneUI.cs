#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RaidRogue.Editor
{
    [InitializeOnLoad]
    public static class RaidMainSceneUI
    {
        private const string MainScene = "Assets/Scenes/SampleScene.unity";
        static RaidMainSceneUI()
        {
            if (SessionState.GetBool("RaidMainSceneUI.Validate", false))
                EditorApplication.playModeStateChanged += OnPlayState;
        }

        [MenuItem("Tools/RaidRogue/Apply UI to Main Scene", false, 2)]
        public static void Install()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory("Assets/Resources/RaidUI");
            const string whitePath = "Assets/Resources/RaidUI/White.png";
            if (!File.Exists(whitePath))
            {
                var texture = new Texture2D(2, 2);
                texture.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
                texture.Apply();
                File.WriteAllBytes(whitePath, texture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(texture);
            }
            AssetDatabase.ImportAsset(whitePath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(whitePath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();

            Scene scene = EditorSceneManager.OpenScene(MainScene);
            var canvas = GameObject.Find("RaidRogue_HUD_Canvas");
            if (canvas == null) throw new InvalidOperationException("Main scene raid HUD is missing.");
            var theme = canvas.GetComponent<RaidHudTheme>() ?? canvas.AddComponent<RaidHudTheme>();
            theme.Apply();
            theme.SendMessage("LateUpdate");
            EditorUtility.SetDirty(theme);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("RAID_MAIN_UI_SAVED: " + MainScene);
        }

        // Runs the saved main scene through real Play mode, including a skill button and live HP updates.
        public static void InstallAndValidate()
        {
            Install();
            EditorSceneManager.OpenScene(MainScene);
            SessionState.SetBool("RaidMainSceneUI.Validate", true);
            EditorApplication.playModeStateChanged += OnPlayState;
            EditorApplication.isPlaying = true;
        }

        private static void OnPlayState(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            SessionState.SetBool("RaidMainSceneUI.Validate", false);
            EditorApplication.playModeStateChanged -= OnPlayState;
            double start = EditorApplication.timeSinceStartup;
            bool skillUsed = false;
            EditorApplication.CallbackFunction tick = null;
            tick = () =>
            {
                if (EditorApplication.timeSinceStartup - start < 1) return;
                try
                {
                    var theme = UnityEngine.Object.FindFirstObjectByType<RaidHudTheme>();
                    var player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
                    var hotbar = UnityEngine.Object.FindFirstObjectByType<SkillHotbarUI>();
                    if (theme == null || player == null || hotbar == null) throw new Exception("Main scene bindings missing.");
                    if (!skillUsed)
                    {
                        hotbar.slots[2].slotButton.onClick.Invoke();
                        if (hotbar.combatController.Skill3CooldownRemaining <= 0) throw new Exception("Defensive skill button did not activate.");
                        foreach (var bot in UnityEngine.Object.FindObjectsByType<PartyBotAI>(FindObjectsSortMode.None)) bot.enabled = false;
                        player.currentHp = player.maxHp * .5f;
                        skillUsed = true;
                        start = EditorApplication.timeSinceStartup;
                        return;
                    }
                    var value = theme.transform.Find("Theme_Party/Value0").GetComponent<Text>();
                    if (value.text != "50%") throw new Exception("Live party HP did not update: " + value.text);
                    if (!hotbar.slots[2].cooldownNumberText.gameObject.activeSelf) throw new Exception("Cooldown text did not update.");
                    if (theme.transform.Find("Theme_Expedition/Timer").GetComponent<Text>().text == "00:00") throw new Exception("Timer did not advance.");
                    Directory.CreateDirectory("UI-Preview");
                    File.WriteAllText("UI-Preview/main-scene-validation.txt", "PASS: saved SampleScene reloaded in Play mode.\nPASS: UI references survive scene serialization.\nPASS: skill 3 button activates cooldown.\nPASS: live party HP updates to 50%.\nPASS: cooldown label and expedition timer update.\n");
                    Debug.Log("RAID_MAIN_PLAY_OK");
                    EditorApplication.update -= tick;
                    EditorApplication.Exit(0);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    EditorApplication.update -= tick;
                    EditorApplication.Exit(1);
                }
            };
            EditorApplication.update += tick;
        }
    }
}
#endif
