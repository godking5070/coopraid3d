#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace RaidRogue.Editor
{
    /// <summary>Explicit resource swap for the main scene in Edit Mode.</summary>
    public static class AstellaRequestedUIResourceSwap
    {
        private const string Stamp = "Logs/RequestedUIResources-player2-party4-skillbar1.applied.txt";

        // Resource migrations run from the menu, never because local Logs are absent.
        [MenuItem("Tools/RaidRogue/UI/Apply All Requested UI Resources")]
        public static void ApplyAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Stop Play and close Prefab Mode.");
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.path != "Assets/Scenes/SampleScene.unity") throw new InvalidOperationException("Open Astella SampleScene.");
            var profile = AssetDatabase.LoadAssetAtPath<RaidUIPresentation>("Assets/Resources/UI/RaidUIPresentation.asset");
            if (profile?.hudPrefab == null) throw new InvalidOperationException("Active authored HUD is missing.");
            bool partyMissing = profile.hudPrefab.GetComponentInChildren<AstellaPartyHUDV4>(true) == null;
            bool skinMissing = profile.hudPrefab.GetComponentInChildren<AstellaSkillSlotSkin>(true) == null;
            if (partyMissing) AstellaPlayerPartyResourceInstaller.Apply();
            if (skinMissing) AstellaSkillbarV1Installer.Apply();

            // Keep scene overrides on layout roots; update only presentation properties this swap owns.
            var liveHuds = scene.GetRootGameObjects().Where(g => g.activeSelf)
                .SelectMany(g => g.GetComponentsInChildren<AstellaOrnateHUD>(true)).Where(h => h.gameObject.activeInHierarchy).ToArray();
            if (liveHuds.Length != 1) throw new InvalidOperationException("Expected one active main-scene HUD; found " + liveHuds.Length);
            var live = liveHuds[0].GetComponentInParent<RaidUIRoot>().gameObject;
            if (live != null)
            {
                AstellaPlayerPartyResourceInstaller.ApplyToHierarchy(live);
                AstellaSkillbarV1Installer.ApplyToHierarchy(live);
                SyncPresentationOverrides(live);
                RaidUIAuthoring.ValidateScripts(live);
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            }
            RaidUIAuthoring.Validate(profile);
            Directory.CreateDirectory("Logs");
            File.WriteAllText(Stamp, DateTime.Now.ToString("s") + "\n" + AssetDatabase.GetAssetPath(profile.hudPrefab) +
                "\nPlayer gauges v2, party HUD v4, skillbar v1. Skill icon artwork remains pending.\n" +
                "Player/identity/skill layout roots retained. Scene synced only on affected presentation branches.");
            AssetDatabase.SaveAssets();
            Debug.Log("REQUESTED UI RESOURCES APPLIED: player gauges v2 + party HUD v4 + skillbar v1 (icons pending).");
        }

        private static void SyncPresentationOverrides(GameObject root)
        {
            var hud = root.GetComponentInChildren<AstellaOrnateHUD>(true);
            var party = root.GetComponentInChildren<PartyListUI>(true);
            foreach (var branch in new[] { hud.playerHpText.transform.parent, hud.secondaryResourceRoot?.transform, party.panelRoot })
                if (branch != null)
                    foreach (var graphic in branch.GetComponentsInChildren<Graphic>(true)) Revert(graphic, "m_Enabled");
            var serialized = new SerializedObject(hud);
            for (int i = 0; i < hud.slots.Length; i++)
            {
                var slot = hud.slots[i];
                if (slot?.button == null || slot.button.GetComponent<AstellaSkillSlotSkin>() == null) continue;
                foreach (var graphic in slot.button.GetComponentsInChildren<Graphic>(true)) Revert(graphic, "m_Enabled");
                foreach (var text in new[] { slot.keycap, slot.cooldown })
                    if (text != null)
                    {
                        foreach (string key in new[] { "m_AnchorMin", "m_AnchorMax", "m_Pivot", "m_AnchoredPosition", "m_SizeDelta", "m_LocalScale" }) Revert(text.rectTransform, key);
                    }
                foreach (string field in new[] { "cooldownShade", "stateGroup" })
                {
                    var property = serialized.FindProperty("slots.Array.data[" + i + "]." + field);
                    if (property != null && property.prefabOverride) PrefabUtility.RevertPropertyOverride(property, InteractionMode.AutomatedAction);
                }
            }
            foreach (var row in party.memberRows)
                if (row?.rowRoot != null)
                    foreach (string key in new[] { "m_AnchorMin", "m_AnchorMax", "m_Pivot", "m_AnchoredPosition", "m_SizeDelta", "m_LocalScale" }) Revert(row.rowRoot.transform, key);
        }
        private static void Revert(UnityEngine.Object target, string key)
        {
            if (target == null || !PrefabUtility.IsPartOfPrefabInstance(target) || PrefabUtility.GetCorrespondingObjectFromSource(target) == null) return;
            var property = new SerializedObject(target).FindProperty(key);
            if (property != null && property.prefabOverride) PrefabUtility.RevertPropertyOverride(property, InteractionMode.AutomatedAction);
        }
    }
}
#endif
