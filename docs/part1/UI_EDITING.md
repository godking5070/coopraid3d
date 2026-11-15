# UI 편집 경로와 현재 확인 범위

원본 프로젝트: `C:/coopraid/coopraid3d`. worktree를 Unity로 열지 않는다.

## 지금 열 파일

- 전체 HUD: `Assets/UI/Generated/ShortcutRepair-20261025-002953-073/RaidHUD.prefab`
- 중첩 패널/공통 템플릿 폴더: `Assets/UI/Generated/20261025-001328-635`
- 메인 메뉴/방 만들기/들어가기: 위 중첩 패널 폴더의 `connection.prefab`
- 직업/자원: `character.prefab`, 기원 선택: `origin.prefab`, 확장 선택: `skill-reward.prefab`
- 설정: `settings.prefab`, 부활/관전: `resurrection.prefab`, 전멸: `wipe.prefab`, 미터: `metrics.prefab`
- 포션/대시: `potion.prefab`, `dash.prefab`
- 활성 설정: `Assets/Resources/UI/RaidUIPresentation.asset`

## RaidHUD를 한눈에 찾기

- RaidHUD 루트의 `RaidUIRoot` 인스펙터는 **전투 기본 HUD / 대상·파티·적 / 메뉴·선택·결과 / 그 밖의 전투 표시** 목록을 제공한다. 항목 버튼을 누르면 해당 오브젝트가 선택된다. 게임 연결용 Presenter 참조는 `고급`을 펼칠 때만 보인다.
- Play를 끈 원본 Unity에서 `Tools > RaidRogue > UI > Organize Active RaidHUD (Copy)`를 한 번 실행하면 Canvas 직속의 수동 제작 전투 기본 HUD 8개를 접을 수 있는 `01 - Combat HUD` 아래로 묶는다. 원본 프리팹은 남기고 `Assets/UI/Generated/Organized-<시각>/RaidHUD.prefab`을 새로 저장·검증한 뒤 활성 프로필만 새 복사본으로 바꾼다. 보상·설정·메뉴 등 화면 순서가 바뀌면 안 되는 중첩 패널은 옮기지 않는다. 실행 후 인스펙터의 `활성 프리팹 참조 검사`와 Play 재진입을 확인한다.
- 프리팹에는 전투 중 숨겨진 메인 메뉴/방 만들기 UI 오브젝트도 포함된다. 이들은 화면과 입력 연결용이다. 실제 방 구성, 파티, HP, 스테이지와 저장 데이터는 각각 게임 시스템에서 관리한다. 메뉴로 돌아갈 때 같은 UI를 다시 사용하며, 전투 중 숨겨진 시작 메뉴의 화면 문구 갱신은 생략한다.

## 레이드 중 캐릭터·보스 대사 자리

- 현재 `Center_MechanicAlert_Text`는 보스 기믹 경고용이며, 대사 UI는 아직 구현되지 않았다. 향후 대사는 경고 문구나 메인 메뉴 텍스트를 재사용하지 않고 별도 패널로 표시한다. 경고·카운터·보상 선택의 가독성과 클릭 영역을 침범하지 않도록 위치를 정한다.
- 편집할 요소는 화자 이름, 대사 본문, 초상화(선택), 배경과 자막 영역이다. 이들은 uGUI 프리팹에서 배치·이미지·폰트를 수정할 수 있게 하고, 표시할 대사 ID·화자·순서·지속 시간은 별도 게임 데이터/진행 이벤트가 관리한다. RaidHUD는 현재 대사만 표시하며 전체 대사 기록이나 보스 진행 상태를 보관하지 않는다.
- 반복 재생, 스테이지 전환, 전멸·재도전, 메뉴 복귀에서는 진행 중인 자막/음성을 정리해야 한다. 멀티플레이에서는 호스트가 확정한 전투 이벤트를 기준으로 대사를 보여 주되, 각 플레이어의 UI 표시와 음량 설정은 로컬에서 처리한다. 구체적인 대사 내용·트리거·음성 리소스가 정해지기 전에는 빈 UI나 임의 대사를 정규 전투에 추가하지 않는다.

이 경로에는 실제 생성된 프리팹이 있다. `Assets/UI/Editable`은 이전 실패 중 생성된 일부 파일이므로 현재 활성 HUD가 아니다. 이전 전체 HUD는 LocalShortcutText 스크립트 누락 10개를 포함하므로 보존용이며, 활성 프로필은 위 복구본을 사용한다. 생성 메뉴를 다시 실행할 필요 없이 Play를 끄고 위 프리팹을 편집할 수 있다. Create Editable Prefabs는 활성 HUD가 있으면 검사 후 해당 에셋을 선택하며 재생성하거나 덮어쓰지 않는다.

이전 결함이 있는 HUD를 복구해야 한다면 Play를 끈 뒤 `Tools/RaidRogue/UI/Repair Legacy Shortcut Scripts`를 사용한다. 확인된 LocalShortcutText 누락만 Unity API로 새 복구본에 교체하며 기존 HUD/디자인은 보존한다. 알 수 없는 누락 스크립트는 자동 제거하지 않는다. 활성 프로필의 변경 전 백업은 `Logs/UIRepairBackups/<생성시각>`에 남는다. 현재 프로젝트는 이미 복구했으므로 다시 실행할 필요가 없다.

## 편집 방법

- 전체 배치는 RaidHUD에서 중첩 패널의 RectTransform 위치/크기/anchor/pivot으로 조절한다. 패널 내부 배치는 해당 중첩 프리팹에서 편집한다.
- `Organize Active RaidHUD (Copy)`는 선택 도구다. 현재 활성 HUD를 편집할 때 실행할 필요가 없다. `connection` 패널의 내부 배치는 `connection.prefab`에서, HUD 안에서 패널 전체의 위치·크기는 활성 RaidHUD의 중첩 인스턴스에서 조절한다.
- 파티 목록과 적 목록의 편집된 패널 위치·크기, 파티원 이름의 높이·글꼴 맞춤은 코드가 다시 지정하지 않는다. 적 목록은 최대 5행이 표시될 수 있으므로 패널 배경 크기와 행 배치를 함께 확인한다. 코드가 만든 기본 UI에는 기존 자동 크기·위치를 유지한다.
- Horizontal/Vertical Layout Group 부모 아래 요소는 부모의 padding/spacing/정렬과 자식 Layout Element를 수정한다. 설정 폼과 반복 목록은 이 방식을 사용한다.
- Image의 sprite/color, Text의 font/fontSize/color는 개별 요소에서 변경한다. RaidUIStyle.useTheme은 기본 꺼짐이며 개별 값을 보존한다. 켜면 Awake에서 해당 테마의 값이 적용된다. themeOverride가 없으면 활성 Presentation의 theme을 사용한다. 상태별 색은 useStateColors와 ready/unavailable/selected/locked를 사용한다.
- 스킬/직업/포션/상태 아이콘은 Presentation의 Icons에서 고유 ID로 연결한다. RaidUIIcon.overrideSprite는 해당 요소에만 우선 적용된다. 빈 아이콘은 숨겨지고 텍스트 표시를 유지한다.
- 공통 템플릿: SkillSlot(확장 슬롯), BaseSkillSlot(기본 슬롯), PartyMember(파티 행), ResourceBar, StatusEffect, FormButton/FormText. 중첩 프리팹 또는 Presentation에 지정된 템플릿을 수정한다. 인스턴스 override가 있으면 그 값이 우선한다.
- UI 오브젝트 이름과 배치는 변경할 수 있지만 RaidUIView의 binding path/action ID 및 실제 참조, RaidUIRoot의 presenter 연결은 유지한다. 선택적인 아이콘/장식은 비어 있어도 동작한다. 필수 버튼/표시 참조는 삭제하지 않는다.
- 게임 코드가 관리하는 값은 실제 HP/자원/쿨타임의 fill, 수치·단축키 텍스트, 표시 여부, 버튼 사용 가능 여부다. 이 값은 플레이 중 갱신된다. 접힌 패널 높이 등은 RaidUILayout의 동작에 따라 바뀐다.

## 검사 및 남은 확인

- `Tools/RaidRogue/UI/Validate Active Presentation`: 활성 프리팹 필수 참조 검사.
- `Tools/RaidRogue/UI/Run Binding Checks`: 15개 참조/배치/상태색 검사.
- `Tools/RaidRogue/UI/Run Prefab Safety Checks`: 215개 프리팹 교체/소유권/스크립트 및 단축키 템플릿 저장·재로드 검사와 위 15개 검사. 도구가 만든 임시 검사 폴더만 정리한다.
- `Tools/RaidRogue/UI/Validate Live Presentation (Play Mode)`: Play 진입 후 HUD 단일 생성, 실제 presenter와 주요 버튼 연결, 누락 스크립트, 메인 메뉴 설정 버튼의 클릭 차단 여부 검사.
- 원본 Unity 배치 모드에서 전체 프리팹 생성 → Play 종료 → 재진입을 실행했다. 479개 참조, HUD 1개, 주요 presenter/버튼 재연결 확인. 로그: `Logs/UI-PrefabSafety-2.log`, `Logs/UI-GenerationSmoke.log`.
- 이전 생성 검사에는 LocalShortcutText의 저장된 MonoScript 누락을 찾지 못한 검증 공백이 있었다. 2026-10-25 복구 및 실제 원본 GUI 재진입에서 해당 누락 해소, 메뉴 표시·방 만들기 클릭, Create 재실행 시 기존 에셋 유지 확인. 아티스트 편집 후 스테이지 전환 및 멀티플레이는 아직 미확인이다.
- 훈련장처럼 메인 메뉴 생성 시 존재하지 않는 일부 패널은 기존 런타임 생성 경로를 유지한다. 모든 화면의 편집 가능 전환이 완료되었다고 보고하지 않는다.

씬/프리팹 YAML을 텍스트로 수정하지 않는다. 에디터에서 편집하고 기존 meta/GUID를 보존한다.

## 미니맵 편집

- 원본 Unity가 변경된 스크립트를 불러오면 Play와 프리팹 편집이 끝난 안전한 시점에 현재 활성 `RaidHUD.prefab`의 누락된 `Minimap`을 자동으로 한 번 추가한다. 기존 프리팹 GUID와 다른 UI는 유지하며, 저장 전 백업은 `Logs/UIRepairBackups/minimap-*`에 남는다. 사용자가 생성 명령이나 프리팹을 열 필요는 없다.
- 활성 프리팹 `Assets/UI/Generated/ShortcutRepair-20261025-002953-073/RaidHUD.prefab`의 `Minimap` RectTransform에서 우상단 위치와 크기를 조절한다. `Arena (north up)`의 크기를 바꾸면 표식 배치 범위도 같이 변한다. 배경과 각 `Image`의 Sprite/Color, `North`·`Stage`의 Text 폰트/크기/색은 인스펙터에서 교체한다. `Party marker template`와 `Add marker template`은 비활성 상태를 유지하고 스타일만 편집한다.
- RaidHUD 루트의 `RaidMinimapUI` 참조는 유지한다. 숫자·좌표·표식 표시만 코드가 갱신하며, 배경/폰트/앵커/크기는 덮어쓰지 않는다. 미니맵은 북쪽(+Z) 고정으로, 우클릭 카메라 회전과 독립적으로 내 방향·파티·보스·쫄·선택 대상을 표시한다. 미니맵 그래픽은 클릭을 받지 않아 전투 입력을 가로채지 않는다.
- 편집용 프리팹 생성 전에도 Play에서는 기본 미니맵이 표시된다. 이 런타임 생성본의 수정은 저장되지 않는다. 자동 추가가 실패했을 때만 `Tools > RaidRogue > UI > Add Minimap to Active RaidHUD`로 재시도할 수 있다. 디자인을 직접 바꿀 때만 Play를 끄고 활성 프리팹을 연다.

- 2026-10-25 미니맵 후속: 우상단 시점 전환(-14~78 px)·직업 버튼(-80~128 px)과 겹치지 않도록 기본 미니맵 시작을 상단에서 154 px 아래로 배치했다. RaidHUD 루트 `RaidMinimapUI`의 Party/Boss/Add/Dead/Selected 색상은 인스펙터에서 편집한다. 정규 로비·메뉴·최종 결과에서는 미니맵을 숨기고 전투/훈련 표시만 유지한다. 훈련장의 다수 표적도 표시한다. 활성 프리팹의 위치는 에디터 생성 이후 자유롭게 변경할 수 있다.
Lobby editing (2026-10-25): Open Assets/UI/Generated/ShortcutRepair-20261025-002953-073/RaidHUD.prefab in the original Unity editor. The Lobby Prompts child contains Interaction Hint and NPC Dialogue; edit their RectTransform, images, font, color and size there. Its root stretches to the Canvas, so child anchors remain relative to the screen. The existing Minimap child is separately editable. Open Assets/Resources/UI/RaidLobbyLayout.asset for north-door (+Z), guide/Iris and party return coordinates, ranges, display names, and optional NPC/door prefabs. Runtime changes only prompt text/visibility and reads this layout; missing asset/prefab uses fallback primitives. The NPC training action reuses the single-player training session; an active multiplayer party must enter training from the main menu.
