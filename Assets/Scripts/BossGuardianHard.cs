using System.Collections.Generic;
using UnityEngine;

namespace RaidRogue
{
    // Hard extends the existing guardian encounter. All clocks and damage stay on the host.
    public partial class BossController
    {
        [SerializeField] private RaidStageCutscenePresentation guardianHardTransformationPresentation;
        private RaidStageCutscenePresentation _hardTransformationInstance;
        private GameObject _hardWall;
        private readonly List<GameObject> _hardWallPieces = new List<GameObject>();
        private readonly List<Transform> _hardCorruption = new List<Transform>();
        private int _hardCrackIndex, _hardPhase;
        private bool _hardExitOpen;
        private Vector3 _hardBreachDirection;

        private bool BeginGuardianHard(GuardianPattern pattern, float now, List<Transform> members)
        {
            _hardPhase = 0;
            if (pattern == GuardianPattern.Lockdown || pattern == GuardianPattern.FullLockdown)
            {
                _guardianVictim = HardLockdownTarget(members);
                if (_guardianVictim == null) { EndGuardian(now); return true; }
                ChooseHardCrack(_guardianVictim.position);
                BuildHardWall();
                BeginHardAim(now);
                return true;
            }
            if (pattern == GuardianPattern.Transformation)
            {
                _guardian.Enter(GuardianStep.Telegraph, now, guardianHard.transformationSeconds);
                _networkCastStarted = now; _castTotalDuration = guardianHard.transformationSeconds;
                _currentCastSkillName = "오염된 마을 수호자로 변신";
                CleanupActiveTelegraphAndTether();
                BeginHardTransformationPresentation();
                SetMechanicAlert("오염된 석상이 갈라집니다 · 변신 중 공격 중지", Color.magenta, _castTotalDuration);
                return true;
            }
            if (pattern == GuardianPattern.TwistedExecution)
            {
                _guardian.Enter(GuardianStep.Telegraph, now, guardianHard.twistedSweepPrepare);
                _networkCastStarted = now; _castTotalDuration = guardianHard.twistedSweepPrepare;
                _activeTelegraph = AoETelegraph.Spawn(AoETelegraphType.FrontCone_120Deg, _guardianOrigin,
                    _guardianSide, _castTotalDuration, 0, 36, 180);
                _currentCastSkillName = "뒤틀린 집행 · 좌우 2회 → 중심 → 외곽";
                SetMechanicAlert(_currentCastSkillName, Color.yellow, _castTotalDuration);
                return true;
            }
            return false;
        }

        private bool TickGuardianHard(float now)
        {
            GuardianPattern pattern = _guardian.Pattern;
            if (pattern == GuardianPattern.Transformation)
            {
                if (_guardian.Due(now))
                {
                    _guardianHard.FinishTransformation();
                    BuildHardCorruption();
                    currentBossTitle = "STAGE 1 : 오염된 마을 수호자 · 하드";
                    EndGuardian(now);
                }
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

        private bool BeginGuardianHardFollowup(float now)
        {
            if (_guardianHardFollowup || !GuardianHard) return false;
            float seconds;
            if (_guardian.Pattern == GuardianPattern.Punishment) seconds = guardianHard.punishmentSecondDelay;
            else if (_guardian.Pattern == GuardianPattern.Sweep) seconds = guardianHard.sweepSecondDelay;
            else if (_guardian.Pattern == GuardianPattern.PlazaSlam) seconds = guardianHard.slamRingDelay;
            else return false;
            _guardianHardFollowup = true;
            _guardian.Enter(GuardianStep.Locked, now, seconds);
            _networkCastStarted = now; _castTotalDuration = seconds;
            if (_activeTelegraph != null) { _activeTelegraph.gameObject.SetActive(false); Destroy(_activeTelegraph.gameObject); _activeTelegraph = null; }
            _guardianDestinations.Clear();
            if (_guardian.Pattern == GuardianPattern.Punishment)
            {
                _currentCastSkillName = "침입자 징벌 · 두 번째 강타";
                if (CombatRange.LivingPartyMember(_guardianVictim))
                    _activeTelegraph = AoETelegraph.Spawn(AoETelegraphType.Circle_Radius6, _guardianVictim.position, Vector3.forward, seconds, 0, .8f);
            }
            else if (_guardian.Pattern == GuardianPattern.Sweep)
            {
                _guardianSide = -_guardianSide;
                _currentCastSkillName = "돌봉 휩쓸기 · 반대쪽 연속 공격";
                _activeTelegraph = AoETelegraph.Spawn(AoETelegraphType.FrontCone_120Deg, _guardianOrigin,
                    _guardianSide, seconds, 0, 36, 180);
            }
            else
            {
                _currentCastSkillName = "광장 내려찍기 · 외곽 고리 / 안으로";
                _activeTelegraph = AoETelegraph.Spawn(AoETelegraphType.Donut_Inner4_Outer12, _guardianOrigin,
                    Vector3.forward, seconds, guardianHard.slamRingInner, guardianHard.slamRingOuter);
            }
            SetMechanicAlert(_currentCastSkillName, Color.yellow, seconds);
            return true;
        }

        private Transform HardLockdownTarget(List<Transform> members)
        {
            // Prefer a living damage dealer; healer, then tank are safe fallback candidates.
            var dealers = members.FindAll(m => m.GetComponent<PartyLife>() is PartyLife life && !life.IsTank && !life.IsHealer);
            return dealers.Count > 0 ? dealers[Random.Range(0, dealers.Count)] : GuardianLineTarget(members);
        }
        private void ChooseHardCrack(Vector3 near)
        {
            Vector3 flat = new Vector3(near.x, 0, near.z);
            if (flat.sqrMagnitude < .01f) flat = -_guardianForward;
            float angle = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
            _hardCrackIndex = Mathf.RoundToInt(angle / 90f);
            float radians = _hardCrackIndex * Mathf.PI * .5f;
            _hardBreachDirection = new Vector3(Mathf.Sin(radians), 0, Mathf.Cos(radians));
        }
        private Vector3 HardBreachPoint(float radius) => _hardBreachDirection * radius + Vector3.up * _guardianOrigin.y;
        private Vector3 HardAimPoint() => Vector3.Lerp(_guardianOrigin, HardBreachPoint(guardianHard.wallRadius), .78f);

        private void BuildHardWall()
        {
            ClearGuardianHard();
            _hardWall = new GameObject("Guardian hard · breakable ring wall");
            int count = Mathf.Max(12, guardianHard.wallSegments);
            int crack = ((_hardCrackIndex * count / 4) % count + count) % count;
            float radius = guardianHard.wallRadius;
            float width = 2 * Mathf.PI * radius / count - .03f;
            for (int i = 0; i < count; i++)
            {
                float angle = i * 2 * Mathf.PI / count;
                Vector3 radial = new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle));
                var piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
                piece.name = i == crack ? "Large cracked exit · target" : "Stone wall";
                piece.transform.SetParent(_hardWall.transform);
                piece.transform.position = radial * radius + Vector3.up * (guardianHard.wallHeight * .5f);
                piece.transform.rotation = Quaternion.LookRotation(radial);
                piece.transform.localScale = new Vector3(width, guardianHard.wallHeight, .42f);
                var material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                material.color = i == crack ? new Color(1f, .48f, .12f) : new Color(.35f, .34f, .32f);
                piece.GetComponent<Renderer>().material = material;
                if (i == crack)
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
            _hardCrackIndex = crack; _hardExitOpen = false;
        }
        private void BeginHardAim(float now)
        {
            _hardPhase = 0;
            _guardian.Enter(GuardianStep.Telegraph, now, guardianHard.lockdownLockAt);
            _networkCastStarted = now; _castTotalDuration = guardianHard.lockdownLockAt;
            _guardianDestinations.Clear();
            if (_activeTelegraph != null) { _activeTelegraph.gameObject.SetActive(false); Destroy(_activeTelegraph.gameObject); }
            ClearActivePartyMarkers();
            _activePartyMarkers.Add(AttachedPartyMechanicMarker.SpawnSpreadMarker(_guardianVictim, .75f));
            TrackGuardianLine();
            _activeTelegraph = AoETelegraph.Spawn(AoETelegraphType.Rectangle, _guardianOrigin, _guardianForward,
                guardianHard.lockdownLineAt, guardianHard.lockdownWidth * .5f,
                BoundedGuardianLineLength(_guardianOrigin, _guardianForward, guardianHard.wallRadius * 2));
            _currentCastSkillName = "봉쇄 명령 · 균열로 파쇄선 유도";
            SetMechanicAlert(_currentCastSkillName, Color.magenta, guardianHard.lockdownLineAt);
        }
        private void TickHardLockdown(float now, bool full)
        {
            if (_hardPhase == 0 && !CombatRange.LivingPartyMember(_guardianVictim))
            {
                _guardianVictim = HardLockdownTarget(GetLivingPartyMemberTransforms());
                if (_guardianVictim == null) { EndGuardian(now); return; }
                BeginHardAim(now); // Full 3+2+6 seconds for the replacement target.
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
                _currentCastSkillName = "봉쇄 명령 · 조준 고정 / 선 밖으로";
                SetMechanicAlert(_currentCastSkillName, Color.yellow, _castTotalDuration);
                PlayGuardianLockCue(); return;
            }
            if (_hardPhase == 1)
            {
                _hardPhase = 2;
                foreach (var member in GetLivingPartyMemberTransforms())
                    if (_activeTelegraph != null && _activeTelegraph.IsPointInsideXZ(member.position)) GuardianHit(member, guardianHard.lockdownLineDamage);
                if (HardLineHitsCrack()) OpenHardExit();
                if (_activeTelegraph != null) { _activeTelegraph.gameObject.SetActive(false); Destroy(_activeTelegraph.gameObject); }
                _guardian.Enter(GuardianStep.Escape, now, guardianHard.lockdownEraseAt - guardianHard.lockdownLineAt);
                _networkCastStarted = now; _castTotalDuration = _guardian.Ends - now;
                _activeTelegraph = AoETelegraph.Spawn(AoETelegraphType.Circle_Radius6, Vector3.zero, Vector3.forward,
                    _castTotalDuration, 0, guardianHard.wallRadius - .3f);
                _currentCastSkillName = _hardExitOpen ? "내부 소거 · 열린 균열로 탈출" : "균열 파쇄 실패 · 내부 소거 대비";
                SetMechanicAlert(_currentCastSkillName, Color.red, _castTotalDuration);
                _guardianDestinations.Clear(); return;
            }
            if (_hardPhase == 2)
            {
                foreach (var member in GetLivingPartyMemberTransforms())
                    if (_activeTelegraph != null && _activeTelegraph.IsPointInsideXZ(member.position)) GuardianHit(member, guardianHard.lockdownFailureDamage);
                ClearGuardianHard(); CleanupActiveTelegraphAndTether(); _guardianDestinations.Clear();
                if (full) BeginFullCharge(now);
                else
                {
                    _hardPhase = 8;
                    _guardian.Enter(GuardianStep.Recovery, now, guardianHard.postFailureRecovery);
                    _currentCastSkillName = "봉쇄 종료 · 재집결 / 회복";
                    SetMechanicAlert(_currentCastSkillName, Color.green, guardianHard.postFailureRecovery);
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
                _currentCastSkillName = "완전 봉쇄 · 푸른 약점 카운터";
                SetMechanicAlert(_currentCastSkillName, Color.cyan, _castTotalDuration); return;
            }
            if (_hardPhase == 4)
            {
                _hardPhase = 5; CombatCounterTarget.Ensure(transform).CloseWindow();
                _guardian.Enter(GuardianStep.Dash, now, Vector3.Distance(_guardianOrigin, _guardianEnd) / Mathf.Max(1, Tuning.chargeSpeed));
                _networkCastStarted = now; _castTotalDuration = _guardian.Ends - now;
                _currentCastSkillName = "완전 봉쇄 · 직선 돌진"; return;
            }
            if (_hardPhase == 6)
            {
                _hardPhase = 7;
                _guardian.Enter(GuardianStep.StaggerCheck, now, Tuning.overloadSeconds);
                _networkCastStarted = now; _castTotalDuration = Tuning.overloadSeconds;
                SetGuardianKneeling(true);
                _currentCastSkillName = "완전 봉쇄 · 수호핵 무력화";
                SetMechanicAlert(_currentCastSkillName, Color.magenta, _castTotalDuration); return;
            }
            if (_hardPhase == 7)
            {
                foreach (var member in GetLivingPartyMemberTransforms()) GuardianHit(member, Tuning.overloadFailureDamage);
                SetGuardianKneeling(false);
                _hardPhase = 8;
                _guardian.Enter(GuardianStep.Recovery, now, guardianHard.postFailureRecovery);
                SetMechanicAlert("무력화 실패 · 회복 시간", Color.yellow, guardianHard.postFailureRecovery); return;
            }
            if (_hardPhase == 8) EndGuardian(now);
        }

        private bool HardLineHitsCrack()
        {
            return GuardianHardGeometry.LineHitsCrack(_guardianOrigin.x, _guardianOrigin.z,
                _guardianForward.x, _guardianForward.z, _hardBreachDirection.x, _hardBreachDirection.z,
                guardianHard.wallRadius, guardianHard.wallSegments, guardianHard.lockdownWidth * .5f);
        }
        private void OpenHardExit()
        {
            if (_hardCrackIndex < 0 || _hardCrackIndex >= _hardWallPieces.Count) return;
            _hardWallPieces[_hardCrackIndex].SetActive(false);
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
            _hardPhase = 6;
            _guardian.Enter(GuardianStep.Recovery, now, guardianHard.fullRecovery);
            _networkCastStarted = now; _castTotalDuration = guardianHard.fullRecovery;
            _currentCastSkillName = "완전 봉쇄 · 재집결 / 회복";
            SetMechanicAlert(_currentCastSkillName, Color.green, _castTotalDuration);
        }

        private void AdvanceTwistedExecution(float now)
        {
            foreach (var member in GetLivingPartyMemberTransforms())
                if (_activeTelegraph != null && _activeTelegraph.IsPointInsideXZ(member.position))
                    GuardianHit(member, _hardPhase < 2 ? Tuning.sweepDamage : Tuning.slamDamage);
            if (_hardPhase == 3) { EndGuardian(now); return; }
            _hardPhase++;
            if (_activeTelegraph != null) { _activeTelegraph.gameObject.SetActive(false); Destroy(_activeTelegraph.gameObject); }
            _guardianDestinations.Clear();
            if (_hardPhase == 1)
            {
                _guardianSide = -_guardianSide;
                _guardian.Enter(GuardianStep.Locked, now, guardianHard.twistedGap);
                _activeTelegraph = AoETelegraph.Spawn(AoETelegraphType.FrontCone_120Deg, _guardianOrigin,
                    _guardianSide, guardianHard.twistedGap, 0, 36, 180);
                _currentCastSkillName = "뒤틀린 집행 · 반대쪽 휩쓸기";
            }
            else if (_hardPhase == 2)
            {
                _guardian.Enter(GuardianStep.Telegraph, now, guardianHard.twistedSlamPrepare);
                _activeTelegraph = AoETelegraph.Spawn(AoETelegraphType.Circle_Radius6, _guardianOrigin,
                    Vector3.forward, guardianHard.twistedSlamPrepare, 0, Tuning.slamRadius);
                _currentCastSkillName = "뒤틀린 집행 · 중심 내려찍기";
            }
            else
            {
                _guardian.Enter(GuardianStep.Locked, now, guardianHard.twistedGap);
                _activeTelegraph = AoETelegraph.Spawn(AoETelegraphType.Donut_Inner4_Outer12, _guardianOrigin,
                    Vector3.forward, guardianHard.twistedGap, guardianHard.slamRingInner, guardianHard.slamRingOuter);
                _currentCastSkillName = "뒤틀린 집행 · 외곽 고리";
            }
            _networkCastStarted = now; _castTotalDuration = _guardian.Ends - now;
            SetMechanicAlert(_currentCastSkillName, Color.yellow, _castTotalDuration);
        }

        private bool GuardianHardDestination(Transform actor, int role, out Vector3 destination)
        {
            destination = actor.position;
            if (!GuardianHard) return false;
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
            if (_guardian.Pattern == GuardianPattern.PlazaSlam && _guardianHardFollowup ||
                _guardian.Pattern == GuardianPattern.TwistedExecution && _hardPhase >= 2)
            {
                Vector3 away = actor.position - _guardianOrigin; away.y = 0;
                if (away.sqrMagnitude < .01f) away = Vector3.forward;
                float safe = _guardian.Pattern == GuardianPattern.TwistedExecution && _hardPhase == 2
                    ? Tuning.slamRadius + 1.5f : Mathf.Max(2, guardianHard.slamRingInner - 2);
                destination = _guardianOrigin + away.normalized * safe;
                destination.y = actor.position.y; return true;
            }
            if (_guardian.Pattern == GuardianPattern.TwistedExecution && _hardPhase < 2)
            {
                destination = _guardianOrigin - _guardianSide * (CombatRadius + 2) +
                    _guardianForward * ((role - 2.5f) * 1.4f);
                destination.y = actor.position.y; return true;
            }
            return false;
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
            if (!guardianHard.temporaryCorruptedVisuals || _hardCorruption.Count > 0) return;
            var body = GetBossBodyTransform(); if (body == null) return;
            _hardCorruption.Add(GuardianShape("Temporary brass drive shaft", body, new Vector3(0,.1f,-.7f),
                new Vector3(.48f,.48f,1.3f), new Color(.62f,.43f,.14f)));
            _hardCorruption.Add(GuardianShape("Temporary corrupted gear left", body, new Vector3(-.8f,.65f,0),
                new Vector3(.75f,.75f,.28f), new Color(.53f,.32f,.13f)));
            _hardCorruption.Add(GuardianShape("Temporary corrupted gear right", body, new Vector3(.8f,.65f,0),
                new Vector3(.75f,.75f,.28f), new Color(.53f,.32f,.13f)));
            SetBossBodyColor(new Color(.43f,.31f,.24f));
        }
        private void ClearHardCorruption()
        {
            foreach (var part in _hardCorruption) if (part != null) Destroy(part.gameObject);
            _hardCorruption.Clear();
        }
        private void ClearGuardianHard()
        {
            StopHardTransformationPresentation();
            if (_hardWall != null) { _hardWall.SetActive(false); Destroy(_hardWall); _hardWall = null; }
            _hardWallPieces.Clear(); _hardExitOpen = false;
        }
        private void BeginHardTransformationPresentation()
        {
            if (guardianHardTransformationPresentation == null) return;
            try
            {
                _hardTransformationInstance = Instantiate(guardianHardTransformationPresentation, transform.position,
                    Quaternion.identity, transform);
                if (!_hardTransformationInstance.Play(this, StageManager.Instance?.player, 0))
                    StopHardTransformationPresentation();
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("Guardian transformation presentation failed; using temporary visuals: " + exception);
                StopHardTransformationPresentation();
            }
        }
        private void StopHardTransformationPresentation()
        {
            if (_hardTransformationInstance == null) return;
            _hardTransformationInstance.StopAndRestore();
            _hardTransformationInstance.gameObject.SetActive(false);
            Destroy(_hardTransformationInstance.gameObject);
            _hardTransformationInstance = null;
        }
    }
}
