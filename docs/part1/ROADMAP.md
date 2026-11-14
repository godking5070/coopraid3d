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
| 8 | 개발 테스트 오류 보고 ZIP: 분리 저장·로그·익명화·수집 결과 | 코드/fixture/Development 빌드/분리 프로필 재실행 준비 완료, 실제 버튼·ZIP 화면 검증 필요 |

기존 저장 및 부활 기능을 재구현하지 않는다. 각 변경에는 관련 검사만 수행하고, 실제 Unity/Steam 실행과 자동 검사를 구분한다.

- RaidHUD 정리(2026-10-25): 편집용 인스펙터 탐색과 원본 보존형 기본 HUD 그룹화 명령 구현·관리 코드 컴파일 통과. 타겟 HUD 런타임 재생성 결함 수정, 숨겨진 메뉴 문구 갱신 축소. 원본 Unity에서 명령 실행·저장된 프리팹 참조 및 실제 화면 확인 필요.

- UI 편집 후속(2026-10-25 재개): 활성 HUD의 10개 중첩 패널 분리 확인. 설정 창 초기화/표시 순서 보완 및 포인터 경로 검사 코드 컴파일 통과. 원본 Unity에서 강화된 포인터 검사, 실제 클릭·단축키, 아티스트 편집 후 재실행은 미확인.

- UI 생성 후속(2026-10-25): 빈 Unity 렌더러 참조 접근 및 실패 시 임시 루트 잔류 수정, 컴파일 통과·원본 반영 완료. Unity binding 검사 15개 실행과 실제 프리팹 생성/재실행은 미완료.
- 위 미확인 항목의 후속 완료: 네이티브 계층 손상 원인 수정 후 원본 Unity binding 15개 / prefab safety 191개 통과. 18개 실제 프리팹 생성 및 Play 재시작/479개 참조 확인 완료. 실제 화면/클릭/전투/스테이지 전환은 여전히 확인 필요. 편집 경로는 UI_EDITING.md.

## 알려진 외부 조건

- 추가 요청 진행 중: 메인 메뉴의 방 만들기/들어가기/훈련소 배치, 계정별 첫 정규 전투 후 훈련소 해금, 저장 관리 하위 화면. 새 메뉴의 실제 실행 확인은 아직 미완료.

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
# UI 편집 구조 요청 (2026-10-24)

- [x] 기존 uGUI/자동 생성/사용자 변경 확인 및 기존 worktree 재사용
- [x] 프리팹 우선 연결, 안정적 참조·액션, 테마/아이콘/반복 템플릿 및 에디터 도구 구현
- [x] 런타임·에디터 C# 컴파일과 원본 소스 충돌 확인/백업
- [ ] 원본 Unity 프리팹 생성, 참조 검사, 편집 후 재실행 확인
- [ ] 실제 편집 경로·사용법·남은 검증 기록

## 2026-10-25 UI 오류 수정 상태
- [x] 실제 프리팹 생성·누락 스크립트 복구·참조 검사·Play 재진입
- [x] 실제 에셋 경로와 편집 방법 기록(UI_EDITING.md)
- [ ] 편집 후 스테이지 전환 및 설정 직접 입력의 표시 확인 — 사용자 요청으로 추가 검사 중단

## 미니맵 후속 (2026-10-25)
- [x] 로컬 상태 표시 코드, 훈련장 다수 표적, 원격 파티 생존 판정, 기존 우상단 버튼과 기본 배치 충돌 수정
- [x] 원본 코드 반영 및 사전 백업, 프리팹 인스펙터 편집 경로 기록
- [ ] 원본 Unity에서 활성 RaidHUD에 Minimap 영구 추가, Play 모드에서 메뉴 숨김과 대기실·전투·훈련 표시 확인
- [ ] 실제 4인 세션에서 표식 및 카메라 회전 독립성 확인
## 걸어 다니는 대기실 (2026-10-25)

- [x] 기존 싱글/Steam/LAN 로비에 런타임 3D 대기실·NPC·북쪽 문 연결
- [x] 직업 선택 후 싱글 자동 출발 제거, 문에서 출발; 멀티 호스트 문 위치·준비 검증
- [x] 대기실 WASD/카메라와 G 상호작용, 로비 대시·전투·포션 차단, 미니맵 대기실 표시
- [x] 원본 Unity 프로젝트에서 관리 코드 컴파일·Windows 빌드
- [ ] 실제 싱글 플레이로 NPC/문/전투/메뉴 복귀 및 프리팹 미니맵 표시 확인
- [ ] LAN/Steam 호스트·클라이언트 4명 이동 동기화와 북쪽 문 출발 확인
- 범위 밖: Stage 4~6 고유 보스 콘텐츠. 기존 빌드와 마찬가지로 1~3 전투를 재사용한다.
- [x] Walkable 3D waiting room with existing lobby movement, host north-door ready gate, and NPC interactions (code/build).
- [x] Single checkpoint continuation waits at the room door; Stage 6 result can return the same party to the room; completed run remains saved.
- [x] Editable room landmark layout asset and RaidHUD minimap/lobby prompts baked using original Unity editor tools with backups.
- [ ] Unity Play verification: room movement/camera/NPC, blocked/unblocked door, saved-stage departure, Stage 6 return and second departure, visible HUD/minimap.
- [ ] LAN host/client and four distinct Steam accounts: pose sync, host authority, return/redeparture and reconnect.

## 단독 테스트 우선 (2026-10-25)

- [x] 기존 싱글 + AI 3명 진입과 Stage 1~6/저장/부활 코드 경로 및 이전 테스트 기록 대조
- [x] 개발용 명시 저장 프로필 추가·계정별 진행 분리 검사; 평소 저장 경로 유지
- [x] 저장 원자성·부활·포션·진행 등 관리 코드 검사와 컴파일
- [ ] 새 Windows Development 빌드: 원본 Unity 에디터가 열려 있어 배치 안전 검사에서 중단. 기존 빌드에 이번 저장 프로필 변경 없음.
- [ ] 실제 단독 A~F 플레이: 메뉴/대기실/문/Stage 1~6/체크포인트/결과. `SOLO_TEST.md` 순서와 기대값 참고.
- [ ] 실제 4인 Steam: 이번 단계에서는 연기. 동일 문서의 별도 체크리스트로 유지.

## Raid approach and boss arrival (2026-10-25)

- [x] Walkable corridor and wide field after the north door, with editable layout values and runtime geometry.
- [x] Supersede all-party rally start with host-validated real-player field entry, one-time party placement, boss reveal, shared cutscene skip and countdown.
- [x] Reuse Stage 1-6, checkpoint/continue and retry; retry spawns near the field and combat closes the entrance.
- [x] Keep boss hidden and non-interactive before reveal; preserve personal camera settings and combat state during placement.
- [x] Managed flow/save/network tests and runtime/editor C# compilation; copy reviewed C# to original with backup.
- [ ] Original Unity import and Play: corridor, player-only trigger, 1+3 placement, reveal, skip, camera restore, gate, retry, checkpoint and transitions.
- [ ] Fresh Windows build from the original Unity editor; the previous executable predates this change.
- [ ] Four real Steam users, disconnect/reconnect during cutscene, and Stage 1-6 Play remain unverified.

## Boss defeat settlement and lobby return (2026-10-25)

- [x] Host-confirmed boss kill ends combat safely, retains the model for one shared defeat cutscene, then clears boss targeting and collision.
- [x] Reward and stage skill selection begin after the cutscene; the central portal opens only after required choices and a safe checkpoint or completed-run save.
- [x] A post-clear settlement journal preserves the confirmed kill, offers, selections and party build across a crash before the next boundary save.
- [x] Solo and network portal return keep the party and next-stage build; the north door starts the next stage approach. Stage 6 ends at results and lobby, never Stage 7.
- [x] Managed campaign/network checks and original Unity editor script compilation.
- [ ] Original Unity Play: defeat camera, skip, residual damage, reward ordering, portal, journal resume, Stage 6, retry and reconnect.
- [ ] Fresh Windows build and four-account Steam synchronization remain unverified.

## Stage 1 reward stall follow-up (2026-10-25)

- [x] Inspect actual Editor.log and pending-clear journal without modifying player saves.
- [x] Prevent an older failed checkpoint save from blocking a durable post-clear settlement; allow settlement-only Continue.
- [x] Rebind card buttons after editable HUD restoration and prevent overlapping skill picker from rejecting card input.
- [x] Surface checkpoint save failure cause, pass managed save/network checks and runtime/editor managed compilation.
- [ ] Original Unity Play: Continue the existing pending offer, choose a card, open portal, return to lobby, start Stage 2.
- [ ] Determine why the first checkpoint file was absent using the new [Campaign checkpoint] warning on a fresh run.

- 2026-10-25 보스 등장 후속: 진입로의 공통 입력 차단과 숨긴 보스의 적 목록/타게팅 노출 수정 완료(관리 검사 통과). 원본 Unity에서 최종 소스 재컴파일, 북쪽 문→보행→소환→등장→전투 Play 및 새 Windows 빌드 확인은 미완료.

## Save/restart audit (2026-10-25)

- [x] Map checkpoint, pending-clear settlement, reward and completed-run restore states; correct settlement-only Continue crash.
- [x] Reject invalid/duplicate pending reward records and test atomic disk read/write, backup and Stage 1-6 restore transitions.
- [x] Sync reviewed source to original and build a fresh protocol-16 Windows Development player.
- [ ] Execute isolated-profile S1-S6 actual quit/crash/relaunch tests in SOLO_TEST.md; desktop capture was blocked by another fullscreen app.
- [ ] Run real four-account Steam save/rejoin validation later.
