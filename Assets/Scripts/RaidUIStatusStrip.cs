using System.Collections.Generic;
using UnityEngine;
namespace RaidRogue
{
    // Optional presentation of the common combat statuses already displayed in the character HUD.
    public sealed class RaidUIStatusStrip : MonoBehaviour
    {
        public RectTransform items;
        public RaidUIRepeatedItem templateOverride;
        private SkillHotbarUI _hotbar;
        private readonly Dictionary<string,RaidUIRepeatedItem> _pool=new Dictionary<string,RaidUIRepeatedItem>();
        private float _next;
        private void Start(){_hotbar=GetComponentInParent<SkillHotbarUI>();}
        private void Update()
        {
            if(Time.unscaledTime<_next)return;_next=Time.unscaledTime+.2f;
            var status=_hotbar?.combatController?.GetComponent<CombatStatusEffects>();
            Show("status.stun",status?.Stunned==true,"기절");
            Show("status.root",status?.Rooted==true,"속박");
            Show("status.morning",status?.MorningActive==true,"아침의 성역");
            Show("status.invulnerable",status?.CombatInvulnerable==true,"무적");
            Show("status.knockback-immunity",status!=null&&status.KnockbackImmunity>0,"넉백 면역");
        }
        private void Show(string id,bool active,string caption)
        {
            if(items==null)return;
            if(!_pool.TryGetValue(id,out var view))
            {
                if(!active)return;
                var template=templateOverride!=null?templateOverride:RaidUIPresentation.Settings?.statusTemplate;
                view=RaidUIRepeatedItem.Create(template,items);if(view==null)return;_pool[id]=view;
            }
            view.gameObject.SetActive(active);if(!active)return;
            view.icon?.Show(id);if(view.label!=null)view.label.text=caption;
        }
    }
}
