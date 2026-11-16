# UI 보존과 전투 가독성 수정

2026년 11월 16일. 요청한 1번과 2번인 UI 자동 재적용 방지, HUD·이름표·카메라·등장 연출과 표시 오류를 수정했다. 최종 Windows 빌드는 `6ced6e7`이다.

| 영역 | 변경 내용 | 주요 파일 |
| --- | --- | --- |
| UI 자산 보존 | 10개 Editor 도구의 import·idle·Play 종료 자동 저작 경로를 제거했다. 기존 Apply·Separate·Capture 메뉴는 명시적으로 사용할 수 있다. | `Assets/Scripts/Editor/`의 HUD installer·layout refinement·`RaidUIAuthoring.cs`·`RaidUISeparation.cs` |
| 보스 HUD | 시전바 아래 대상 글자의 외곽선만 3px→1px로 줄였다. 기존 배치·폰트·테마를 유지했다. | `RaidUIAuthoring.cs`, 기존 `RaidHUD.prefab` |
| 이름표와 선택 | 봇 이름은 짧게 표시하고 대기실·통로·일시정지·Replica에서도 방향을 갱신한다. 등장·격파 연출에서는 생성된 봇 이름표를 숨겼다가 복구한다. 선택 장식 링과 중복 화살표를 줄였다. | `PartyBotAI.cs`, `TargetingManager.cs` |
| 전투 카메라 | 3D에서 플레이어와 보스, 야누스의 추가 본체를 HUD 사이에 담도록 초점과 거리를 보정한다. 사용자 회전·줌 선호값을 자동 보정 거리로 덮어쓰지 않는다. | `QuarterViewCamera.cs`, `CameraControlMath.cs` |
| 등장 연출 | 실제 모델 범위가 화면을 벗어나면 기본 카메라로 전환한다. Stage 1 카메라·보스 클립은 작성한 시작 좌표를 유지하도록 재생 설정을 수정했다. | `RaidStageCutscenePresentation.cs`, `RaidStageCutsceneRuntime.cs`, `RaidCutsceneAuthoring.cs`, `ArrivalExample.playable` |
| 안내·복원 | 저장 난이도, 현재 6보스 안내, 복원한 직업 이름, 완료 후 정산 대기 문구를 수정했다. 저장 검증 오류와 IO 오류를 구분했다. 훈련 안내는 현재 허수아비 동선에 맞췄다. | `CampaignUI.cs`, `CampaignAdapters.cs`, `CampaignRuntime.cs`, `CampaignStorage.cs` |

2D 카메라의 기존 조작·투영은 유지한다. 보스 전후측면 링, 표적·집결·산개 등 필수 기믹 표시, 피해량·공격 간격·하드 예고 시간은 변경하지 않았다.

자동 저작 제거 대상은 `AstellaPlayerHUDV3Installer`, `AstellaBossHUDV4Installer`, `AstellaRequestedUIResourceSwap`, `AstellaClassHubsInstaller`, `AstellaClassHubLayoutRefinement`, `AstellaHUDTextOutlineInstaller`, `AstellaSplitCombatHUD`, `AstellaPlayHUDLayout`, `RaidUIAuthoring`, `RaidUISeparation`이다. 모두 기존 Editor 폴더의 C# 파일이다.

**작성 자산의 변경 범위**

원본 Unity의 명시적 Editor 메서드와 `PrefabUtility`·`AssetDatabase`를 사용했다. YAML은 직접 편집하지 않았다. 백업은 `Logs/UIRepairBackups/`와 `Logs/CutsceneRepairBackups/`에 있다.

- `Assets/UI/AstellaOrnate/Applied-20261106-131209-797/RaidHUD.prefab`: 보스 대상 글자 Outline 거리 한 곳만 `(3,-3)`→`(1,-1)`.
- `Assets/Cutscenes/Examples/ArrivalExample.playable`: 두 클립의 `Remove Start Offset`만 해제했다. 첫 위치를 제거하던 설정 때문에 카메라 좌표가 틀어지고 보스가 바닥 아래로 내려갔다. [Unity 설정 설명](https://docs.unity3d.com/kr/Packages/com.unity.timeline%401.6/api/UnityEngine.Timeline.AnimationPlayableAsset.removeStartOffset.html).
- 씬·AnimationClip·AnimatorController·기존 meta/GUID와 타임라인 키프레임·트랙 구성은 유지했다. 새 예제 생성도 작성 좌표를 유지하는 설정을 사용한다.

**검증 결과**

| 검사 | 결과와 범위 |
| --- | --- |
| Studio UI | 5/5 스크립트 통과. 자동 저작 정책 40개와 실제 봇 표시 메서드 53개 포함. 이전 자동 설치 소스는 대조 검사에서 예상대로 실패했다. |
| 저장·진행 | 저장 41,863개, 진행 1,003개, 새 런 16개, 실제 직업 복원 234개, 실제 정산·경계 저장·이리스 60개 통과. 완료 저장·삭제 잠금 실패·부분 HP와 이름 복원을 포함한다. |
| 카메라 계산 | 이동·회전·줌·입력 수명·투영 경계 3,460개와 근접 범위 4,017개 통과. |
| Network·컴파일 | 변경 작업본의 Network 체인과 최종 원본 Unity 런타임·Editor 컴파일, Windows 빌드 통과. 실제 Steam 연결 결과는 아니다. |
| 원본 Editor GUI | 원본 프로젝트의 최초 GUI import·SampleScene 대기와 실제 Play 진입·종료를 확인했다. 전후 입력 지문이 빌드와 일치했고 Editor도 정상 종료했다. Play는 별도 검사 프로필을 사용했다. |
| 실제 화면 | 하드 저장 요약, 6보스 안내, 재실행 후 팔라딘 이름, 짧은 봇 이름과 통로 방향, 2D/3D 전환, 3D 거리 보정, 시전바·대상 줄 분리 확인. |
| Stage 1 연출 | 최종 빌드에서 보스가 정상 높이로 등장했다. 연출 후 전투 카메라·이름표 복구, 전투 진입·메뉴 복귀·정상 종료를 확인했다. 최종 실행 C# 예외 헤더 0건. |

최종 빌드 입력 SHA256은 `c2426901389082657e5eac7bdb1ab0763b23889413b46e81545731b2a6df38b9`, 입력 파일은 2,179개다. 빌드 전후 정규화 지문이 일치했다. 검사 프로필은 일반 저장과 분리했고, 종료 후 일반 저장 폴더는 기존과 같은 파일 0개였다. 엔진 Unity 캐시는 이 비교에서 제외했다.

새 회귀는 `Tests/Run-UIAuthoringSafetyChecks.ps1`, `Tests/PartyBotPresentationTests.cs`, `Tests/Run-PartyBotPresentationChecks.ps1`이며 UI 그룹에 연결했다. 기존 `CameraControlTests.cs`, `CampaignSaveTests.cs`, `CampaignSettlementRestoreTests.cs`, `DamageCampaignRestoreTests.cs`, `Run-CampaignSaveChecks.ps1`, `Run-StudioChecks.ps1`도 보강했다.

**원본에서 확인할 순서**

1. `C:/Astella/Astella`만 Unity로 연다. 명시적 Apply 메뉴를 실행하지 않았다면 UI 디자인이 자동 재적용되지 않아야 한다.
2. 격리 프로필로 하드 런을 저장·종료한 뒤 저장 관리의 하드 표시와 이어하기 후 직업명을 확인한다.
3. Stage 1 등장→전투에서 보스가 보이고 이름표·카메라가 복구되는지 확인한다. 이동·줌·F12 전환 후 시전 정보와 선택 표식을 확인한다.
4. `./Tests/Run-StudioChecks.ps1 -Group UI -OriginalProject C:/Astella/Astella`로 관련 명령 검사를 실행한다.

실제 화면 확인은 Stage 1과 1920×1080 환경이다. 사람 4인·Steam·모든 보스의 제작 컷씬·모든 해상도·실측 성능은 별도 검증 항목이다. 앞선 원인 미상 종료 1회도 이번 수정으로 해결됐다고 판정하지 않았다.

로컬 증거는 `C:/Astella/ui-readability-evidence-20261116/`의 `verification-summary.json`, `VERIFIED_BUILD_RECEIPT.json`, `editor-gui-preservation.json`, `editor-play-preservation.json`, `Player-arrival-verified.log`, `verified-arrival-*.png`, `24-verified-combat-restored.png`, `final-ui/`, `network/`에 있다. 저장 회귀 로그는 `C:/Astella/playtest-evidence-20261116/flow-display-checks.log`에 보존했다.
