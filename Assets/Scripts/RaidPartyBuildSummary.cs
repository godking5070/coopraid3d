using System;
using System.Linq;

namespace RaidRogue
{
    // Read-only presentation of an actor's current/host-restored build; no requests or mutations.
    public sealed class RaidPartyBuildSummary
    {
        public string Job="동기화 대기", Role="—", Route="미선택";
        public string Combo="—", Utility="—", Identity="—", Tactical="—", RoleSkill="—", Core="—";
        public string Counter="카운터 정보 대기";
        public bool CounterEquipped;
        public static RaidPartyBuildSummary Read(CharacterDefinition definition, CharacterStateSnapshot state)
        {
            var result=new RaidPartyBuildSummary();
            if(definition==null||state==null||state.jobId!=definition.id)return result;
            result.Job=definition.name;
            result.Role=definition.role==CharacterRole.Tank?"탱커":definition.role==CharacterRole.Healer?"힐러":"딜러";
            result.Route=Array.Find(definition.routes,r=>r.id==state.routeId)?.name??(definition.routes.Length==0?"기원 없음":"미선택");
            result.Combo=SkillLabel(definition,state,CharacterSlot.Gcd2);
            result.Utility=SkillLabel(definition,state,CharacterSlot.OffGcd1);
            result.Identity=SkillLabel(definition,state,CharacterSlot.Identity);
            result.Tactical=SkillLabel(definition,state,CharacterSlot.OffGcd2);
            result.RoleSkill=SkillLabel(definition,state,CharacterSlot.Counter);
            result.Core=definition.Skill(state.coreTrait)?.name??"미선택";
            var counter=Selected(definition,state,CharacterSlot.Counter);
            bool counterSelected=counter?.implemented==true&&counter.isCounter;
            result.CounterEquipped=counterSelected&&(state.unlocked??Array.Empty<string>()).Contains(counter.id);
            result.Counter=result.CounterEquipped?"카운터 장착":counterSelected?"카운터 잠김":"카운터 미장착";
            return result;
        }
        private static CharacterSkillDefinition Selected(CharacterDefinition definition,CharacterStateSnapshot state,CharacterSlot slot)
        {
            if(!CharacterSlots.Enabled(definition,slot))return null;
            var selection=Array.Find(state.selections??Array.Empty<JobSlotSelection>(),s=>s.slot==slot);
            return selection==null?null:definition.Skill(selection.skillId);
        }
        private static string SkillLabel(CharacterDefinition definition,CharacterStateSnapshot state,CharacterSlot slot)
        {
            var skill=Selected(definition,state,slot);
            if(skill==null)return "미선택";
            return skill.name+(!skill.implemented?" (미구현)":!(state.unlocked??Array.Empty<string>()).Contains(skill.id)?" (잠김)":"");
        }
    }
}
