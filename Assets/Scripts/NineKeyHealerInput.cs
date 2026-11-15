using System.Globalization;
using UnityEngine;

namespace RaidRogue
{
    public sealed partial class NineKeyHealerKit
    {
        private readonly AegisRecallInput _identityInput = new AegisRecallInput();
        public bool UsesGround => Active && Job.index == 3 && Route == 2;
        public bool UsesLongInput => Active && (UsesGround && Core(2, 1) || Job.index == 5 && Route == 1 && Core(1, 1));
        public bool AllowsLongInput => UsesLongInput;
        public void MovementStarted(int? supportId)
        {
            if(!Active||RaidCombatAuthority.Replica||StageManager.PlayerSkillsSuspended||!_actor.HasAuxiliary(NineKeyHealerData.Mobile))return;
            Bind();
            var targeting=TargetingManager.Instance;
            var ally=supportId.HasValue?CharacterKit.Resolve(supportId.Value):targeting!=null&&targeting.HasSupportSelection?targeting.SupportTarget:transform;
            Effect("mobile", "speed", transform, .15f, 3);
            if(ally!=transform&&Valid(ally,10))Effect("mobile", "speed", ally, .15f, 3);
        }
        public string Unavailable(CharacterSlot slot)
        {
            if (!Active) return "";
            Bind(); float resource = _runtime.Resource(Job.Resource);
            if (slot == CharacterSlot.OffGcd1)
            {
                string id = _actor.Selected(slot)?.id;
                if (Job.index == 4 && id == NineKeyHealerData.Id(Job, "a1") && _encore.Length == 0) return "재현할 Q/F 회복 기록 없음";
                if (Job.index == 5 && id == NineKeyHealerData.Id(Job, "a0"))
                {
                    var state = _resources.Snapshot;
                    if (_resources.Alchemy && state.ingredients.Length == 3 && System.Array.TrueForAll(state.ingredients, c => c == state.lastIngredient) || !_resources.Alchemy && resource >= 100) return "보충할 자원 여유 없음";
                }
            }
            if (slot != CharacterSlot.Identity) return "";
            if (Route == 0) return "루트 선택 필요";
            if (Job.index == 3)
            {
                if (Route == 3 && _harvest.Active) return "";
                if (UsesGround && Core(2, 1) && Flowers.Length > 0) return ""; // Moving an existing flower is free.
                if (resource < 1) return "꽃봉오리 1개 필요";
                if (Route == 1 && _garden.Snapshot.Length >= (Core(1, 0) ? 2 : 3)) return "묘목 대상 수 최대";
            }
            if (Job.index == 4)
            {
                if (Route == 3 && _song.Active || Route == 2 && _layers.Active) return "";
                if (resource < (Route == 2 ? 2 : 1)) return "공명음 " + (Route == 2 ? 2 : 1) + "개 필요";
                if (Route == 1 && _conductor.Snapshot.Length >= (Core(1, 0) ? 1 : 3)) return "요청·화답 보관 수 최대";
            }
            if (Job.index == 5)
            {
                if (Route == 1 && _link.Active) return _link.Snapshot.switchDelay > 0 ? "연결 재지정 대기" : "";
                if (Route == 3 && _surgery.Active) return _surgery.Ready ? "" : "후속 처치 입력 대기";
                if (resource < (Route == 2 ? 3 : 40)) return Route == 2 ? "배합 성분 3개 필요" : "진홍 영약 40 필요";
            }
            return "";
        }
        public bool Channeling { get { Bind(); return _song?.Active == true; } }
        public bool AiWantsIdentity(Transform patient, float health, bool incoming)
        {
            Bind(); float resource = _runtime.Resource(Job.Resource);
            if (Job.index == 3)
            {
                if (Route == 1) return resource >= 1 && Valid(patient, 25) && _garden.CanPlant(CharacterKit.Id(patient), Core(1, 0) ? 2 : 3);
                if (Route == 2) return resource >= 1 && Flowers.Length < (Core(2, 0) ? 1 : 2);
                if (Route == 3) return _harvest.Active ? health < .7f || _harvest.Snapshot.growth == _harvest.Snapshot.maximum && health < .95f || _harvest.Snapshot.remaining < 2 : resource >= 1;
            }
            if (Job.index == 4)
            {
                if (Route == 1) return resource >= 1 && _conductor.CanRequest(CharacterKit.Id(patient), CharacterKit.Id(transform), Core(1, 0) ? 1 : 3);
                if (Route == 2) return _layers.Active ? health < .55f : resource >= 2 && incoming;
                if (Route == 3) return !_song.Active && resource >= 2 && health < .85f;
            }
            if (Job.index == 5)
            {
                if (Route == 1) return _link.Active ? _link.Snapshot.first != CharacterKit.Id(patient) && health < .8f : resource >= 40;
                if (Route == 2) return resource >= 3 && (health < .9f || incoming);
                if (Route == 3) return _surgery.Ready && health < .85f && (_surgery.Active || resource >= 40);
            }
            return false;
        }
        public bool CanPlaceFlower(Vector3 point)
        {
            var player = GetComponent<PlayerController>();
            if (!UsesGround || player == null || !CharacterDefinitionValidation.Finite(point.x) || !CharacterDefinitionValidation.Finite(point.z)) return false;
            Vector2 p = new Vector2(point.x, point.z), self = new Vector2(transform.position.x, transform.position.z);
            float arena = RaidApproachField.Instance?.Showing == true ? RaidApproachField.Instance.FieldRadius : player.arenaBoundaryRadius;
            if ((p - self).sqrMagnitude > 625 || p.magnitude > arena) return false;
            var boss = StageManager.Instance?.boss;
            if (boss?.JustitiaEncounter == true && (JustitiaNormalRules.Clamp(point, boss.JustitiaHalfWidth, boss.JustitiaHalfDepth, 0) - point).sqrMagnitude > .001f) return false;
            if (boss?.JanusEncounter == true && p.magnitude > boss.janus.arenaRadius) return false;
            foreach (var hit in Physics.RaycastAll(new Vector3(point.x, transform.position.y + 3, point.z), Vector3.down, 6, ~0, QueryTriggerInteraction.Ignore))
                if (hit.normal.y > .8f && hit.collider.GetComponentInParent<PartyLife>() == null && hit.collider.GetComponentInParent<BossController>() == null && hit.collider.GetComponentInParent<BossAddMinion>() == null) return true;
            return false;
        }
        public void RequestIdentity(bool longInput = false)
        {
            if (!Active || !RaidCombatAuthority.Local(_actor) || RaidCombatAuthority.InputBlocked(_actor)) return;
            var targeting = TargetingManager.Instance;
            var support = targeting != null && targeting.HasSupportSelection ? targeting.SupportTarget : transform;
            var enemy = targeting?.AttackTarget;
            Vector3? point = null;
            string rule = longInput ? "healer.branch" : "support";
            string payload = RaidCombatAuthority.Id(support).ToString(CultureInfo.InvariantCulture);
            if (UsesGround)
            {
                var camera = Camera.main;
                if (camera == null || LocalGameplayInput.PointerOverUI(LocalGameplayInput.MousePosition)) return;
                var plane = new Plane(Vector3.up, Vector3.zero); var ray = camera.ScreenPointToRay(LocalGameplayInput.MousePosition);
                if (!plane.Raycast(ray, out float distance) || !CanPlaceFlower(ray.GetPoint(distance)))
                { GetComponent<PlayerController>()?.combatController?.ReportNetworkFailure("계절꽃을 놓을 유효한 지상 위치를 가리키세요 (25m)"); return; }
                point = ray.GetPoint(distance); rule = longInput ? "healer.move" : "healer.ground";
                payload = point.Value.x.ToString("R", CultureInfo.InvariantCulture) + "|" + point.Value.z.ToString("R", CultureInfo.InvariantCulture);
            }
            if (RaidCombatAuthority.Active) RaidNetworkSession.Instance.RequestCharacterAction(10, _actor.Definition.id, (int)CharacterSlot.Identity, RaidCombatAuthority.Id(enemy), rule, payload);
            else if (!_actor.Execute(CharacterSlot.Identity, enemy, out string failure, support, point, longInput)) GetComponent<PlayerController>()?.combatController?.ReportNetworkFailure(failure);
        }
        public void UpdateInput()
        {
            UpdateFlowerPresentation();
            if (!UsesLongInput || GetComponent<PlayerController>() == null || !RaidCombatAuthority.Local(_actor)) return;
            string binding = CharacterSlots.Binding(_actor.Definition, CharacterSlot.Identity);
            bool blocked = RaidCombatAuthority.InputBlocked(_actor);
            foreach (var slot in new[] { CharacterSlot.Job1, CharacterSlot.OffGcd2, CharacterSlot.Counter, CharacterSlot.Movement })
                if (LocalInputBindings.Pressed(CharacterSlots.Binding(_actor.Definition, slot))) blocked = true;
            int gesture = _identityInput.Step(LocalInputBindings.Pressed(binding), LocalInputBindings.Held(binding), Time.unscaledTime, blocked);
            if (gesture != 0) RequestIdentity(gesture == 2);
        }
    }
}
