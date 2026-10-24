using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RaidRogue
{
    // A composable effect adapter for the common actor transaction, not a second input/cooldown engine.
    [DefaultExecutionOrder(450)]
    public sealed partial class CharacterKit : MonoBehaviour
    {
        private CharacterActor _actor;
        private PlayerController _player;
        private readonly PlayerMovementCollision _collision = new PlayerMovementCollision();
        public readonly CharacterEffectState State = new CharacterEffectState();
        private CharacterKitSnapshot Data => State.Data;
        public RosterJob Job => CharacterRosterData.Find(_actor?.Definition?.id);
        public bool Active => Job != null;
        public bool Overcome => CharacterEvolutionCards.Has(_actor, false);
        public bool Maximize => CharacterEvolutionCards.Has(_actor, true);
        public int Route => Active ? Array.FindIndex(_actor.Definition.routes, r => r.id == _actor.RouteId) + 1 : 0;
        public float Now => RaidCombatAuthority.ServerTime;
        public Color Tint => Color.HSVToRGB((Job?.index ?? 0) / 12f, .55f, .95f);

        public bool Has(string id) => State.Find(id, Now) != null;
        public CharacterTimedEffect Effect(string id) => State.Find(id, Now);
        public float Resource => _actor.Resource(Job.Resource);
        public bool Channeling => Has("channel");
        public bool CanWeaveDuringChannel => Job?.index == 8 && Route == 1 && Has("identity") || Job?.index == 2 && Data.effects.Any(e => e.kind == "channel" && e.skill == "z2" && e.end > Now);
        public bool MovementBlocked => Active && (Has("root") || Has("air") || Channeling && Effect("channel").aux == 0);
        public bool DashBlocked => Active && (Has("air") || Has("root") || Job.index == 10 && Maximize && _actor.Snapshot.castSkillId.Length > 0);

        public static CharacterKit Ensure(GameObject go) => go.GetComponent<CharacterKit>() ?? go.AddComponent<CharacterKit>();
        private void Awake()
        {
            _actor = GetComponent<CharacterActor>();
            _player = GetComponent<PlayerController>();
            _actor.RegisterHandler(CharacterRosterData.Handler, Execute);
        }

        public string Capture() => JsonUtility.ToJson(Data);
        public bool Restore(string json) => !string.IsNullOrEmpty(json) && State.Restore(JsonUtility.FromJson<CharacterKitSnapshot>(json));
        public void Clear()
        {
            State.Clear();
            if (!RaidCombatAuthority.Replica)
            {
                int source = CombatMetricsManager.SourceId(transform);
                foreach (var ally in PartyLife.OrderedMembers())
                {
                    ally.GetComponent<PlayerController>()?.RemoveShieldsFrom(source);
                    ally.GetComponent<PartyBotAI>()?.RemoveShieldsFrom(source);
                    ally.GetComponent<CombatStatusEffects>()?.RemoveProtectionFrom(source);
                }
            }

            Recalculate();
            HideGear();
        }

        public void CancelCasting()
        {
            State.Remove("air");
            State.Remove("aimLock");
            if (Job?.index == 8)
                State.Remove("root");
        }

        public void CancelChannel()
        {
            if (Data.effects.Any(e => e.kind == "channel" && e.skill.StartsWith("z")))
            {
                State.Remove("identity");
                if (Job?.index == 1)
                    State.Remove("reduction");
            }

            State.Remove("channel");
            foreach (var e in Data.effects.Where(e => e.kind == "channel").ToArray())
                State.Remove(e.id);
        }

        public string StatusText() => string.Join(" · ", Data.effects.Where(e => e.target == Id(transform) && e.end > Now && e.kind == "status").Select(e => e.skill + " " + (e.end - Now).ToString("0.0") + "초"));
        public static int Id(Transform t) => t == null ? 0 : RaidCombatAuthority.Active ? RaidCombatAuthority.Id(t) : t.GetInstanceID();
        public static Transform Resolve(int id)
        {
            if (id == 0)
                return null;
            if (RaidCombatAuthority.Active)
                return RaidCombatAuthority.Resolve(id);
            return Units().Find(t => t.GetInstanceID() == id);
        }

        public static List<Transform> Enemies()
        {
            var list = new List<Transform>();
            var boss = StageManager.Instance?.boss;
            if (boss != null && CombatRange.LivingEnemy(boss.transform))
                list.Add(boss.transform);
            foreach (var add in FindObjectsByType<BossAddMinion>(FindObjectsSortMode.None))
                if (CombatRange.LivingEnemy(add.transform))
                    list.Add(add.transform);
            return list;
        }

        private static List<Transform> Units()
        {
            var list = PartyLife.OrderedMembers();
            list.AddRange(Enemies());
            return list;
        }

        public static float Hp(Transform t) => t?.GetComponent<PlayerController>()?.currentHp ?? t?.GetComponent<PartyBotAI>()?.currentHp ?? 0;
        public static float MaxHp(Transform t) => t?.GetComponent<PlayerController>()?.maxHp ?? t?.GetComponent<PartyBotAI>()?.maxHp ?? 1;
        private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
        private IEnumerable<Transform> Party(float radius = 1000) => PartyLife.OrderedMembers().Where(t => CombatRange.LivingPartyMember(t) && Flat(t.position, transform.position) <= radius);
        private float PartyRadius => Job?.index == 4 ? (Maximize ? 10 : Overcome ? 35 : 15) : 1000;

        public void Status(string id, string name, Transform target, float duration, float value = 1, float aux = 0)
        {
            if (target == null || RaidCombatAuthority.Replica)
                return;
            if (Job?.index == 4 && Maximize && id == "attack")
                value *= 1.5f;
            State.Put(new CharacterTimedEffect { id = id + "@" + Id(target) + "#" + name, kind = "buff", skill = name, target = Id(target), end = Now + duration, next = float.MaxValue, value = value, aux = aux });
        }

        private void Self(string id, string name, float duration, float value = 1, float aux = 0)
        {
            State.Put(new CharacterTimedEffect { id = id, kind = "status", skill = name, target = Id(transform), end = Now + duration, next = float.MaxValue, value = value, aux = aux });
        }

        private void BuffParty(string id, string name, float duration, float value = 1, float radius = -1)
        {
            foreach (var ally in Party(radius < 0 ? PartyRadius : radius))
                Status(id, name, ally, duration, value);
        }

        public static IEnumerable<(CharacterKit owner, CharacterTimedEffect effect)> Buffs(Transform unit, string key)
        {
            foreach (var kit in FindObjectsByType<CharacterKit>(FindObjectsSortMode.None))
                if (kit.Active && CombatRange.LivingPartyMember(kit.transform))
                    foreach (var e in kit.Data.effects)
                        if (e.end > kit.Now && e.target == Id(unit) && (e.id == key || e.id == key + "@" + Id(unit) || e.id.StartsWith(key + "@" + Id(unit) + "#", StringComparison.Ordinal)))
                            yield return (kit, e);
        }

        public static float Bonus(Transform t, string key) => Buffs(t, key).Select(b => b.effect.value).DefaultIfEmpty(0).Max();
        public static bool Protected(Transform t) => Bonus(t, "invulnerable") > 0;
        public void OnResourceChanged(string id, float delta)
        {
            if (Active && Job.index == 2 && id == Job.Resource && delta < 0 && Resource <= 0)
                Self("overheat", "총열 과열", CharacterRosterRules.OverheatSeconds(Overcome, Maximize));
        }
        public void Gain(float amount)
        {
            if (!Active || RaidCombatAuthority.Replica)
                return;
            _actor.GainResource(Job.Resource, amount * (1 + Bonus(transform, "regen")));
        }

        public float EffectiveRange(CharacterSkillDefinition s)
        {
            if (s == null || !Active)
                return s?.range ?? 0;
            string k = CharacterRosterData.Key(s.id);
            if (Job.index == 4 && s.target == SkillTargetRule.LivingAlly) return PartyRadius;
            if (Job.index == 6 && Has("ninjutsu") && (k == "g1" || k == "g2" || k == "a"))
                return 15;
            if (Job.index == 5 && s.target == SkillTargetRule.LivingEnemy)
                return Maximize ? 6 : Overcome ? 15 : s.range;
            if (Job.index == 8 && Maximize && k == "g2")
                return s.range * 1.5f;
            if (Job.index == 11 && k == "g2" && (Route == 3 || Overcome || Has("meleeLeap")))
                return 15;
            return s.range + (Job.index == 7 && Route == 2 && s.comboChain > 0 ? 2 : 0);
        }

        public float GcdScale(CharacterSkillDefinition s)
        {
            if (!Active || !s.usesGcd)
                return 1;
            if (Job.index == 6 && Has("identity"))
                return 1 / s.gcd;
            if (Job.index == 8 && Route == 1 && Has("identity"))
                return .8f / s.gcd;
            if (Job.index == 11 && Route == 2 && Has("identity"))
                return 1.2f / s.gcd;
            float haste = Bonus(transform, "haste");
            if (Job.index == 2 && Maximize && Has("overheat") && s.comboChain == 1)
                haste += .8f;
            if (Job.index == 5 && Maximize)
                haste += .5f;
            if (Job.index == 1)
            {
                haste += Resource / 100 * .2f;
                if (Route == 2)
                    haste -= .15f;
                if (Route == 3)
                    haste += .2f;
            }

            if (Job.index == 7 && Route == 2)
                haste += .2f;
            if (Job.index == 11 && Route == 2 && s.comboChain == 2)
                haste += .2f;
            if (Has("polaris") && Flat(transform.position, new Vector3(Effect("polaris").x, 0, Effect("polaris").z)) <= 4)
                haste += .25f;
            float card = _player?.combatController != null ? _player.combatController.baseGcdDuration - _player.combatController.currentGcdDuration : 0;
            return Mathf.Max(.6f, s.gcd - Mathf.Max(0, card)) / s.gcd / Mathf.Max(.25f, 1 + haste);
        }

        public float CastTime(CharacterSkillDefinition s)
        {
            if (!Active)
                return s.castTime;
            string k = CharacterRosterData.Key(s.id);
            float cast = s.castTime;
            if (Job.Role == 2 && (k == "q" || k == "e") && _actor.Resource(Job.Id + ".awakening") >= 1)
                return 0;
            if (Job.index == 1 && Route == 2 && s.comboChain > 0 && Combo(s) == 3)
                cast = 1.5f;
            if (Job.index == 8 && k == "g2" && Route != 1)
                cast = Combo(s) == 3 ? 1.5f : 1;
            if (Job.index == 10)
            {
                if (Route == 2 || Has("instant") || Has("nextInstant"))
                    return 0;
                if (k == "g2")
                    cast = Combo(s) == 3 ? 2 : 1.5f;
                if (k == "g2" && Has("starHaste"))
                    cast *= .5f;
            }

            if (Job.index == 7 && (k == "a" || k == "r2" || k == "z1" || k == "v1"))
                cast = .8f;
            if (Job.index == 8 && Route == 3 && k == "g2")
                cast += .5f;
            if (Job.index == 7)
            {
                if (Overcome)
                    cast *= .5f;
                if (Maximize && cast > 0)
                    cast += .3f;
            }

            if (Job.index == 10 && Maximize && cast > 0)
                cast += .5f;
            return cast * GcdScale(new CharacterSkillDefinition { usesGcd = true, gcd = 2 });
        }

        public float CostScale(CharacterSkillDefinition s) => Active && Job.Role == 2 && (CharacterRosterData.Key(s.id) == "q" || CharacterRosterData.Key(s.id) == "e") && _actor.Resource(Job.Id + ".awakening") >= 1 ? 0 : 1;
        public void BeganCast(CharacterSkillDefinition skill)
        {
            if (!Active)
                return;
            string key = CharacterRosterData.Key(skill.id);
            float seconds = CastTime(skill);
            if (Job.index == 7 && (key == "a" || key == "r2" || key == "z1" || key == "v1"))
            {
                Self("air", skill.name, seconds);
                if (key == "v1")
                    Self("invulnerable", skill.name, seconds + .05f);
            }

            if (Job.index == 8 && (key == "g2" || key == "z3") && Route != 1 && !Overcome)
            {
                Self("root", skill.name, seconds);
                if (Maximize)
                {
                    Self("aimLock", skill.name, seconds);
                    Effect("aimLock").yaw = transform.eulerAngles.y;
                }
            }

            if (Job.index == 1 && key == "z2")
                GrantSuperArmor(transform,seconds);
        }

        public bool AllowsMovingCast(CharacterSkillDefinition s) => Active && (Job.index == 7 || Job.index == 10 && (CastTime(s) == 0 || Overcome) || Job.index == 8);
        private int Combo(CharacterSkillDefinition s) => s.comboChain == 1 ? _actor.Snapshot.combo1 : s.comboChain == 2 ? _actor.Snapshot.combo2 : 0;
        public bool CanAirAttack(CharacterSkillDefinition s, Transform target) => Active && Job.index == 5 && Overcome && (CharacterRosterData.Key(s.id) == "g1" || CharacterRosterData.Key(s.id) == "g2" || CharacterRosterData.Key(s.id) == "a") && !CombatRange.LivingEnemy(target) && Enemies().Count == 0;
        public string Unavailable(CharacterSkillDefinition s, Transform target)
        {
            if (!Active)
                return "";
            string k = CharacterRosterData.Key(s.id);
            if ((Has("air") || Has("root")) && !_actor.CompletingCast)
                return "동작/반동 중";
            if (Channeling && k != "m" && !(CanWeaveDuringChannel && (k == "a" || Job.index == 8 && (k == "g1" || k == "g2"))))
                return "채널링 중";
            if (k.StartsWith("z") && Has("identity"))
                return "아이덴티티 진행 중";
            if (Job.index == 1 && k == "z3" && Resource < 5)
                return "열기 부족";
            if (Job.index == 2 && k == "g2" && (Combo(s) == 1 || Combo(s) == 3) && Resource < 1)
                return "용염 탄창 부족";
            if (Job.index == 2 && k == "z1" && Resource < 1)
                return "용염 탄창 부족";
            if (Job.index == 8 && k == "g2" && Combo(s) == 3 && Resource < 2)
                return "마탄 부족";
            if (Job.index == 9 && k == "f1" && !Buffs(target, "windMark").Any(b => b.owner == this))
                return "자신의 바람 표식 필요";
            if (Job.index == 7 && k == "f1" && !Has("mirage"))
                return "점프 직후만 사용 가능";
            if (Job.index == 11 && k == "a" && Data.lastSkill.Length == 0)
                return "복제할 공격 없음";
            if (Job.index == 11 && k == "f1" && !Has("clonePosition"))
                return "교환할 거울상 없음";
            if (Job.index == 4 && k == "f1" && Data.lastHeal.Length == 0)
                return "재현할 치유 없음";
            return "";
        }

        private void Update()
        {
            if (!Active)
            {
                HideGear();
                return;
            }

            DrawGear();
            DrawEffects();
            if (RaidCombatAuthority.Replica || StageManager.CombatSuspended)
                return;
            if (!CombatRange.LivingPartyMember(transform))
                return;
            if (_player.IsStunned || _player.IsBeingKnockedBack || _player.IsBeingRescued)
                CancelChannel();
            Data.regen += Time.deltaTime;
            while (Data.regen >= 1)
            {
                Data.regen -= 1;
                if (Job.Role == 2)
                    _actor.GainResource(Job.Id + ".mana", 3 * (1 + Bonus(transform, "regen")));
                if (Job.index == 3)
                    Gain(.05f);
            }

            var whirlwind = Data.effects.FirstOrDefault(e => e.kind == "channel" && e.skill == "z3" && Job.index == 1);
            if (whirlwind != null && Resource >= 5)
            {
                float end = Mathf.Max(whirlwind.end, Now + Resource / 20);
                whirlwind.end = end;
                foreach (var id in new[]
                {
                    "channel",
                    "identity",
                    "reduction"
                }

                )
                {
                    var state = Data.effects.FirstOrDefault(e => e.id == id);
                    if (state != null)
                        state.end = end;
                }
            }

            foreach (var e in Data.effects.ToArray())
            {
                if (e.kind == "ground" || e.kind == "hot" || e.kind == "dot" || e.kind == "channel")
                {
                    int count = CharacterEffectState.TakeTicks(e, Now);
                    for (int i = 0; i < count; i++)
                        TickEffect(e);
                }

                if (e.end <= Now && e.id != "refundZ")
                {
                    Expire(e);
                    State.Remove(e.id);
                }
            }

            UpdatePortal();
            UpdateSatellites();
            UpdateDashTrail();
            if (Job.index == 7 && Route == 3)
            {
                BuffParty("haste", "태풍의 핵", .2f, .05f, 8);
                BuffParty("speed", "태풍의 핵", .2f, .05f, 8);
            }

            var refund = Data.effects.FirstOrDefault(e => e.id == "refundZ");
            if (refund != null)
            {
                _actor.ReduceCooldowns(new[] { Job.SkillId("z1") }, refund.value);
                State.Remove("refundZ");
            }

            Recalculate();
        }

        private void UpdatePortal()
        {
            var portal = Effect("portal");
            if (portal == null)
                return;
            var center = new Vector3(portal.x, portal.y, portal.z);
            foreach (var ally in Party())
            {
                if (ally == transform || Flat(ally.position, center) > 2 || Has("portalUsed." + Id(ally)))
                    continue;
                var pc = ally.GetComponent<PlayerController>();
                Vector3 destination = transform.position + transform.right * (CombatRange.Radius(ally) + CombatRange.Radius(transform) + .2f);
                var bot = ally.GetComponent<PartyBotAI>();
                float arena = pc != null ? pc.arenaBoundaryRadius : bot?.humanPlayer?.arenaBoundaryRadius ?? 18;
                var delta = destination - ally.position;
                var end = _collision.Move(ally, ally.position, CombatRange.Radius(ally), arena, delta, delta.magnitude, out bool blocked);
                if (blocked)
                    continue;
                var old = ally.position;
                ally.position = end;
                if (RaidCombatAuthority.Active)
                    RaidNetworkSession.Instance.CommitSkillRelocation(ally);
                if (pc != null)
                    pc.targetBoss?.TryInterceptPlayerMovement(pc, old, end);
                Heal(ally, MaxHp(ally) * .15f);
                var actor = ally.GetComponent<CharacterActor>();
                if (actor != null)
                    actor.ReduceCooldowns(actor.Definition.skills.Where(s => s.id != actor.Selected(CharacterSlot.Identity)?.id && s.id != actor.Selected(CharacterSlot.Ultimate)?.id).Select(s => s.id), 3);
                Self("portalUsed." + Id(ally), "관문 통과", portal.end - Now);
            }
        }

        public void Recalculate()
        {
            if (!Active || _player == null || RaidCombatAuthority.Replica)
                return;
            float max = Job.hp * (Job.index == 3 && Maximize ? .85f : 1) * (1 + Bonus(transform, "maxHp"));
            float ratio = _player.maxHp > 0 ? _player.currentHp / _player.maxHp : 1;
            if (Mathf.Abs(max - _player.maxHp) > .01f)
            {
                _player.maxHp = max;
                _player.currentHp = max * ratio;
            }

            _player.moveSpeed = _actor.Definition.moveSpeed * Job.speed * Mathf.Pow(1.3f, _player.ghostStepStacks) * (1 + Bonus(transform, "speed") + (Job.index == 9 && Route == 2 ? .15f : 0));
        }

        public float Defense => !Active ? 0 : Job.defense * (Job.index == 1 && Maximize ? .8f : 1) * (Job.index == 1 && Route == 1 ? 1 + Mathf.Pow(1 - _player.HpRatio, 2) * 2 : 1);

        private void Expire(CharacterTimedEffect e)
        {
            if (e.id == "identity" && Job.index == 11 && Route == 2)
            {
                // Natural expiry only: Clear/death/retry never invokes a finisher. Use the last
                // confirmed target and existing third-combo potency, with a fresh live range check.
                var enemy = Resolve(Data.lastTarget);
                var combo = Job.Ability("g2");
                if (!_player.IsStunned && CombatRange.LivingEnemy(enemy) &&
                    CombatRange.InRange(transform, enemy, _actor.EffectiveRange(_actor.Selected(CharacterSlot.Gcd2))))
                {
                    var finisher = new RosterSkill("z2", "사신 빙의 · 유리꽃 피니시", 0, "", combo.power * CharacterRosterRules.ComboThirdMultiplier, combo.range, 0, combo.radius)
                        { shape = combo.shape, angle = combo.angle, stagger = combo.stagger };
                    AttackArea(enemy, finisher, 1, true);
                }
            }
            if (e.id.StartsWith("layers@"))
            {
                var ally = Resolve(e.target);
                Heal(ally, MaxHp(ally) * .04f * e.value);
            }
        }

        private void OnDisable()
        {
            if (!RaidCombatAuthority.Replica)
                State.Clear();
            HideGear();
        }
    }
}
