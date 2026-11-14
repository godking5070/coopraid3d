# 2026-11-02 18:50 결과창 겹침·보스 텍스트 수정 완료

- `RaidConnectionUI.cs`: 결과창이 열린 동안 RunFlow 안내줄을 숨긴다. Update 이후 결과창이 열리는 경우도 LateUpdate에서 숨겨 기존 Raise가 결과창 위에 안내를 올리지 못하도록 했다. 결과창 종료 후 기존 상태 조건에 따라 자동 복구한다.
- `FloatingTextPopup.cs`: 생성된 월드 텍스트의 실제 글자 경계를 카메라 화면에 투영해 크기를 제한하고 전투 영역 안으로 위치를 보정한다. 카메라 뒤 팝업은 숨긴다. 숫자 행별 상단 경계를 달리해 화면 위에서 모두 한 줄에 모이지 않게 했다. 사용자 HUD·테마 배치는 변경하지 않았다.
- `BossController.cs`: 활성 phase/cast HUD가 있으면 동일 정보를 중복 표시하는 월드 시전 배너를 숨긴다. HUD 참조가 없거나 비활성이면 기존 배너를 유지한다. 피해 계산·시전 시간·패턴 변경 없음.
- 관리 검사: `Tests/Run-CombatMetricsChecks.ps1 -OriginalProject C:/coopraid/coopraid3d` 종료 0, 총 1,184개 검사와 runtime/editor 관리 컴파일 통과. 로그 `%TEMP%/Coopraid-UIOverlap-Final-20261102.log`. diff whitespace 검사 통과.
- 최종 Windows 빌드 `2026-11-02T09:50:21.1907678Z` (18:50:21 KST), protocol 17. 원본 Unity에서 빌드했으며 씬/프리팹 YAML을 직접 편집하지 않았다.
- 실기 1920×1080 AI 노멀 6층 직접 진입: 중간 빌드에서 결과창 안내 겹침 제거와 닫은 뒤 안내 복구 확인. 최종 빌드에서는 상단 중복 배너가 사라지고 피해 숫자가 HUD 아래 화면 안에 표시됨을 확인했으며 25.23초 클리어 후 결과창에도 안내 겹침 없음. 로그 `Logs/CodexOverlap-20261102.log`, `Logs/CodexOverlapFinal-20261102.log`에서 Exception/Assertion failed/NullReference 검색 결과 없음.
- 한계/다음: 동시에 생성되는 여러 버프 문구끼리의 겹침과 12개 초과 숫자 몰림은 남는다. 팝업 영역은 기본 HUD 기준이며 작은 해상도·울트라와이드·HUD 미사용 대체 배너·실제 멀티플레이는 미검증. 다음은 버프 안내 중복 억제/중요 알림 예산 보호와 해상도별 확인이다.
- 원본 확인 순서: AI 6층 노멀 관전 → HUD 시전바 유지/월드 중복 배너 숨김 및 숫자 위치 확인 → 클리어 결과창에서 승리 안내줄 없음 → 닫으면 안내줄 복구. 테스트 실행은 격리 저장 프로필 사용 후 소유 프로세스만 종료했고 Unity Play는 켜지 않았다. 자동 병합·푸시 없음.

# 2026-11-02 16:49 빌드 — 가독성 자산 적용과 화면 확인

사용자가 직접 조작을 명시적으로 재허용해 원본 Unity에서 적용·빌드·Windows AI 관전을 진행했다. 아래의 소스만 준비/자산 미적용 기록은 이전 상태다.

- 전용 Improve Combat Readability Only 메뉴 적용: 활성 CleanNavy의 RaidHUD.prefab, metrics.prefab, CleanNavyTheme.asset 저장. 결과 배경 갱신 6회(중복 참조 포함), compactDamageText=1. UI 참조 479개 검사 통과. 백업: `Logs/UIRepairBackups/readability-20261102-164855-818`. YAML 직접 편집 및 GUID 변경 없음.
- 최신 Windows 빌드: `2026-11-02T07:49:38.9269232Z` (16:49:38 KST), protocol 17. 이번 가독성 소스와 자산 포함. 실행 파일 `Builds/Part1Windows/RaidPart1.exe`.
- 1920×1080 AI 노멀 관전: 1층에서 330/300/170 등 숫자형 타격의 분산 표시를 직접 확인. 6층 직접 진입은 26.26초 클리어, 결과창의 진한 배경과 닫기→전체 런 결과 재열기 확인. 전체 1~6층 완주 또는 사람 조작 검증은 아니다.
- 발견한 잔여 문제: 승리 상태의 안내줄/메뉴 버튼이 결과창 위에 그려진다. 보스 상단의 월드 텍스트는 HUD와 겹치거나 화면 밖으로 잘린다. 6층 개발 진입 직후 카드/회복 안내도 한꺼번에 겹친다. 숫자 간소화만으로 모든 가독성이 해결된 것은 아니다.
- 다음 우선순위: (1) 결과창 위 승리 안내 겹침 수정 (2) 보스 팝업의 화면 내 위치와 HUD 겹침 보정 (3) 중요 기믹 문구 생성 예산 보호 (4) 해상도별 배치 검증 (5) 저장·이어하기/사망 귀환/정규 사람 플레이 검증.
- 로그 `Logs/CodexReadability-20261102.log`, `Logs/CodexReadabilityStage1-20261102.log`에서 Exception/Assertion failed/NullReference 검색 결과 없음. 격리 테스트 프로필만 사용했고 테스트 프로세스는 경로 확인 후 종료했다. 정상 종료 UI/저장 복구 검증으로 간주하지 않는다. 원본 Unity Play는 사용하지 않았다. 병합·푸시는 하지 않았다.

# 2026-11-02 전투 UI 가독성 개선 — 소스 준비, 자산 적용 대기

- `FloatingTextPopup`, `RaidUITheme`, `BossController`, `BossAddSpawner`: 테마의 `compactDamageText`를 켜면 일반 보스/쫄 타격을 숫자로 줄인다. 상세 타격 기록과 피해 계산은 유지한다. 숫자 팝업은 가까운 타격을 카메라 기준 12개 위치로 분산하고 표시 시간을 0.85초로 줄인다. 12개 초과 동시 타격에서는 겹침이 남을 수 있다. 카운터·기믹 문구는 기존 경로를 유지한다.
- `Editor/RaidUICleanDesign`: 원본 Unity의 `Tools > RaidRogue > UI > Improve Combat Readability Only` 명령을 준비했다. 실행 시 활성 CleanNavy HUD/metrics/테마와 meta를 `Logs/UIRepairBackups/readability-*`에 백업하고, 결과창 배경 불투명도와 숫자 표시 옵션만 편집한다. 광범위한 `Refine` 명령과 구분한다.
- 명령 기반 검사 로그: `%TEMP%/Coopraid-Readability-20261102.log`. 통계 1,042 / AI 피해 20 / AI 진행 101 / 아이콘 8 / 모달 13 및 runtime/editor 관리 컴파일 통과. 실제 Unity 렌더링과 멀티플레이 화면 검증은 아니다.
- 현재 테마에는 옵션이 아직 적용되지 않았다. 직접 조작 제한에 따라 후속 Unity 메뉴 실행·빌드·게임 실행은 진행하지 않았다. 기존 Windows 빌드(2026-11-02T02:16:10.3198140Z)에는 이번 가독성 수정이 포함되지 않는다. 자산 YAML 직접 수정·자동 병합·푸시는 하지 않았다.
- 추후 원본 Unity 확인 순서: 컴파일 완료 → 위 전용 메뉴 실행 → 결과 배경 변경 로그 및 참조 검사 확인 → AI 관전에서 일반 타격 숫자/기믹 문구 확인 → 결과창 열기·닫기와 배경 비침 확인. 작은 해상도와 12개 이상 동시 타격 겹침은 미검증이다.

# UI 디자인 방향 — Clean Navy

2026-11-02 소스 후속: 접힌 통계의 전체 기록 복사를 제거하고, 아이콘 최초 표시/참조 교체 및 상세 패널 재표시 문구를 보완했다. 관리 컴파일과 아이콘 stub 검사 8개 통과. 자산 디자인은 유지했으며 아래 11월 1일 실행 파일에는 이번 수정이 포함되지 않는다. 실제 렌더링·작은 해상도·성능은 추가 확인이 필요하다.

추천 방향은 짙은 남색, 얇은 금색 포인트, 청록색 상호작용이다. 큰 장식보다 정보의 우선순위와 여백으로 협동 레이드의 전장을 읽기 쉽게 만든다.

참고한 원칙:

- [FFXIV HUD 가이드](https://na.finalfantasyxiv.com/uiguide/know/know-hud/hud-layout.html): HUD의 크기·위치·표시 여부를 조정하는 접근. 파티, 보스, 개인 조작 정보를 서로 다른 구역에 모은다.
- [Guild Wars 2 UI](https://wiki.guildwars2.com/wiki/User_interface): 하단 스킬과 직업 자원을 가까이 묶고 스킬 상세는 툴팁으로 읽는 구조를 참고한다.
- [Lost Ark 접근성 안내](https://help.playlostark.com/hc/en-us/articles/37754234136475-Accessibility-Options): UI 요소와 알림의 가시성을 조절하는 접근을 참고한다.

게임별 이미지·아이콘·로고는 복제하지 않았다. 위 자료에서 정보 배치 원칙을 참고하고 이 프로젝트의 기존 uGUI 프리팹을 사용했다.

## 적용 범위

- 상단 중앙: 스테이지, 보스 체력, 시전바, 대상 정보. 위험/공격 예고와 체력·역할 색상은 구분한다.
- 좌측: 작은 4인 파티와 적 목록. 장문의 개발 조작 설명은 기본 HUD에서 숨긴다.
- 하단 중앙: 주력 스킬, 보조 스킬, 직업 자원. 긴 직업/능력치 설명은 별도 ‘직업 정보’ 버튼으로 펼친다.
- 우측: 미니맵과 기본 접힘 상태의 전투 통계. 펼치면 기존 상세 통계/결과 기능을 사용한다.
- 메뉴: 불투명 남색 배경과 일관된 버튼·글꼴. 전투 HUD가 메뉴 뒤로 비치지 않도록 한다.
- 전투 텍스트: 새 테마의 combatTextScale로 크기를 조정한다. 기존 테마 기본값은 1이며 새 테마는 0.62다. 피해량·판정·생성 제한·수명은 바꾸지 않는다.

## 편집과 복구

활성 디자인: `Assets/UI/CleanNavy-20261101-032249-979/`. HUD·뷰·반복 템플릿 18개와 테마를 기존 자산에서 복사해 Unity Editor API로 저장했다. 씬/프리팹 YAML은 직접 편집하지 않았다.

`Assets/Resources/UI/RaidUIPresentation.asset`이 새 사본을 가리킨다. 이전 프로필은 `Logs/UIRepairBackups/clean-design-20261101-032249-979/` 및 `.git/codex-backups/ui-design-20261101/`에 보관했다. 기존 UI 프리팹/테마/메타는 보존했다. 원래 HUD로 돌아가려면 원본 Unity에서 프로필의 HUD·views·templates·theme 참조를 백업 구성으로 복구한다.

현재 프리팹과 테마를 인스펙터에서 직접 편집할 수 있다. 런타임에서 배치나 기본 색상을 재설정하지 않는다. `Apply Clean Navy Design (Copy Assets)`는 명시적으로 실행할 때 새 사본을 만들고, `Refine Active Clean Navy Design`은 새 사본에 프리셋을 다시 적용하므로 수동 편집 이후에는 실행하지 않는다.

소스 변경: `RaidUIFoldout.cs`, `Editor/RaidUICleanDesign.cs`, `CombatMetricsUI.cs`, `RaidUITheme.cs`, `FloatingTextPopup.cs`, `PartyListUI.cs`. 새 사본의 역할 배지는 상태 색상에 명시적으로 동의하도록 설정하여 파티 순서가 바뀌어도 실제 역할/사망 상태를 따른다. 기존 수동 색상 편집본은 이 옵션을 켜지 않으면 그대로 유지된다.

## 확인

UI 참조 479개/버튼 action/아이콘 ID 검사 통과. 명령 기반 runtime·editor 관리 컴파일 및 기존 지표·관전 검사는 `%TEMP%/Coopraid-CleanNavy-Compile-20261101.log` 참고. Unity 기록은 `Logs/CodexUIDesign-20261101.log`다.

Windows 실기에서 메뉴, 노멀 AI 1층 전투→보상, 직업 정보 열기/닫기, 전투 통계 펼치기/접기, 설정 열기/취소를 확인했다. `Logs/CodexUICleanPlayer-20261101.log`에 기록했다. 실행 인자에서 1600×900을 요청했지만 저장된 사용자 화면 설정이 적용되어 실제 확인 화면은 1920×1080이었다. 작은 해상도와 울트라와이드는 미검증이다.

시각 검토에서 발견한 중복 적 목록은 실제 presenter에 연결된 목록만 남기고 새 사본에서 비활성화했다. 직업 정보 버튼은 부활 안내와 겹치지 않게 왼쪽으로 옮겼다. 기존 원본 프리팹은 수정하지 않았다.

최종 빌드: 2026-11-01 03:36:37 KST(UTC 2026-10-31T18:36:37.5484160Z), protocol 17, `Builds/Part1Windows/RaidPart1.exe`. 역할 배지의 마지막 상태 색상 보정도 포함한다. 이 마지막 색상 보정은 컴파일/자산 설정/빌드까지 확인했으며 별도 재플레이는 하지 않았다.

직전 보정 빌드에서는 일반 방 만들기→싱글→직업 선택→로비를 확인해 중복 적 목록 제거와 왼쪽으로 옮긴 직업 정보 버튼을 확인했다(`Logs/CodexUICleanFinal-20261101.log`). 해당 격리 검증 프로세스는 체크포인트가 없는 테스트 로비에서 종료했다. 원본 Unity는 Play 꺼짐, 씬 미저장 변경 없음 상태로 남겼다. 커밋/병합/푸시는 하지 않았다.

원본 Unity 또는 최신 Windows 빌드에서 메뉴→로비→전투→보상을 확인한다. ‘직업 정보’ 열기/닫기, 전투 통계 펼치기/접기, 설정 열기/취소, 파티·스킬 클릭과 작은 화면에서의 겹침을 확인한다. 실제 Steam 동기화와 하드 전 패턴 판정은 이 UI 작업의 검증 범위가 아니다.
