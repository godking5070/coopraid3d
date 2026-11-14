# Stage 3 노멀 — 침묵의 종루 : 울리지 않는 대종, 캄파넬라

## 확정 기획과 초기 구현안

사용자 확정 내용은 열린 고딕 종루, 사슬과 푸른 원정대 망토로 종소리를 억제한 청동 종·거미다리 보스, I~IV 공명판의 순서별 담당, HP 40% 실제 무력화, 처치 후 X시 관·여섯 번째 동료·V A/B 선택이다. 아래 여섯 일반 공격과 HP·시간·피해·반경은 **노멀의 초기 구현값**이다. 열두 번째 종소리를 횟수 게이지나 도전 제한으로 만들지 않았다. 노멀에는 형태 변신을 적용하지 않으며 하드 구현은 `CAMPANELLA_HARD.md`에 별도로 기록한다. 극한은 미구현이다.

기존 Stage 3 범용 보스 대신 `BossController.CampanellaNormal` 상태 기계를 사용한다. 일반 공격 여섯 개는 사슬 압착·청동다리 휩쓸기·억눌린 진동·낙하하는 봉인추·공명 분담·사슬 폭주이고, 일곱 번째 반복 후보로 `침묵의 조율`을 사용한다. 다음 휴식 경계에서 HP 40% 이하라면 `강제 타종`을 한 번 우선한다. 각 핵심 기믹은 일반 공격과 겹치지 않는다. HP 잠금과 회복은 없다. 보스 처치가 먼저 확정되면 대기 상태를 정리한다.

## 초기 수치와 발판

`BossController.campanella` (`CampanellaNormalSettings`)에서 모두 조절한다. Stage 1·2의 체력과 속도 배율은 상속하지 않는다.

| 항목 | 초기값 |
| --- | ---: |
| 보스 HP / 전장 반경 | 42,000 / 18m |
| 일반 시전 / 패턴 간격 / 회복 | 3.5초 / 3.2초 / 2.5초 |
| 사슬 압착 / 반면 / 중심 진동 / 봉인추 | 460 / 300 / 290 / 280 피해 |
| 공명 분담 총 피해 / 돌진 | 1,000 / 390 피해 |
| 진동 / 봉인추 / 분담 반경 | 6 / 2.7 / 3.5m |
| 돌진 폭·길이 | 2.5×18m |
| 조율 준비 / 번호당 창 / 단계 간격 | 6 / 2.5 / 0.7초 |
| 공명판 반경 / 단계 실패 | 2.4m / 생존자당 260 피해 |
| 강제 타종 조건 / 제한 / 요구 무력화 | HP 40% / 10초 / 350 |
| 타종 실패 / 성공 그로기 / 이후 회복 | 850 광역 피해 / 8초 / 5초 이상 |
| 이야기 대사 | HP 10% 이하, 도전당 한 번 |

기본 발판 중심은 I(-9,9), II(9,9), III(9,-9), IV(-9,-9)다. 반경 18m 안에 발판 전체가 들어간다. `platePoints`와 `plateRadius`는 별도 설정이다. 최저 기본 이동 속도 7.8×0.9=7.02m/초 기준 가장 먼 판 사이 약 25.5m는 약 3.63초 이동이며 6초 배치 시간 안에 들어간다. AI는 각자 자기 판으로만 이동한다. 추락·바닥 붕괴는 없다.

## 판정과 표시

- 일반 피해는 시전바·준비 동작 뒤 붉은 범위를 충분히 보여준다. 사슬 폭주의 금색 ⚔ 카운터는 기존 `CombatCounterTarget`의 기술 적중·정면·거리·시간 조건을 사용하며 전 직업이 성공할 수 있다. 실패하면 고정된 붉은 직선을 회피할 수 있다. 파란 탱커 힘겨루기는 없다.
- 조율 시작 시 호스트가 살아있는 파티원을 섞어 번호를 중복 없이 배정한다. 번호는 역할에 고정되지 않는다. 머리 위 번호와 로컬 개인 안내에 내 번호를 표시하고, 판에는 I~IV 문자와 현재·성공·실패·미배정 상태를 표시한다. 전투 중 연결 대체 AI는 같은 캐릭터 Transform을 이어받는다.
- 6초 배치 후 I→II→III→IV를 진행한다. 활성 창의 **매 틱 위치**를 검사하므로 미리 서 있어도 성공한다. 잘못된 사람이 서 있으면 오답을 알리고 성공으로 인정하지 않는다. 한 단계는 한 번만 확정하고 다음 단계 전에 0.7초 간격을 둔다. 미배정은 성공이나 피해로 처리하지 않고 건너뛴다. 담당자 사망은 해당 창 만료 시 실패와 회복 가능한 피해로 처리한다. 부활자는 진행 중 번호를 다시 받지 않는다.
- HP 40% 강제 타종은 기존 공격·조율을 끝낸 다음 시작한다. 보라색 게이지는 실제 누적 무력화 350/남은 10초를 표시한다. 일반 피해 감소가 아닌 기존 기술의 무력화 수치를 사용한다. 성공 시 종을 재봉인하고 8초 그로기, 실패 시 **불완전한 타종** 충격 850 피해 후 전투와 회복으로 돌아간다.
- HP 10% 대사는 `RaidLobbyUI.Say`로 순서대로 표시하고 실제 공격 시전바를 가리지 않는다. 빠른 처치에는 정산을 우선한다.

## 리소스·이야기 연결

- `RaidLobbyLayout.stage3BellTowerPrefab`은 종루 배경, `stage3ExpeditionCloakPrefab`은 진입로 조사 물체다. 없으면 코드 임시 석재 전장·황금 장식 고리·번호판·망토를 쓴다. 장식 고리와 붉은 공격 범위는 별개다. 성향 성장 데이터는 아직 없으므로 `RaidApproachField.campanellaDevelopmentVoice`가 개발용 독백을 고른다. 조사는 필수가 아니며 저장하지 않는다.
- 보스 인스펙터의 `campanella.bossVisualPrefab`, `bossAnimatorController`, 각 `*Trigger`, 효과음을 연결한다. 없으면 충돌 없는 청동 종·거미다리·사슬·망토와 임시 준비 동작을 사용한다. 반복 공격에 완전한 대종 소리를 자동 재생하지 않는다.
- 등장·처치 Timeline과 카메라·트랙은 기존 `RaidLobbyLayout.bossArrivals`/`bossDefeats`의 **stage=3** 항목 및 `RaidStageCutscenePresentation`에서 연결한다. 리소스가 없거나 건너뛰면 기존 `StageManager`가 카메라·입력을 복구한다. 기본 처치 문구는 종치·구동부 정지로 표현한다.
- 성소 담당 장면에 `Stage3StorySceneBindings`를 붙여 X시 닫힌/열린 관과 각성 시각물을 연결한다. `CampaignRuntime.Stage3BellSilenced`는 기존 확정 Stage 3 영수증을 읽으며 별도 중복 보상 플래그를 만들지 않는다. `RaidLobbyLayout.stage3AwakenedCompanion`은 여섯 번째 동료 데이터 연결 자리다. 현재 실제 데이터와 성소 바인딩이 없으므로 **동료 해금과 관 시각 변화는 에디터 연결 후 실기 확인이 필요**하다. 이름·직업·스킬·파티 슬롯은 만들지 않았다.
- 직업별 Stage 3 V 선택은 이미 있는 `CharacterRosterData`의 `v1/v2`, `CharacterRewardUI`, 런별 선택·정산 저장을 그대로 쓴다. 선택 전 V는 미해금이고 표시 키는 현재 입력 설정을 따른다. 이전 R·F와 4인 정원, 공용 부활 최대 4회는 기존 정책을 유지한다.

## 수정 파일과 검사

새 파일: `Assets/Scripts/CampanellaNormalData.cs`, `BossCampanellaNormal.cs`, `CampanellaCloakText.cs`, `Stage3StorySceneBindings.cs` 및 각각의 `.meta`; `Tests/CampanellaNormalTests.cs`, `Tests/Run-CampanellaNormalChecks.ps1`.

수정 파일: `BossController.cs`, `CombatCounterTarget.cs`, `PartyAiTactics.cs`, `PartyBotAI.cs`, `PartyBotGuardian.cs`, `RaidReplacementAI.cs`, `BossAddSpawner.cs`, `RaidCombatProtocol.cs`, `RaidCombatWorld.cs`, `RaidApproachField.cs`, `RaidLobbyLayout.cs`, `StageManager.cs`, `CampaignRuntime.cs`, 이 문서와 `HANDOFF.md`.

명령 기반: Campanella 규칙·배치 27개, Flora 노멀 17개, Guardian 노멀 20,052개, CharacterRoster 1,136개, CampaignSave 31,881개(임시 경로), CombatMetrics 1,042개와 런타임/에디터 관리 코드 컴파일 통과. 전체 네트워크 스크립트는 기초 3,737개 통과 후 기존 `RaidCombatNetworkTests` 단계에서 중단되므로 이번 Stage 3 동기화의 실행 검증으로 간주하지 않는다. Unity Play·에디터 임포트·실제 AI·컷씬·4인 Steam·저장 복구는 미검증이다.

사용자는 개발 테스트 저장에서 **플레이어 1명+AI 3명**으로 (1) 여섯 일반 공격과 금색 카운터, (2) 조율 번호가 역할과 무관하게 중복 없이 배정되는지와 미리 서기·오답·실패·사망, (3) 40% 타종 성공·실패와 보라 게이지, (4) 10% 대사·처치 컷씬·V A/B 선택·X시 관, (5) 전멸·재도전과 재접속, (6) 개발 명령 없는 전체 완주를 확인한다. 탱커·힐러·딜러 역할을 바꾸어도 AI 세 명이 자기 판과 무력화에 참여하는지 확인한다. 일반 저장·업적·클라우드 기록은 이 작업에서 변경하지 않았다.
