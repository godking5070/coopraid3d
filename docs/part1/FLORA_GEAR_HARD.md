# Stage 2 하드 — 기억의 유리 온실 : 플로라 기어

## 확정 기획과 초기 구현안

사용자 확정 기획은 노멀을 보존한 빠른 하드 전투, 40% 변신, 변신 직후 오염핵 개화, 25% 덩굴→씨앗 순차 기믹이다. 아래 외형·신규 공격·수치는 이번 작업의 **초기 조정값**이다. 극한은 구현하지 않았다. Stage 1의 가고일·탱커 힘겨루기·HP 30,000은 적용하지 않는다.

`BossController.FloraEncounter`가 노멀과 하드의 기존 Stage 2 상태 기계로 진입한다. `flora`는 노멀 전용이고 `floraHard.combat`와 나머지 `floraHard` 필드는 하드 전용이다. 두 난이도는 서로의 설정을 변경하지 않는다. 하드 `healthOverride=0`은 현재 노멀 HP 36,000을 임시로 사용한다. `BossAddSpawner`의 기존 소환수·타깃·네트워크 경로를 뿌리핵에 재사용하고, Stage 2에서는 범용 70%/30% 소환을 실행하지 않는다.

## 공격과 진행

- 하드는 뿌리 환류(외곽→중심)와 교차 전정(I→II)을 먼저 단독 소개한 뒤, 기존 7개 일반 후보와 함께 9개 패턴을 순환한다. HP 잠금은 없다. 직전 두 신규 패턴의 즉시 반복은 순환표로 피한다.
- 정원사의 전정은 같은 탱커에게 2회, 가위질은 반대쪽 후속, 뿌리 솟구침은 중심→외곽, 박제의 꽃잎은 폭발 후 2초 잔류 가시를 준다. 추모의 개화는 기존 집합 한 번 후 회복한다. 가지치기 돌진은 모든 직업의 기존 카운터가 가능하다.
- 일반 공격의 시전바·문구·애니메이션 트리거가 먼저 시작된다. 빨간 피해 범위는 마지막 0.4초만 보인다. 교차 전정의 고정 I·II 선은 피해 범위와 별도의 지속 안내선이다. 대상 표식·금색 카운터·덩굴·씨앗·안전지대는 대응 동안 유지한다.
- HP 40% 이하를 휴식 경계에서 한 번 처리한다. 현재 기믹을 끝내고 표식·가시를 제거한 뒤 Timeline 또는 8초 임시 연출로 변신한다. 체력과 최대 체력은 그대로다. 연출 중 보스 피해·공격과 로컬 전투 입력은 막는다. `Space`(설정 가능)로 건너뛰면 카메라를 복구한다. 각 화면은 호스트의 변신 상태와 Timeline 진행 시간을 받아 재생한다. 리소스가 없으면 충돌 판정 없는 임시 가시꽃·태엽 덩굴 외형으로 바뀐다.
- 이어서 두 기생 뿌리핵을 12초 안에 파괴한다. 보스는 이때 공격할 수 없고, 핵은 기존 소환수 체력·피해·타기팅·복제 경로를 쓴다. 성공 시 6초 그로기, 실패 시 생존자에게 850 광역 피해 한 번 후 5초 회복이다. 두 경로 모두 핵과 보호 상태를 제거한다.
- 노멀의 첫 `영원한 박제 정원`은 하드 전용 `combat.gardenThreshold=50%` 이하에서, 덩굴을 한 번 소개한 뒤 실행한다. 40% 변신·뿌리핵과 동시에 시작하지 않는다. 첫 정원 후 HP 25% 이하라면 마지막 박제 정원을 한 번 실행한다. 덩굴 종료와 실패 피해 정리→2초 정렬→씨앗 2개 투척·활성화→안전지대 판정 순서다. HP 조건을 빠르게 지나면 변신→뿌리핵→첫 정원→마지막 정원 순으로 다음 안전한 경계에서 처리한다. 보스 처치가 먼저면 남은 기믹은 취소된다.
- 기존 10% 이야기 대사, +15% 정화 버프 15초 비중첩, 4회 공용 부활, 처치 컷씬·F 선택·꽃 변화·IX시 동면관·동료 해금·포탈·저장 흐름을 사용한다. 클리어 영수증에는 난이도가 기록되고, 첫 이야기 해금은 난이도와 관계없이 Stage 2 영수증 한 번으로 판단한다. 새 런의 F 선택은 런별 키를 따른다. 데이터가 없는 다섯 번째 동료 이름·직업은 만들지 않았다.

## 초기 조정값과 이동 검토

모든 값은 `BossController.floraHard`의 `FloraHardSettings`에서 조절한다. `combat` 안의 피해·반경·덩굴·씨앗 값은 노멀과 동일한 **별도 객체**의 초기값이다.

| 항목 | 초기값 |
| --- | ---: |
| HP / 템포 / 일반 시전 | 노멀 36,000 임시 기준 / 1.2 / 3.5÷1.2=2.92초 |
| 일반 패턴 대기 / 회복 | 3.2÷1.2=2.67초 / 2.5÷1.2=2.08초 |
| 2연타·집합 후 회복 | 2.5초, 필수 회복 시간 유지 |
| 2타 기본 간격 / 카운터 가능 / 빨간 강조 | 2.4÷1.2=2초 / 1.6초 / 0.4초 |
| 꽃잎 잔류 가시 | 2초, 0.5초 간격 120 피해 |
| 40% 변신 / 뿌리핵 | 8초 / 12초, 핵당 HP 1,800 |
| 핵 실패 / 성공 그로기 / 특수 회복 | 광역 850 / 6초 / 5초 |
| 첫 정원 / 마지막 정원 / 정렬 | 50% / 25% / 2초 |
| 덩굴 / 씨앗 / 안전지대 / 정화 | 10초·12m / 2초 도착·12초 시전 / 5.5m / +15% 15초 |

현재 최저 기본 이동은 7.8×0.9=7.02m/초다. 반면 연속 공격 AI 목표는 경계 양쪽 1.8m, 총 3.6m 이동이라 0.52초가 필요하고 2초 간격 안에 들어간다. 뿌리 중심↔도넛 목표는 반경 4m↔8m로 약 0.57초 이동이다. 교차 전정 두 선은 중앙에서 ±4m에 폭 2.4m로 놓여 중앙 전방 7m 지점이 두 타격 모두 안전하다. 덩굴 10초와 씨앗 2초 도착·12초 시전은 템포 배율을 적용하지 않는다. 실제 충돌·애니메이션 길이는 플레이에서 확인해야 한다.

## 에셋 연결과 사용자 조정 위치

- 원본 Unity 에디터의 Stage 2 보스 `BossController.floraHard.transformationPresentation`에 사용자 Timeline 프리팹을 연결한다. 이 프리팹의 `PlayableDirector`, 전용 카메라, 트랙 바인딩은 기존 `RaidStageCutscenePresentation`을 따른다. `cutsceneSkipKey`는 기본 Space다.
- `transformedVisualPrefab`과 `transformedAnimatorController`가 변신 외형을 교체한다. 미연결 시 코드 임시 꽃·덩굴이 사용된다. 프리팹 콜라이더는 비활성이고 보스의 기존 피해 범위·타기팅·충돌 크기는 별도로 유지한다.
- `attackPrepCue`, `attackImpactCue`, `followupCue`, `transformationCue`, `coreCue`를 연결한다. `pruningTrigger`, `scissorsTrigger`, `rootsTrigger`, `refluxTrigger`, `crossTrigger`, `followupTrigger` 이름은 사용자 Animator의 Trigger와 맞춘다. 없는 Trigger는 호출하지 않고 임시 가위·뿌리 준비 동작을 표시한다. 클립·Timeline·프리팹은 코드가 덮어쓰지 않는다.
- `combat.seedOne/seedTwo`, `firstCore/secondCore`, `combat.*Damage`, `combat.*Radius`, `combat.gardenThreshold`, 나머지 시간은 하드 인스펙터에서 따로 조정한다. 이리스의 발코니·투척 애니메이션은 기존 `RaidApproachField` 연결을 따른다. 노멀 모델은 하드 초기 외형의 대체 자산으로 사용할 수 있다.

## 검사와 사용자 플레이

수정 파일: `BossFloraNormal.cs`, `BossFloraHard.cs`, `FloraNormalData.cs`, `FloraHardData.cs`, `FloraHardRules.cs`, `BossAddSpawner.cs`, `BossController.cs`, `CombatCounterTarget.cs`, `LocalGameplayInput.cs`, `PartyAiTactics.cs`, `PartyBotAI.cs`, `PartyBotGuardian.cs`, `RaidReplacementAI.cs`, `RaidCombatProtocol.cs`, `RaidCombatWorld.cs`, `Tests/FloraHardRulesTests.cs`, `Tests/Run-FloraHardChecks.ps1`, 이 문서와 `HANDOFF.md`. 새 C# 파일 세 개의 `.meta`만 추가했다.

명령 기반: `Run-FloraHardChecks.ps1` 12개 우선순위·템포·이동 검사, `Run-FloraNormalChecks.ps1` 17개, `Run-GuardianHardChecks.ps1` 1,656개, `Run-CombatMetricsChecks.ps1` 1,042개와 런타임/에디터 관리 코드 컴파일을 실행한다. Unity 에디터 컴파일, Timeline/Animator·카메라·효과음, AI 실제 이동, 정상 플레이, 4인 Steam, 저장 복구는 미검증이다.

사용자는 **개발 테스트 저장에서 플레이어 1명+AI 3명**으로 (1) 강화 기본 공격·신규 2패턴을 각각 피하고 AI의 이동 확인, (2) 40% 변신과 Space 건너뛰기·카메라/입력 복구 및 HP 유지, (3) 핵 두 개 파괴 성공과 제한시간 실패, (4) 25% 덩굴 뒤 씨앗 2개 안전지대 및 +15% 비중첩, (5) 전멸·재도전 시 1페이즈 복귀, (6) 개발 명령 없는 전체 완주를 순서대로 확인한다. 탱커·힐러·딜러 역할을 바꿔서도 핵 타깃 분배, 2연타 회복, 씨앗 이동을 확인한다. 일반 저장·업적·클라우드 기록은 이 작업에서 변경하지 않았다.
