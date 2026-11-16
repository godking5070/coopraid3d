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
        public bool GuardianNormal => currentStageNumber == 1;
        public bool GuardianHard => GuardianNormal && CampaignRuntime.Instance?.DifficultyId == CampaignCatalog.Hard;
        private GuardianNormalSettings Tuning => GuardianHard ? guardianHard.combat : guardian;
        private readonly GuardianNormalState _guardian = new GuardianNormalState();
        private readonly GuardianHardState _guardianHard = new GuardianHardState();
        private GuardianPattern _guardianReplicaPattern;
        private GuardianStep _guardianReplicaStep;
        public GuardianPattern GuardianPatternNow => RaidCombatAuthority.Replica ? _guardianReplicaPattern : _guardian.Pattern;
        public GuardianStep GuardianStepNow => RaidCombatAuthority.Replica ? _guardianReplicaStep : _guardian.Step;
        private float _guardianStarted, _guardianEnds, _guardianStagger;
        private bool _guardianGeneralStagger;
        private bool GuardianRequiredStaggerPending => GuardianHard && _guardianHard.HasPendingMilestone(HpRatio);
        public bool GuardianStaggerLocked => !GuardianNormal || currentHp<=0 || StageManager.CombatSuspended ||
            _guardian.Step==GuardianStep.Groggy || _guardian.Step==GuardianStep.Stopped || GuardianRequiredStaggerPending ||
            _guardian.Pattern==GuardianPattern.Lockdown || _guardian.Pattern==GuardianPattern.Transformation ||
            _guardian.Pattern==GuardianPattern.FullLockdown;
        public bool TryGuardianStaggerGroggy(float seconds)
        {
            if(RaidCombatAuthority.Replica||GuardianStaggerLocked||float.IsNaN(seconds)||float.IsInfinity(seconds)||seconds<=0)return false;
            NotifyDamageCastEnded();
            ClearHardStoneRain();
            _guardianCombatStarted=true;
            _guardian.Enter(GuardianStep.Groggy,GuardianNow,seconds);
            EnterGuardianGroggy("무력화 성공");
            _guardianGeneralStagger=true;
            _castRemainingTime=_castTotalDuration=0;
            return true;
        }
        private int _guardianLineTrackFrame = -1;
        private Vector3 _guardianOrigin, _guardianForward, _guardianEnd, _guardianSide;
        private Transform _guardianVictim;
        private readonly List<AoETelegraph> _guardianGround = new List<AoETelegraph>();
        private readonly Dictionary<Transform, Vector3> _guardianDestinations = new Dictionary<Transform, Vector3>();
        private readonly HashSet<Transform> _guardianDashHits = new HashSet<Transform>();
        private Transform _guardianClub, _guardianAttackArm, _guardianShield, _guardianCore, _guardianWeakness;
        private TextMesh _guardianCounterIcon;
        private GameObject _guardianDecor;
        private AudioSource _guardianAudio;
        private AudioClip _guardianCounterSound;
        private AudioClip _guardianClashSound;
        private AudioClip _guardianLockSound;
        private AudioClip _guardianImpactSound;
        private int _guardianImpactSerial;
        private bool _guardianReplicaImpactInitialized;
        private int _guardianLockSerial;
        private bool _guardianReplicaLockInitialized;
        private bool _guardianSignal;
        private bool _guardianCombatStarted, _guardianKneeling, _guardianHardFollowup, _hardResonancePushed;
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
            _guardianLineTrackFrame = -1;
            guardian = guardian ?? new GuardianNormalSettings();
            guardianHard = guardianHard ?? new GuardianHardSettings();
            _guardianHard.Reset(); ClearHardStoneRain(); ClearGuardianMechanics(); ClearHardCorruption(); _guardian.Reset(GuardianNow, Tuning); _guardianCombatStarted = false;
            System.Array.Clear(_guardianUses,0,_guardianUses.Length); System.Array.Clear(_guardianHits,0,_guardianHits.Length);
            if (!GuardianNormal) return;
            if (GuardianHard) guardianHard.EnsureDefaults();
            else { guardian.MigrateInitialTuning(); guardian.EnsureTempoDefaults(); }
            currentBossTitle = GuardianHard ? "STAGE 1 : 마을 수호 석상 · 하드" : "STAGE 1 : 마을 수호 석상 · 노멀";
            maxHp = RaidBossHealthTuning.Resolve(Tuning.health, GuardianHard); currentHp = maxHp;
            arenaBoundaryRadius = 18; autoAttackDamageToTank = Tuning.autoDamage;
            _stagePhase1BodyColor = new Color(.47f, .49f, .45f);
            SetBossBodyColor(_stagePhase1BodyColor);
            BuildGuardianPresentation();
        }

        private void TickGuardian()
        {
            float now = GuardianNow;
            if (!_guardianCombatStarted) { _guardianCombatStarted = true; _guardian.Reset(now, Tuning); }
            if(_guardianGeneralStagger && GuardianRequiredStaggerPending)
            { EndGuardian(now);_guardian.Enter(GuardianStep.Rest,now,0); }
            // Once lockdown has started, the 40% phase change takes
            // priority over casts, groggy and recovery rather than waiting for Rest.
            if (GuardianHard && _guardianHard.TryStartTransformation(HpRatio))
                BeginGuardian(GuardianPattern.Transformation, now);
            TickHardStoneRain(now);
            if(currentHp<=0||StageManager.CombatSuspended)return;
            if (GuardianHard) TickHardSlamLure(now);
            if (GuardianHard) TickHardImpactWarning(now);
            _castRemainingTime = _guardian.Remaining(now);
            if (_guardian.Step == GuardianStep.Rest)
            {
                fsmState = BossFSMState.IdleChasing;
                // Resolve the queued threshold before another autoattack or pattern can begin.
                if (HardStoneRainActive)
                { RotateTowardsAggroTargetXZ(); UpdateMeleePursuit(Time.deltaTime); UpdateAutoAttack(); }
                else if (GuardianHard && _guardian.Due(now))
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
                if (GuardianHard && _guardian.Pattern == GuardianPattern.Transformation) TickGuardianHard(now);
                else if (_guardian.Due(now)) EndGuardian(now);
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
                    BeginGuardian(GuardianPattern.Resonance, now, true); // New recipient gets a full cast without another push.
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
            GuardianPattern.ShieldPush => "방패 충격파", GuardianPattern.LineFracture => "돌봉 직선 파쇄",
            GuardianPattern.Overload => "수호핵 과부하", GuardianPattern.Lockdown => "봉쇄 명령",
            GuardianPattern.Transformation => "거대 가고일 변신 · 힘겨루기", GuardianPattern.TwistedExecution => "뒤틀린 집행",
            GuardianPattern.AerialCharge => "가고일 공중 도약 · 외곽 연속 돌진",
            GuardianPattern.FullLockdown => "완전 봉쇄", _ => "회복 · 재정렬"
        };
        private void BeginGuardian(GuardianPattern pattern, float now, bool resonanceRetarget = false)
        {
            ClearGuardianMechanics(); CleanupActiveTelegraphAndTether();
            _guardian.Begin(pattern, now, Tuning);
            _guardianHardFollowup = false;
            if (!resonanceRetarget) _hardResonancePushed = false;
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
                    if (GuardianHard)
                    {
                        _currentCastSkillName = "침입자 징벌 · 대상 2연타 (1/2)";
                        _activePartyMarkers.Add(AttachedPartyMechanicMarker.SpawnTargetMarker(_guardianVictim, .8f));
                    }
                    else _activeTelegraph = AoETelegraph.Spawn(AoETelegraphType.Circle_Radius6, _guardianVictim.position, Vector3.forward, _castTotalDuration, 0, .8f);
                    SetMechanicAlert(GuardianHard ? "침입자 징벌 · 같은 대상 2회 강타 / 방어와 회복" :
                        "침입자 징벌 · 붉은 표식 대상 방어 / 회복 준비", Color.red, _castTotalDuration);
                    break;
                case GuardianPattern.Sweep:
                    if (!GuardianHard) _activeTelegraph = AoETelegraph.Spawn(AoETelegraphType.FrontCone_120Deg, _guardianOrigin, _guardianSide, _castTotalDuration, 0, 36, 180);
                    if (GuardianHard) _currentCastSkillName = Vector3.Dot(_guardianSide, transform.right) < 0
                        ? "돌봉 휩쓸기 · 왼쪽 → 오른쪽" : "돌봉 휩쓸기 · 오른쪽 → 왼쪽";
                    SetMechanicAlert(GuardianHard ? _currentCastSkillName + " / 첫 타격 뒤 경계 교차" :
                        "돌봉 휩쓸기 · 표시된 반면의 반대쪽으로!", Color.yellow, _castTotalDuration);
                    break;
                case GuardianPattern.PlazaSlam:
                    if (!GuardianHard) _activeTelegraph = AoETelegraph.Spawn(AoETelegraphType.Circle_Radius6, _guardianOrigin, _guardianForward, _castTotalDuration, 0, Tuning.slamRadius);
                    if (GuardianHard) _currentCastSkillName = "광장 내려찍기 · 중심 → 외곽";
                    SetMechanicAlert(GuardianHard ? "광장 내려찍기 · 중심 직후 외곽 고리 / 경계 밖에서 안으로" :
                        "광장 내려찍기 · 고정 중심 8m 밖으로!", Color.yellow, _castTotalDuration);
                    if(GuardianHard)BeginHardSlamLure(members);
                    break;
                case GuardianPattern.FallingStone:
                    // The cast starts a stream of fixed snapshots, not a follow/lock/wait cycle.
                    break;
                case GuardianPattern.Resonance:
                    if (members.Count == 0) { EndGuardian(now); return; }
                    _guardianVictim = members[Random.Range(0, members.Count)];
                    _stackTarget = _guardianVictim; _isStackActive = true; stackRadius = Tuning.resonanceRadius;
                    _activeStackMarker = AttachedPartyMechanicMarker.SpawnStackMarker(_guardianVictim, stackRadius);
                    _activePartyMarkers.Add(_activeStackMarker);
                    if (GuardianHard && !_hardResonancePushed)
                    {
                        _hardResonancePushed = true;
                        foreach (var member in members)
                        {
                            var player = member.GetComponent<PlayerController>();
                            if (player != null) player.StartKnockbackXZ(_guardianOrigin, guardianHard.resonanceKnockback);
                            else member.GetComponent<PartyBotAI>()?.StartKnockbackXZ(_guardianOrigin, guardianHard.resonanceKnockback);
                        }
                    }
                    SetMechanicAlert(GuardianHard ? "방패 공명 · 밀쳐냄 후 노란 원으로 집합!" :
                        "방패 공명 · 노란 원 안으로 집합! 피해 분담", Color.yellow, _castTotalDuration);
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
                    SetMechanicAlert("성문 쇄도 준비 · ⚔ 금색 약점 때 [" + LocalInputBindings.Label("Counter") + "] 카운터 / 실패 시 옆으로", Color.yellow, _castTotalDuration + Tuning.counterWindowSeconds);
                    break;
                case GuardianPattern.ShieldPush:
                    if(!GuardianHard)SpawnGuardianShieldWave(_castTotalDuration);
                    break;
                case GuardianPattern.LineFracture:
                    _guardianVictim = GuardianLineTarget(members);
                    if (_guardianVictim == null) { EndGuardian(now); return; }
                    TrackGuardianLine();
                    _activePartyMarkers.Add(GuardianHard
                        ? AttachedPartyMechanicMarker.SpawnTargetMarker(_guardianVictim, .75f)
                        : AttachedPartyMechanicMarker.SpawnSpreadMarker(_guardianVictim, .75f));
                    _activeTelegraph = AoETelegraph.Spawn(AoETelegraphType.Rectangle, _guardianOrigin,
                        _guardianForward, _castTotalDuration, Tuning.lineWidth * .5f,
                        BoundedGuardianLineLength(_guardianOrigin, _guardianForward, Tuning.lineLength));
                    if (GuardianHard) _activeTelegraph.SetGuideStyle(false);
                    SetMechanicAlert("돌봉 직선 파쇄 · 표적 추적 중, 고정되면 옆으로!", GuardianHard ? Color.yellow : Color.magenta,
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

        private void SpawnGuardianShieldWave(float seconds)
        {
            Vector3 side=Vector3.Cross(Vector3.up,_guardianForward);
            _activeTelegraph=AoETelegraph.Spawn(AoETelegraphType.Rectangle,_guardianOrigin-_guardianForward*Tuning.shieldRange,
                _guardianForward,seconds,Tuning.shieldCrossHalfWidth,Tuning.shieldRange*2);
            _guardianGround.Add(AoETelegraph.Spawn(AoETelegraphType.Rectangle,_guardianOrigin-side*Tuning.shieldRange,
                side,seconds,Tuning.shieldCrossHalfWidth,Tuning.shieldRange*2));
        }
        private bool GuardianShieldWaveHits(Vector3 point,float padding=0)
        {
            Vector3 offset=point-_guardianOrigin,side=Vector3.Cross(Vector3.up,_guardianForward);
            return GuardianShieldWaveGeometry.Hits(Vector3.Dot(offset,side),Vector3.Dot(offset,_guardianForward),
                Tuning.shieldCrossHalfWidth+padding,Tuning.shieldRange+padding);
        }
        private Vector3 GuardianShieldWaveSafePoint(Transform actor)
        {
            Vector3 side=Vector3.Cross(Vector3.up,_guardianForward),best=actor.position;
            float distance=Mathf.Max(Tuning.shieldCrossHalfWidth+CombatRange.Radius(actor)+.6f,(CombatRadius+1.2f)*.707107f);
            float cost=float.MaxValue;
            for(int i=0;i<4;i++)
            {
                Vector3 point=CombatRange.ClampToArena(_guardianOrigin+side*(i%2==0?distance:-distance)+
                    _guardianForward*(i<2?distance:-distance),16.8f);
                if(GuardianShieldWaveHits(point,CombatRange.Radius(actor)+.3f))continue;
                float candidate=(point-actor.position).sqrMagnitude;
                if(candidate<cost){cost=candidate;best=point;}
            }
            best.y=actor.position.y;return best;
        }
        private void AdvanceGuardian(float now)
        {
            if (_guardian.Pattern == GuardianPattern.FallingStone)
            { StartHardStoneRain(now); EndGuardian(now); return; }
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
                if (GuardianHard) _activeTelegraph.SetGuideStyle(true);
                else
                {
                    _activeTelegraph.baseFillColor = new Color(1f,.48f,.07f,.45f);
                    _activeTelegraph.borderColor = new Color(1f,.7f,.12f,1f);
                    _activeTelegraph.RefreshColors();
                }
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
            if (GuardianHard && (_guardian.Pattern == GuardianPattern.Sweep || _guardian.Pattern == GuardianPattern.PlazaSlam ||
                _guardian.Pattern == GuardianPattern.Punishment || _guardian.Pattern == GuardianPattern.Resonance ||
                _guardian.Pattern == GuardianPattern.ShieldPush || _guardian.Pattern == GuardianPattern.LineFracture)) PlayHardImpactCue();
            if (_guardian.Pattern == GuardianPattern.Punishment)
                GuardianHit(_guardianVictim, Tuning.punishmentDamage, CombatDamageKind.Direct);
            else if (_guardian.Pattern == GuardianPattern.Sweep || _guardian.Pattern == GuardianPattern.PlazaSlam ||
                _guardian.Pattern == GuardianPattern.ShieldPush || _guardian.Pattern == GuardianPattern.LineFracture)
            {
                foreach (var member in living) if ((_guardian.Pattern==GuardianPattern.ShieldPush?
                    GuardianShieldWaveHits(member.position):_activeTelegraph != null && _activeTelegraph.IsPointInsideXZ(member.position)) &&
                    (!GuardianHard || _guardian.Pattern != GuardianPattern.PlazaSlam || !_guardianHardFollowup || HardRingHits(member.position)))
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
            EndGuardian(now, GuardianHard && (_guardian.Pattern == GuardianPattern.Sweep ||
                _guardian.Pattern == GuardianPattern.PlazaSlam));
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
                bool hit = GuardianHard && _guardian.Pattern == GuardianPattern.AerialCharge
                    ? GuardianHardGeometry.SweptChargeHits(delta.x,delta.z,_guardianForward.x,_guardianForward.z,
                        travelled,Tuning.chargeHalfWidth,CombatRange.Radius(member))
                    : GuardianNormalState.InRectangle(Vector3.Dot(delta, Vector3.Cross(Vector3.up, _guardianForward)), along,
                        Tuning.chargeHalfWidth, travelled);
                if (!_guardianDashHits.Contains(member) && hit)
                { _guardianDashHits.Add(member); GuardianHit(member, Tuning.chargeDamage); }
            }
            if (_guardian.Due(now))
            {
                NotifyDamagePatternMoveEnded();
                if (GuardianHard && _guardian.Pattern == GuardianPattern.FullLockdown) FinishFullLockdownCharge(now);
                else if (GuardianHard && _guardian.Pattern == GuardianPattern.AerialCharge) FinishHardAerialChargePass(now);
                else EndGuardian(now);
            }
        }
        private void GuardianHit(Transform member, float amount, CombatDamageKind kind = CombatDamageKind.Area, string origin = "boss.pattern")
        {
            if (!CombatRange.LivingPartyMember(member)) return;
            _guardianPatternHits++;
            _guardianHits[(int)_guardian.Pattern]++;
            DamagePartyMember(member, amount, kind, origin);
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
            if (GuardianHard)
            {
                // The tracking tick and lock transition can both call this in one frame.
                // Spend the angular budget once and lock the displayed direction as-is.
                if (_guardianLineTrackFrame != Time.frameCount)
                {
                    float currentYaw=Mathf.Atan2(_guardianForward.x,_guardianForward.z)*Mathf.Rad2Deg;
                    float targetYaw=Mathf.Atan2(direction.x,direction.z)*Mathf.Rad2Deg;
                    float yaw=(float)GuardianHardGeometry.TrackYaw(currentYaw,targetYaw,
                        guardianHard.aimTurnDegreesPerSecond,Time.deltaTime);
                    _guardianForward=Quaternion.Euler(0,yaw,0)*Vector3.forward;
                    _guardianLineTrackFrame=Time.frameCount;
                }
                transform.rotation=Quaternion.LookRotation(_guardianForward);
            }
            else _guardianForward = direction.normalized;
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
            if (!RaidCombatAuthority.Replica && GuardianHard) _guardianLockSerial++;
            if (_guardianAudio == null)
            { _guardianAudio = gameObject.AddComponent<AudioSource>(); _guardianAudio.playOnAwake = false; _guardianAudio.spatialBlend = 0; _guardianAudio.volume = .25f; }
            if (_guardianLockSound == null)
            {
                _guardianLockSound = AudioClip.Create("Guardian line lock cue", 4410, 1, 22050, false);
                var samples = new float[4410];
                for (int i = 0; i < samples.Length; i++)
                    samples[i] = Mathf.Sin(i * 2 * Mathf.PI * 330 / 22050) * (1f-i/(float)samples.Length) * .22f;
                _guardianLockSound.SetData(samples, 0);
            }
            if (_guardianAudio != null && _guardianLockSound != null) _guardianAudio.PlayOneShot(_guardianLockSound);
        }
        private void PlayHardImpactCue()
        {
            if (!RaidCombatAuthority.Replica) _guardianImpactSerial++;
            if (_guardianAudio == null)
            { _guardianAudio = gameObject.AddComponent<AudioSource>(); _guardianAudio.playOnAwake = false; _guardianAudio.spatialBlend = 0; _guardianAudio.volume = .25f; }
            if (_guardianImpactSound == null)
            {
                _guardianImpactSound = AudioClip.Create("Guardian temporary stone impact", 6615, 1, 22050, false);
                var samples = new float[6615];
                for (int i = 0; i < samples.Length; i++)
                {
                    float decay = 1f - i / (float)samples.Length;
                    samples[i] = (Mathf.Sin(i * 2 * Mathf.PI * 105 / 22050) +
                        .35f * Mathf.Sin(i * 2 * Mathf.PI * 210 / 22050)) * decay * decay * .32f;
                }
                _guardianImpactSound.SetData(samples, 0);
            }
            _guardianAudio.PlayOneShot(_guardianImpactSound);
        }
        private void EndGuardian(float now, bool retainImpactVisual = false,bool recoveryPaid=false)
        {
            _guardianGeneralStagger=false;
            NotifyDamageCastEnded();
            Debug.Log($"[Guardian] end {_guardian.Pattern}/{_guardian.Step} t={now:F2} hits={_guardianPatternHits}");
            if (retainImpactVisual) FinishHardImpact(now);
            ClearGuardianMechanics(retainImpactVisual); CleanupActiveTelegraphAndTether();
            _guardian.Rest(now, Tuning); fsmState = BossFSMState.IdleChasing;
            if(recoveryPaid)_guardian.Enter(GuardianStep.Rest,now,0);
            _currentCastSkillName = ""; _castRemainingTime = 0; _autoAttackTimer = 0;
        }
        private void SyncGuardianTiming()
        {
            _guardianStarted = _guardian.Started; _guardianEnds = _guardian.Ends; _guardianStagger = _guardian.Stagger;
            _castRemainingTime = _guardian.Remaining(GuardianNow);
            if(GuardianHard && _guardian.Pattern==GuardianPattern.Transformation && _clashPhase>=1 && _clashPhase<=5)
            {
                // Only the opening grants groggy. Hold and post-opening recovery must
                // never inherit the previous frame's FSM state or damage vulnerability.
                bool opening=GuardianClashRules.OpeningActive(_clashPhase,_guardian.Step);
                fsmState=opening?BossFSMState.Groggy:BossFSMState.CastingSkill;
                _groggyRemainingTime=opening?_castRemainingTime:0;
            }
        }
        public bool GuardianCounterSucceeded(Transform actor = null)
        {
            if (!GuardianNormal || RaidCombatAuthority.Replica) return false;
            if (GuardianClashCounterWindow) return GuardianClashCounterSucceeded(actor);
            if (!_guardian.Counter(GuardianNow)) return false;
            EnterGuardianGroggy("카운터 성공 · 2초 그로기"); return true;
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
        private void ClearGuardianMechanics(bool retainImpactVisual = false)
        {
            _guardianGeneralStagger=false;
            if (!retainImpactVisual) ClearHardImpactAfterimage();
            ClearHardAuxImpact();
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
            if (GuardianHard && _guardian.Pattern == GuardianPattern.Transformation && _clashPhase >= 1 && _clashPhase <= 3)
            {
                if (role == 1 && _clashPhase <= 2)
                { destination = _guardianOrigin + _guardianForward * Mathf.Min(CombatRadius + .5f, guardianHard.clashCircleRadius - .4f);
                  destination.y = actor.position.y; return true; }
                return _clashPhase == 3 && actor == _clashTank;
            }
            if (GuardianHardStoneDestination(actor, role, out destination)) return true;
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
                if(!GuardianShieldWaveHits(actor.position,CombatRange.Radius(actor)+.3f))return true;
                destination=GuardianShieldWaveSafePoint(actor);
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
            var candidates = GetLivingPartyMemberTransforms().FindAll(t =>
                (t.GetComponent<PartyBotAI>() != null || t.GetComponent<NetworkRaidAvatar>()?.Automated == true)&&PartyAiTactics.CanTakeCounterPosition(t));
            candidates.Sort((a,b) => CounterPriority(a).CompareTo(CounterPriority(b)));
            int rank = candidates.IndexOf(actor); return rank < 2 ? rank : -1;
        }
        private static int CounterPriority(Transform actor)
        {
            var bot = actor.GetComponent<PartyBotAI>(); int role = bot != null ? (int)bot.role + 1 : actor.GetComponent<NetworkRaidAvatar>().Role;
            return role == 3 ? 0 : role == 4 ? 1 : role == 1 ? 2 : 3;
        }
        public bool GuardianAiCounterReady(Transform actor) => GuardianNormal && Tuning.aiCounters && GuardianStepNow == GuardianStep.CounterWindow &&
            (GuardianClashCounterWindow
                ? GuardianClashCounterAllowed(actor) && GuardianNow - _guardian.Started >= Tuning.aiReactionSeconds
                : GuardianCounterRank(actor) >= 0 && GuardianNow - _guardian.Started >= Tuning.aiReactionSeconds + GuardianCounterRank(actor) * Tuning.aiBackupDelay);

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
        private void OnDisable()
        {
            ClearHardStoneRain();
            ClearHardImpactAfterimage();
            RestoreGargoyleJump();
            StopHardTransformationPresentation();
        }
        private void OnDestroy()
        {
            ClearHardStoneRain();
            ClearHardImpactAfterimage();
            RestoreGargoyleJump();
            StopHardTransformationPresentation();
            ClearClashPresentation();
            if(_guardianDecor!=null)Destroy(_guardianDecor);
            if(_guardianCounterSound!=null)Destroy(_guardianCounterSound);
            if(_guardianClashSound!=null)Destroy(_guardianClashSound);
            if(_guardianLockSound!=null)Destroy(_guardianLockSound);
            if(_guardianImpactSound!=null)Destroy(_guardianImpactSound);
        }
        private void BuildGuardianPresentation()
        {
            // Replaceable additions on the existing visual body; authored Timeline/camera assets are untouched.
            var body = GetBossBodyTransform(); if (!Tuning.temporaryStatueVisuals || body == null || _guardianClub != null) return;
            if (GuardianHard) _guardianAttackArm = GuardianShape("Guardian temporary attack arm", body, new Vector3(1.05f,.55f,0),
                new Vector3(.4f,1.05f,.4f), new Color(.45f,.45f,.42f));
            _guardianClub = GuardianShape("Guardian temporary stone club", body, new Vector3(1.25f,.5f,0), new Vector3(.23f,1.4f,.23f), new Color(.48f,.46f,.42f));
            _guardianShield = GuardianShape("Guardian temporary shield", body, new Vector3(-1.1f,.1f,.5f), new Vector3(.7f,1,.2f), new Color(.36f,.4f,.38f));
            _guardianCore = GuardianShape("Guardian exposed core", body, new Vector3(0,.35f,1), Vector3.one * .25f, new Color(.65f,.2f,.9f));
            _guardianWeakness = GuardianShape("Guardian counter weakness · crossed swords", body, new Vector3(0,.8f,1), new Vector3(.45f,.15f,.15f), new Color(1f,.75f,.12f));
            var counterIcon = new GameObject("Counter crossed swords icon").AddComponent<TextMesh>();
            _guardianCounterIcon = counterIcon;
            counterIcon.transform.SetParent(_guardianWeakness, false); counterIcon.transform.localPosition = new Vector3(0,1.1f,0);
            counterIcon.text = "⚔"; counterIcon.fontSize = 48; counterIcon.anchor = TextAnchor.MiddleCenter;
            counterIcon.color = new Color(1f,.85f,.2f);
            _guardianCore.gameObject.SetActive(false); _guardianWeakness.gameObject.SetActive(false);
            _guardianAudio = gameObject.AddComponent<AudioSource>(); _guardianAudio.playOnAwake = false;
            _guardianAudio.spatialBlend = 0; _guardianAudio.volume = .25f;
            _guardianCounterSound = AudioClip.Create("Guardian counter cue", 4410, 1, 22050, false);
            var samples = new float[4410]; for (int i=0;i<samples.Length;i++) samples[i] = Mathf.Sin(i * 2 * Mathf.PI * 880 / 22050) * (1f-i/(float)samples.Length) * .2f;
            _guardianCounterSound.SetData(samples,0);
            _guardianClashSound = AudioClip.Create("Guardian tank clash cue", 6615, 1, 22050, false);
            var clashSamples = new float[6615];
            for (int i=0;i<clashSamples.Length;i++) clashSamples[i] =
                (Mathf.Sin(i*2*Mathf.PI*440/22050)+.5f*Mathf.Sin(i*2*Mathf.PI*660/22050)) *
                (1f-i/(float)clashSamples.Length)*.2f;
            _guardianClashSound.SetData(clashSamples,0);
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
            if (signal) _guardianWeakness.GetComponent<Renderer>().material.color = GuardianClashCounterWindow
                ? new Color(.35f,.85f,1f) : new Color(1f,.75f,.12f);
            if (signal && _guardianCounterIcon != null)
            { _guardianCounterIcon.text = GuardianClashCounterWindow ? "🛡" : "⚔";
              _guardianCounterIcon.color = GuardianClashCounterWindow ? new Color(.4f,.85f,1f) : new Color(1f,.85f,.2f); }
            _guardianCore.gameObject.SetActive(active && GuardianStepNow==GuardianStep.StaggerCheck);
            _guardianClub.gameObject.SetActive(visible); _guardianShield.gameObject.SetActive(visible);
            if (_guardianAttackArm != null) _guardianAttackArm.gameObject.SetActive(visible && GuardianHard);
            if(signal&&!_guardianSignal&&_guardianAudio!=null)_guardianAudio.PlayOneShot(GuardianClashCounterWindow?_guardianClashSound:_guardianCounterSound);
            _guardianSignal=signal;
            float progress=active && GuardianStepNow==GuardianStep.Telegraph ?
                1f-Mathf.Clamp01((RaidCombatAuthority.Replica ? _guardianEnds-GuardianNow : _guardian.Remaining(GuardianNow)) /
                    Mathf.Max(.01f,(RaidCombatAuthority.Replica ? _guardianEnds-_guardianStarted : _guardian.Ends-_guardian.Started))) : 1f;
            float pose=active&&(GuardianPatternNow==GuardianPattern.Punishment||GuardianPatternNow==GuardianPattern.PlazaSlam||
                GuardianPatternNow==GuardianPattern.TwistedExecution && _hardPhase==2||
                GuardianPatternNow==GuardianPattern.LineFracture)?Mathf.Lerp(0,-70,progress):0;
            if (active && GuardianHard && GuardianPatternNow==GuardianPattern.Punishment && _guardianHardFollowup)
            {
                float elapsed=RaidCombatAuthority.Replica?GuardianNow-_guardianStarted:GuardianNow-_guardian.Started;
                pose=Mathf.Lerp(30,-70,Mathf.Clamp01(elapsed/guardianHard.punishmentSecondDelay));
            }
            bool hardSweep=GuardianHard && (GuardianPatternNow==GuardianPattern.Sweep ||
                GuardianPatternNow==GuardianPattern.TwistedExecution && _hardPhase < 2);
            bool hardSlam=GuardianHard && (GuardianPatternNow==GuardianPattern.PlazaSlam ||
                GuardianPatternNow==GuardianPattern.TwistedExecution && _hardPhase >= 2);
            if(active && hardSweep)
            {
                float sign=Vector3.Dot(_guardianSide,transform.right)>0?1:-1;
                bool followup=GuardianPatternNow==GuardianPattern.Sweep?_guardianHardFollowup:_hardPhase==1;
                float elapsed=RaidCombatAuthority.Replica?GuardianNow-_guardianStarted:GuardianNow-_guardian.Started;
                pose=followup?Mathf.Lerp(-sign*35,sign*65,Mathf.Clamp01(elapsed/.22f)):sign*65;
            }
            else if(active && hardSlam && (GuardianPatternNow==GuardianPattern.PlazaSlam?_guardianHardFollowup:_hardPhase==3))
                pose=35;
            else if(active&&GuardianPatternNow==GuardianPattern.Sweep)pose=Vector3.Dot(_guardianSide,transform.right)>0?65:-65;
            float sideSign=Vector3.Dot(_guardianSide,transform.right)>0?1:-1;
            _guardianClub.localPosition=hardSweep?new Vector3(sideSign*1.25f,1.1f,.1f):
                hardSlam?new Vector3(.65f,1.35f,.1f):new Vector3(1.25f,.5f,0);
            if(_guardianAttackArm!=null)
            {
                _guardianAttackArm.localPosition=hardSweep?new Vector3(sideSign*1.05f,.55f,0):new Vector3(1.05f,.55f,0);
                _guardianAttackArm.localRotation=Quaternion.Euler(hardSweep?pose*.8f:hardSlam?pose:0,0,hardSweep?pose*.5f:0);
            }
            _guardianClub.localRotation=Quaternion.Euler(pose,0,hardSweep || GuardianPatternNow==GuardianPattern.Sweep?pose:0);
            _guardianShield.localRotation=Quaternion.Euler(active&&GuardianPatternNow==GuardianPattern.ShieldPush?Mathf.Lerp(0,-65,progress):0,
                GuardianPatternNow==GuardianPattern.GateCharge?35:0,0);
            _guardianShield.localPosition=active&&GuardianPatternNow==GuardianPattern.ShieldPush?
                new Vector3(-1.1f,Mathf.Lerp(1.1f,-.5f,progress),.5f):new Vector3(-1.1f,.1f,.5f);
        }
        private void GuardianUI()
        {
            if (!GuardianNormal || currentHp <= 0) return;
            if(IsGroggy && bossCastFillImage != null) bossCastFillImage.fillAmount = Mathf.Clamp01((_guardianEnds-GuardianNow)/Mathf.Max(.1f,_guardianEnds-_guardianStarted));
            if(bossPhaseText!=null)bossPhaseText.text=IsGroggy? _currentCastSkillName : GuardianHard ?
                (_guardianHard.Transformed ? "거대 가고일 · 하드" : "마을 수호 석상 · 하드") :
                "마을 수호 석상 · 노멀";
            if (GuardianHard && GuardianPatternNow == GuardianPattern.Transformation && _clashPhase >= 1 && _clashPhase <= 3 && bossCastFillImage != null)
                bossCastFillImage.color = new Color(.25f,.65f,1f);
            if(GuardianStaggerActive)
            {
                if(bossCastFillImage!=null){bossCastFillImage.fillAmount=Mathf.Clamp01(_guardianStagger/Mathf.Max(1,Tuning.staggerRequired));bossCastFillImage.color=new Color(.7f,.2f,1);}
                if(bossCastText!=null)bossCastText.text=$"수호핵 무력화 {_guardianStagger:F0}/{Tuning.staggerRequired:F0} · {_castRemainingTime:F1}초";
            }
        }
    }
}
