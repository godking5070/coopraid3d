# 아스텔라 재개 기준 — 2026-11-14

현재 상태는 이 문서를 먼저 확인한다. HANDOFF/ROADMAP의 날짜별 항목은 당시 작업 이력이다. 자동 검사, 실제 Unity 빌드, 사람 플레이, 실제 Steam 연결의 완료 여부를 각각 기록한다.

## 보존한 기준

- 원본: `C:/Astella/Astella`. 원본 브랜치 `feature/aegis-paladin-routes`와 기존 인덱스를 보존했다.
- 통합 작업: `integration/astella-resume-20261114`, `C:/Astella/Astella-resume`. Unity로 열지 않는다.
- 원본 전체 기준 커밋: `295ab332c018ca590bb04b8d129ae1d06fc20e3a`. 최신 playable 커밋을 부모로 두고 현재 원본 소스·씬·프리팹·GUID·자산을 그대로 포함한다.
- 원본 2,404파일의 실제 바이트 백업: `C:/Astella/branch-backups/resume-20261114-200225/original-working-files.tar.gz`.
- 파일별 SHA256, 원래 HEAD/인덱스 해시, 미커밋 작업 16폴더의 보존 커밋: 같은 폴더 `manifest.json`. 백업 Git 참조는 `backup/resume-20261114-200225/worktree-*`다.
- 이전 UI 프로토타입·보스 콘셉트의 전체 변경은 백업 참조로 보존했다. 현재 HUD와 보스 코드를 이전 버전으로 덮어쓰지 않았다. playable에만 있던 오류 보고 검사·로컬 큐 도구를 검토해 가져왔다.

## 검증할 고정 파티

`Tests/Scenarios/Part1-FourPlayer.json`은 팔라딘·오라클·나이트블레이드·레인저 4직업, 노멀/하드, 1~6스테이지를 지정한다. 각 직업의 첫 기원/첫 교체 선택과 기본 보조 2개를 재현용 초기 빌드로 사용한다. 이는 게임의 직업 선택 제한이나 사용자 확정 밸런스가 아니다. 실제 사람 플레이는 보류다.

| 단계 | 현재 증거 | 남은 확인 |
| --- | --- | --- |
| 원본 보존·통합 기준 | 기준 커밋, 바이트 백업, 16폴더 스냅샷 완료. 통합 수정 `f165343`의 All 30묶음 통과·원본 반영 완료 | 새 격리 실행 도구의 엔진 결과 기록 |
| 최신 빌드 | `f165343` 원본 Windows 배치 빌드 성공. 2026-11-14 20:24 KST, 프로토콜34, 입력2,179파일 지문 전후 일치 | 격리 실행 도구를 포함한 다음 빌드 |
| 1스테이지 파티 | 실제 직업 정의/선택/출발·데이터 격리를 고정한 601개 명령 검사 통과 | 사람 입력, 기믹·화면·밸런스는 보류 |
| 1부 완주·복원 | 고정 4직업의 두 난이도 6단계 상태/원자 파일 왕복 검사 통과 | 엔진 전투 완주와 앱 재시작 복원 |
| 2→4인 연결·성능 | 기존 규칙/SDK/가짜 전송 검사 존재 | 실제 LAN, 유효 Steam App ID/서로 다른 계정, 엔진 성능 실측 |

## 재현 명령

원본 Unity 참조는 읽기만 하며 worktree에 Library를 만들지 않는다.

```powershell
cd C:/Astella/Astella-resume
./Tests/Run-StudioChecks.ps1 -Group All -OriginalProject C:/Astella/Astella
./Tests/Build-Part1Windows.ps1 -OriginalProject C:/Astella/Astella -CheckOnly
```

`All`은 노멀/하드 6보스, 무력화, 새 직업, 우클릭 이동, HUD/로비, 오류 보고, Steam→Network→Compile/저장 종속 체인, 고정 파티 및 빌드 사전 검사를 포함한다. 각 하위 검사 로그와 종료 코드가 `results.json`에 남는다. 이전 `All`의 9묶음만으로 새 직업·이동까지 확인됐다고 해석하지 않는다.

새 빌드 경로는 `C:/Astella/Astella/Builds/AstellaWindows/Astella.exe`다. 빌드는 원본의 단일 메인 씬을 사용하고 UI 필수 참조를 검사한다. 배치 모드에서는 UI 자동 설치/재배치 도구가 실행되지 않는다. `BUILD_INFO.txt`, `BUILD_RECEIPT.json`, `BUILD_INPUTS.json`으로 커밋·소스/자산 지문·실행 파일/게임 어셈블리 해시와 빌드 전후 입력 보존 여부를 확인한다.

## 입력 없는 격리 실행

새 `Run-HeadlessResumeChecks.ps1`는 현재 소스/자산 지문과 실행 파일 해시가 빌드 영수증과 일치해야 실행한다. 개발 빌드·batchmode·명시적인 `--astella-check`와 전용 프로필이 모두 필요하다. 일반 실행과 배포 빌드에서는 검사 드라이버가 켜지지 않는다. 키보드·마우스 입력을 주입하지 않는다.

```powershell
./Tests/Run-HeadlessResumeChecks.ps1 -OriginalProject C:/Astella/Astella -Mode LAN -Players 2 -TimeoutSeconds 120
./Tests/Run-HeadlessResumeChecks.ps1 -OriginalProject C:/Astella/Astella -Mode LAN -Players 4 -TimeoutSeconds 120
./Tests/Run-HeadlessResumeChecks.ps1 -OriginalProject C:/Astella/Astella -Mode AI -Difficulty both -ThroughStage 1
./Tests/Run-HeadlessResumeChecks.ps1 -OriginalProject C:/Astella/Astella -Mode AI -Difficulty both -ThroughStage 6
```

LAN은 실제 게임 프로세스와 UDP를 사용해 연결·직업/기원/교체 선택·준비 상태 복제를 확인한다. 실제 전투·Steam 재접속·다른 PC 지연 검증과 구분한다. AI는 기본 피해100%의 실제 엔진 전투와 Unity JsonUtility 체크포인트를 검사하며, 전멸/정체/엔진 오류를 성공으로 처리하거나 피해·보스 수치를 바꾸지 않는다.

테스트 프로필은 캠페인뿐 아니라 로컬 설정과 HUD 배치 파일도 분리한다. 프로필 없는 검사 요청은 일반 저장으로 대체하지 않고 거절한다. 각 실행 전후 일반 사용자 데이터 파일 지문을 대조한다. 검사 도구는 자신이 생성한 프로세스만 종료하며 Steam이나 사용자 게임을 종료하지 않는다. 기록된 프레임 간격·GC·메모리는 무화면 조건의 관측값이고, GPU/사람4인 성능이나 밸런스 판정이 아니다.

기본 Steam App ID는 0이며 임의 샘플 ID를 설정하지 않는다. 실제 마우스/키보드 조작·사람 플레이는 사용자 보류 상태다. 명령 검사 통과를 실제 플레이·연결·성능 검증으로 대체하지 않는다.
