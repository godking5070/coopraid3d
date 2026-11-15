#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
namespace RaidRogue.Editor
{
    public static class RaidUIBindingChecks
    {
        [MenuItem("Tools/RaidRogue/UI/Run Binding Checks")]
        public static void Run()
        {
            var host=new GameObject("UI binding check",typeof(RectTransform));host.SetActive(false);
            try
            {
                var probe=host.AddComponent<RaidUIBindingProbe>();var parent=RaidUIView.Begin(probe,"test",host.transform);
                var node=new GameObject("old name",typeof(RectTransform),typeof(Text),typeof(Button));node.transform.SetParent(parent,false);
                var text=node.GetComponent<Text>();var button=node.GetComponent<Button>();var rect=(RectTransform)node.transform;
                rect.anchoredPosition=new Vector2(83,127);rect.sizeDelta=new Vector2(223,71);text.fontSize=31;text.color=Color.magenta;
                var removed=new GameObject("removed reference",typeof(RectTransform),typeof(Text),typeof(MeshRenderer));removed.transform.SetParent(parent,false);
                var removedText=removed.GetComponent<Text>();probe.removedRenderer=removed.GetComponent<MeshRenderer>();probe.removedObject=removed;
                UnityEngine.Object.DestroyImmediate(removed);
                Assert(!ReferenceEquals(probe.removedRenderer,null)&&probe.removedRenderer==null,"Unity fake-null fixture");
                probe.title=text;probe.buttons[1]=button;probe.rows.Add(new[]{text,removedText});probe.labels[CharacterSlot.Active]=text;probe.nested.skillNameText=text;
                var view=RaidUIView.For(probe);view.Capture(probe);
                Assert(!view.references.Exists(x=>x.path=="optionalRenderer"),"unassigned optional renderer skipped");
                Assert(!view.references.Exists(x=>x.path=="removedRenderer"||x.path=="removedObject"),"destroyed Unity references skipped");
                var other=new GameObject("different parent",typeof(RectTransform));other.transform.SetParent(parent,false);
                node.name="artist renamed this";node.transform.SetParent(other.transform,false);
                probe.title=null;probe.buttons[1]=null;probe.rows.Clear();probe.labels.Clear();probe.nested=null;
                view.Restore(probe);
                Assert(probe.title==text,"renamed text binding");Assert(probe.buttons[1]==button,"readonly array binding");
                Assert(probe.rows.Count==1&&probe.rows[0][0]==text,"nested list/array binding");
                Assert(probe.rows[0].Length==2&&probe.rows[0][1]==null,"destroyed nested text skipped without losing slot count");
                Assert(probe.labels[CharacterSlot.Active]==text,"enum dictionary binding");
                Assert(probe.nested.skillNameText==text,"nested serialized view binding");
                Assert(text.fontSize==31&&text.color==Color.magenta,"individual typography/color preserved");
                Assert(rect.anchoredPosition==new Vector2(83,127)&&rect.sizeDelta==new Vector2(223,71),"layout preserved");
                int calls=0;RaidUIView.Bind(probe,"action",()=>calls++,button);RaidUIView.Bind(probe,"action",()=>calls++,button);
                button.onClick.Invoke();Assert(calls==1,"callback rebound exactly once");
                for(int i=0;i<5;i++)view.Restore(probe);
                Assert(probe.rows.Count==1&&probe.labels.Count==1,"repeat restoration does not accumulate rows");
                var style=node.AddComponent<RaidUIStyle>();style.graphic=text;RaidUIStyle.Color(text,Color.yellow);
                Assert(text.color==Color.magenta,"gameplay palette cannot overwrite authored color");
                style.useStateColors=true;style.locked=Color.blue;RaidUIStyle.State(text,false,true);Assert(text.color==Color.blue,"authored locked palette");
                Debug.Log("UI BINDING CHECKS PASS: 15 checks. Missing/destroyed references, rename/reparent, nested references, repeated restoration, callbacks and authored style.");
            }
            finally{UnityEngine.Object.DestroyImmediate(host);}
        }
        private static void Assert(bool condition,string name){if(!condition)throw new InvalidOperationException("UI check failed: "+name);}
    }
}
#endif
