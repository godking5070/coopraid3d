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
        public float FieldRadius => StageManager.Instance != null && StageManager.Instance.currentStage == 1 ? 18 :
            StageManager.Instance != null && StageManager.Instance.currentStage == 2 ? 17.5f :
            StageManager.Instance?.boss?.JanusEncounter==true ? StageManager.Instance.boss.JanusSettings.arenaRadius :
            StageManager.Instance?.boss?.CampanellaEncounter == true ? Mathf.Max(12,StageManager.Instance.boss.CampanellaHard?
                StageManager.Instance.boss.campanellaHard.combat.arenaRadius:StageManager.Instance.boss.campanella.arenaRadius) :
            StageManager.Instance?.boss?.MarionetteEncounter == true ? StageManager.Instance.boss.MarionetteStageHalfSize*Mathf.Sqrt(2) :
            StageManager.Instance?.boss?.JustitiaEncounter == true ? Mathf.Sqrt(Mathf.Pow(StageManager.Instance.boss.JustitiaHalfWidth,2)+Mathf.Pow(StageManager.Instance.boss.JustitiaHalfDepth,2)) :
            Mathf.Max(12, _layout != null && _layout.fieldRadius > 0 ? _layout.fieldRadius : 18);
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
        private GameObject _floraScenery, _floraFlower;
        private GameObject _campScenery, _campCloak;
        private GameObject _marScenery, _marDolls;
        private GameObject _justScenery, _justBook;
        private GameObject _janusScenery,_janusBar,_janusChains;
        private bool _janusInspected,_janusHintVisible;
        [Header("Stage 6 development dialogue branch; replace when personality data exists")]
        [SerializeField] private JanusBarVoice janusDevelopmentVoice=JanusBarVoice.Belief;
        private bool _justInspected,_justHintVisible;
        [Header("Stage 5 development dialogue branch; replace when personality data exists")]
        [SerializeField] private JustitiaLawVoice justitiaDevelopmentVoice=JustitiaLawVoice.Belief;
        private bool _marInspected,_marHintVisible;
        [Header("Stage 4 development dialogue branch; replace when personality data exists")]
        [SerializeField] private MarionetteDollsVoice marionetteDevelopmentVoice=MarionetteDollsVoice.Belief;
        private readonly Renderer[] _campPlateRenderers=new Renderer[4];
        private readonly TextMesh[] _campPlateLabels=new TextMesh[4];
        private int _campVisualActive=int.MinValue,_campVisualSuccess,_campVisualFailure,_campVisualMissing;
        private bool _campInspected,_campHintVisible;
        private float _builtFieldRadius;
        private int _builtFieldShape, _sceneryStage = -1;
        [Header("Stage 3 development dialogue branch; replace when personality data exists")]
        [SerializeField] private CampanellaCloakVoice campanellaDevelopmentVoice=CampanellaCloakVoice.Belief;
        private Animator _floraIrisAnimator;
        private Material _floraGlassMaterial;
        private Material _janusFloorMaterial;
        private Mesh _janusFloorMesh;
        private bool _floraInspected;
        private bool _floraHintVisible;
        [Header("Stage 2 development dialogue branch; replace when personality data exists")]
        [SerializeField] private FloraMemorialVoice floraDevelopmentVoice = FloraMemorialVoice.Belief;
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
            SetCampanellaPlateState(-1,0,0);
            _floraInspected = false;
            _floraHintVisible = false;
            _campInspected=false;_campHintVisible=false;
            _marInspected=false;_marHintVisible=false;
            _justInspected=false;_justHintVisible=false;
            _janusInspected=false;_janusHintVisible=false;
            SetJanusChainBroken(false);
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
            // Replicas enter via Show/EnterApproach rather than the host-only Begin.
            // Keep stage scenery and arena geometry in sync on both paths.
            if (visible) EnsureStageGeometry();
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
            if (RaidNetworkSession.Instance?.AiSpectatorActive == true)
            {
                var leader=RaidNetworkSession.Instance.AiSpectatorLeader;
                // Spectator combat starts as soon as loading completes; SummonParty places
                // all four AI at the authored boss-room arrival positions in the same frame.
                return leader!=null && CombatRange.LivingPartyMember(leader);
            }
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
            if(boss.JanusEncounter){boss.PrepareJanusArrival();bossPoint=boss.transform.position;}
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
            if(boss.JanusEncounter)boss.SetJanusOmegaVisible(visible);
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
            // Before the first hide there is no saved presentation to restore.
            // Default false values must not disable the authored HUD before capture.
            if (boss != null && _bossRenderers != null)
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
            UpdateFloraMemorial();
            UpdateCampanellaCloak();
            UpdateMarionetteDolls();
            UpdateJustitiaLawBook();
            UpdateJanusBar();
            if(_campScenery!=null && _campScenery.activeInHierarchy && Camera.main!=null)
                foreach(var label in _campPlateLabels)if(label!=null)label.transform.rotation=Camera.main.transform.rotation;
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
            _builtFieldRadius=FieldRadius;
            _builtFieldShape=FieldShape;
            _sceneryStage=-1;
            _root = new GameObject("Raid approach · runtime");
            var fieldFloor=Block("Raid field floor", new Vector3(0, -.16f, StageManager.Instance?.boss?.JanusEncounter==true?-FieldRadius*.5f:0),
                StageManager.Instance?.boss?.JustitiaEncounter==true
                    ? new Vector3(StageManager.Instance.boss.JustitiaHalfWidth*2+1,.3f,StageManager.Instance.boss.JustitiaHalfDepth*2+1)
                    : StageManager.Instance?.boss?.JanusEncounter==true
                    ? new Vector3(FieldRadius*2+1,.3f,FieldRadius+1)
                    : new Vector3(FieldRadius * 2 + 1, .3f, FieldRadius * 2 + 1), new Color(.21f, .23f, .27f), true);
            if(StageManager.Instance?.boss?.JanusEncounter==true)fieldFloor.GetComponent<Renderer>().enabled=false;
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
            _portal = RaidPrimitive.Create(PrimitiveType.Cylinder);
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
            BuildFloraScenery();
            BuildCampanellaScenery();
            BuildMarionetteScenery();
            BuildJustitiaScenery();
            BuildJanusScenery();
            _root.SetActive(false);
        }
        private int FieldShape => StageManager.Instance?.boss?.JanusEncounter == true ? 2 :
            StageManager.Instance?.boss?.JustitiaEncounter == true ? 1 : 0;
        private void EnsureStageGeometry()
        {
            if (_root == null || Mathf.Abs(_builtFieldRadius - FieldRadius) > .1f || _builtFieldShape != FieldShape)
            {
                if (_root != null) { _root.SetActive(false); Destroy(_root); }
                ClearSceneryResources();
                BuildGeometry();
                // BuildGeometry creates an inactive root even if the previous root was shown.
                _showing = false;
                _sealed = false; _approachEntered = false; PortalActive = false;
                _walkingBots.Clear();
            }
            int stage = StageManager.Instance != null ? StageManager.Instance.currentStage : 0;
            if (_sceneryStage == stage) return;
            _sceneryStage = stage;
            if (_floraScenery != null) _floraScenery.SetActive(stage == 2);
            if (_campScenery != null) _campScenery.SetActive(stage == 3);
            if (_marScenery != null) _marScenery.SetActive(stage == 4);
            if (_justScenery != null) _justScenery.SetActive(stage == 5);
            if (_janusScenery != null) _janusScenery.SetActive(stage == 6);
        }
        private void BuildFloraScenery()
        {
            _floraScenery = new GameObject("Stage 2 greenhouse visuals");
            _floraScenery.transform.SetParent(_root.transform,false);
            if(_layout != null && _layout.stage2GreenhousePrefab != null)
            {
                var authored=Instantiate(_layout.stage2GreenhousePrefab,_floraScenery.transform);
                authored.name="Authored greenhouse";
                foreach(var collider in authored.GetComponentsInChildren<Collider>())collider.enabled=false;
            }
            else
            {
                var glass=Decor("Glass floor",PrimitiveType.Cylinder,new Vector3(0,.02f,0),new Vector3(35,.015f,35),new Color(.14f,.38f,.44f,.55f));
                _floraGlassMaterial=AoETelegraph.CreateTransparentTelegraphMaterial(new Color(.14f,.38f,.44f,.55f));
                glass.GetComponent<Renderer>().sharedMaterial=_floraGlassMaterial;
                glass.transform.SetParent(_floraScenery.transform,true);
                for(int i=0;i<4;i++)
                {
                    float a=i*Mathf.PI*.5f;
                    var line=Decor("Quadrant brass seam",PrimitiveType.Cube,new Vector3(Mathf.Sin(a)*8,.02f,Mathf.Cos(a)*8),new Vector3(.12f,.04f,16),new Color(.8f,.6f,.22f));
                    line.transform.SetParent(_floraScenery.transform,true);line.transform.rotation=Quaternion.Euler(0,i*90,0);
                    var root=Decor("Blue root below glass",PrimitiveType.Cube,new Vector3(Mathf.Sin(a)*6,.015f,Mathf.Cos(a)*6),new Vector3(.25f,.02f,9),new Color(.08f,.55f,.9f));
                    root.transform.SetParent(_floraScenery.transform,true);root.transform.rotation=Quaternion.Euler(0,i*90+20,0);
                    var gear=Decor("Brass gear under glass",PrimitiveType.Cylinder,new Vector3(Mathf.Sin(a)*10,.018f,Mathf.Cos(a)*10),new Vector3(3,.005f,3),new Color(.55f,.4f,.15f));
                    gear.transform.SetParent(_floraScenery.transform,true);
                }
                for(int i=0;i<12;i++)
                {
                    float a=i*Mathf.PI*2/12;
                    var petal=Decor("Suspended blue rose petal",PrimitiveType.Sphere,
                        new Vector3(Mathf.Sin(a)*12,4+(i%3)*.8f,Mathf.Cos(a)*12),new Vector3(.22f,.06f,.42f),new Color(.12f,.46f,.9f));
                    petal.transform.SetParent(_floraScenery.transform,true);
                    petal.transform.rotation=Quaternion.Euler(20,i*30,35);
                }
                for(int i=0;i<32;i++)
                {
                    float a=i*Mathf.PI*2/32;
                    var frame=Decor("Dome brass frame",PrimitiveType.Cube,new Vector3(Mathf.Sin(a)*17.5f,4,Mathf.Cos(a)*17.5f),new Vector3(.18f,8,.18f),new Color(.65f,.5f,.24f));
                    frame.transform.SetParent(_floraScenery.transform,true);
                }
            }
            var balcony=Decor("Iris balcony",PrimitiveType.Cube,new Vector3(19,3,2),new Vector3(4,.3f,4),new Color(.65f,.53f,.28f));
            balcony.transform.SetParent(_floraScenery.transform,true);
            bool irisVisualOnly=_layout != null && _layout.stage2IrisBalconyPrefab != null &&
                _layout.stage2IrisBalconyPrefab.GetComponentInChildren<PartyLife>(true)==null &&
                _layout.stage2IrisBalconyPrefab.GetComponentInChildren<PlayerController>(true)==null &&
                _layout.stage2IrisBalconyPrefab.GetComponentInChildren<PartyBotAI>(true)==null &&
                _layout.stage2IrisBalconyPrefab.GetComponentInChildren<NetworkRaidAvatar>(true)==null;
            GameObject iris=irisVisualOnly ? Instantiate(_layout.stage2IrisBalconyPrefab) :
                Decor("Iris visual placeholder",PrimitiveType.Capsule,new Vector3(19,4.2f,2),new Vector3(.7f,1.5f,.7f),new Color(.95f,.85f,.55f));
            iris.transform.SetParent(_floraScenery.transform,true);
            iris.transform.position=new Vector3(19,4.2f,2);
            _floraIrisAnimator=iris.GetComponentInChildren<Animator>();
            foreach(var collider in iris.GetComponentsInChildren<Collider>())collider.enabled=false;
            _floraFlower=_layout != null && _layout.stage2MemorialFlowerPrefab != null ? Instantiate(_layout.stage2MemorialFlowerPrefab) :
                Decor("Blue memorial flower placeholder",PrimitiveType.Sphere,new Vector3(-2,.8f,StartZ+8),new Vector3(.8f,.8f,.8f),new Color(.12f,.55f,1));
            _floraFlower.name="시들지 않는 푸른 헌화";
            _floraFlower.transform.SetParent(_floraScenery.transform,true);
            _floraFlower.transform.position=new Vector3(-2,.8f,StartZ+8);
            foreach(var collider in _floraFlower.GetComponentsInChildren<Collider>())collider.enabled=false;
            _floraScenery.SetActive(false);
        }
        private void BuildCampanellaScenery()
        {
            _campVisualActive=int.MinValue;
            _campScenery=new GameObject("Stage 3 open bell tower visuals");
            _campScenery.transform.SetParent(_root.transform,false);
            if(_layout!=null && _layout.stage3BellTowerPrefab!=null)
            {
                var authored=Instantiate(_layout.stage3BellTowerPrefab,_campScenery.transform);
                authored.name="Authored bell tower";
                foreach(var collider in authored.GetComponentsInChildren<Collider>())collider.enabled=false;
            }
            else
            {
                // Cylinder half-height is .06: keep its top at .02, above the saved Y=0 arena floor
                // and below attack telegraphs at .06, so the two floor surfaces cannot z-fight.
                var stone=Decor("Circular stone tower crown",PrimitiveType.Cylinder,new Vector3(0,-.04f,0),
                    new Vector3(FieldRadius*2,.06f,FieldRadius*2),new Color(.29f,.3f,.32f));
                stone.transform.SetParent(_campScenery.transform,true);
                for(int i=0;i<32;i++)
                {
                    float angle=i*Mathf.PI*2/32;
                    var segment=Decor("Decorative muted sound ring",PrimitiveType.Cube,
                        new Vector3(Mathf.Sin(angle)*14,.06f,Mathf.Cos(angle)*14),
                        new Vector3(2.8f,.025f,.15f),new Color(.53f,.4f,.18f));
                    segment.transform.SetParent(_campScenery.transform,true);
                    segment.transform.rotation=Quaternion.Euler(0,i*360f/32,0);
                    if(i%4!=0)continue;
                    var buttress=Decor("Open gothic buttress",PrimitiveType.Cube,
                        new Vector3(Mathf.Sin(angle)*(FieldRadius-.6f),1.6f,Mathf.Cos(angle)*(FieldRadius-.6f)),
                        new Vector3(.8f,3.2f,.8f),new Color(.38f,.39f,.41f));
                    buttress.transform.SetParent(_campScenery.transform,true);
                }
            }
            var boss=StageManager.Instance?.boss;
            for(int i=0;i<4;i++)
            {
                Vector3 point=boss!=null?boss.CampanellaPlatePosition(i):new Vector3(i==0||i==3?-9:9,0,i<2?9:-9);
                var plate=Decor("Resonance plate "+(i+1),PrimitiveType.Cylinder,point+Vector3.up*.09f,
                    new Vector3(4.8f,.06f,4.8f),new Color(.66f,.48f,.17f));
                plate.transform.SetParent(_campScenery.transform,true);
                _campPlateRenderers[i]=plate.GetComponent<Renderer>();
                var label=new GameObject("Resonance numeral "+(i+1)).AddComponent<TextMesh>();
                label.text=new[]{"I","II","III","IV"}[i];label.fontSize=64;label.fontStyle=FontStyle.Bold;
                label.anchor=TextAnchor.MiddleCenter;label.alignment=TextAlignment.Center;label.color=new Color(1,.92f,.55f);
                label.transform.SetParent(_campScenery.transform,false);label.transform.position=point+Vector3.up*.65f;
                label.transform.localScale=Vector3.one*.12f;_campPlateLabels[i]=label;
            }
            _campCloak=_layout!=null && _layout.stage3ExpeditionCloakPrefab!=null
                ?Instantiate(_layout.stage3ExpeditionCloakPrefab)
                :Decor("Torn blue expedition cloak",PrimitiveType.Cube,new Vector3(-2,.8f,StartZ+8),
                    new Vector3(1.6f,.08f,1.1f),new Color(.12f,.3f,.62f));
            _campCloak.name="종에 감겨 있던 원정대 망토";
            _campCloak.transform.SetParent(_campScenery.transform,true);
            _campCloak.transform.position=new Vector3(-2,.8f,StartZ+8);
            foreach(var collider in _campCloak.GetComponentsInChildren<Collider>())collider.enabled=false;
            _campScenery.SetActive(false);
        }
        public void SetCampanellaPlateState(int active,int successMask,int failureMask,Vector3[] points=null,int missingMask=0)
        {
            if(points==null && active==_campVisualActive && successMask==_campVisualSuccess &&
                failureMask==_campVisualFailure && missingMask==_campVisualMissing)return;
            _campVisualActive=active;_campVisualSuccess=successMask;_campVisualFailure=failureMask;_campVisualMissing=missingMask;
            for(int i=0;i<4;i++)
            {
                if(_campPlateRenderers[i]==null)continue;
                if(points!=null && points.Length>i)
                {
                    _campPlateRenderers[i].transform.position=points[i]+Vector3.up*.09f;
                    if(_campPlateLabels[i]!=null)_campPlateLabels[i].transform.position=points[i]+Vector3.up*.65f;
                }
                Color color=(successMask&(1<<i))!=0?new Color(.18f,.75f,.4f):
                    (missingMask&(1<<i))!=0?new Color(.5f,.5f,.52f):
                    (failureMask&(1<<i))!=0?new Color(.8f,.22f,.18f):
                    active==i?new Color(1,.9f,.3f):new Color(.66f,.48f,.17f);
                var block=new MaterialPropertyBlock();block.SetColor("_BaseColor",color);block.SetColor("_Color",color);
                _campPlateRenderers[i].SetPropertyBlock(block);
                if(_campPlateLabels[i]!=null)
                {
                    _campPlateLabels[i].color=color;
                    _campPlateLabels[i].text=new[]{"I","II","III","IV"}[i]+
                        ((successMask&(1<<i))!=0?" ✓":(missingMask&(1<<i))!=0?" 미배정":
                        (failureMask&(1<<i))!=0?" ✕":active==i?" 현재":"");
                }
            }
        }
        private void BuildMarionetteScenery()
        {
            _marScenery=new GameObject("Stage 4 gothic opera theater visuals");
            _marScenery.transform.SetParent(_root.transform,false);
            float half=StageManager.Instance?.boss?.MarionetteStageHalfSize??15;
            if(_layout!=null && _layout.stage4OperaTheaterPrefab!=null)
            {
                var authored=Instantiate(_layout.stage4OperaTheaterPrefab,_marScenery.transform);
                authored.name="Authored opera theater";
                foreach(var collider in authored.GetComponentsInChildren<Collider>())collider.enabled=false;
            }
            else
            {
                var stage=Decor("Black and white marble main stage",PrimitiveType.Cube,new Vector3(0,-.05f,0),
                    new Vector3(half*2,.08f,half*2),new Color(.23f,.22f,.25f));
                stage.transform.SetParent(_marScenery.transform,true);
                for(int x=-3;x<3;x++)for(int z=-3;z<3;z++)if((x+z)%2==0)
                {
                    var tile=Decor("Decorative marble check",PrimitiveType.Cube,new Vector3((x+.5f)*half/3,.01f,(z+.5f)*half/3),
                        new Vector3(half/3-.05f,.02f,half/3-.05f),new Color(.66f,.64f,.61f));
                    tile.transform.SetParent(_marScenery.transform,true);
                }
                for(int i=0;i<10;i++)
                {
                    var seat=Decor("Mannequin audience · non target",PrimitiveType.Capsule,
                        new Vector3((i%5-2)*2,1,half+8+i/5*2),new Vector3(.5f,1.6f,.5f),new Color(.36f,.34f,.39f));
                    seat.transform.SetParent(_marScenery.transform,true);
                }
                var rear=Decor("Rear puppet stage · visual only",PrimitiveType.Cube,new Vector3(0,1,half+5),
                    new Vector3(10,2,.5f),new Color(.2f,.12f,.19f));rear.transform.SetParent(_marScenery.transform,true);
                for(int i=0;i<8;i++)
                {
                    var thread=Decor("Decorative golden thread",PrimitiveType.Cylinder,
                        new Vector3((i-3.5f)*3,4,(-1+i%3)*5),new Vector3(.02f,6,.02f),new Color(.68f,.48f,.16f));
                    thread.transform.SetParent(_marScenery.transform,true);
                }
            }
            _marDolls=_layout!=null && _layout.stage4BrokenDollsPrefab!=null?
                Instantiate(_layout.stage4BrokenDollsPrefab):
                Decor("Twelve broken knight dolls and saint doll",PrimitiveType.Cube,
                    new Vector3(-2,.6f,StartZ+8),new Vector3(2,.8f,1),new Color(.52f,.4f,.36f));
            _marDolls.name="열두 개의 부서진 기사 인형과 성녀 인형";
            _marDolls.transform.SetParent(_marScenery.transform,true);
            _marDolls.transform.position=new Vector3(-2,.6f,StartZ+8);
            foreach(var collider in _marDolls.GetComponentsInChildren<Collider>())collider.enabled=false;
            _marScenery.SetActive(false);
        }
        private void UpdateMarionetteDolls()
        {
            if(StageManager.Instance?.currentStage!=4 || !Walking || _marDolls==null || LocalGameplayInput.Blocked)
            {ClearMarionetteHint();return;}
            var local=StageManager.Instance.player;
            if(local==null || !CombatRange.LivingPartyMember(local.transform) ||
                Vector3.Distance(local.transform.position,_marDolls.transform.position)>3)
            {ClearMarionetteHint();return;}
            var ui=FindFirstObjectByType<RaidLobbyUI>();
            if(!_marInspected){ui?.Hint(LocalInputBindings.Label("Interact")+" · 열두 개의 부서진 기사 인형과 성녀 인형 조사");_marHintVisible=true;}
            if(_marInspected || !LocalInputBindings.Pressed("Interact"))return;
            _marInspected=true;LocalInputBindings.QuarantineHeld();ClearMarionetteHint();
            ui?.Say(MarionetteDollsText.Description+"\n"+MarionetteDollsText.Line(marionetteDevelopmentVoice));
        }
        private void ClearMarionetteHint()
        {
            if(!_marHintVisible)return;
            FindFirstObjectByType<RaidLobbyUI>()?.Hint(null);_marHintVisible=false;
        }
        private void BuildJustitiaScenery()
        {
            _justScenery=new GameObject("Stage 5 courthouse visuals");
            _justScenery.transform.SetParent(_root.transform,false);
            float width=StageManager.Instance?.boss?.JustitiaHalfWidth??18;
            float depth=StageManager.Instance?.boss?.JustitiaHalfDepth??12;
            if(_layout?.stage5CourthousePrefab!=null)
            {
                var authored=Instantiate(_layout.stage5CourthousePrefab,_justScenery.transform);
                authored.name="Authored courthouse";
                foreach(var collider in authored.GetComponentsInChildren<Collider>())collider.enabled=false;
            }
            else
            {
                var floor=Decor("Courthouse marble floor",PrimitiveType.Cube,new Vector3(0,-.04f,0),
                    new Vector3(width*2,.08f,depth*2),new Color(.28f,.27f,.25f));floor.transform.SetParent(_justScenery.transform,true);
                var left=Decor("LEFT balance zone decorative plate",PrimitiveType.Cube,new Vector3(-width*.5f,.015f,0),
                    new Vector3(width-.8f,.025f,depth*2-.8f),new Color(.39f,.33f,.16f));left.transform.SetParent(_justScenery.transform,true);
                var right=Decor("RIGHT balance zone decorative plate",PrimitiveType.Cube,new Vector3(width*.5f,.015f,0),
                    new Vector3(width-.8f,.025f,depth*2-.8f),new Color(.42f,.35f,.19f));right.transform.SetParent(_justScenery.transform,true);
                var seam=Decor("Center unassigned boundary",PrimitiveType.Cube,new Vector3(0,.04f,0),
                    new Vector3(1.3f,.03f,depth*2),new Color(.16f,.15f,.16f));seam.transform.SetParent(_justScenery.transform,true);
                for(int i=0;i<6;i++)
                {
                    var juror=Decor("Blindfolded stone juror · decoration",PrimitiveType.Capsule,
                        new Vector3((i-2.5f)*5,1.4f,depth+2),new Vector3(1,2.8f,1),new Color(.42f,.42f,.4f));
                    juror.transform.SetParent(_justScenery.transform,true);
                }
                var beam=Decor("Suspended golden scale · decoration",PrimitiveType.Cube,new Vector3(0,5,depth+2),
                    new Vector3(10,.2f,.3f),new Color(.8f,.63f,.2f));beam.transform.SetParent(_justScenery.transform,true);
            }
            _justBook=_layout?.stage5LawBookPrefab!=null?Instantiate(_layout.stage5LawBookPrefab):
                Decor("Clocktower equivalent exchange law book",PrimitiveType.Cube,
                    new Vector3(-2,.5f,StartZ+8),new Vector3(1,.3f,.7f),new Color(.58f,.4f,.13f));
            _justBook.name="시계탑 등가교환의 법전";
            _justBook.transform.SetParent(_justScenery.transform,true);
            _justBook.transform.position=new Vector3(-2,.5f,StartZ+8);
            foreach(var collider in _justBook.GetComponentsInChildren<Collider>())collider.enabled=false;
            _justScenery.SetActive(false);
        }
        private void BuildJanusScenery()
        {
            _janusScenery=new GameObject("Stage 6 sealed clocktower gate visuals");
            _janusScenery.transform.SetParent(_root.transform,false);
            if(_layout?.stage6GateFieldPrefab!=null)
            {var authored=Instantiate(_layout.stage6GateFieldPrefab,_janusScenery.transform);
             authored.name="Authored Janus gate field";
             foreach(var collider in authored.GetComponentsInChildren<Collider>())collider.enabled=false;}
            else
            {
                BuildJanusFloor();
                var door=Decor("Sealed gate · 50m visual",PrimitiveType.Cube,new Vector3(0,25,2.5f),
                    new Vector3(26,50,1),new Color(.22f,.24f,.31f));door.transform.SetParent(_janusScenery.transform,true);
                for(int i=0;i<4;i++)
                {var point=StageManager.Instance?.boss?.janus?.postPositions;
                 var at=point!=null&&point.Length==4?point[i]:new Vector3(i<2?-10:10,0,i%2==0?-8:-14);
                 var guide=Decor("Chain winch "+(i+1)+" · decoration",PrimitiveType.Cylinder,
                     at+Vector3.up*.08f,new Vector3(2,.15f,2),new Color(.43f,.24f,.19f));guide.transform.SetParent(_janusScenery.transform,true);}
            }
            _janusChains=new GameObject("Janus seal chains · clear on four posts");
            _janusChains.transform.SetParent(_janusScenery.transform,false);
            if(_layout?.stage6SealChainsPrefab!=null)
            {var authoredChains=Instantiate(_layout.stage6SealChainsPrefab,_janusChains.transform);
             foreach(var collider in authoredChains.GetComponentsInChildren<Collider>())collider.enabled=false;}
            else for(int i=0;i<3;i++)
            {var chain=Decor("Red seal chain · decoration",PrimitiveType.Cube,new Vector3(0,10+i*9,1.85f),
                new Vector3(28,.35f,.4f),new Color(.75f,.13f,.15f));chain.transform.SetParent(_janusChains.transform,true);}
            var north=Block("Stage 6 north boundary · no Part 2 entry",new Vector3(0,1.5f,1),
                new Vector3(FieldRadius*2+1,3,.5f),new Color(.21f,.23f,.27f),true);
            north.transform.SetParent(_janusScenery.transform,true);
            var renderer=north.GetComponent<Renderer>();if(renderer!=null)renderer.enabled=false;
            _janusBar=_layout?.stage6WeldedBarPrefab!=null?Instantiate(_layout.stage6WeldedBarPrefab):
                Decor("Welded gate bar",PrimitiveType.Cube,new Vector3(2,.7f,StartZ+8),
                    new Vector3(1.4f,.2f,.6f),new Color(.45f,.42f,.35f));
            _janusBar.name="안쪽에서 걸어 잠근 정문의 빗장";
            _janusBar.transform.SetParent(_janusScenery.transform,true);
            _janusBar.transform.position=new Vector3(2,.7f,StartZ+8);
            foreach(var collider in _janusBar.GetComponentsInChildren<Collider>())collider.enabled=false;
            _janusScenery.SetActive(false);
        }
        public void SetJanusChainBroken(bool broken)
        {if(_janusChains!=null)_janusChains.SetActive(!broken);}
        private void BuildJanusFloor()
        {
            const int segments=48;
            var vertices=new Vector3[segments+2];var triangles=new int[segments*3];
            vertices[0]=new Vector3(0,.02f,0);
            for(int i=0;i<=segments;i++)
            {float angle=Mathf.PI+i*Mathf.PI/segments;
             vertices[i+1]=new Vector3(Mathf.Cos(angle)*FieldRadius,.02f,Mathf.Sin(angle)*FieldRadius);}
            for(int i=0;i<segments;i++)
            {triangles[i*3]=0;triangles[i*3+1]=i+2;triangles[i*3+2]=i+1;}
            _janusFloorMesh=new Mesh{name="Janus 40m semicircle"};
            _janusFloorMesh.vertices=vertices;_janusFloorMesh.triangles=triangles;
            _janusFloorMesh.RecalculateNormals();
            var floor=new GameObject("Janus 40m semicircle floor · visual");floor.transform.SetParent(_janusScenery.transform,false);
            floor.AddComponent<MeshFilter>().sharedMesh=_janusFloorMesh;
            _janusFloorMaterial=AoETelegraph.CreateTransparentTelegraphMaterial(new Color(.27f,.29f,.32f,1));
            floor.AddComponent<MeshRenderer>().sharedMaterial=_janusFloorMaterial;
        }
        private void UpdateJanusBar()
        {
            if(StageManager.Instance?.currentStage!=6||!Walking||_janusBar==null||LocalGameplayInput.Blocked)
            {ClearJanusBarHint();return;}
            var local=StageManager.Instance.player;
            if(local==null||!CombatRange.LivingPartyMember(local.transform)||
                Vector3.Distance(local.transform.position,_janusBar.transform.position)>3)
            {ClearJanusBarHint();return;}
            var ui=FindFirstObjectByType<RaidLobbyUI>();
            if(!_janusInspected){ui?.Hint(LocalInputBindings.Label("Interact")+" · 정문의 빗장 조사");_janusHintVisible=true;}
            if(_janusInspected||!LocalInputBindings.Pressed("Interact"))return;
            _janusInspected=true;LocalInputBindings.QuarantineHeld();ClearJanusBarHint();
            ui?.Say(JanusBarText.Description+"\n"+JanusBarText.Line(janusDevelopmentVoice));
        }
        private void ClearJanusBarHint()
        {if(!_janusHintVisible)return;FindFirstObjectByType<RaidLobbyUI>()?.Hint(null);_janusHintVisible=false;}
        private void UpdateJustitiaLawBook()
        {
            if(StageManager.Instance?.currentStage!=5||!Walking||_justBook==null||LocalGameplayInput.Blocked)
            {ClearJustitiaHint();return;}
            var local=StageManager.Instance.player;
            if(local==null||!CombatRange.LivingPartyMember(local.transform)||
                Vector3.Distance(local.transform.position,_justBook.transform.position)>3)
            {ClearJustitiaHint();return;}
            var ui=FindFirstObjectByType<RaidLobbyUI>();
            if(!_justInspected){ui?.Hint(LocalInputBindings.Label("Interact")+" · 시계탑 등가교환의 법전 조사");_justHintVisible=true;}
            if(_justInspected||!LocalInputBindings.Pressed("Interact"))return;
            _justInspected=true;LocalInputBindings.QuarantineHeld();ClearJustitiaHint();
            ui?.Say(JustitiaLawText.Article+"\n"+JustitiaLawText.Line(justitiaDevelopmentVoice));
        }
        private void ClearJustitiaHint()
        {
            if(!_justHintVisible)return;
            FindFirstObjectByType<RaidLobbyUI>()?.Hint(null);_justHintVisible=false;
        }
        private void UpdateCampanellaCloak()
        {
            if(StageManager.Instance?.currentStage!=3 || !Walking || _campCloak==null || LocalGameplayInput.Blocked)
            {ClearCampanellaHint();return;}
            var local=StageManager.Instance.player;
            if(local==null || !CombatRange.LivingPartyMember(local.transform) ||
                Vector3.Distance(local.transform.position,_campCloak.transform.position)>3)
            {ClearCampanellaHint();return;}
            var ui=FindFirstObjectByType<RaidLobbyUI>();
            if(!_campInspected){ui?.Hint(LocalInputBindings.Label("Interact")+" · 종에 감겨 있던 원정대 망토 조사");_campHintVisible=true;}
            if(_campInspected || !LocalInputBindings.Pressed("Interact"))return;
            _campInspected=true;LocalInputBindings.QuarantineHeld();ClearCampanellaHint();
            ui?.Say("종이 울리지 못하도록 종치에 겹겹이 감아둔 푸른 원정대 망토들.\n"+
                CampanellaCloakText.Line(campanellaDevelopmentVoice));
        }
        private void ClearCampanellaHint()
        {
            if(!_campHintVisible)return;
            FindFirstObjectByType<RaidLobbyUI>()?.Hint(null);_campHintVisible=false;
        }
        public void PlayFloraIrisThrow(string trigger)
        {
            if(_floraIrisAnimator==null || string.IsNullOrEmpty(trigger))return;
            foreach(var parameter in _floraIrisAnimator.parameters)
                if(parameter.type==AnimatorControllerParameterType.Trigger && parameter.name==trigger)
                { _floraIrisAnimator.SetTrigger(trigger); return; }
        }
        private static GameObject Decor(string label,PrimitiveType primitive,Vector3 at,Vector3 scale,Color color)
        {
            var go=RaidPrimitive.Create(primitive);go.name=label;go.transform.position=at;go.transform.localScale=scale;
            go.GetComponent<Collider>().enabled=false;
            var block=new MaterialPropertyBlock();block.SetColor("_BaseColor",color);block.SetColor("_Color",color);
            go.GetComponent<Renderer>().SetPropertyBlock(block);return go;
        }
        private void UpdateFloraMemorial()
        {
            if(StageManager.Instance?.currentStage!=2 || !Walking || _floraFlower==null || LocalGameplayInput.Blocked)
            { ClearFloraHint(); return; }
            var local=StageManager.Instance.player;
            if(local==null || !CombatRange.LivingPartyMember(local.transform)) { ClearFloraHint(); return; }
            if(Vector3.Distance(local.transform.position,_floraFlower.transform.position)>3) { ClearFloraHint(); return; }
            var ui=FindFirstObjectByType<RaidLobbyUI>();
            if(!_floraInspected) { ui?.Hint(LocalInputBindings.Label("Interact")+" · 시들지 않는 푸른 헌화 조사"); _floraHintVisible=true; }
            if(_floraInspected || !LocalInputBindings.Pressed("Interact"))return;
            _floraInspected=true;LocalInputBindings.QuarantineHeld();
            ClearFloraHint();
            ui?.Say("먼저 잠든 네 명의 방패를 추모하며\n"+FloraMemorialText.Line(floraDevelopmentVoice));
        }
        private void ClearFloraHint()
        {
            if(!_floraHintVisible)return;
            FindFirstObjectByType<RaidLobbyUI>()?.Hint(null);
            _floraHintVisible=false;
        }
        private GameObject Block(string name, Vector3 pos, Vector3 size, Color color, bool solid)
        {
            var go = RaidPrimitive.Create(PrimitiveType.Cube);
            go.name = name; go.transform.SetParent(_root.transform, false);
            go.transform.localPosition = pos; go.transform.localScale = size;
            if (!solid) go.GetComponent<Collider>().enabled = false;
            var block = new MaterialPropertyBlock(); block.SetColor("_BaseColor", color); block.SetColor("_Color", color);
            go.GetComponent<Renderer>().SetPropertyBlock(block);
            return go;
        }
        private void ClearSceneryResources()
        {
            // Only these resources are created here; authored prefab assets stay shared.
            if (_floraGlassMaterial != null) Destroy(_floraGlassMaterial);
            if (_janusFloorMaterial != null) Destroy(_janusFloorMaterial);
            if (_janusFloorMesh != null) Destroy(_janusFloorMesh);
            _floraGlassMaterial = null; _janusFloorMaterial = null; _janusFloorMesh = null;
        }
        private void OnDestroy()
        {
            QuarterViewCamera.LocalCamera?.ClearArrivalCamera();
            RestoreMovementBounds();
            if (_root != null) Destroy(_root);
            ClearSceneryResources();
            if (Instance == this) Instance = null;
        }
    }
}
