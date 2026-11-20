using System;
using System.Collections.Generic;
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
        public int DamageEvents { get; private set; }
        public int Attacks { get; private set; }
        public int DodgeCount { get; private set; }
        public int ActiveTelegraphs => _hazards.Count;
        public float PlayerHealth => _player != null ? _player.Hp : 0;
        public float BossHealth => _bossHp;
        public Vector2 PlayerPosition => _player != null ? _player.Body.position : Vector2.zero;
        public Vector2 BossPosition => new Vector2(0, 1.1f);
        public float CombatRange => IsRanged ? 10f : 2.8f;
        public Astella2DPrismView prismHudPrefab;

        sealed class Fighter
        {
            public int Character;
            public float Hp, MaxHp, HitTimer, AttackTimer, HealTimer;
            public Rigidbody2D Body;
            public SpriteRenderer Art;
            public Transform Visual;
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
            public Mesh Mesh;
            public Color Color;
        }
        sealed class Effect
        {
            public GameObject View;
            public float Life, Total;
            public Vector3 From, To;
            public bool Flying;
            public Mesh Mesh;
        }

        readonly List<Fighter> _party = new List<Fighter>();
        readonly List<Hazard> _hazards = new List<Hazard>();
        readonly List<Effect> _effects = new List<Effect>();
        readonly float[] _cooldowns = new float[5];
        readonly float[] _durations = { .52f, 4.8f, 10f, 1f, 2.2f };
        readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        readonly List<Mesh> _battleMeshes = new List<Mesh>();
        Fighter _player;
        Camera _camera;
        Material _shapeMaterial;
        Transform _world;
        SpriteRenderer _bossArt;
        Sprite _bossSprite;
        Font _font, _titleFont;
        GameObject _menu, _battleHud, _pause, _help;
        Text _selectionText, _noticeText;
        Astella2DPrismView _prism;
        Astella2DPrismOutcome _outcome;
        int _selectedParty;
        readonly List<Button> _characterButtons = new List<Button>();
        Image _selectedPortrait;
        Vector2 _move, _aim = Vector2.right, _dashDirection;
        float _bossHp, _bossMax, _patternTimer, _dashTime, _hurtTime, _ultimate, _elapsed, _noticeTime;
        float _damageBonus = 1, _healthBonus = 1, _dashBonus = 1;
        int _encounter, _pattern;
        bool _automation, _paused;
        string _notice = "";
        static readonly Color Ink = new Color(.045f, .055f, .11f, .93f);
        static readonly Color Silver = new Color(.89f, .91f, .98f);
        static readonly Color Violet = new Color(.66f, .43f, .96f);
        bool IsRanged => _player != null && (Astella2DRoster.All[SelectedCharacter].Role == PixelRole.Ranged || Astella2DRoster.All[SelectedCharacter].Role == PixelRole.Caster || Astella2DRoster.All[SelectedCharacter].Role == PixelRole.Healer);

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

        public void SetAutomation(bool value) { _automation = value; _move = Vector2.zero; }
        public void RequestMove(Vector2 direction) { _move = Vector2.ClampMagnitude(direction, 1); }
        public void AimAt(Vector2 target) { if ((target - PlayerPosition).sqrMagnitude > .001f) _aim = (target - PlayerPosition).normalized; }

        public void BeginRun(int index = 0)
        {
            SelectedCharacter = Mathf.Clamp(index, 0, Astella2DRoster.All.Count - 1);
            _damageBonus = _healthBonus = _dashBonus = 1; _encounter = 0; _elapsed = 0;
            DamageEvents = Attacks = DodgeCount = 0;
            StartEncounter();
        }

        void StartEncounter()
        {
            ClearBattle();
            Finished = Won = _paused = false; Running = true; _move = Vector2.zero;
            _dashTime = _hurtTime = 0; _ultimate = 25; _patternTimer = 3; _pattern = 0;
            Array.Clear(_cooldowns, 0, _cooldowns.Length);
            _bossMax = 14000 + _encounter * 6500; _bossHp = _bossMax;
            _bossArt = SpriteObject("공명 수호자", _world, _bossSprite, 10);
            _bossArt.transform.position = BossPosition;
            _bossArt.transform.localScale = Vector3.one * (3.7f / _bossSprite.bounds.size.y);
            MakeShadow(_bossArt.transform, 1.35f, .38f);
            _player = Spawn(SelectedCharacter, new Vector2(0, -2.2f));
            int[] candidates = { 0, 4, 8, 12 };
            foreach (int candidate in candidates)
            {
                if (_party.Count >= 4) break;
                if (candidate == SelectedCharacter) continue;
                Spawn(candidate, new Vector2((_party.Count - 2) * 2.3f, -1.8f));
            }
            _selectedParty = 0;
            _menu.SetActive(false); _battleHud.SetActive(true); _outcome.Hide(); _pause.SetActive(false);
            Notice("공명 원정 " + (_encounter + 1) + " / 3 · 장판을 피하며 수호자의 핵을 파괴하세요", 4);
            RefreshHud();
        }

        Fighter Spawn(int index, Vector2 point)
        {
            var definition = Astella2DRoster.All[index];
            float hp = definition.Role == PixelRole.Tank ? 1650 : definition.Role == PixelRole.Healer ? 1150 : 1050;
            var go = new GameObject(definition.Name, typeof(Rigidbody2D), typeof(CircleCollider2D)); go.transform.SetParent(_world, false);
            go.transform.position = point;
            var body = go.GetComponent<Rigidbody2D>(); body.gravityScale = 0; body.freezeRotation = true;
            body.interpolation = RigidbodyInterpolation2D.Interpolate; body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.linearDamping = 20;
            go.GetComponent<CircleCollider2D>().radius = .22f;
            var visual = new GameObject("Pixel Visual").transform; visual.SetParent(go.transform, false);
            var art = SpriteObject("Eight Direction Sprite", visual, Astella2DRoster.Frame(index, Vector2.down), 30);
            var fighter = new Fighter { Character = index, Body = body, Art = art, Visual = visual, Target = point, Hp = hp * _healthBonus, MaxHp = hp * _healthBonus, AttackTimer = .6f + _party.Count * .2f, HealTimer = 5 };
            _party.Add(fighter); MakeShadow(go.transform, .40f, .14f); return fighter;
        }

        public void ReturnToMenu()
        {
            Running = Finished = Won = _paused = false; _move = Vector2.zero;
            ClearBattle();
            _menu.SetActive(true); _battleHud.SetActive(false); _outcome.Hide(); _pause.SetActive(false);
            SelectCharacter(SelectedCharacter);
        }

        void ClearBattle()
        {
            foreach (var fighter in _party) if (fighter.Body != null) { fighter.Body.simulated = false; fighter.Body.gameObject.SetActive(false); Destroy(fighter.Body.gameObject); }
            _party.Clear(); _player = null;
            if (_bossArt != null) { _bossArt.gameObject.SetActive(false); Destroy(_bossArt.gameObject); }
            foreach (var hazard in _hazards) ReleaseHazard(hazard);
            _hazards.Clear();
            foreach (var effect in _effects) { Destroy(effect.View); Destroy(effect.Mesh); }
            _effects.Clear();
            foreach (var mesh in _battleMeshes) if (mesh != null) Destroy(mesh);
            _battleMeshes.Clear();
            _bossHp = 0;
        }

        void Update()
        {
            if (!_automation) ReadInput();
            float dt = Time.deltaTime;
            if (Running && !_paused)
            {
                _elapsed += dt; _hurtTime = Mathf.Max(0, _hurtTime - dt);
                for (int i = 0; i < _cooldowns.Length; i++) _cooldowns[i] = Mathf.Max(0, _cooldowns[i] - dt);
                _ultimate = Mathf.Min(100, _ultimate + dt * 1.3f);
                UpdateHazards(dt);
                if (Running) UpdateBoss(dt);
                if (Running) UpdateParty(dt);
            }
            if (!_paused) UpdateEffects(dt);
            if (_noticeTime > 0) { _noticeTime -= dt; if (_noticeTime <= 0) _notice = ""; }
            RefreshHud();
        }

        void ReadInput()
        {
            var keys = Keyboard.current;
            if (keys == null) return;
            if (keys.escapeKey.wasPressedThisFrame && Running) { _paused = !_paused; _pause.SetActive(_paused); _move = Vector2.zero; }
            if (keys.f1Key.wasPressedThisFrame) _help.SetActive(!_help.activeSelf);
            if (!Running || _paused) return;
            if (!Application.isFocused) { _move = Vector2.zero; return; }
            _move = RaidRogue.LocalInputBindings.Movement;
            var mouse = Mouse.current;
            if (mouse != null)
            {
                Vector3 p = _camera.ScreenToWorldPoint(new Vector3(mouse.position.ReadValue().x, mouse.position.ReadValue().y, 20)); AimAt(p);
                if (mouse.leftButton.isPressed && !(EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())) TryUseSkill(0);
            }
            if (keys.jKey.isPressed) TryUseSkill(0);
            if (keys.qKey.wasPressedThisFrame) TryUseSkill(1);
            if (keys.eKey.wasPressedThisFrame) TryUseSkill(2);
            if (keys.rKey.wasPressedThisFrame) TryUseSkill(3);
            if (RaidRogue.LocalInputBindings.Pressed("Dash") || keys.spaceKey.wasPressedThisFrame) TryUseSkill(4);
        }

        void FixedUpdate()
        {
            if (!Running || _paused || _player == null)
            {
                foreach (var fighter in _party) if (fighter.Body != null) fighter.Body.linearVelocity = Vector2.zero;
                return;
            }
            float dt = Time.fixedDeltaTime;
            var movement = _dashTime > 0 ? _dashDirection * 12.8f : _move * 5.2f;
            if (_dashTime > 0) _dashTime = Mathf.Max(0, _dashTime - dt);
            var point = _player.Body.position + movement * dt;
            var clamped = Astella2DCombatRules.ClampToEllipse(point.x, point.y, 9.1f, 3.65f);
            _player.Body.MovePosition(new Vector2(clamped.X, clamped.Y));
            UpdateVisual(_player, movement.sqrMagnitude > .01f ? movement : _aim, movement.sqrMagnitude > .01f);
            for (int i = 1; i < _party.Count; i++)
            {
                var fighter = _party[i];
                if (fighter.Hp <= 0) { fighter.Body.linearVelocity = Vector2.zero; continue; }
                Vector2 direction = fighter.Target - fighter.Body.position;
                fighter.Body.MovePosition(Vector2.MoveTowards(fighter.Body.position, fighter.Target, dt * 2.6f));
                UpdateVisual(fighter, direction.sqrMagnitude > .02f ? direction : BossPosition - fighter.Body.position, direction.sqrMagnitude > .02f);
            }
        }

        void UpdateVisual(Fighter fighter, Vector2 direction, bool moving)
        {
            if (direction.sqrMagnitude > .001f) fighter.Facing = direction.normalized;
            fighter.Art.sprite = Astella2DRoster.Frame(fighter.Character, fighter.Facing);
            fighter.Art.sortingOrder = 40 - Mathf.RoundToInt(fighter.Body.position.y * 20);
            fighter.Visual.localPosition = new Vector3(0, moving ? Mathf.Abs(Mathf.Sin(Time.time * 14)) * .055f : Mathf.Sin(Time.time * 2.5f) * .008f, 0);
            fighter.Art.color = fighter.Hp <= 0 ? new Color(.43f, .41f, .56f, .55f) : fighter.HitTimer > 0 ? new Color(1, .5f, .55f) : fighter == _player && _dashTime > 0 ? new Color(.55f, .85f, 1) : Color.white;
        }

        public bool TryUseSkill(int slot)
        {
            if (!Running || _paused || _player == null || _player.Hp <= 0 || slot < 0 || slot >= 5 || _cooldowns[slot] > 0) return false;
            if (slot == 4)
            {
                _dashDirection = _move.sqrMagnitude > .01f ? _move.normalized : _aim;
                if (_dashDirection.sqrMagnitude < .01f) _dashDirection = Vector2.down;
                _dashTime = .22f; _cooldowns[4] = _durations[4] * _dashBonus;
                Flash(PlayerPosition, .45f, new Color(.48f, .80f, 1, .6f)); return true;
            }
            if (slot == 2)
            {
                foreach (var fighter in _party)
                {
                    fighter.Hp = Mathf.Min(fighter.MaxHp, fighter.Hp + fighter.MaxHp * (Astella2DRoster.All[SelectedCharacter].Role == PixelRole.Healer ? .4f : .23f));
                    Flash(fighter.Body.position, .6f, new Color(.35f, .95f, .73f, .6f));
                }
                _hurtTime = .8f; _cooldowns[2] = _durations[2]; Notice("은빛 가호 · 파티 회복과 소생", 1.5f); return true;
            }
            if (slot == 3 && _ultimate < 100) { Notice("공명을 100% 모으면 사용할 수 있습니다", .8f); return false; }
            float range = slot == 0 ? CombatRange : slot == 1 ? (IsRanged ? 10 : 3.8f) : 99;
            if (Vector2.Distance(PlayerPosition, BossPosition) > range) { Notice("사거리가 닿지 않습니다 · 수호자에게 더 가까이 이동하세요", .8f); return false; }
            float damage = (slot == 0 ? 105 : slot == 1 ? 420 : 2100) * _damageBonus;
            _cooldowns[slot] = _durations[slot]; Attacks++;
            _ultimate = slot == 3 ? 0 : Mathf.Min(100, _ultimate + (slot == 0 ? 6 : 16));
            _aim = (BossPosition - PlayerPosition).normalized;
            if (IsRanged) Projectile(PlayerPosition + Vector2.up * .7f, BossPosition + Vector2.up * 1.1f, slot == 3 ? new Color(.95f, .8f, 1) : Violet);
            else Flash(BossPosition + Vector2.up * .5f, slot == 0 ? .7f : 1.3f, new Color(.75f, .67f, 1, .7f));
            if (slot == 1 || slot == 3) Flash(BossPosition, slot == 3 ? 3 : 1.7f, new Color(.65f, .38f, 1, .6f));
            HitBoss(damage); return true;
        }

        void HitBoss(float amount)
        {
            if (!Running || amount <= 0) return;
            _bossHp = Mathf.Max(0, _bossHp - amount);
            DamageEvents++;
            if (_bossArt != null) _bossArt.color = new Color(1, .72f, .96f);
            if (_bossHp <= 0) EndEncounter(true);
        }

        public void ApplyPlayerDamage(float amount)
        {
            if (!Running || _player == null || amount <= 0 || float.IsNaN(amount) || float.IsInfinity(amount)) return;
            if (_dashTime > 0) { DodgeCount++; Notice("회피", .7f); return; }
            if (_hurtTime > 0) return;
            _player.Hp = Mathf.Max(0, _player.Hp - amount); _player.HitTimer = .25f; _hurtTime = .48f; DamageEvents++;
            Flash(PlayerPosition, .55f, new Color(1, .30f, .43f, .65f));
            if (_player.Hp <= 0) EndEncounter(false);
        }

        void UpdateParty(float dt)
        {
            for (int i = 0; i < _party.Count; i++)
            {
                var fighter = _party[i]; fighter.HitTimer = Mathf.Max(0, fighter.HitTimer - dt);
                if (i == 0 || fighter.Hp <= 0) { if (fighter.Hp <= 0) UpdateVisual(fighter, fighter.Facing, false); continue; }
                float angle = i * 2.1f + _elapsed * .035f;
                float distance = Astella2DRoster.All[fighter.Character].Role == PixelRole.Tank ? 2.2f : 4.2f;
                Vector2 target = BossPosition + new Vector2(Mathf.Cos(angle) * distance, Mathf.Sin(angle) * distance * .60f);
                foreach (var hazard in _hazards)
                {
                    if (hazard.Time > .9f || !Contains(hazard, fighter.Body.position, .3f)) continue;
                    target = FindSafePoint(fighter.Body.position); break;
                }
                var bounded = Astella2DCombatRules.ClampToEllipse(target.x, target.y, 8.7f, 3.4f); target = new Vector2(bounded.X, bounded.Y);
                fighter.Target = target;
                fighter.AttackTimer -= dt; fighter.HealTimer -= dt;
                if (fighter.AttackTimer <= 0) { fighter.AttackTimer = 1.25f; Projectile(fighter.Body.position + Vector2.up * .6f, BossPosition + Vector2.up, new Color(.58f, .75f, 1)); HitBoss(62 * _damageBonus); if (!Running) return; }
                if (fighter.HealTimer <= 0 && Astella2DRoster.All[fighter.Character].Role == PixelRole.Healer)
                {
                    fighter.HealTimer = 7;
                    foreach (var ally in _party) if (ally.Hp > 0) ally.Hp = Mathf.Min(ally.MaxHp, ally.Hp + 75);
                }
            }
        }

        Vector2 FindSafePoint(Vector2 origin)
        {
            // Search the actual union of imminent telegraphs; radial escape is wrong for donuts and beams.
            Vector2 best = origin; float bestDistance = float.PositiveInfinity;
            for (int step = 1; step <= 9; step++)
            {
                float distance = step * .5f;
                for (int angle = 0; angle < 24; angle++)
                {
                    Vector2 raw = origin + Rotate(Vector2.right, angle * 15) * distance;
                    var bounded = Astella2DCombatRules.ClampToEllipse(raw.x, raw.y, 8.7f, 3.4f);
                    var candidate = new Vector2(bounded.X, bounded.Y);
                    float cost = (candidate - origin).sqrMagnitude;
                    if (cost >= bestDistance) continue;
                    bool safe = true;
                    foreach (var hazard in _hazards) if (hazard.Time <= 1 && Contains(hazard, candidate, .32f)) { safe = false; break; }
                    if (safe) { best = candidate; bestDistance = cost; }
                }
                if (!float.IsPositiveInfinity(bestDistance)) break;
            }
            return best;
        }

        void UpdateBoss(float dt)
        {
            if (_bossArt != null) _bossArt.color = Color.Lerp(_bossArt.color, Color.white, dt * 12);
            _patternTimer -= dt;
            if (_patternTimer > 0) return;
            bool enraged = _bossHp < _bossMax * .5f;
            float cast = (enraged ? 1.15f : 1.55f) - _encounter * .06f;
            switch (_pattern++ % 5)
            {
                case 0: AddHazard(Shape.Circle, PlayerPosition, Vector2.up, 1.75f, 0, 0, cast, "낙성 · 표적 원형"); break;
                case 1: AddHazard(Shape.Donut, BossPosition, Vector2.up, 6.5f, 2.6f, 0, cast + .4f, "공명 파동 · 안쪽으로"); break;
                case 2:
                    var direction = (PlayerPosition - BossPosition).normalized;
                    for (int i = -1; i <= 1; i++) AddHazard(Shape.Line, BossPosition, Rotate(direction, i * 35), 9.4f, 0, .55f, cast, "분광 절단 · 직선");
                    break;
                case 3: AddHazard(Shape.Cone, BossPosition, (PlayerPosition - BossPosition).normalized, 8, 0, 42, cast, "수정 숨결 · 측면으로"); break;
                default:
                    for (int i = 0; i < 5; i++)
                    {
                        float angle = i * Mathf.PI * 2 / 5 + _elapsed * .15f;
                        AddHazard(Shape.Circle, new Vector2(Mathf.Cos(angle) * 5.5f, Mathf.Sin(angle) * 2.5f), Vector2.up, 1.65f, 0, 0, cast + .2f, "별의 잔향 · 안전지대 찾기");
                    }
                    break;
            }
            _patternTimer = enraged ? 2.6f : 3.6f;
        }

        static Vector2 Rotate(Vector2 value, float degrees) { float a = degrees * Mathf.Deg2Rad; return new Vector2(value.x * Mathf.Cos(a) - value.y * Mathf.Sin(a), value.x * Mathf.Sin(a) + value.y * Mathf.Cos(a)); }
        void AddHazard(Shape shape, Vector2 center, Vector2 direction, float radius, float inner, float width, float cast, string name)
        {
            if (direction.sqrMagnitude < .001f) direction = Vector2.down;
            var hazard = new Hazard { Shape = shape, Center = center, Direction = direction.normalized, Radius = radius, Inner = inner, Width = width, Time = cast, Duration = cast, Name = name, Color = new Color(.96f, .21f, .36f, .26f) };
            hazard.View = ShapeView(hazard, hazard.Color, -40, out hazard.Mesh); _hazards.Add(hazard);
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
            for (int i = _hazards.Count - 1; i >= 0; i--)
            {
                var hazard = _hazards[i]; hazard.Time -= dt;
                SetMeshColor(hazard.Mesh, new Color(.98f, .16f, .29f, Mathf.Lerp(.55f, .15f, Mathf.Clamp01(hazard.Time / hazard.Duration))));
                if (hazard.Time > 0) continue;
                if (Contains(hazard, PlayerPosition, .22f)) ApplyPlayerDamage(260 + _encounter * 35);
                if (!Running) return; // EndEncounter clears all views and collections.
                for (int j = 1; j < _party.Count; j++)
                {
                    var fighter = _party[j];
                    if (fighter.Hp <= 0 || !Contains(hazard, fighter.Body.position, .22f)) continue;
                    fighter.Hp = Mathf.Max(0, fighter.Hp - 230); fighter.HitTimer = .3f;
                }
                var view = ShapeView(hazard, new Color(1, .62f, .80f, .65f), -30, out var impactMesh);
                _effects.Add(new Effect { View = view, Mesh = impactMesh, Life = .17f, Total = .17f, From = view.transform.position, To = view.transform.position });
                ReleaseHazard(hazard); _hazards.RemoveAt(i);
            }
        }

        void EndEncounter(bool won)
        {
            Running = false; Finished = true; Won = won; _move = Vector2.zero; _dashTime = 0;
            foreach (var fighter in _party) { fighter.Body.linearVelocity = Vector2.zero; fighter.Body.simulated = false; }
            foreach (var hazard in _hazards) ReleaseHazard(hazard); _hazards.Clear();
            _battleHud.SetActive(false);
            _outcome.Show(won, won && _encounter < 2);
        }

        void ChooseReward(int reward)
        {
            if (!Finished || !Won || _encounter >= 2) return;
            if (reward == 0) _damageBonus *= 1.25f;
            if (reward == 1) _healthBonus *= 1.3f;
            if (reward == 2) _dashBonus *= .75f;
            _encounter++; StartEncounter();
        }

        void Notice(string value, float duration) { _notice = value; _noticeTime = duration; }
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
            else if (h.Shape == Shape.Cone) ring(Vector2.zero, 0, h.Radius, Mathf.Atan2(h.Direction.y, h.Direction.x) * Mathf.Rad2Deg - h.Width, h.Width * 2);
            else
            {
                var normal = new Vector2(-h.Direction.y, h.Direction.x) * h.Width; var end = h.Direction * h.Radius;
                vertices.AddRange(new Vector3[] { normal, -normal, end - normal, end + normal }); triangles.AddRange(new[] { 0, 1, 2, 0, 2, 3 });
                float angle = Mathf.Atan2(h.Direction.y, h.Direction.x) * Mathf.Rad2Deg;
                ring(Vector2.zero, 0, h.Width, angle + 90, 180); ring(end, 0, h.Width, angle - 90, 180);
            }
            mesh = new Mesh { name = "2D " + h.Shape }; mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); SetMeshColor(mesh, color); mesh.RecalculateBounds();
            var go = new GameObject("Telegraph " + h.Name, typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(_world, false); go.transform.position = h.Center;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _shapeMaterial;
            renderer.sortingOrder = order;
            return go;
        }
        static void SetMeshColor(Mesh mesh, Color color) { var colors = new Color[mesh.vertexCount]; for (int i = 0; i < colors.Length; i++) colors[i] = color; mesh.colors = colors; }
        void ReleaseHazard(Hazard hazard) { if (hazard.View != null) Destroy(hazard.View); if (hazard.Mesh != null) Destroy(hazard.Mesh); }

        void MakeShadow(Transform parent, float radius, float height)
        {
            var h = new Hazard { Shape = Shape.Circle, Radius = radius, Name = "Shadow" };
            var go = ShapeView(h, new Color(.015f, .01f, .04f, .35f), 0, out var mesh); _battleMeshes.Add(mesh);
            go.transform.SetParent(parent, false); go.transform.localScale = new Vector3(1, height / radius, 1);
        }
        void Flash(Vector2 position, float radius, Color color)
        {
            var h = new Hazard { Shape = Shape.Donut, Radius = radius, Inner = radius * .72f, Center = position, Name = "Resonance" };
            var go = ShapeView(h, color, 200, out var mesh);
            _effects.Add(new Effect { View = go, Mesh = mesh, Life = .25f, Total = .25f, From = position, To = position });
        }
        void Projectile(Vector2 from, Vector2 to, Color color)
        {
            var h = new Hazard { Shape = Shape.Circle, Radius = .10f, Center = from, Name = "Bolt" };
            var go = ShapeView(h, color, 150, out var mesh);
            _effects.Add(new Effect { View = go, Mesh = mesh, Life = .24f, Total = .24f, From = from, To = to, Flying = true });
        }
        void UpdateEffects(float dt)
        {
            for (int i = _effects.Count - 1; i >= 0; i--)
            {
                var effect = _effects[i]; effect.Life -= dt;
                if (effect.Life <= 0) { Destroy(effect.View); Destroy(effect.Mesh); _effects.RemoveAt(i); continue; }
                float progress = 1 - effect.Life / effect.Total;
                if (effect.Flying) effect.View.transform.position = Vector3.Lerp(effect.From, effect.To, progress);
                else effect.View.transform.localScale = Vector3.one * (1 + progress * .35f);
            }
        }

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
                var button = Button(_menu.transform, "", new Vector2(220, 108), topLeft, new Vector2(132 + i % 4 * 234, -300 - i / 4 * 123), () => SelectCharacter(index));
                var art = Panel(button.transform, "Portrait", new Vector2(78, 90), topLeft, new Vector2(6, -9), Color.white).GetComponent<Image>(); art.sprite = Astella2DRoster.Frame(i, Vector2.down); art.preserveAspect = true;
                Label(button.transform, character.Name, new Vector2(130, 44), topLeft, new Vector2(83, -20), 18, true);
                Label(button.transform, RoleName(character.Role), new Vector2(125, 30), topLeft, new Vector2(85, -62), 15).color = new Color(.65f, .66f, .82f);
                _characterButtons.Add(button);
            }
            _selectedPortrait = Panel(_menu.transform, "Selected Portrait", new Vector2(185, 220), topLeft, new Vector2(1190, -280), Color.white).GetComponent<Image>(); _selectedPortrait.preserveAspect = true;
            _selectionText = Label(_menu.transform, "", new Vector2(470, 150), topLeft, new Vector2(1390, -307), 26, true);
            Label(_menu.transform, "WASD / 방향키  이동\n마우스 왼쪽 / J  기본 공격\nQ  공명 기술    E  파티 회복·소생\nR  궁극기    SPACE  무적 대시\nESC  일시정지    F1  조작 안내", new Vector2(620, 260), topLeft, new Vector2(1195, -545), 24);
            Label(_menu.transform, "세 번의 전투, 세 번의 공명\n도트 캐릭터와 함께 장판을 피하고\n다음 원정을 위한 축복을 선택하세요.", new Vector2(620, 130), topLeft, new Vector2(1195, -820), 23, true);
            Button(_menu.transform, "원정 시작", new Vector2(400, 70), topLeft, new Vector2(133, -865), () => BeginRun(SelectedCharacter), 30);
            Label(_menu.transform, "캐릭터 컨셉 브랜치 · 13종 / 8방향", new Vector2(800, 45), topLeft, new Vector2(135, -975), 17).color = new Color(.61f, .60f, .75f);

            _battleHud = new GameObject("Combat HUD", typeof(RectTransform)); _battleHud.transform.SetParent(canvasGo.transform, false);
            _battleHud.transform.SetAsFirstSibling();
            var hudRect = (RectTransform)_battleHud.transform; hudRect.anchorMin = Vector2.zero; hudRect.anchorMax = Vector2.one; hudRect.offsetMin = hudRect.offsetMax = Vector2.zero;
            if (prismHudPrefab == null) throw new InvalidOperationException("Assign the 2D Prism HUD with Astella > 2D > Create Scene.");
            _prism = Instantiate(prismHudPrefab, hudRect, false);
            _prism.Initialize(slot => TryUseSkill(slot), member => _selectedParty = member);
            _prism.Show(true);
            _noticeText = Label(_battleHud.transform, "", new Vector2(1150, 48), new Vector2(.5f, 0), new Vector2(0, 240), 24);
            _noticeText.alignment = TextAnchor.MiddleCenter;
            Button(_battleHud.transform, "ESC  메뉴", new Vector2(150, 43), Vector2.one, new Vector2(-30, -25), () => { _paused = true; _pause.SetActive(true); }, 19);
            _outcome = Astella2DPrismMenu.CreateOutcome(ui, ChooseReward, () => BeginRun(SelectedCharacter), ReturnToMenu);
            _pause = Panel(ui, "Paused", new Vector2(600, 440), new Vector2(.5f, .5f), Vector2.zero, Ink).gameObject;
            Label(_pause.transform, "잠시 쉬어가기", new Vector2(500, 90), new Vector2(.5f, 1), new Vector2(0, -30), 36, true).alignment = TextAnchor.MiddleCenter;
            Button(_pause.transform, "계속", new Vector2(400, 65), new Vector2(.5f, .5f), new Vector2(0, 35), () => { _paused = false; _pause.SetActive(false); });
            Button(_pause.transform, "다시 도전", new Vector2(400, 65), new Vector2(.5f, .5f), new Vector2(0, -45), () => BeginRun(SelectedCharacter));
            Button(_pause.transform, "캐릭터 선택", new Vector2(400, 65), new Vector2(.5f, .5f), new Vector2(0, -125), ReturnToMenu);
            _help = Panel(ui, "Controls Help", new Vector2(560, 210), Vector2.one, new Vector2(-30, -145), Ink).gameObject;
            Label(_help.transform, "WASD 이동 · 마우스 조준 · LMB/J 공격\nQ 공명 기술 · E 회복/소생 · R 궁극기\nSPACE 무적 대시 · ESC 메뉴 · F1 닫기\n붉은 예고가 사라지는 순간 피해가 적용됩니다", new Vector2(530, 190), new Vector2(.5f, .5f), Vector2.zero, 20); _help.SetActive(false);
            Astella2DPrismMenu.Apply(_menu, _pause);
        }

        void SelectCharacter(int index)
        {
            SelectedCharacter = Mathf.Clamp(index, 0, Astella2DRoster.All.Count - 1);
            var value = Astella2DRoster.All[SelectedCharacter];
            _selectionText.text = value.Name + "\n" + RoleName(value.Role) + "\n8방향 쿼터뷰";
            _selectedPortrait.sprite = Astella2DRoster.Frame(SelectedCharacter, Vector2.down);
            for (int i = 0; i < _characterButtons.Count; i++) Astella2DPrismMenu.SetSelected(_characterButtons[i], i == SelectedCharacter);
        }
        static string RoleName(PixelRole role) => role == PixelRole.Tank ? "수호" : role == PixelRole.Healer ? "치유" : role == PixelRole.Melee ? "근접" : role == PixelRole.Ranged ? "원거리" : "마법";
        void RefreshHud()
        {
            if (_player == null || !_battleHud.activeSelf) return;
            var cast = _hazards.Count > 0 ? _hazards[0] : null;
            string bossName = _bossHp < _bossMax * .5f ? "공명 수호자 · 격앙" : "공명 수호자";
            _prism.UpdateBoss(_bossHp, _bossMax, _encounter + 1, bossName, cast?.Name, cast?.Time ?? 0, cast?.Duration ?? 0);
            _prism.UpdatePlayer(_player.Hp, _player.MaxHp, _ultimate, _cooldowns[4], _durations[4] * _dashBonus);
            _prism.UpdateSkills(_cooldowns, _durations, _ultimate, Running && !_paused);
            if (_noticeText.text != _notice) _noticeText.text = _notice;
            for (int i = 0; i < 4; i++)
            {
                var fighter = i < _party.Count ? _party[i] : null;
                if (fighter == null) { _prism.UpdateParty(i, "", 0, 0, null, false); continue; }
                var character = Astella2DRoster.All[fighter.Character];
                _prism.UpdateParty(i, character.Name, fighter.Hp, fighter.MaxHp, Astella2DRoster.Frame(fighter.Character, Vector2.down), i == _selectedParty);
            }
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            ClearBattle(); Astella2DRoster.Release();
            foreach (var item in _owned) if (item != null) Destroy(item);
            _owned.Clear(); if (Instance == this) Instance = null;
        }
    }
}
