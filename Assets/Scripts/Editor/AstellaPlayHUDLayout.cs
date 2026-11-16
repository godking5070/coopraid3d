#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RaidRogue.Editor
{
    // One capture request for the layout the user has arranged in the current Play session.
    // Store transforms only: health, cooldown, readiness and other live values are never saved.
    public static class AstellaPlayHUDLayout
    {
        private const string CapturePath="Logs/UserHUDLayout-20261106.json";
        private const string AppliedPath="Logs/UserHUDLayout-20261106-applied.txt";
        [Serializable] private sealed class Layout
        {
            public string capturedAt;
            public Node[] nodes;
        }
        [Serializable] private sealed class Node
        {
            public string key,parent;
            public Vector2 anchorMin,anchorMax,pivot,size;
            public Vector3 position,scale;
            public Quaternion rotation;
        }
        // Capture and Apply are separate explicit actions. A stale capture on disk must
        // never be written into a newer prefab just because Play Mode has ended.
        [MenuItem("Tools/Astella/UI/Capture Current Play HUD Layout")]
        public static void Capture()
        {
            CaptureCore();
        }
        private static void CaptureCore()
        {
            if(!IsOriginalProject())return;
            if(!EditorApplication.isPlaying)return;
            var hud=UnityEngine.Object.FindObjectsByType<AstellaOrnateHUD>(FindObjectsInactive.Exclude,FindObjectsSortMode.None)
                .FirstOrDefault(h=>h.gameObject.scene.IsValid()&&h.playerRoot!=null&&AstellaSplitCombatLayout.IsApplied(h));
            if(hud==null)return;
            var map=Map(hud);var nodes=new List<Node>();
            foreach(var pair in map)
            {
                if(!(pair.Value is RectTransform r))continue;
                string parent=map.FirstOrDefault(p=>p.Value==r.parent).Key;
                nodes.Add(new Node{key=pair.Key,parent=parent,anchorMin=r.anchorMin,anchorMax=r.anchorMax,pivot=r.pivot,size=r.sizeDelta,
                    position=r.anchoredPosition3D,scale=r.localScale,rotation=r.localRotation});
            }
            Directory.CreateDirectory("Logs");
            File.WriteAllText(CapturePath,JsonUtility.ToJson(new Layout{capturedAt=DateTime.Now.ToString("s"),nodes=nodes.ToArray()},true));
            Debug.Log("USER HUD LAYOUT CAPTURED: "+nodes.Count+" transforms. "+CapturePath+". Stop Play, then use Apply Captured Play HUD Layout to update the prefab.");
        }
        [MenuItem("Tools/Astella/UI/Apply Captured Play HUD Layout")]
        public static void ApplySavedLayout()
        {
            if(!IsOriginalProject())throw new InvalidOperationException("Use the original Astella Unity project.");
            if(EditorApplication.isPlayingOrWillChangePlaymode||PrefabStageUtility.GetCurrentPrefabStage()!=null)
                throw new InvalidOperationException("Stop Play and close Prefab Mode to save the captured layout.");
            if(!File.Exists(CapturePath))throw new FileNotFoundException("Capture the Play layout first.",CapturePath);
            var data=JsonUtility.FromJson<Layout>(File.ReadAllText(CapturePath));
            if(data?.nodes==null||data.nodes.Length<15)throw new InvalidOperationException("Captured HUD layout is incomplete; existing prefab preserved.");
            AstellaSplitCombatHUD.ApplyIfMissing();
            var profile=AssetDatabase.LoadAssetAtPath<RaidUIPresentation>("Assets/Resources/UI/RaidUIPresentation.asset");
            if(profile?.hudPrefab==null)throw new InvalidOperationException("Active HUD prefab missing.");
            string path=AssetDatabase.GetAssetPath(profile.hudPrefab);
            string backup="Logs/UIRepairBackups/user-layout-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");Directory.CreateDirectory(backup);
            File.Copy(path,backup+"/RaidHUD.prefab");File.Copy(path+".meta",backup+"/RaidHUD.prefab.meta");
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                Apply(root.GetComponentInChildren<AstellaOrnateHUD>(true),data);
                RaidUIAuthoring.ValidateScripts(root);
                if(PrefabUtility.SaveAsPrefabAsset(root,path)==null)throw new IOException(path);
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
            for(int i=0;i<SceneManager.sceneCount;i++)
            {
                var scene=SceneManager.GetSceneAt(i);if(!scene.isLoaded)continue;
                foreach(var hud in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<AstellaOrnateHUD>(true)))
                {
                    if(!AstellaSplitCombatLayout.IsApplied(hud))continue;
                    Undo.RegisterFullObjectHierarchyUndo(hud.playerRoot,"Apply captured HUD layout");
                    Apply(hud,data);EditorSceneManager.MarkSceneDirty(scene);
                }
            }
            AssetDatabase.SaveAssets();
            File.WriteAllText(AppliedPath,path+"\nCaptured: "+data.capturedAt+"\nBackup: "+backup);
            Debug.Log("USER HUD LAYOUT SAVED: "+path+". Captured positions, sizes and scales retained; save the scene to keep scene overrides.");
        }
        private static void Apply(AstellaOrnateHUD hud,Layout data)
        {
            if(hud==null)throw new InvalidOperationException("HUD presenter missing.");
            var map=Map(hud);
            foreach(var node in data.nodes)
                if(!map.ContainsKey(node.key)||(!string.IsNullOrEmpty(node.parent)&&!map.ContainsKey(node.parent)))
                    throw new InvalidOperationException("Captured HUD binding missing: "+node.key+". Prefab was not saved.");
            foreach(var node in data.nodes)
            {
                var r=(RectTransform)map[node.key];
                if(!string.IsNullOrEmpty(node.parent)&&r.parent!=map[node.parent])r.SetParent(map[node.parent],false);
                r.anchorMin=node.anchorMin;r.anchorMax=node.anchorMax;r.pivot=node.pivot;r.sizeDelta=node.size;
                r.anchoredPosition3D=node.position;r.localScale=node.scale;r.localRotation=node.rotation;
            }
            hud.combatLayoutVersion=1;
        }
        private static Dictionary<string,Transform> Map(AstellaOrnateHUD hud)
        {
            var map=new Dictionary<string,Transform>();
            void Add(string key,Transform target){if(target!=null)map[key]=target;}
            var player=hud.playerRoot!=null?hud.playerRoot.transform:null;
            Add("player",player);Add("left",player?.Find("Left - 1234"));Add("right",player?.Find("Right - Q F R Space"));
            Add("center",player?.Find("Center - V"));Add("identity",hud.resourceRoot?.transform);
            Add("hp",hud.playerHpText?.transform.parent);Add("hpText",hud.playerHpText?.transform);Add("hpFill",hud.playerHpFill?.transform);
            Add("identityText",hud.resourceText?.transform);Add("identityEmblem",hud.identityEmblem?.transform);
            Add("secondary",hud.secondaryResourceRoot?.transform);Add("secondaryText",hud.secondaryResourceText?.transform);
            Add("revival",hud.partyRevivalRoot?.transform);
            foreach(var binding in hud.slots)
            {
                if(binding?.button==null)continue;
                string key=binding.sharedResurrection?"revivalButton":"slot."+binding.slot;
                Add(key,binding.button.transform);Add(key+".key",binding.keycap?.transform.parent);Add(key+".keyText",binding.keycap?.transform);
            }
            return map;
        }
        private static bool IsOriginalProject()=>string.Equals(Path.GetFullPath(Application.dataPath).TrimEnd('/','\\'),@"C:\Astella\Astella\Assets",StringComparison.OrdinalIgnoreCase);
    }
}
#endif
