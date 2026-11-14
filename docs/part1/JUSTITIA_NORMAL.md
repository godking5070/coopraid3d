# Stage 5 노멀 — 성채 재판소 : 죄업을 재는 천칭, 유스티티아

## 확정 기획과 초기 구현

- **확정:** 36m × 24m 재판소, 월드 X축 기준 좌/우 천칭, 걸어서 왕래, 선택형 법전, 여섯 일반 공격, 질량 균형, HP 35% 판결문 낭독과 이리스 반박, HP 10% 복선, 처치 후 XII시 관·여덟 번째 동료 이야기 연결. 4인 파티이며 Stage 6으로 진행한다. 하드·극한·변신은 이번 범위에서 구현하지 않았다.
- **초기 구현:** `BossController.justitia`의 HP·피해·반경·시전 시간·무력화 요구량과 법전 성향의 개발 선택 필드. 에디터에서 조정 가능하다. 기존 Stage 1~4 설정은 바꾸지 않는다.
- AI 4인 관전에서는 기존 네 자동 전투 캐릭터가 질량을 받는다. 관전자용 다섯 번째 전투 몸은 없고 `PartyLife.OrderedMembers()`에 들어가지 않는다. AI 공격 피해 배율은 적 HP에만 적용되며 질량·무력화·기믹 시간에 곱하지 않는다.

## 전투 상태와 수치

| 항목 | 초기값 | 위치 |
|---|---:|---|
| HP | 52,000 | `JustitiaNormalSettings.health` |
| 전장 X/Z 반폭 | 18m / 12m | `halfWidth`, `halfDepth` |
| 중앙 미배정 띠 | X ±0.65m | `centerDeadZone` |
| 일반 패턴 간격/시전/회복 | 3.2초 / 3.6초 / 3초 | `patternInterval`, `castSeconds`, `recoverySeconds` |
| 탱커/반면/낙인/원형/돌진 피해 | 520 / 330 / 300 / 330 / 430 | `tankDamage` 등 |
| 분담 피해 총량 | 1,100 | `stackDamage` |
| 낙인 위치 고정 후 회피 | 1.6초 | `markLockSeconds` |
| 일반 카운터 창 | 2.2초 | `counterWindow` |
| 천칭 제한/실패 피해/이후 회복 | 11초 / 620 / 4초 | `balanceSeconds`, `balanceFailureDamage`, `balanceRecoverySeconds` |
| 판결 시작 | HP 35%, 도전당 1회 | `verdictThreshold` |
| 판결 무력화 제한/요구량 | 10초 / 460 | `verdictSeconds`, `verdictStagger` |
| 판결 성공 그로기/실패 피해/회복 | 8초 / 900 / 5초 | `verdictGroggySeconds`, `verdictFailureDamage`, `verdictRecoverySeconds` |
| 이야기 대사 | HP 10%, 도전당 1회 | `storyThreshold` |

일반 공격은 집행자의 일격→편향된 참격→판결의 낙인→천칭의 진동→연대 책임→집행 돌진→수평 심판 순서로 소개 후 반복한다. HP 35% 판결문은 진행 중인 패턴이 정리된 뒤 우선한다. 보스가 먼저 처치되면 대기 중인 기믹을 취소한다. 숨겨진 HP 잠금이나 회복은 없다. 일반 카운터는 금색 ⚔이며 전 직업의 실제 카운터 기술 적중을 기존 `CombatCounterTarget`이 검증한다. 보라색 무력화는 기존 스킬의 수치를 누적한다.

천칭 질량은 머리 위 숫자와 기존 파티 이름 행의 `⚖숫자`로 함께 표시한다. 후보는 4인 `[3,1,1,1]`, `[2,2,1,1]`, `[3,2,2,1]`; 3인 `[2,1,1]`, `[3,2,1]`; 2인 `[1,1]`, `[2,2]`다. `JustitiaNormalRules.HasEqualPartition`으로 해가 있는 조합만 유지한다. 시작 생존자가 1명이면 보류한다. 판정은 **마지막 시각**의 모든 생존 참가자 위치로 한 번 한다. 중앙 띠·0 대 0은 실패다. 사망으로 참가자가 바뀌면 한 번 새 질량과 전체 예고 시간을 주며, 다시 바뀌면 취소하고 전투로 복귀한다. 부활·AI 대체로 진행 중 질량을 몰래 재배정하지 않는다. AI는 공개 질량과 사용자 현재 좌우를 바탕으로 최소 이동 분할을 선택하고 한 번만 재계획한다.

## 리소스와 이야기 연결

- 보스 모델·Animator·준비 트리거·카운터/천칭/판결 효과음: 보스의 `BossController.justitia`. 모델 누락 시 콜라이더 없는 임시 황금 천사, 대검·등 천칭과 준비 막대를 표시한다. 보스 타기팅·피해 반경은 시각 모델 배율과 별개다.
- 등장·처치 Timeline/카메라: 기존 `RaidLobbyLayout.bossArrivals`/`bossDefeats`에서 Stage 5 엔트리를 연결한다. 누락·건너뛰기 시 기존 복구 경로를 사용한다.
- 재판소 배경과 진입로 법전: `RaidLobbyLayout.stage5CourthousePrefab`, `stage5LawBookPrefab`. 비어 있으면 코드 임시 바닥·판·배심석·법전을 표시한다. 장식은 타기팅과 충돌에서 제외된다.
- 법전 독백: `RaidApproachField.justitiaDevelopmentVoice` 개발 선택 및 `JustitiaLawText`. 정식 성향 데이터가 생기면 이 연결점을 대체한다. 조사는 선택이다.
- XII시 관과 동료 시각물: 실제 성소 장면에 `Stage5StorySceneBindings`를 연결하고 세 참조를 사용자 에디터에서 지정한다. `RaidLobbyLayout.stage5AwakenedCompanion`은 미정 동료 데이터 연결 위치다. 현재 이름·직업·스킬 데이터와 실제 성소 바인딩이 없어 새 동료 실체를 만들거나 슬롯을 교체하지 않았다. 기존 적격 Stage 5 클리어 영수증과 정산·저장 경로를 읽는다.
- 이리스의 반박 문구는 `BossJustitiaNormal.TickJustitiaDialogue`의 교체 가능한 초기 문구다. 원문 판결은 두 구간으로 나누어 표시하고 성공 판정은 대사 종료를 기다리지 않는다. 이리스에게 피해 상태를 주지 않는다.

## 검사와 사용자 확인

- `Tests/Run-JustitiaNormalChecks.ps1`: 질량 후보·불가 조합·중앙·0 대 0·3 대 1+1+1·AI 4인 분할 등 33개 통과.
- `Tests/Run-CombatMetricsChecks.ps1 -OriginalProject C:\\coopraid\\coopraid3d`: 기존 전투 지표 1,042개, AI 배율 20개, 관리 런타임·에디터 C# 컴파일 통과. **Unity 에디터 컴파일과 실제 플레이 결과가 아니다.**
- `Tests/Run-CampaignSaveChecks.ps1`: 임시 테스트 경로의 캠페인 저장·정산 규칙 31,913개 통과. Stage 5 실제 처치 복구와 성소 바인딩 실기는 별도 확인이 필요하다.
- 실제 1인+AI 3인: 개발 테스트 저장에서 Stage 5 노멀로 진입 → 법전 선택 조사와 컷씬 → 여섯 공격·금색 카운터 → 질량 `[3,1,1,1]`의 3 대 1+1+1, 중앙 실패, AI의 사용자 편 선택 → 35% 성공/실패 무력화·이리스 반박 → 10% 대사 → 처치·정산·포탈·대기실·Stage 6 문 → 재도전 초기화를 확인한다.
- AI 4인 관전: 테스트 프로필에서 Stage 5 노멀, AI 공격 피해 100%로 시작 → 네 AI만 질량을 받고 스스로 분할·무력화하는지 → 관전자 제외·결과 기록·배율 변경 시 질량/무력화 불변을 확인한다. 높은 배율로 임계치와 처치를 연속 통과하는 경우 예약보다 처치가 우선인지 확인한다.
- 실제 AI 성공률, 카운터 방향·사거리, 화면 가독성, 네트워크 동기화, 저장 복구, 4인 Steam은 **미검증**이다. 원본 Unity 프로젝트의 사용자 플레이 결과가 필요하다.
