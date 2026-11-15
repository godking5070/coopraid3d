#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RaidRogue.Editor
{
    // Runs only in an already-open original editor. Never launches Unity or edits scene YAML.
    public static class RaidUISeparation
    {
        private const string ProfilePath="Assets/Resources/UI/RaidUIPresentation.asset";
        private const string StationPath="Assets/Resources/UI/ConstellationBuild.prefab";
        private const string Folder="Assets/UI/Separated";
        private const string Stamp="Logs/UISeparation-v1.json";
        private sealed class ReferenceSnapshot
        {
            public string owner,property,targetAddress;
            public Object target;
        }
        [InitializeOnLoadMethod]
        private static void Schedule() { if(Application.isBatchMode)return;EditorApplication.update-=WhenSafe;EditorApplication.update+=WhenSafe; }
        private static void WhenSafe()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating||PrefabStageUtility.GetCurrentPrefabStage()!=null)return;
            EditorApplication.update-=WhenSafe;
            try { AstellaSplitCombatHUD.ApplyIfMissing(); }
            catch(Exception error){Debug.LogError("Split combat HUD authoring stopped: "+error);return;}
            // Full HUD nesting can change IDs referenced by scene overrides. Keep that
            // explicit, rather than reattempting a heavy migration after every compilation.
            // Missing station assets and the compact fallback slots remain safe to initialize.
            try { RepairSlotWrappers();CreateStation(); }
            catch(Exception error){Debug.LogError("UI station initialization stopped: "+error);}
        }
        [MenuItem("Tools/Astella/UI/Separate Editable UI Prefabs")]
        public static void Separate()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||PrefabStageUtility.GetCurrentPrefabStage()!=null)
                throw new InvalidOperationException("Stop Play Mode and close Prefab Mode before separating UI.");
            if(!string.Equals(Path.GetFullPath(Application.dataPath).TrimEnd('/','\\'),@"C:\Astella\Astella\Assets",StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Only run in the original C:\\Astella\\Astella project.");
            EnsureFolder("Assets/Resources/UI");EnsureFolder(Folder);
            AstellaSplitCombatHUD.ApplyIfMissing();
            RepairSlotWrappers();
            CreateStation();
            if(File.Exists(Stamp)){Debug.Log("UI prefabs already separated. Existing designs were not regenerated. See "+Stamp);return;}
            var profile=AssetDatabase.LoadAssetAtPath<RaidUIPresentation>(ProfilePath);
            if(profile==null||profile.hudPrefab==null)throw new InvalidOperationException("Active RaidHUD profile is missing.");
            string source=AssetDatabase.GetAssetPath(profile.hudPrefab);
            var referencedIds=ReferencedIds(source);
            string output=Folder+"/HUD-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");EnsureFolder(output);
            string backup="Logs/UIRepairBackups/separation-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");Directory.CreateDirectory(backup);
            File.Copy(source,backup+"/RaidHUD.prefab");File.Copy(source+".meta",backup+"/RaidHUD.prefab.meta");
            File.Copy(ProfilePath,backup+"/RaidUIPresentation.asset");
            var root=PrefabUtility.LoadPrefabContents(source);
            var exported=new List<string>();
            bool saveStarted=false;
            try
            {
                RaidUIAuthoring.ValidateScripts(root);
                int index=0;
                var visualNames=new HashSet<string>{"PlayerHUD","BossHUD","Hotbar anchor","Party revival","Stage"};
                var hud=root.GetComponentInChildren<AstellaOrnateHUD>(true);
                var artSlots=hud!=null?hud.slots.Where(s=>s.button!=null).ToDictionary(s=>s.button.transform,s=>s.sharedResurrection?"SharedRevival":s.slot==CharacterSlot.Movement?"Dash":s.slot==CharacterSlot.Potion?"Potion":"Skill-"+s.slot):new Dictionary<Transform,string>();
                var visualSections=root.GetComponentsInChildren<RectTransform>(true)
                    .Where(t=>t!=root.transform&&t.parent!=root.transform&&(visualNames.Contains(t.name)||artSlots.ContainsKey(t)))
                    .OrderByDescending(t=>Depth(t)).ToArray();
                foreach(var section in visualSections)
                {
                    string path=output+"/"+(++index).ToString("00")+"-"+SafeName(artSlots.TryGetValue(section,out var label)?label:section.name)+".prefab";
                    ExportAndNest(root,section.gameObject,path);exported.Add(path);
                }
                // Root children correspond to menus, party, targets, minimap, rewards and HUD sections.
                // Their sibling order and active state remain intact; native connect retains references.
                var sections=root.transform.Cast<Transform>().Where(t=>t is RectTransform).ToArray();
                foreach(var section in sections)
                {
                    string name=SafeName(section.name);
                    string path=output+"/"+(++index).ToString("00")+"-"+name+".prefab";
                    ExportAndNest(root,section.gameObject,path);exported.Add(path);
                }
                RaidUIAuthoring.ValidateScripts(root);
                ValidateInternalReferences(root);
                if(!File.ReadAllBytes(source).SequenceEqual(File.ReadAllBytes(backup+"/RaidHUD.prefab")))
                    throw new IOException("RaidHUD changed during separation. Newer edits were preserved; run again after saving your work.");
                saveStarted=true;
                if(PrefabUtility.SaveAsPrefabAsset(root,source)==null)throw new IOException("Could not save composed RaidHUD.");
                AssetDatabase.SaveAssets();
                var savedIds=new HashSet<string>(Regex.Matches(File.ReadAllText(source),@"(?m)^--- !u!\d+ &(-?\d+)").Cast<Match>().Select(m=>m.Groups[1].Value));
                if(referencedIds.Any(id=>!savedIds.Contains(id)))
                    throw new InvalidOperationException("Separation changed externally referenced HUD object IDs. Original HUD restored; station prefabs remain available.");
                Directory.CreateDirectory("Logs");
                File.WriteAllText(Stamp,JsonUtility.ToJson(new Report{source=source,folder=output,backup=backup,prefabs=exported.ToArray()},true));
                Debug.Log("UI PREFABS SEPARATED: "+output+" | Station: "+StationPath+" | Backup: "+backup);
            }
            catch
            {
                // Restore the original source if final save/validation failed. Child assets remain reviewable.
                if(saveStarted){File.Copy(backup+"/RaidHUD.prefab",source,true);AssetDatabase.ImportAsset(source);}
                throw;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        [Serializable] private sealed class Report { public string source,folder,backup;public string[] prefabs; }
        [MenuItem("Tools/Astella/UI/Audit All UI Prefabs")]
        public static void Audit()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)return;
            var paths=new HashSet<string>(AssetDatabase.GetDependencies(ProfilePath,true).Where(p=>p.EndsWith(".prefab",StringComparison.OrdinalIgnoreCase)));
            if(File.Exists(StationPath))paths.UnionWith(AssetDatabase.GetDependencies(StationPath,true).Where(p=>p.EndsWith(".prefab",StringComparison.OrdinalIgnoreCase)));
            var active=new HashSet<string>(paths);
            paths.UnionWith(AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/UI"}).Select(AssetDatabase.GUIDToAssetPath));
            var report=new System.Text.StringBuilder("# UI prefab audit\n\n"+DateTime.Now.ToString("s")+"\n\n");
            report.AppendLine(File.Exists(Stamp)?"HUD separation completed. See Logs/UISeparation-v1.json.":"HUD separation is NOT complete. HUD-* folders are partial outputs, not the active HUD.");
            int errors=0,warnings=0;
            foreach(string path in paths.OrderBy(p=>p))
            {
                report.AppendLine("\n## "+path);
                report.AppendLine(active.Contains(path)?"ACTIVE dependency.":"Not referenced by the active profile/station; archive or partial output.");
                var root=PrefabUtility.LoadPrefabContents(path);
                try
                {
                    try{RaidUIAuthoring.ValidateScripts(root);}catch(Exception error){errors++;report.AppendLine("ERROR: "+error.Message);}
                    var rect=root.GetComponent<RectTransform>();
                    if(rect!=null)report.AppendLine("Root: anchors "+rect.anchorMin+" / "+rect.anchorMax+", size "+rect.sizeDelta+", position "+rect.anchoredPosition+".");
                    var graphics=root.GetComponentsInChildren<UnityEngine.UI.Graphic>(true);
                    report.AppendLine("Graphics: "+graphics.Length+"; enabled in current hierarchy: "+graphics.Count(g=>g.enabled&&g.gameObject.activeInHierarchy)+". Inactive menus/cooldown masks may be intentional.");
                    foreach(var view in root.GetComponentsInChildren<RaidUIView>(true))
                    {
                        report.AppendLine("View: "+view.viewId+"; references "+view.references.Count+"; actions "+view.actions.Count+".");
                        foreach(var action in view.actions.Where(a=>a.button==null)){errors++;report.AppendLine("ERROR: missing button: "+view.viewId+"/"+action.id);}
                        var slot=view.references.FirstOrDefault(r=>r.path=="_slot")?.target as GameObject;
                        if(slot!=null&&!slot.transform.IsChildOf(view.transform)){warnings++;report.AppendLine("INFO: slot is located outside this binding wrapper. Edit the illustrated skill slot prefab, not this wrapper.");}
                    }
                    var station=root.GetComponent<RaidBuildStationView>();
                    if(station!=null)try{station.ValidateBindings();report.AppendLine("Station bindings: PASS.");}catch(Exception error){errors++;report.AppendLine("ERROR: "+error.Message);}
                    foreach(var fit in root.GetComponentsInChildren<UnityEngine.UI.AspectRatioFitter>(true))
                        if(fit.aspectRatio<=0){errors++;report.AppendLine("ERROR: invalid aspect ratio: "+fit.name);}
                    foreach(var text in root.GetComponentsInChildren<UnityEngine.UI.Text>(true))
                        if(text.font==null&&!string.IsNullOrEmpty(text.text)){warnings++;report.AppendLine("WARN: missing font: "+text.name);}
                }
                catch(Exception error){errors++;report.AppendLine("ERROR: "+error);}
                finally{PrefabUtility.UnloadPrefabContents(root);}
            }
            report.Insert(0,"Prefabs: "+paths.Count+"; errors: "+errors+"; warnings/information: "+warnings+".\n\n");
            Directory.CreateDirectory("Logs");File.WriteAllText("Logs/UIPrefabAudit.md",report.ToString());
            Debug.Log("UI PREFAB AUDIT: "+paths.Count+" prefabs, "+errors+" errors, "+warnings+" warnings/info. Logs/UIPrefabAudit.md. Visual rendering and live input require Play verification.");
        }
        private static void CreateStation()
        {
            var existing=AssetDatabase.LoadAssetAtPath<RaidBuildStationView>(StationPath);
            if(existing!=null){existing.ValidateBindings();return;}
            if(File.Exists(StationPath))throw new InvalidOperationException("Existing station prefab has no RaidBuildStationView. It was not overwritten.");
            var holder=new GameObject("Station authoring host",typeof(RectTransform));
            try
            {
                var presenter=holder.AddComponent<CharacterFrameworkUI>();
                var view=presenter.CreateDefaultStationView(holder.transform);
                string[] jobs={"이지스 팔라딘","실반 오라클","나이트블레이드","스피릿 레인저"};
                for(int i=0;i<4;i++){view.members[i].label.text=jobs[i]+"\n플레이어 "+(i+1)+"\n정비 중";view.members[i].counterBadge.text="C–";}
                for(int i=0;i<7;i++)view.choices[i].label.text=i<5?"스킬 · 특성 "+(i+1):"보조 "+(i-4);
                view.stage.text="STAGE 01 / 06\n\n스테이지 정보\n\n노말";
                view.counterSummary.text="카운터 장착 0명 · 준비 0 / 4";
                view.tooltip.text="아이콘에 마우스를 올려 상세 정보 확인";
                var root=view.transform;
                string stationFolder=Folder+"/Station-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");EnsureFolder(stationFolder);
                // Separate the station's left roster, center build and right stage card too.
                var groups=new[]{"PartyRoster","MyBuild","StageInfo"};
                foreach(string groupName in groups)
                {
                    var group=new GameObject(groupName,typeof(RectTransform)).GetComponent<RectTransform>();
                    group.SetParent(root,false);group.anchorMin=group.anchorMax=new Vector2(.5f,.5f);group.sizeDelta=((RectTransform)root).sizeDelta;
                    foreach(var child in root.Cast<Transform>().ToArray())
                    {
                        if(child==group||groups.Contains(child.name))continue;
                        bool belongs=groupName=="PartyRoster"?(child.name.StartsWith("PartyMember",StringComparison.Ordinal)||child==view.partyTitle.transform||child==view.counterSummary.transform):
                            groupName=="MyBuild"?(child.name.StartsWith("Choice",StringComparison.Ordinal)||child==view.localHeading.transform||child==view.ownRoute.root||child==view.routeChange.transform||child==view.tooltip.transform):
                            (child.name=="StageCard"||child.GetComponent<UnityEngine.UI.Text>()?.text=="현재 스테이지");
                        if(belongs)child.SetParent(group,false);
                    }
                    ExportAndNest(view.gameObject,group.gameObject,stationFolder+"/"+groupName+".prefab");
                }
                view.ValidateBindings();RaidUIAuthoring.ValidateScripts(view.gameObject);
                if(PrefabUtility.SaveAsPrefabAsset(view.gameObject,StationPath)==null)throw new IOException(StationPath);
                AssetDatabase.SaveAssets();
            }
            finally {Object.DestroyImmediate(holder);}
        }
        private static void RepairSlotWrappers()
        {
            var profile=AssetDatabase.LoadAssetAtPath<RaidUIPresentation>(ProfilePath);
            if(profile==null)return;
            foreach(var entry in profile.views.Where(e=>e.id=="dash"||e.id=="potion"))
            {
                if(entry.prefab==null)continue;
                string path=AssetDatabase.GetAssetPath(entry.prefab);
                var root=PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var view=root.GetComponent<RaidUIView>();
                    var slot=view.references.FirstOrDefault(r=>r.path=="_slot")?.target as GameObject;
                    var rect=root.GetComponent<RectTransform>();var child=slot!=null?slot.GetComponent<RectTransform>():null;
                    // Only repair the original empty fullscreen wrapper with a single fixed-size slot.
                    // Copying the child's placement onto the wrapper preserves runtime screen position.
                    if(child==null||child.parent!=rect||rect.childCount!=1||rect.anchorMin!=Vector2.zero||rect.anchorMax!=Vector2.one||
                        rect.sizeDelta!=Vector2.zero||rect.anchoredPosition!=Vector2.zero||rect.localScale!=Vector3.one||rect.localRotation!=Quaternion.identity||
                        child.anchorMin!=child.anchorMax)continue;
                    string backup="Logs/UIRepairBackups/slot-wrapper-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");Directory.CreateDirectory(backup);
                    File.Copy(path,backup+"/"+Path.GetFileName(path));File.Copy(path+".meta",backup+"/"+Path.GetFileName(path)+".meta");
                    RaidUILayout.Copy(rect,child);
                    child.anchorMin=Vector2.zero;child.anchorMax=Vector2.one;child.offsetMin=child.offsetMax=Vector2.zero;
                    child.localRotation=Quaternion.identity;child.localScale=Vector3.one;
                    if(PrefabUtility.SaveAsPrefabAsset(root,path)==null)throw new IOException(path);
                    Debug.Log("Compact UI prefab wrapper repaired: "+path+" | Runtime placement preserved. Backup: "+backup);
                }
                finally{PrefabUtility.UnloadPrefabContents(root);}
            }
        }
        private static void ExportAndNest(GameObject root,GameObject section,string path)
        {
            if(File.Exists(path))throw new InvalidOperationException("Refusing to overwrite edited prefab: "+path);
            // Native connect can replace component wrappers. Store stable hierarchy addresses,
            // not live component pointers, before Unity performs that operation.
            var before=new Dictionary<string,Object>();IndexObjects(root.transform,"",before);
            var addresses=before.ToDictionary(p=>p.Value,p=>p.Key);
            var references=new List<ReferenceSnapshot>();
            foreach(var pair in before)
            {
                if(!(pair.Value is MonoBehaviour script)||script==null)continue;
                using(var serialized=new SerializedObject(script))
                {
                    var fields=new HashSet<string>();
                    for(var type=script.GetType();type!=null&&type!=typeof(MonoBehaviour);type=type.BaseType)
                    foreach(var field in type.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly))
                    {
                        if(!fields.Add(field.Name))continue;
                        var property=serialized.FindProperty(field.Name);if(property==null)continue;
                        var end=property.GetEndProperty();
                        do
                        {
                            if(property.propertyType!=SerializedPropertyType.ObjectReference||property.objectReferenceValue==null)continue;
                            var target=property.objectReferenceValue;
                            references.Add(new ReferenceSnapshot{owner=pair.Key,property=property.propertyPath,target=target,targetAddress=addresses.TryGetValue(target,out var address)?address:null});
                        }while(property.Next(true)&&!SerializedProperty.EqualContents(property,end));
                    }
                }
            }
            var asset=PrefabUtility.SaveAsPrefabAssetAndConnect(section,path,InteractionMode.AutomatedAction,out bool saved);
            if(!saved||asset==null)throw new IOException(path);
            var after=new Dictionary<string,Object>();IndexObjects(root.transform,"",after);
            if(before.Count!=after.Count||before.Keys.Any(key=>!after.ContainsKey(key)||before[key].GetType()!=after[key].GetType()))throw new InvalidOperationException("UI hierarchy changed during native prefab connect.");
            foreach(var reference in references)
            {
                var owner=after[reference.owner];
                var target=reference.targetAddress!=null?after[reference.targetAddress]:reference.target;
                using(var serialized=new SerializedObject(owner))
                {
                    var property=serialized.FindProperty(reference.property);
                    if(property==null)throw new InvalidOperationException("Lost UI binding: "+reference.property);
                    if(property.objectReferenceValue==target)continue;
                    property.objectReferenceValue=target;serialized.ApplyModifiedPropertiesWithoutUndo();
                    if(PrefabUtility.IsPartOfPrefabInstance(owner))PrefabUtility.RecordPrefabInstancePropertyModifications(owner);
                }
            }
        }
        private static void IndexObjects(Transform node,string address,Dictionary<string,Object> objects)
        {
            objects[address+"/go"]=node.gameObject;
            var components=node.GetComponents<Component>();
            for(int i=0;i<components.Length;i++)
            {
                if(components[i]==null)throw new InvalidOperationException("Missing UI script at "+node.name);
                objects[address+"/c"+i]=components[i];
            }
            for(int i=0;i<node.childCount;i++)IndexObjects(node.GetChild(i),address+"/"+i,objects);
        }
        private static void ValidateInternalReferences(GameObject root)
        {
            foreach(var script in root.GetComponentsInChildren<MonoBehaviour>(true))
            using(var serialized=new SerializedObject(script))
            {
                var property=serialized.GetIterator();
                while(property.Next(true))
                {
                    if(property.propertyType!=SerializedPropertyType.ObjectReference)continue;
                    var target=property.objectReferenceValue;
                    Transform tr=target is GameObject go?go.transform:target is Component component?component.transform:null;
                    if(tr!=null&&!EditorUtility.IsPersistent(target)&&tr!=root.transform&&!tr.IsChildOf(root.transform))
                        throw new InvalidOperationException("External scene reference: "+script.GetType().Name+"/"+property.propertyPath);
                }
            }
        }
        private static string SafeName(string name)
        {foreach(char invalid in Path.GetInvalidFileNameChars())name=name.Replace(invalid,'_');return name;}
        private static int Depth(Transform t){int depth=0;while(t.parent!=null){depth++;t=t.parent;}return depth;}
        private static HashSet<string> ReferencedIds(string source)
        {
            string guid=AssetDatabase.AssetPathToGUID(source);
            var pattern=new Regex(@"fileID: (-?\d+), guid: "+Regex.Escape(guid)+@"[,}]");
            var ids=new HashSet<string>();
            foreach(string path in Directory.EnumerateFiles("Assets","*",SearchOption.AllDirectories))
            {
                if(path.Replace('\\','/')==source||!(path.EndsWith(".unity",StringComparison.OrdinalIgnoreCase)||path.EndsWith(".prefab",StringComparison.OrdinalIgnoreCase)||path.EndsWith(".asset",StringComparison.OrdinalIgnoreCase)))continue;
                foreach(Match match in pattern.Matches(File.ReadAllText(path)))
                    if(match.Groups[1].Value!="100100000")ids.Add(match.Groups[1].Value);
            }
            return ids;
        }
        private static void EnsureFolder(string path)
        {
            string current="Assets";
            foreach(string part in path.Split('/').Skip(1))
            {string next=current+"/"+part;if(!AssetDatabase.IsValidFolder(next))AssetDatabase.CreateFolder(current,part);current=next;}
        }
    }
}
#endif
