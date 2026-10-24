using System;
using System.Collections.Generic;
using System.Linq;

namespace RaidRogue
{
    // Authored numbers, independent of Unity and of every player's mutable state.
    public sealed class RosterSkill
    {
        public string key, name, description;
        public string shape = "target";
        public int hits = 1;
        public float angle = 90;
        public float cooldown, range = 15, power = 200, stagger = 25, duration, radius = 3, interval = 1, cast;
        public SkillTargetRule target = SkillTargetRule.LivingEnemy;
        public RosterSkill(string key, string name, float cooldown, string description, float power = 200, float range = 15, float duration = 0, float radius = 3)
        {
            this.key = key;
            this.name = name;
            this.cooldown = cooldown;
            this.description = description;
            this.power = power;
            this.range = range;
            this.duration = duration;
            this.radius = radius;
        }
    }

    public sealed class RosterJob
    {
        public string slug, name, theme, weapon, resource, disadvantage;
        public int index, capacity = 100;
        public float hp = 1200, defense = 30, speed = 1, range = 3, gcd1 = 1.5f, gcd2 = 2.5f, dashCooldown = 8, dashDistance = 4;
        public int dashCharges = 1;
        public bool fractional;
        public string[] combo1, combo2, origins, passives;
        public float[] identityCosts;
        public RosterSkill[] abilities;
        public string Id => "job." + slug;
        public string Resource => Id + ".resource";
        public int Role => index <= 2 ? 1 : index <= 5 ? 2 : index <= 7 ? 3 : 4;

        public string SkillId(string key) => Id + "." + key;
        public RosterSkill Ability(string key) => Array.Find(abilities, a => a.key == key);
    }

    public static class CharacterRosterData
    {
        public const string Handler = "roster.skill";
        public static readonly RosterJob[] Jobs = BuildJobs();
        public static RosterJob Find(string id) => Array.Find(Jobs, j => j.Id == id);
        public static string Key(string skill) => skill.Substring(skill.LastIndexOf('.') + 1);
        // Three instant follow-ups share the original input transaction, but have distinct authored effects.
        public static RosterSkill Ninjutsu(string trigger) => trigger == "g1" ? new RosterSkill("ninjutsu.fire", "화둔 대폭발", 0, "반경 5m 화염 폭발", 800, 15, 0, 5)
        {
            stagger = 70
        }

        : trigger == "g2" ? new RosterSkill("ninjutsu.thunder", "뇌둔 낙뢰", 0, "단일 확정 치명 낙뢰", 900, 15, 0, 0)
        {
            stagger = 100
        }

        : new RosterSkill("ninjutsu.water", "수둔 얼음수리검", 0, "관통 수리검", 650, 15, 0, 0)
        {
            shape = "line",
            stagger = 80
        };
        public static int Role(CharacterDefinition d) => d.legacyRole > 0 ? d.legacyRole : d.role == CharacterRole.Tank ? 1 : d.role == CharacterRole.Healer ? 2 : d.classification == AttackClassification.Melee ? 3 : 4;
        public static string RouteDescription(CharacterDefinition d, string route)
        {
            if (d.id == AegisPaladinData.JobId)
                return AegisRouteData.Description(route);
            var j = Find(d.id);
            int i = Array.FindIndex(d.routes, r => r.id == route);
            if (j == null || i < 0)
                return "";
            var s = d.Skill(d.routes[i].identitySkillId);
            return j.origins[i] + "\n" + j.passives[i] + "\n\n" + s.name + "\n" + s.description + "\n자원 " + j.identityCosts[i] + " · 재사용 " + s.cooldown + "초";
        }

        public static CharacterDefinition Definition(RosterJob j)
        {
            var d = new CharacterDefinition
            {
                id = j.Id,
                name = j.name,
                description = j.theme + " · " + j.weapon + " · 초기 전투 구현",
                playable = true,
                role = j.Role == 1 ? CharacterRole.Tank : j.Role == 2 ? CharacterRole.Healer : CharacterRole.Damage,
                classification = j.Role == 1 || j.Role == 3 ? AttackClassification.Melee : j.index == 8 || j.index == 9 ? AttackClassification.RangedPhysical : AttackClassification.Caster,
                baseHp = j.hp,
                moveSpeed = 7.8f,
                moveSpeedMultiplier = j.speed,
                physicalDefense = j.defense,
                magicDefense = j.defense,
                tenacity = j.Role == 1 ? 80 : 20,
                visualKey = j.slug + ".primitives"
            };
            var resources = new List<JobResourceDefinition>
            {
                new JobResourceDefinition
                {
                    id = j.Resource,
                    name = j.resource,
                    maximum = j.capacity,
                    integer = !j.fractional
                }
            };
            if (j.Role == 2)
            {
                resources.Add(new JobResourceDefinition { id = j.Id + ".mana", name = "마나", maximum = 100, initial = 100 });
                resources.Add(new JobResourceDefinition { id = j.Id + ".awakening", name = "치유 각성", maximum = 3, integer = true });
            }

            d.resources = resources.ToArray();
            var skills = new List<CharacterSkillDefinition>();
            var slots = new List<CharacterSlotDefinition>();
            string[] keys =
            {
                "g1",
                "g2",
                "a",
                "c",
                "q",
                "e",
                "m",
                "z1",
                "r1",
                "f1",
                "v1",
                "p"
            };
            foreach (var a in j.abilities)
            {
                var s = new CharacterSkillDefinition
                {
                    id = j.SkillId(a.key),
                    name = a.name,
                    description = a.description + "\n사거리 " + a.range + "m · 쿨타임 " + a.cooldown + "초",
                    handlerId = Handler,
                    implemented = true,
                    range = a.range,
                    cooldown = a.cooldown,
                    castTime = a.cast,
                    target = a.target,
                    comboWindow = 8
                };
                if (a.key == "g1" || a.key == "g2")
                {
                    s.usesGcd = true;
                    s.comboChain = a.key == "g1" ? 1 : 2;
                    s.gcd = a.key == "g1" ? j.gcd1 : j.gcd2;
                }

                if (a.key == "m")
                {
                    s.handlerId = "legacy.dash";
                    s.usableWhileCasting = true;
                    s.maximumCharges = j.dashCharges;
                    s.movement = new JobMovementDefinition
                    {
                        distance = j.dashDistance,
                        duration = .2f,
                        preserveCast = j.index == 10
                    };
                }

                if (a.key == "a" && (j.index == 2 || j.index == 6))
                    s.maximumCharges = j.index == 2 ? 2 : 3;
                if (a.key.StartsWith("z"))
                {
                    int route = int.Parse(a.key.Substring(1)) - 1;
                    s.costs = new[]
                    {
                        new ResourceCost
                        {
                            resourceId = j.Resource,
                            amount = j.identityCosts[route]
                        }
                    };
                }

                if (j.Role == 2 && (a.key == "q" || a.key == "e"))
                {
                    s.castTime = 2;
                    s.costs = new[]
                    {
                        new ResourceCost
                        {
                            resourceId = j.Id + ".mana",
                            amount = a.key == "q" ? 25 : 40
                        }
                    };
                }

                skills.Add(s);
            }

            skills.Add(new CharacterSkillDefinition { id = j.SkillId("p"), name = "공통 포션", implemented = true, handlerId = "legacy.action.6", legacyAction = 6, cooldown = 30, usableWhileCasting = true, target = SkillTargetRule.Self });
            for (int i = 0; i < keys.Length; i++)
                slots.Add(new CharacterSlotDefinition { slot = (CharacterSlot)(i + 1), skillId = j.SkillId(keys[i]), initiallyUnlocked = i < 7 || i == 11 });
            d.skills = skills.ToArray();
            d.slots = slots.ToArray();
            d.routes = Enumerable.Range(1, 3).Select(i => new CharacterRouteDefinition { id = j.Id + ".origin." + i, name = j.origins[i - 1], identitySkillId = j.SkillId("z" + i), unlockSkills = new[] { j.SkillId("z" + i) } }).ToArray();
            if (j.index == 2)
                d.routes[0].capacityOverrides = new[]
                {
                    new ResourceCost
                    {
                        resourceId = j.Resource,
                        amount = 6
                    }
                };
            d.unlocks = new[]
            {
                new CharacterUnlockDefinition
                {
                    id = j.Id + ".stage1",
                    afterStage = 1,
                    slot = CharacterSlot.Active,
                    choices = new[]
                    {
                        j.SkillId("r1"),
                        j.SkillId("r2"),
                        j.SkillId("r3")
                    }
                },
                new CharacterUnlockDefinition
                {
                    id = j.Id + ".stage2",
                    afterStage = 2,
                    slot = CharacterSlot.OffGcd2,
                    choices = new[]
                    {
                        j.SkillId("f1"),
                        j.SkillId("f2"),
                        j.SkillId("f3")
                    }
                },
                new CharacterUnlockDefinition
                {
                    id = j.Id + ".stage3",
                    afterStage = 3,
                    slot = CharacterSlot.Ultimate,
                    choices = new[]
                    {
                        j.SkillId("v1"),
                        j.SkillId("v2")
                    }
                }
            };
            return d;
        }

        private static RosterSkill S(string k, string n, float cd, string desc, float p = 200, float r = 15, float t = 0, float area = 3) => new RosterSkill(k, n, cd, desc, p, r, t, area);
        private static RosterSkill Self(RosterSkill s)
        {
            s.target = SkillTargetRule.Self;
            return s;
        }

        private static RosterSkill Ally(RosterSkill s)
        {
            s.target = SkillTargetRule.LivingAlly;
            return s;
        }

        private static RosterJob[] BuildJobs()
        {
            var jobs = new List<RosterJob>();
            jobs.Add(new RosterJob { index = 1, slug = "berserker", name = "버서커", theme = "정오 · 열혈", weapon = "대검 · 붉은 사자 갈기 망토", resource = "정오의 열기", hp = 3600, defense = 85, combo1 = new[] { "단두대 가르기", "흉골 찌르기", "분노의 처형" }, combo2 = new[] { "야수의 휩쓸기", "폭풍 회전참", "대지 분쇄파" }, origins = new[] { "불사신 (언다잉)", "폭군의 단두대", "피의 폭풍" }, passives = new[] { "잃은 HP에 따라 방어·흡혈 증가", "느린 공격 · 콤보 3타 차징 및 피해 강화", "빠른 공격 · 출혈 중첩" }, identityCosts = new[] { 100f, 50, 0 }, abilities = new[] { S("g1", "단두대 가르기", 0, "3타: 출혈 8초 · 방어력 -5%", 180, 3), S("g2", "야수의 휩쓸기", 0, "360도 · 3타 실제 피해 30% 흡혈", 200, 3, 0, 4), S("a", "투기 폭발", 10, "주변 즉발 폭발", 220, 4, 0, 4), S("c", "대지 퍼올리기", 10, "슈퍼아머 상단 카운터", 200, 3), Self(S("q", "야수의 심장", 25, "10초 HP +30% · 피해 -30% · 받는 치유/흡혈 +25%", 0, 0, 10)), S("e", "포식자의 포효", 20, "도발 · 적 공격력 -10% 8초", 0, 20, 8), Self(S("m", "맹수의 도약", 8, "슈퍼아머 도약 후 타격", 120, 4)), S("r1", "지진 해일 찍기", 16, "도약 강타 · 최상 무력화", 550, 12), S("r2", "피의 갈증", 20, "2초 6연격 · 타격마다 잃은 HP 5% 회복", 85, 3, 2), S("r3", "투기 사슬 그랩", 15, "사슬로 적 앞에 접근하여 타격", 300, 18), Self(S("f1", "끓어오르는 피", 20, "현재 HP 15% 소모 · 열기 +50 · 다음 공격 확정 치명", 0)), S("f2", "두개골 박치기", 12, "무력화 · 3초 공속 +20%", 220, 3, 3), Self(S("f3", "혈기 전환 갑주", 18, "최근 5초 유효 흡혈량만큼 보호막", 0, 0, 6)), S("v1", "태양의 정점: 라그나로크", 120, "거대 대검 2회 참격", 1100, 8), S("v2", "백주대낮의 포효", 120, "타격 · 파티 치명 +15%p 및 흡혈 12초", 1200, 20, 12), Self(S("z1", "불굴의 투혼", 1, "6초 HP 1 이하로 감소하지 않음 · 확정 치명 · 흡혈 3배", 0, 0, 6)), S("z2", "처형자의 일격", 1, "1.5초 슈퍼아머 차징 · 최상 무력화", 1200, 4), Self(S("z3", "적색 회오리", 1, "누르는 동안 이동 회전 · 열기 초당 20 · 받는 피해 -25%", 100, 4, 5, 4)) } });
            jobs.Add(new RosterJob { index = 2, slug = "dragon-breaker", name = "드래곤 브레이커", theme = "황혼 · 용염", weapon = "용골 창포 · 화염 건틀릿", resource = "용염 탄창", capacity = 4, hp = 3100, defense = 95, dashCooldown = 9, combo1 = new[] { "용아 찌르기", "화염 할퀴기", "브레스 장전" }, combo2 = new[] { "영거리 화염포", "용 꼬리 후리기", "볼케이노 버스트" }, origins = new[] { "포격의 화신", "홍련의 연속검", "불 꺼지지 않는 가마" }, passives = new[] { "최대 탄창 6 · 포격 범위/무력화 강화", "포격 반동 삭제 · 초고속 위빙", "누적 화상 · 파티 보호" }, identityCosts = new[] { 0f, 2, 2 }, abilities = new[] { S("g1", "용아 찌르기", 0, "3타 탄창 +1 · 이동 속도 증가", 150, 3), S("g2", "영거리 화염포", 0, "1·3타 탄창 1 소모 · 높은 무력화 · 반동 0.6초", 250, 4), S("a", "용격포 사출", 7, "2회 충전 · 즉발 화염 말뚝", 230, 8), S("c", "용의 역린", 10, "전방 접근 산탄 카운터", 210, 8), Self(S("q", "화룡의 비늘 갑주", 24, "8초 자신 피해 -35% · 주변 아군 -15%", 0, 0, 8, 8)), S("e", "황혼의 인화탄", 20, "도발 · 8초 파티 적중 시 화염 추가타", 0, 20, 8), Self(S("m", "플레임 부스터", 9, "직선 화염 돌진", 0)), S("r1", "화룡의 숨결", 16, "2.5초 이동 가능 전방 지속 화염/무력화", 90, 8, 2.5f, 8), S("r2", "메테오 파일드라이버", 18, "적에게 도약하여 수직 낙하 폭발", 600, 15), Self(S("r3", "마그마 방벽 전개", 25, "5초 전방 방벽 · 투사체 방어 · 주변 보호막", 0, 0, 5, 6)), Self(S("f1", "블러드 페스트", 25, "과열 해제 · 탄창 가득 · 5초 포격 +20%", 0, 0, 5)), S("f2", "역분사 백스텝 포격", 12, "후방 이동 · 0.8초 무적", 250, 10), S("f3", "심장부 점화", 14, "화상 폭발 · 파티 이동 +20% 5초", 250, 20, 5), S("v1", "황혼의 종말: 이그니스", 120, "스킬/탄창 초기화 · 화룡 브레스", 2000, 20), S("v2", "일몰의 대지", 120, "12초 적 방어 -15% · 받는 무력화 +20%", 1000, 20, 12), S("z1", "오버버스트: 전탄 발사", 1, "보유 탄창 전체 소모 · 탄환 수 비례 포격", 380, 6), S("z2", "황혼의 어금니", 1, "3연격 · 각 타격 후 용격포 무료 충전", 300, 6), Self(S("z3", "화룡의 요람", 1, "8초 주변 아군 피해 -20% · 매초 보호막", 0, 0, 8, 8)) } });
            jobs.Add(new RosterJob { index = 3, slug = "sylvan-oracle", name = "실반 오라클", theme = "봄 · 세계수", weapon = "살아 있는 목재 지팡이 · 생명의 경전", resource = "세계수의 꽃봉오리", capacity = 3, fractional = true, hp = 900, defense = 15, range = 20, dashCooldown = 12, combo1 = new[] { "빛의 잎사귀", "정령의 숨결", "세계수의 자각" }, combo2 = new[] { "가시 덩굴 채찍", "대지의 맥박", "신록의 폭풍" }, origins = new[] { "세계수의 대리자", "계절의 인도자", "가시나무 마녀" }, passives = new[] { "치유 +30% · 만피 치유 시 최대 HP 증가", "관문 이동 · 치유와 일반 스킬 쿨감", "지속 피해에 비례한 자동 치유" }, identityCosts = new[] { 1f, 1, 3 }, abilities = new[] { S("g1", "빛의 잎사귀", 0, "3타 치유 각성 · 꽃봉오리 0.5", 120, 20), S("g2", "가시 덩굴 채찍", 0, "3타 적 주변 15m 아군 HP 5% 회복 · 치유 각성", 170, 20, 0, 4), S("a", "꽃보라 파열", 9, "적 타격 · 최저 HP 아군에게 6초 지속 회복", 190, 20, 6), S("c", "고목의 뿌리 방벽", 10, "전방 뿌리 마법 카운터", 150, 15), Ally(S("q", "생명의 개화", 8, "대량 치유 · 피해 -20%와 지속 회복 6초", 0, 25, 6)), Self(S("e", "대자연의 축복", 14, "파티 대량 회복 · 지속 회복 8초", 0, 0, 8, 40)), Self(S("m", "요정의 바람걸음", 12, "꽃바람 이동 · 3초 안 재입력 시 귀환", 0)), Ally(S("r1", "구원의 덩굴손", 30, "아군을 옆자리로 구출 · 보호막", 0, 25)), S("r2", "신성한 숲의 성역", 20, "대상 위치 10초 장판 · 피해 -15% · 지속 치유", 0, 25, 10, 6), S("r3", "태양광선", 16, "직선 광선 · 무력화 · 명중률 감소", 430, 25), Self(S("f1", "만개의 외침", 15, "파티에 부여된 직업 지속 회복 잔여량 즉시 회복", 0)), Self(S("f2", "청명한 아침 이슬", 12, "파티 디버프 1개 정화 · 이동 +25% 4초", 0, 0, 4)), S("f3", "기생하는 겨우살이", 14, "6초 적 타격 시 파티 흡혈", 150, 25, 6), S("v1", "만개하는 봄의 기적: 에덴의 빛", 120, "세계수 타격 · 파티 HP 100% 회복 · 초과량 보호막", 1400, 25), S("v2", "시간의 순풍", 120, "타격 · 12초 파티 공격/시전/GCD 15% 가속", 900, 25, 12), Self(S("z1", "생명의 요람", 1, "파티 즉시 치유 · 최대 HP +15% 8초", 0, 0, 8)), Self(S("z2", "봄의 관문", 1, "꽃 관문 접촉 시 시전자에게 이동 · 치유 · 일반 쿨타임 -3초", 0, 0, 8, 2)), S("z3", "핏빛 장미의 심판", 1, "핏빛 가시꽃 · 강력한 피해와 무력화", 1800, 25) } });
            jobs.Add(new RosterJob { index = 4, slug = "seraphic-bard", name = "세라픽 바드", theme = "여름 · 천상의 성가", weapon = "천사 날개 하프 · 빛의 지휘봉", resource = "화음 악보", capacity = 3, hp = 1050, defense = 25, range = 15, dashCooldown = 10, combo1 = new[] { "스타카토 화살", "크레센도 울림", "소프라노 피어싱" }, combo2 = new[] { "베이스 코드", "수호의 왈츠", "천상의 오케스트라" }, origins = new[] { "용맹의 지휘자", "성역의 수호성가", "발키리의 진혼곡" }, passives = new[] { "공격 시너지 특화", "보호막 +40% · 보호막 중 경직 면역", "음파 공격/무력화 강화" }, identityCosts = new[] { 3f, 3, 3 }, abilities = new[] { S("g1", "스타카토 화살", 0, "3타 ♩ · 치유 각성 · 주변 공격력 +3%", 110, 15), S("g2", "베이스 코드", 0, "3타 ♪ · 치유 각성 · 주변 아군 보호막 5%", 160, 15, 0, 4), S("a", "피날레의 고음", 8, "즉발 음파 · ♫ 획득", 200, 15), S("c", "포르티시모 폭발", 10, "중거리 음파 카운터", 160, 15), Ally(S("q", "수호천사의 독주", 9, "소량 회복 · HP 25% 보호막 · 6초 피해 -20%", 0, 15, 6)), Self(S("e", "성가대의 합창", 16, "15m 파티 회복 · 8초 HP 15% 보호막", 0, 0, 8, 15)), Self(S("m", "글리산도 스텝", 10, "음계 이동 · 스친 아군 이동 +20% 4초", 0)), S("r1", "불협화음 폭풍", 15, "무력화 · 적 공격력 -10% 8초", 350, 15, 8), S("r2", "윈드 오브 뮤직", 14, "적에게 접근 · 근처 아군 보호막/슈퍼아머", 260, 15, 5, 6), Self(S("r3", "천상의 세레나데", 24, "3초 제자리 연주 · 주변 피해 -50% · 고속 회복", 0, 0, 3, 15)), Self(S("f1", "앙코르", 25, "직전 Q/E를 비용 없이 50% 위력으로 한 번 재현", 0)), Self(S("f2", "템포 업", 18, "파티 이동기 재충전 -5초", 0)), S("f3", "공명 파열", 12, "자신이 부여한 보호막 총량 비례 피해 · 보호막 유지", 0, 20), S("v1", "여름의 축제: 천사의 나팔", 120, "일곱 황금빛 음계 빛기둥", 260, 25), S("v2", "영웅들의 행진곡", 120, "타격 · 파티 보호막 30% · 12초 주는 피해 +12%", 900, 25, 12), Self(S("z1", "용맹의 아리아", 1, "10초 파티 피해 +20% · 치명 피해 +15%", 0, 0, 10, 15)), Self(S("z2", "구원의 세레나데", 1, "파티 HP 40% 회복 · 5초 상태이상 면역/피해 -30%", 0, 0, 5, 15)), S("z3", "죽음의 오페라: 디에스 이레", 1, "빛의 오르간 날개 · 직선 음파 레이저", 1700, 25) } });
            jobs.Add(new RosterJob { index = 5, slug = "blood-apothecary", name = "블러드 아포테카리", theme = "가을 · 혈류와 약초", weapon = "은침 · 약초 비약", resource = "진홍빛 영약", fractional = true, hp = 1100, defense = 25, range = 10, dashCooldown = 9, combo1 = new[] { "은침 투척", "경혈 찌르기", "활력 추출" }, combo2 = new[] { "부식초 플라스크", "맹독 가시탄", "연쇄 영약 폭발" }, origins = new[] { "혈류 공명자", "광기의 연금술사", "기적의 집도의" }, passives = new[] { "혈류 공명이 파티 전체에 적용", "플라스크 범위/무력화 +50%", "HP 50% 이하 대상 치유 +100%" }, identityCosts = new[] { 50f, 50, 50 }, abilities = new[] { S("g1", "은침 투척", 0, "매 타격 혈류 치유 · 3타 각성/Q·E -2초", 150, 10), S("g2", "부식초 플라스크", 0, "3타 각성 · 5초 약초 안개 · 적 받는 피해 +5%", 190, 10, 5, 4), S("a", "신경 독소 기폭", 8, "은침 폭발 · 파티 회복", 260, 10), S("c", "마비 산탄 투척", 10, "마비 약초 카운터", 180, 10), Ally(S("q", "초재생 수혈 주사", 8, "대량 치유 · 6초 피해 -25% / 공격력 +10%", 0, 20, 6)), Self(S("e", "만병통치 연무", 15, "파티 대량 회복 · 해로운 디버프 1회 차단 5초", 0, 0, 5)), Self(S("m", "안개 걸음", 9, "붉은 약초 안개 무적 이동", 0)), S("r1", "생명력 전환로", 18, "2.5초 채널링 · 지속 무력화/파티 치유", 95, 10, 2.5f), S("r2", "극약 처방: 산성비", 16, "무력화 · 10초 방어력 -12%", 400, 10, 10), Ally(S("r3", "불사조의 비약", 40, "5초 사망 방지 1회 · 치명상 시 HP 30%로 유지", 0, 20, 5)), Self(S("f1", "아드레날린 촉진제", 20, "파티 3/F 쿨타임 -3초", 0)), Self(S("f2", "진통 마취제", 16, "파티 받는 피해 -25% 4초", 0, 0, 4)), Self(S("f3", "자가 채혈", 15, "현재 HP 10% 소모 · 영약 50 · 각성 2", 0)), S("v1", "수확의 절정: 현자의 붉은 돌", 120, "방어 무시 타격 · 실제 피해만큼 파티 치유", 1900, 10), S("v2", "생체 맥박 폭주", 120, "타격 · 12초 파티 치명 피해 +25% / 이동 +20%", 1100, 10, 12), Self(S("z1", "판하이마: 전신 혈류 결계", 1, "파티 5겹 보호막 · 남은 겹은 만료 시 치유", 0, 0, 8)), S("z2", "금기 조합: 판도라의 영약", 1, "폭발 무력화 · 파티 공격력 +15% 8초", 850, 10, 8, 5), Self(S("z3", "긴급 소생 시술", 1, "살아 있는 최저 HP 아군 2명 완전 회복 · 2.5초 무적", 0)) } });
            jobs.Add(new RosterJob { index = 6, slug = "nightblade", name = "나이트블레이드", theme = "자정 · 그림자", weapon = "초승달 쌍단검", resource = "그림자 인장", capacity = 5, fractional = true, hp = 1200, defense = 30, range = 3, gcd1 = 1.4f, gcd2 = 2, dashCooldown = 6, dashCharges = 2, combo1 = new[] { "그림자 베기", "초승달 칼춤", "측면 동맥 가르기" }, combo2 = new[] { "사각 찌르기", "흑월 교차참", "후방 심장 적출" }, origins = new[] { "사각의 집행자", "무한 그림자 춤", "인술의 달인" }, passives = new[] { "후방 3타 보너스 +80%", "모든 방향 측후면 성공", "중거리 속성 인술 조합" }, identityCosts = new[] { 5f, 3, 4 }, abilities = new[] { S("g1", "그림자 베기", 0, "3타 측면 치명 +20% · 인장 2 · 오방향 피해 -35%", 170, 3), S("g2", "사각 찌르기", 0, "3타 후방 피해 +40% · 연무 1충전", 220, 3), S("a", "그림자 연무", 8, "3회 충전 · 2연속 참격", 150, 3), S("c", "그림자 반전", 10, "적 정면으로 이동하여 카운터", 200, 12), Self(S("q", "연막 은신", 24, "1.5초 무적 · 이후 5초 피해 -30%", 0, 0, 6.5f)), S("e", "죽음의 표식", 20, "10초 자신의 공속 +15% · 파티 치명 피해 +12%", 0, 20, 10), Self(S("m", "사각 스텝", 6, "2회 충전 · 대상 반대편으로 이동", 0)), S("r1", "흑월 열파참", 14, "후방 적중 시 확정 치명", 600, 3), S("r2", "수리검 폭풍", 15, "3초 원거리 회전 타격", 120, 20, 3), S("r3", "아킬레스건 절단", 16, "높은 무력화 · 출혈 10초", 350, 3, 10), Self(S("f1", "분신 협공", 15, "3초 내 다음 GCD 3타를 후방 분신으로 한 번 복제", 0, 0, 3)), S("f2", "암기 투척: 독침", 10, "인장 +2 · 이동 증가", 220, 15, 4), Self(S("f3", "그림자 회귀", 18, "쐐기 설치 · 5초 내 재입력 시 귀환 타격", 300, 0, 5)), S("v1", "영겁의 찰나: 절명", 120, "그림자 난무 후 폭발", 2200, 12), S("v2", "치명적인 약점 노출", 120, "12초 파티 모든 공격 후방 성공 · 적 방어 -15%", 1000, 15, 12), S("z1", "급소 관통: 종언의 일격", 12, "후방 필살기 · 후방 성공 시 재사용 50% 반환", 1300, 3), Self(S("z2", "암야의 광무", 1, "6초 GCD 1초 · 공격마다 그림자 추가타", 0, 0, 6)), Self(S("z3", "인법: 천지인 개방", 1, "다음 1/2/3 입력으로 화둔/뇌둔/수둔 인술 선택", 0, 0, 8)) } });
            jobs.Add(new RosterJob { index = 7, slug = "tempest-lancer", name = "템페스트 랜서", theme = "폭풍 · 푸른 뇌전", weapon = "뇌룡 장창", resource = "뇌룡의 전류", hp = 1350, defense = 40, range = 5, dashCooldown = 8, combo1 = new[] { "질풍 찌르기", "나선 관통", "뇌전 일섬" }, combo2 = new[] { "창자루 후리기", "낙뢰 강타", "폭풍 파쇄격" }, origins = new[] { "창공의 지배자", "뇌제의 창술", "태풍의 핵" }, passives = new[] { "점프 피해 +50% · 점프 재사용 -30%", "지상 창술 가속/사거리 증가", "무력화 +40% · 주변 아군 공속/이속 지원" }, identityCosts = new[] { 100f, 50, 80 }, abilities = new[] { S("g1", "질풍 찌르기", 0, "후방 3타 방어 30% 관통 · 다음 GCD 2 +35%", 190, 5), S("g2", "창자루 후리기", 0, "측면 3타 추가 번개 · 다음 GCD 1 +35%", 230, 5, 0, 4), S("a", "스카이 다이브", 9, "0.8초 체공 후 타격 · 출발 위치 귀환 · 체공 중 대쉬 불가", 360, 15), S("c", "뇌룡 파쇄창", 10, "긴 창 찌르기 카운터", 220, 6), Self(S("q", "폭풍의 눈", 24, "8초 피해 -35% · 피격 이상 면역", 0, 0, 8)), Self(S("e", "용의 눈", 22, "가장 가까운 딜러와 10초 연결 · 둘 다 피해 +12%/치명 +10%p", 0, 0, 10)), Self(S("m", "라이트닝 대시", 8, "직선 번개 돌진", 0)), S("r1", "뇌신창: 적룡포", 16, "장거리 직선 확정 치명", 650, 30), S("r2", "드래곤 다이브", 18, "적에게 직접 도약 · 최상 무력화", 650, 20), S("r3", "회전 폭풍창", 14, "전방 10연속 타격 · 투사체 방어", 70, 6, 2, 6), S("f1", "미라지 다이브", 1, "점프 직후만 활성화 · 뇌룡 환영", 320, 20), S("f2", "초전도 스파크", 12, "무력화 · 전류 +30", 250, 8), Self(S("f3", "엘루시브 점프", 14, "뒤로 15m 공중제비", 180)), S("v1", "뇌신의 낙하: 기가 볼트 스트라이크", 120, "무적 체공 후 뇌룡 강하", 2200, 25), S("v2", "번개의 피뢰침", 120, "12초 파티 타격마다 연쇄 번개", 1000, 25, 12), S("z1", "스타다이버", 1, "도약 · 반경 15m 확정 치명 뇌전", 1500, 25, 0, 15), S("z2", "뇌룡출수", 1, "지상 전진 · 8연 찌르기와 횡베기", 130, 8), S("z3", "초거대 적란운 소환", 1, "8초 매초 번개 · 파티 공속 +15%", 160, 25, 8, 5) } });
            jobs.Add(new RosterJob { index = 8, slug = "gunslinger", name = "건슬링어", theme = "유성 · 은탄", weapon = "쌍권총 · 퇴마 장총", resource = "마탄 실린더", capacity = 6, hp = 1200, defense = 30, range = 15, dashCooldown = 7, combo1 = new[] { "패스트 드로우", "더블 탭", "실버 불릿 샤워" }, combo2 = new[] { "매그넘 브레이크", "퇴마 산탄", "데드아이 스나이프" }, origins = new[] { "질주하는 쌍권총: 피스키퍼", "전장의 돌격소총: 런앤건", "일격필살 저격소총: 데드아이" }, passives = new[] { "장총 대신 이동 가능한 쌍권총", "연사 · 유탄 무력화 강화", "긴 조준 · 고위력 관통 사격" }, identityCosts = new[] { 4f, 3, 6 }, abilities = new[] { S("g1", "패스트 드로우", 0, "이동 사격 · 3타 탄환 +2/이속 증가 · 낮은 무력화", 170, 15), S("g2", "매그넘 브레이크", 0, "제자리 조준 · 3타 탄환 2 소모/확정 치명", 270, 20), S("a", "트릭 샷", 8, "은화 도탄 · 탄환 +1", 230, 20), S("c", "피스키퍼 산탄", 10, "슬라이딩 산탄 카운터", 220, 12), Self(S("q", "사냥꾼의 그림자 외투", 24, "6초 피해 -35% · 이동 +30%", 0, 0, 6)), Self(S("e", "약점 포착 조명탄", 20, "10초 파티 치명 +10%p · 자신의 공격력 증가", 0, 0, 10)), Self(S("m", "스타일리시 구르기", 7, "이동하며 탄환 +2 장전", 0)), S("r1", "타겟 다운", 18, "제자리 고화력 3연속 저격", 400, 25), S("r2", "대재앙 유탄", 15, "연쇄 폭발 · 높은 무력화", 600, 20, 0, 5), S("r3", "이퀄리브리엄", 14, "2초 이동하며 360도 권총 난사", 90, 15, 2, 8), Self(S("f1", "퀵 리로드", 22, "실린더 가득 · 다음 공격 치명 +30%p", 0)), S("f2", "플라즈마 수류탄", 14, "즉발 무력화 · 감전 5초", 280, 20, 5), S("f3", "후방 사격 회피", 12, "사격하며 뒤로 10m 이동", 260, 20), S("v1", "유성우의 탄환: 데스페라도", 120, "12 마력 총신 난사 · 대구경 마탄", 2000, 25), S("v2", "처형 선고: 은빛 세례", 120, "12초 적 받는 피해 +12% · 파티 이동 +15%", 1100, 25, 12), S("z1", "불릿 타임: 난사", 1, "5초 무빙 난사 초당 10발 · GCD 0.8초", 45, 20, 5), S("z2", "네이팜 탄막 폭격", 1, "고무력화 유탄 · 화염 지대 8초", 160, 20, 8, 5), S("z3", "절대영도 저격: 캘러미티", 1, "1.5초 조준 · 방어 무시 초대형 마탄", 1800, 30) } });
            jobs.Add(new RosterJob { index = 9, slug = "spirit-ranger", name = "스피릿 레인저", theme = "사계의 순환 · 바람", weapon = "고대 목재 장궁 · 소환수 없음", resource = "사계의 풍압", hp = 1150, defense = 25, range = 18, dashCooldown = 8, combo1 = new[] { "바람 화살", "이중 시위", "매의 눈 관통사격" }, combo2 = new[] { "갈래 화살", "폭풍 시위 당기기", "천공의 화살비" }, origins = new[] { "천궁의 명사수", "질풍의 연사수", "사계의 마궁사" }, passives = new[] { "거리에 따라 피해 최대 +45%", "이동 속도 +15% · 무빙 연사", "속성 순환 · 광역 피해/무력화" }, identityCosts = new[] { 100f, 50, 80 }, abilities = new[] { S("g1", "바람 화살", 0, "3타 풍압 +15 · 8초 바람 표식 치명 +15%p", 180, 18), S("g2", "갈래 화살", 0, "3타 풍압 +25 · 높은 무력화 화살비", 240, 18, 0, 4), S("a", "유도 정령시", 8, "추적 화살 5발", 70, 18), S("c", "돌풍 파쇄시", 10, "원거리 즉발 전방 카운터", 210, 18), Self(S("q", "바람의 장막", 22, "6초 피해 -35% · 둔화/속박 정화", 0, 0, 6)), S("e", "순풍의 이정표", 20, "10초 파티 이속/공속 +10% · 적 받는 피해 +8%", 0, 18, 10), Self(S("m", "윈드 워커 도약", 8, "원하는 방향으로 바람 도약", 0)), S("r1", "아르테미스의 달빛 화살", 16, "1초 차징 · 장거리 관통 · 거리에 따라 강화", 650, 1000), S("r2", "가시 덩굴 쐐기시", 15, "5초 지속 무력화", 140, 18, 5, 4), S("r3", "소나기 화살", 14, "4초 화살비 · 매 틱 풍압 획득", 130, 18, 4, 5), S("f1", "급소 꿰뚫기", 10, "내 바람 표식이 있는 대상 · 확정 치명", 500, 18), S("f2", "매의 발톱", 12, "공중 3발 사격 · 뒤로 5m", 150, 18), Self(S("f3", "숲의 위장막", 18, "4초 은신/어그로 초기화 · 다음 화살 +30%", 0, 0, 4)), S("v1", "사계절의 종언: 아스트랄 애로우", 120, "사계절 관통 화살과 폭풍", 2200, 30), S("v2", "사계절의 순풍 영토", 120, "12초 파티 일반 쿨타임 회복 +20% · 공격력 +10%", 1000, 25, 12), S("z1", "별을 쏘는 활: 드래곤 피어싱", 1, "나선 관통 화살 다단 폭발", 1700, 35), S("z2", "질풍노도", 1, "5초 무빙 연사 · 초당 8발", 70, 18, 5), S("z3", "만리장풍: 사계 폭격", 1, "4속성 광역 화살비 · 최상 무력화", 220, 25, 4, 7) } });
            jobs.Add(new RosterJob { index = 10, slug = "astromancer", name = "아스트로맨서", theme = "천체 · 별들의 공전", weapon = "순수 수정 천체 구슬", resource = "성좌 마력", hp = 1050, defense = 20, speed = .9f, range = 20, dashCooldown = 14, combo1 = new[] { "스타더스트", "혜성 꼬리", "펄사 방출" }, combo2 = new[] { "중력장 압축", "암흑 성운", "메테오 스트라이크" }, origins = new[] { "종말의 메테오", "유성우 지휘자", "중력 제어자" }, passives = new[] { "시전 완료 마법 피해 +60%", "모든 마법 즉시 시전", "마법 지속 중력장 · 무력화 +50%" }, identityCosts = new[] { 100f, 50, 80 }, abilities = new[] { S("g1", "스타더스트", 0, "즉발 이동 가능 · 3타 마력/다음 GCD 2 시전 반감", 160, 20), S("g2", "중력장 압축", 0, "1.5/1.5/2초 시전 · 이동 시 취소 · 강력한 메테오", 350, 20, 0, 5), S("a", "유성우 소환", 9, "별똥별 4회 낙하", 110, 20), S("c", "중력 역전장", 10, "전방 중력 카운터", 220, 15), Self(S("q", "별빛 장막", 25, "8초 피해 -35% · 모든 시전 즉발/무빙", 0, 0, 8)), Self(S("e", "운명의 별자리", 20, "10초 자신 마법 공격력 +15% · 파티 속성 피해 +10%", 0, 0, 10)), Self(S("m", "성간 워프", 14, "시전을 유지하는 순간 이동", 0)), S("r1", "블랙홀 생성", 18, "4초 지속 피해/무력화", 180, 25, 4, 5), Self(S("r2", "폴라리스", 22, "발밑 고정 마법진 10초 · 안에서 시전/GCD 25% 가속", 0, 0, 10, 4)), S("r3", "감마선 버스트", 16, "직선 초신성 광선", 750, 30), Self(S("f1", "마나 증폭", 20, "다음 시전 마법 즉발 · 확정 치명", 0)), Self(S("f2", "위성 궤도 방어", 15, "보호막 · 위성 3개 근접 자동 무력화", 180, 0, 8, 5)), Self(S("f3", "공간 도약 귀환", 12, "폴라리스 또는 가장 가까운 힐러에게 이동", 0)), S("v1", "우주의 주기: 코스믹 노바", 120, "행성 충돌 · 초고위력 광역 마법", 2600, 30, 0, 8), S("v2", "황도 12궁의 정렬", 120, "12초 파티 방어 관통 +20% · 자원/마나 회복 2배", 1300, 30, 12), S("z1", "둠즈데이", 1, "2초 시전 · 초거대 소행성", 2100, 30, 0, 8), S("z2", "밀키웨이 오버드라이브", 1, "4초 이동하며 유성우 폭격", 190, 25, 4, 5), S("z3", "사건의 지평선: 그라비티 제로", 1, "중력 압축 · 대량 무력화 · 6초 받는 마법 피해 +15%", 1400, 25, 6) } });
            jobs.Add(new RosterJob { index = 11, slug = "mirror-phantom", name = "미러 팬텀", theme = "회귀 · 과거의 거울상", weapon = "수정 거울 · 환영 대낫", resource = "거울상 파편", capacity = 3, hp = 1150, defense = 20, range = 15, dashCooldown = 9, combo1 = new[] { "유리 파편", "프리즘 광선", "크리스탈 스피어" }, combo2 = new[] { "환영 낫 베기", "그림자 단두대", "차원 절단: 유리꽃" }, origins = new[] { "환영의 군단장", "사신의 대낫", "프리즘 아키텍트" }, passives = new[] { "분신 최대 3체 유지 · 협공", "왕복 페널티 제거 · 근접 낫 가속/범위", "근접 대신 원거리 거울 레이저" }, identityCosts = new[] { 3f, 3, 2 }, abilities = new[] { S("g1", "유리 파편", 0, "15m 수정 마법 · 3타 파편/다음 근접 +30%", 180, 15), S("g2", "환영 낫 베기", 0, "근접 광역 · 3타 파편/다음 원거리 +30%", 260, 3, 0, 4), S("a", "거울상 복제", 10, "직전 GCD 또는 R 공격을 분신이 한 번 복제", 0, 20), S("c", "반사의 거울벽", 10, "전방 마법 거울 카운터", 220, 8), Self(S("q", "유리 분신 교대", 22, "6초 받는 피해 -40%", 0, 0, 6)), Self(S("e", "프리즘 굴절 결계", 20, "10초 파티 치명 +10%p · 타격에 5% 추가 피해", 0, 0, 10)), Self(S("m", "거울 속으로", 9, "거울 속 무적 이동", 0)), S("r1", "레퀴엠 오브 글래스", 16, "대상에게 접근 · 거대 환영 낫", 700, 15), S("r2", "프리즘 레이저 포격", 15, "6개 수정 거울 광선", 130, 20), S("r3", "거울 감옥 봉인", 18, "수정 감옥 파쇄 · 최상 무력화", 650, 20), Self(S("f1", "데자뷔", 14, "자신의 거울상 위치와 교환 · 파편 폭발", 320)), S("f2", "샤터드 블레이드", 12, "즉발 파편 · 거울상 파편 +1", 300, 15), Self(S("f3", "환영 방패 투영", 16, "자신과 가까운 아군 2명에게 5초 보호막", 0, 0, 5)), S("v1", "만화경 거울 지옥", 120, "분신 대낫 난무 · 거울 파쇄", 2200, 20), S("v2", "영웅들의 그림자 투영", 120, "10초 파티 공격에 12% 환영 추가타", 1100, 20, 10), S("z1", "그랜드 피날레: 일제 절단", 1, "본체와 남은 모든 분신의 합동 참격", 650, 20), Self(S("z2", "사신 빙의", 1, "6초 GCD 1.2초 · 대낫 연격 강화", 0, 0, 6)), S("z3", "만화경 프리즘 캐논", 1, "8각 거울 진 굴절 포격", 1800, 25) } });
            foreach (var j in jobs)
            {
                j.disadvantage = j.index switch {
                    1 => "혈기 과열: 탱커 중 낮은 기본 방어력 · 유효 공격이 없으면 피흡 불가",
                    2 => "포격 반동: 제자리 후딜레이 · 탄창을 모두 쓰면 과열 중 장전 불가",
                    3 => "연약한 체질: 낮은 HP·방어 · 치유 각성이 없으면 Q/E 시전과 마나 필요",
                    4 => "낮은 순수 회복량 · 음파 거리 밖 아군에게 치유와 버프 전달 불가",
                    5 => "중근거리 공격 · 공격할 대상이 없으면 혈류 치유와 자원 수급 단절",
                    6 => "측·후면 의존: 잘못된 방향 타격은 피해와 인장 수급 감소",
                    7 => "체공 경직: 점프 중 Space로 취소할 수 없음",
                    8 => "쌍권총은 낮은 무력화 · 장총 조준 중 일반 이동 제한",
                    9 => "영거리 사격 피해 감소 · 받는 회복과 보호막 효율 감소 · 소환수 없음",
                    10 => "낮은 이동 속도 · 일반 이동 시 시전 취소 · 전용 워프는 시전 유지",
                    11 => "굴절 피로: 같은 거리 콤보 반복 시 피해 감소 · 원근 교차 필요",
                    _ => ""
                };
                var movement = j.Ability("m");
                movement.cooldown = j.dashCooldown;
                if (j.index == 2)
                {
                    j.Ability("g1").stagger = 35;
                    j.Ability("g2").stagger = 80;
                }

                if (j.index == 8)
                    j.Ability("g1").stagger = 1;
                if (j.index == 1)
                {
                    j.Ability("g2").shape = j.Ability("a").shape = j.Ability("z3").shape = "self";
                }

                if (j.index == 2)
                {
                    j.Ability("r1").shape = "cone";
                    j.Ability("r3").shape = "wall";
                }

                if (j.index == 3)
                    j.Ability("r3").shape = "line";
                if (j.index == 4)
                    j.Ability("z3").shape = "line";
                if (j.index == 7)
                {
                    j.Ability("r1").shape = "line";
                    j.Ability("r3").shape = "cone";
                }

                if (j.index == 8)
                    j.Ability("r3").shape = "self";
                if (j.index == 9)
                {
                    j.Ability("r1").shape = "line";
                    j.Ability("z1").shape = "line";
                    j.Ability("z1").hits = 4;
                }
                if (j.index == 11)
                {
                    j.Ability("z3").hits = 8;
                    j.Ability("z2").description += " · 종료 시 마지막 유효 대상에 GCD 2의 3타 위력 피니시";
                }
                if (j.index == 10)
                    j.Ability("r3").shape = "line";
                j.Ability("g1").stagger = j.index == 8 ? 0 : 15;
                j.Ability("g2").stagger = j.index == 2 ? 80 : j.index == 10 ? 75 : 55;
                j.Ability("c").stagger = 45;
                foreach (var a in j.abilities)
                    if (a.description.Contains("최상"))
                        a.stagger = 100;
                    else if (a.description.Contains("높은 무력화") || a.description.Contains("고무력화"))
                        a.stagger = 70;
                if (j.index == 1)
                    j.Ability("z2").cast = 1.5f;
                if (j.index == 8)
                    j.Ability("z3").cast = 1.5f;
                if (j.index == 9)
                    j.Ability("r1").cast = 1;
                if (j.index == 10)
                    j.Ability("z1").cast = 2;
            }

            return jobs.ToArray();
        }
    }
}
