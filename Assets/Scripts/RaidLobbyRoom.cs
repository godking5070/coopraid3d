using UnityEngine;

namespace RaidRogue
{
    // A local presentation of the existing single/Steam/LAN lobby. No run data lives here.
    public sealed class RaidLobbyRoom : MonoBehaviour
    {
        public static RaidLobbyRoom Instance { get; private set; }
        public static bool Active => Instance != null && Instance._showing && Instance._session != null &&
            Instance._session.Mode != RaidSessionMode.Menu && !CharacterTrainingSession.Active;
        public static bool HostAtDoor => Instance != null && Instance._showing &&
            Instance._player != null && Vector3.Distance(Instance._player.transform.position, Instance._layout.doorPosition) < Instance._layout.doorRange;

        private StageManager _stage;
        private RaidNetworkSession _session;
        private PlayerController _player;
        private GameObject _room, _arena;
        private Renderer[] _bossRenderers;
        private Collider[] _bossColliders;
        private bool[] _rendererEnabled, _colliderEnabled;
        private bool _showing;
        private RaidLobbyLayout _layout;
        private bool _ownsLayout;
        private RaidLobbyUI _ui;
        private int _shownHint = -1;
        private int _shownBindingRevision = -1;

        private void Awake() { Instance = this; }
        private void Start()
        {
            _stage = GetComponent<StageManager>();
            _session = GetComponent<RaidNetworkSession>();
            _layout = Resources.Load<RaidLobbyLayout>("UI/RaidLobbyLayout");
            if (_layout == null) { _layout = ScriptableObject.CreateInstance<RaidLobbyLayout>(); _ownsLayout = true; }
            _player = _stage != null ? _stage.player : null;
            _arena = GameObject.Find("Battlefield_Arena");
            BuildRoom();
            var root = FindFirstObjectByType<RaidUIRoot>();
            if (root != null)
                _ui = root.GetComponentInChildren<RaidLobbyUI>(true) ?? RaidLobbyUI.CreateDefault(root.transform);
            ShowRoom(true);
        }

        private void Update()
        {
            if (_session == null || _player == null) return;
            bool lobby = _session.Mode == RaidSessionMode.Menu ||
                (_session.Mode == RaidSessionMode.Single && _stage.Progress.Phase == RaidBattlePhase.Lobby) ||
                (RaidNetworkSession.Multiplayer && _session.BattlePhase == RaidBattlePhase.Lobby);
            ShowRoom(lobby && !_session.Ending && !CharacterTrainingSession.Active);
            if (!_showing || _session.Mode == RaidSessionMode.Menu || _session.MenuOpen || LocalGameplayInput.Blocked)
            { ShowHint(-1); return; }

            Vector3 here = _player.transform.position;
            int nearby = Vector3.Distance(here, _layout.doorPosition) < _layout.doorRange ? 0 :
                Vector3.Distance(here, _layout.guidePosition) < _layout.npcRange ? 1 :
                Vector3.Distance(here, _layout.irisPosition) < _layout.npcRange ? 2 : -1;
            ShowHint(nearby);
            if (LocalInputBindings.Pressed("Interact") && nearby >= 0)
            {
                LocalInputBindings.QuarantineHeld();
                if (nearby == 0) UseDoor();
                else if (nearby == 1) TalkToGuide();
                else TalkToIris();
            }
        }

        public void HideForDeparture() { ShowRoom(false); }
        public void ReturnFromRaid()
        {
            if (_player != null) _player.transform.position = _layout.partyReturnPosition;
            if (_session.Mode == RaidSessionMode.Single)
            {
                int offset = 0;
                foreach (var member in PartyLife.OrderedMembers())
                    if (member != _player?.transform)
                        member.position = _layout.partyReturnPosition + new Vector3(++offset * 2.5f, 0, 0);
            }
            foreach (var avatar in _session.Avatars.Values)
            {
                if (avatar.IsLocal) continue;
                avatar.transform.position = _layout.partyReturnPosition + new Vector3((avatar.Role - 2.5f) * 2.5f, 0, 0);
            }
            ShowRoom(true);
            TargetingManager.Instance?.ClearTarget();
            QuarterViewCamera.LocalCamera?.ReleaseOrbit();
        }

        private void UseDoor()
        {
            if (_session.Mode == RaidSessionMode.Single)
            {
                if (CampaignRuntime.Instance?.Resume != null)
                { _session.ContinueSavedSingleFromDoor(); return; }
                if (_stage.Progress.Stage > 0) { _stage.BeginSingleNextStage(); return; }
                if (CharacterActor.Ensure(_player.gameObject).DepartureReady) _stage.BeginSingleRun();
                else { Say("출발 전에 직업과 기원 루트를 선택하세요."); RoleSelectionManager.Instance?.OpenRoleSelectionModal(); }
                return;
            }
            if (!_session.HasLocalAvatar || !_session.ControlsReady) { Say("파티 접속을 기다리고 있습니다."); return; }
            if (_session.IsHostAuthority && _session.CanStartLobby) _session.RequestAction(9);
            else { _session.MenuOpen = true; Say("역할과 기원을 선택하고 준비를 완료하세요. 호스트가 문에서 출발합니다."); }
        }

        private void TalkToGuide()
        {
            if (_session.Mode == RaidSessionMode.Single) RoleSelectionManager.Instance?.OpenRoleSelectionModal();
            else if (_session.CanEditLobbyBuild) AegisRouteSelectionUI.OpenNetwork();
            else _session.MenuOpen = true;
            Say("길잡이: 이곳에서 직업과 기원을 정한 뒤 북쪽 문으로 출발하세요.");
        }
        private void TalkToIris()
        {
            if (CampaignRuntime.Instance?.TrainingUnlocked != true)
            { Say("이리스: 훈련소는 정규 전투에 한 번 참가하면 열립니다."); return; }
            if (_session.Mode == RaidSessionMode.Single && _stage.Progress.Phase == RaidBattlePhase.Lobby)
            {
                _session.EnterTrainingFromLobby();
                return;
            }
            Say("이리스: 파티 로비에서는 훈련소로 이동할 수 없습니다. 메뉴에서 훈련소를 선택하세요.");
        }

        private void ShowHint(int nearby)
        {
            if (_ui == null || (_shownHint == nearby && _shownBindingRevision == LocalInputBindings.Revision)) return;
            _shownHint = nearby;
            _shownBindingRevision = LocalInputBindings.Revision;
            _ui.Hint(nearby < 0 ? null : LocalInputBindings.Label("Interact") + " · " +
                (nearby == 0 ? "북쪽 문 · 레이드 출발" : nearby == 1 ? _layout.guideName + " · 직업/기원" : _layout.irisName + " · 훈련소"));
        }

        private void Say(string message)
        {
            _ui?.Say(message);
        }

        private void ShowRoom(bool show)
        {
            if (_room == null || _showing == show) return;
            _showing = show;
            _room.SetActive(show);
            if (_arena != null) _arena.SetActive(!show);
            if (_bossRenderers != null)
                for (int i = 0; i < _bossRenderers.Length; i++)
                    if (_bossRenderers[i] != null) _bossRenderers[i].enabled = !show && _rendererEnabled[i];
            if (_bossColliders != null)
                for (int i = 0; i < _bossColliders.Length; i++)
                    if (_bossColliders[i] != null) _bossColliders[i].enabled = !show && _colliderEnabled[i];
            if (!show) { ShowHint(-1); _ui?.Hide(); }
            else TargetingManager.Instance?.ClearTarget();
        }

        private void BuildRoom()
        {
            _room = new GameObject("Waiting Room · runtime");
            Block("Garden floor", new Vector3(0, -.16f, 0), new Vector3(32, .3f, 32), new Color(.16f, .26f, .22f));
            Block("South wall", new Vector3(0, 1.8f, -16), new Vector3(32, 3.6f, .6f), new Color(.33f, .35f, .31f));
            Block("West wall", new Vector3(-16, 1.8f, 0), new Vector3(.6f, 3.6f, 32), new Color(.33f, .35f, .31f));
            Block("East wall", new Vector3(16, 1.8f, 0), new Vector3(.6f, 3.6f, 32), new Color(.33f, .35f, .31f));
            Block("North wall left", new Vector3(-9.3f, 1.8f, 16), new Vector3(13.4f, 3.6f, .6f), new Color(.33f, .35f, .31f));
            Block("North wall right", new Vector3(9.3f, 1.8f, 16), new Vector3(13.4f, 3.6f, .6f), new Color(.33f, .35f, .31f));
            Block("Open north doorway lintel", new Vector3(0, 3.25f, 16), new Vector3(5.2f, .7f, .7f), new Color(.64f, .51f, .29f));
            Block("Departure threshold", new Vector3(_layout.doorPosition.x, .04f, _layout.doorPosition.z), new Vector3(5, .08f, 1.5f), new Color(.52f, .76f, .48f), false);
            Landmark("North departure door", _layout.doorPosition, _layout.doorPrefab, new Color(.52f, .76f, .48f));
            Landmark(_layout.guideName, _layout.guidePosition, _layout.guidePrefab, new Color(.31f, .77f, .54f));
            Landmark(_layout.irisName, _layout.irisPosition, _layout.irisPrefab, new Color(.9f, .72f, .36f));
            var boss = _stage != null ? _stage.boss : null;
            if (boss == null) return;
            _bossRenderers = boss.GetComponentsInChildren<Renderer>(true);
            _rendererEnabled = System.Array.ConvertAll(_bossRenderers, r => r.enabled);
            _bossColliders = boss.GetComponentsInChildren<Collider>(true);
            _colliderEnabled = System.Array.ConvertAll(_bossColliders, c => c.enabled);
        }

        private void Block(string name, Vector3 position, Vector3 scale, Color color, bool solid = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name; go.transform.SetParent(_room.transform, false);
            go.transform.localPosition = position; go.transform.localScale = scale;
            if (!solid) go.GetComponent<Collider>().enabled = false;
            Paint(go.GetComponent<Renderer>(), color);
        }

        private void Landmark(string name, Vector3 position, GameObject prefab, Color color)
        {
            if (prefab == null && name == "North departure door") return;
            var go = prefab != null ? Instantiate(prefab) : GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = name; go.transform.SetParent(_room.transform, false);
            go.transform.localPosition = position;
            if (prefab == null) Paint(go.GetComponent<Renderer>(), color);
        }

        private static void Paint(Renderer renderer, Color color)
        {
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", color); block.SetColor("_Color", color);
            renderer.SetPropertyBlock(block);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_room != null) Destroy(_room);
            if (_ownsLayout && _layout != null) Destroy(_layout);
        }
    }
}
