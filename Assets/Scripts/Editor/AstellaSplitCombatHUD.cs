#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace RaidRogue.Editor
{
    // One-time authoring pass; runtime never resets these transforms.
    public static class AstellaSplitCombatHUD
    {
        private const string Stamp="Logs/SplitCombatHUD-v1.txt";
        public static void ApplyIfMissing()
        {
            if(File.Exists(Stamp)){SyncLoadedScenes();return;}
            Apply();
        }
        [MenuItem("Tools/Astella/UI/Apply Split Combat HUD")]
        public static void Apply()
        {
            if(EditorApplication.isPlaying||PrefabStageUtility.GetCurrentPrefabStage()!=null)throw new InvalidOperationException("Stop Play and close Prefab Mode first.");
            if(!string.Equals(Path.GetFullPath(Application.dataPath).TrimEnd('/','\\'),@"C:\Astella\Astella\Assets",StringComparison.OrdinalIgnoreCase))return;
            var profile=AssetDatabase.LoadAssetAtPath<RaidUIPresentation>("Assets/Resources/UI/RaidUIPresentation.asset");
            if(profile?.hudPrefab==null)return;
            string path=AssetDatabase.GetAssetPath(profile.hudPrefab);
            if(File.Exists(Stamp)){SyncLoadedScenes();return;}
            string backup="Logs/UIRepairBackups/split-combat-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");Directory.CreateDirectory(backup);
            File.Copy(path,backup+"/RaidHUD.prefab");File.Copy(path+".meta",backup+"/RaidHUD.prefab.meta");
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                Build(root.GetComponentInChildren<AstellaOrnateHUD>(true));
                RaidUIAuthoring.ValidateScripts(root);
                if(PrefabUtility.SaveAsPrefabAsset(root,path)==null)throw new IOException(path);
                AssetDatabase.SaveAssets();
                File.WriteAllText(Stamp,path+"\nBackup: "+backup+"\n1 2 3 4 | identity + V | Q F R   Space; compact potion; party revival top-left.\n");
                Debug.Log("SPLIT COMBAT HUD APPLIED: "+path+". Backup: "+backup);
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
            SyncLoadedScenes();
        }
        // Apply/scene synchronization is explicit; local migration stamps are not authority.
        private static bool HasSplitLayout(AstellaOrnateHUD hud)=>hud!=null&&(hud.combatLayoutVersion>=1||AstellaSplitCombatLayout.IsApplied(hud));
        private static void SyncLoadedScenes()
        {
            if(EditorApplication.isPlaying||PrefabStageUtility.GetCurrentPrefabStage()!=null)return;
            for(int i=0;i<SceneManager.sceneCount;i++)
            {
                var scene=SceneManager.GetSceneAt(i);if(!scene.isLoaded)continue;
                foreach(var hud in scene.GetRootGameObjects().SelectMany(go=>go.GetComponentsInChildren<AstellaOrnateHUD>(true)).ToArray())
                {
                    if(HasSplitLayout(hud))continue;
                    if(!string.IsNullOrEmpty(scene.path)&&File.Exists(scene.path))
                    {
                        string backup="Logs/UIRepairBackups/split-scene-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");Directory.CreateDirectory(backup);
                        File.Copy(scene.path,backup+"/"+Path.GetFileName(scene.path));
                        if(File.Exists(scene.path+".meta"))File.Copy(scene.path+".meta",backup+"/"+Path.GetFileName(scene.path)+".meta");
                    }
                    Undo.RegisterFullObjectHierarchyUndo(hud.transform.root.gameObject,"Apply split combat HUD");
                    Build(hud);EditorSceneManager.MarkSceneDirty(scene);
                    Debug.Log("SPLIT COMBAT HUD SCENE UPDATED: "+scene.path+". Scene changes remain unsaved for review.");
                }
            }
        }
        private static void Build(AstellaOrnateHUD hud)
        {
            var circle=AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/AstellaOrnate/Shapes/Circle.png");
            AstellaSplitCombatLayout.ApplyIfMissing(hud,Art,circle);
            if(!HasSplitLayout(hud))throw new InvalidOperationException("The HUD has no recognized legacy or split layout. No defaults were applied.");
        }
        private static Sprite Art(string name)=>AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/AstellaOrnate/Art/"+name+".png");
    }
}
#endif
