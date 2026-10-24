# 작업 기록

## 시작
- AGENTS.md 확인. 원본 `feature/aegis-paladin-routes`의 미커밋 변경을 보존.
- 저장/이어하기 완료 커밋 `fd06247`에서 `feature/part1-playable-build`와 `C:/coopraid/coopraid3d-playable` worktree 생성.
- 이전 보고서: `Tests/Part1SaveReview.txt`. 기존 검사 147,722 assertions 및 C# 컴파일 통과 기록 있음. 실제 Unity/Steam 플레이는 미확인.
- 1단계 시작: 실행 경로·사망/관전·HUD·빌드 도구와 단계 전환 코드 조사.

## 1·2단계 조사 완료 / 3단계 시작
- 메뉴→직업/기원→Stage 1~6→보상/선택→결과 경로와 로딩 장벽/도전 revision/종료 정리 확인. 기존 흐름 재사용.
- 발견: 관전 없음. 사망 시 보스를 선택한 채 이리스 키를 누르면 자신의 부활을 요청하지 못함. 싱글 부활 실패 피드백 없음.
- 발견: EditorBuildSettings는 SampleScene, 기존 빌드 메뉴는 임의 활성 씬을 빌드한다. 7단계에 명시적 전투 씬 검증 연결.
- 원본 Unity 6000.3.26f1 실행 중 확인. 원본 소스는 fd06247 이전으로 최신 빌드 확인 불가. 소스 반영·원본 실행은 외부 조건으로 기록.
- 다음 수정: 기존 ResurrectionSlotUI에 사망/관전 UI를 통합하고 카메라의 보기 대상만 분리. 이리스 공유 예산 엔진은 유지.

## 3·4단계 구현 / 첫 회귀 검사
- 기존 ResurrectionSlotUI에 사망 상태·남은 부활·자기 구조 안내·이전/다음 살아 있는 파티원 관전 통합. QuarterViewCamera는 별도 보기 대상만 사용하며 combat target/제어 대상을 바꾸지 않는다. 부활/전환 시 원래 카메라 복귀.
- 로컬 사망 시 선택된 적 대신 자기 구조 요청. 싱글과 호스트 양쪽에서 이리스 결과/불가 사유를 기존 단일 실패 안내 영역에 표시.
- 전멸 화면의 메뉴 복귀는 기존 저장 안내 확인창 사용. 다른 직업 준비 안내에 팔라딘 이름이 나오던 문구 수정.
- Run-SteamChecks 전체 검사와 실제 참조 C# 런타임/Editor 컴파일 통과. 관전 인덱스 6개 경계 검사 추가(카메라 총 1,511). 실제 UI/Unity 실행은 아님.

## 5단계 조사 / 6단계 수정 시작
- Steam 생성/참가 콜백의 generation + expectedSession + Ending 검사, 전환 epoch/로딩 ACK, 제어 lease, 보상 자동 확정 및 다음 경계 저장 확인. 기존 구현 유지.
- 숨겨진 로비가 매 프레임 참가자 문자열과 Steam 회원 정보를 다시 조회하는 근거 확인: 숨김 시 생략, 열린 로비는 0.2초 갱신으로 변경. 게임 규칙/네트워크 동기화 주기는 유지.
- 포인터 UI 검사에서 매 호출 PointerEventData 생성 제거. EventSystem 교체/세션 종료 때 캐시 초기화.
- 7단계 다음: 지정된 자동 구성 씬 검증, 원본 전용 Windows 빌드 명령 및 저장소 내부 Builds 출력, 실제 실행 체크리스트.

## 7단계 빌드 도구 / 실제 빌드 차단 확인
- `BuildPartOne`은 원본 전용으로 생성한 Part1_Playable 씬만 사용하며 플레이어/보스/카메라 각 1, AI 3, 보상/결과 참조 및 4인/6스테이지 설정을 검사한다. 임의 활성 씬/SampleScene을 빌드하지 않는다.
- SceneAutoBuilder의 기존 구성 함수를 재사용하되 전용 씬 생성에서는 기존 9개 테스트 씬을 덮어쓰지 않는다. 배치 임포트 때 임의 씬 자동 재생성을 중단한다.
- 출력은 원본 `Builds/Part1Windows/RaidPart1.exe`. 빌드 결과/콘텐츠 재사용/Steam 설정 여부를 BUILD_INFO.txt에 기록.
- `Build-Part1Windows.ps1 -CheckOnly` 실제 실행: 필수 소스 87개 차이 + 원본 Unity 실행 중으로 차단. Unity는 실행하지 않았고 exe도 생성하지 않음. 원본은 이전 브랜치와 사용자 변경을 유지.
- 설치 Unity 6000.3.26f1 및 Windows 모듈 존재 확인. 제품 Steam App ID는 0. 실제 다중 계정 테스트에는 별도 앱/계정 환경 필요.
- 배치 첫 씬이 저장되지 않은 빈 씬일 수 있음을 반영해 배치에서는 빈 경로를 허용하되 실제 dirty 씬은 계속 거절. 저장 씬 생성 실패는 빌드 실패로 처리.
- 다음: 변경 전체 최종 managed 런타임/Editor/Player 심볼 컴파일, diff 검토, 원본 보존 확인. 실제 플레이 절차는 PLAYTEST.md 작성 완료.

## 최종 검증과 인계
- Run-SteamChecks 출력 합산 **147,728 assertions 통과**(기존 147,722 + 관전 인덱스 6). 전투 흐름/도전/저장 파일 I/O/보상/재접속 상태/가짜 Steam 경계 검사이며 실제 Unity 또는 Steam 플레이가 아니다.
- 이후 UI 할당 및 빌드 도구 변경을 포함하여 런타임/Editor/Player 조건으로 최종 C# 컴파일 통과. Player 검사는 UNITY_EDITOR 계열 심볼과 UnityEditor 직접 참조를 제외하여 수행. Unity BuildPipeline 실행/네이티브 코드/최종 패키징 검증과 구분.
- 기존 `HealingSanctuaryZone._innerCross` 미사용 CS0169 경고 유지. 새 컴파일 오류 없음.
- 검사 출력: Git 제외 `Tests/PlayableChecks.log`. 컴파일 응답/어셈블리는 Temp/RaidRogue-Step10-98dc1b79292947a3898042c7b190e7a0. 워크트리 Library/Builds 없음.
- `git diff --check` 통과. 원본 기준 309개 파일 SHA256 차이 0. 원본 브랜치/사용자 변경 유지. 새 Assets 파일/meta/GUID/씬 YAML 직접 편집 없음.
- BUILD_INFO의 갱신 시각도 검사하여 이전 빌드 exe를 이번 빌드 성공으로 오인하지 않게 했다.
- 독립적으로 가능한 코드·검사·도구·문서 작업 완료. 실제 빌드는 소스 통합/원본 실행이라는 외부 조건 때문에 수행하지 않았다. 실제 플레이 0회. 단순 반복 테스트로 이를 대체하지 않는다.

## 변경 파일별 역할

- `BattleResurrection`, `IrisRescue`, `RaidCombatSession`: 자기 구조 대상과 구체적 호스트 결과, 기존 공유 예산 재사용.
- `QuarterViewCamera`, `ResurrectionSlotUI`, `CameraControlMath`: 사망 관전/상태 안내, 입력·전투 타겟과 분리, 복귀/정리.
- `PartyWipeUI`, `RaidLobbySession`: 전멸 메뉴 복귀 및 직업 공통 준비 안내.
- `LocalGameplayInput`, `RaidConnectionUI`: EventSystem별 포인터 데이터 재사용, 숨겨진 로비 갱신 생략.
- `Editor/SceneAutoBuilder`, `Editor/RaidMultiplayerBuild`: 기존 자동 구성 재사용, 전용 진입 씬/참조 검사/원본 전용 빌드/출력 고정.
- `Tests/CameraControlTests.cs`, `Tests/Build-Part1Windows.ps1`: 관전 경계 검사, 읽기 전용 빌드 사전 검사 및 배치 실행.
- `docs/part1/ROADMAP.md`, `WORK_LOG.md`, `HANDOFF.md`, `PLAYTEST.md`: 상태·근거·외부 조건·재개 순서.

## 재개: 기록과 실제 상태 대조 (2026-10-24)

- AGENTS.md와 3개 기록 문서 확인. 기존 worktree/브랜치의 HEAD는 `c6fbc5b78292e1817d856c50966cf41a9cbeb98c`, 재개 당시 미커밋 변경 없음. 이전 구현이 커밋되어 있어 새 브랜치/worktree를 만들지 않음.
- 원본은 `feature/aegis-paladin-routes` / `7c57e36`, 사용자 미커밋 변경 유지. 원본 Unity PID 32372 실행 중, 남아 있는 별도 빌드/csc/MSBuild/플레이 실행 프로세스 없음.
- 기록의 관전 누락/자기 부활 결함/임의 씬 빌드 위험은 c6fbc5b 실제 코드에서 이미 수정됨. ROADMAP의 해당 조사 행을 현재 상태로 갱신.
- 빌드 사전 검사의 87개 차이 중 10개는 CRLF/LF 차이뿐인 오탐. 읽기 시 줄바꿈과 BOM을 정규화하여 비교하고 디스크 내용은 변경하지 않도록 수정. 대소문자·공백·실제 내용 차이는 계속 차단.
- Scripts 폴더 밖 C#, asmdef/asmref/rsp 및 원본에만 있는 추가 컴파일 입력도 검사. 추가 파일은 삭제하지 않고 검토 필요로 표시.
- `Run-Part1BuildChecks.ps1`: 실제 임시 파일 I/O 검사 **13개 통과**. 줄바꿈/BOM, 대소문자/공백, 누락/추가 코드, 어셈블리/컴파일 옵션, Unity 버전, 원본 파일/meta 비수정 확인. 고유 임시 시험 폴더만 경로 검증 후 정리.
- 수정 후 `Build-Part1Windows.ps1 -CheckOnly` 실제 실행: **누락 24개/내용 변경 53개/추가 컴파일 입력 0개**, 원본 에디터 실행 중으로 정상 차단. Unity/BuildPipeline 실행 및 exe 생성 없음.
- 원본 사용자 변경과 겹치는 5개 파일을 PLAYTEST에 명시. 이 단계 이후 필요한 작업은 최신 소스 원본 반영 및 Unity/네트워크 실제 실행이며, 이전의 광범위 게임 검사를 이유 없이 반복하지 않음.
- 재개 종료 확인: PowerShell 3개 구문 및 `git diff --check` 통과. 원본 기준 309개 파일 SHA256 차이 0. 원본 브랜치/미커밋 변경 유지. worktree Library/Builds, 원본 RaidPart1.exe 없음. 원본 Unity만 실행 중이며 빌드/플레이 검사를 중복 실행하지 않음. 이번 변경은 Tests 아래 PowerShell 3개와 기록 문서 4개이며 게임 C#/씬/meta는 변경하지 않음.

## 원본 소스 반영 시작 (사용자 요청: 진행 요약의 1번)

- 원본 브랜치 `feature/aegis-paladin-routes` / `7c57e36`, 작업 소스 `feature/part1-playable-build` / `b5de65a` 확인. 브랜치 전환/Git 병합 없이 작업 파일에 반영한다.
- 원본 미커밋 7개 파일과 미추적 CharacterSkillTestWindow.cs의 내용이 보존 선행 커밋 `cd86fa2`와 모두 일치함을 확인. 사용자 수정은 최신 브랜치의 조상에 포함되어 있으며 이후 독립적인 원본 코드 변경 없음. 스킬 테스트 API·시험 창·관련 테스트와 직업 선택 UI의 후속 구현 확인.
- 기존 미추적 .meta 26개의 GUID가 최신 소스와 일치. 원본 ProjectSettings.asset도 최신 내용과 일치하므로 해당 파일과 기존 meta는 복사하지 않는다.
- 다음: 원본에 적용할 파일 목록과 원본 바이트 백업을 .git 아래에 기록 → 해시 재확인 후 소스/테스트/문서 반영 → 소스 일치 및 보호 대상 검증. Unity 빌드/플레이는 이번 단계에 포함하지 않는다.

## 원본 반영 완료 및 검증

- 백업 경로: `C:/coopraid/coopraid3d/.git/codex-backups/part1-apply-20261024T113825Z`. original/에 기존 파일 바이트, user-changes.patch, status-before.txt, index-before.txt, manifest.json에 전후 해시와 적용 목록 기록.
- 검토된 파일 113개 반영(기존 60개 갱신/53개 추가). 적용 직전 원본/소스 해시를 재확인하고 준비한 파일을 교체. 기존 meta/씬/프리팹/ProjectSettings에 쓰기 없음. 보호 대상 249개 해시와 원본 Git 인덱스 동일.
- 원본 브랜치 `feature/aegis-paladin-routes` / HEAD `7c57e36` 유지, 변경은 미커밋 상태. 사용자 변경은 cd86fa2를 포함하는 최신 버전으로 계승. Git 병합/전환/푸시/배포 없음.
- 작업 소스와 원본의 컴파일 입력 비교: 누락 0/내용 차이 0/추가 입력 0. 빌드 사전 검사는 원본 에디터 실행 중이라는 조건만 남음.
- 원본 `Run-AegisRouteChecks.ps1` 실제 실행: **21,565개 통과**. 기존 사용자 시험 도구/선택/자원/회복 상태 테스트를 포함한 계산 검사이며 Unity 실행은 아님.
- diff 검사에서 일시적으로 core.autocrlf=false 옵션을 사용해 CRLF를 공백 오류로 표시한 검사 호출이 있었음. 소스는 변경하지 않고 저장소 기본 설정으로 다시 실행하여 `git diff --check` 통과 확인.
- 원본 Unity 로그는 반영 이후 새 컴파일 완료 기록 없음. 작업 worktree Library 없음. 빌드/Play는 미실행이며 다음 단계에서 원본 재컴파일부터 확인한다.
