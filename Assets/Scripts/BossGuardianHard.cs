using System.Collections.Generic;
using UnityEngine;

namespace RaidRogue
{
    // Hard extends the existing guardian encounter. All clocks and damage stay on the host.
    public partial class BossController
    {
        [SerializeField] private RaidStageCutscenePresentation guardianHardTransformationPresentation;
        [SerializeField] private GameObject guardianHardGargoyleVisual;
        private RaidStageCutscenePresentation _hardTransformationInstance;
        private Camera _hardTransformationCamera, _hardGameplayCamera;
        private bool _hardGameplayCameraWasEnabled;
        private bool _hardTransformationViewActive;
        private GameObject _gargoyleInstance;
        private readonly Dictionary<Renderer, bool> _gargoyleHiddenRenderers = new Dictionary<Renderer, bool>();
        private Transform _gargoyleJumpBody;
        private Vector3 _gargoyleJumpOrigin;
        private GameObject _hardWall;
        private readonly List<GameObject> _hardWallPieces = new List<GameObject>();
        private readonly List<int> _hardCrackIndices = new List<int>();
        private readonly List<Transform> _hardCorruption = new List<Transform>();
        private int _hardCrackIndex, _hardPhase;
        private bool _hardExitOpen;
        private Vector3 _hardBreachDirection;
        private readonly Dictionary<Transform, Vector3> _hardStoneEscapeDestinations = new Dictionary<Transform, Vector3>();
        private readonly List<AoETelegraph> _hardStoneGround = new List<AoETelegraph>();
        private readonly List<AoETelegraph> _hardStoneImpacts = new List<AoETelegraph>();
        private AoETelegraph _hardAuxImpact;
        private sealed class GuardianStoneDrop { public AoETelegraph ground,warning; public float impactAt; }
        private readonly List<GuardianStoneDrop> _stoneDrops=new List<GuardianStoneDrop>();
        private readonly GuardianStoneRainSchedule _stoneRain=new GuardianStoneRainSchedule();
        private int _hardStoneStage,_stoneRainSerial;
        private float _hardStoneStarted;
        private AoETelegraph _hardImpactAfterimage;
        private float _hardImpactAfterimageEnds;
        private AoETelegraph _clashCircle;
        private Transform _clashShieldLabel;
        private Transform _clashTank;
        private float _clashNextPulse;
        private int _clashPhase;
        private bool _clashShockwaveWarned;
        private bool _clashSucceeded;
        private int _airChargePass;
        private float _airChargeAngle, _airChargeGroundY;
        private Vector3 _airChargeFrom, _airChargeLanding;
        private bool _airChargeInFlight;
        private Transform _hardLureTarget;
        private AttachedPartyMechanicMarker _hardLureMarker;
        private bool _hardLurePrompted;
        private readonly PlayerMovementCollision _hardLureCollision = new PlayerMovementCollision();
        public Transform GuardianLureTarget => _hardLureTarget;
        public bool GuardianLured(Transform actor) => GuardianHard && actor != null && actor == _hardLureTarget &&
            GuardianHardGeometry.LurePhase(GuardianPatternNow,_guardianHardFollowup,_hardPhase) && currentHp > 0 && !StageManager.CombatSuspended;
        public bool GuardianLureRescueWindow => GuardianLured(_hardLureTarget) &&
            GuardianHardGeometry.RescueWindow(_guardianEnds-GuardianNow,guardianHard.lureRescueWindow);
        public bool GuardianHardAerialAirborne => GuardianHard && GuardianPatternNow == GuardianPattern.AerialCharge &&
            (GuardianStepNow == GuardianStep.Telegraph || GuardianStepNow == GuardianStep.Recovery);
        // 0 = cutscene, 1 = preparation, 2 = counter, 3 = hold, 4 = opening, 5 = recovery.
        public bool GuardianHardTransformationActive => GuardianClashRules.TransformationActive(GuardianHard, GuardianPatternNow, _clashPhase);
        public bool GuardianClashCounterWindow => GuardianHard && GuardianPatternNow == GuardianPattern.Transformation && _clashPhase == 2;
        public bool GuardianClashHolding(Transform actor) => GuardianHard && GuardianPatternNow == GuardianPattern.Transformation &&
            _clashPhase == 3 && _clashTank == actor;
        public bool GuardianClashPulseImminent(Transform actor) => GuardianClashHolding(actor) &&
            _clashNextPulse - GuardianNow <= 1.5f;
        public bool GuardianClashCounterAllowed(Transform actor) => actor != null &&
            GuardianClashRules.CounterEligible(GuardianClashCounterWindow, CombatRange.LivingPartyMember(actor),
                actor.GetComponent<PartyLife>()?.IsTank == true,
                BattleResurrection.FlatDistance(actor.position, _guardianOrigin), guardianHard.clashCircleRadius);
        private bool HardStoneRainActive => _hardStoneStage != 0;

        private bool BeginGuardianHard(GuardianPattern pattern, float now, List<Transform> members)
        {
            _hardPhase = 0;
            if (pattern == GuardianPattern.AerialCharge)
            {
                var target = ResolveLivingAggroTarget();
                Vector3 outward = target != null ? target.position : transform.forward;
                outward.y = 0;
                if (outward.sqrMagnitude < .01f) outward = Vector3.forward;
                _airChargeAngle = Mathf.Atan2(outward.x,outward.z) * Mathf.Rad2Deg;
                _airChargeGroundY = transform.position.y;
                _airChargePass = 0;
                BeginHardAerialChargePass(now);
                return true;
            }
            if (pattern == GuardianPattern.Lockdown || pattern == GuardianPattern.FullLockdown)
            {
                _guardianVictim = HardLockdownTarget(members);
                if (_guardianVictim == null) { EndGuardian(now); return true; }
                ChooseHardCracks(_guardianVictim.position);
                BuildHardWall();
                BeginHardAim(now);
                return true;
            }
            if (pattern == GuardianPattern.FallingStone)
            {
                _guardian.Enter(GuardianStep.Telegraph, now, guardianHard.stoneCastSeconds);
                _networkCastStarted = now; _castTotalDuration = guardianHard.stoneCastSeconds;
                _currentCastSkillName = $"석편 낙하 · 연속 {guardianHard.stoneWaves}회 준비";
                SetMechanicAlert(_currentCastSkillName, Color.magenta, _castTotalDuration);
                return true;
            }
            if (pattern == GuardianPattern.Transformation)
            {
                _clashPhase = 0; _clashTank = null; _clashSucceeded = false;
                ClearHardStoneRain();
                float duration = guardianHardTransformationPresentation != null
                    ? RaidStageCutscenePresentation.Duration(guardianHardTransformationPresentation.DirectorAsset, guardianHard.transformationSeconds)
                    : guardianHard.transformationSeconds;
                _guardian.Enter(GuardianStep.Telegraph, now, duration);
                _networkCastStarted = now; _castTotalDuration = duration;
                _currentCastSkillName = "공중 도약 · 거대 가고일 변신";
                CleanupActiveTelegraphAndTether();
                BeginHardTransformationPresentation();
                SetMechanicAlert("석상이 공중으로 도약합니다 · 가고일 변신", Color.magenta, _castTotalDuration);
                return true;
            }
            if (pattern == GuardianPattern.TwistedExecution)
            {
                _guardian.Enter(GuardianStep.Telegraph, now, guardianHard.twistedSweepPrepare);
                _networkCastStarted = now; _castTotalDuration = guardianHard.twistedSweepPrepare;
                _currentCastSkillName = Vector3.Dot(_guardianSide, transform.right) < 0
                    ? "뒤틀린 집행 · 왼쪽 → 오른쪽 → 중심 → 외곽"
                    : "뒤틀린 집행 · 오른쪽 → 왼쪽 → 중심 → 외곽";
                SetMechanicAlert(_currentCastSkillName, Color.yellow, _castTotalDuration);
                return true;
            }
            return false;
        }

        private bool TickGuardianHard(float now)
        {
            GuardianPattern pattern = _guardian.Pattern;
            if (pattern == GuardianPattern.AerialCharge)
            {
                TickHardAerialCharge(now);
                return true;
            }
            if (pattern == GuardianPattern.Transformation)
            {
                if (_clashPhase == 0 && _gargoyleJumpBody != null)
                {
                    float progress = Mathf.Clamp01((now - _guardian.Started) / Mathf.Max(.1f, _castTotalDuration));
                    UpdateHardTransformationFallback(progress);
                }
                if (_clashPhase == 0 && _guardian.Due(now))
                {
                    RestoreGargoyleJump();
                    StopHardTransformationPresentation(); // Camera is restored before the preparation clock starts.
                    PlayHardImpactCue();
                    _guardianHard.FinishTransformation();
                    RestoreHardTransformationHealth();
                    BuildHardCorruption();
                    currentBossTitle = "STAGE 1 : 거대 가고일 · 하드";
                    BeginGuardianClash(now);
                }
                else if (_clashPhase != 0) TickGuardianClash(now);
                return true;
            }
            if (pattern == GuardianPattern.Lockdown || pattern == GuardianPattern.FullLockdown)
            {
                TickHardLockdown(now, pattern == GuardianPattern.FullLockdown);
                return true;
            }
            if (pattern == GuardianPattern.TwistedExecution)
            {
                if (_guardian.Due(now)) AdvanceTwistedExecution(now);
                return true;
            }
            return false;
        }

        private void TickHardImpactWarning(float now)
        {
            TickHardAuxImpactWarning(now);
            GuardianPattern pattern = _guardian.Pattern;
            if (pattern == GuardianPattern.ShieldPush)
            {
                if (_activeTelegraph == null && _guardian.Step == GuardianStep.Telegraph &&
                    _guardian.Remaining(now) <= guardianHard.impactWarningSeconds)
                    SpawnGuardianShieldWave(Mathf.Max(.01f,_guardian.Remaining(now)));
                return;
            }
            if (pattern != GuardianPattern.Sweep && pattern != GuardianPattern.PlazaSlam &&
                pattern != GuardianPattern.TwistedExecution) return;
            if (_activeTelegraph != null || _guardian.Step == GuardianStep.Rest ||
                _guardian.Step == GuardianStep.Recovery) return;
            float remaining = _guardian.Remaining(now);
            if (remaining > guardianHard.impactWarningSeconds) return;
            float display = Mathf.Max(.01f, remaining) + guardianHard.impactAfterimageSeconds;
            bool sweep = pattern == GuardianPattern.Sweep || pattern == GuardianPattern.TwistedExecution && _hardPhase < 2;
            bool ring = pattern == GuardianPattern.PlazaSlam && _guardianHardFollowup ||
                pattern == GuardianPattern.TwistedExecution && _hardPhase == 3;
            _activeTelegraph = sweep
                ? AoETelegraph.Spawn(AoETelegraphType.FrontCone_120Deg, _guardianOrigin, _guardianSide,
                    display, 0, 36, 180)
                : ring
                    ? AoETelegraph.Spawn(AoETelegraphType.Donut_Inner4_Outer12, _guardianOrigin,
                        Vector3.forward, display, Tuning.slamRadius, guardianHard.slamRingOuter)
                    : AoETelegraph.Spawn(AoETelegraphType.Circle_Radius6, _guardianOrigin,
                        Vector3.forward, display, 0, Tuning.slamRadius);
        }

        private void TickHardAuxImpactWarning(float now)
        {
            if (_hardAuxImpact != null && _guardian.Pattern == GuardianPattern.Resonance && _guardianVictim != null)
                _hardAuxImpact.transform.position = new Vector3(_guardianVictim.position.x, 0, _guardianVictim.position.z);
            if (_hardAuxImpact != null || _guardian.Remaining(now) > guardianHard.impactWarningSeconds) return;
            GuardianPattern pattern = _guardian.Pattern;
            float duration = Mathf.Max(.01f, _guardian.Remaining(now));
            if (pattern == GuardianPattern.Resonance && _guardianVictim != null && _guardian.Step == GuardianStep.Telegraph)
                _hardAuxImpact = AoETelegraph.Spawn(AoETelegraphType.Circle_Radius6,
                    _guardianVictim.position, Vector3.forward, duration, 0, Tuning.resonanceRadius);
            else if (pattern == GuardianPattern.LineFracture && _guardian.Step == GuardianStep.Locked && _activeTelegraph != null)
                _hardAuxImpact = AoETelegraph.Spawn(AoETelegraphType.Rectangle,
                    _guardianOrigin, _guardianForward, duration, _activeTelegraph.innerRadius, _activeTelegraph.outerRadius);
            else if ((pattern == GuardianPattern.Lockdown || pattern == GuardianPattern.FullLockdown) &&
                _hardPhase == 1 && _activeTelegraph != null)
                _hardAuxImpact = AoETelegraph.Spawn(AoETelegraphType.Rectangle,
                    _guardianOrigin, _guardianForward, duration, _activeTelegraph.innerRadius, _activeTelegraph.outerRadius);
        }

        private void ClearHardAuxImpact()
        {
            if (_hardAuxImpact != null) { _hardAuxImpact.gameObject.SetActive(false); Destroy(_hardAuxImpact.gameObject); }
            _hardAuxImpact = null;
        }

        private void FinishHardImpact(float now)
        {
            if (_activeTelegraph == null) return;
            ClearHardImpactAfterimage();
            if (guardianHard.impactAfterimageSeconds > 0)
            {
                _hardImpactAfterimage = _activeTelegraph;
                _hardImpactAfterimageEnds = now + guardianHard.impactAfterimageSeconds;
            }
            else { _activeTelegraph.gameObject.SetActive(false); Destroy(_activeTelegraph.gameObject); }
            _activeTelegraph = null;
        }

        private void TickHardImpactAfterimage(float now)
        {
            if (_hardImpactAfterimage != null && now >= _hardImpactAfterimageEnds)
                ClearHardImpactAfterimage();
        }
        private void ClearHardImpactAfterimage()
        {
            if (_hardImpactAfterimage != null)
            { _hardImpactAfterimage.gameObject.SetActive(false); Destroy(_hardImpactAfterimage.gameObject); }
            _hardImpactAfterimage = null;
        }

        private bool BeginGuardianHardFollowup(float now)
        {
            if (_guardianHardFollowup || !GuardianHard) return false;
            float seconds;
            if (_guardian.Pattern == GuardianPattern.Punishment) seconds = guardianHard.punishmentSecondDelay;
            else if (_guardian.Pattern == GuardianPattern.Sweep) seconds = guardianHard.sweepSecondDelay;
            else if (_guardian.Pattern == GuardianPattern.PlazaSlam) seconds = guardianHard.slamRingDelay;
            else return false;
            if (_guardian.Pattern == GuardianPattern.PlazaSlam) ClearHardSlamLure();
            if (_guardian.Pattern == GuardianPattern.Sweep || _guardian.Pattern == GuardianPattern.PlazaSlam)
                FinishHardImpact(now);
            _guardianHardFollowup = true;
            _guardian.Enter(GuardianStep.Locked, now, seconds);
            _networkCastStarted = now; _castTotalDuration = seconds;
            if (_activeTelegraph != null) { _activeTelegraph.gameObject.SetActive(false); Destroy(_activeTelegraph.gameObject); _activeTelegraph = null; }
            _guardianDestinations.Clear();
            if (_guardian.Pattern == GuardianPattern.Punishment)
            {
                _currentCastSkillName = "침입자 징벌 · 대상 2연타 (2/2)";
            }
            else if (_guardian.Pattern == GuardianPattern.Sweep)
            {
                _guardianSide = -_guardianSide;
                _currentCastSkillName = "돌봉 휩쓸기 · 반대쪽 연속 공격";
            }
            else
            {
                _currentCastSkillName = "광장 내려찍기 · 외곽 고리 / 안으로";
            }
            SetMechanicAlert(_currentCastSkillName, Color.yellow, seconds);
            return true;
        }

        // Each fixed-position drop resolves independently; the next wave does not wait for impact.
        // Normal and hard share this authoritative stream while the statue pursues and autoattacks.
        private void StartHardStoneRain(float now)
        {
            ClearHardStoneRain();
            _hardStoneStage=1;_hardStoneStarted=now;
            _stoneRain.Start(now,GuardianHard?guardianHard.stoneWaveGapSeconds:Tuning.stoneWaveInterval,
                GuardianHard?guardianHard.stoneWaves:Tuning.stoneWaves);
            TickHardStoneRain(now);
        }
        private void EmitStoneWave(float now)
        {
            float delay=GuardianHard?guardianHard.stoneLockedSeconds:Tuning.LockedDuration(GuardianPattern.FallingStone);
            delay=Mathf.Max(.35f,delay);
            _hardStoneEscapeDestinations.Clear();
            foreach(var member in GetLivingPartyMemberTransforms())
            {
                var ground=AoETelegraph.Spawn(AoETelegraphType.Circle_Radius6,member.position,
                    Vector3.forward,delay,0,Tuning.stoneRadius);
                if(GuardianHard)ground.SetGuideStyle(true);
                _hardStoneGround.Add(ground);
                _stoneDrops.Add(new GuardianStoneDrop{ground=ground,impactAt=now+delay});
            }
            PlayGuardianLockCue();
        }
        private void TickHardStoneRain(float now)
        {
            if(!HardStoneRainActive||RaidCombatAuthority.Replica)return;
            int serial=_stoneRainSerial;
            if(_stoneRain.TryEmit(now))EmitStoneWave(now);
            bool impactCue=false;
            for(int i=_stoneDrops.Count-1;i>=0;i--)
            {
                var drop=_stoneDrops[i];
                if(drop.ground==null){RemoveStoneObject(drop.warning);_hardStoneImpacts.Remove(drop.warning);_hardStoneGround.Remove(drop.ground);_stoneDrops.RemoveAt(i);continue;}
                if(GuardianHard&&drop.warning==null&&now<drop.impactAt&&drop.impactAt-now<=guardianHard.impactWarningSeconds)
                {
                    drop.warning=AoETelegraph.Spawn(AoETelegraphType.Circle_Radius6,drop.ground.transform.position,
                        Vector3.forward,Mathf.Max(.1f,drop.impactAt-now),0,Tuning.stoneRadius);
                    _hardStoneImpacts.Add(drop.warning);
                }
                if(now<drop.impactAt)continue;
                _stoneDrops.RemoveAt(i);_hardStoneGround.Remove(drop.ground);_hardStoneImpacts.Remove(drop.warning);
                Vector3 point=drop.ground!=null?drop.ground.transform.position:Vector3.positiveInfinity;
                RemoveStoneObject(drop.ground);RemoveStoneObject(drop.warning);
                _hardStoneEscapeDestinations.Clear();
                if(!impactCue){PlayHardImpactCue();impactCue=true;}
                foreach(var member in GetLivingPartyMemberTransforms())
                {
                    // Death/wipe callbacks may clear every remaining drop synchronously.
                    if(serial!=_stoneRainSerial||!HardStoneRainActive)return;
                    if(BattleResurrection.FlatDistance(member.position,point)<=Tuning.stoneRadius)
                    {
                        _guardianHits[(int)GuardianPattern.FallingStone]++;
                        DamagePartyMember(member,Tuning.stoneDamage,CombatDamageKind.Area);
                        if(serial!=_stoneRainSerial||!HardStoneRainActive)return;
                    }
                }
            }
            if(!_stoneRain.Emitting&&_stoneDrops.Count==0)ClearHardStoneRain();
        }
        private void RemoveStoneObject(AoETelegraph circle)
        {if(circle!=null){circle.gameObject.SetActive(false);Destroy(circle.gameObject);}}
        private void ClearHardStoneRain()
        {
            _stoneRainSerial++;
            foreach(var circle in _hardStoneGround)RemoveStoneObject(circle);
            foreach(var circle in _hardStoneImpacts)RemoveStoneObject(circle);
            _hardStoneGround.Clear();_hardStoneImpacts.Clear();_stoneDrops.Clear();_stoneRain.Clear();
            _hardStoneEscapeDestinations.Clear();_hardStoneStage=0;_hardStoneStarted=0;
        }
        private bool GuardianHardStoneDestination(Transform actor,int role,out Vector3 destination)
        {
            destination=actor.position;
            if(!HardStoneRainActive||!CombatRange.LivingPartyMember(actor)||
                GuardianNow-_hardStoneStarted<Tuning.aiLockReactionSeconds)return false;
            bool threatened=false;
            foreach(var circle in _hardStoneGround)
                if(circle!=null&&BattleResurrection.FlatDistance(actor.position,circle.transform.position)<
                    Tuning.stoneRadius+CombatRange.Radius(actor)+.3f){threatened=true;break;}
            if(!threatened)return false;
            if(_hardStoneEscapeDestinations.TryGetValue(actor,out destination))return true;
            float best=float.MaxValue;
            for(int i=0;i<48;i++)
            {
                float angle=(i+role*3)*Mathf.PI/24;
                Vector3 point=CombatRange.ClampToArena(actor.position+new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle))*(Tuning.stoneRadius+1.5f),16.8f);
                bool safe=true;
                foreach(var circle in _hardStoneGround)
                    if(circle!=null&&BattleResurrection.FlatDistance(point,circle.transform.position)<
                        Tuning.stoneRadius+CombatRange.Radius(actor)+.5f){safe=false;break;}
                float cost=(point-actor.position).sqrMagnitude+i*.001f;
                if(safe&&cost<best){best=cost;destination=point;}
            }
            destination.y=actor.position.y;
            if(best==float.MaxValue)return false;
            _hardStoneEscapeDestinations[actor]=destination;return true;
        }
        private Transform HardLockdownTarget(List<Transform> members)
        {
            // Prefer a living damage dealer; healer, then tank are safe fallback candidates.
            var dealers = members.FindAll(m => m.GetComponent<PartyLife>() is PartyLife life && !life.IsTank && !life.IsHealer);
            return dealers.Count > 0 ? dealers[Random.Range(0, dealers.Count)] : GuardianLineTarget(members);
        }
        private void ChooseHardCracks(Vector3 near)
        {
            _hardCrackIndices.Clear();
            int count = Mathf.Max(12, guardianHard.wallSegments);
            int sector = count / 3;
            int rotation = Random.Range(0, count);
            _hardCrackIndices.AddRange(GuardianHardGeometry.ThreeCracks(count, rotation,
                Random.Range(1, sector - 1), Random.Range(1, sector - 1), Random.Range(1, sector - 1)));
            SelectPreferredHardCrack(near);
        }
        private void SelectPreferredHardCrack(Vector3 near)
        {
            int count = Mathf.Max(12, guardianHard.wallSegments);
            Vector3 flat = new Vector3(near.x, 0, near.z);
            if (flat.sqrMagnitude < .01f) flat = -_guardianForward;
            _hardCrackIndex = _hardCrackIndices[0];
            float best = float.NegativeInfinity;
            foreach (int candidate in _hardCrackIndices)
            {
                float angle = candidate * Mathf.PI * 2 / count;
                float score = Vector3.Dot(flat.normalized, new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)));
                if (score > best) { best = score; _hardCrackIndex = candidate; }
            }
            SetHardBreachDirection(_hardCrackIndex, count);
        }
        private void SetHardBreachDirection(int index, int count)
        {
            float radians = index * Mathf.PI * 2 / count;
            _hardBreachDirection = new Vector3(Mathf.Sin(radians), 0, Mathf.Cos(radians));
        }
        private Vector3 HardBreachPoint(float radius) => _hardBreachDirection * radius + Vector3.up * _guardianOrigin.y;
        private Vector3 HardAimPoint() => Vector3.Lerp(_guardianOrigin, HardBreachPoint(guardianHard.wallRadius), .78f);

        private void BuildHardWall()
        {
            ClearGuardianHard();
            _hardWall = new GameObject("Guardian hard · breakable ring wall");
            int count = Mathf.Max(12, guardianHard.wallSegments);
            float radius = guardianHard.wallRadius;
            float width = 2 * Mathf.PI * radius / count - .03f;
            for (int i = 0; i < count; i++)
            {
                float angle = i * 2 * Mathf.PI / count;
                Vector3 radial = new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle));
                var piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bool cracked = _hardCrackIndices.Contains(i);
                piece.name = cracked ? "Cracked exit · breakable" : "Stone wall";
                piece.transform.SetParent(_hardWall.transform);
                piece.transform.position = radial * radius + Vector3.up * (guardianHard.wallHeight * .5f);
                piece.transform.rotation = Quaternion.LookRotation(radial);
                piece.transform.localScale = new Vector3(width, guardianHard.wallHeight, .42f);
                var material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                material.color = cracked ? new Color(1f, .48f, .12f) : new Color(.35f, .34f, .32f);
                piece.GetComponent<Renderer>().material = material;
                if (cracked)
                {
                    var seam = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    seam.name = "Visible fracture seam";
                    seam.transform.SetParent(piece.transform, false);
                    seam.transform.localPosition = new Vector3(0, 0, .55f);
                    seam.transform.localScale = new Vector3(.09f, .9f, .12f);
                    Destroy(seam.GetComponent<Collider>());
                    var seamMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                    seamMaterial.color = new Color(.08f, .04f, .02f);
                    seam.GetComponent<Renderer>().material = seamMaterial;
                }
                _hardWallPieces.Add(piece);
            }
            _hardExitOpen = false;
        }
        private void BeginHardAim(float now)
        {
            _hardPhase = 0;
            _guardian.Enter(GuardianStep.Telegraph, now, guardianHard.lockdownLockAt);
            _networkCastStarted = now; _castTotalDuration = guardianHard.lockdownLockAt;
            _guardianDestinations.Clear();
            if (_activeTelegraph != null) { _activeTelegraph.gameObject.SetActive(false); Destroy(_activeTelegraph.gameObject); }
            ClearActivePartyMarkers();
            _activePartyMarkers.Add(AttachedPartyMechanicMarker.SpawnTargetMarker(_guardianVictim, .75f));
            TrackGuardianLine();
            _activeTelegraph = AoETelegraph.Spawn(AoETelegraphType.Rectangle, _guardianOrigin, _guardianForward,
                guardianHard.lockdownLineAt, guardianHard.lockdownWidth * .5f,
                BoundedGuardianLineLength(_guardianOrigin, _guardianForward, guardianHard.wallRadius * 2));
            _activeTelegraph.SetGuideStyle(false);
            _currentCastSkillName = _guardian.Pattern == GuardianPattern.FullLockdown
                ? "완전 봉쇄 · 균열로 파쇄선 유도" : "봉쇄 명령 · 균열로 파쇄선 유도";
            SetMechanicAlert(_currentCastSkillName, Color.yellow, guardianHard.lockdownLineAt);
        }
        private void TickHardLockdown(float now, bool full)
        {
            if(_hardPhase==2&&_guardian.Step==GuardianStep.Escape&&_hardExitOpen)
            {
                var members=GetLivingPartyMemberTransforms();int outside=0;
                foreach(var member in members)
                    if(!GuardianHardGeometry.LockdownEraseHits(member.position.x,member.position.z,
                        guardianHard.wallRadius,CombatRange.Radius(member)))outside++;
                if(GuardianTempo.CanHastenErase(true,members.Count,outside,_guardian.Remaining(now),guardianHard.lockdownReadyImpactSeconds))
                {
                    _guardian.Enter(GuardianStep.Escape,now,guardianHard.lockdownReadyImpactSeconds);
                    _networkCastStarted=now;_castTotalDuration=_guardian.Ends-now;
                    if(_activeTelegraph!=null){_activeTelegraph.gameObject.SetActive(false);Destroy(_activeTelegraph.gameObject);}
                    _activeTelegraph=AoETelegraph.Spawn(AoETelegraphType.Circle_Radius6,Vector3.zero,Vector3.forward,
                        _castTotalDuration,0,guardianHard.wallRadius);
                }
            }
            if (_hardPhase == 0 && !CombatRange.LivingPartyMember(_guardianVictim))
            {
                _guardianVictim = HardLockdownTarget(GetLivingPartyMemberTransforms());
                if (_guardianVictim == null) { EndGuardian(now); return; }
                SelectPreferredHardCrack(_guardianVictim.position);
                BeginHardAim(now); // The replacement target receives the complete configured aiming window.
                return;
            }
            if (!_guardian.Due(now)) return;
            if (_hardPhase == 0)
            {
                _hardPhase = 1;
                TrackGuardianLine();
                _guardian.Enter(GuardianStep.Locked, now, guardianHard.lockdownLineAt - guardianHard.lockdownLockAt);
                _networkCastStarted = now; _castTotalDuration = _guardian.Ends - now;
                ClearActivePartyMarkers(); _guardianDestinations.Clear();
                if (_activeTelegraph != null) { _activeTelegraph.gameObject.SetActive(false); Destroy(_activeTelegraph.gameObject); }
                _activeTelegraph = AoETelegraph.Spawn(AoETelegraphType.Rectangle, _guardianOrigin, _guardianForward,
                    _castTotalDuration, guardianHard.lockdownWidth * .5f,
                    BoundedGuardianLineLength(_guardianOrigin, _guardianForward, guardianHard.wallRadius * 2));
                _activeTelegraph.SetGuideStyle(true);
                _currentCastSkillName = full ? "완전 봉쇄 · 조준 고정 / 선 밖으로" : "봉쇄 명령 · 조준 고정 / 선 밖으로";
                SetMechanicAlert(_currentCastSkillName, Color.yellow, _castTotalDuration);
                PlayGuardianLockCue(); return;
            }
            if (_hardPhase == 1)
            {
                _hardPhase = 2;
                PlayHardImpactCue();
                foreach (var member in GetLivingPartyMemberTransforms())
                    if (_activeTelegraph != null && _activeTelegraph.IsPointInsideXZ(member.position)) GuardianHit(member, guardianHard.lockdownLineDamage);
                ClearHardAuxImpact();
                int hitCrack = HardHitCrackIndex();
                if (hitCrack >= 0) OpenHardExit(hitCrack);
                if (_activeTelegraph != null) { _activeTelegraph.gameObject.SetActive(false); Destroy(_activeTelegraph.gameObject); }
                _guardian.Enter(GuardianStep.Escape, now, guardianHard.lockdownEraseAt - guardianHard.lockdownLineAt);
                _networkCastStarted = now; _castTotalDuration = _guardian.Ends - now;
                _activeTelegraph = AoETelegraph.Spawn(AoETelegraphType.Circle_Radius6, Vector3.zero, Vector3.forward,
                    _castTotalDuration, 0, guardianHard.wallRadius);
                _currentCastSkillName = _hardExitOpen ? "내부 소거 · 열린 균열로 탈출" : "균열 파쇄 실패 · 내부 소거 대비";
                SetMechanicAlert(_currentCastSkillName, Color.red, _castTotalDuration);
                _guardianDestinations.Clear(); return;
            }
            if (_hardPhase == 2)
            {
                PlayHardImpactCue();
                foreach (var member in GetLivingPartyMemberTransforms())
                    if (GuardianHardGeometry.LockdownEraseHits(member.position.x, member.position.z,
                        guardianHard.wallRadius, CombatRange.Radius(member))) GuardianHit(member, guardianHard.lockdownFailureDamage);
                ClearGuardianHard(); CleanupActiveTelegraphAndTether(); _guardianDestinations.Clear();
                if (full) BeginFullCharge(now);
                else
                {
                    // The normal rest already lets the boss pursue players back into melee range.
                    EndGuardian(now);
                }
                return;
            }
            if (!full) { EndGuardian(now); return; }
            if (_hardPhase == 3)
            {
                _hardPhase = 4;
                _guardian.Enter(GuardianStep.CounterWindow, now, Tuning.counterWindowSeconds);
                CombatCounterTarget.Ensure(transform).OpenWindow(Tuning.counterWindowSeconds);
                _networkCastStarted = now; _castTotalDuration = Tuning.counterWindowSeconds;
                _currentCastSkillName = "완전 봉쇄 · ⚔ 금색 약점 카운터";
                SetMechanicAlert(_currentCastSkillName, Color.cyan, _castTotalDuration); return;
            }
            if (_hardPhase == 4)
            {
                _hardPhase = 5; CombatCounterTarget.Ensure(transform).CloseWindow();
                _guardian.Enter(GuardianStep.Dash, now, Vector3.Distance(_guardianOrigin, _guardianEnd) / Mathf.Max(1, Tuning.chargeSpeed));
                _networkCastStarted = now; _castTotalDuration = _guardian.Ends - now;
                _currentCastSkillName = "완전 봉쇄 · 직선 돌진"; return;
            }
            if (_hardPhase == 8) EndGuardian(now,recoveryPaid:true);
        }

        private int HardHitCrackIndex()
        {
            int count = Mathf.Max(12, guardianHard.wallSegments);
            foreach (int crack in _hardCrackIndices)
            {
                float angle = crack * Mathf.PI * 2 / count;
                if (GuardianHardGeometry.LineHitsCrack(_guardianOrigin.x, _guardianOrigin.z,
                    _guardianForward.x, _guardianForward.z, Mathf.Sin(angle), Mathf.Cos(angle),
                    guardianHard.wallRadius, count, guardianHard.lockdownWidth * .5f)) return crack;
            }
            return -1;
        }
        private void OpenHardExit(int crack)
        {
            if (crack < 0 || crack >= _hardWallPieces.Count) return;
            _hardCrackIndex = crack;
            SetHardBreachDirection(crack, _hardWallPieces.Count);
            _hardWallPieces[crack].SetActive(false);
            _hardExitOpen = true;
        }
        private void BeginFullCharge(float now)
        {
            _hardPhase = 3;
            _guardianOrigin = transform.position;
            _guardianForward = _hardBreachDirection;
            transform.rotation = Quaternion.LookRotation(_guardianForward);
            Vector3 flat = _guardianOrigin; flat.y = 0;
            float dot = Vector3.Dot(flat, _guardianForward);
            float radius = Mathf.Max(1, arenaBoundaryRadius - CombatRadius);
            float length = Mathf.Max(1, -dot + Mathf.Sqrt(Mathf.Max(0, dot * dot + radius * radius - flat.sqrMagnitude)));
            _guardianEnd = _guardianOrigin + _guardianForward * length;
            _guardian.Enter(GuardianStep.Telegraph, now, Tuning.chargePrepare);
            _networkCastStarted = now; _castTotalDuration = Tuning.chargePrepare;
            _activeTelegraph = AoETelegraph.Spawn(AoETelegraphType.Rectangle, _guardianOrigin, _guardianForward,
                _castTotalDuration + Tuning.counterWindowSeconds, Tuning.chargeHalfWidth, length);
            _currentCastSkillName = "완전 봉쇄 · 출구 방향 돌진 준비";
            SetMechanicAlert(_currentCastSkillName, Color.yellow, _castTotalDuration);
        }
        private void FinishFullLockdownCharge(float now)
        {
            CombatCounterTarget.Ensure(transform).CloseWindow();
            CleanupActiveTelegraphAndTether(); _guardianDestinations.Clear();
            _guardianOrigin = transform.position;
            _hardPhase = 8; // Return to the normal rotation after recovery; no stagger check.
            _guardian.Enter(GuardianStep.Recovery, now, guardianHard.fullRecovery);
            _networkCastStarted = now; _castTotalDuration = guardianHard.fullRecovery;
            _currentCastSkillName = "완전 봉쇄 · 재집결 / 회복";
            SetMechanicAlert(_currentCastSkillName, Color.green, _castTotalDuration);
        }

        private void BeginHardAerialChargePass(float now)
        {
            CleanupActiveTelegraphAndTether(); ClearHardAuxImpact();
            _guardianDashHits.Clear(); _guardianDestinations.Clear();
            float angle = (float)GuardianHardGeometry.AerialChargeAngle(_airChargeAngle,_airChargePass) * Mathf.Deg2Rad;
            Vector3 radial = new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle));
            float radius = (float)GuardianHardGeometry.AerialChargeStartRadius(arenaBoundaryRadius,CombatRadius);
            _airChargeFrom = transform.position;
            _airChargeLanding = radial * radius + Vector3.up * _airChargeGroundY;
            _guardianOrigin = _airChargeFrom;
            _guardianForward = Vector3.forward;
            _airChargePass++; _hardPhase = _airChargePass * 10;
            _airChargeInFlight = true;
            _guardian.Enter(GuardianStep.Telegraph,now,guardianHard.aerialLeapSeconds);
            _networkCastStarted=now;_castTotalDuration=guardianHard.aerialLeapSeconds;
            _currentCastSkillName=$"공중 이탈 {_airChargePass}/{guardianHard.aerialChargePasses}";
            SetMechanicAlert(_currentCastSkillName,Color.yellow,_castTotalDuration);
        }
        private void TickHardAerialCharge(float now)
        {
            if (_guardian.Step == GuardianStep.Telegraph)
            {
                float progress = Mathf.InverseLerp(_guardian.Started,_guardian.Ends,now);
                // Exit straight along world +Z first; change the approach position only
                // once outside the arena, rather than hopping diagonally to its edge.
                float exitZ=Mathf.Max(_airChargeFrom.z,arenaBoundaryRadius+CombatRadius+18);
                transform.position=Vector3.Lerp(_airChargeFrom,new Vector3(_airChargeFrom.x,_airChargeGroundY,exitZ),progress);
                if (!_guardian.Due(now)) return;
                transform.position=_airChargeLanding;_airChargeInFlight=false;
                _guardianOrigin=_airChargeLanding;
                _guardianForward=new Vector3(-_guardianOrigin.x,0,-_guardianOrigin.z).normalized;
                // Both ends stay beyond the arena, including the boss's full body radius.
                _guardianEnd=_guardianForward*(float)GuardianHardGeometry.AerialChargeStartRadius(arenaBoundaryRadius,CombatRadius)+Vector3.up*_airChargeGroundY;
                transform.rotation=Quaternion.LookRotation(_guardianForward);
                _hardPhase=_airChargePass*10+1;
                _guardian.Enter(GuardianStep.Locked,now,guardianHard.aerialAimSeconds);
                _networkCastStarted=now;_castTotalDuration=guardianHard.aerialAimSeconds;
                _activeTelegraph=AoETelegraph.Spawn(AoETelegraphType.Rectangle,_guardianOrigin,_guardianForward,
                    _castTotalDuration,Tuning.chargeHalfWidth,Vector3.Distance(_guardianOrigin,_guardianEnd));
                _currentCastSkillName=$"외곽 돌진 {_airChargePass}/{guardianHard.aerialChargePasses} · 1초 후! ["+LocalInputBindings.Label("Dash")+"] 회피";
                SetMechanicAlert(_currentCastSkillName,Color.yellow,_castTotalDuration);
                PlayGuardianLockCue();
            }
            else if (_guardian.Step == GuardianStep.Locked && _guardian.Due(now))
            {
                ClearHardAuxImpact();CleanupActiveTelegraphAndTether();
                float seconds=Vector3.Distance(_guardianOrigin,_guardianEnd)/Mathf.Max(1,guardianHard.aerialChargeSpeed);
                _hardPhase=_airChargePass*10+2;
                _guardian.Enter(GuardianStep.Dash,now,seconds);
                _networkCastStarted=now;_castTotalDuration=seconds;
                _currentCastSkillName=$"외곽 돌진 {_airChargePass}/{guardianHard.aerialChargePasses}";
                PlayHardImpactCue();
            }
            else if (_guardian.Step == GuardianStep.Recovery)
            {
                float progress=Mathf.InverseLerp(_guardian.Started,_guardian.Ends,now);
                transform.position=Vector3.Lerp(_airChargeFrom,new Vector3(0,_airChargeGroundY,0),progress);
                if(!_guardian.Due(now))return;
                transform.position=new Vector3(0,_airChargeGroundY,0);
                _airChargeInFlight=false;
                EndGuardian(now,recoveryPaid:true);
            }
        }
        private void FinishHardAerialChargePass(float now)
        {
            NotifyDamagePatternMoveEnded();
            if (_airChargePass < guardianHard.aerialChargePasses) BeginHardAerialChargePass(now);
            else
            {
                CleanupActiveTelegraphAndTether();ClearHardAuxImpact();
                // The third charge has already exited the map. Re-enter on the center's
                // Z axis, without sweeping a fourth damage path or stopping at the edge.
                _airChargeFrom=new Vector3(0,_airChargeGroundY,arenaBoundaryRadius+CombatRadius+18);
                transform.position=_airChargeFrom;
                _guardianOrigin=_airChargeFrom;_guardianForward=Vector3.back;
                _hardPhase=_airChargePass*10+3;_airChargeInFlight=true;
                _guardian.Enter(GuardianStep.Recovery,now,guardianHard.aerialReturnSeconds);
                _networkCastStarted=now;_castTotalDuration=guardianHard.aerialReturnSeconds;
                _currentCastSkillName="연속 돌진 종료 · 중앙 복귀";
            }
        }
        public bool GuardianAerialDashNeeded(Transform actor)
        {
            if(!GuardianHard||_guardian.Pattern!=GuardianPattern.AerialCharge||actor==null||
                (_guardian.Step!=GuardianStep.Locked&&_guardian.Step!=GuardianStep.Dash))return false;
            if(_guardian.Step==GuardianStep.Locked&&_guardian.Remaining(GuardianNow)>.55f)return false;
            Vector3 relative=actor.position-_guardianOrigin;
            return GuardianHardGeometry.SweptChargeHits(relative.x,relative.z,_guardianForward.x,_guardianForward.z,
                Vector3.Distance(_guardianOrigin,_guardianEnd),Tuning.chargeHalfWidth,CombatRange.Radius(actor));
        }
        private void AdvanceTwistedExecution(float now)
        {
            PlayHardImpactCue();
            foreach (var member in GetLivingPartyMemberTransforms())
                if (_activeTelegraph != null && _activeTelegraph.IsPointInsideXZ(member.position) &&
                    (_hardPhase != 3 || HardRingHits(member.position)))
                    GuardianHit(member, _hardPhase < 2 ? Tuning.sweepDamage : Tuning.slamDamage);
            if (_hardPhase == 2) ClearHardSlamLure();
            if (_hardPhase == 3) { EndGuardian(now, true); return; }
            _hardPhase++;
            FinishHardImpact(now);
            _guardianDestinations.Clear();
            if (_hardPhase == 1)
            {
                _guardianSide = -_guardianSide;
                _guardian.Enter(GuardianStep.Locked, now, guardianHard.sweepSecondDelay);
                _currentCastSkillName = "뒤틀린 집행 · 반대쪽 휩쓸기";
            }
            else if (_hardPhase == 2)
            {
                _guardian.Enter(GuardianStep.Telegraph, now, guardianHard.twistedSlamPrepare);
                _currentCastSkillName = "뒤틀린 집행 · 중심 내려찍기";
            }
            else
            {
                _guardian.Enter(GuardianStep.Locked, now, guardianHard.slamRingDelay);
                _currentCastSkillName = "뒤틀린 집행 · 외곽 고리";
            }
            _networkCastStarted = now; _castTotalDuration = _guardian.Ends - now;
            SetMechanicAlert(_currentCastSkillName, Color.yellow, _castTotalDuration);
            if (_hardPhase == 2) BeginHardSlamLure(GetLivingPartyMemberTransforms());
        }

        private void BeginHardSlamLure(List<Transform> members)
        {
            ClearHardSlamLure();
            var dealers=members.FindAll(m=>m.GetComponent<PartyLife>() is PartyLife life && !life.IsTank && !life.IsHealer);
            if(dealers.Count==0)return;
            _hardLureTarget=dealers[Random.Range(0,dealers.Count)];_hardLurePrompted=false;
            _hardLureTarget.GetComponent<PlayerController>()?.StopForArrival();
            _hardLureTarget.GetComponent<PlayerCombatController>()?.InterruptForControl();
            _hardLureTarget.GetComponent<CharacterActor>()?.InterruptCast();
            _hardLureMarker=AttachedPartyMechanicMarker.SpawnTargetMarker(_hardLureTarget,.9f);
            _activePartyMarkers.Add(_hardLureMarker);
            SetMechanicAlert("딜러 유혹 · 보스 발밑으로 강제 이동 / 힐러는 원 밖에서 구출 준비",Color.magenta,_castTotalDuration);
        }
        private void TickHardSlamLure(float now)
        {
            if(_hardLureTarget==null)return;
            if(!GuardianLured(_hardLureTarget)||!CombatRange.LivingPartyMember(_hardLureTarget))
            {ClearHardSlamLure();return;}
            if(GuardianLureRescueWindow&&!_hardLurePrompted)
            {
                _hardLurePrompted=true;
                SetMechanicAlert("힐러 ["+LocalInputBindings.Label("Interact")+"] 구출! · 원형 내려찍기 임박",Color.cyan,Mathf.Max(.1f,_guardianEnds-now));
            }
            if(!RaidCombatAuthority.Active&&!LocalGameplayInput.Blocked&&LocalInputBindings.Pressed("Interact"))
                TryGuardianLureRescue(StageManager.Instance?.player?.transform,out _);
            if(_hardLureTarget==null)return;
            var pc=_hardLureTarget.GetComponent<PlayerController>();
            if(pc!=null&&(pc.IsBeingRescued||pc.IsBeingKnockedBack))return;
            if(_hardLureTarget.GetComponent<PartyBotAI>()?.IsBeingRescued==true)return;
            Vector3 toward=_guardianOrigin-_hardLureTarget.position;toward.y=0;
            if(toward.sqrMagnitude<.01f)return;
            Vector3 before=_hardLureTarget.position;
            _hardLureTarget.position=_hardLureCollision.Move(_hardLureTarget,before,CombatRange.Radius(_hardLureTarget),
                arenaBoundaryRadius,toward.normalized,Mathf.Min(toward.magnitude,guardianHard.lureWalkSpeed*Time.deltaTime),out _);
            _hardLureTarget.rotation=Quaternion.LookRotation(toward);
            if(RaidCombatAuthority.Host)RaidNetworkSession.Instance?.CommitSkillRelocation(_hardLureTarget);
        }
        public bool TryGuardianLureRescue(Transform healer,out string reason)
        {
            reason="구출 타이밍이 아닙니다";
            if(RaidCombatAuthority.Replica||!GuardianLureRescueWindow)return false;
            if(!CombatRange.LivingPartyMember(healer)||healer.GetComponent<PartyLife>()?.IsHealer!=true)
            {reason="힐러가 구출해야 합니다";return false;}
            var healerPc=healer.GetComponent<PlayerController>();
            if(healerPc!=null&&(healerPc.IsStunned||healerPc.IsBeingRescued||healerPc.IsBeingKnockedBack))return false;
            var member=_hardLureTarget;
            if(!CombatRange.InRange(healer,member,guardianHard.lureRescueRange)){reason="구출 대상이 너무 멉니다";return false;}
            Vector3 away=healer.position-_guardianOrigin;away.y=0;
            if(away.sqrMagnitude<.01f)return false;
            Vector3 destination=CombatRange.ClampToArena(healer.position+away.normalized*1.25f,arenaBoundaryRadius-CombatRange.Radius(member)-.5f);
            if(!GuardianHardGeometry.RescueOutside(BattleResurrection.FlatDistance(destination,_guardianOrigin),Tuning.slamRadius,CombatRange.Radius(member)))
            {reason="원형 피해 범위 밖으로 이동한 뒤 구출하세요";return false;}
            var pc=member.GetComponent<PlayerController>();
            if(pc!=null){pc.StartPulledByHealerRescue(destination);pc.AddShield(pc.maxHp*.15f,healer);}
            else member.GetComponent<PartyBotAI>()?.StartPulledByPlayerRescue(destination);
            reason="유혹 구출 성공";
            return !GuardianLured(member);
        }
        public void OnGuardianLureRescue(Transform member,Vector3 destination)
        {
            if(RaidCombatAuthority.Replica||!GuardianLured(member)||!GuardianLureRescueWindow||
                !GuardianHardGeometry.RescueOutside(BattleResurrection.FlatDistance(destination,_guardianOrigin),Tuning.slamRadius,CombatRange.Radius(member)))return;
            ClearHardSlamLure();
            CombatMetricsManager.Event("딜러 유혹 · 힐러 구출 성공");
            SetMechanicAlert("유혹 해제 · 중심 타격 후 외곽 고리 대비",Color.green,Mathf.Max(.1f,_guardianEnds-GuardianNow));
        }
        private void ClearHardSlamLure()
        {
            if(_hardLureMarker!=null){_activePartyMarkers.Remove(_hardLureMarker);Destroy(_hardLureMarker.gameObject);}
            _hardLureMarker=null;_hardLureTarget=null;_hardLurePrompted=false;
        }

        private bool GuardianHardDestination(Transform actor, int role, out Vector3 destination)
        {
            destination = actor.position;
            if (!GuardianHard) return false;
            if (_guardian.Pattern == GuardianPattern.AerialCharge)
            {
                Vector3 forward = new Vector3(-_airChargeLanding.x,0,-_airChargeLanding.z).normalized;
                Vector3 side = Vector3.Cross(Vector3.up,forward);
                float offset=Vector3.Dot(actor.position-_airChargeLanding,side);
                float clearance=Tuning.chargeHalfWidth+CombatRange.Radius(actor)+.5f;
                if (Mathf.Abs(offset)<clearance)
                {
                    float sign=Mathf.Abs(offset)>.1f?Mathf.Sign(offset):(role%2==0?1:-1);
                    destination=CombatRange.ClampToArena(actor.position+side*sign*(clearance-Mathf.Abs(offset)+.2f),
                        arenaBoundaryRadius-CombatRange.Radius(actor)-.5f);
                }
                destination.y=actor.position.y;
                return true;
            }
            if (_guardian.Pattern == GuardianPattern.Lockdown || _guardian.Pattern == GuardianPattern.FullLockdown)
            {
                if (_hardPhase == 0)
                    destination = actor == _guardianVictim ? HardAimPoint() : GuardianLineEscape(actor);
                else if (_hardPhase == 1)
                    destination = GuardianLineEscape(actor);
                else if (_hardPhase == 2)
                    destination = _hardExitOpen ? HardBreachPoint(guardianHard.wallRadius + 3.2f) : actor.position;
                else if (_hardPhase == 3 || _hardPhase == 4)
                    destination = GuardianChargeDestination(actor, role);
                else return false;
                destination.y = actor.position.y;
                return true;
            }
            if (_guardian.Pattern == GuardianPattern.Sweep)
            {
                destination = HardSweepSafePoint(actor);
                destination.y = actor.position.y; return true;
            }
            if (_guardian.Pattern == GuardianPattern.PlazaSlam ||
                _guardian.Pattern == GuardianPattern.TwistedExecution && _hardPhase >= 2)
            {
                Vector3 away = actor.position - _guardianOrigin; away.y = 0;
                if (away.sqrMagnitude < .01f) away = Vector3.forward;
                bool ring = _guardian.Pattern == GuardianPattern.PlazaSlam ? _guardianHardFollowup : _hardPhase == 3;
                float safe = ring ? Mathf.Max(2, Tuning.slamRadius - CombatRange.Radius(actor) - .35f)
                    : Tuning.slamRadius + CombatRange.Radius(actor) + .35f;
                destination = _guardianOrigin + away.normalized * safe;
                destination.y = actor.position.y; return true;
            }
            if (_guardian.Pattern == GuardianPattern.TwistedExecution && _hardPhase < 2)
            {
                destination = HardSweepSafePoint(actor);
                destination.y = actor.position.y; return true;
            }
            return false;
        }
        private Vector3 HardSweepSafePoint(Transform actor)
        {
            // Prepare near the rear edge of the boss, so a basic-speed actor can cross the
            // left/right dividing line after the first hit without passing through its collider.
            float radius = CombatRange.Radius(actor);
            return _guardianOrigin - _guardianForward * (CombatRadius + radius + .4f)
                - _guardianSide * (radius + .25f);
        }
        private bool HardRingHits(Vector3 position)
        {
            Vector3 delta = position - _guardianOrigin;
            return delta.x * delta.x + delta.z * delta.z > Tuning.slamRadius * Tuning.slamRadius;
        }
        private Vector3 GuardianChargeDestination(Transform actor, int role)
        {
            bool counter = Tuning.aiCounters && GuardianCounterRank(actor) >= 0;
            Vector3 side = Vector3.Cross(Vector3.up, _guardianForward);
            return counter ? _guardianOrigin + _guardianForward * (CombatRadius + .65f) :
                _guardianOrigin + side * (role % 2 == 0 ? -1 : 1) * (Tuning.chargeHalfWidth + 1.5f);
        }

        private void BuildHardCorruption()
        {
            if (_gargoyleInstance != null || _hardCorruption.Count > 0) return;
            var body = GetBossBodyTransform(); if (body == null) return;
            if (guardianHardGargoyleVisual != null)
            {
                foreach (var renderer in body.GetComponentsInChildren<Renderer>(true))
                { _gargoyleHiddenRenderers[renderer] = renderer.enabled; renderer.enabled = false; }
                _gargoyleInstance = Instantiate(guardianHardGargoyleVisual, body);
                _gargoyleInstance.name = "Guardian authored gargoyle visual";
                // Model scale never changes combat radius or the counter target collider.
                foreach (var collider in _gargoyleInstance.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                return;
            }
            if (!guardianHard.temporaryCorruptedVisuals) return;
            _hardCorruption.Add(GuardianShape("Temporary gargoyle left wing", body, new Vector3(-1.2f,1.2f,-.3f),
                new Vector3(2.3f,.14f,1.3f), new Color(.34f,.38f,.4f)));
            _hardCorruption.Add(GuardianShape("Temporary gargoyle right wing", body, new Vector3(1.2f,1.2f,-.3f),
                new Vector3(2.3f,.14f,1.3f), new Color(.34f,.38f,.4f)));
            _hardCorruption.Add(GuardianShape("Temporary gargoyle head", body, new Vector3(0,1.6f,.35f),
                new Vector3(.8f,.6f,.7f), new Color(.4f,.43f,.45f)));
            _hardCorruption.Add(GuardianShape("Temporary gargoyle left arm", body, new Vector3(-1f,.55f,.55f),
                new Vector3(.55f,1.5f,.55f), new Color(.39f,.42f,.44f)));
            _hardCorruption.Add(GuardianShape("Temporary gargoyle right arm", body, new Vector3(1f,.55f,.55f),
                new Vector3(.55f,1.5f,.55f), new Color(.39f,.42f,.44f)));
        }
        private void ClearHardCorruption()
        {
            if (_gargoyleInstance != null) { Destroy(_gargoyleInstance); _gargoyleInstance = null; }
            foreach (var pair in _gargoyleHiddenRenderers) if (pair.Key != null) pair.Key.enabled = pair.Value;
            _gargoyleHiddenRenderers.Clear();
            foreach (var part in _hardCorruption) if (part != null) Destroy(part.gameObject);
            _hardCorruption.Clear();
        }
        private void RestoreGargoyleJump()
        {
            if (_gargoyleJumpBody != null) _gargoyleJumpBody.localPosition = _gargoyleJumpOrigin;
            _gargoyleJumpBody = null;
        }
        private void BeginGuardianClash(float now)
        {
            _clashPhase = 1;
            _guardianOrigin = new Vector3(0, transform.position.y, 0);
            transform.position = _guardianOrigin;
            _guardian.Enter(GuardianStep.Telegraph, now, guardianHard.clashPrepareSeconds);
            _networkCastStarted = now; _castTotalDuration = guardianHard.clashPrepareSeconds;
            _currentCastSkillName = "🛡 탱커 집결 · 중앙 파란 원";
            _clashCircle = AoETelegraph.Spawn(AoETelegraphType.Circle_Radius6, _guardianOrigin,
                Vector3.forward, guardianHard.clashPrepareSeconds + guardianHard.clashCounterSeconds,
                0, guardianHard.clashCircleRadius);
            _clashCircle.SetTankGuideStyle();
            var label = new GameObject("Guardian clash shield icon").AddComponent<TextMesh>();
            _clashShieldLabel = label.transform;
            label.transform.position = new Vector3(_guardianOrigin.x, _guardianOrigin.y + .18f, _guardianOrigin.z);
            label.transform.rotation = Quaternion.Euler(90, 0, 0);
            label.text = "🛡 탱커"; label.anchor = TextAnchor.MiddleCenter;
            label.characterSize = .42f; label.fontSize = 36; label.color = new Color(.35f, .8f, 1f);
            SetMechanicAlert("🛡 탱커 집결 · 파란 원 안에서 보스 정면을 향해 [" +
                LocalInputBindings.Label("Counter") + "] 준비", new Color(.25f,.7f,1f), _castTotalDuration);
        }
        private void TickGuardianClash(float now)
        {
            if (_clashPhase == 3 && !CombatRange.LivingPartyMember(_clashTank))
            { FinishGuardianClash(now, false, "힘겨루기 중 탱커 사망"); return; }
            if (_clashPhase == 3 && now >= _clashNextPulse && !_guardian.Due(now))
            {
                float before = _clashTank.GetComponent<PartyLife>()?.Hp ?? 0;
                GuardianHit(_clashTank, guardianHard.clashPulseDamage, CombatDamageKind.Direct, "guardian.clash.pulse");
                if ((_clashTank.GetComponent<PartyLife>()?.Hp ?? before) < before)
                    CombatMetricsManager.Event("힘겨루기 맥동 피격: " + _clashTank.name);
                _clashNextPulse += guardianHard.clashPulseInterval;
                if (!CombatRange.LivingPartyMember(_clashTank))
                { FinishGuardianClash(now, false, "힘겨루기 중 탱커 사망"); return; }
            }
            if (!_guardian.Due(now))
            {
                if (_clashPhase == 3)
                {
                    _currentCastSkillName = $"🛡 버티기 {_guardian.Remaining(now):F1}초 · 다음 맥동 {Mathf.Max(0,_clashNextPulse-now):F1}초";
                    if (!_clashShockwaveWarned && _guardian.Remaining(now) <= 1.5f)
                    { _clashShockwaveWarned = true; SetMechanicAlert("힘겨루기 해제 충격파 임박 · 파티 체력 준비", Color.yellow, 1.5f); }
                }
                return;
            }
            if (_clashPhase == 1)
            {
                _clashPhase = 2;
                var tanks = GetLivingPartyMemberTransforms().FindAll(t => t.GetComponent<PartyLife>()?.IsTank == true);
                if (tanks.Count > 0)
                {
                    Vector3 facing = tanks[0].position - _guardianOrigin; facing.y = 0;
                    if (facing.sqrMagnitude > .01f) _guardianForward = facing.normalized;
                }
                _guardian.Enter(GuardianStep.CounterWindow, now, guardianHard.clashCounterSeconds);
                CombatCounterTarget.Ensure(transform).OpenWindow(guardianHard.clashCounterSeconds);
                _networkCastStarted = now; _castTotalDuration = guardianHard.clashCounterSeconds;
                _currentCastSkillName = "🛡 카운터! · 탱커 전용 힘겨루기";
                SetMechanicAlert(_currentCastSkillName, new Color(.4f,.85f,1f), _castTotalDuration);
            }
            else if (_clashPhase == 2) FinishGuardianClash(now, false, "탱커 카운터 미성공");
            else if (_clashPhase == 3) FinishGuardianClash(now, true, "힘겨루기 해제 충격파");
            else if (_clashPhase == 4)
            {
                _clashPhase = 5;
                _guardian.Enter(GuardianStep.Recovery, now, guardianHard.clashRecoverySeconds);
                _networkCastStarted=now;_castTotalDuration=guardianHard.clashRecoverySeconds;
                _currentCastSkillName = "충격파 후 회복 · 재정렬";
                SetMechanicAlert(_currentCastSkillName, Color.green, guardianHard.clashRecoverySeconds);
            }
            else if (_clashPhase == 5) EndGuardian(now,recoveryPaid:true);
        }
        private void FinishGuardianClash(float now, bool success, string eventName)
        {
            if (_clashPhase != 2 && _clashPhase != 3) return;
            CombatCounterTarget.Ensure(transform).CloseWindow();
            if (_clashCircle != null) { _clashCircle.gameObject.SetActive(false); Destroy(_clashCircle.gameObject); _clashCircle = null; }
            if (_clashShieldLabel != null) { Destroy(_clashShieldLabel.gameObject); _clashShieldLabel = null; }
            CombatMetricsManager.Event(eventName);
            PlayHardImpactCue();
            foreach (var member in GetLivingPartyMemberTransforms())
            {
                float before = member.GetComponent<PartyLife>()?.Hp ?? 0;
                GuardianHit(member, success ? guardianHard.clashShockwaveDamage : guardianHard.clashFailureDamage,
                    CombatDamageKind.Area, success ? "guardian.clash.release" : "guardian.clash.failure");
                if ((member.GetComponent<PartyLife>()?.Hp ?? before) < before)
                    CombatMetricsManager.Event("힘겨루기 충격파 피격: " + member.name);
                if (!success)
                {
                    var player = member.GetComponent<PlayerController>();
                    if (player != null) player.StartKnockbackXZ(_guardianOrigin, 3);
                    else member.GetComponent<PartyBotAI>()?.StartKnockbackXZ(_guardianOrigin, 3);
                }
            }
            Debug.Log("[Guardian] " + eventName);
            _clashPhase = success ? 4 : 5;
            _clashSucceeded = success;
            float duration = success ? guardianHard.clashOpeningSeconds : guardianHard.clashRecoverySeconds;
            _guardian.Enter(success ? GuardianStep.Groggy : GuardianStep.Recovery, now, duration);
            SyncGuardianTiming();
            fsmState = success ? BossFSMState.Groggy : BossFSMState.CastingSkill;
            _networkCastStarted = now; _castTotalDuration = duration;
            _currentCastSkillName = success ? "가고일 밀어내기 · 공격 기회" : "힘겨루기 실패 · 회복 시간";
            SetMechanicAlert(_currentCastSkillName, success ? Color.yellow : Color.red, duration);
        }
        private bool GuardianClashCounterSucceeded(Transform actor)
        {
            if (!GuardianClashCounterAllowed(actor)) return false;
            _clashPhase = 3; _clashTank = actor;
            _clashShockwaveWarned = false;
            if (_clashCircle != null) { _clashCircle.gameObject.SetActive(false); Destroy(_clashCircle.gameObject); _clashCircle = null; }
            if (_clashShieldLabel != null) { Destroy(_clashShieldLabel.gameObject); _clashShieldLabel = null; }
            CombatCounterTarget.Ensure(transform).CloseWindow();
            Vector3 facing = _guardianOrigin - actor.position; facing.y = 0;
            if (facing.sqrMagnitude > .01f) actor.rotation = Quaternion.LookRotation(facing);
            _guardian.Enter(GuardianStep.Locked, GuardianNow, guardianHard.clashHoldSeconds);
            SyncGuardianTiming();
            fsmState = BossFSMState.CastingSkill;
            _clashNextPulse = GuardianNow + guardianHard.clashPulseInterval;
            _networkCastStarted = GuardianNow; _castTotalDuration = guardianHard.clashHoldSeconds;
            _currentCastSkillName = "🛡 힘겨루기 · 탱커 보호 / 보스 공격 / 종료 충격파";
            SetMechanicAlert(_currentCastSkillName, new Color(.3f,.7f,1f), guardianHard.clashHoldSeconds);
            Debug.Log("[Guardian] tank clash counter succeeded actor=" + RaidCombatAuthority.Id(actor));
            CombatMetricsManager.Event("탱커 힘겨루기 카운터 성공: " + actor.name);
            return true;
        }
        private void ClearGuardianHard()
        {
            ClearHardSlamLure();
            if (_airChargeInFlight)
                transform.position=new Vector3(transform.position.x,_airChargeGroundY,transform.position.z);
            _airChargeInFlight=false;_airChargePass=0;
            ClearClashPresentation();
            RestoreGargoyleJump();
            StopHardTransformationPresentation();
            if (_clashCircle != null) { _clashCircle.gameObject.SetActive(false); Destroy(_clashCircle.gameObject); _clashCircle = null; }
            if (_clashShieldLabel != null) { Destroy(_clashShieldLabel.gameObject); _clashShieldLabel = null; }
            _clashTank = null; _clashPhase = 0;
            if (_hardWall != null) { _hardWall.SetActive(false); Destroy(_hardWall); _hardWall = null; }
            _hardWallPieces.Clear(); _hardExitOpen = false;
        }
        private void BeginHardTransformationPresentation(double elapsedSeconds = 0)
        {
            _hardTransformationViewActive = true;
            if (guardianHardTransformationPresentation != null) try
            {
                _hardTransformationInstance = Instantiate(guardianHardTransformationPresentation, transform.position,
                    Quaternion.identity, transform);
                if (!_hardTransformationInstance.Play(this, StageManager.Instance?.player, elapsedSeconds))
                    StopHardTransformationPresentation();
                else return;
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("Guardian transformation presentation failed; using temporary visuals: " + exception);
                StopHardTransformationPresentation();
            }
            // Runtime fallback preserves authored Timeline assets and needs no inspector setup.
            _hardTransformationViewActive = true;
            _gargoyleJumpBody = GetBossBodyTransform();
            if (_gargoyleJumpBody != null) _gargoyleJumpOrigin = _gargoyleJumpBody.localPosition;
            _hardGameplayCamera = QuarterViewCamera.LocalCamera?.UnityCamera;
            if (_hardGameplayCamera == null) return;
            _hardGameplayCameraWasEnabled = _hardGameplayCamera.enabled;
            var shot = new GameObject("Guardian transformation cinematic camera");
            _hardTransformationCamera = shot.AddComponent<Camera>();
            _hardTransformationCamera.CopyFrom(_hardGameplayCamera);
            _hardTransformationCamera.orthographic = false;
            _hardTransformationCamera.fieldOfView = 50;
            _hardTransformationCamera.tag = "MainCamera";
            _hardGameplayCamera.enabled = false;
            _hardTransformationCamera.enabled = true;
            UpdateHardTransformationFallback(Mathf.Clamp01((float)elapsedSeconds / Mathf.Max(.1f, _castTotalDuration)));
        }
        private void TickHardReplicaTransformation()
        {
            if (GuardianHardTransformationActive && currentHp > 0 && !StageManager.CombatSuspended)
            {
                float elapsed = Mathf.Max(0, GuardianNow - _guardianStarted);
                if (!_hardTransformationViewActive) BeginHardTransformationPresentation(elapsed);
                if (_gargoyleJumpBody != null)
                    UpdateHardTransformationFallback(Mathf.Clamp01(elapsed / Mathf.Max(.1f, _guardianEnds - _guardianStarted)));
            }
            else if (_hardTransformationViewActive)
            {
                RestoreGargoyleJump();
                StopHardTransformationPresentation();
            }
        }
        private void UpdateHardTransformationFallback(float progress)
        {
            float height = (float)GuardianClashRules.JumpHeight(progress);
            if (_gargoyleJumpBody != null)
                _gargoyleJumpBody.localPosition = _gargoyleJumpOrigin + Vector3.up * height;
            if (_hardTransformationCamera == null) return;
            Vector3 center = transform.position;
            Vector3 offset = Vector3.Lerp(new Vector3(9,5,-12), new Vector3(-7,7,-10), progress);
            _hardTransformationCamera.transform.position = center + offset + Vector3.up * (height * .6f);
            _hardTransformationCamera.transform.LookAt(center + Vector3.up * (2 + height * .6f));
        }
        private void StopHardTransformationPresentation()
        {
            _hardTransformationViewActive = false;
            if (_hardTransformationInstance != null)
            {
                _hardTransformationInstance.StopAndRestore();
                _hardTransformationInstance.gameObject.SetActive(false);
                Destroy(_hardTransformationInstance.gameObject);
                _hardTransformationInstance = null;
            }
            if (_hardTransformationCamera != null)
            {
                _hardTransformationCamera.enabled = false;
                Destroy(_hardTransformationCamera.gameObject);
                _hardTransformationCamera = null;
                if (_hardGameplayCamera != null) _hardGameplayCamera.enabled = _hardGameplayCameraWasEnabled;
                _hardGameplayCamera = null;
            }
        }
    }
}
