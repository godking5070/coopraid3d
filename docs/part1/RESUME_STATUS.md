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
| 원본 보존·통합 기준 | 기준 커밋, 바이트 백업, 16폴더 스냅샷 완료. `d41e38a`의 All 31묶음 통과 | 원격 병합·푸시는 하지 않음 |
| 최신 빌드 | `d41e38a`, 2026-11-14 22:18 KST 원본 Windows 배치 빌드·필수 HUD 참조 검사 성공. 프로토콜34, 입력2,179파일 지문 전후 일치 | 사람 화면/GPU 검증 보류 |
| 1스테이지 파티 | 고정 빌드/격리 601개, 실제 출발·봇 준비 메서드 917개 명령 검사 통과. 노멀 실제 AI 클리어 | 하드 AI는 1스테이지에서 전멸. 사람 입력·화면·밸런스는 보류 |
| 1부 완주·복원 | `2990c59` 노멀 실제 AI 1~6 연속 완주·완료 저장, `10c9b17` 일반/하드 싱글 앱 재시작·비기본 빌드 복원 통과 | 하드 엔진 완주 미확인 |
| 2→4인 연결·성능 | 실제 게임 2인/4인 UDP 연결·직업/기원/교체/준비 복제 통과. 무화면 프레임/GC/메모리 기록 | 실제 Steam·다른 PC·GPU 성능 미확인 |

## 재현 명령

원본 Unity 참조는 읽기만 하며 worktree에 Library를 만들지 않는다.

```powershell
cd C:/Astella/Astella-resume
./Tests/Run-StudioChecks.ps1 -Group All -OriginalProject C:/Astella/Astella
./Tests/Build-Part1Windows.ps1 -OriginalProject C:/Astella/Astella -CheckOnly
```

`All`은 노멀/하드 6보스, 무력화, 새 직업, 우클릭 이동, HUD/로비, 오류 보고, Steam→Network→Compile/저장 종속 체인, 고정 파티 및 빌드 사전 검사를 포함한다. 각 하위 검사 로그와 종료 코드가 `results.json`에 남는다. 이전 `All`의 9묶음만으로 새 직업·이동까지 확인됐다고 해석하지 않는다.

추가로 확인한 실제 결함: `CharacterActor.ResetEncounter(NewRun)`이 새 딜러만 빌드를 보존하고 탱커·힐러는 기원만 재선택해 비기본 교체 스킬/핵심/보조를 잃었다. 수정 전 실제 메서드 기반 검사에서 팔라딘 첫 기원·변경 빌드 유실을 재현했다. 모든 새 9키 정의가 새 런 빌드 보존 경로를 사용하도록 수정했으며, 탱커3종·힐러3종·각3기원·기본/변경 선택의 901개 검사가 통과했다. 이전 런의 V 권리·카드 효과·진행·전투 수치는 전달하지 않는다. `Run-DepartureBuildChecks.ps1`를 Characters/Progression/All에 연결해 All은 31묶음이 된다.

일반 싱글의 실제 출발 검사에서 추가 결함을 재현했다. 네이티브 파티 봇의 `RestorePartyLife(resetCooldowns: true)`가 행동 타이머를 0으로 두고, `CaptureNetworkCombat`는 이를 1.25~1.45초 남은 GCD로 기록했다. 따라서 출발 직후의 체크포인트가 안전 경계 검증에 거절됐다. `PartyBotAI.cs`와 `RaidCombatWorld.cs`에서 같은 행동 간격을 사용하고 전투 준비 때 준비 완료 상태로 초기화한다. 일반 부활은 기존 지연·구출 쿨타임을 유지한다. 실제 복구/캡처 메서드와 실제 런타임 정의를 사용하는 16개 회귀검사를 추가해 총917개가 통과했다. 저장 검증을 완화하지 않았다. 거절된 원본 상태는 `native-single-diagnostic/case-bc77d779361643e0936c4aa95220925d/rejected-working-checkpoint.json`에 보존했다.

콘셉트 작업 대조: CharacterMatched와 ModelingRedesign의 이미지15개가 각 manifest SHA256과 모두 일치한다. ConceptArt 아래 실제 mesh 파일은 없으며 두 manifest도 생산용 mesh/rig 미제공 상태를 명시한다. `C:/Astella/branch-backups/resume-20261114-200225/art-audit.json`에 원본 경로와 지문을 기록했다. 기존 활성 HUD나 임시 3D 비주얼을 이미지로 임의 대체하지 않았다.

새 빌드 경로는 `C:/Astella/Astella/Builds/AstellaWindows/Astella.exe`다. 빌드는 원본의 단일 메인 씬을 사용하고 UI 필수 참조를 검사한다. 배치 모드에서는 UI 자동 설치/재배치 도구가 실행되지 않는다. `BUILD_INFO.txt`, `BUILD_RECEIPT.json`, `BUILD_INPUTS.json`으로 커밋·소스/자산 지문·실행 파일/게임 어셈블리 해시와 빌드 전후 입력 보존 여부를 확인한다.

## 입력 없는 격리 실행

새 `Run-HeadlessResumeChecks.ps1`는 현재 소스/자산 지문과 실행 파일 해시가 빌드 영수증과 일치해야 실행한다. 개발 빌드·batchmode·명시적인 `--astella-check`와 전용 프로필이 모두 필요하다. 일반 실행과 배포 빌드에서는 검사 드라이버가 켜지지 않는다. 키보드·마우스 입력을 주입하지 않는다.

```powershell
./Tests/Run-HeadlessResumeChecks.ps1 -OriginalProject C:/Astella/Astella -Mode LAN -Players 2 -TimeoutSeconds 120
./Tests/Run-HeadlessResumeChecks.ps1 -OriginalProject C:/Astella/Astella -Mode LAN -Players 4 -TimeoutSeconds 120
./Tests/Run-HeadlessResumeChecks.ps1 -OriginalProject C:/Astella/Astella -Mode AI -Difficulty both -ThroughStage 1
./Tests/Run-HeadlessResumeChecks.ps1 -OriginalProject C:/Astella/Astella -Mode AI -Difficulty both -ThroughStage 6 -TimeoutSeconds 3600
./Tests/Run-HeadlessResumeChecks.ps1 -OriginalProject C:/Astella/Astella -Mode SaveResume -Difficulty both -ThroughStage 1
```

LAN은 실제 게임 프로세스와 UDP를 사용해 연결·직업/기원/교체 선택·준비 상태 복제를 확인한다. 실제 전투·Steam 재접속·다른 PC 지연 검증과 구분한다. AI는 기본 피해100%의 실제 엔진 전투와 Unity JsonUtility 체크포인트를 검사하며, 전멸/정체/엔진 오류를 성공으로 처리하거나 피해·보스 수치를 바꾸지 않는다.

SaveResume은 일반 단일 플레이의 첫 출발 체크포인트에 비기본 빌드를 저장하고 프로세스를 정상 종료한 뒤, 같은 테스트 프로필의 별도 프로세스에서 실제 이어하기 경로를 실행한다. 같은 런·프로필, 다른 프로세스 세션·도전 ID, 빌드·HP·쿨타임·부활4회를 대조한다. 문까지의 이동은 검사 대상에서 제외하고 해당 위치에서 기존 문 출발 함수를 호출한다. 이후 스테이지 카드 정산/사람 입력 검증을 이 첫 경계 검사로 대체하지 않는다.

테스트 프로필은 캠페인뿐 아니라 로컬 설정과 HUD 배치 파일도 분리한다. 프로필 없는 검사 요청은 일반 저장으로 대체하지 않고 거절한다. 각 실행 전후 일반 저장·설정 등 파일을 백업하고 지문을 대조한다. Unity/ 아래 엔진 분석·셰이더 캐시는 별도의 전후 지문으로 기록한다. 첫 검사에서 이 엔진 캐시 추가가 전체 폴더 지문 차이로 잡혔으며, 제품 저장과 혼동하지 않도록 보고서를 구분했다. 검사 도구는 자신이 생성한 프로세스만 종료하며 Steam이나 사용자 게임을 종료하지 않는다. 기록된 프레임 간격·GC·메모리는 무화면 조건의 관측값이고, GPU/사람4인 성능이나 밸런스 판정이 아니다.

격리 실행 중 발견한 도구 문제도 보완했다. Null 그래픽 장치에서는 물리 도형만 생성하고 렌더 재질 설정을 하지 않는다. 실제 그래픽 장치의 셰이더 오류 검사는 유지한다. UTF-8 결과 파일의 한글 경로를 명시적으로 디코딩한다. 자동 종료는 기존 `DisconnectConfirmed` 세션 정리를 거쳐 일반 종료 확인창에 막히지 않게 했다. 실패했던 실행과 영수증은 별도 case 폴더에 그대로 남긴다.

기본 Steam App ID는 0이며 임의 샘플 ID를 설정하지 않는다. 실제 마우스/키보드 조작·사람 플레이는 사용자 보류 상태다. 명령 검사 통과를 실제 플레이·연결·성능 검증으로 대체하지 않는다.

## 실제 실행 결과와 남은 단계

아래 경로는 모두 `C:/Astella/branch-backups/resume-20261114-200225/` 아래다. 각 `results.json`은 커밋·입력 지문·프로세스 종료 코드·일반 저장 보존 지문과 개별 case 경로를 포함한다. case의 원본 UTF-8 JSON과 전체 로그를 근거로 사용한다.

- `checks-native-bot-all/results.json`: `10c9b17`, 31/31 통과, 실패0. 컴파일·순수 로직/실제 메서드 스텁/파일 IO 범위다.
- `native-final-lan-two/results.json` 및 `native-final-lan-four/results.json`: 최종 `10c9b17` 빌드의 실제 2/4프로세스 UDP 연결·준비 복제·정상 종료 통과. 최초 동시 접속 fixture는 역할 도착 순서가 뒤바뀌어 실패했고, 역할/빌드/준비 확정 후 다음 클라이언트를 실행하도록 수정했다. 제품의 역할 중복 제한은 유지했다.
- `native-single-fixed/results.json`: 최종 `10c9b17` 빌드에서 일반/하드 각각 출발 저장→정상 종료→새 프로세스 이어하기 통과. 4개 case 모두 일반 사용자 파일 지문 보존, 종료코드0. 난이도별 같은 런/프로필과 다른 프로세스 세션/도전 ID, 비기본 탱커 빌드·체력/쿨타임·부활4회를 대조했다. 첫 준비 경계 검증이며, 사람의 문 이동과 후기 스테이지 이어하기를 대신하지 않는다.
- `native-ai-normal-six/results.json`: `2990c59`, 기본 피해100%, 노멀 1~6 연속 완주. 실제 시간618.26초, 재시도0, 부활1회, 클리어6개, 확정 카드20개(파티원별5개), 파티원 모두 3스테이지 V 해금. Unity JSON 완료 체크포인트 검증 및 정상 종료 통과. 엔진 오류0.
- `native-ai-hard-six/results.json`: 최종 `10c9b17`에서 하드 1~6 연속 실행을 시도했으나 약140초에 1스테이지 AI 전멸로 실패했다. 엔진 오류0, 부활4회 소진, 일반 파일 지문 보존. 앞선 `final-ai-stage-one/results.json`의 하드1스테이지 약161초 전멸도 보존했다. 하드 완주·밸런스가 검증됐다고 해석하지 않는다.

하드 실패 로그에는 카운터3회·무력화2회 성공, 봉쇄 피격3명, 힐러에 부활3회/레인저에1회 사용, 이후 공명 분담 인원4→2→1 감소가 기록됐다. 단순히 카운터가 전혀 실행되지 않은 상태는 아니다. `GuardianHardDestination`의 봉쇄 탈출은 열린 균열 밖 한 점으로 직진하고, 네이티브 충돌은 벽을 막으므로 경유 이동/사전 위치 선정이 다음 AI 조사 지점이다. 실제 피격 순간 위치·치유 내역을 이 로그만으로 확정할 수 없어 원인 가설로 남긴다. 보스 체력·피해·공격 간격을 변경해 성공으로 만들지 않았다.

### 하드 AI 후속 조사·수정

`00e492f`의 `native-hard-tactic-trace/`에 0.25초 간격 실제 위치·HP·시전·단계·열린 균열 위치를 기록했다. `tactics.csv`는 명시적인 격리 개발 검사에서만 생성하고 AI 목적지 함수를 호출하지 않는 관측이다. 해당 실행도 1스테이지 약167초에 전멸했다. 봉쇄 종료 직전 근딜 `(-11.2201,-1.2960)`, 원딜 `(-10.4910,-3.5507)`이 벽 안쪽에 남고 열린 균열은 `(-11.5911,-3.1058)`에 있는 것을 확인했다.

`d41e38a`에서 `GuardianNormalData.cs`/`BossGuardianHard.cs`의 AI 경로를 보완했다. 조준선의 같은 쪽에서 균열 근처로 미리 모이고, 벽 안쪽 경유점에 맞춘 뒤 열린 틈으로 나온다. 이미 밖으로 나간 AI는 안으로 되돌아오지 않는다. `RaidReplacementAI.cs`는 기존 직업만 적용하던 이동기 선택 조건에 새 9키 직업도 포함한다. 보스 수치·공격 시간·회전 제한·AI 반응 설정·플레이어 입력/이동 충돌은 유지한다.

하드 상태/기하2,547개, 노멀20,048개 및 `checks-guardian-routing-all/results.json`의 31/31 묶음이 통과했다. 순수 계산 검사는 실제 기록의 시작점과 기존 6초 창을 사용하고, 벽 회전별 조준선/캐릭터 반경·균열 폭·벽 내부 경유를 검사한다. 이를 실제 엔진 충돌 검증과 구분한다.

후속 실제 실행은 `native-hard-routing-six/`, `native-normal-routing-six/`다. 노멀은 약636초에 재시도0/부활0, 클리어6/카드20, 파티원별 카드5개, 완료 저장/정상 종료/일반 파일 보존으로 통과했다. 하드는 1~5 클리어 뒤 6스테이지 전투 중 검사 자체의1,800초 제한으로 종료됐다. 전멸이나 엔진 오류가 아니라 시간 제한 실패이며, 완료 판정은 하지 않는다. 이 실측에 맞춰 개발 검사/launcher의 허용 상한을3,600초로 늘리고, AI 6단계 검사의 기본 제한도3,600초로 정한다. 게임 전투 시간·체력·피해는 유지한다.

하드 첫 봉쇄는 균열 유도 실패로 피격4명이었으므로 모든 AI 기믹을 해결했다고 주장하지 않는다. 두 번째 완전 봉쇄에서는 실제 열린 균열 `(0,-12)`을 통해 4명이 모두 반경12.65~14.88로 벽 밖에 나가고 피격0명이 기록됐다. 최종 하드 완주/저장은 연장한 새 연속 실행의 영수증으로 별도 확인한다.

원격도 추가 대조했다. GitHub 브랜치 검색의 끝 페이지와 `git ls-remote --heads origin` 모두 `ingame-move-system` 하나만 반환했다. HEAD는 로컬 캐시와 같은 `f80d0c5476cd1e6f49be2adde3e39610c810014e`(2026-10-21)이며 `remote-audit.json`에 기록했다. 최근 개발 변경은 원격의 별도 최신 브랜치가 아니라 보존한 로컬 브랜치/worktree에 있다.

노멀 완주 과정의 무화면 관측은 평균 프레임 간격26.31ms, 시작 로딩 포함 최대835.63ms, Unity 할당 메모리 최대100.03MiB였다. 이 실행은 AI·로그·컴파일 검사 병행 환경이며 GPU 렌더링과 사람 입력이 없다. 출시 성능 기준이나 4인 실플레이 FPS 판정으로 사용하지 않는다.

Steam 2→4인 검증에는 유효한 아스텔라 App ID, 서로 다른 테스트 계정과 PC가 필요하다. 현재 설정은0이고 환경 정보는 요청한 상태다. 실제 사람 플레이·마우스/키보드·HUD 가독성·공격 예고 시각·GPU 성능은 사용자 보류를 유지한다. 하드 AI 전멸 원인/행동 개선과 하드 완주, 실제 Steam 연결은 남은 단계다.

원본에서 추후 확인할 때는 `Builds/AstellaWindows/BUILD_RECEIPT.json`과 빌드 입력 지문을 먼저 대조한다. 사용자 보류가 해제된 뒤 원본 폴더에서 비기본 탱커/힐러 빌드 출발·이어하기, 노멀/하드 공격 예고와 HUD, Steam 2→4인 준비/전투/재접속을 순서대로 확인한다. 통합 worktree를 Unity로 열지 않는다.

## 변경 검토 위치

| 파일 | 검토할 변경 |
| --- | --- |
| `Assets/Scripts/CharacterActor.cs` | 모든 새 9키 직업의 출발 빌드 보존. 이전 런 진행/카드/V 해금은 초기화 |
| `Assets/Scripts/PartyBotAI.cs`, `RaidCombatWorld.cs` | 전투 준비에서 네이티브 봇 GCD를 준비 상태로 초기화. 일반 부활은 쿨타임을 유지 |
| `Assets/Scripts/GuardianNormalData.cs`, `BossGuardianHard.cs`, `RaidReplacementAI.cs` | 하드 봉쇄 AI의 조준선 옆 사전 이동·벽 안 경유·균열 탈출 및 새 9키 직업 이동기 선택 |
| `Assets/Scripts/AiSpectatorDevelopment.cs`, `RaidPrimitive.cs` | 명시적인 격리 개발 검사와 Null 그래픽 장치 처리. 일반/릴리스 입력 흐름과 실제 GPU 셰이더 검사는 유지 |
| `Assets/Scripts/CampaignStorage.cs`, `LocalSettingsRuntime.cs`, `LocalHUDLayoutController.cs` | 검사 프로필 없는 실행 거절, 일반 저장/설정/HUD와 테스트 데이터 분리 |
| `Assets/Scripts/Editor/RaidMultiplayerBuild.cs` 및 UI 자동 설치 도구 | 활성 HUD 필수 연결 검사, 배치 빌드 중 사용자 편집 UI 자동 설치/재배치 방지 |
| `Tests/Run-StudioChecks.ps1`, `Build-Part1Windows.ps1`, `Run-HeadlessResumeChecks.ps1` | 전체 검사 선택, 원본만 빌드, 소스/자산/산출물 지문, 실행 중 출력 파일 보호, 격리 실제 프로세스 영수증 |
| `Tests/DepartureBuildTests.cs`, `ResumeScenarioTests.cs` 및 오류 보고 검사 | 발견 결함 회귀, 고정 4직업 6단계 상태/파일 왕복, 기존 오류 보고 회귀 |

원본 HEAD와 인덱스는 기존 상태를 보존했다. 검토한 파일만 원본 작업 파일에 반영했으며, 원본 인덱스 SHA256은 `ce345c2144a658aa620262e9f99f1b5c0fa36a1ea44789cb5a51ea857cc59c48`이다. 작업 worktree에는 Library를 만들지 않았다. 씬·프리팹 YAML/기존 GUID는 수정하지 않았다.

현재 빌드 입력 SHA256은 `bcedf553712e72123c2af61b504cdcdb0bb0b3d0ecf1548e13c67fad3accf3a6`다. 빌드 영수증의 코드 커밋은 `d41e38a684db5ad584fb33d8ab75429c8ad4596a`다. 이전 `10c9b17` 빌드와 실행 증거도 별도로 보존했다. 하드 완주와 실제 Steam 연결이 남아 있으므로 전체 재개 목표는 완료로 처리하지 않는다.
