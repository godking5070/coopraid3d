# 개발 테스트: AI 4인 관전

## 범위와 저장 경계

- 개발 빌드/에디터 메뉴에서만 시작한다. 탱커·힐러·근접 딜러·원거리 딜러 네 명이 기존 호스트 전투 판정과 `RaidReplacementAI`의 실제 이동·스킬·자원·쿨타임을 사용한다. 원래 로컬 플레이어 몸은 AI 리더로 전환하며 관전자용 다섯 번째 몸은 만들지 않는다.
- 시작 전 별도 테스트 프로필을 확인한다. 에디터는 관전 시작 때 고유 `GuardianNormal.TestProfile`을 만들고, 개발 빌드는 `--raid-test-profile=<이름>`으로 실행해야 한다. 일반 저장으로 자동 전환하지 않는다. 완료되지 않은 저장이나 정산이 있는 테스트 프로필에서는 새 관전을 거부한다.
- Stage 1부터 연속 진행하면 테스트 저장의 `spectator-test` 전송 유형으로 체크포인트·정산·카드·이야기 진행을 기록한다. 후반 스테이지 강제 진입은 기존 개발 기능처럼 런을 비적격 처리하며 저장을 검증하는 정상 완주가 아니다. 해당 진입의 이전 스테이지 R/F/V 및 카드는 개발 프리셋으로 첫 후보를 선택해 로그에 남긴다.
- 현재 구현된 고유 보스는 Stage 1~5 노멀·하드와 Stage 6 야누스 노멀·하드다. Stage 6 뒤에는 기존 1부 완료 흐름에서 멈춘다. 야누스 하드의 AI 완주는 실제 게임에서 미검증이다.
- 중단된 `spectator-test` 저장을 일반 싱글 이어하기로 열지 않는다. 관전 전용 이어하기는 현재 연결되지 않았으므로 중간 종료 복구는 미검증이다. 이미 끝난 게임을 자동 재실행하지 않는다.

## 시작과 조작

1. 원본 프로젝트의 개발 테스트 환경에서 메인 메뉴의 **AI 4인 관전 테스트**를 연다. 스테이지·난이도·네 역할의 직업을 선택한다. `연속 진행`을 끄면 현재 스테이지 처치와 보상 뒤 멈춘다.
2. `전멸 시 자동 재도전`은 기본 꺼짐이다. 켤 때 최대 횟수(기본 2, 0~20)를 정한다. 부활은 기존 도전당 공용 4회가 그대로 적용된다.
3. 시작 후 개발 상태창에서 AI 직업·HP·생존, 보스 HP·패턴, 도전 단계, 공유 부활, 저장 경계를 확인한다. 관전 UI의 이전/다음 AI·보스·전장·자유 버튼이나 설정 가능한 `SpectatePrevious`, `SpectateNext`, `SpectateBoss`, `SpectateArena`, `SpectateFree` 입력을 사용한다. 기본 키는 PageUp/PageDown/Home/End/Insert이고 자유 카메라는 기존 이동 입력과 확대·축소를 사용한다. 관전 입력은 AI 전투 요청을 보내지 않는다.
4. 결과는 기존 전투 지표 및 오류 보고 ZIP의 파티 구성·관전 모드 메타데이터로 확인한다. 정체가 90초 계속되면 관전 상태창과 로그에 경고를 남긴다. 보스 HP를 조정하지 않는다.
5. **관전 테스트 종료** 버튼으로 메인 메뉴로 돌아간다. 일반 플레이 카메라 설정은 관전 중 저장하지 않는다.

## 실시간 AI 공격 피해

- 관전 상태창의 `AI 공격 피해 ×1.20 / 120%` 표시 아래에서 `-10%p`, `+10%p`, 0~500% 슬라이더, 정수 입력, `100%로 초기화`를 사용한다. 100→110→120%처럼 버튼은 10퍼센트포인트씩 움직인다. 0%는 AI의 공격 동작과 판정·무력화 입력을 유지하면서 적 HP 피해만 0으로 만든다. 기믹 오브젝트의 파괴 속도도 같은 배율을 따른다.
- 네 AI에게 공통 적용하며 변경 즉시 이후의 적 HP 피해에 반영한다. 현재 세션의 재도전·다음 스테이지까지 유지되고 새 관전 세션에서는 100%로 돌아간다. 100%가 아니면 상태창에 `기본 밸런스 결과 아님`을 계속 표시한다. 일반 저장·스킬 데이터·영구 능력치에는 기록하지 않는다.
- 적용 지점은 `BossController.ApplyBossHpDamage`와 `BossAddMinion.ApplyAddHpDamage`다. 원래 AI 시전자의 `AiSpectatorActor` 표식을 확인하므로 적 공격·피해 수용·회복·보호막에는 적용되지 않는다. 투사체와 주기 피해는 생성 값이 아닌 적중/틱 시점의 현재 배율을 한 번 사용한다. 이미 확정된 피해에서 파생한 즉시 추가 피해는 `alreadyScaled`로 중복 곱을 막고, 저장해 나중에 재현하는 공격은 원래 피해의 기준값을 기록해 재현 시 현재 배율을 사용한다.
- 도전 시작 배율, 변경 시각과 전후 값, 도전 중 변경 여부, 도전 종료와 세션 종료 배율은 `[AI Spectator]` 로그에 남는다. 오류 보고 ZIP의 요약에도 현재 값과 이력이 들어간다.

## 사용자 실기 확인

1. Stage 1 노멀을 1탱·1힐·2딜로 시작한다. 전투 파티가 정확히 4명이고 관전자에게 HP·표식·어그로·부활 슬롯이 없는지 확인한다.
2. 파티원 보기와 보스·전장·자유 보기, 확대·축소, 파티원 사망 후 대체 보기, 등장·변신·처치 컷씬 뒤 보기를 확인한다.
3. 각 스테이지의 주요 기믹을 AI가 정상 이동·스킬로 처리하는지 확인한다: Stage 1 카운터/무력화/힘겨루기, Stage 2 덩굴/씨앗, Stage 3 번호판/잔향/종치, Stage 4 실 구출/재연/클라이맥스.
4. 전멸 시 기본 멈춤, 켠 경우 유한 재도전, 공용 부활 4회, 보상 선택과 포탈·대기실 출발을 확인한다. 강제 진입과 Stage 1부터의 저장 완주 결과를 구분한다.
5. 관전 오류 보고 ZIP에 `AI spectator: yes`, 네 AI 직업/루트/선택·HP와 스테이지 상태가 실렸는지 확인한다. 일반 저장·업적·클라우드 경계는 실제 빌드에서 확인한다.
6. 같은 AI 공격을 0%·100%·120%·500%로 관찰한다. 0%에서 공격 동작·카운터·무력화는 계속되고 보스/실/핵의 HP만 줄지 않는지, 투사체 비행 또는 지속 피해 도중 배율을 바꾸면 다음 적중/틱부터 반영되는지 확인한다.

## 코드 연결

- 구성·자동 진행: `Assets/Scripts/RaidAiSpectatorSession.cs`, `AiSpectatorDevelopment.cs`, `RaidReplacementAI.cs`, `RaidConnectionUI.cs`.
- 이동·입장·보상: `RaidLobbySession.cs`, `RaidApproachField.cs`, `RaidCombatSession.cs`, `StageManager.cs`.
- 관전 카메라·입력: `QuarterViewCamera.cs`, `LocalSettingsData.cs`, `LocalGameplayInput.cs`.
- 테스트 저장·오류 보고: `CampaignRuntime.cs`, `CampaignSaveData.cs`, `DevBugReportUI.cs`.
- 피해 배율: `RaidAiSpectatorSession.cs`, `AiSpectatorDamageRules.cs`, `BossController.cs`, `BossAddSpawner.cs`, `CombatHitContext.cs`, `CharacterKitCombat.cs`, `CharacterKitSkills.cs`, `AegisPaladin.cs`.
- UI는 `AiSpectatorDevelopment.menuRect/statusRect`로 위치·크기를 편집한다. 사용자 제작 UI·Timeline·씬·프리팹은 수정하지 않았다.

## 검사 상태

- 명령 기반: `Run-CampaignSaveChecks.ps1` 31,913 assertions 통과(네 AI 전용 체크포인트, 카드 선택, 현대 직업과 일반 싱글 경계 포함). `Run-CombatMetricsChecks.ps1` 1,042 전투 지표 assertions와 20개 AI 피해 배율 경계 assertions 및 관리 런타임·에디터 C# 컴파일 통과. `git diff --check` 통과.
- Unity 임포트·실제 AI 전투·컷씬/관전 카메라·연속 진행·4인 Steam은 실행하지 않아 미검증이다. Unity·Steam·게임 창을 실행하거나 화면/입력을 자동 조작하지 않았다.
