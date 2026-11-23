using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Astella.TwoD
{
    // A separate XY/Physics2D game. No 3D raid scene, saved campaign or network owner is modified.
    public sealed class Astella2DGame : MonoBehaviour
    {
        public static Astella2DGame Instance { get; private set; }
        public bool Running { get; private set; }
        public bool Finished { get; private set; }
        public bool Won { get; private set; }
        public int SelectedCharacter { get; private set; }
        public int Encounter => _encounter;
        public int DamageEvents { get; private set; }
        public int Attacks { get; private set; }
        public int DodgeCount { get; private set; }
        public int RevivesLeft => _revivesLeft;
        public int RevivesUsed => _revivesUsed;
        public bool Downed => Running && _player != null && _player.Hp <= 0;
        public bool RewardOffered => _outcome != null && _outcome.RewardVisible;
        public bool ResultShown => _outcome != null && _outcome.ResultVisible;
        public string ResultTitle => _outcome != null ? _outcome.ResultTitle : "";
        public string OfferedBlessing(int index) => _offer != null && index >= 0 && index < _offer.Length ? _offer[index].Name : "";
        public float PlayerGuard => _player != null && _player.GuardTime > 0 ? _player.Guard : 0;
        public float Ultimate => _ultimate;
        public float CooldownOf(int slot) => _cooldowns[slot];
        public int ActiveTelegraphs => _hazards.Count;
        public float PlayerHealth => _player != null ? _player.Hp : 0;
        public float PlayerMaxHealth => _player != null ? _player.MaxHp : 0;
        public int LivingAllies { get { int n = 0; for (int i = 1; i < _party.Count; i++) if (_party[i].Hp > 0) n++; return n; } }
        public float BossHealth => _bossHp;
        public float BossMaxHealth => _bossMax;
        public float DamageDealt => _damageDealt;
        public Vector2 PlayerPosition => _player != null ? _player.Body.position : Vector2.zero;
        public Vector2 BossPosition => new Vector2(0, 1.1f);
        public RoleKit PlayerKit => Astella2DKits.For(Astella2DRoster.All[SelectedCharacter].Role);
        public float CombatRange => PlayerKit.Basic.Range;
        public Astella2DPrismView prismHudPrefab;

        sealed class Fighter
        {
            public int Character;
            public RoleKit Kit;
            public float Hp, MaxHp, HitTimer, AttackTimer, HealTimer, Guard, GuardTime;
            public Rigidbody2D Body;
            public SpriteRenderer Art;
            public Transform Visual;
            public Mesh Marker;
            public int MarkerState = -1;
            public Vector2 Facing = Vector2.down, Target;
        }
        enum Shape { Circle, Donut, Line, Cone }
        sealed class Hazard
        {
            public Shape Shape;
            public Vector2 Center, Direction;
            public float Radius, Inner, Width, Time, Duration;
            public string Name;
            public GameObject View;
            public Transform Progress;
            public Mesh Fill, Edge, ProgressMesh;
        }
        sealed class Effect
        {
            public GameObject View;
            public float Life, Total, Grow;
            public Vector3 From, To;
            public bool Flying;
            public Mesh Mesh;
        }
        sealed class Blast
        {
            public Vector2 Position;
            public float Time, Total, Damage, Radius;
            public GameObject View;
            public Mesh Fill, Edge;
        }
        sealed class Scheduled
        {
            public float Time;
            public Action Run;
        }

        static readonly Color Ink = new Color(.045f, .055f, .11f, .93f);
        static readonly Color Silver = new Color(.89f, .91f, .98f);
        static readonly Color Violet = new Color(.66f, .43f, .96f);
        static readonly Color HazardFill = new Color(.96f, .21f, .36f, .16f);
        static readonly Color HazardEdge = new Color(1f, .42f, .52f, .9f);
        static readonly Color HazardProgress = new Color(.98f, .18f, .30f, .34f);

        readonly List<Fighter> _party = new List<Fighter>();
        readonly List<Hazard> _hazards = new List<Hazard>();
        readonly List<Effect> _effects = new List<Effect>();
        readonly List<Blast> _blasts = new List<Blast>();
        readonly List<Scheduled> _scheduled = new List<Scheduled>();
        readonly float[] _cooldowns = new float[5];
        readonly float[] _durations = { .5f, 5f, 12f, 1f, Astella2DKits.DashCooldown };
        readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        readonly List<Mesh> _battleMeshes = new List<Mesh>();
        readonly List<GameObject> _battleObjects = new List<GameObject>();
        RunModifiers _mods = new RunModifiers();
        System.Random _random = new System.Random();
        Fighter _player;
        Camera _camera;
        Material _shapeMaterial;
        Transform _world, _aimMarker;
        SpriteRenderer _bossArt;
        Sprite _bossSprite;
        Font _font, _titleFont;
        GameObject _menu, _battleHud, _pause, _help;
        Text _selectionText, _kitText, _noticeText, _statusText;
        Astella2DPrismView _prism;
        Astella2DPrismOutcome _outcome;
        Astella2DFeedback _feedback;
        Blessing[] _offer;
        int _selectedParty;
        readonly List<Button> _characterButtons = new List<Button>();
        Image _selectedPortrait;
        Vector2 _move, _aim = Vector2.right, _dashDirection;
        float _bossHp, _bossMax, _patternTimer, _dashTime, _hurtTime, _invulnerable, _downTime, _ultimate, _elapsed, _runTime, _noticeTime;
        float _empower, _empowerTime, _bossScale, _bossPunch, _damageDealt, _damageTaken;
        int _encounter, _castIndex, _revivesLeft, _revivesUsed, _hitsTaken;
        BossPattern _lastPattern;
        bool _automation, _autopilot, _paused, _enraged;
        string _notice = "";

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            Application.runInBackground = true;
            _font = Resources.Load<Font>("Astella2D/Fonts/Pretendard-Medium");
            _titleFont = Resources.Load<Font>("Astella2D/Fonts/MaruBuri-SemiBold");
            if (_font == null || _titleFont == null) throw new InvalidOperationException("Build the Astella 2D scene with Tools/Astella/2D first: required fonts are missing.");
            var shapeShader = Resources.Load<Shader>("Astella2D/ArenaShape");
            if (shapeShader == null) throw new InvalidOperationException("Astella 2D arena shape shader is missing.");
            _shapeMaterial = new Material(shapeShader);
            _owned.Add(_shapeMaterial);
            _world = new GameObject("2D World").transform; _world.SetParent(transform, false);
            CreateWorld(); CreateHud(); SelectCharacter(0); ReturnToMenu();
        }

        void Start()
        {
            string smoke = CommandLine("--astella2d-smoke=");
            if (smoke != null) StartCoroutine(SmokeRun(smoke));
        }

        void CreateWorld()
        {
            var go = new GameObject("2D Orthographic Camera", typeof(Camera)); go.transform.SetParent(transform, false);
            _camera = go.GetComponent<Camera>(); go.tag = "MainCamera";
            go.transform.position = new Vector3(0, .3f, -20);
            _camera.orthographic = true; _camera.orthographicSize = 7.15f;
            _camera.clearFlags = CameraClearFlags.SolidColor; _camera.backgroundColor = new Color(.035f, .04f, .09f);
            go.AddComponent<AudioListener>();
            var tex = Resources.Load<Texture2D>("Astella2D/arena-sanctuary");
            if (tex == null) throw new InvalidOperationException("Astella 2D arena artwork is missing.");
            var bg = NewSprite(tex, new Vector2(.5f, .5f), 100); _owned.Add(bg);
            var art = SpriteObject("Sanctuary Floor", _world, bg, -200);
            art.transform.localScale = Vector3.one * (25.4f / bg.bounds.size.x);
            var bossTex = Resources.Load<Texture2D>("Astella2D/crystal-warden");
            if (bossTex == null) throw new InvalidOperationException("Astella 2D boss artwork is missing.");
            _bossSprite = NewSprite(bossTex, new Vector2(.5f, .12f), 256); _owned.Add(_bossSprite);
        }

        // ---- Public control surface (input, automation and checks) ----

        public void SetAutomation(bool value) { _automation = value; _move = Vector2.zero; }
        /// <summary>Lets the built-in party AI drive the player too. Used by demo and smoke runs.</summary>
        public void SetAutopilot(bool value) { _autopilot = value; _automation = value || _automation; _move = Vector2.zero; }
        public void RequestMove(Vector2 direction) { _move = Vector2.ClampMagnitude(direction, 1); }
        public void AimAt(Vector2 target) { if ((target - PlayerPosition).sqrMagnitude > .001f) _aim = (target - PlayerPosition).normalized; }
        public void DebugSetBossHealth(float value) { if (Running) _bossHp = Mathf.Clamp(value, 1, _bossMax); }
        public void DebugChargeUltimate() { _ultimate = Astella2DKits.UltimateCost; }
        public void DebugTeleport(Vector2 point)
        {
            if (_player == null) return;
            var p = Astella2DCombatRules.ClampToEllipse(point.x, point.y, 9.1f, 3.65f);
            _player.Body.position = new Vector2(p.X, p.Y);
            _player.Body.transform.position = new Vector3(p.X, p.Y, 0);
        }
        public void ChooseBlessing(int index) => ChooseReward(index);

        public void BeginRun(int index = 0)
        {
            SelectedCharacter = Mathf.Clamp(index, 0, Astella2DRoster.All.Count - 1);
            _mods = new RunModifiers();
            _random = new System.Random(Environment.TickCount);
            _encounter = 0; _runTime = 0;
            DamageEvents = Attacks = DodgeCount = 0;
            _damageDealt = _damageTaken = 0; _revivesUsed = _hitsTaken = 0;
            SelectCharacter(SelectedCharacter);
            StartEncounter();
        }

        void StartEncounter()
        {
            ClearBattle();
            var spec = Astella2DEncounters.Get(_encounter);
            Finished = Won = _paused = _enraged = false; Running = true; _move = Vector2.zero;
            _dashTime = _hurtTime = _invulnerable = _downTime = _empowerTime = _elapsed = 0; _ultimate = 25;
            _patternTimer = 3; _castIndex = 0; _lastPattern = BossPattern.TargetCircle;
            _revivesLeft = Astella2DBlessings.StartingRevives + _mods.ExtraRevives;
            Array.Clear(_cooldowns, 0, _cooldowns.Length);
            var kit = PlayerKit;
            for (int i = 0; i < 4; i++) _durations[i] = Mathf.Max(.1f, kit.Skill(i).Cooldown);
            _durations[4] = Astella2DKits.DashCooldown * _mods.DashCooldown;
            _bossMax = spec.BossHp; _bossHp = _bossMax;

            _bossArt = SpriteObject(spec.Name, _world, _bossSprite, SortOrder(BossPosition.y));
            _bossArt.transform.position = BossPosition;
            _bossScale = 3.7f / _bossSprite.bounds.size.y;
            _bossArt.transform.localScale = Vector3.one * _bossScale;
            _bossArt.color = BossTint(spec);
            MakeShadow(_bossArt.transform, 1.35f / _bossScale, .38f / _bossScale);
            var body = new GameObject("Boss Body", typeof(Rigidbody2D), typeof(CircleCollider2D));
            body.transform.SetParent(_world, false); body.transform.position = BossPosition;
            body.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;
            body.GetComponent<CircleCollider2D>().radius = .95f;
            _battleObjects.Add(body);

            _player = Spawn(SelectedCharacter, new Vector2(0, -2.2f), true);
            Vector2[] allySpawns = { new Vector2(-2.8f, -1.5f), new Vector2(2.8f, -1.5f), new Vector2(0, -3.25f) };
            foreach (int candidate in PartyFor(SelectedCharacter))
                Spawn(candidate, allySpawns[_party.Count - 1], false);
            _selectedParty = 0;
            _prism.SetSkillNames(kit.Basic.Name, kit.Primary.Name, kit.Utility.Name, kit.Ultimate.Name);
            _menu.SetActive(false); _battleHud.SetActive(true); _outcome.Hide(); _pause.SetActive(false);
            Notice(spec.Title + " · " + spec.Name + " · 붉은 장판이 차오르면 피하세요", 4);
            RefreshHud();
        }

        // A balanced party around the chosen character: tank, healer and damage when possible.
        static IEnumerable<int> PartyFor(int selected)
        {
            var chosen = new List<int> { selected };
            PixelRole own = Astella2DRoster.All[selected].Role;
            PixelRole[] wanted = own == PixelRole.Tank ? new[] { PixelRole.Healer, PixelRole.Melee, PixelRole.Ranged }
                : own == PixelRole.Healer ? new[] { PixelRole.Tank, PixelRole.Melee, PixelRole.Caster }
                : new[] { PixelRole.Tank, PixelRole.Healer, own == PixelRole.Melee ? PixelRole.Caster : PixelRole.Melee };
            foreach (var role in wanted)
                for (int i = 0; i < Astella2DRoster.All.Count; i++)
                    if (!chosen.Contains(i) && Astella2DRoster.All[i].Role == role) { chosen.Add(i); break; }
            for (int i = 0; chosen.Count < 4 && i < Astella2DRoster.All.Count; i++) if (!chosen.Contains(i)) chosen.Add(i);
            chosen.RemoveAt(0);
            return chosen;
        }

        Fighter Spawn(int index, Vector2 point, bool player)
        {
            var definition = Astella2DRoster.All[index];
            var kit = Astella2DKits.For(definition.Role);
            float hp = kit.MaxHp * (player ? _mods.Health : 1);
            var go = new GameObject(definition.Name, typeof(Rigidbody2D), typeof(CircleCollider2D)); go.transform.SetParent(_world, false);
            go.transform.position = point;
            var body = go.GetComponent<Rigidbody2D>(); body.gravityScale = 0; body.freezeRotation = true;
            body.interpolation = RigidbodyInterpolation2D.Interpolate; body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.linearDamping = 20;
            go.GetComponent<CircleCollider2D>().radius = .22f;
            var visual = new GameObject("Pixel Visual").transform; visual.SetParent(go.transform, false);
            var art = SpriteObject("Eight Direction Sprite", visual, Astella2DRoster.Frame(index, Vector2.down), 30);
            var fighter = new Fighter { Character = index, Kit = kit, Body = body, Art = art, Visual = visual, Target = point, Hp = hp, MaxHp = hp, AttackTimer = .6f + _party.Count * .2f, HealTimer = 4 + _party.Count };
            _party.Add(fighter);
            MakeShadow(go.transform, .40f, .14f);
            // Ground ring: bright for the player, a thin role-colored ring for allies.
            var ring = new Hazard { Shape = Shape.Donut, Radius = player ? .5f : .42f, Inner = player ? .4f : .37f, Name = "Marker" };
            var marker = ShapeView(ring, player ? PlayerRing : RoleColor(kit.Role, .55f), 2, out fighter.Marker);
            _battleMeshes.Add(fighter.Marker);
            marker.transform.SetParent(go.transform, false); marker.transform.localPosition = Vector3.zero; marker.transform.localScale = new Vector3(1, .42f, 1);
            if (player)
            {
                var aim = AimTriangle(); _battleMeshes.Add(aim.GetComponent<MeshFilter>().sharedMesh);
                aim.transform.SetParent(go.transform, false); _aimMarker = aim.transform;
            }
            return fighter;
        }

        static readonly Color PlayerRing = new Color(.62f, .96f, 1f, .95f);
        static Color RoleColor(PixelRole role, float alpha)
        {
            Color c = role == PixelRole.Tank ? new Color(.55f, .7f, 1f) : role == PixelRole.Healer ? new Color(.45f, 1f, .7f)
                : role == PixelRole.Melee ? new Color(1f, .62f, .45f) : role == PixelRole.Ranged ? new Color(1f, .9f, .45f) : new Color(.85f, .6f, 1f);
            c.a = alpha; return c;
        }

        public void ReturnToMenu()
        {
            Running = Finished = Won = _paused = false; _move = Vector2.zero; _offer = null;
            ClearBattle();
            _menu.SetActive(true); _battleHud.SetActive(false); _outcome.Hide(); _pause.SetActive(false);
            SelectCharacter(SelectedCharacter);
        }

        void ClearBattle()
        {
            foreach (var fighter in _party) if (fighter.Body != null) { fighter.Body.simulated = false; fighter.Body.gameObject.SetActive(false); Destroy(fighter.Body.gameObject); }
            _party.Clear(); _player = null; _aimMarker = null;
            if (_bossArt != null) { _bossArt.gameObject.SetActive(false); Destroy(_bossArt.gameObject); }
            foreach (var hazard in _hazards) ReleaseHazard(hazard);
            _hazards.Clear();
            foreach (var blast in _blasts) ReleaseBlast(blast);
            _blasts.Clear();
            _scheduled.Clear();
            foreach (var effect in _effects) { Destroy(effect.View); Destroy(effect.Mesh); }
            _effects.Clear();
            foreach (var mesh in _battleMeshes) if (mesh != null) Destroy(mesh);
            _battleMeshes.Clear();
            foreach (var go in _battleObjects) if (go != null) Destroy(go);
            _battleObjects.Clear();
            _feedback?.Clear();
            _bossHp = 0;
        }

        // ---- Frame loop ----

        void Update()
        {
            if (!_automation) ReadInput();
            float dt = Time.deltaTime;
            if (Running && !_paused)
            {
                _elapsed += dt; _runTime += dt;
                if (_autopilot) Autopilot();
                TickPlayer(dt);
                if (!Running) { RefreshHud(); return; }
                for (int i = 0; i < _cooldowns.Length; i++) _cooldowns[i] = Mathf.Max(0, _cooldowns[i] - dt);
                if (_player.Hp > 0) _ultimate = Mathf.Min(Astella2DKits.UltimateCost, _ultimate + dt * Astella2DKits.UltimatePerSecond * _mods.UltimateGain);
                UpdateScheduled(dt);
                UpdateHazards(dt);
                if (Running) UpdateBlasts(dt);
                if (Running) UpdateBoss(dt);
                if (Running) UpdateParty(dt);
            }
            if (!_paused) { UpdateEffects(dt); _feedback.Tick(dt); }
            if (_noticeTime > 0) { _noticeTime -= dt; if (_noticeTime <= 0) _notice = ""; }
            RefreshHud();
        }

        void ReadInput()
        {
            var keys = Keyboard.current;
            if (keys == null) return;
            if (keys.escapeKey.wasPressedThisFrame && Running) SetPaused(!_paused);
            if (keys.f1Key.wasPressedThisFrame) _help.SetActive(!_help.activeSelf);
            if (!Running || _paused) return;
            if (!Application.isFocused) { _move = Vector2.zero; return; }
            _move = RaidRogue.LocalInputBindings.Movement;
            var mouse = Mouse.current;
            if (mouse != null)
            {
                Vector2 screen = mouse.position.ReadValue();
                Vector3 p = _camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 20)); AimAt(p);
                if (mouse.leftButton.isPressed && !(EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())) TryUseSkill(0, true);
            }
            if (keys.jKey.isPressed) TryUseSkill(0, true);
            if (keys.qKey.wasPressedThisFrame) TryUseSkill(1);
            if (keys.eKey.wasPressedThisFrame) TryUseSkill(2);
            if (keys.rKey.wasPressedThisFrame) TryUseSkill(3);
            if (RaidRogue.LocalInputBindings.Pressed("Dash") || keys.spaceKey.wasPressedThisFrame) TryUseSkill(4);
        }

        void SetPaused(bool value)
        {
            if (!Running) return;
            _paused = value; _pause.SetActive(value); _move = Vector2.zero;
            _feedback.Play("ui", .6f, 0);
        }

        void FixedUpdate()
        {
            if (!Running || _paused || _player == null)
            {
                foreach (var fighter in _party) if (fighter.Body != null) fighter.Body.linearVelocity = Vector2.zero;
                return;
            }
            float dt = Time.fixedDeltaTime;
            if (_player.Hp > 0)
            {
                var movement = _dashTime > 0 ? _dashDirection * Astella2DKits.DashSpeed : _move * Astella2DKits.MoveSpeed;
                if (_dashTime > 0) _dashTime = Mathf.Max(0, _dashTime - dt);
                var point = _player.Body.position + movement * dt;
                var clamped = Astella2DCombatRules.ClampToEllipse(point.x, point.y, 9.1f, 3.65f);
                _player.Body.MovePosition(new Vector2(clamped.X, clamped.Y));
                UpdateVisual(_player, movement.sqrMagnitude > .01f ? movement : _aim, movement.sqrMagnitude > .01f);
            }
            else { _player.Body.linearVelocity = Vector2.zero; UpdateVisual(_player, _player.Facing, false); }
            for (int i = 1; i < _party.Count; i++)
            {
                var fighter = _party[i];
                if (fighter.Hp <= 0) { fighter.Body.linearVelocity = Vector2.zero; continue; }
                Vector2 direction = fighter.Target - fighter.Body.position;
                fighter.Body.MovePosition(Vector2.MoveTowards(fighter.Body.position, fighter.Target, dt * 2.8f));
                UpdateVisual(fighter, direction.sqrMagnitude > .02f ? direction : BossPosition - fighter.Body.position, direction.sqrMagnitude > .02f);
            }
        }

        void UpdateVisual(Fighter fighter, Vector2 direction, bool moving)
        {
            if (direction.sqrMagnitude > .001f) fighter.Facing = direction.normalized;
            fighter.Art.sprite = Astella2DRoster.Frame(fighter.Character, fighter.Facing);
            fighter.Art.sortingOrder = SortOrder(fighter.Body.position.y);
            bool down = fighter.Hp <= 0;
            fighter.Visual.localPosition = new Vector3(0, down ? -.04f : moving ? Mathf.Abs(Mathf.Sin(Time.time * 14)) * .055f : Mathf.Sin(Time.time * 2.5f) * .008f, 0);
            fighter.Visual.localRotation = Quaternion.Euler(0, 0, down ? 80 : 0);
            bool isPlayer = fighter == _player;
            fighter.Art.color = down ? new Color(.43f, .41f, .56f, .55f)
                : fighter.HitTimer > 0 ? new Color(1, .5f, .55f)
                : isPlayer && _dashTime > 0 ? new Color(.55f, .85f, 1)
                : isPlayer && _invulnerable > 0 ? new Color(1, 1, 1, .55f + Mathf.PingPong(Time.time * 6, .45f))
                : isPlayer && _empowerTime > 0 ? new Color(1, .9f, .72f)
                : Color.white;
            int state = down ? 0 : fighter.GuardTime > 0 ? 2 : 1;
            if (state != fighter.MarkerState)
            {
                fighter.MarkerState = state;
                Color ring = state == 0 ? new Color(.5f, .45f, .6f, .25f) : state == 2 ? new Color(.45f, .8f, 1f, 1f) : isPlayer ? PlayerRing : RoleColor(fighter.Kit.Role, .55f);
                SetMeshColor(fighter.Marker, ring);
            }
            if (isPlayer && _aimMarker != null)
            {
                _aimMarker.gameObject.SetActive(!down);
                _aimMarker.localPosition = new Vector3(_aim.x * .66f, _aim.y * .66f * .45f, 0);
                _aimMarker.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(_aim.y, _aim.x) * Mathf.Rad2Deg - 90);
            }
        }

        static int SortOrder(float y) => 200 - Mathf.RoundToInt(y * 20);

        void TickPlayer(float dt)
        {
            _hurtTime = Mathf.Max(0, _hurtTime - dt);
            _invulnerable = Mathf.Max(0, _invulnerable - dt);
            _empowerTime = Mathf.Max(0, _empowerTime - dt);
            foreach (var fighter in _party) fighter.GuardTime = Mathf.Max(0, fighter.GuardTime - dt);
            if (_player.Hp > 0 || _downTime <= 0) return;
            _downTime -= dt;
            if (_downTime > 0) return;
            _player.Hp = _player.MaxHp * .5f; _invulnerable = 1.6f;
            Flash(PlayerPosition, .9f, new Color(.6f, 1, .85f, .8f), .45f);
            _feedback.Play("revive");
            Notice("다시 일어났습니다 · 잠시 무적", 1.6f);
        }

        // ---- Player skills ----

        public bool TryUseSkill(int slot) => TryUseSkill(slot, false);

        bool TryUseSkill(int slot, bool quiet)
        {
            if (!Running || _paused || _player == null || _player.Hp <= 0 || slot < 0 || slot >= 5 || _cooldowns[slot] > 0) return false;
            if (slot == 4)
            {
                _dashDirection = _move.sqrMagnitude > .01f ? _move.normalized : _aim;
                if (_dashDirection.sqrMagnitude < .01f) _dashDirection = Vector2.down;
                _dashTime = Astella2DKits.DashTime; _cooldowns[4] = _durations[4] = Astella2DKits.DashCooldown * _mods.DashCooldown;
                Flash(PlayerPosition, .45f, new Color(.48f, .80f, 1, .6f));
                _feedback.Play("dash", .8f);
                return true;
            }
            var spec = PlayerKit.Skill(slot);
            if (slot == 3 && _ultimate < Astella2DKits.UltimateCost) { if (!quiet) Notice("공명을 100% 모으면 궁극기를 쓸 수 있습니다", .9f); return false; }
            if (spec.NeedsRange && Vector2.Distance(PlayerPosition, BossPosition) > spec.Range)
            {
                if (!quiet || _noticeTime <= 0) Notice("사거리가 닿지 않습니다 · 수호자에게 더 가까이 이동하세요", .8f);
                return false;
            }
            float cooldown = slot == 3 ? 0 : spec.Cooldown * (slot == 0 ? 1 : _mods.Cooldown);
            _cooldowns[slot] = cooldown;
            if (cooldown > 0) _durations[slot] = cooldown;
            if (spec.Damage > 0) Attacks++;
            _ultimate = slot == 3 ? 0 : Mathf.Min(Astella2DKits.UltimateCost, _ultimate + _mods.UltimateGain *
                (slot == 0 ? Astella2DKits.UltimatePerBasic : slot == 1 ? Astella2DKits.UltimatePerPrimary : 0));
            ApplySkill(spec, slot);
            return true;
        }

        void ApplySkill(SkillSpec spec, int slot)
        {
            var kit = PlayerKit;
            if (spec.Damage > 0)
            {
                _aim = (BossPosition - PlayerPosition).normalized;
                float damage = spec.Damage * _mods.Damage * (_empowerTime > 0 ? 1 + _empower : 1);
                if (spec.Delay > 0)
                {
                    AddBlast(BossPosition + UnityEngine.Random.insideUnitCircle * .4f, spec.Delay, damage, slot == 3 ? 2.6f : 1.6f);
                    Projectile(PlayerPosition + Vector2.up * .7f, BossPosition + Vector2.up * .6f, Violet, .3f);
                }
                else
                {
                    if (kit.Ranged) Projectile(PlayerPosition + Vector2.up * .7f, BossPosition + Vector2.up * 1.1f, slot == 3 ? new Color(1, .9f, .6f) : slot == 1 ? new Color(.95f, .75f, 1) : Violet, slot == 0 ? .2f : .26f);
                    else Slash(slot);
                    HitBoss(damage, slot > 0, false);
                }
            }
            if (spec.PartyHeal > 0 || spec.Revive)
            {
                foreach (var fighter in _party)
                {
                    if (fighter.Hp <= 0)
                    {
                        if (!spec.Revive || fighter == _player) continue;
                        fighter.Hp = fighter.MaxHp * Astella2DKits.ReviveHealth; fighter.MarkerState = -1;
                        _feedback.Number(fighter.Body.position + Vector2.up * 1.4f, "소생", new Color(.6f, 1, .8f), 26);
                    }
                    else if (spec.PartyHeal > 0) Heal(fighter, fighter.MaxHp * spec.PartyHeal);
                    Flash(fighter.Body.position, .6f, new Color(.35f, .95f, .73f, .6f));
                }
                _feedback.Play("heal");
            }
            if (spec.SelfHeal > 0) Heal(_player, _player.MaxHp * spec.SelfHeal);
            if (spec.Guard > 0)
            {
                foreach (var fighter in _party)
                {
                    if (fighter.Hp <= 0 || (!spec.GuardParty && fighter != _player)) continue;
                    fighter.Guard = spec.Guard; fighter.GuardTime = spec.GuardTime; fighter.MarkerState = -1;
                    Flash(fighter.Body.position, .75f, new Color(.45f, .8f, 1f, .7f), .4f);
                }
                _feedback.Play("guard");
            }
            if (spec.Empower > 0) { _empower = spec.Empower; _empowerTime = spec.EmpowerTime; _feedback.Play("guard", .7f); }
            if (spec.Leap > 0)
            {
                _dashDirection = (PlayerPosition - BossPosition).normalized;
                if (_dashDirection.sqrMagnitude < .01f) _dashDirection = Vector2.down;
                _dashTime = spec.Leap / Astella2DKits.DashSpeed;
                _feedback.Play("dash");
            }
            if (slot == 3)
            {
                Flash(BossPosition + Vector2.up, 2.6f, new Color(.95f, .8f, 1, .42f), .5f);
                Flash(PlayerPosition, 1.2f, new Color(1, .9f, .6f, .5f), .45f);
                _feedback.Shake(.32f, .45f);
                _feedback.Play("ult");
            }
            if (slot >= 1) Notice(spec.Name, 1.2f);
        }

        void Heal(Fighter fighter, float amount)
        {
            if (fighter.Hp <= 0 || amount <= 0) return;
            float before = fighter.Hp;
            fighter.Hp = Mathf.Min(fighter.MaxHp, fighter.Hp + amount);
            float healed = fighter.Hp - before;
            if (healed >= 1) _feedback.Number(fighter.Body.position + Vector2.up * 1.3f, "+" + Mathf.RoundToInt(healed), new Color(.5f, 1, .7f), fighter == _player ? 28 : 22);
        }

        void HitBoss(float amount, bool heavy, bool ally)
        {
            if (!Running || amount <= 0) return;
            _bossHp = Mathf.Max(0, _bossHp - amount);
            DamageEvents++;
            if (!ally) _damageDealt += amount;
            if (_bossArt != null) _bossArt.color = new Color(1, .72f, .96f);
            _bossPunch = Mathf.Max(_bossPunch, heavy ? .06f : ally ? .01f : .025f);
            Vector2 at = BossPosition + new Vector2(0, 2.6f);
            if (ally) _feedback.Number(at, Mathf.RoundToInt(amount).ToString(CultureInfo.InvariantCulture), new Color(.72f, .7f, .9f, .9f), 20, .6f);
            else
            {
                _feedback.Number(at, Mathf.RoundToInt(amount).ToString("N0", CultureInfo.InvariantCulture), heavy ? new Color(1, .86f, .45f) : Color.white, heavy ? 42 : 30);
                _feedback.Play(heavy ? "heavy" : "hit", heavy ? 1 : .55f);
                if (heavy) _feedback.Shake(.12f, .18f);
            }
            if (_bossHp <= 0) EndEncounter(true);
        }

        public void ApplyPlayerDamage(float amount)
        {
            if (!Running || _player == null || _player.Hp <= 0 || amount <= 0 || float.IsNaN(amount) || float.IsInfinity(amount)) return;
            if (_dashTime > 0)
            {
                DodgeCount++; Notice("회피!", .7f);
                _feedback.Number(PlayerPosition + Vector2.up * 1.4f, "회피", new Color(.55f, .9f, 1), 28);
                return;
            }
            if (_hurtTime > 0 || _invulnerable > 0) return;
            float dealt = Astella2DDamage.Incoming(amount, PlayerGuard, _mods.DamageTaken);
            _player.Hp = Mathf.Max(0, _player.Hp - dealt); _player.HitTimer = .25f; _hurtTime = .48f; DamageEvents++;
            _damageTaken += dealt; _hitsTaken++;
            Flash(PlayerPosition, .55f, new Color(1, .30f, .43f, .65f));
            _feedback.Number(PlayerPosition + Vector2.up * 1.3f, "-" + Mathf.RoundToInt(dealt), new Color(1, .42f, .5f), 30);
            _feedback.Shake(.18f, .22f);
            _feedback.Play("hurt");
            if (_player.Hp <= 0) PlayerDown();
        }

        void PlayerDown()
        {
            _dashTime = 0; _move = Vector2.zero; _player.MarkerState = -1;
            if (_revivesLeft <= 0) { EndEncounter(false); return; }
            _revivesLeft--; _revivesUsed++; _downTime = 2.5f;
            _feedback.Play("down");
            Notice("쓰러졌습니다 · 2.5초 뒤 부활합니다 (남은 부활 " + _revivesLeft + "회)", 2.5f);
        }

        // ---- Party AI ----

        void UpdateParty(float dt)
        {
            for (int i = 0; i < _party.Count; i++)
            {
                var fighter = _party[i]; fighter.HitTimer = Mathf.Max(0, fighter.HitTimer - dt);
                if (i == 0 || fighter.Hp <= 0) { if (fighter.Hp <= 0 && i != 0) UpdateVisual(fighter, fighter.Facing, false); continue; }
                var kit = fighter.Kit;
                float angle = i * 2.1f + _elapsed * .035f;
                Vector2 target = BossPosition + new Vector2(Mathf.Cos(angle) * kit.AllyDistance, Mathf.Sin(angle) * kit.AllyDistance * .60f);
                if (InDanger(fighter.Body.position, .9f, .3f)) target = FindSafePoint(fighter.Body.position);
                var bounded = Astella2DCombatRules.ClampToEllipse(target.x, target.y, 8.7f, 3.4f); target = new Vector2(bounded.X, bounded.Y);
                fighter.Target = target;
                fighter.AttackTimer -= dt; fighter.HealTimer -= dt;
                if (fighter.AttackTimer <= 0)
                {
                    fighter.AttackTimer = kit.AllyInterval;
                    if (kit.Ranged) Projectile(fighter.Body.position + Vector2.up * .6f, BossPosition + Vector2.up, RoleColor(kit.Role, .9f), .24f);
                    else Flash(BossPosition + (fighter.Body.position - BossPosition).normalized * .9f + Vector2.up * .5f, .45f, RoleColor(kit.Role, .6f), .18f);
                    HitBoss(kit.AllyDamage * _mods.AllyDamage, false, true);
                    if (!Running) return;
                }
                if (kit.Role == PixelRole.Healer && fighter.HealTimer <= 0) AllyHeal(fighter);
                if (kit.Role == PixelRole.Tank && fighter.HealTimer <= 0)
                {
                    fighter.HealTimer = 16;
                    foreach (var ally in _party) if (ally.Hp > 0 && ally.GuardTime <= 0) { ally.Guard = .25f; ally.GuardTime = 3; ally.MarkerState = -1; }
                    Notice(Astella2DRoster.All[fighter.Character].Name + " · 수호의 함성 (3초간 피해 25% 감소)", 1.4f);
                }
            }
        }

        void AllyHeal(Fighter healer)
        {
            Fighter fallen = null, lowest = null;
            for (int i = 1; i < _party.Count; i++) if (_party[i].Hp <= 0) { fallen = _party[i]; break; }
            foreach (var member in _party) if (member.Hp > 0 && (lowest == null || member.Hp / member.MaxHp < lowest.Hp / lowest.MaxHp)) lowest = member;
            if (fallen != null)
            {
                healer.HealTimer = 10;
                fallen.Hp = fallen.MaxHp * Astella2DKits.ReviveHealth; fallen.MarkerState = -1;
                Flash(fallen.Body.position, .8f, new Color(.35f, .95f, .73f, .7f), .4f);
                _feedback.Number(fallen.Body.position + Vector2.up * 1.4f, "소생", new Color(.6f, 1, .8f), 24);
                return;
            }
            healer.HealTimer = 5.5f;
            if (lowest == null || lowest.Hp >= lowest.MaxHp * .97f) return;
            Heal(lowest, lowest.MaxHp * .22f);
            Flash(lowest.Body.position, .6f, new Color(.35f, .95f, .73f, .55f));
        }

        bool InDanger(Vector2 position, float horizon, float radius)
        {
            foreach (var hazard in _hazards) if (hazard.Time <= horizon && Contains(hazard, position, radius)) return true;
            return false;
        }

        Vector2 FindSafePoint(Vector2 origin)
        {
            // Search the actual union of imminent telegraphs; radial escape is wrong for donuts and beams.
            Vector2 best = origin; float bestDistance = float.PositiveInfinity;
            for (int step = 1; step <= 10; step++)
            {
                float distance = step * .5f;
                for (int angle = 0; angle < 24; angle++)
                {
                    Vector2 raw = origin + Rotate(Vector2.right, angle * 15) * distance;
                    var bounded = Astella2DCombatRules.ClampToEllipse(raw.x, raw.y, 8.7f, 3.4f);
                    var candidate = new Vector2(bounded.X, bounded.Y);
                    float cost = (candidate - origin).sqrMagnitude;
                    if (cost >= bestDistance || (candidate - BossPosition).sqrMagnitude < 1.3f) continue;
                    if (!InDanger(candidate, 1.4f, .34f)) { best = candidate; bestDistance = cost; }
                }
                if (!float.IsPositiveInfinity(bestDistance)) break;
            }
            return best;
        }

        // Drives the player with the same rules as the party AI. Demo and smoke runs only.
        void Autopilot()
        {
            if (_player == null || _player.Hp <= 0) { _move = Vector2.zero; return; }
            var kit = PlayerKit;
            Vector2 position = PlayerPosition;
            float angle = -Mathf.PI / 2 + Mathf.Sin(_elapsed * .2f) * .6f;
            float distance = Mathf.Min(kit.Basic.Range * .8f, kit.Ranged ? 5f : 2.2f);
            Vector2 target = BossPosition + new Vector2(Mathf.Cos(angle) * distance, Mathf.Sin(angle) * distance * .7f);
            bool danger = InDanger(position, 1.1f, .3f);
            if (danger) target = FindSafePoint(position);
            Vector2 delta = target - position;
            _move = delta.magnitude > .15f ? delta.normalized : Vector2.zero;
            _aim = (BossPosition - position).normalized;
            if (danger && InDanger(position, .2f, .25f) && _cooldowns[4] <= 0) TryUseSkill(4, true);
            if (danger) return;
            float range = Vector2.Distance(position, BossPosition);
            if (range <= kit.Basic.Range) TryUseSkill(0, true);
            if (range <= kit.Primary.Range || !kit.Primary.NeedsRange) TryUseSkill(1, true);
            bool partyHurt = false; foreach (var m in _party) if (m.Hp <= 0 || m.Hp < m.MaxHp * .6f) partyHurt = true;
            if (kit.Utility.PartyHeal > 0 ? partyHurt : kit.Utility.Leap > 0 ? range < 3 : true) TryUseSkill(2, true);
            if (_ultimate >= Astella2DKits.UltimateCost && range <= kit.Ultimate.Range) TryUseSkill(3, true);
        }

        // ---- Boss ----

        static Color BossTint(EncounterSpec spec) => new Color(spec.TintR, spec.TintG, spec.TintB);

        void UpdateBoss(float dt)
        {
            var spec = Astella2DEncounters.Get(_encounter);
            if (_bossArt != null)
            {
                Color rest = BossTint(spec);
                if (_hazards.Count > 0) rest = Color.Lerp(rest, new Color(1, .55f, .85f), .25f + Mathf.PingPong(Time.time * 3, .2f));
                _bossArt.color = Color.Lerp(_bossArt.color, rest, dt * 10);
                _bossPunch = Mathf.MoveTowards(_bossPunch, 0, dt * .35f);
                float breathe = 1 + Mathf.Sin(Time.time * 2) * .012f;
                _bossArt.transform.localScale = new Vector3(_bossScale * (breathe + _bossPunch), _bossScale * (breathe - _bossPunch * .6f), 1);
            }
            bool enraged = _bossHp < _bossMax * .5f;
            if (enraged && !_enraged)
            {
                _enraged = true;
                Notice(spec.Name + "가 격앙합니다 · 공격이 빨라집니다", 2.5f);
                _feedback.Shake(.25f, .6f); _feedback.Play("enrage");
            }
            _patternTimer -= dt;
            if (_patternTimer > 0) return;
            float cast = (enraged ? 1.15f : 1.55f) * spec.CastScale;
            var pattern = Astella2DEncounters.Next(spec, _castIndex++, _lastPattern, _random);
            _lastPattern = pattern;
            float extra = CastPattern(pattern, cast);
            _patternTimer = (enraged ? spec.EnragedInterval : spec.Interval) + extra;
            _feedback.Play("cast", .7f);
        }

        // Returns extra seconds the pattern occupies beyond its first cast.
        float CastPattern(BossPattern pattern, float cast)
        {
            Vector2 toPlayer = (PlayerPosition - BossPosition).normalized;
            switch (pattern)
            {
                case BossPattern.TargetCircle:
                    AddHazard(Shape.Circle, PlayerPosition, Vector2.up, 1.75f, 0, 0, cast, "낙성 · 표적 원형");
                    return 0;
                case BossPattern.Donut:
                    AddHazard(Shape.Donut, BossPosition, Vector2.up, 6.8f, 2.6f, 0, cast + .4f, "공명 파동 · 안쪽으로");
                    return 0;
                case BossPattern.TripleLine:
                    for (int i = -1; i <= 1; i++) AddHazard(Shape.Line, BossPosition, Rotate(toPlayer, i * 35), 9.4f, 0, .55f, cast, "분광 절단 · 직선");
                    return 0;
                case BossPattern.Cone:
                    AddHazard(Shape.Cone, BossPosition, toPlayer, 8, 0, 42, cast, "수정 숨결 · 측면으로");
                    return 0;
                case BossPattern.StarFall:
                    float offset = UnityEngine.Random.Range(0f, Mathf.PI * 2);
                    for (int i = 0; i < 5; i++)
                    {
                        float a = i * Mathf.PI * 2 / 5 + offset;
                        AddHazard(Shape.Circle, new Vector2(Mathf.Cos(a) * 5.5f, Mathf.Sin(a) * 2.5f), Vector2.up, 1.65f, 0, 0, cast + .2f, "별의 잔향 · 안전지대 찾기");
                    }
                    return 0;
                case BossPattern.ChaseCircles:
                    for (int i = 0; i < 3; i++)
                    {
                        float c = cast * .85f;
                        Schedule(i * .6f, () => AddHazard(Shape.Circle, PlayerPosition, Vector2.up, 1.45f, 0, 0, c, "추적 낙성 · 계속 움직이기"));
                    }
                    return 1.2f;
                case BossPattern.RotatingBeams:
                    float start = UnityEngine.Random.Range(0, 90f);
                    for (int i = 0; i < 4; i++) AddHazard(Shape.Line, BossPosition, Rotate(Vector2.right, start + i * 90), 10f, 0, .6f, cast, "회전 광선 · 틈으로");
                    Schedule(.75f, () => { for (int i = 0; i < 4; i++) AddHazard(Shape.Line, BossPosition, Rotate(Vector2.right, start + 45 + i * 90), 10f, 0, .6f, cast, "회전 광선 · 틈으로"); });
                    return .9f;
                case BossPattern.InOut:
                    AddHazard(Shape.Circle, BossPosition, Vector2.up, 3.4f, 0, 0, cast, "봉쇄 파동 · 밖으로, 그다음 안으로");
                    AddHazard(Shape.Donut, BossPosition, Vector2.up, 7.5f, 3.4f, 0, cast + 1.2f, "봉쇄 파동 · 안으로");
                    return 1.2f;
                default:
                    Vector2 p = PlayerPosition;
                    AddHazard(Shape.Line, p - Vector2.right * 12, Vector2.right, 24, 0, .6f, cast + .1f, "십자 절단 · 대각선으로");
                    AddHazard(Shape.Line, p - Vector2.up * 8, Vector2.up, 16, 0, .6f, cast + .1f, "십자 절단 · 대각선으로");
                    return 0;
            }
        }

        void Schedule(float delay, Action run)
        {
            if (delay <= 0) run(); else _scheduled.Add(new Scheduled { Time = delay, Run = run });
        }

        void UpdateScheduled(float dt)
        {
            for (int i = _scheduled.Count - 1; i >= 0; i--)
            {
                var item = _scheduled[i]; item.Time -= dt;
                if (item.Time > 0) continue;
                _scheduled.RemoveAt(i); item.Run();
                if (!Running) return;
            }
        }

        static Vector2 Rotate(Vector2 value, float degrees) { float a = degrees * Mathf.Deg2Rad; return new Vector2(value.x * Mathf.Cos(a) - value.y * Mathf.Sin(a), value.x * Mathf.Sin(a) + value.y * Mathf.Cos(a)); }

        void AddHazard(Shape shape, Vector2 center, Vector2 direction, float radius, float inner, float width, float cast, string name)
        {
            if (direction.sqrMagnitude < .001f) direction = Vector2.down;
            var hazard = new Hazard { Shape = shape, Center = center, Direction = direction.normalized, Radius = radius, Inner = inner, Width = width, Time = cast, Duration = cast, Name = name };
            hazard.View = new GameObject("Telegraph " + name); hazard.View.transform.SetParent(_world, false); hazard.View.transform.position = center;
            ShapeView(hazard, HazardFill, -40, out hazard.Fill).transform.SetParent(hazard.View.transform, true);
            var edge = OutlineView(hazard, HazardEdge, -38, .07f, out hazard.Edge); edge.transform.SetParent(hazard.View.transform, true);
            if (shape != Shape.Donut)
            {
                var progress = ShapeView(hazard, HazardProgress, -39, out hazard.ProgressMesh);
                progress.transform.SetParent(hazard.View.transform, true);
                hazard.Progress = progress.transform; hazard.Progress.localScale = Vector3.zero;
            }
            _hazards.Add(hazard);
        }

        bool Contains(Hazard h, Vector2 p, float actor)
        {
            switch (h.Shape)
            {
                case Shape.Circle: return Astella2DCombatRules.Circle(p.x, p.y, h.Center.x, h.Center.y, h.Radius, actor);
                case Shape.Donut: return Astella2DCombatRules.Donut(p.x, p.y, h.Center.x, h.Center.y, h.Inner, h.Radius, actor);
                case Shape.Line:
                    var end = h.Center + h.Direction * h.Radius;
                    return Astella2DCombatRules.Line(p.x, p.y, h.Center.x, h.Center.y, end.x, end.y, h.Width, actor);
                default: return Astella2DCombatRules.Cone(p.x, p.y, h.Center.x, h.Center.y, h.Direction.x, h.Direction.y, h.Radius, h.Width, actor);
            }
        }

        void UpdateHazards(float dt)
        {
            var spec = Astella2DEncounters.Get(_encounter);
            for (int i = _hazards.Count - 1; i >= 0; i--)
            {
                if (i >= _hazards.Count) continue;
                var hazard = _hazards[i]; hazard.Time -= dt;
                float progress = 1 - Mathf.Clamp01(hazard.Time / hazard.Duration);
                if (hazard.Progress != null) hazard.Progress.localScale = Vector3.one * progress;
                else SetMeshColor(hazard.Fill, new Color(.96f, .21f, .36f, Mathf.Lerp(.12f, .42f, progress)));
                bool flash = hazard.Time < .22f && Mathf.Repeat(Time.time * 18, 1) < .5f;
                SetMeshColor(hazard.Edge, flash ? new Color(1, .95f, .97f, 1) : HazardEdge);
                if (hazard.Time > 0) continue;
                if (Contains(hazard, PlayerPosition, .22f)) ApplyPlayerDamage(spec.HazardDamage);
                if (!Running) return; // EndEncounter clears all views and collections.
                for (int j = 1; j < _party.Count; j++)
                {
                    var fighter = _party[j];
                    if (fighter.Hp <= 0 || !Contains(hazard, fighter.Body.position, .22f)) continue;
                    float dealt = Astella2DDamage.Incoming(spec.HazardDamage * .85f, fighter.GuardTime > 0 ? fighter.Guard : 0, 1);
                    fighter.Hp = Mathf.Max(0, fighter.Hp - dealt); fighter.HitTimer = .3f;
                    if (fighter.Hp <= 0)
                    {
                        fighter.MarkerState = -1;
                        _feedback.Number(fighter.Body.position + Vector2.up * 1.3f, "쓰러짐", new Color(.8f, .7f, .9f), 22);
                    }
                }
                var view = ShapeView(hazard, new Color(1, .62f, .80f, .6f), -30, out var impactMesh);
                _effects.Add(new Effect { View = view, Mesh = impactMesh, Life = .2f, Total = .2f, Grow = .04f, From = view.transform.position, To = view.transform.position });
                _feedback.Shake(hazard.Radius > 5 ? .2f : .1f, .2f);
                _feedback.Play("impact", .55f);
                _bossPunch = Mathf.Max(_bossPunch, .03f);
                ReleaseHazard(hazard); _hazards.RemoveAt(i);
            }
        }

        void AddBlast(Vector2 position, float delay, float damage, float radius)
        {
            var shape = new Hazard { Shape = Shape.Circle, Radius = radius, Center = position, Name = "Friendly Blast" };
            var blast = new Blast { Position = position, Time = delay, Total = delay, Damage = damage, Radius = radius };
            blast.View = new GameObject("Friendly Blast"); blast.View.transform.SetParent(_world, false); blast.View.transform.position = position;
            ShapeView(shape, new Color(.62f, .45f, 1f, .2f), -35, out blast.Fill).transform.SetParent(blast.View.transform, true);
            OutlineView(shape, new Color(.8f, .65f, 1f, .9f), -34, .06f, out blast.Edge).transform.SetParent(blast.View.transform, true);
            _blasts.Add(blast);
        }

        void UpdateBlasts(float dt)
        {
            for (int i = _blasts.Count - 1; i >= 0; i--)
            {
                if (i >= _blasts.Count) continue;
                var blast = _blasts[i]; blast.Time -= dt;
                blast.View.transform.localScale = Vector3.one * (1 + Mathf.Sin(Time.time * 20) * .03f);
                if (blast.Time > 0) continue;
                _blasts.RemoveAt(i);
                Flash(blast.Position, blast.Radius, new Color(.85f, .6f, 1f, .8f), .35f);
                ReleaseBlast(blast);
                HitBoss(blast.Damage, true, false);
                _feedback.Shake(.2f, .25f);
                if (!Running) return;
            }
        }

        void ReleaseBlast(Blast blast)
        {
            if (blast.View != null) Destroy(blast.View);
            if (blast.Fill != null) Destroy(blast.Fill);
            if (blast.Edge != null) Destroy(blast.Edge);
        }

        // ---- Outcome ----

        void EndEncounter(bool won)
        {
            Running = false; Finished = true; Won = won; _move = Vector2.zero; _dashTime = 0;
            foreach (var fighter in _party) { fighter.Body.linearVelocity = Vector2.zero; fighter.Body.simulated = false; }
            foreach (var hazard in _hazards) ReleaseHazard(hazard); _hazards.Clear();
            foreach (var blast in _blasts) ReleaseBlast(blast); _blasts.Clear();
            _scheduled.Clear();
            _battleHud.SetActive(false);
            _feedback.Clear();
            _feedback.Play(won ? "win" : "lose", .9f, 0);
            var spec = Astella2DEncounters.Get(_encounter);
            if (won && _encounter < Astella2DEncounters.Count - 1)
            {
                _offer = Astella2DBlessings.Draw(_random, 3);
                _outcome.ShowReward(spec.Name + " 격파", "전투 시간 " + Clock(_elapsed) + " · 축복은 이번 원정이 끝날 때까지 유지됩니다.", _offer);
                return;
            }
            _offer = null;
            // The authored result box holds two lines: verdict, then the run summary.
            string summary = (won ? "세 번의 공명을 모두 잠재웠습니다." : spec.Title + "에서 쓰러졌습니다. 장판이 차오르는 순간을 읽으세요.") +
                "\n" + Clock(_runTime) + " · 준 피해 " + Mathf.RoundToInt(_damageDealt).ToString("N0", CultureInfo.InvariantCulture) +
                " · 피격 " + _hitsTaken + " · 회피 " + DodgeCount + " · 부활 " + _revivesUsed;
            _outcome.ShowResult(won, summary);
        }

        void ChooseReward(int reward)
        {
            if (!Finished || !Won || _offer == null || _encounter >= Astella2DEncounters.Count - 1 || reward < 0 || reward >= _offer.Length) return;
            var blessing = _offer[reward];
            _mods.Apply(blessing.Id);
            _offer = null;
            _feedback.Play("ui", .8f, 0);
            _encounter++; StartEncounter();
            Notice(Astella2DEncounters.Get(_encounter).Title + " · 축복 「" + blessing.Name + "」 적용", 3.5f);
        }

        static string Clock(float seconds)
        {
            int total = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return (total / 60) + ":" + (total % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        void Notice(string value, float duration) { _notice = value; _noticeTime = duration; }

        // ---- Meshes and effects ----

        static Sprite NewSprite(Texture2D texture, Vector2 pivot, float ppu) => Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), pivot, ppu, 0, SpriteMeshType.FullRect);
        SpriteRenderer SpriteObject(string name, Transform parent, Sprite sprite, int order)
        {
            var go = new GameObject(name, typeof(SpriteRenderer));
            go.transform.SetParent(parent, false);
            var art = go.GetComponent<SpriteRenderer>();
            art.sprite = sprite;
            art.sortingOrder = order;
            return art;
        }

        GameObject MeshObject(string name, Vector3 position, Mesh mesh, int order)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(_world, false); go.transform.position = position;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _shapeMaterial;
            renderer.sortingOrder = order;
            return go;
        }

        GameObject ShapeView(Hazard h, Color color, int order, out Mesh mesh)
        {
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            Action<Vector2, float, float, float, float> ring = (center, inner, outer, start, sweep) =>
            {
                int offset = vertices.Count, count = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(sweep) / 5));
                for (int i = 0; i <= count; i++)
                {
                    var dir = new Vector2(Mathf.Cos((start + sweep * i / count) * Mathf.Deg2Rad), Mathf.Sin((start + sweep * i / count) * Mathf.Deg2Rad));
                    vertices.Add(center + dir * inner); vertices.Add(center + dir * outer);
                    if (i == count) continue;
                    int n = offset + i * 2; triangles.AddRange(new[] { n, n + 1, n + 3, n, n + 3, n + 2 });
                }
            };
            if (h.Shape == Shape.Circle || h.Shape == Shape.Donut) ring(Vector2.zero, h.Inner, h.Radius, 0, 360);
            else if (h.Shape == Shape.Cone) ring(Vector2.zero, h.Inner, h.Radius, Mathf.Atan2(h.Direction.y, h.Direction.x) * Mathf.Rad2Deg - h.Width, h.Width * 2);
            else
            {
                var normal = new Vector2(-h.Direction.y, h.Direction.x) * h.Width; var end = h.Direction * h.Radius;
                vertices.AddRange(new Vector3[] { normal, -normal, end - normal, end + normal }); triangles.AddRange(new[] { 0, 1, 2, 0, 2, 3 });
                float angle = Mathf.Atan2(h.Direction.y, h.Direction.x) * Mathf.Rad2Deg;
                ring(Vector2.zero, 0, h.Width, angle + 90, 180); ring(end, 0, h.Width, angle - 90, 180);
            }
            mesh = new Mesh { name = "2D " + h.Shape }; mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); SetMeshColor(mesh, color); mesh.RecalculateBounds();
            return MeshObject("Shape " + h.Name, h.Center, mesh, order);
        }

        // A thin band along the shape's perimeter, so the exact edge of a telegraph is readable.
        GameObject OutlineView(Hazard h, Color color, int order, float thickness, out Mesh mesh)
        {
            var loops = new List<List<Vector2>>();
            Func<Vector2, float, float, float, int, List<Vector2>> arc = (center, radius, start, sweep, steps) =>
            {
                var points = new List<Vector2>();
                for (int i = 0; i <= steps; i++)
                {
                    float a = (start + sweep * i / steps) * Mathf.Deg2Rad;
                    points.Add(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
                }
                return points;
            };
            float heading = Mathf.Atan2(h.Direction.y, h.Direction.x) * Mathf.Rad2Deg;
            if (h.Shape == Shape.Circle || h.Shape == Shape.Donut)
            {
                var outer = arc(Vector2.zero, h.Radius, 0, 360, 72); outer.RemoveAt(outer.Count - 1); loops.Add(outer);
                if (h.Shape == Shape.Donut && h.Inner > .05f) { var inner = arc(Vector2.zero, h.Inner, 0, 360, 60); inner.RemoveAt(inner.Count - 1); loops.Add(inner); }
            }
            else if (h.Shape == Shape.Cone)
            {
                var points = new List<Vector2> { Vector2.zero };
                points.AddRange(arc(Vector2.zero, h.Radius, heading - h.Width, h.Width * 2, Mathf.Max(6, Mathf.CeilToInt(h.Width * 2 / 4))));
                loops.Add(points);
            }
            else
            {
                var end = h.Direction * h.Radius;
                var points = arc(Vector2.zero, h.Width, heading + 90, 180, 12);
                points.AddRange(arc(end, h.Width, heading - 90, 180, 12));
                loops.Add(points);
            }
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            foreach (var loop in loops)
            {
                int n = loop.Count, offset = vertices.Count;
                for (int i = 0; i < n; i++)
                {
                    Vector2 prev = loop[(i - 1 + n) % n], here = loop[i], next = loop[(i + 1) % n];
                    Vector2 a = (here - prev).normalized, b = (next - here).normalized;
                    Vector2 na = new Vector2(-a.y, a.x), nb = new Vector2(-b.y, b.x);
                    Vector2 normal = (na + nb).sqrMagnitude > .0001f ? (na + nb).normalized : nb;
                    float miter = thickness * .5f / Mathf.Max(.35f, Vector2.Dot(normal, nb));
                    vertices.Add(here + normal * miter); vertices.Add(here - normal * miter);
                    int j = offset + i * 2, k = offset + ((i + 1) % n) * 2;
                    triangles.AddRange(new[] { j, j + 1, k + 1, j, k + 1, k });
                }
            }
            mesh = new Mesh { name = "2D Outline " + h.Shape }; mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); SetMeshColor(mesh, color); mesh.RecalculateBounds();
            return MeshObject("Edge " + h.Name, h.Center, mesh, order);
        }

        GameObject AimTriangle()
        {
            var mesh = new Mesh { name = "2D Aim" };
            mesh.SetVertices(new List<Vector3> { new Vector3(0, .13f), new Vector3(-.1f, -.05f), new Vector3(.1f, -.05f) });
            mesh.SetTriangles(new[] { 0, 2, 1 }, 0); SetMeshColor(mesh, PlayerRing); mesh.RecalculateBounds();
            return MeshObject("Aim Marker", Vector3.zero, mesh, 3);
        }

        static void SetMeshColor(Mesh mesh, Color color)
        {
            if (mesh == null) return;
            var colors = new Color[mesh.vertexCount]; for (int i = 0; i < colors.Length; i++) colors[i] = color; mesh.colors = colors;
        }

        void ReleaseHazard(Hazard hazard)
        {
            if (hazard.View != null) Destroy(hazard.View);
            if (hazard.Fill != null) Destroy(hazard.Fill);
            if (hazard.Edge != null) Destroy(hazard.Edge);
            if (hazard.ProgressMesh != null) Destroy(hazard.ProgressMesh);
        }

        void MakeShadow(Transform parent, float radius, float height)
        {
            var h = new Hazard { Shape = Shape.Circle, Radius = radius, Name = "Shadow" };
            var go = ShapeView(h, new Color(.015f, .01f, .04f, .35f), 0, out var mesh); _battleMeshes.Add(mesh);
            go.transform.SetParent(parent, false); go.transform.localPosition = Vector3.zero; go.transform.localScale = new Vector3(1, height / radius, 1);
        }

        void Flash(Vector2 position, float radius, Color color, float life = .25f)
        {
            var h = new Hazard { Shape = Shape.Donut, Radius = radius, Inner = radius * .72f, Center = position, Name = "Resonance" };
            var go = ShapeView(h, color, 450, out var mesh);
            _effects.Add(new Effect { View = go, Mesh = mesh, Life = life, Total = life, Grow = .35f, From = position, To = position });
        }

        void Slash(int slot)
        {
            // A thin crescent on the boss body, never a filled floor shape that could read as a telegraph.
            Vector2 from = (PlayerPosition - BossPosition).normalized;
            float radius = slot == 0 ? 1.1f : 1.5f;
            var h = new Hazard { Shape = Shape.Cone, Radius = radius, Inner = radius * .8f, Width = slot == 0 ? 50 : 70, Direction = Rotate(from, UnityEngine.Random.Range(-25f, 25f)), Center = BossPosition + Vector2.up * 1.3f, Name = "Slash" };
            var go = ShapeView(h, slot == 0 ? new Color(.95f, .9f, 1f, .8f) : new Color(1f, .82f, .5f, .9f), 450, out var mesh);
            _effects.Add(new Effect { View = go, Mesh = mesh, Life = .16f, Total = .16f, Grow = .25f, From = go.transform.position, To = go.transform.position });
        }

        void Projectile(Vector2 from, Vector2 to, Color color, float life)
        {
            var h = new Hazard { Shape = Shape.Circle, Radius = .11f, Center = from, Name = "Bolt" };
            var go = ShapeView(h, color, 440, out var mesh);
            _effects.Add(new Effect { View = go, Mesh = mesh, Life = life, Total = life, From = from, To = to, Flying = true });
        }

        void UpdateEffects(float dt)
        {
            for (int i = _effects.Count - 1; i >= 0; i--)
            {
                var effect = _effects[i]; effect.Life -= dt;
                if (effect.Life <= 0) { Destroy(effect.View); Destroy(effect.Mesh); _effects.RemoveAt(i); continue; }
                float progress = 1 - effect.Life / effect.Total;
                if (effect.Flying) effect.View.transform.position = Vector3.Lerp(effect.From, effect.To, progress);
                else effect.View.transform.localScale = Vector3.one * (1 + progress * effect.Grow);
            }
        }

        // ---- UI ----

        RectTransform Panel(Transform parent, string name, Vector2 size, Vector2 anchor, Vector2 position, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform; rect.anchorMin = rect.anchorMax = anchor; rect.pivot = anchor; rect.sizeDelta = size; rect.anchoredPosition = position;
            var image = go.GetComponent<Image>(); image.color = color; image.raycastTarget = false; return rect;
        }
        Text Label(Transform parent, string text, Vector2 size, Vector2 anchor, Vector2 position, int fontSize = 23, bool title = false)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text)); go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform; rect.anchorMin = rect.anchorMax = anchor; rect.pivot = anchor; rect.sizeDelta = size; rect.anchoredPosition = position;
            var label = go.GetComponent<Text>(); label.font = title ? _titleFont : _font; label.fontSize = fontSize; label.text = text; label.color = Silver;
            label.alignment = TextAnchor.MiddleLeft; label.raycastTarget = false;
            var outline = go.AddComponent<Outline>(); outline.effectColor = new Color(.01f, .01f, .04f, .9f); outline.effectDistance = new Vector2(1.1f, -1.1f); return label;
        }
        Button Button(Transform parent, string text, Vector2 size, Vector2 anchor, Vector2 position, Action action, int fontSize = 23)
        {
            var rect = Panel(parent, text, size, anchor, position, new Color(.14f, .13f, .24f, .94f));
            var image = rect.GetComponent<Image>(); image.raycastTarget = true;
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            var colors = button.colors; colors.normalColor = Color.white; colors.highlightedColor = new Color(1.22f, 1.13f, 1.4f); colors.selectedColor = new Color(1.15f, 1.08f, 1.3f); button.colors = colors;
            button.onClick.AddListener(() => action());
            var label = Label(rect, text, size - new Vector2(24, 0), new Vector2(.5f, .5f), Vector2.zero, fontSize, true); label.alignment = TextAnchor.MiddleCenter;
            return button;
        }
        void CreateHud()
        {
            if (EventSystem.current == null) { var events = new GameObject("2D EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule)); events.transform.SetParent(transform, false); }
            var canvasGo = new GameObject("Astella 2D UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)); canvasGo.transform.SetParent(transform, false);
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
            var ui = (RectTransform)new GameObject("Reference Layout", typeof(RectTransform)).transform; ui.SetParent(canvasGo.transform, false); ui.anchorMin = ui.anchorMax = new Vector2(.5f, .5f); ui.sizeDelta = new Vector2(1920, 1080);
            _menu = Panel(ui, "Character Selection", new Vector2(1920, 1080), new Vector2(.5f, .5f), Vector2.zero, new Color(.025f, .03f, .075f, .79f)).gameObject;
            var topLeft = new Vector2(0, 1);
            Label(_menu.transform, "ASTELLA", new Vector2(900, 100), topLeft, new Vector2(125, -65), 73, true);
            Label(_menu.transform, "2D 공명 원정", new Vector2(600, 60), topLeft, new Vector2(130, -166), 30, true);
            Label(_menu.transform, "함께할 캐릭터를 선택하세요", new Vector2(700, 50), topLeft, new Vector2(133, -236), 24);
            for (int i = 0; i < Astella2DRoster.All.Count; i++)
            {
                int index = i; var character = Astella2DRoster.All[i];
                var button = Button(_menu.transform, "", new Vector2(220, 108), topLeft, new Vector2(132 + i % 4 * 234, -300 - i / 4 * 123), () => { SelectCharacter(index); _feedback.Play("ui", .6f, 0); });
                var art = Panel(button.transform, "Portrait", new Vector2(78, 90), topLeft, new Vector2(6, -9), Color.white).GetComponent<Image>(); art.sprite = Astella2DRoster.Frame(i, Vector2.down); art.preserveAspect = true;
                Label(button.transform, character.Name, new Vector2(130, 44), topLeft, new Vector2(83, -20), 18, true);
                Label(button.transform, RoleName(character.Role), new Vector2(125, 30), topLeft, new Vector2(85, -62), 15).color = RoleColor(character.Role, 1);
                _characterButtons.Add(button);
            }
            _selectedPortrait = Panel(_menu.transform, "Selected Portrait", new Vector2(185, 220), topLeft, new Vector2(1190, -260), Color.white).GetComponent<Image>(); _selectedPortrait.preserveAspect = true;
            _selectionText = Label(_menu.transform, "", new Vector2(470, 200), topLeft, new Vector2(1390, -270), 26, true);
            _kitText = Label(_menu.transform, "", new Vector2(640, 330), topLeft, new Vector2(1195, -500), 20);
            _kitText.alignment = TextAnchor.UpperLeft; _kitText.lineSpacing = 1.1f;
            Label(_menu.transform, "WASD 이동 · 마우스 조준 · 왼쪽 클릭/J 공격 · SPACE 대시\nQ·E 기술 · R 궁극기 · ESC 일시정지 · F1 조작 안내", new Vector2(640, 70), topLeft, new Vector2(1195, -850), 19).color = new Color(.7f, .7f, .86f);
            Button(_menu.transform, "원정 시작", new Vector2(400, 70), topLeft, new Vector2(133, -865), () => BeginRun(SelectedCharacter), 30);
            Label(_menu.transform, "세 번의 전투 · 쓰러져도 전투마다 부활 2회 · 승리하면 축복 선택", new Vector2(800, 45), topLeft, new Vector2(135, -975), 18).color = new Color(.61f, .60f, .75f);

            _battleHud = new GameObject("Combat HUD", typeof(RectTransform)); _battleHud.transform.SetParent(canvasGo.transform, false);
            _battleHud.transform.SetAsFirstSibling();
            var hudRect = (RectTransform)_battleHud.transform; hudRect.anchorMin = Vector2.zero; hudRect.anchorMax = Vector2.one; hudRect.offsetMin = hudRect.offsetMax = Vector2.zero;
            var numbers = (RectTransform)new GameObject("Combat Numbers", typeof(RectTransform)).transform; numbers.SetParent(hudRect, false);
            numbers.anchorMin = Vector2.zero; numbers.anchorMax = Vector2.one; numbers.offsetMin = numbers.offsetMax = Vector2.zero;
            _feedback = new Astella2DFeedback(numbers, _camera, _titleFont);
            if (prismHudPrefab == null) throw new InvalidOperationException("Assign the 2D Prism HUD with Astella > 2D > Create Scene.");
            _prism = Instantiate(prismHudPrefab, hudRect, false);
            _prism.Initialize(slot => TryUseSkill(slot), member => _selectedParty = member);
            _prism.Show(true);
            _noticeText = Label(_battleHud.transform, "", new Vector2(1150, 48), new Vector2(.5f, 0), new Vector2(0, 240), 24);
            _noticeText.alignment = TextAnchor.MiddleCenter;
            _statusText = Label(_battleHud.transform, "", new Vector2(520, 70), new Vector2(0, 1), new Vector2(34, -330), 20);
            _statusText.alignment = TextAnchor.UpperLeft;
            Button(_battleHud.transform, "ESC  메뉴", new Vector2(150, 43), Vector2.one, new Vector2(-30, -25), () => SetPaused(true), 19);
            _outcome = Astella2DPrismMenu.CreateOutcome(ui, ChooseReward, () => BeginRun(SelectedCharacter), ReturnToMenu);
            _pause = Panel(ui, "Paused", new Vector2(600, 440), new Vector2(.5f, .5f), Vector2.zero, Ink).gameObject;
            Label(_pause.transform, "잠시 쉬어가기", new Vector2(500, 90), new Vector2(.5f, 1), new Vector2(0, -30), 36, true).alignment = TextAnchor.MiddleCenter;
            Button(_pause.transform, "계속", new Vector2(400, 65), new Vector2(.5f, .5f), new Vector2(0, 35), () => SetPaused(false));
            Button(_pause.transform, "다시 도전", new Vector2(400, 65), new Vector2(.5f, .5f), new Vector2(0, -45), () => BeginRun(SelectedCharacter));
            Button(_pause.transform, "캐릭터 선택", new Vector2(400, 65), new Vector2(.5f, .5f), new Vector2(0, -125), ReturnToMenu);
            _help = Panel(ui, "Controls Help", new Vector2(600, 250), Vector2.one, new Vector2(-30, -145), Ink).gameObject;
            Label(_help.transform, "WASD 이동 · 마우스 조준 · LMB/J 기본 공격\nQ·E 직업 기술 · R 궁극기(공명 100%)\nSPACE 무적 대시 · ESC 메뉴 · F1 닫기\n붉은 장판이 테두리까지 차오르는 순간 피해\n쓰러지면 전투마다 2회까지 다시 일어납니다", new Vector2(570, 230), new Vector2(.5f, .5f), Vector2.zero, 20); _help.SetActive(false);
            Astella2DPrismMenu.Apply(_menu, _pause);
        }

        void SelectCharacter(int index)
        {
            SelectedCharacter = Mathf.Clamp(index, 0, Astella2DRoster.All.Count - 1);
            var value = Astella2DRoster.All[SelectedCharacter];
            var kit = Astella2DKits.For(value.Role);
            _selectionText.text = value.Name + "\n" + RoleName(value.Role) + " · 체력 " + kit.MaxHp + "\n" + (kit.Ranged ? "원거리" : "근거리");
            _kitText.text = kit.Summary + "\n\n" +
                "LMB  " + kit.Basic.Name + " · " + kit.Basic.Description + "\n" +
                "Q  " + kit.Primary.Name + " · " + kit.Primary.Description + "\n" +
                "E  " + kit.Utility.Name + " · " + kit.Utility.Description + "\n" +
                "R  " + kit.Ultimate.Name + " · " + kit.Ultimate.Description;
            _selectedPortrait.sprite = Astella2DRoster.Frame(SelectedCharacter, Vector2.down);
            for (int i = 0; i < _characterButtons.Count; i++) Astella2DPrismMenu.SetSelected(_characterButtons[i], i == SelectedCharacter);
        }
        static string RoleName(PixelRole role) => Astella2DKits.For(role).RoleName;
        void RefreshHud()
        {
            if (_player == null || !_battleHud.activeSelf) return;
            var spec = Astella2DEncounters.Get(_encounter);
            Hazard cast = null;
            foreach (var hazard in _hazards) if (cast == null || hazard.Time < cast.Time) cast = hazard;
            string bossName = _bossHp < _bossMax * .5f ? spec.Name + " · 격앙" : spec.Name;
            _prism.UpdateBoss(_bossHp, _bossMax, _encounter + 1, bossName, cast?.Name, cast?.Time ?? 0, cast?.Duration ?? 0);
            _prism.UpdatePlayer(_player.Hp, _player.MaxHp, _ultimate, _cooldowns[4], _durations[4]);
            _prism.UpdateSkills(_cooldowns, _durations, _ultimate, Running && !_paused && _player.Hp > 0);
            if (_noticeText.text != _notice) _noticeText.text = _notice;
            string status = "부활 " + _revivesLeft + "회";
            if (_player.Hp <= 0 && _downTime > 0) status += " · 부활까지 " + _downTime.ToString("0.0", CultureInfo.InvariantCulture) + "초";
            if (_player.GuardTime > 0) status += " · 보호 " + Mathf.RoundToInt(_player.Guard * 100) + "% " + _player.GuardTime.ToString("0.0", CultureInfo.InvariantCulture) + "초";
            if (_empowerTime > 0) status += " · 집중 " + _empowerTime.ToString("0.0", CultureInfo.InvariantCulture) + "초";
            if (_statusText.text != status) _statusText.text = status;
            for (int i = 0; i < 4; i++)
            {
                var fighter = i < _party.Count ? _party[i] : null;
                if (fighter == null) { _prism.UpdateParty(i, "", 0, 0, null, false); continue; }
                var character = Astella2DRoster.All[fighter.Character];
                _prism.UpdateParty(i, character.Name, fighter.Hp, fighter.MaxHp, Astella2DRoster.Frame(fighter.Character, Vector2.down), i == _selectedParty);
            }
        }

        // ---- Player smoke run: --astella2d-smoke=<dir> [--astella2d-character=N] ----

        static string CommandLine(string prefix)
        {
            foreach (string value in Environment.GetCommandLineArgs())
                if (value.StartsWith(prefix, StringComparison.Ordinal)) return value.Substring(prefix.Length);
            return null;
        }

        [Serializable]
        sealed class SmokeReport
        {
            public string utc, character, graphics, screenshot, result;
            public int encounter, attacks, damageEvents, dodges, revivesUsed, frames, screenWidth, screenHeight;
            public float seconds, bossHealthRatio, playerHealth, averageFrameMs, worstFrameMs, damageDealt;
            public bool finished, won;
        }

        IEnumerator SmokeRun(string directory)
        {
            directory = Path.GetFullPath(directory);
            Directory.CreateDirectory(directory);
            int character = int.TryParse(CommandLine("--astella2d-character="), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : 10;
            yield return null;
            BeginRun(character);
            SetAutopilot(true);
            float seconds = 0, worst = 0; int frames = 0;
            while (seconds < 40 && !(Finished && !RewardOffered))
            {
                if (RewardOffered) ChooseReward(0);
                yield return null;
                seconds += Time.unscaledDeltaTime; frames++;
                if (seconds > 2) worst = Mathf.Max(worst, Time.unscaledDeltaTime);
            }
            string shot = Path.Combine(directory, "player-smoke.png");
            ScreenCapture.CaptureScreenshot(shot);
            for (int i = 0; i < 120 && !File.Exists(shot); i++) yield return null;
            var report = new SmokeReport
            {
                utc = DateTime.UtcNow.ToString("O"), character = Astella2DRoster.All[SelectedCharacter].Name,
                graphics = SystemInfo.graphicsDeviceType + ": " + SystemInfo.graphicsDeviceName, screenshot = File.Exists(shot) ? shot : "",
                result = Finished ? (Won ? "won" : "lost") : "running", encounter = _encounter + 1, attacks = Attacks, damageEvents = DamageEvents,
                dodges = DodgeCount, revivesUsed = _revivesUsed, frames = frames, screenWidth = Screen.width, screenHeight = Screen.height,
                seconds = seconds, bossHealthRatio = _bossMax > 0 ? _bossHp / _bossMax : 0, playerHealth = PlayerHealth,
                averageFrameMs = frames > 0 ? seconds / frames * 1000 : 0, worstFrameMs = worst * 1000, damageDealt = _damageDealt,
                finished = Finished, won = Won
            };
            File.WriteAllText(Path.Combine(directory, "player-smoke.json"), JsonUtility.ToJson(report, true) + "\n", new System.Text.UTF8Encoding(false));
            Application.Quit();
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            ClearBattle(); Astella2DRoster.Release();
            _feedback?.Release();
            foreach (var item in _owned) if (item != null) Destroy(item);
            _owned.Clear(); if (Instance == this) Instance = null;
        }
    }
}
