# 1부 플레이 빌드 작업

기준: `fd06247a12e0c414afa80f049d32fc5872a41c0c` · 브랜치: `feature/part1-playable-build`

| 순서 | 작업 / 완료 기준 | 상태 |
|---|---|---|
| 1 | 메뉴부터 Stage 6 결과까지 실제 실행 경로 확인, 누락/실행 미확인/콘텐츠 부족 분리 | 코드 확인 완료, Unity 실행 미확인 |
| 2 | 전환 정리, 보상 장벽, 저장 및 카운트다운, 최종 완료 단일 처리 | 기존 구현 확인/회귀 검사 통과, 실제 완주 미확인 |
| 3 | 사망 안내·부활 결과·관전·입력 복구·전멸 재도전 | 구현/컴파일 완료, 실제 플레이 미확인 |
| 4 | 기존 HUD/실패 안내/실제 바인딩/입력 충돌 확인 및 보완 | 코드 확인/안내 수정 완료, 훈련장 레이아웃 수정·화면 확인, 정규 전투 실기 필요 |
| 5 | 저장·재접속·AI·늦은 콜백 경계 확인 및 수정 | 기존 경계/저장/전송 검사 통과, 실제 Steam 미확인 |
| 6 | 반복 실행의 상태 누수와 근거 있는 할당/메시지 문제 점검 | UI 할당·종료 시 파괴 객체 참조 오류 수정, 종료 로그 재확인 완료, 장시간 실행 미확인 |
| 7 | Windows 빌드 구성·실행 절차·가능한 빌드/플레이 확인 | 원본 Unity Windows 빌드 성공, 메뉴/훈련장 실기 확인, 완주·네트워크 미확인 |

기존 저장 및 부활 기능을 재구현하지 않는다. 각 변경에는 관련 검사만 수행하고, 실제 Unity/Steam 실행과 자동 검사를 구분한다.

## 알려진 외부 조건

- Stage 4~6은 기존 보스 1~3 재사용이다. 별도 고유 콘텐츠 자료 확인이 필요하며 6종 보스 완성으로 보고하지 않는다.
- 원본 Unity 폴더는 이전 브랜치를 유지하며, 사용자 승인에 따라 최신 소스를 미커밋 변경으로 반영했다. 원본 사용자 수정은 최신 브랜치 선행 커밋에 포함됨을 확인했고 기존 meta/씬/설정은 보존했다. worktree Unity 실행과 Library 생성은 금지한다.
- 실제 4개 Steam 계정 검증은 별도 환경이 필요하다.

## 실행 경로 조사

| 경로 | 근거 | 분류 |
|---|---|---|
| 메뉴 → 싱글/Steam → 직업/기원 → 준비 | RaidConnectionUI, RoleSelectionManager, AegisRouteSelectionUI, RaidLobbySession | 구현됨, 실행 확인 필요 |
| Stage 1~5 → 카드 + R/F/V → 다음 전투 | StageManager.UpdateSingleRewards / RaidCombatSession.FinishRewardsIfReady | 구현됨, 기존 순수 상태 검증 있음 |
| 다음 스테이지 정리/저장/로딩/카운트다운 | PrepareEncounter, CleanupAttempt, CampaignRuntime.Prepared, RaidLobbySession | 구현됨, 실제 개체/물리 확인 필요 |
| Stage 6 → 최종 기록/결과 → 메뉴 | CampaignRuntime.Cleared/Completed, StageManager, RaidNetworkSession | 구현됨, 즉시 종료 경계 실기 확인 필요 |
| 사망 시 관전 전환 | QuarterViewCamera의 별도 관전 대상 + ResurrectionSlotUI | c6fbc5b에서 구현/컴파일 완료, 화면 확인 필요 |
| 사망 중 보스 선택 상태에서 자기 부활 | BattleResurrection.TryBegin이 사망한 로컬 사용자를 우선 | c6fbc5b에서 수정 완료, 실제 부활 확인 필요 |
| Windows 진입 씬 | 전용 Part1_Playable 자동 구성 + BuildPartOne 참조 검증 | 원본 Unity 빌드 성공, exe 메뉴 진입 확인 |
| Stage 4~6 고유 콘텐츠 | 데이터/문서 추가 없음, 템플릿 1/2/3 반복 | 콘텐츠 자료 필요 |

## 종료 시 판정

**코드 준비 완료, Unity 실행 확인 필요.** Windows 테스트 빌드 생성과 메뉴/훈련장 화면 확인은 완료했다. 이 판정에서 남은 실행 확인은 정규 Stage 1~6 완주·저장/이어하기·사망/재도전·네트워크이다.

우선순위:
1. 원본 소스 반영 및 Windows 메뉴 빌드 완료: 기존 77개 불일치 해소, 브랜치/스테이징 유지. 원본 Builds/Part1Windows/RaidPart1.exe로 다음 실기를 진행한다. worktree Unity 실행은 하지 않았다.
2. 실기 미확인: UI 관전·부활/물리·Stage 1~6 완주·종료/이어하기·장시간 반복·LAN/Steam 연결. PLAYTEST.md 순서로 기록.
3. Steam 환경: App ID 0, 실제 다중 계정 환경 필요. 배포/호스트 이전 범위 제외.
4. 콘텐츠 부족: Stage 4~6 고유 보스 명세/데이터 필요. 현재 경로는 기존 3종을 반복하는 6스테이지 진행 테스트다.

이번 소스 검증에서 남은 컴파일 오류나 실패한 자동 검사는 없다. 화면·물리·네이티브 네트워크 결함 유무는 아직 판정할 수 없다.

재개 후 7단계 보완: 소스 사전 검사는 CRLF/LF·UTF-8 BOM 차이를 허용하고 실제 텍스트 차이, 추가 스크립트·어셈블리 정의·컴파일 옵션을 구분한다. 실제 임시 파일 검사 13개 통과. 2026-10-24 원본 Windows 빌드 성공. 화면 검사에서 발견한 훈련장 행 확장/설정 버튼 겹침을 수정했다.
