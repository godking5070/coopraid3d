using System.Collections.Generic;
using UnityEngine;

namespace RaidRogue
{
    // Runtime corridor and field. The host accepts a human crossing; bots never start a fight.
    public sealed class RaidApproachField : MonoBehaviour
    {
        public static RaidApproachField Instance { get; private set; }
        public bool Walking => StageManager.Instance != null &&
            (RaidNetworkSession.Multiplayer ? RaidNetworkSession.Instance.BattlePhase : StageManager.Instance.Progress.Phase) == RaidBattlePhase.Approach;
        public float FieldRadius => StageManager.Instance != null && StageManager.Instance.currentStage == 1 ? 18 : Mathf.Max(12, _layout != null && _layout.fieldRadius > 0 ? _layout.fieldRadius : 18);
        public float Width => Mathf.Max(4, _layout != null && _layout.approachWidth > 0 ? _layout.approachWidth : 8);
        public float Length => Mathf.Max(8, _layout != null && _layout.approachLength > 0 ? _layout.approachLength : 25);
        public float StartZ => -FieldRadius - Length;
        public float SpawnZ => StartZ + Mathf.Clamp(_layout != null && _layout.approachSpawnOffset > 0 ? _layout.approachSpawnOffset : 2, 1, Length - 2);
        public float RallyZ => -FieldRadius + Mathf.Clamp(_layout != null && _layout.rallyDistanceIntoField > 0 ? _layout.rallyDistanceIntoField : 4, 2, FieldRadius - 2);
        public float MovementRadius => Mathf.Max(FieldRadius + 2, -StartZ + 4);
        public float EntryZ => -FieldRadius + Mathf.Clamp(_layout != null && _layout.rallyDistanceIntoField > 0 ? _layout.rallyDistanceIntoField : 4, 2, FieldRadius - 2);
        public bool BossVisible { get; private set; }
        public RaidArrivalStageLayout Arrival(int stage)
        {
            if (_layout != null && _layout.bossArrivals != null)
                foreach (var entry in _layout.bossArrivals) if (entry != null && entry.stage == stage) return entry;
            return _defaultArrival;
        }
        public RaidDefeatStageLayout Defeat(int stage)
        {
            if (_layout != null && _layout.bossDefeats != null)
                foreach (var entry in _layout.bossDefeats) if (entry != null && entry.stage == stage) return entry;
            return _defaultDefeat;
        }
        public Vector3 RallyPoint(int role) => new Vector3((role - 2.5f) * Mathf.Min(1.4f, Width / 6f), 1, RallyZ);
        private readonly RaidArrivalStageLayout _defaultArrival = new RaidArrivalStageLayout();
        private readonly RaidDefeatStageLayout _defaultDefeat = new RaidDefeatStageLayout();
        private RaidLobbyLayout _layout;
        private GameObject _root, _gate, _portal;
        private readonly Dictionary<PlayerController, float> _normalRadii = new Dictionary<PlayerController, float>();
        private readonly List<PartyBotAI> _walkingBots = new List<PartyBotAI>();
        private bool _showing;
        public bool Showing => _showing;
        public bool PortalActive { get; private set; }
        public bool NearPortal(Transform member) => PortalActive && member != null &&
            Vector3.Distance(new Vector3(member.position.x, 0, member.position.z), Vector3.zero) <= 3.5f;
        public void SetPortalActive(bool active)
        {
            PortalActive = active;
            if (_portal != null) _portal.SetActive(active && _showing);
            if (active) QuarterViewCamera.LocalCamera?.ClearArrivalCamera();
        }
        private bool _sealed;
        private bool _bossCombatReady;
        private bool _approachEntered;
        private float _originalBossRadius;
        private Renderer[] _bossRenderers;
        private Collider[] _bossColliders;
        private bool[] _bossRendererStates, _bossColliderStates;
        private GameObject _bossHudRoot, _bossPhaseRoot;
        private bool _bossHudWasActive, _bossPhaseWasActive;
        private bool _bossHpTextEnabled, _bossHpFillEnabled, _bossPhaseTextEnabled;

        private void Awake()
        {
            Instance = this;
            _layout = Resources.Load<RaidLobbyLayout>("UI/RaidLobbyLayout");
            _originalBossRadius = GetComponent<StageManager>()?.boss?.arenaBoundaryRadius ?? 18;
            BuildGeometry();
        }
        public static RaidApproachField Ensure(StageManager stage)
            => stage.GetComponent<RaidApproachField>() ?? stage.gameObject.AddComponent<RaidApproachField>();

        public void Begin(bool retry)
        {
            QuarterViewCamera.LocalCamera?.ClearArrivalCamera();
            SetPortalActive(false);
            ResetBossPresentation();
            Show(true);
            _sealed = false;
            _approachEntered = false;
            _gate.SetActive(false);
            _walkingBots.Clear();
            SetBossVisible(false);
            int slot = 0;
            foreach (var member in PartyLife.OrderedMembers())
            {
                if (member == null || !member.gameObject.activeInHierarchy) continue;
                var pc = member.GetComponent<PlayerController>();
                if (pc != null && !_normalRadii.ContainsKey(pc)) _normalRadii.Add(pc, pc.arenaBoundaryRadius);
                if (pc != null) pc.arenaBoundaryRadius = MovementRadius;
                var bot = member.GetComponent<PartyBotAI>();
                if (bot != null) _walkingBots.Add(bot);
                float x = ((slot % 4) - 1.5f) * Mathf.Min(1.4f, Width / 6f);
                float retryZ = Mathf.Min(-FieldRadius + Mathf.Clamp(_layout != null && _layout.retryDistanceFromField > 0 ? _layout.retryDistanceFromField : 3, 1, FieldRadius - 2), EntryZ - 1);
                member.position = new Vector3(x, 1, retry ? retryZ : SpawnZ + (slot / 4) * 1.5f);
                slot++;
            }
            QuarterViewCamera.LocalCamera?.SnapToTargetImmediate();
        }
        public void Show(bool visible)
        {
            if (_root == null || _showing == visible) return;
            _showing = visible; _root.SetActive(visible);
            if (!visible)
            {
                SetPortalActive(false);
                SetBossVisible(false);
                QuarterViewCamera.LocalCamera?.ClearArrivalCamera();
                _approachEntered = false; _walkingBots.Clear(); RestoreMovementBounds();
                BossVisible = false;
                var boss = GetComponent<StageManager>()?.boss;
                if (boss != null) boss.arenaBoundaryRadius = _originalBossRadius;
            }
        }
        public void EnterApproach()
        {
            Show(true);
            if (_approachEntered) return;
            _approachEntered = true;
            _sealed = false;
            _gate.SetActive(false);
            foreach (var pc in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
            {
                if (!_normalRadii.ContainsKey(pc)) _normalRadii.Add(pc, pc.arenaBoundaryRadius);
                pc.arenaBoundaryRadius = MovementRadius;
            }
        }
        public bool HumanEntered()
        {
            if (!Walking || _sealed) return false;
            if (RaidNetworkSession.Multiplayer)
            {
                if (!RaidCombatAuthority.Host) return false;
                foreach (var avatar in RaidNetworkSession.Instance.Avatars.Values)
                    if (!avatar.IsBot && !avatar.Automated && avatar.gameObject.activeInHierarchy &&
                        CombatRange.LivingPartyMember(avatar.transform) && InsideEntry(avatar.transform.position)) return true;
                return false;
            }
            var player = StageManager.Instance?.player;
            return player != null && !player.IsDead && InsideEntry(player.transform.position);
        }
        private bool InsideEntry(Vector3 point) => point.z >= EntryZ && point.z < FieldRadius - 1 && Mathf.Abs(point.x) <= Width * .5f;
        public void SummonParty()
        {
            var boss = StageManager.Instance?.boss;
            if (boss == null) return;
            var layout = Arrival(StageManager.Instance.currentStage);
            Vector3 bossPoint = layout.bossPosition;
            bossPoint.x = Mathf.Clamp(bossPoint.x, -FieldRadius + 4, FieldRadius - 4);
            bossPoint.z = Mathf.Clamp(bossPoint.z, -FieldRadius + 4, FieldRadius - 4);
            boss.transform.position = bossPoint;
            boss.transform.rotation = Quaternion.Euler(0, layout.bossYaw, 0);
            Vector3 center = layout.partyCenter;
            Vector3 away = center - bossPoint; away.y = 0;
            if (away.sqrMagnitude < .01f) away = Vector3.back;
            center = bossPoint + away.normalized * Mathf.Clamp(layout.bossDistance, 3, FieldRadius - 3);
            center.y = Mathf.Max(1, layout.partyCenter.y);
            var members = PartyLife.OrderedMembers();
            float spacing = Mathf.Clamp(layout.partySpacing, 1, Mathf.Max(1, FieldRadius / 3));
            for (int i = 0; i < members.Count; i++)
            {
                var member = members[i];
                if (member == null || !member.gameObject.activeInHierarchy) continue;
                var pc = member.GetComponent<PlayerController>();
                pc?.StopForArrival();
                Vector3 right = Vector3.Cross(Vector3.up, away.normalized);
                Vector3 position = center + right * ((i % 4) - 1.5f) * spacing;
                position.x = Mathf.Clamp(position.x, -FieldRadius + 2, FieldRadius - 2);
                position.z = Mathf.Clamp(position.z, -FieldRadius + 2, FieldRadius - 2);
                member.position = position;
                Vector3 facing = bossPoint - position; facing.y = 0;
                if (facing.sqrMagnitude > .01f) member.rotation = Quaternion.LookRotation(facing);
                if (RaidCombatAuthority.Host) RaidNetworkSession.Instance?.CommitSkillRelocation(member);
            }
            SealField(false);
            QuarterViewCamera.LocalCamera?.SnapToTargetImmediate();
        }
        public void SealField(bool relocate = true)
        {
            if (_sealed) return;
            _sealed = true;
            if (relocate)
                foreach (var member in PartyLife.OrderedMembers())
                    if (member != null && member.gameObject.activeInHierarchy && member.position.z < -FieldRadius + 1)
                    {
                        member.position = RallyPoint(member.GetComponent<NetworkRaidAvatar>()?.Role ?? (int)(member.GetComponent<PartyBotAI>()?.role ?? PartyBotRole.MeleeDPS) + 1);
                        if (RaidCombatAuthority.Host) RaidNetworkSession.Instance?.CommitSkillRelocation(member);
                    }
            foreach (var pc in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
            {
                if (!_normalRadii.ContainsKey(pc)) _normalRadii.Add(pc, pc.arenaBoundaryRadius);
                pc.arenaBoundaryRadius = FieldRadius;
            }
            var boss = GetComponent<StageManager>()?.boss;
            if (boss != null) boss.arenaBoundaryRadius = FieldRadius;
            _gate.SetActive(true);
            QuarterViewCamera.LocalCamera?.SnapToTargetImmediate();
        }
        public void SetBossVisible(bool visible)
        {
            var boss = StageManager.Instance?.boss;
            if (boss == null) return;
            if (_bossRenderers != null && BossVisible == visible) return;
            if (_bossRenderers == null)
            {
                _bossRenderers = boss.GetComponentsInChildren<Renderer>(true);
                _bossColliders = boss.GetComponentsInChildren<Collider>(true);
                _bossRendererStates = System.Array.ConvertAll(_bossRenderers, r => r.enabled);
                _bossColliderStates = System.Array.ConvertAll(_bossColliders, c => c.enabled);
                _bossHudRoot = boss.bossHpText != null ? boss.bossHpText.transform.parent.gameObject : null;
                _bossPhaseRoot = boss.bossPhaseText != null ? boss.bossPhaseText.transform.parent.gameObject : null;
                if (_bossHudRoot != null && (_bossHudRoot.GetComponent<Canvas>() != null || _bossHudRoot.GetComponent<RaidUIRoot>() != null)) _bossHudRoot = null;
                if (_bossPhaseRoot != null && (_bossPhaseRoot.GetComponent<Canvas>() != null || _bossPhaseRoot.GetComponent<RaidUIRoot>() != null)) _bossPhaseRoot = null;
                _bossHudWasActive = _bossHudRoot != null && _bossHudRoot.activeSelf;
                _bossPhaseWasActive = _bossPhaseRoot != null && _bossPhaseRoot.activeSelf;
                _bossHpTextEnabled = boss.bossHpText != null && boss.bossHpText.enabled;
                _bossHpFillEnabled = boss.bossHpFillImage != null && boss.bossHpFillImage.enabled;
                _bossPhaseTextEnabled = boss.bossPhaseText != null && boss.bossPhaseText.enabled;
            }
            BossVisible = visible;
            for (int i = 0; i < _bossRenderers.Length; i++) if (_bossRenderers[i] != null) _bossRenderers[i].enabled = visible && _bossRendererStates[i];
            if (!visible) _bossCombatReady = false;
            ApplyBossColliders();
            if (_bossHudRoot != null) _bossHudRoot.SetActive(visible && _bossHudWasActive);
            if (_bossPhaseRoot != null) _bossPhaseRoot.SetActive(visible && _bossPhaseWasActive);
            if (boss.bossHpText != null) boss.bossHpText.enabled = visible && _bossHpTextEnabled;
            if (boss.bossHpFillImage != null) boss.bossHpFillImage.enabled = visible && _bossHpFillEnabled;
            if (boss.bossPhaseText != null) boss.bossPhaseText.enabled = visible && _bossPhaseTextEnabled;
            if (boss.mechanicAlertBannerText != null && !visible) boss.mechanicAlertBannerText.text = "";
            if (boss.bossCastBarRoot != null && !visible) boss.bossCastBarRoot.SetActive(false);
            boss.ShowArrivalName(visible);
            if (boss.targetRing != null) boss.targetRing.enabled = visible;
            if (!visible) TargetingManager.Instance?.ClearTarget();
        }
        public void ResetBossPresentation()
        {
            if (_bossRenderers != null)
                for (int i = 0; i < _bossRenderers.Length; i++)
                    if (_bossRenderers[i] != null) _bossRenderers[i].enabled = _bossRendererStates[i];
            if (_bossColliders != null)
                for (int i = 0; i < _bossColliders.Length; i++)
                    if (_bossColliders[i] != null) _bossColliders[i].enabled = _bossColliderStates[i];
            if (_bossHudRoot != null) _bossHudRoot.SetActive(_bossHudWasActive);
            if (_bossPhaseRoot != null) _bossPhaseRoot.SetActive(_bossPhaseWasActive);
            var boss = StageManager.Instance?.boss;
            if (boss != null)
            {
                if (boss.bossHpText != null) boss.bossHpText.enabled = _bossHpTextEnabled;
                if (boss.bossHpFillImage != null) boss.bossHpFillImage.enabled = _bossHpFillEnabled;
                if (boss.bossPhaseText != null) boss.bossPhaseText.enabled = _bossPhaseTextEnabled;
                if (boss.targetRing != null) boss.targetRing.enabled = true;
            }
            _bossRenderers = null; _bossColliders = null;
            _bossCombatReady = false;
            BossVisible = true; // Force the next hide to capture freshly rebuilt stage visuals.
        }
        public void PrepareReplicaStage()
        {
            _approachEntered = false;
            _sealed = false;
            if (_gate != null) _gate.SetActive(false);
            ResetBossPresentation();
        }
        public void SetBossCombatReady(bool ready)
        {
            if (_bossCombatReady == ready) return;
            _bossCombatReady = ready;
            ApplyBossColliders();
        }
        private void ApplyBossColliders()
        {
            if (_bossColliders == null) return;
            for (int i = 0; i < _bossColliders.Length; i++)
                if (_bossColliders[i] != null) _bossColliders[i].enabled = BossVisible && _bossCombatReady && _bossColliderStates[i];
        }
        private void RestoreMovementBounds()
        {
            foreach (var pair in _normalRadii) if (pair.Key != null) pair.Key.arenaBoundaryRadius = pair.Value;
            _normalRadii.Clear();
        }
        private void Update()
        {
            if (!Walking || RaidCombatAuthority.Replica) return;
            foreach (var bot in _walkingBots)
            {
                if (bot == null || !bot.gameObject.activeInHierarchy || bot.isDead) continue;
                var goal = RallyPoint((int)bot.role + 1);
                bot.transform.position = Vector3.MoveTowards(bot.transform.position, goal, bot.moveSpeed * Time.deltaTime);
            }
        }
        private void BuildGeometry()
        {
            _root = new GameObject("Raid approach · runtime");
            Block("Raid field floor", new Vector3(0, -.16f, 0),
                new Vector3(FieldRadius * 2 + 1, .3f, FieldRadius * 2 + 1), new Color(.21f, .23f, .27f), true);
            float end = -FieldRadius + 1;
            float middle = (StartZ + end) / 2;
            float span = end - StartZ + 4;
            Block("Approach floor", new Vector3(0, -.16f, middle), new Vector3(Width, .3f, span), new Color(.23f, .24f, .27f), true);
            Block("Left approach wall", new Vector3(-Width / 2 - .25f, 1.5f, middle), new Vector3(.5f, 3, span), new Color(.33f, .35f, .38f), true);
            Block("Right approach wall", new Vector3(Width / 2 + .25f, 1.5f, middle), new Vector3(.5f, 3, span), new Color(.33f, .35f, .38f), true);
            Block("Start wall", new Vector3(0, 1.5f, StartZ - 2), new Vector3(Width + 1, 3, .5f), new Color(.33f, .35f, .38f), true);
            Block("Rally line", new Vector3(0, .025f, RallyZ), new Vector3(Width + 2, .05f, .2f), new Color(.4f, .8f, .7f), false);
            _gate = Block("Combat gate", new Vector3(0, 1.6f, -FieldRadius + .5f), new Vector3(Width + 5, 3.2f, .5f), new Color(.55f, .32f, .2f), true);
            _gate.SetActive(false);
            _portal = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _portal.name = "Return portal · runtime";
            _portal.transform.SetParent(_root.transform, false);
            _portal.transform.localPosition = new Vector3(0, .12f, 0);
            _portal.transform.localScale = new Vector3(4.5f, .12f, 4.5f);
            _portal.GetComponent<Collider>().enabled = false;
            var portalColor = new MaterialPropertyBlock();
            portalColor.SetColor("_BaseColor", new Color(.15f, .75f, 1f));
            portalColor.SetColor("_Color", new Color(.15f, .75f, 1f));
            _portal.GetComponent<Renderer>().SetPropertyBlock(portalColor);
            _portal.SetActive(false);
            _root.SetActive(false);
        }
        private GameObject Block(string name, Vector3 pos, Vector3 size, Color color, bool solid)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name; go.transform.SetParent(_root.transform, false);
            go.transform.localPosition = pos; go.transform.localScale = size;
            if (!solid) go.GetComponent<Collider>().enabled = false;
            var block = new MaterialPropertyBlock(); block.SetColor("_BaseColor", color); block.SetColor("_Color", color);
            go.GetComponent<Renderer>().SetPropertyBlock(block);
            return go;
        }
        private void OnDestroy()
        {
            QuarterViewCamera.LocalCamera?.ClearArrivalCamera();
            RestoreMovementBounds();
            if (_root != null) Destroy(_root);
            if (Instance == this) Instance = null;
        }
    }
}
