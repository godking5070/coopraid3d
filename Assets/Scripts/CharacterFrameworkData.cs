using System;
using System.Collections.Generic;
using System.Linq;

namespace RaidRogue
{
    // Values/IDs are protocol and authoring identities, never display names or array indexes.
    public enum CharacterSlot { Gcd1=1, Gcd2=2, OffGcd1=3, Counter=4, Job1=5, Job2=6, Movement=7, Identity=8, Active=9, OffGcd2=10, Ultimate=11, Potion=12 }
    public enum CharacterRole { Tank=1, Healer=2, Damage=3 }
    public enum AttackClassification { Melee, RangedPhysical, Caster }
    public enum SkillTargetRule { None, Self, LivingEnemy, LivingAlly, DeadAlly }
    public enum CharacterReset { Retry, NextStage, NewRun }
    [Serializable] public sealed class JobResourceDefinition
    { public string id, name; public float maximum=100, initial; public bool integer, resetOnRetry=true, resetOnStage=true; }
    [Serializable] public sealed class ResourceCost { public string resourceId; public float amount; }
    [Serializable] public sealed class JobMovementDefinition
    {
        public float distance=4, duration=.2f, commonDistanceMultiplier=1;
        public bool preserveCast, useCommonDistance;
        public float ResolveDistance(float commonDistance)=>(useCommonDistance?commonDistance:distance)*commonDistanceMultiplier;
    }
    [Serializable] public sealed class CharacterSkillDefinition
    {
        public string id, name, description, handlerId, iconKey;
        public bool implemented, usesGcd, cancelOnMovement=true, usableWhileCasting;
        public float range=20, castTime, cooldown, gcd=2, comboWindow=8;
        public int maximumCharges=1, comboChain, legacyAction;
        public SkillTargetRule target;
        public ResourceCost[] costs=Array.Empty<ResourceCost>();
        public JobMovementDefinition movement;
    }
    [Serializable] public sealed class CharacterSlotDefinition
    { public CharacterSlot slot; public string skillId; public bool initiallyUnlocked=true; }
    [Serializable] public sealed class CharacterRouteDefinition
    { public string id, name, identitySkillId; public string[] unlockSkills=Array.Empty<string>(); public ResourceCost[] capacityOverrides=Array.Empty<ResourceCost>(); }
    [Serializable] public sealed class CharacterUnlockDefinition
    { public string id; public string chapterId=CharacterRewardKey.CurrentChapter; public CharacterSlot slot; public int afterStage; public string[] choices=Array.Empty<string>(); }
    [Serializable] public sealed class CharacterDefinition
    {
        public string id, name, description, visualKey, iconKey;
        public bool playable; public int legacyRole;
        public CharacterRole role; public AttackClassification classification;
        public float baseHp=1000, moveSpeed=7.8f, combatRadius=.55f;
        public float moveSpeedMultiplier=1, physicalDefense, magicDefense, tenacity;
        public CharacterSlotDefinition[] slots=Array.Empty<CharacterSlotDefinition>();
        public CharacterSkillDefinition[] skills=Array.Empty<CharacterSkillDefinition>();
        public JobResourceDefinition[] resources=Array.Empty<JobResourceDefinition>();
        public CharacterRouteDefinition[] routes=Array.Empty<CharacterRouteDefinition>();
        public CharacterUnlockDefinition[] unlocks=Array.Empty<CharacterUnlockDefinition>();
        public CharacterSkillDefinition Skill(string skillId) => Array.Find(skills, s => s != null && s.id == skillId);
    }
    public static class CharacterSlots
    {
        public static readonly CharacterSlot[] All = (CharacterSlot[])Enum.GetValues(typeof(CharacterSlot));
        public static string Binding(CharacterSlot slot) => slot switch {
            CharacterSlot.Gcd1=>"Skill1", CharacterSlot.Gcd2=>"Skill2", CharacterSlot.OffGcd1=>"Skill3", CharacterSlot.Counter=>"Counter",
            CharacterSlot.Job1=>"Skill4", CharacterSlot.Job2=>"JobSkill2", CharacterSlot.Movement=>"Dash", CharacterSlot.Identity=>"Identity",
            CharacterSlot.Active=>"SelectedActive", CharacterSlot.OffGcd2=>"SelectedOffGcd", CharacterSlot.Ultimate=>"Ultimate", CharacterSlot.Potion=>"Potion", _=>"" };
        public static string Name(CharacterSlot slot) => slot switch {
            CharacterSlot.Gcd1=>"GCD 1", CharacterSlot.Gcd2=>"GCD 2", CharacterSlot.OffGcd1=>"oGCD 1", CharacterSlot.Counter=>"카운터",
            CharacterSlot.Job1=>"직업 스킬 1", CharacterSlot.Job2=>"직업 스킬 2", CharacterSlot.Movement=>"직업 이동기", CharacterSlot.Identity=>"아이덴티티",
            CharacterSlot.Active=>"선택형 액티브", CharacterSlot.OffGcd2=>"선택형 oGCD", CharacterSlot.Ultimate=>"궁극기", CharacterSlot.Potion=>"공통 포션", _=>"알 수 없음" };
        public static CharacterSlot LegacySlot(int action) => action == 4 ? CharacterSlot.Job1 : action == 6 ? CharacterSlot.Potion : (CharacterSlot)action;
    }
    public static class CharacterDefinitionValidation
    {
        public static string[] Check(IEnumerable<CharacterDefinition> definitions)
        {
            var errors = new List<string>(); var ids = new HashSet<string>();
            foreach (var d in definitions)
            {
                if (d == null || string.IsNullOrWhiteSpace(d.id) || !ids.Add(d.id)) { errors.Add("중복/누락 직업 ID"); continue; }
                if(d.resources==null||d.skills==null||d.slots==null||d.routes==null||d.unlocks==null){errors.Add(d.id+": 정의 배열 누락");continue;}
                var skills = new HashSet<string>(); var resources = new HashSet<string>(); var slots = new HashSet<CharacterSlot>();
                foreach (var r in d.resources ?? Array.Empty<JobResourceDefinition>())
                    if (r == null || string.IsNullOrEmpty(r.id) || !resources.Add(r.id) || !Finite(r.maximum) || r.maximum <= 0 || !Finite(r.initial) || r.initial < 0 || r.initial > r.maximum || r.integer && (r.maximum != Math.Floor(r.maximum) || r.initial != Math.Floor(r.initial))) errors.Add(d.id+": 잘못된 자원");
                foreach (var s in d.skills ?? Array.Empty<CharacterSkillDefinition>())
                {
                    if (s == null || string.IsNullOrEmpty(s.id) || !skills.Add(s.id)) { errors.Add(d.id+": 중복/누락 스킬 ID"); continue; }
                    if (!Finite(s.range) || s.range < 0 || !Finite(s.cooldown) || s.cooldown < 0 || !Finite(s.castTime) || s.castTime < 0 || !Finite(s.gcd) || s.gcd < 0 || !Finite(s.comboWindow) || s.comboWindow < 0 || s.maximumCharges < 1 || s.maximumCharges > 10 || s.comboChain < 0 || s.comboChain > 2) errors.Add(d.id+": 잘못된 스킬 수치");
                    if (s.costs == null) errors.Add(d.id+": 자원 비용 배열 누락");
                    if (s.legacyAction < 0 || s.legacyAction > 6 || s.legacyAction == 5 || d.legacyRole == 0 && s.legacyAction > 0 && s.legacyAction != 6) errors.Add(d.id+": 기존 테스트 전용 실행기는 테스트 직업에서만 사용합니다");
                    if (s.implemented && string.IsNullOrWhiteSpace(s.handlerId)) errors.Add(d.id+": 실행 처리기 누락");
                    foreach (var c in s.costs ?? Array.Empty<ResourceCost>()) if (c == null || !resources.Contains(c.resourceId) || !Finite(c.amount) || c.amount < 0) errors.Add(d.id+": 잘못된 자원 비용");
                    if (s.movement != null && (!Finite(s.movement.distance) || s.movement.distance <= 0 || !Finite(s.movement.duration) || s.movement.duration <= 0 || !Finite(s.movement.commonDistanceMultiplier) || s.movement.commonDistanceMultiplier<=0)) errors.Add(d.id+": 잘못된 이동기");
                }
                foreach (var slot in d.slots ?? Array.Empty<CharacterSlotDefinition>())
                    if (slot == null || !Enum.IsDefined(typeof(CharacterSlot),slot.slot) || !slots.Add(slot.slot) || !skills.Contains(slot.skillId)) errors.Add(d.id+": 중복 슬롯/누락 스킬 참조");
                var options = new HashSet<string>();
                foreach (var route in d.routes ?? Array.Empty<CharacterRouteDefinition>())
                    if (route == null || route.unlockSkills == null || string.IsNullOrEmpty(route.id) || !options.Add(route.id) || (!string.IsNullOrEmpty(route.identitySkillId) && (!skills.Contains(route.identitySkillId)||!slots.Contains(CharacterSlot.Identity))) || (route.unlockSkills ?? Array.Empty<string>()).Any(s => !skills.Contains(s))) errors.Add(d.id+": 잘못된 루트 참조");
                foreach(var route in d.routes??Array.Empty<CharacterRouteDefinition>())if(route?.capacityOverrides!=null)
                {
                    var seenCaps=new HashSet<string>();
                    foreach(var cap in route.capacityOverrides)
                    {
                        var resource=Array.Find(d.resources??Array.Empty<JobResourceDefinition>(),v=>v?.id==cap?.resourceId);
                        if(cap==null||resource==null||!seenCaps.Add(cap.resourceId)||!Finite(cap.amount)||cap.amount<=0||resource.integer&&cap.amount!=Math.Floor(cap.amount))errors.Add(d.id+": 잘못된 루트 자원 상한");
                    }
                }
                options.Clear();
                foreach (var rule in d.unlocks ?? Array.Empty<CharacterUnlockDefinition>())
                    if (rule == null || string.IsNullOrEmpty(rule.id) || !CharacterRewardKey.ValidId(rule.chapterId) || !options.Add(rule.id) || rule.afterStage < 1 || !slots.Contains(rule.slot) || rule.choices == null || rule.choices.Length == 0 || rule.choices.Distinct().Count()!=rule.choices.Length || rule.choices.Any(s => !skills.Contains(s))) errors.Add(d.id+": 잘못된 성장 참조");
                if (!Finite(d.combatRadius) || d.combatRadius <= 0 || !Finite(d.baseHp) || d.baseHp <= 0 || !Finite(d.moveSpeed) || d.moveSpeed <= 0) errors.Add(d.id+": 잘못된 기본 스탯");
                if(!Finite(d.moveSpeedMultiplier)||d.moveSpeedMultiplier<=0||!Finite(d.physicalDefense)||d.physicalDefense<0||!Finite(d.magicDefense)||d.magicDefense<0||!Finite(d.tenacity)||d.tenacity<0)errors.Add(d.id+": 잘못된 방어/이동 보정");
            }
            return errors.ToArray();
        }
        public static bool Finite(float n) => !float.IsNaN(n) && !float.IsInfinity(n);
    }
    public static class CharacterCatalog
    {
        private static readonly List<CharacterDefinition> Entries = new List<CharacterDefinition>(Build());
        public static void ResetCatalog() { Entries.Clear(); Entries.AddRange(Build()); }
        public static IReadOnlyList<CharacterDefinition> All => Entries.AsReadOnly();
        public static CharacterDefinition Find(string id) => Entries.Find(d => d.id == id);
        public static CharacterDefinition Legacy(int role) => Entries.Find(d => d.legacyRole == role && d.playable);
        public static bool Register(CharacterDefinition definition,out string error,bool implementsRegisteredJob=false)
        {
            var existing=definition==null?null:Find(definition.id);
            bool replace=implementsRegisteredJob && existing!=null && !existing.playable && existing.legacyRole==0 && definition.playable;
            if(implementsRegisteredJob && (!replace || existing.role!=definition.role || existing.classification!=definition.classification))
            { error="미구현 등록 직업의 ID/역할/분류를 유지한 구현 정의가 필요합니다";return false; }
            var all=new List<CharacterDefinition>(Entries);if(replace)all.Remove(existing);all.Add(definition);
            var errors=CharacterDefinitionValidation.Check(all);error=string.Join("; ",errors);if(errors.Length>0)return false;
            if(replace)Entries[Entries.IndexOf(existing)]=definition;else Entries.Add(definition);return true;
        }
        private static CharacterDefinition[] Build()
        {
            var list = new List<CharacterDefinition>();
            string[] ids={"aegis-paladin","berserker","dragon-breaker","sylvan-oracle","seraphic-bard","blood-apothecary","nightblade","tempest-lancer","gunslinger","spirit-ranger","astromancer","mirror-phantom"};
            string[] names={"이지스 팔라딘","버서커","드래곤 브레이커","실반 오라클","세라픽 바드","블러드 아포테카리","나이트블레이드","템페스트 랜서","건슬링어","스피릿 레인저","아스트로맨서","미러 팬텀"};
            for (int i=0;i<ids.Length;i++)
            {
                var d=BuildDefinition("job."+ids[i],names[i],false,0,i<3?CharacterRole.Tank:i<6?CharacterRole.Healer:CharacterRole.Damage,i<3||i>=6&&i<8?AttackClassification.Melee:i>=8&&i<10?AttackClassification.RangedPhysical:AttackClassification.Caster);
                list.Add(d);
            }
            string[] legacyIds={"guardian","oracle","blade","archmage"}; string[] legacyNames={"가디언 (기존 테스트)","오라클 (기존 테스트)","블레이드 (기존 테스트)","아크메이지 (기존 테스트)"};
            for (int role=1;role<=4;role++) list.Add(BuildDefinition("test."+legacyIds[role-1],legacyNames[role-1],true,role,role==1?CharacterRole.Tank:role==2?CharacterRole.Healer:CharacterRole.Damage,role==1||role==3?AttackClassification.Melee:AttackClassification.Caster));
            string[] errors=CharacterDefinitionValidation.Check(list); if (errors.Length>0) throw new InvalidOperationException(string.Join(";",errors));
            return list.ToArray();
        }
        private static CharacterDefinition BuildDefinition(string id,string name,bool legacy,int role,CharacterRole family,AttackClassification classification)
        {
            var d=new CharacterDefinition { id=id,name=name,description=legacy?"기존 전투 처리기를 재사용하는 테스트 캐릭터":"등록 완료 · 스킬 미구현 · 전투 선택 불가",playable=legacy,legacyRole=role,baseHp=legacy?(role==1?3000:role==2?1100:1000):1000,role=family,classification=classification,visualKey=legacy?"legacy.role."+role:"",iconKey="" };
            var skills=new List<CharacterSkillDefinition>(); var slots=new List<CharacterSlotDefinition>();
            foreach (var slot in CharacterSlots.All)
            {
                int action=slot==CharacterSlot.Gcd1?1:slot==CharacterSlot.Gcd2?2:slot==CharacterSlot.OffGcd1?3:slot==CharacterSlot.Job1?4:slot==CharacterSlot.Potion?6:0;
                if(!legacy&&slot!=CharacterSlot.Potion)action=0;
                bool implemented=slot==CharacterSlot.Potion || legacy && (action>0||slot==CharacterSlot.Movement);
                var skill=new CharacterSkillDefinition { id=id+".slot."+slot.ToString().ToLowerInvariant(),name=CharacterSlots.Name(slot),implemented=implemented,
                    handlerId=action>0?"legacy.action."+action:slot==CharacterSlot.Movement&&legacy?"legacy.dash":"",legacyAction=action,
                    usableWhileCasting=slot==CharacterSlot.Movement||slot==CharacterSlot.Potion,usesGcd=slot==CharacterSlot.Gcd1||slot==CharacterSlot.Gcd2,comboChain=slot==CharacterSlot.Gcd1?1:slot==CharacterSlot.Gcd2?2:0,
                    target=slot==CharacterSlot.Gcd1||slot==CharacterSlot.Gcd2?SkillTargetRule.LivingEnemy:SkillTargetRule.Self,
                    cooldown=slot==CharacterSlot.Movement?5:slot==CharacterSlot.Potion?30:0,movement=slot==CharacterSlot.Movement?new JobMovementDefinition():null };
                skills.Add(skill); slots.Add(new CharacterSlotDefinition { slot=slot,skillId=skill.id,initiallyUnlocked=slot<CharacterSlot.Identity||slot==CharacterSlot.Potion });
            }
            d.skills=skills.ToArray();d.slots=slots.ToArray();
            if (!legacy)
            {
                string identity=slots.Find(s=>s.slot==CharacterSlot.Identity).skillId;
                d.routes=new[]{new CharacterRouteDefinition { id=id+".route.a",name="루트 A (구현 대기)",identitySkillId=identity,unlockSkills=new[]{identity} },new CharacterRouteDefinition { id=id+".route.b",name="루트 B (구현 대기)",identitySkillId=identity,unlockSkills=new[]{identity} }};
                var ultimate=slots.Find(s=>s.slot==CharacterSlot.Ultimate).skillId;
                skills.Add(new CharacterSkillDefinition { id=ultimate+".b",name="궁극기 B (구현 대기)" }); d.skills=skills.ToArray();
                d.unlocks=new[]{ new CharacterUnlockDefinition { id=id+".stage1",slot=CharacterSlot.Active,afterStage=1,choices=new[]{slots.Find(s=>s.slot==CharacterSlot.Active).skillId} },
                    new CharacterUnlockDefinition { id=id+".stage2",slot=CharacterSlot.OffGcd2,afterStage=2,choices=new[]{slots.Find(s=>s.slot==CharacterSlot.OffGcd2).skillId} },
                    new CharacterUnlockDefinition { id=id+".stage3",slot=CharacterSlot.Ultimate,afterStage=3,choices=new[]{ultimate,ultimate+".b"} } };
            }
            return d;
        }
    }
    public static class CharacterRewardKey
    {
        public const string CurrentChapter="main";
        public static string Stage(string run,string chapter,int stage)=>run+"/"+chapter+"/"+stage;
        public static string Choice(string run,string chapter,int stage,string rule)=>Stage(run,chapter,stage)+"/"+rule;
        public static bool ValidId(string id)=>!string.IsNullOrWhiteSpace(id)&&id.Length<=100&&!id.Contains("/");
    }
}
