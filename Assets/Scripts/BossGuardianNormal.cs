using System.Collections.Generic;
using UnityEngine;

namespace RaidRogue
{
    public partial class BossController
    {
        [Header("Stage 1 · 마을 수호 석상 노멀 (initial tuning)")]
        public GuardianNormalSettings guardian = new GuardianNormalSettings();
        [Header("Stage 1 · 마을 수호 석상 하드 (initial tuning; HP temporary)")]
        public GuardianHardSettings guardianHard = new GuardianHardSettings();
        public bool GuardianNormal => currentStageNumber == 1 && !CharacterTrainingSession.Active;
        public bool GuardianHard => GuardianNormal && CampaignRuntime.Instance?.DifficultyId == CampaignCatalog.Hard;
        private GuardianNormalSettings Tuning => GuardianHard ? guardianHard.combat : guardian;
        private readonly GuardianNormalState _guardian = new GuardianNormalState();
        private readonly GuardianHardState _guardianHard = new GuardianHardState();
        private GuardianPattern _guardianReplicaPattern;
        private GuardianStep _guardianReplicaStep;
        public GuardianPattern GuardianPatternNow => RaidCombatAuthority.Replica ? _guardianReplicaPattern : _guardian.Pattern;
        public GuardianStep GuardianStepNow => RaidCombatAuthority.Replica ? _guardianReplicaStep : _guardian.Step;
        private float _guardianStarted, _guardianEnds, _guardianStagger;
        private Vector3 _guardianOrigin, _guardianForward, _guardianEnd, _guardianSide;
        private Transform _guardianVictim;
        private readonly List<AoETelegraph> _guardianGround = new List<AoETelegraph>();
        private readonly Dictionary<Transform, Vector3> _guardianDestinations = new Dictionary<Transform, Vector3>();
        private readonly HashSet<Transform> _guardianDashHits = new HashSet<Transform>();
        private Transform _guardianClub, _guardianShield, _guardianCore, _guardianWeakness;
        private GameObject _guardianDecor;
        private AudioSource _guardianAudio;
        private AudioClip _guardianCounterSound;
        private AudioClip _guardianLockSound;
        private bool _guardianSignal;
        private bool _guardianCombatStarted, _guardianKneeling, _guardianHardFollowup;
        private readonly int[] _guardianUses = new int[System.Enum.GetValues(typeof(GuardianPattern)).Length];
        private readonly int[] _guardianHits = new int[System.Enum.GetValues(typeof(GuardianPattern)).Length];
        public string GuardianRunReport
        {
            get
            {
                var lines = new List<string>();
                foreach (GuardianPattern pattern in System.Enum.GetValues(typeof(GuardianPattern)))
                    if (pattern != GuardianPattern.None) lines.Add($"{pattern}: {_guardianUses[(int)pattern]} uses, {_guardianHits[(int)pattern]} hits");
                return string.Join("; ", lines);
            }
        }
        private Vector3 _guardianBodyPosition; private Quaternion _guardianBodyRotation;
        public bool GuardianStaggerActive => GuardianNormal && GuardianStepNow == GuardianStep.StaggerCheck;
        private float GuardianNow => RaidCombatAuthority.ServerTime;

        private void ResetGuardian()
        {
            guardian = guardian ?? new GuardianNormalSettings();
            guardianHard = guardianHard ?? new GuardianHardSettings();
            _guardianHard.Reset(); ClearGuardianMechanics(); ClearHardCorruption(); _guardian.Reset(GuardianNow, Tuning); _guardianCombatStarted = false;
            System.Array.Clear(_guardianUses,0,_guardianUses.Length); System.Array.Clear(_guardianHits,0,_guardianHits.Length);
            if (!GuardianNormal) return;
            if (GuardianHard) guardianHard.EnsureDefaults();
            else { guardian.MigrateInitialTuning(); guardian.EnsureTempoDefaults(); }
            currentBossTitle = GuardianHard ? "STAGE 1 : 마을 수호 석상 · 하드" : "STAGE 1 : 마을 수호 석상 · 노멀";
            maxHp = Mathf.Max(1, Tuning.health); currentHp = maxHp;
            arenaBoundaryRadius = 18; autoAttackDamageToTank = Tuning.autoDamage;
            _stagePhase1BodyColor = new Color(.47f, .49f, .45f);
            SetBossBodyColor(_stagePhase1BodyColor);
            BuildGuardianPresentation();
        }

        private void TickGuardian()
        {
            float now = GuardianNow;
            if (!_guardianCombatStarted) { _guardianCombatStarted = true; _guardian.Reset(now, Tuning); }
            if (!GuardianHard) _guardian.ObserveHealth(HpRatio);
            _castRemainingTime = _guardian.Remaining(now);
            if (_guardian.Step == GuardianStep.Rest)
            {
                fsmState = BossFSMState.IdleChasing;
                // Resolve the queued threshold before another autoattack or pattern can begin.
                if (GuardianHard && _guardian.Due(now))
                {
                    GuardianPattern forced = _guardianHard.Next(HpRatio);
                    if (forced != GuardianPattern.None) BeginGuardian(forced, now);
                    else if (_guardian.Due(now)) BeginGuardian(_guardian.Choose(Random.Range(0, 100000)), now);
                    else { RotateTowardsAggroTargetXZ(); UpdateMeleePursuit(Time.deltaTime); UpdateAutoAttack(); }
                }
                else if (GuardianHard) { RotateTowardsAggroTargetXZ(); UpdateMeleePursuit(Time.deltaTime); UpdateAutoAttack(); }
                else if (_guardian.OverloadPending || _guardian.Due(now))
                    BeginGuardian(_guardian.Choose(Random.Range(0, 100000)), now);
                else { RotateTowardsAggroTargetXZ(); UpdateMeleePursuit(Time.deltaTime); UpdateAutoAttack(); }
            }
            else if (_guardian.Step == GuardianStep.Groggy)
            {
                _groggyRemainingTime = _guardian.Remaining(now);
                if (_guardian.Due(now)) EndGuardian(now);
            }
            else
            {
                // Facing and origin are immutable through cast, lock and the entire charge.
                if ((_guardian.Pattern == GuardianPattern.LineFracture || GuardianHard &&
                    (_guardian.Pattern == GuardianPattern.Lockdown || _guardian.Pattern == GuardianPattern.FullLockdown)) && _guardian.Step == GuardianStep.Telegraph)
                    TrackGuardianLine();
                transform.rotation = Quaternion.LookRotation(_guardianForward);
                if (_guardian.Step != GuardianStep.Dash) transform.position = _guardianOrigin;
                if (_guardian.Pattern == GuardianPattern.Punishment && !CombatRange.LivingPartyMember(_guardianVictim))
                { EndGuardian(now); return; } // Cancel; next cast uses the normal living aggro resolver.
                if (_guardian.Pattern == GuardianPattern.Resonance && !CombatRange.LivingPartyMember(_guardianVictim))
                {
                    var members = GetLivingPartyMemberTransforms();
                    if (members.Count == 0) { EndGuardian(now); return; }
                    BeginGuardian(GuardianPattern.Resonance, now); // New recipient gets a full six seconds.
                    return;
                }
                if (_guardian.Pattern == GuardianPattern.LineFracture && _guardian.Step == GuardianStep.Telegraph &&
                    !CombatRange.LivingPartyMember(_guardianVictim))
                { BeginGuardian(GuardianPattern.LineFracture, now); return; } // New target receives the entire tracking time.
                if (_guardian.Pattern == GuardianPattern.Punishment && _activeTelegraph != null)
                    _activeTelegraph.transform.position = new Vector3(_guardianVictim.position.x, 0, _guardianVictim.position.z);
                if (_guardian.Step == GuardianStep.Dash) TickGuardianDash(now);
                else if (GuardianHard && TickGuardianHard(now)) { }
                else if (_guardian.Due(now)) AdvanceGuardian(now);
            }
            SyncGuardianTiming();
            UpdateCastBanner(); UpdateTopBossUI(); UpdateGuardianPresentation();
        }

        private static string GuardianName(GuardianPattern pattern) => pattern switch
        {
            GuardianPattern.Punishment => "침입자 징벌", GuardianPattern.Sweep => "돌봉 휩쓸기",
            GuardianPattern.FallingStone => "석편 낙하", GuardianPattern.PlazaSlam => "광장 내려찍기",
            GuardianPattern.GateCharge => "성문 쇄도", GuardianPattern.Resonance => "방패 공명",
            GuardianPattern.ShieldPush => "방패 밀쳐내기", GuardianPattern.LineFracture => "돌봉 직선 파쇄",
            GuardianPattern.Overload => "수호핵 과부하", GuardianPattern.Lockdown => "봉쇄 명령",
            GuardianPattern.Transformation => "오염된 마을 수호자", GuardianPattern.TwistedExecution => "뒤틀린 집행",
            GuardianPattern.FullLockdown => "완전 봉쇄", _ => "회복 · 재정렬"
        };
        private void BeginGuardian(GuardianPattern pattern, float now)
        {
            ClearGuardianMechanics(); CleanupActiveTelegraphAndTether();
            _guardian.Begin(pattern, now, Tuning);
            _guardianHardFollowup = false;
            _guardianUses[(int)pattern]++;
            _guardianPatternHits = 0;
            _guardianOrigin = transform.position; _guardianForward = transform.forward;
            _guardianSide = transform.right * (Random.value < .5f ? -1 : 1);
            fsmState = BossFSMState.CastingSkill; _isChasing = false; _autoAttackTimer = 0;
            _currentCastSkillName = GuardianName(pattern);
            _networkCastStarted = now; _castTotalDuration = _guardian.Ends - now;
            var members = GetLivingPartyMemberTransforms();
            if (GuardianHard && BeginGuardianHard(pattern, now, members)) { SyncGuardianTiming(); return; }
            switch (pattern)
            {
                case GuardianPattern.Punishment:
                    _guardianVictim = ResolveLivingAggroTarget();
                    if (_guardianVictim == null) { EndGuardian(now); return; }
                    _activeTelegraph = AoETelegraph.Spawn(AoETelegraphType.Circle_Radius6, _guardianVictim.position, Vector3.forward, _castTotalDuration, 0, .8f);
                    SetMechanicAlert(GuardianHard ? "침입자 징벌 · 같은 대상 2회 강타 / 방어와 회복" :
                        "침입자 징벌 · 붉은 표식 대상 방어 / 회복 준비", Color.red, _castTotalDuration);
                    break;
                case GuardianPattern.Sweep:
                    _activeTelegraph = AoETelegraph.Spawn(AoETelegraphType.FrontCone_120Deg, _guardianOrigin, _guardianSide, _castTotalDuration, 0, 36, 180);
                    SetMechanicAlert(GuardianHard ? "돌봉 휩쓸기 · 양쪽 연속 / 첫 반면의 반대쪽으로!" :
                        "돌봉 휩쓸기 · 표시된 반면의 반대쪽으로!", Color.yellow, _castTotalDuration);
                    break;
                case GuardianPattern.PlazaSlam:
                    _activeTelegraph = AoETelegraph.Spawn(AoETelegraphType.Circle_Radius6, _guardianOrigin, _guardianForward, _castTotalDuration, 0, Tuning.slamRadius);
                    SetMechanicAlert(GuardianHard ? "광장 내려찍기 · 중심 후 외곽 고리 / 먼저 밖으로" :
                        "광장 내려찍기 · 고정 중심 8m 밖으로!", Color.yellow, _castTotalDuration);
                    break;
                case GuardianPattern.FallingStone:
                    AssignGuardianSpread(members);
                    _isSpreadActive = true; _currentSpreadRadius = Tuning.stoneRadius;
                    foreach (var member in members)
                    { _spreadTargets.Add(member); _activePartyMarkers.Add(AttachedPartyMechanicMarker.SpawnSpreadMarker(member, Tuning.stoneRadius)); }
                    SetMechanicAlert("석편 낙하 · 산개 → 바닥 고정 후 이탈", Color.magenta,
                        _castTotalDuration + Tuning.LockedDuration(GuardianPattern.FallingStone));
                    break;
                case GuardianPattern.Resonance:
                    if (members.Count == 0) { EndGuardian(now); return; }
                    _guardianVictim = members[Random.Range(0, members.Count)];
                    _stackTarget = _guardianVictim; _isStackActive = true; stackRadius = Tuning.resonanceRadius;
                    _activeStackMarker = AttachedPartyMechanicMarker.SpawnStackMarker(_guardianVictim, stackRadius);
                    _activePartyMarkers.Add(_activeStackMarker);
                    SetMechanicAlert("방패 공명 · 노란 원 안으로 집합! 피해 분담", Color.yellow, _castTotalDuration);
                    break;
                case GuardianPattern.GateCharge:
                    // A bounded straight segment; the boss never turns towards a moving victim later.
                    Vector3 flat = _guardianOrigin; flat.y = 0;
                    float dot = Vector3.Dot(flat, _guardianForward);
                    float radius = Mathf.Max(1, arenaBoundaryRadius - CombatRadius);
                    float distance = Mathf.Max(1, -dot + Mathf.Sqrt(Mathf.Max(0, dot * dot + radius * radius - flat.sqrMagnitude)));
                    _guardianEnd = _guardianOrigin + _guardianForward * distance;
                    _activeTelegraph = AoETelegraph.Spawn(AoETelegraphType.Rectangle, _guardianOrigin, _guardianForward, _castTotalDuration + 2,
                        Tuning.chargeHalfWidth, distance);
                    SetMechanicAlert("성문 쇄도 준비 · 정면 접근, 푸른 약점 때 [" + LocalInputBindings.Label("Counter") + "] 카운터 / 실패 시 옆으로", Color.yellow, _castTotalDuration + Tuning.counterWindowSeconds);
                    break;
                case GuardianPattern.ShieldPush:
                    _activeTelegraph = AoETelegraph.Spawn(AoETelegraphType.FrontCone_120Deg, _guardianOrigin,
                        _guardianForward, _castTotalDuration, 0, Tuning.shieldRange, Tuning.shieldAngle);
                    SetMechanicAlert("방패 밀쳐내기 · 정면 밖 측후면으로 회피", Color.yellow, _castTotalDuration);
                    break;
                case GuardianPattern.LineFracture:
                    _guardianVictim = GuardianLineTarget(members);
                    if (_guardianVictim == null) { EndGuardian(now); return; }
                    TrackGuardianLine();
                    _activePartyMarkers.Add(AttachedPartyMechanicMarker.SpawnSpreadMarker(_guardianVictim, .75f));
                    _activeTelegraph = AoETelegraph.Spawn(AoETelegraphType.Rectangle, _guardianOrigin,
                        _guardianForward, _castTotalDuration, Tuning.lineWidth * .5f,
                        BoundedGuardianLineLength(_guardianOrigin, _guardianForward, Tuning.lineLength));
                    SetMechanicAlert("돌봉 직선 파쇄 · 표적 추적 중, 고정되면 옆으로!", Color.magenta,
                        _castTotalDuration + Tuning.LockedDuration(GuardianPattern.LineFracture));
                    break;
                case GuardianPattern.Overload:
                    SetGuardianKneeling(true);
                    SetMechanicAlert($"수호핵 과부하 · {Tuning.overloadSeconds:0.#}초 안에 무력화! 기본 스킬로 핵을 공격", new Color(.8f,.3f,1), Tuning.overloadSeconds);
                    break;
            }
            SyncGuardianTiming();
            Debug.Log($"[Guardian] begin {pattern} t={now:F2} end={_guardian.Ends:F2}");
        }

        private void AdvanceGuardian(float now)
        {
            if (_guardian.Pattern == GuardianPattern.FallingStone && _guardian.Step == GuardianStep.Telegraph)
            {
                // Dead/unavailable recipients lose their tracking marker. Locked circles survive later deaths.
                foreach (var member in _spreadTargets)
                    if (CombatRange.LivingPartyMember(member))
                        _guardianGround.Add(AoETelegraph.Spawn(AoETelegraphType.Circle_Radius6, member.position, Vector3.forward,
                            Tuning.LockedDuration(GuardianPattern.FallingStone), 0, Tuning.stoneRadius));
                ClearActivePartyMarkers(); _isSpreadActive = false; _guardianDestinations.Clear();
                _guardian.Enter(GuardianStep.Locked, now, Tuning.LockedDuration(GuardianPattern.FallingStone));
                _currentCastSkillName = "석편 낙하 · 위치 고정 / 이탈";
                _networkCastStarted = now; _castTotalDuration = _guardian.Ends - now;
                PlayGuardianLockCue();
                return;
            }
            if (_guardian.Pattern == GuardianPattern.LineFracture && _guardian.Step == GuardianStep.Telegraph)
            {
                // One authoritative direction and origin for the warning and the final hit.
                TrackGuardianLine();
                if (_activeTelegraph != null) { _activeTelegraph.gameObject.SetActive(false); Destroy(_activeTelegraph.gameObject); }
                ClearActivePartyMarkers(); _guardianDestinations.Clear();
                _guardian.Enter(GuardianStep.Locked, now, Tuning.LockedDuration(GuardianPattern.LineFracture));
                _castTotalDuration = _guardian.Ends - now; _networkCastStarted = now;
                _currentCastSkillName = "돌봉 직선 파쇄 · 방향 고정 / 옆으로!";
                _activeTelegraph = AoETelegraph.Spawn(AoETelegraphType.Rectangle, _guardianOrigin,
                    _guardianForward, _castTotalDuration, Tuning.lineWidth * .5f,
                    BoundedGuardianLineLength(_guardianOrigin, _guardianForward, Tuning.lineLength));
                _activeTelegraph.baseFillColor = new Color(1f,.48f,.07f,.45f);
                _activeTelegraph.borderColor = new Color(1f,.7f,.12f,1f);
                _activeTelegraph.RefreshColors();
                SetMechanicAlert(_currentCastSkillName, new Color(1f,.65f,.1f), _castTotalDuration);
                PlayGuardianLockCue();
                return;
            }
            if (_guardian.Pattern == GuardianPattern.GateCharge && _guardian.Step == GuardianStep.Telegraph)
            {
                _guardian.Enter(GuardianStep.CounterWindow, now, Tuning.counterWindowSeconds);
                CombatCounterTarget.Ensure(transform).OpenWindow(Tuning.counterWindowSeconds);
                _networkCastStarted = now; _castTotalDuration = Tuning.counterWindowSeconds; _currentCastSkillName = "성문 쇄도 · 카운터 가능!";
                return;
            }
            if (_guardian.Pattern == GuardianPattern.GateCharge && _guardian.Step == GuardianStep.CounterWindow)
            {
                CombatCounterTarget.Ensure(transform).CloseWindow();
                _guardian.Enter(GuardianStep.Dash, now, Vector3.Distance(_guardianOrigin, _guardianEnd) / Mathf.Max(1, Tuning.chargeSpeed));
                _guardianDestinations.Clear(); _castTotalDuration = _guardian.Ends - now;
                _currentCastSkillName = "성문 쇄도 · 돌진"; return;
            }
            var living = GetLivingPartyMemberTransforms();
            if (_guardian.Pattern == GuardianPattern.Punishment)
                GuardianHit(_guardianVictim, Tuning.punishmentDamage, CombatDamageKind.Direct);
            else if (_guardian.Pattern == GuardianPattern.Sweep || _guardian.Pattern == GuardianPattern.PlazaSlam ||
                _guardian.Pattern == GuardianPattern.ShieldPush || _guardian.Pattern == GuardianPattern.LineFracture)
            {
                foreach (var member in living) if (_activeTelegraph != null && _activeTelegraph.IsPointInsideXZ(member.position))
                {
                    float damage = _guardian.Pattern == GuardianPattern.Sweep ? Tuning.sweepDamage :
                        _guardian.Pattern == GuardianPattern.PlazaSlam ? Tuning.slamDamage :
                        _guardian.Pattern == GuardianPattern.ShieldPush ? Tuning.shieldDamage : Tuning.lineDamage;
                    GuardianHit(member, damage);
                    if (_guardian.Pattern == GuardianPattern.ShieldPush && CombatRange.LivingPartyMember(member))
                    {
                        var player=member.GetComponent<PlayerController>();
                        if(player!=null)player.StartKnockbackXZ(_guardianOrigin, Tuning.shieldKnockback);
                        else member.GetComponent<PartyBotAI>()?.StartKnockbackXZ(_guardianOrigin, Tuning.shieldKnockback);
                    }
                }
            }
            else if (_guardian.Pattern == GuardianPattern.FallingStone)
            {
                foreach (var circle in _guardianGround) foreach (var member in living)
                    if (circle != null && circle.IsPointInsideXZ(member.position)) GuardianHit(member, Tuning.stoneDamage);
            }
            else if (_guardian.Pattern == GuardianPattern.Resonance)
            {
                Vector3 center = _guardianVictim.position;
                var recipients = living.FindAll(m => BattleResurrection.FlatDistance(m.position, center) <= Tuning.resonanceRadius);
                float damage = GuardianNormalState.SharedDamage(Tuning.resonanceDamage, recipients.Count);
                foreach (var member in recipients) GuardianHit(member, damage);
                Debug.Log($"[Guardian] resonance recipients={recipients.Count} each={damage:F1}");
            }
            else if (_guardian.Pattern == GuardianPattern.Overload)
            {
                foreach (var member in living) GuardianHit(member, Tuning.overloadFailureDamage);
                Debug.Log($"[Guardian] overload failed stagger={_guardian.Stagger:F1}/{Tuning.staggerRequired}");
            }
            if (GuardianHard && BeginGuardianHardFollowup(now)) return;
            EndGuardian(now);
        }

        private void TickGuardianDash(float now)
        {
            Vector3 previous = transform.position;
            float t = Mathf.InverseLerp(_guardian.Started, _guardian.Ends, now);
            transform.position = Vector3.Lerp(_guardianOrigin, _guardianEnd, t);
            float travelled = Vector3.Distance(previous, transform.position);
            foreach (var member in GetLivingPartyMemberTransforms())
            {
                Vector3 delta = member.position - previous;
                float along = Vector3.Dot(delta, _guardianForward);
                if (!_guardianDashHits.Contains(member) && GuardianNormalState.InRectangle(Vector3.Dot(delta, Vector3.Cross(Vector3.up, _guardianForward)), along,
                    Tuning.chargeHalfWidth, travelled))
                { _guardianDashHits.Add(member); GuardianHit(member, Tuning.chargeDamage); }
            }
            if (_guardian.Due(now))
            {
                if (GuardianHard && _guardian.Pattern == GuardianPattern.FullLockdown) FinishFullLockdownCharge(now);
                else EndGuardian(now);
            }
        }
        private void GuardianHit(Transform member, float amount, CombatDamageKind kind = CombatDamageKind.Area)
        {
            if (!CombatRange.LivingPartyMember(member)) return;
            _guardianPatternHits++;
            _guardianHits[(int)_guardian.Pattern]++;
            DamagePartyMember(member, amount, kind);
        }
        private int _guardianPatternHits;
        private Transform GuardianLineTarget(List<Transform> members)
        {
            var eligible = members.FindAll(m => m.GetComponent<PartyLife>()?.IsTank != true);
            if (eligible.Count == 0) eligible = members;
            return eligible.Count == 0 ? null : eligible[Random.Range(0, eligible.Count)];
        }
        private static float BoundedGuardianLineLength(Vector3 origin, Vector3 forward, float requested)
        {
            Vector3 flat = new Vector3(origin.x, 0, origin.z);
            float dot = Vector3.Dot(flat, forward);
            float radius = 17f;
            float boundary = -dot + Mathf.Sqrt(Mathf.Max(0, dot * dot + radius * radius - flat.sqrMagnitude));
            return Mathf.Max(.1f, Mathf.Min(requested, boundary));
        }
        private void TrackGuardianLine()
        {
            if (_guardianVictim == null) return;
            Vector3 direction = _guardianVictim.position - _guardianOrigin; direction.y = 0;
            if (direction.sqrMagnitude < .01f) return;
            _guardianForward = direction.normalized;
            if (_activeTelegraph != null)
            {
                _activeTelegraph.transform.rotation = Quaternion.LookRotation(_guardianForward);
                // The visible segment and later hit both end at the arena boundary.
                _activeTelegraph.SetRectangleLength(BoundedGuardianLineLength(_guardianOrigin, _guardianForward,
                    GuardianHard && (_guardian.Pattern == GuardianPattern.Lockdown || _guardian.Pattern == GuardianPattern.FullLockdown)
                        ? guardianHard.wallRadius * 2 : Tuning.lineLength));
            }
        }
        private void PlayGuardianLockCue()
        {
            if (_guardianAudio != null && _guardianLockSound != null) _guardianAudio.PlayOneShot(_guardianLockSound);
        }
        private void EndGuardian(float now)
        {
            Debug.Log($"[Guardian] end {_guardian.Pattern}/{_guardian.Step} t={now:F2} hits={_guardianPatternHits}");
            ClearGuardianMechanics(); CleanupActiveTelegraphAndTether();
            _guardian.Rest(now, Tuning); fsmState = BossFSMState.IdleChasing;
            _currentCastSkillName = ""; _castRemainingTime = 0; _autoAttackTimer = 0;
        }
        private void SyncGuardianTiming()
        { _guardianStarted = _guardian.Started; _guardianEnds = _guardian.Ends; _guardianStagger = _guardian.Stagger; _castRemainingTime = _guardian.Remaining(GuardianNow); }
        public bool GuardianCounterSucceeded()
        {
            if (!GuardianNormal || RaidCombatAuthority.Replica || !_guardian.Counter(GuardianNow)) return false;
            if (GuardianHard && _guardian.Pattern == GuardianPattern.FullLockdown)
            { FinishFullLockdownCharge(GuardianNow); return true; }
            EnterGuardianGroggy("카운터 성공 · 6초 그로기"); return true;
        }
        public void AddGuardianStagger(float points)
        {
            if (!GuardianNormal || RaidCombatAuthority.Replica || StageManager.CombatSuspended || currentHp <= 0) return;
            if (_guardian.AddStagger(points, GuardianNow, Tuning)) EnterGuardianGroggy("무력화 성공 · 8초 그로기");
            SyncGuardianTiming();
        }
        private void EnterGuardianGroggy(string message)
        {
            ClearGuardianMechanics(); CleanupActiveTelegraphAndTether();
            fsmState = BossFSMState.Groggy; _groggyRemainingTime = _guardian.Remaining(GuardianNow);
            SyncGuardianTiming();
            _currentCastSkillName = message; SetMechanicAlert(message, Color.yellow, _groggyRemainingTime);
            Debug.Log($"[Guardian] {message} t={GuardianNow:F2} until={_guardian.Ends:F2}");
        }
        private void ClearGuardianMechanics()
        {
            ClearGuardianHard();
            SetGuardianKneeling(false);
            foreach (var aoe in _guardianGround) if (aoe != null) { aoe.gameObject.SetActive(false); Destroy(aoe.gameObject); }
            _guardianGround.Clear(); _guardianDestinations.Clear(); _guardianDashHits.Clear(); _guardianVictim = null;
            var counter = GetComponent<CombatCounterTarget>(); if (counter != null) counter.CloseWindow();
            _guardianSignal = false; if (_guardianWeakness != null) _guardianWeakness.gameObject.SetActive(false);
        }

        // Stable destinations per visible stage of the cast. No following another AI's moving destination.
        public bool GuardianDestination(Transform actor, int role, out Vector3 destination)
        {
            destination = actor.position;
            if (!GuardianNormal || _guardian.Step == GuardianStep.Rest || _guardian.Step == GuardianStep.Groggy ||
                _guardian.Step == GuardianStep.StaggerCheck || _guardian.Step == GuardianStep.Recovery ||
                _guardian.Pattern == GuardianPattern.Transformation) return false;
            float reaction = _guardian.Step == GuardianStep.Locked ? Tuning.aiLockReactionSeconds : Tuning.aiReactionSeconds;
            if (GuardianNow - _guardian.Started < reaction) return true;
            if (GuardianHard && GuardianHardDestination(actor, role, out destination)) return true;
            if (_guardian.Pattern == GuardianPattern.LineFracture && _guardian.Step == GuardianStep.Telegraph && actor != _guardianVictim)
            {
                if (_activeTelegraph != null && _activeTelegraph.IsPointInsideXZ(actor.position))
                { destination = GuardianLineEscape(actor); return true; }
                return true; // Wait outside the currently visible line; reevaluate if it turns.
            }
            if (_guardianDestinations.TryGetValue(actor, out destination)) return true;
            Vector3 offset = actor.position - _guardianOrigin; offset.y = 0;
            if (offset.sqrMagnitude < .01f) offset = -_guardianForward;
            if (_guardian.Pattern == GuardianPattern.Sweep)
                destination = _guardianOrigin - _guardianSide * (CombatRadius + 2) + _guardianForward * ((role - 2.5f) * 1.4f);
            else if (_guardian.Pattern == GuardianPattern.ShieldPush)
            {
                if (_activeTelegraph == null || !_activeTelegraph.IsPointInsideXZ(actor.position)) return true;
                Vector3 side=Vector3.Cross(Vector3.up,_guardianForward);
                float sign=Vector3.Dot(offset,side)>=0?1:-1;
                destination=_guardianOrigin + side*sign*(CombatRadius+2.5f) - _guardianForward*1.5f;
            }
            else if (_guardian.Pattern == GuardianPattern.LineFracture)
            {
                if (_guardian.Step == GuardianStep.Telegraph)
                {
                    // The marked member places the future line away from the nearest teammate.
                    Vector3 outward=offset.normalized;
                    if(outward.sqrMagnitude<.01f)outward=_guardianForward;
                    destination=CombatRange.ClampToArena(_guardianOrigin+outward*12,16.8f);
                }
                else destination=GuardianLineEscape(actor);
            }
            else if (_guardian.Pattern == GuardianPattern.PlazaSlam)
                {
                destination = _guardianOrigin + offset.normalized * (Tuning.slamRadius + 1.5f);
                if (CombatRange.ClampToArena(destination,16.8f) != destination)
                    destination = _guardianOrigin + (-new Vector3(_guardianOrigin.x,0,_guardianOrigin.z)).normalized * (Tuning.slamRadius + 1.5f);
            }
            else if (_guardian.Pattern == GuardianPattern.FallingStone)
            {
                if (_guardian.Step == GuardianStep.Telegraph)
                {
                    var members = PartyLife.OrderedMembers(); int slot = Mathf.Max(0, members.IndexOf(actor));
                    float angle = (slot * 90 + 45) * Mathf.Deg2Rad;
                    destination = new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * 11;
                }
                else destination = FindGuardianSafePoint(actor, role);
            }
            else if (_guardian.Pattern == GuardianPattern.Resonance)
            {
                if (actor == _guardianVictim) destination = actor.position;
                else if (_guardianVictim != null)
                {
                    // The marked AI stays still; a human recipient may move, so refresh this one bounded target.
                    float angle = role * Mathf.PI * .5f;
                    destination = _guardianVictim.position + new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * .8f;
                    destination.y = actor.position.y; return true;
                }
            }
            else if (_guardian.Pattern == GuardianPattern.GateCharge)
            {
                bool counter = Tuning.aiCounters && GuardianCounterRank(actor) >= 0 && _guardian.Step != GuardianStep.Dash;
                destination = counter ? _guardianOrigin + _guardianForward * (CombatRadius + .65f) :
                    _guardianOrigin + _guardianForward * Vector3.Dot(offset, _guardianForward) + Vector3.Cross(Vector3.up, _guardianForward) * (role % 2 == 0 ? -1 : 1) * (Tuning.chargeHalfWidth + 1.5f);
            }
            else return false;
            destination = CombatRange.ClampToArena(destination, 16.8f); destination.y = actor.position.y;
            _guardianDestinations[actor] = destination; return true;
        }
        private Vector3 GuardianLineEscape(Transform actor)
        {
            Vector3 offset=actor.position-_guardianOrigin; offset.y=0;
            float along=Vector3.Dot(offset,_guardianForward);
            Vector3 side=Vector3.Cross(Vector3.up,_guardianForward);
            float lateral=Vector3.Dot(offset,side);
            float clearance=Tuning.lineWidth*.5f+CombatRange.Radius(actor)+.8f;
            if (along<0 || along>BoundedGuardianLineLength(_guardianOrigin,_guardianForward,Tuning.lineLength) ||
                Mathf.Abs(lateral)>=clearance) return actor.position;
            float sign=lateral>=0?1:-1;
            Vector3 candidate=CombatRange.ClampToArena(actor.position+side*(sign*(clearance-Mathf.Abs(lateral))),16.8f);
            if (_activeTelegraph != null && _activeTelegraph.IsPointInsideXZ(candidate))
                candidate=CombatRange.ClampToArena(actor.position-side*(sign*(clearance+Mathf.Abs(lateral))),16.8f);
            candidate.y=actor.position.y;return candidate;
        }
        private Vector3 FindGuardianSafePoint(Transform actor, int role)
        {
            Vector3 best = actor.position; float score = float.MaxValue;
            for (int i = 0; i < 48; i++)
            {
                float angle = i * Mathf.PI / 24;
                Vector3 candidate = CombatRange.ClampToArena(actor.position + new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle)) * (Tuning.stoneRadius + 1.5f), 16.8f);
                bool safe = true;
                foreach (var circle in _guardianGround) if (circle != null && BattleResurrection.FlatDistance(candidate, circle.transform.position) < Tuning.stoneRadius + .7f) safe = false;
                float cost = (candidate - actor.position).sqrMagnitude + .01f * ((i + role * 9) % 48);
                if (safe && cost < score) { score = cost; best = candidate; }
            }
            return best;
        }
        public int GuardianCounterRank(Transform actor)
        {
            var candidates = GetLivingPartyMemberTransforms().FindAll(t => t.GetComponent<PartyBotAI>() != null || t.GetComponent<NetworkRaidAvatar>()?.Automated == true);
            candidates.Sort((a,b) => CounterPriority(a).CompareTo(CounterPriority(b)));
            int rank = candidates.IndexOf(actor); return rank < 2 ? rank : -1;
        }
        private static int CounterPriority(Transform actor)
        {
            var bot = actor.GetComponent<PartyBotAI>(); int role = bot != null ? (int)bot.role + 1 : actor.GetComponent<NetworkRaidAvatar>().Role;
            return role == 3 ? 0 : role == 4 ? 1 : role == 1 ? 2 : 3;
        }
        public bool GuardianAiCounterReady(Transform actor) => GuardianNormal && Tuning.aiCounters && GuardianStepNow == GuardianStep.CounterWindow &&
            GuardianCounterRank(actor) >= 0 && GuardianNow - _guardian.Started >= Tuning.aiReactionSeconds + GuardianCounterRank(actor) * Tuning.aiBackupDelay;

        private void AssignGuardianSpread(List<Transform> members)
        {
            // Match visible living members to separated slots by shortest total travel (at most 4!).
            var slots = new Vector3[4]; for(int i=0;i<4;i++){float a=(i*90+45)*Mathf.Deg2Rad;slots[i]=new Vector3(Mathf.Sin(a),0,Mathf.Cos(a))*8;}
            float best=float.MaxValue; int[] choice=new int[members.Count], selected=new int[members.Count];
            System.Action<int,int,float> assign=null;
            assign=(n,mask,cost)=>{if(n==members.Count){if(cost<best){best=cost;System.Array.Copy(choice,selected,n);}return;}
                for(int i=0;i<4;i++)if((mask&(1<<i))==0){choice[n]=i;assign(n+1,mask|(1<<i),cost+BattleResurrection.FlatDistance(members[n].position,slots[i]));}};
            assign(0,0,0);
            for(int i=0;i<members.Count;i++){var point=slots[selected[i]];point.y=members[i].position.y;_guardianDestinations[members[i]]=point;}
        }
        private void SetGuardianKneeling(bool kneel)
        {
            var body=GetBossBodyTransform(); if(body==null || !Tuning.temporaryStatueVisuals || kneel==_guardianKneeling)return;
            if(kneel){_guardianBodyPosition=body.localPosition;_guardianBodyRotation=body.localRotation;body.localPosition+=Vector3.down*.6f;body.localRotation*=Quaternion.Euler(22,0,0);}
            else {body.localPosition=_guardianBodyPosition;body.localRotation=_guardianBodyRotation;}
            _guardianKneeling=kneel;
        }
        private void OnDestroy()
        {
            if(_guardianDecor!=null)Destroy(_guardianDecor);
            if(_guardianCounterSound!=null)Destroy(_guardianCounterSound);
            if(_guardianLockSound!=null)Destroy(_guardianLockSound);
        }
        private void BuildGuardianPresentation()
        {
            // Replaceable additions on the existing visual body; authored Timeline/camera assets are untouched.
            var body = GetBossBodyTransform(); if (!Tuning.temporaryStatueVisuals || body == null || _guardianClub != null) return;
            _guardianClub = GuardianShape("Guardian temporary stone club", body, new Vector3(1.25f,.5f,0), new Vector3(.23f,1.4f,.23f), new Color(.48f,.46f,.42f));
            _guardianShield = GuardianShape("Guardian temporary shield", body, new Vector3(-1.1f,.1f,.5f), new Vector3(.7f,1,.2f), new Color(.36f,.4f,.38f));
            _guardianCore = GuardianShape("Guardian exposed core", body, new Vector3(0,.35f,1), Vector3.one * .25f, new Color(.65f,.2f,.9f));
            _guardianWeakness = GuardianShape("Guardian counter weakness", body, new Vector3(0,.8f,1), new Vector3(.45f,.15f,.15f), Color.cyan);
            _guardianCore.gameObject.SetActive(false); _guardianWeakness.gameObject.SetActive(false);
            _guardianAudio = gameObject.AddComponent<AudioSource>(); _guardianAudio.playOnAwake = false;
            _guardianAudio.spatialBlend = 0; _guardianAudio.volume = .25f;
            _guardianCounterSound = AudioClip.Create("Guardian counter cue", 4410, 1, 22050, false);
            var samples = new float[4410]; for (int i=0;i<samples.Length;i++) samples[i] = Mathf.Sin(i * 2 * Mathf.PI * 880 / 22050) * (1f-i/(float)samples.Length) * .2f;
            _guardianCounterSound.SetData(samples,0);
            _guardianLockSound = AudioClip.Create("Guardian line lock cue", 4410, 1, 22050, false);
            for (int i=0;i<samples.Length;i++) samples[i] = Mathf.Sin(i * 2 * Mathf.PI * 330 / 22050) * (1f-i/(float)samples.Length) * .22f;
            _guardianLockSound.SetData(samples,0);
            _guardianDecor = new GameObject("Guardian plaza cardinal motifs");
            string[] labels = { "북 N", "동 E", "남 S", "서 W" };
            for (int i=0;i<4;i++)
            {
                var mark = new GameObject(labels[i]); mark.transform.SetParent(_guardianDecor.transform);
                float angle=i*Mathf.PI*.5f; mark.transform.position = new Vector3(Mathf.Sin(angle)*15.5f,.09f,Mathf.Cos(angle)*15.5f);
                mark.transform.rotation=Quaternion.Euler(90,0,0);
                var text=mark.AddComponent<TextMesh>(); text.text=labels[i];text.anchor=TextAnchor.MiddleCenter;text.characterSize=.35f;text.fontSize=32;text.color=new Color(.8f,.74f,.5f);
            }
        }
        private static Transform GuardianShape(string name, Transform parent, Vector3 position, Vector3 scale, Color color)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube); go.name=name;go.transform.SetParent(parent,false);
            go.transform.localPosition=position;go.transform.localScale=scale;Object.Destroy(go.GetComponent<Collider>());
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));material.color=color;
            go.GetComponent<Renderer>().material=material;return go.transform;
        }
        private void UpdateGuardianPresentation()
        {
            bool visible=GuardianNormal && (RaidApproachField.Instance == null || RaidApproachField.Instance.BossVisible);
            if (_guardianDecor != null) _guardianDecor.SetActive(GuardianNormal && RaidApproachField.Instance?.Showing == true);
            if (_guardianClub == null) return;
            bool active=visible && !StageManager.CombatSuspended && currentHp>0;
            bool signal=active && GuardianStepNow==GuardianStep.CounterWindow && GetComponent<CombatCounterTarget>()?.WindowOpen==true;
            _guardianWeakness.gameObject.SetActive(signal);
            _guardianCore.gameObject.SetActive(active && GuardianStepNow==GuardianStep.StaggerCheck);
            _guardianClub.gameObject.SetActive(visible); _guardianShield.gameObject.SetActive(visible);
            if(signal&&!_guardianSignal&&_guardianAudio!=null)_guardianAudio.PlayOneShot(_guardianCounterSound);
            _guardianSignal=signal;
            float progress=active && GuardianStepNow==GuardianStep.Telegraph ?
                1f-Mathf.Clamp01((_guardianEnds-GuardianNow)/Mathf.Max(.01f,_guardianEnds-_guardianStarted)) : 1f;
            float pose=active&&(GuardianPatternNow==GuardianPattern.Punishment||GuardianPatternNow==GuardianPattern.PlazaSlam||
                GuardianPatternNow==GuardianPattern.LineFracture)?Mathf.Lerp(0,-70,progress):0;
            if(active&&GuardianPatternNow==GuardianPattern.Sweep)pose=(Vector3.Dot(_guardianSide,transform.right)>0?65:-65)*progress;
            _guardianClub.localRotation=Quaternion.Euler(pose,0,GuardianPatternNow==GuardianPattern.Sweep?pose:0);
            _guardianShield.localRotation=Quaternion.Euler(active&&GuardianPatternNow==GuardianPattern.ShieldPush?Mathf.Lerp(0,-65,progress):0,
                GuardianPatternNow==GuardianPattern.GateCharge?35*progress:0,0);
        }
        private void GuardianUI()
        {
            if (!GuardianNormal || currentHp <= 0) return;
            if(IsGroggy && bossCastFillImage != null) bossCastFillImage.fillAmount = Mathf.Clamp01((_guardianEnds-GuardianNow)/Mathf.Max(.1f,_guardianEnds-_guardianStarted));
            if(bossPhaseText!=null)bossPhaseText.text=IsGroggy? _currentCastSkillName : GuardianHard ?
                (_guardianHard.Transformed ? "오염된 마을 수호자 · 하드" : "마을 수호 석상 · 하드") :
                "마을 수호 석상 · 노멀 / 반면 회피 · 위치 유도 · 집합 · 카운터";
            if(GuardianStaggerActive)
            {
                if(bossCastFillImage!=null){bossCastFillImage.fillAmount=Mathf.Clamp01(_guardianStagger/Mathf.Max(1,Tuning.staggerRequired));bossCastFillImage.color=new Color(.7f,.2f,1);}
                if(bossCastText!=null)bossCastText.text=$"수호핵 무력화 {_guardianStagger:F0}/{Tuning.staggerRequired:F0} · {_castRemainingTime:F1}초";
            }
        }
    }
}
