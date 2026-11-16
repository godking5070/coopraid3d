# Prism V4: 메뉴·보조 HUD 스타일 적용 결과

승인된 페인팅 Prism의 은색 금속과 보라색 패널을 기존 메뉴·보조 HUD에 적용했다. 기존 동작과 한글 폰트를 유지하면서 메인 HUD, 카탈로그 뷰, 반복 항목, 빌드 스테이션을 별도 사본으로 저작했다. 현재 기록은 2026-11-16 17:21 적용본, 17:46 최종 보정·GPU 카탈로그와 17:47까지의 실제 Play 증거를 기준으로 한다.

활성 자산 폴더는 `Assets/UI/AstellaPrismV4/AppliedRemaining/Applied-20261116-172147-686/`이다. 이전 `AppliedMain/Layout1920-20261116-162926-372/RaidHUD.prefab`과 원래 `AstellaAstral-20261106-120639-548` 자산은 보존했다. 원본 Unity의 Editor API로 복사·저장했고 씬·프리팹 YAML을 직접 편집하지 않았다. 이전 씬 인스턴스는 삭제하지 않고 비활성화했다.

| 적용 대상 | 내용 |
|---|---|
| 메인 HUD 1개 | 기존 Prism 전투 구성과 배치를 보존하고 주변 패널·버튼을 같은 재질로 정리 |
| 카탈로그 뷰 10개 | character, dash, potion, origin, metrics, resurrection, settings, connection, wipe, skill-reward |
| 반복 템플릿 7개 | Skill, Hotbar, Party, Resource, Status, FormButton, FormText |
| 빌드 스테이션 1개 | 기존 참조·선택 구조를 유지한 `BuildStation.prefab` 사본 |
| 공통 테마 | 같은 폴더의 `PrismTheme.asset`과 페인팅 패널·버튼 표면 사용 |

위 표의 프리팹 합계는 19개이며 테마 자산은 별도다. 새 스킬·역할 아이콘은 제작하지 않았다. 메인 메뉴의 기존 별자리 장식은 객체를 남겨 비활성화하고, 기존 페인팅 허브의 배경·수정·금속 프레임을 조합한 장식으로 교체했다. 이전 훈련 카드와 참조는 유지하며 런타임의 기존 표시 정책을 따른다. `DevBugReportUI`의 개발용 IMGUI는 제품 스킨 적용 범위에 포함하지 않는다.

원래 폰트 참조·크기·스타일, Button 이벤트와 제어 참조, Selectable 상태·탐색 설정, 바인딩 키와 기존 객체를 보존하는 검사를 적용했다. 장식으로 추가한 Graphic은 포인터 입력을 가로채지 않는다. 기존 Sprite와 원본 자산의 GUID를 바꾸지 않고 복사본은 자체 참조로 연결했다. EnemyList의 배경 높이 자동 맞춤은 복사본에서만 선택적으로 활성화하며, `fitBackgroundToRows=false`인 기존 저작물의 기본 규칙을 유지한다.

최종 보정에서는 전투 기록과 적 목록의 외곽 패널에도 같은 표면·프레임을 적용했다. `RaidConnectionUI.ButtonAt`이 런타임에 생성하는 난이도 버튼 등에도 `AstellaPrismProceduralSurface.ApplyNew`를 적용해 저작된 버튼과 재질을 맞췄다. 기존 난이도 선택·게임 동작·클릭 연결은 유지한다.

검사와 이미지의 원시 증거는 저장소 밖의 `C:/Astella/prism-v4-remaining-ui-20261116/`에 있다. 아래 결과는 각 파일이 기록한 범위로 제한한다.

| 증거 | 결과 | 검증 범위 |
|---|---|---|
| [authoring-latest.json](C:/Astella/prism-v4-remaining-ui-20261116/authoring-latest.json) | `passed=true`, 19개 자산, 1,796개 Graphic, 총 93,641개 바인딩·보존 검사 | 19개 원본 SHA256이 적용 전후 동일. `sourcesPreserved`, `profileValidated`, `sceneSaved` 모두 true |
| [첫 보정 완료](C:/Astella/prism-v4-remaining-ui-20261116/Refine-20261116-172915-440/complete.txt) | `checks-*.json` 19개 전부 통과, 총 103,948개 검사, 오류 0개 | 보정 전후 로드된 프리팹의 기존 객체·컴포넌트·폰트·제어·바인딩 보존 |
| [두 번째 보정 완료](C:/Astella/prism-v4-remaining-ui-20261116/Refine-20261116-173512-249/complete.txt) | `checks-*.json` 19개 전부 통과, 총 103,958개 검사, 오류 0개 | 보정 전후의 보존 계약 |
| [세 번째 보정 완료](C:/Astella/prism-v4-remaining-ui-20261116/Refine-20261116-174414-622/complete.txt) | `checks-*.json` 19개 전부 통과, 총 104,148개 검사, 오류 0개 | 보정 전후의 보존 계약 |
| [최종 보정 완료](C:/Astella/prism-v4-remaining-ui-20261116/Refine-20261116-174615-258/complete.txt) | `checks-*.json` 19개 전부 통과, 총 104,151개 검사, 오류 0개 | 새 코드 재컴파일 후 최신 보정본의 보존 계약. 예: [RaidHUD 검사](C:/Astella/prism-v4-remaining-ui-20261116/Refine-20261116-174615-258/checks-RaidHUD.json), [connection 검사](C:/Astella/prism-v4-remaining-ui-20261116/Refine-20261116-174615-258/checks-View-connection.json). 원본 19개 SHA256 동일 재확인 |
| [UI-Checks-Final/results.json](C:/Astella/prism-v4-remaining-ui-20261116/UI-Checks-Final/results.json) | 선택 6개·완료 6개, 전부 exitCode 0, failures 비어 있음 | 17:44 명령 검사. 실제 Play·Steam 검증으로 집계하지 않음 |
| [catalog-preview.json](C:/Astella/prism-v4-remaining-ui-20261116/catalog-preview/catalog-preview.json) | 1920×1080 GPU 렌더 9개, 전부 rendered=true, errors=[] | 08:46:28Z(17:46:28 KST), 최종 보정 후 디자인 렌더. 원래 활성 씬 복원·dirty 상태 보존 모두 true |

UI 명령 검사는 `Run-UIAuthoringSafetyChecks.ps1`, `Run-PartyBotPresentationChecks.ps1`, `Run-AstellaPrismBridgeChecks.ps1`, `Run-HUDLayoutChecks.ps1`, `Run-LobbyPracticeChecks.ps1`, `Run-LobbyBuildStationChecks.ps1`이다. 실행 시점은 17:44:22~17:44:47이다. 17:45:59에 원본 Unity에서 Assets Refresh와 새 코드 컴파일을 완료하고 최종 보정을 다시 수행했다. 그 이후의 보존 여부는 17:46 보정 폴더의 19개 `checks-*.json`, 실제 렌더는 새 GPU 카탈로그와 아래 Play 이미지로 별도 확인했다.

GPU 카탈로그는 [AstellaPrismRemainingPreview.cs](../../Assets/Scripts/Editor/AstellaPrismRemainingPreview.cs)의 `Tools/Astella/Prism V4/Render Remaining UI Catalog`로 생성했다. 실제 URP `SingleCameraRequest`와 임시 Additive Scene을 사용하며 현재 프로필에서 설정, 연결, 기원, 캐릭터, 지표, 부활, 전멸, 스킬 보상, 빌드 스테이션을 하나씩 복제해 촬영했다. [9개 패널 비교 이미지](C:/Astella/prism-v4-remaining-ui-20261116/catalog-preview/catalog-overview.png)에서 전체 재질을 검토할 수 있다.

이 렌더는 **디자인 미리보기이며 게임플레이 검증이 아니다.** 게임 presenter와 액션을 실행하지 않고 선택한 패널만 보이도록 임시 복제본을 정리한다. 원본 텍스트가 비어 있는 일부 결과·부활·전멸·보상 문구는 표시용 샘플이며 JSON의 `previewOnlyText`에 기록했다. 빌드 스테이션은 `sprite == null`인 `Artwork` Image 36개를 복제본에서만 숨겼다. 연결 화면의 레거시 `_training` 버튼도 복제본에서만 숨겼다. 이 처리들은 실제 런타임 동작을 수정하거나 검증한 것으로 집계하지 않는다.

실제 Play 증거로 확인한 화면은 다음과 같다. 게임 화면 캡처의 각 JSON은 Direct3D12 GPU, 1920×1080 화면·텍스처, 활성 bridge 1개를 기록한다. 홈·설정·배치 편집·방 만들기는 `mode=Menu`이며 직업·기원 선택과 로비는 `mode=Single`이다. `metrics-flow.png`는 별도의 Unity 데스크톱 관찰 이미지다.

| 실제 Play 화면 | 확인한 내용 | 증거 |
|---|---|---|
| 메인 홈 | 수정 장식, 방 만들기·들어가기 카드, 하단 메뉴가 실제 실행 화면에 표시됨. 전투 HUD가 홈 위를 가리지 않음. 한글 표시 확인 | [01-home.png](C:/Astella/prism-v4-remaining-ui-20261116/01-home.png), [JSON](C:/Astella/prism-v4-remaining-ui-20261116/01-home.json) |
| 설정의 화면 탭 | 화면·사운드·조작·카메라 탭과 화면 설정 패널, 하단 버튼의 실제 렌더 확인. 한글 표시 확인 | [02-settings-display.png](C:/Astella/prism-v4-remaining-ui-20261116/02-settings-display.png), [JSON](C:/Astella/prism-v4-remaining-ui-20261116/02-settings-display.json) |
| 설정의 조작 탭 | 실제 클릭으로 전환하고 이동 방식·키 바인딩 목록 표시 확인. 키 변경·저장 검증까지 포함하지 않음 | [03-settings-controls.png](C:/Astella/prism-v4-remaining-ui-20261116/03-settings-controls.png), [JSON](C:/Astella/prism-v4-remaining-ui-20261116/03-settings-controls.json) |
| HUD 배치 편집 | 설정에서 열기와 취소 동작 확인. 촬영 시 `layoutEditing=true`. 청록색 편집 영역은 편집 상태 표시이며 일반 전투 스킨이 아님 | [04-hud-layout-editor.png](C:/Astella/prism-v4-remaining-ui-20261116/04-hud-layout-editor.png), [JSON](C:/Astella/prism-v4-remaining-ui-20261116/04-hud-layout-editor.json) |
| 직업 선택 | 홈의 방 만들기 흐름으로 진입한 실제 12직업 카드와 한글 표시 확인 | [05-job-selection.png](C:/Astella/prism-v4-remaining-ui-20261116/05-job-selection.png), [JSON](C:/Astella/prism-v4-remaining-ui-20261116/05-job-selection.json) |
| 기원 선택 | 이지스 팔라딘 선택 후 실제 기원 3개 카드와 설명 표시 확인. 출발 이후 전투 검증으로 집계하지 않음 | [06-origin-selection.png](C:/Astella/prism-v4-remaining-ui-20261116/06-origin-selection.png), [JSON](C:/Astella/prism-v4-remaining-ui-20261116/06-origin-selection.json) |
| 확정 후 로비·최종 재질 | 직업·기원 확정 후 실제 로비 진입. 17:47:51 최신 촬영본에서 이지스 팔라딘 HUD와 새 적 목록·전투 기록 외곽 패널 표시 확인. `battlePhase=Lobby` | [07-lobby-hud.png](C:/Astella/prism-v4-remaining-ui-20261116/07-lobby-hud.png), [JSON](C:/Astella/prism-v4-remaining-ui-20261116/07-lobby-hud.json) |
| 전투 기록 펼침 | 17:42 데스크톱 관찰에서 실제 펼침 입력과 DPS/HPS 탭·4인 기록 목록 표시 확인. 외곽 패널 보정 전의 기능 증거이며 전투 수치 정확성 검증으로 집계하지 않음 | [metrics-flow.png](C:/Astella/prism-v4-remaining-ui-20261116/metrics-flow.png) |
| 방 만들기·최종 난이도 버튼 | 새 코드 컴파일 후 17:46:54 실제 화면에서 방 만들기와 `Stage 1 난이도: 노멀` 버튼의 페인팅 재질 표시 확인 | [09-create-difficulty-final.png](C:/Astella/prism-v4-remaining-ui-20261116/09-create-difficulty-final.png), [JSON](C:/Astella/prism-v4-remaining-ui-20261116/09-create-difficulty-final.json) |

조작 탭 증거는 17:41에 촬영한 최신 `03-settings-controls.png`와 JSON이다. 화면 탭과 다른 이미지임을 SHA256과 화면 내용으로 확인했다. 위 이미지는 명시한 표시·전환의 증거이며 모든 버튼의 작동과 설정 변경·저장·복원을 검증한 결과는 아니다. `08-combat-metrics-expanded.png`의 최신 파일은 실제로 접힌 상태이므로 펼침 증거에 포함하지 않는다. 최종 외곽 재질의 실제 Play 증거는 `07`, 난이도 버튼 재질은 `09`로 확인한다.

이번 적용본에서 실제로 검증하지 않은 범위는 다음과 같다.

- 보상·부활·전멸·결과 화면의 실제 게임 상태 전환과 해당 버튼 동작.
- 실제 다인 Steam 접속과 동기화.
- 전체 직업·기원·스킬 조합의 동작.
- 이번 변경을 포함한 Windows 재빌드와 별도 실행 파일 검증.

기존 1920 배치와 페인팅 리소스의 출처·게이지 수정 이력은 [1920 배치 및 페인팅 정정 문서](PRISM_V4_1920_LAYOUT_AND_PAINTED_FIX_2026-11-16.md)에 있다. 이번 결과 문서는 현재 적용본과 위에 명시한 증거를 정리하며 과거 전체 Play 검증 결과를 이번 보정본의 새 검증으로 재사용하지 않는다.
