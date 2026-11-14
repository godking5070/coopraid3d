using System;
using System.Collections.Generic;
using UnityEngine;
namespace RaidRogue
{
    // Stable control IDs allow settings to rebind callbacks to a fresh draft without recreating the form.
    public sealed class RaidUIForm : MonoBehaviour
    {
        [Serializable] public sealed class Control { public string id; public Component reference; }
        public string id;
        public RectTransform content;
        public List<Control> controls=new List<Control>();
        public T Find<T>(string key) where T:Component => controls.Find(x=>x.id==key)?.reference as T;
        public T Remember<T>(string key,T component) where T:Component
        {controls.Add(new Control{id=key,reference=component});return component;}
    }
}
