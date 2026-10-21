using System.Collections;
using UnityEngine;

namespace RaidRogue
{
    public enum PartyBotRole
    {
        Tank,      // 탱커 봇 (파란색 캡슐) - 북쪽(Z+) 이동 및 보스 시선 북쪽 주차, 단발 탱버 시 뎀감기 방어
        Healer,    // 힐러 봇 (초록색 캡슐) - 남서쪽 위치, 초록색 연결선 힐 & 위험 장판 80% 시전 시 '구출(Rescue)' 발동
        RangedDPS  // 원딜 봇 (주황색 캡슐) - 남동쪽 외곽 무빙 및 투사체 발사
    }

    /// <summary>
    /// 4인 협동 레이드용 3명의 AI 파티원 봇 (탱커 / 힐러 / 원딜) 컨트롤러 (Step 2 ~ Step 6 통합):
    /// - 모든 거리 및 이동 계산은 XZ 평면(Y축 무시)을 기준으로 수행됩니다.
    /// - Step 6 협동 기믹 AI:
    ///   ① [4인 산개 (Spread)] 시 서로 원이 겹치지 않도록 흩어지며 플레이어와도 거리를 벌림
    ///   ② [4인 쉐어 (Stack)] 시 내 캐릭터(플레이어) 위치로 일제히 달려와 4인 뭉치기(1/4 피해 분담) 수행
    ///   ③ Stage 3 [도넛 장판 + 4인 산개] 시 도넛 안전지대(4m 이내) 북/서/동에 정교하게 산개하여 남쪽 자리를 비워줌
    ///   ④ Stage 3 [외곽 넉백 직후 4인 쉐어] 시 외곽으로 밀려난 직후 플레이어에게 달려와 쉐어 수행
    /// </summary>
    public class PartyBotAI : MonoBehaviour
    {
        [Header("Role & References")]
        public PartyBotRole role = PartyBotRole.Tank;
        public BossController targetBoss;
        public PlayerController humanPlayer;
        public PartyBotAI tankBotReference;
        public Renderer bodyRenderer;

        [Header("Movement Settings (XZ Plane)")]
        public float moveSpeed = 7.8f;
        public float rotationSpeed = 540f;

        [Header("Health Settings")]
        public float maxHp = 1600f;
        public float currentHp = 1600f;
        public bool isDead;

        [Header("Tank Settings (North Z+ Parking & Defensive)")]
        public float tankNorthParkingDistance = 4.8f;
        public float tankAttackInterval = 1.8f;
        public int tankAttackDamage = 45;
        public bool isTankDefensiveActive;
        private float _tankDefensiveTimer;

        [Header("Healer Settings (Southwest, Green Beam Heal & Rescue)")]
        public Vector3 healerDesiredOffsetXZ = new Vector3(-6.8f, 0f, -6.5f);
        public float healCheckInterval = 1.3f;
        public float healAmount = 260f;
        public float healBeamDuration = 0.55f;

        [Tooltip("구출(Rescue) 쿨타임 (30초)")]
        public float rescueCooldown = 30.0f;
        [Tooltip("보스 원형/도넛 장판 시전 진행도 80% 이상일 때 구출 발동")]
        public float rescueTriggerCastProgress = 0.80f;

        [Header("Ranged DPS Settings (Southeast Strafe & Projectile)")]
        public Vector3 rangedBaseOffsetXZ = new Vector3(6.6f, 0f, -6.0f);
        public float rangedStrafeAmplitude = 2.4f;
        public float rangedStrafeSpeed = 1.6f;
        public float rangedFireInterval = 1.25f;
        public float projectileSpeed = 18f;
        public int projectileDamage = 55;

        public float HpRatio => maxHp > 0f ? Mathf.Clamp01(currentHp / maxHp) : 0f;
        public bool NeedsHealing => !isDead && currentHp < maxHp - 1f;
        public float RescueCooldownRemaining => _rescueCooldownTimer;
        public bool IsRescueReady => _rescueCooldownTimer <= 0f;

        private float _actionTimer;
        private float _strafePhase;
        private float _deadReviveTimer;
        private float _rescueCooldownTimer;
        private float _berserkerAttackSpeedTimer;
        private AoETelegraph _lastRescuedTelegraph;

        // 넉백(밀쳐내기) 상태
        private bool _isBeingKnockedBack;
        private Vector3 _knockbackTargetXZ;
        private float _knockbackSpeed = 24f;

        private LineRenderer _effectBeam;
        private Coroutine _beamCoroutine;
        private Color _defaultBodyColor;

        // 머리 위 3D 체력바
        private Transform _hpBarRoot;
        private Transform _hpFillBar;
        private TextMesh _nameLabelText;

        private void Start()
        {
            if (targetBoss == null)
            {
                targetBoss = FindFirstObjectByType<BossController>();
            }

            if (humanPlayer == null)
            {
                humanPlayer = FindFirstObjectByType<PlayerController>();
            }

            if (bodyRenderer == null)
            {
                bodyRenderer = GetComponent<Renderer>();
            }

            if (tankBotReference == null)
            {
                PartyBotAI[] bots = FindObjectsByType<PartyBotAI>(FindObjectsSortMode.None);
                foreach (PartyBotAI bot in bots)
                {
                    if (bot.role == PartyBotRole.Tank)
                    {
                        tankBotReference = bot;
                        break;
                    }
                }
            }

            _defaultBodyColor = role switch
            {
                PartyBotRole.Tank => new Color(0.18f, 0.52f, 1.0f, 1f),
                PartyBotRole.Healer => new Color(0.18f, 0.88f, 0.38f, 1f),
                PartyBotRole.RangedDPS => new Color(1.0f, 0.50f, 0.12f, 1f),
                _ => Color.white
            };

            if (role == PartyBotRole.Tank)
            {
                maxHp = 1800f;
                currentHp = maxHp;
                if (targetBoss != null && targetBoss.aggroTarget == null)
                {
                    targetBoss.SetAggroTarget(transform);
                }
            }
            else
            {
                maxHp = 1000f;
                currentHp = maxHp;
            }

            CreateBeamLineRenderer();
            CreateOverheadHpBar();
        }

        private void Update()
        {
            if (Time.timeScale <= 0.001f || targetBoss == null)
            {
                return;
            }

            if (_rescueCooldownTimer > 0f)
            {
                _rescueCooldownTimer = Mathf.Max(0f, _rescueCooldownTimer - Time.deltaTime);
            }

            if (_berserkerAttackSpeedTimer > 0f)
            {
                _berserkerAttackSpeedTimer = Mathf.Max(0f, _berserkerAttackSpeedTimer - Time.deltaTime);
            }

            if (isDead)
            {
                UpdateDeadState();
                UpdateOverheadHpBar();
                return;
            }

            if (_tankDefensiveTimer > 0f)
            {
                _tankDefensiveTimer -= Time.deltaTime;
                if (_tankDefensiveTimer <= 0f)
                {
                    _tankDefensiveTimer = 0f;
                    isTankDefensiveActive = false;
                    ApplyBodyColor(_defaultBodyColor);
                }
            }

            // 보스가 쓰러진 연출 중일 때는 제자리 대기
            if (targetBoss.IsBossDefeatedCollapse)
            {
                UpdateOverheadHpBar();
                return;
            }

            // 외곽 넉백(밀쳐내기) 중일 때는 넉백 이동 우선 처리
            if (_isBeingKnockedBack)
            {
                UpdateKnockbackMovementXZ();
                UpdateOverheadHpBar();
                return;
            }

            switch (role)
            {
                case PartyBotRole.Tank:
                    UpdateTankAI();
                    break;

                case PartyBotRole.Healer:
                    UpdateHealerAI();
                    break;

                case PartyBotRole.RangedDPS:
                    UpdateRangedDPSAI();
                    break;
            }

            UpdateOverheadHpBar();
        }

        /// <summary>
        /// Step 6: 스테이지 클리어 후 카드 선택 시 봇 전원 체력 100% 회복 및 초기 위치로 재배치
        /// </summary>
        public void ResetBotForNewStage(Vector3 spawnPos)
        {
            isDead = false;
            _isBeingKnockedBack = false;
            isTankDefensiveActive = false;
            _tankDefensiveTimer = 0f;
            _rescueCooldownTimer = 0f;
            _lastRescuedTelegraph = null;
            currentHp = maxHp;

            transform.position = new Vector3(spawnPos.x, 1.0f, spawnPos.z);
            transform.rotation = Quaternion.identity;
            ApplyBodyColor(_defaultBodyColor);

            if (role == PartyBotRole.Tank && targetBoss != null)
            {
                targetBoss.SetAggroTarget(transform);
            }
        }

        /// <summary>
        /// Step 6 Stage 3 복합 기믹: 보스 중심에서 외곽으로 강하게 밀쳐내는 넉백(Knockback)
        /// </summary>
        public void StartKnockbackXZ(Vector3 originXZ, float knockbackDistance, float duration = 0.45f)
        {
            if (isDead)
            {
                return;
            }

            Vector3 dirXZ = new Vector3(
                transform.position.x - originXZ.x,
                0f,
                transform.position.z - originXZ.z
            );

            if (dirXZ.sqrMagnitude < 0.001f)
            {
                dirXZ = role switch
                {
                    PartyBotRole.Tank => Vector3.forward,
                    PartyBotRole.Healer => new Vector3(-0.7f, 0f, -0.7f).normalized,
                    PartyBotRole.RangedDPS => new Vector3(0.7f, 0f, -0.7f).normalized,
                    _ => Vector3.forward
                };
            }
            else
            {
                dirXZ.Normalize();
            }

            Vector3 targetPos = transform.position + dirXZ * knockbackDistance;
            Vector2 flat = new Vector2(targetPos.x, targetPos.z);
            if (flat.magnitude > 16.5f)
            {
                flat = flat.normalized * 16.5f;
            }

            _knockbackTargetXZ = new Vector3(flat.x, 1.0f, flat.y);
            float dist = Vector2.Distance(
                new Vector2(transform.position.x, transform.position.z),
                new Vector2(_knockbackTargetXZ.x, _knockbackTargetXZ.z)
            );
            _knockbackSpeed = Mathf.Max(14f, dist / Mathf.Max(0.15f, duration));
            _isBeingKnockedBack = true;
        }

        private void UpdateKnockbackMovementXZ()
        {
            Vector3 current = transform.position;
            Vector3 target = new Vector3(_knockbackTargetXZ.x, 1.0f, _knockbackTargetXZ.z);
            transform.position = Vector3.MoveTowards(current, target, _knockbackSpeed * Time.deltaTime);

            Vector3 diffXZ = new Vector3(target.x - transform.position.x, 0f, target.z - transform.position.z);
            if (diffXZ.sqrMagnitude < 0.04f)
            {
                transform.position = target;
                _isBeingKnockedBack = false;
            }
        }

        public void ResetRescueCooldownImmediate()
        {
            _rescueCooldownTimer = 0f;
            _lastRescuedTelegraph = null;
        }

        public void ApplyBerserkerAttackSpeedBoost(float duration)
        {
            _berserkerAttackSpeedTimer = Mathf.Max(_berserkerAttackSpeedTimer, duration);
            FloatingTextPopup.Spawn(
                transform.position + Vector3.up * 2.2f,
                "공격속도 증가! (광전사의 함성)",
                new Color(1f, 0.4f, 0.45f, 1f),
                false
            );
        }

        private float GetAttackSpeedMultiplier()
        {
            return _berserkerAttackSpeedTimer > 0f ? 1.6f : 1.0f;
        }

        /// <summary>
        /// Step 6 파티 협동 기믹 위치 계산:
        /// - [4인 쉐어 (Stack)] 중이면 플레이어 주변 0.8m 원 안으로 일제히 달려와 뭉쳐줌!
        /// - [4인 산개 (Spread)] 중이면 서로 겹치지 않는 방향으로 흩어지며 플레이어와도 거리를 유지함!
        /// </summary>
        private bool TryGetPartyMechanicDestinationXZ(out Vector3 mechanicDestXZ)
        {
            mechanicDestXZ = transform.position;
            if (targetBoss == null)
            {
                return false;
            }

            Vector3 bossPos = targetBoss.transform.position;

            // ① [4인 쉐어 (Stack)] 발동 중 -> 내 캐릭터(플레이어) 위치로 봇 3명이 즉시 달려와 뭉침!
            if (targetBoss.IsStackMechanicActive && humanPlayer != null)
            {
                Vector3 playerPos = humanPlayer.transform.position;
                Vector3 stackOffset = role switch
                {
                    PartyBotRole.Tank => new Vector3(0f, 0f, 0.85f),
                    PartyBotRole.Healer => new Vector3(-0.80f, 0f, -0.55f),
                    PartyBotRole.RangedDPS => new Vector3(0.80f, 0f, -0.55f),
                    _ => Vector3.zero
                };

                mechanicDestXZ = new Vector3(
                    playerPos.x + stackOffset.x,
                    1.0f,
                    playerPos.z + stackOffset.z
                );
                return true;
            }

            // ② [4인 산개 (Spread)] 발동 중 -> 서로 원이 겹치지 않도록 흩어짐!
            if (targetBoss.IsSpreadMechanicActive)
            {
                bool isDonutCombo = targetBoss.ActiveTelegraph != null &&
                                    targetBoss.ActiveTelegraph.telegraphType == AoETelegraphType.Donut_Inner4_Outer12;

                if (isDonutCombo)
                {
                    // Stage 3 [도넛 장판(내경 4m 안전지대) + 4인 산개(반경 1.65m)] 동시 발동:
                    // 도넛 안전지대(반경 4m 이내)의 북/서/동 2.85m 지점으로 봇 3명이 산개하여 남쪽(0, -2.85)을 플레이어 자리로 비워둠!
                    Vector3 donutSpreadOffset = role switch
                    {
                        PartyBotRole.Tank => new Vector3(0f, 0f, 2.85f),       // 북쪽
                        PartyBotRole.Healer => new Vector3(-2.85f, 0f, 0f),    // 서쪽
                        PartyBotRole.RangedDPS => new Vector3(2.85f, 0f, 0f),  // 동쪽
                        _ => Vector3.zero
                    };
                    mechanicDestXZ = new Vector3(bossPos.x + donutSpreadOffset.x, 1.0f, bossPos.z + donutSpreadOffset.z);
                }
                else
                {
                    // 일반 [4인 산개 (반경 2.6m)]: 전장 북쪽 / 남서쪽 외곽 / 남동쪽 외곽으로 넓게 산개
                    Vector3 spreadOffset = role switch
                    {
                        PartyBotRole.Tank => new Vector3(0f, 0f, 8.2f),
                        PartyBotRole.Healer => new Vector3(-8.5f, 0f, -7.2f),
                        PartyBotRole.RangedDPS => new Vector3(8.5f, 0f, -7.2f),
                        _ => Vector3.zero
                    };

                    mechanicDestXZ = new Vector3(bossPos.x + spreadOffset.x, 1.0f, bossPos.z + spreadOffset.z);

                    // 만약 플레이어가 내 산개 위치 근처로 다가오면 반대 방향으로 더 비켜줌
                    if (humanPlayer != null)
                    {
                        Vector2 botXZ = new Vector2(mechanicDestXZ.x, mechanicDestXZ.z);
                        Vector2 playerXZ = new Vector2(humanPlayer.transform.position.x, humanPlayer.transform.position.z);
                        float minSafeDist = targetBoss.CurrentSpreadRadius * 2.25f;
                        Vector2 diff = botXZ - playerXZ;
                        if (diff.magnitude < minSafeDist)
                        {
                            Vector2 awayDir = diff.sqrMagnitude > 0.01f ? diff.normalized : new Vector2(0f, 1f);
                            Vector2 pushed = playerXZ + awayDir * (minSafeDist + 0.8f);
                            if (pushed.magnitude > 16.5f)
                            {
                                pushed = pushed.normalized * 16.5f;
                            }
                            mechanicDestXZ = new Vector3(pushed.x, 1.0f, pushed.y);
                        }
                    }
                }

                return true;
            }

            return false;
        }

        // =====================================================================
        // 1. 탱커 봇 AI: 항상 보스의 북쪽(Z+ 방향)으로 이동해 보스 시선을 북쪽으로 고정(주차)
        // =====================================================================
        private void UpdateTankAI()
        {
            Vector3 bossPos = targetBoss.transform.position;
            Vector3 desiredPos = new Vector3(
                bossPos.x,
                1.0f,
                bossPos.z + tankNorthParkingDistance
            );

            if (TryGetPartyMechanicDestinationXZ(out Vector3 mechanicPos))
            {
                desiredPos = mechanicPos;
            }
            else if (targetBoss.ActiveTelegraph != null &&
                     targetBoss.ActiveTelegraph.telegraphType == AoETelegraphType.Donut_Inner4_Outer12)
            {
                // 도넛 장판 시 북쪽 도넛 안전지대(2.8m) 안으로 진입
                desiredPos = new Vector3(bossPos.x, 1.0f, bossPos.z + 2.8f);
            }

            MoveTowardsXZ(desiredPos, BossLookPointXZ(), targetBoss.IsStackMechanicActive || targetBoss.IsSpreadMechanicActive ? 1.25f : 1.0f);

            if (targetBoss.aggroTarget != transform)
            {
                targetBoss.SetAggroTarget(transform);
            }

            _actionTimer += Time.deltaTime * GetAttackSpeedMultiplier();
            if (_actionTimer >= tankAttackInterval)
            {
                _actionTimer = 0f;
                targetBoss.ReceiveBotAttackDamage(tankAttackDamage, false);
                ShowBeamEffect(
                    transform.position + Vector3.up * 0.2f,
                    targetBoss.transform.position + Vector3.up * 1.2f,
                    new Color(0.25f, 0.65f, 1f, 0.9f),
                    0.14f,
                    0.12f
                );
            }
        }

        public void ActivateTankDefensive(float duration)
        {
            if (isDead)
            {
                return;
            }

            isTankDefensiveActive = true;
            _tankDefensiveTimer = duration;
            ApplyBodyColor(new Color(0.35f, 0.95f, 1f, 1f));

            FloatingTextPopup.Spawn(
                transform.position + Vector3.up * 2.3f,
                "TANK 뎀감기 ON! (단발 탱버 방어)",
                new Color(0.3f, 0.95f, 1f, 1f),
                true
            );
        }

        public void KillTankByUninterceptedTether()
        {
            if (isDead)
            {
                return;
            }

            currentHp = 0f;
            isDead = true;
            isTankDefensiveActive = false;
            _deadReviveTimer = 6.5f;

            transform.position = new Vector3(transform.position.x, 0.5f, transform.position.z);
            transform.rotation = Quaternion.Euler(0f, 0f, 90f);
            ApplyBodyColor(new Color(0.25f, 0.25f, 0.28f, 1f));

            FloatingTextPopup.Spawn(
                transform.position + Vector3.up * 2.4f,
                "TANK DEAD!! (줄 가로채기 실패!)",
                new Color(1f, 0.15f, 0.15f, 1f),
                true
            );

            if (targetBoss != null && humanPlayer != null)
            {
                targetBoss.SetAggroTarget(humanPlayer.transform);
            }
        }

        public void ReviveBot()
        {
            isDead = false;
            currentHp = maxHp;
            transform.position = new Vector3(transform.position.x, 1.0f, transform.position.z);
            transform.rotation = Quaternion.identity;
            ApplyBodyColor(_defaultBodyColor);

            FloatingTextPopup.Spawn(
                transform.position + Vector3.up * 2.3f,
                "TANK 부활 완료!",
                new Color(0.3f, 1f, 0.55f, 1f),
                false
            );

            if (role == PartyBotRole.Tank && targetBoss != null)
            {
                targetBoss.SetAggroTarget(transform);
            }
        }

        private void UpdateDeadState()
        {
            _deadReviveTimer -= Time.deltaTime;
            if (_deadReviveTimer <= 0f)
            {
                ReviveBot();
            }
        }

        // =====================================================================
        // 2. 힐러 봇 AI: 남서쪽 위치 유지, 초록색 연결선 회복 & 위험 장판 80% 시 '구출(Rescue)'
        // =====================================================================
        private void UpdateHealerAI()
        {
            Vector3 bossPos = targetBoss.transform.position;
            Vector3 desiredSafePos = new Vector3(
                bossPos.x + healerDesiredOffsetXZ.x,
                1.0f,
                bossPos.z + healerDesiredOffsetXZ.z
            );

            if (TryGetPartyMechanicDestinationXZ(out Vector3 mechanicPos))
            {
                desiredSafePos = mechanicPos;
            }
            else if (targetBoss.ActiveTelegraph != null &&
                     targetBoss.ActiveTelegraph.telegraphType == AoETelegraphType.Donut_Inner4_Outer12)
            {
                desiredSafePos = new Vector3(bossPos.x - 2.1f, 1.0f, bossPos.z - 2.1f);
            }

            MoveTowardsXZ(desiredSafePos, BossLookPointXZ(), targetBoss.IsStackMechanicActive || targetBoss.IsSpreadMechanicActive ? 1.3f : 1.0f);

            // [Step 4 핵심 기믹] 보스의 원형/도넛 장판 시전이 80% 이상 진행되었는데도
            // 플레이어가 위험 반경 안에 서 있으면 힐러가 안전한 자기 쪽으로 빠르게 끌어당기는 '구출(Rescue)' 발동!
            // (단, 4인 산개 중일 때는 산개 원이 겹치면 안 되므로 구출을 보류하거나 도넛 안전지대 남쪽으로 당김)
            CheckAndTriggerHealerRescue(desiredSafePos);

            _actionTimer += Time.deltaTime * GetAttackSpeedMultiplier();
            if (_actionTimer >= healCheckInterval)
            {
                _actionTimer = 0f;
                TryHealPartyMember();
            }
        }

        private void CheckAndTriggerHealerRescue(Vector3 healerSafeSpotXZ)
        {
            if (humanPlayer == null || targetBoss == null || !targetBoss.IsCasting)
            {
                return;
            }

            AoETelegraph telegraph = targetBoss.ActiveTelegraph;
            if (telegraph == null)
            {
                return;
            }

            bool isCircleOrDonut =
                telegraph.telegraphType == AoETelegraphType.Circle_Radius6 ||
                telegraph.telegraphType == AoETelegraphType.Donut_Inner4_Outer12;

            if (!isCircleOrDonut)
            {
                return;
            }

            if (targetBoss.CastProgress < rescueTriggerCastProgress ||
                _rescueCooldownTimer > 0f ||
                _lastRescuedTelegraph == telegraph)
            {
                return;
            }

            if (telegraph.IsPointInsideXZ(humanPlayer.transform.position))
            {
                _lastRescuedTelegraph = telegraph;
                _rescueCooldownTimer = rescueCooldown;

                transform.position = new Vector3(healerSafeSpotXZ.x, 1.0f, healerSafeSpotXZ.z);
                Vector3 rescueDestinationXZ = new Vector3(
                    transform.position.x + 0.9f,
                    1.0f,
                    transform.position.z + 0.5f
                );

                if (telegraph.telegraphType == AoETelegraphType.Circle_Radius6)
                {
                    Vector3 fromBoss = new Vector3(
                        rescueDestinationXZ.x - targetBoss.transform.position.x,
                        0f,
                        rescueDestinationXZ.z - targetBoss.transform.position.z
                    );
                    if (fromBoss.magnitude < telegraph.outerRadius + 1.2f)
                    {
                        fromBoss = fromBoss.normalized * (telegraph.outerRadius + 1.5f);
                        rescueDestinationXZ = new Vector3(
                            targetBoss.transform.position.x + fromBoss.x,
                            1.0f,
                            targetBoss.transform.position.z + fromBoss.z
                        );
                    }
                }
                else if (telegraph.telegraphType == AoETelegraphType.Donut_Inner4_Outer12)
                {
                    // 만약 도넛 장판 + 4인 산개 동시 발동 중이면 남쪽 도넛 안전지대(0, -2.85)로 구출해 산개 원이 안 겹치게 함!
                    if (targetBoss.IsSpreadMechanicActive)
                    {
                        rescueDestinationXZ = new Vector3(
                            targetBoss.transform.position.x,
                            1.0f,
                            targetBoss.transform.position.z - 2.85f
                        );
                    }
                    else
                    {
                        rescueDestinationXZ = new Vector3(
                            targetBoss.transform.position.x - 1.8f,
                            1.0f,
                            targetBoss.transform.position.z - 1.8f
                        );
                    }
                }

                FaceTargetXZ(humanPlayer.transform.position);
                humanPlayer.StartPulledByHealerRescue(rescueDestinationXZ);
                ShowTrackingRescueBeam(humanPlayer.transform, 0.55f);

                FloatingTextPopup.Spawn(
                    transform.position + Vector3.up * 2.4f,
                    "HEALER 구출(Rescue) 발동!! (쿨타임 30초)",
                    new Color(0.3f, 0.95f, 1f, 1f),
                    true
                );

                targetBoss.ShowExternalAlert(
                    "힐러 봇의 [구출(Rescue)] 발동! 장판 80% 시전 시점에 위험 반경의 플레이어를 끌어당겨 살려냈습니다! (쿨타임 30초)",
                    new Color(0.3f, 0.95f, 1f, 1f),
                    4.0f
                );
            }
        }

        private void TryHealPartyMember()
        {
            bool playerNeedsHeal = humanPlayer != null && humanPlayer.NeedsHealing;
            bool tankNeedsHeal = tankBotReference != null && tankBotReference.NeedsHealing;

            if (!playerNeedsHeal && !tankNeedsHeal)
            {
                return;
            }

            bool healPlayerFirst = false;
            if (playerNeedsHeal && tankNeedsHeal)
            {
                healPlayerFirst = humanPlayer.HpRatio <= tankBotReference.HpRatio;
            }
            else if (playerNeedsHeal)
            {
                healPlayerFirst = true;
            }

            if (healPlayerFirst && humanPlayer != null)
            {
                FaceTargetXZ(humanPlayer.transform.position);
                humanPlayer.ReceiveHeal(healAmount);
                ShowTrackingHealBeam(humanPlayer.transform, healBeamDuration);
            }
            else if (tankBotReference != null)
            {
                FaceTargetXZ(tankBotReference.transform.position);
                tankBotReference.ReceiveHeal(healAmount);
                ShowTrackingHealBeam(tankBotReference.transform, healBeamDuration);
            }
        }

        // =====================================================================
        // 3. 원딜 봇 AI: 남동쪽 외곽에서 무빙하며 보스에게 주기적으로 투사체 발사
        // =====================================================================
        private void UpdateRangedDPSAI()
        {
            _strafePhase += Time.deltaTime * rangedStrafeSpeed;

            Vector3 bossPos = targetBoss.transform.position;
            Vector3 strafeOffset = new Vector3(
                Mathf.Sin(_strafePhase) * rangedStrafeAmplitude,
                0f,
                Mathf.Cos(_strafePhase) * (rangedStrafeAmplitude * 0.65f)
            );

            Vector3 desiredSoutheastPos = new Vector3(
                bossPos.x + rangedBaseOffsetXZ.x + strafeOffset.x,
                1.0f,
                bossPos.z + rangedBaseOffsetXZ.z + strafeOffset.z
            );

            if (TryGetPartyMechanicDestinationXZ(out Vector3 mechanicPos))
            {
                desiredSoutheastPos = mechanicPos;
            }
            else if (targetBoss.ActiveTelegraph != null &&
                     targetBoss.ActiveTelegraph.telegraphType == AoETelegraphType.Donut_Inner4_Outer12)
            {
                desiredSoutheastPos = new Vector3(bossPos.x + 2.2f, 1.0f, bossPos.z - 2.2f);
            }

            MoveTowardsXZ(desiredSoutheastPos, BossLookPointXZ(), targetBoss.IsStackMechanicActive || targetBoss.IsSpreadMechanicActive ? 1.3f : 1.0f);

            _actionTimer += Time.deltaTime * GetAttackSpeedMultiplier();
            if (_actionTimer >= rangedFireInterval)
            {
                _actionTimer = 0f;
                FireProjectileAtBoss();
            }
        }

        private void FireProjectileAtBoss()
        {
            if (targetBoss == null)
            {
                return;
            }

            FaceTargetXZ(targetBoss.transform.position);

            Vector3 spawnPos = transform.position + transform.forward * 0.65f + Vector3.up * 0.25f;
            RangedBotProjectile.Spawn(spawnPos, targetBoss, projectileSpeed, projectileDamage);
        }

        // =====================================================================
        // 공통 이동 / 체력 / 이펙트 유틸리티 (XZ 평면 기준)
        // =====================================================================
        private Vector3 BossLookPointXZ()
        {
            return targetBoss != null ? targetBoss.transform.position : Vector3.zero;
        }

        private void MoveTowardsXZ(Vector3 destinationWorld, Vector3 lookTargetWhenArrived, float speedMultiplier = 1.0f)
        {
            Vector3 currentPos = transform.position;
            Vector3 toDestXZ = new Vector3(
                destinationWorld.x - currentPos.x,
                0f,
                destinationWorld.z - currentPos.z
            );

            float distXZ = toDestXZ.magnitude;
            if (distXZ > 0.08f)
            {
                Vector3 moveDir = toDestXZ / distXZ;
                float step = Mathf.Min(distXZ, moveSpeed * speedMultiplier * Time.deltaTime);
                transform.position = new Vector3(
                    currentPos.x + moveDir.x * step,
                    destinationWorld.y,
                    currentPos.z + moveDir.z * step
                );

                Quaternion moveRot = Quaternion.LookRotation(moveDir, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, moveRot, rotationSpeed * Time.deltaTime);
            }
            else
            {
                FaceTargetXZ(lookTargetWhenArrived);
            }
        }

        private void FaceTargetXZ(Vector3 targetWorldPos)
        {
            Vector3 toTargetXZ = new Vector3(
                targetWorldPos.x - transform.position.x,
                0f,
                targetWorldPos.z - transform.position.z
            );

            if (toTargetXZ.sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(toTargetXZ.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
            }
        }

        public void TakeDamage(float rawAmount, bool allowLethal = false)
        {
            if (isDead)
            {
                return;
            }

            float finalAmount = isTankDefensiveActive ? rawAmount * 0.25f : rawAmount;
            float minClamp = allowLethal ? 0f : 120f;
            currentHp = Mathf.Max(minClamp, currentHp - finalAmount);

            string msg = isTankDefensiveActive
                ? $"-{Mathf.RoundToInt(finalAmount)} (탱커 뎀감기 방어!)"
                : $"-{Mathf.RoundToInt(finalAmount)}";
            Color col = isTankDefensiveActive
                ? new Color(0.35f, 0.9f, 1f, 1f)
                : new Color(1f, 0.45f, 0.45f, 1f);

            FloatingTextPopup.Spawn(transform.position + Vector3.up * 2.0f, msg, col, isTankDefensiveActive);

            if (allowLethal && currentHp <= 0.1f)
            {
                KillTankByUninterceptedTether();
            }
        }

        public void ReceiveHeal(float amount)
        {
            if (isDead || currentHp >= maxHp)
            {
                return;
            }

            float before = currentHp;
            currentHp = Mathf.Min(maxHp, currentHp + amount);
            float actualHealed = currentHp - before;

            if (actualHealed > 0.5f)
            {
                FloatingTextPopup.Spawn(
                    transform.position + Vector3.up * 2.1f,
                    $"+{Mathf.RoundToInt(actualHealed)} HP",
                    new Color(0.25f, 1f, 0.45f, 1f),
                    false
                );
            }
        }

        private void ApplyBodyColor(Color color)
        {
            if (bodyRenderer == null || bodyRenderer.material == null)
            {
                return;
            }
            bodyRenderer.material.color = color;
            if (bodyRenderer.material.HasProperty("_BaseColor"))
            {
                bodyRenderer.material.SetColor("_BaseColor", color);
            }
            if (bodyRenderer.material.HasProperty("_Color"))
            {
                bodyRenderer.material.SetColor("_Color", color);
            }
        }

        private void CreateBeamLineRenderer()
        {
            GameObject beamObj = new GameObject("Bot_EffectBeam");
            beamObj.transform.SetParent(transform, false);

            _effectBeam = beamObj.AddComponent<LineRenderer>();
            _effectBeam.useWorldSpace = true;
            _effectBeam.positionCount = 2;
            _effectBeam.numCapVertices = 4;
            _effectBeam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _effectBeam.receiveShadows = false;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }
            _effectBeam.sharedMaterial = new Material(shader) { color = Color.white };
            _effectBeam.enabled = false;
        }

        private void ShowBeamEffect(Vector3 start, Vector3 end, Color color, float width, float duration)
        {
            if (_effectBeam == null)
            {
                return;
            }

            if (_beamCoroutine != null)
            {
                StopCoroutine(_beamCoroutine);
            }
            _beamCoroutine = StartCoroutine(StaticBeamRoutine(start, end, color, width, duration));
        }

        private void ShowTrackingHealBeam(Transform healTarget, float duration)
        {
            if (_effectBeam == null || healTarget == null)
            {
                return;
            }

            if (_beamCoroutine != null)
            {
                StopCoroutine(_beamCoroutine);
            }
            _beamCoroutine = StartCoroutine(TrackingBeamRoutine(
                healTarget,
                duration,
                new Color(0.15f, 1f, 0.38f, 0.95f),
                new Color(0.45f, 1f, 0.65f, 0.95f),
                0.22f
            ));
        }

        private void ShowTrackingRescueBeam(Transform rescueTarget, float duration)
        {
            if (_effectBeam == null || rescueTarget == null)
            {
                return;
            }

            if (_beamCoroutine != null)
            {
                StopCoroutine(_beamCoroutine);
            }
            _beamCoroutine = StartCoroutine(TrackingBeamRoutine(
                rescueTarget,
                duration,
                new Color(1f, 0.92f, 0.25f, 1f),
                new Color(0.25f, 0.95f, 1f, 1f),
                0.30f
            ));
        }

        private IEnumerator StaticBeamRoutine(Vector3 start, Vector3 end, Color color, float width, float duration)
        {
            _effectBeam.enabled = true;
            _effectBeam.startWidth = width;
            _effectBeam.endWidth = width * 0.5f;
            _effectBeam.startColor = color;
            _effectBeam.endColor = color;
            _effectBeam.SetPosition(0, start);
            _effectBeam.SetPosition(1, end);

            yield return new WaitForSeconds(duration);
            _effectBeam.enabled = false;
            _beamCoroutine = null;
        }

        private IEnumerator TrackingBeamRoutine(Transform target, float duration, Color startCol, Color endCol, float width)
        {
            _effectBeam.enabled = true;
            _effectBeam.startColor = startCol;
            _effectBeam.endColor = endCol;
            _effectBeam.startWidth = width;
            _effectBeam.endWidth = width * 0.75f;

            float elapsed = 0f;
            while (elapsed < duration && target != null)
            {
                elapsed += Time.deltaTime;
                _effectBeam.SetPosition(0, transform.position + Vector3.up * 0.25f);
                _effectBeam.SetPosition(1, target.position + Vector3.up * 0.25f);
                yield return null;
            }

            _effectBeam.enabled = false;
            _beamCoroutine = null;
        }

        private void CreateOverheadHpBar()
        {
            _hpBarRoot = new GameObject("Overhead_HpBar").transform;
            _hpBarRoot.SetParent(transform, false);
            _hpBarRoot.localPosition = new Vector3(0f, 1.55f, 0f);

            GameObject bgObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
            bgObj.name = "HP_Bg";
            Destroy(bgObj.GetComponent<Collider>());
            bgObj.transform.SetParent(_hpBarRoot, false);
            bgObj.transform.localPosition = Vector3.zero;
            bgObj.transform.localScale = new Vector3(1.25f, 0.15f, 1f);
            SetUnlitColor(bgObj.GetComponent<Renderer>(), new Color(0.1f, 0.1f, 0.12f, 0.9f));

            GameObject fillObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
            fillObj.name = "HP_Fill";
            Destroy(fillObj.GetComponent<Collider>());
            _hpFillBar = fillObj.transform;
            _hpFillBar.SetParent(_hpBarRoot, false);
            _hpFillBar.localPosition = new Vector3(0f, 0f, -0.01f);
            _hpFillBar.localScale = new Vector3(1.20f, 0.11f, 1f);

            Color roleColor = role switch
            {
                PartyBotRole.Tank => new Color(0.25f, 0.65f, 1f, 1f),
                PartyBotRole.Healer => new Color(0.25f, 0.95f, 0.45f, 1f),
                PartyBotRole.RangedDPS => new Color(1f, 0.58f, 0.15f, 1f),
                _ => Color.white
            };
            SetUnlitColor(fillObj.GetComponent<Renderer>(), roleColor);

            string roleLabel = role switch
            {
                PartyBotRole.Tank => "TANK (북쪽 주차)",
                PartyBotRole.Healer => "HEALER (구출: 준비됨)",
                PartyBotRole.RangedDPS => "RANGED (원딜)",
                _ => "BOT"
            };

            GameObject nameObj = new GameObject("NameLabel");
            nameObj.transform.SetParent(_hpBarRoot, false);
            nameObj.transform.localPosition = new Vector3(0f, 0.24f, 0f);
            nameObj.transform.localScale = Vector3.one * 0.075f;
            _nameLabelText = nameObj.AddComponent<TextMesh>();
            _nameLabelText.text = roleLabel;
            _nameLabelText.fontSize = 40;
            _nameLabelText.fontStyle = FontStyle.Bold;
            _nameLabelText.anchor = TextAnchor.MiddleCenter;
            _nameLabelText.alignment = TextAlignment.Center;
            _nameLabelText.color = roleColor;
        }

        private void UpdateOverheadHpBar()
        {
            if (_hpBarRoot == null)
            {
                return;
            }

            Camera cam = Camera.main;
            if (cam != null)
            {
                _hpBarRoot.rotation = cam.transform.rotation;
            }

            if (_hpFillBar != null)
            {
                float ratio = HpRatio;
                const float fullWidth = 1.20f;
                _hpFillBar.localScale = new Vector3(fullWidth * ratio, 0.11f, 1f);
                _hpFillBar.localPosition = new Vector3(-fullWidth * (1f - ratio) * 0.5f, 0f, -0.01f);
            }

            if (_nameLabelText == null)
            {
                return;
            }

            if (targetBoss != null && targetBoss.IsStackMechanicActive)
            {
                _nameLabelText.text = $"{role} [4인 쉐어 집결 중!]";
                _nameLabelText.color = new Color(1f, 0.88f, 0.2f, 1f);
                return;
            }

            if (targetBoss != null && targetBoss.IsSpreadMechanicActive)
            {
                _nameLabelText.text = $"{role} [4인 산개 위치 이동!]";
                _nameLabelText.color = new Color(1f, 0.45f, 0.85f, 1f);
                return;
            }

            if (role == PartyBotRole.Tank)
            {
                if (isDead)
                {
                    _nameLabelText.text = $"TANK [사망 - 부활 {_deadReviveTimer:F1}s]";
                    _nameLabelText.color = new Color(1f, 0.3f, 0.3f, 1f);
                }
                else if (isTankDefensiveActive)
                {
                    _nameLabelText.text = "TANK [방어 뎀감기 ON!]";
                    _nameLabelText.color = new Color(0.35f, 0.95f, 1f, 1f);
                }
                else
                {
                    _nameLabelText.text = "TANK (북쪽 주차)";
                    _nameLabelText.color = new Color(0.25f, 0.65f, 1f, 1f);
                }
            }
            else if (role == PartyBotRole.Healer)
            {
                if (_rescueCooldownTimer > 0f)
                {
                    _nameLabelText.text = $"HEALER (구출 쿨타임: {_rescueCooldownTimer:F0}s)";
                    _nameLabelText.color = new Color(0.65f, 0.9f, 0.7f, 1f);
                }
                else
                {
                    _nameLabelText.text = "HEALER (구출[Rescue]: 준비됨)";
                    _nameLabelText.color = new Color(0.25f, 0.95f, 0.45f, 1f);
                }
            }
            else if (role == PartyBotRole.RangedDPS)
            {
                _nameLabelText.text = "RANGED (원딜)";
                _nameLabelText.color = new Color(1f, 0.58f, 0.15f, 1f);
            }
        }

        private static void SetUnlitColor(Renderer r, Color color)
        {
            if (r == null)
            {
                return;
            }
            Shader s = Shader.Find("Sprites/Default");
            if (s == null)
            {
                s = Shader.Find("Universal Render Pipeline/Unlit");
            }
            Material mat = new Material(s) { color = color };
            r.sharedMaterial = mat;
        }
    }

    public class RangedBotProjectile : MonoBehaviour
    {
        private BossController _targetBoss;
        private float _speed;
        private int _damage;
        private float _lifetime = 3.0f;

        public static RangedBotProjectile Spawn(Vector3 startPos, BossController boss, float speed, int damage)
        {
            GameObject projObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            projObj.name = "RangedDPS_Projectile";
            projObj.transform.position = startPos;
            projObj.transform.localScale = Vector3.one * 0.38f;

            Collider col = projObj.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col);
            }

            Renderer r = projObj.GetComponent<Renderer>();
            if (r != null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader == null)
                {
                    shader = Shader.Find("Universal Render Pipeline/Unlit");
                }
                r.sharedMaterial = new Material(shader)
                {
                    color = new Color(1f, 0.55f, 0.12f, 1f)
                };
            }

            TrailRenderer trail = projObj.AddComponent<TrailRenderer>();
            trail.time = 0.18f;
            trail.startWidth = 0.25f;
            trail.endWidth = 0.02f;
            trail.startColor = new Color(1f, 0.65f, 0.15f, 0.9f);
            trail.endColor = new Color(1f, 0.3f, 0.05f, 0f);
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            if (r != null)
            {
                trail.sharedMaterial = r.sharedMaterial;
            }

            RangedBotProjectile proj = projObj.AddComponent<RangedBotProjectile>();
            proj._targetBoss = boss;
            proj._speed = speed;
            proj._damage = damage;
            return proj;
        }

        private void Update()
        {
            _lifetime -= Time.deltaTime;
            if (_lifetime <= 0f || _targetBoss == null || _targetBoss.IsBossDefeatedCollapse)
            {
                Destroy(gameObject);
                return;
            }

            Vector3 targetPos = new Vector3(
                _targetBoss.transform.position.x,
                1.35f,
                _targetBoss.transform.position.z
            );

            Vector3 toTarget = targetPos - transform.position;
            Vector2 toTargetXZ = new Vector2(toTarget.x, toTarget.z);

            if (toTargetXZ.magnitude <= 0.85f)
            {
                _targetBoss.ReceiveBotAttackDamage(_damage, true);
                Destroy(gameObject);
                return;
            }

            transform.position = Vector3.MoveTowards(transform.position, targetPos, _speed * Time.deltaTime);
        }
    }
}
