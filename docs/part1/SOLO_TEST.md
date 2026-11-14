# 1부 단독 플레이 검증 — 2026-10-25

기준: `feature/part1-playable-build` / `5d4e1c6`. 기존 Windows 빌드의 영수증은 2026-10-25 02:12:57 UTC이며, 이번 개발용 저장 프로필 변경은 **새 Windows 빌드가 성공하기 전까지 그 실행 파일에 포함되지 않는다**. 4인 Steam 검증은 별도 후속 작업이다.

## 실행 준비와 저장 분리

원본 프로젝트 `C:/coopraid/coopraid3d`의 Unity 에디터가 열린 동안 배치 빌드를 동시에 시작하지 않는다. 에디터의 컴파일이 끝난 뒤 `Raid Rogue > Build Part 1 Playable (Windows)`를 실행하거나, 에디터를 닫고 다음 명령을 실행한다. 빌드 성공 시 `BUILD_INFO.txt`의 `Built UTC`가 새 값인지 확인한다.

```powershell
& C:/coopraid/coopraid3d-playable/Tests/Build-Part1Windows.ps1 -OriginalProject C:/coopraid/coopraid3d -CheckOnly
& C:/coopraid/coopraid3d-playable/Tests/Build-Part1Windows.ps1 -OriginalProject C:/coopraid/coopraid3d
```

새 **Development** 빌드에서 아래 명령으로 실행하면 개인 진행도와 체크포인트만 별도 테스트 프로필에 저장한다. `solo-check`는 필요한 테스트 이름으로 바꿀 수 있다. 일반 게임 실행에서는 기존 저장 위치를 그대로 쓴다. 설정과 키 배치는 기존 사용자 설정을 읽으며 삭제하지 않는다. Steam App ID는 현재 미설정이므로 오프라인/AI로 진행한다.

```powershell
$game='C:/coopraid/coopraid3d/Builds/Part1Windows'
& "$game/RaidPart1.exe" --raid-test-profile=solo-check -logFile "$game/SoloTest.log"
```

테스트 진행 저장은 `%USERPROFILE%/AppData/LocalLow/DefaultCompany/coopraid3d/test-profiles/solo-check/campaign/offline-local/`의 `checkpoint.json`과 `progress.json`이다. 로그는 `Builds/Part1Windows/SoloTest.log`, 빌드 로그는 같은 폴더의 `UnityBuild.log`다. 로그를 공유할 때는 계정 식별 정보가 포함될 수 있으므로 확인한다. 기존 일반 저장을 삭제하거나 옮길 필요가 없다.

## 결과 구분

| 흐름 | 코드/자동 검사 | 실제 단독 플레이 | 수동으로 볼 핵심 결과 |
|---|---|---|---|
| A 메뉴·대기실·훈련장·HUD/미니맵 | 관리 코드 컴파일 통과, 연결 코드 검토 | 미검증 | 메뉴→싱글, WASD·우클릭, G 길잡이/이리스, 훈련장 왕복, UI 클릭 뒤 월드 타겟 불변, 사용자 편집 HUD 유지 |
| B 문 출발·AI 3명 | AI 역할 보충과 문 조건 코드 검토 | 미검증 | 직업/기원 미선택 때 차단, 선택 뒤 +Z 북쪽 문 G→Stage 1, 빠른 두 번 입력에도 중복 없음, 1탱·1힐·2딜 |
| C 전투·포션·사망·부활 | 포션 505개·파티 생사 179개 검사 통과 | 미검증 | X는 최대 HP의 50% 회복/쿨타임, 사망 시 행동 차단·관전, 이리스 공용 4→0, 재도전/다음 Stage 4/4 |
| D Stage 1~3 보상 | 흐름 630개·저장 31,913개 검사 통과 | 미검증 | Stage 1 R·카드, Stage 2 F·카드, Stage 3 V·카드, 선택 전 출발 불가/중복 없음 |
| E Stage 4 체크포인트 이어하기 | 경계 저장/복원 계산과 원자적 파일 검사 통과 | 미검증 | Stage 4 전투 중 종료 후 재실행→Stage 4 시작, R/F/V·카드 유지, 부활 4/4, AI 중복 없음 |
| F Stage 4~6·결과·복귀 | Stage 7 방지와 완료/복귀 코드 검토 | 미검증 | Stage 6 완료 결과 1회, Stage 7 없음, 대기실 복귀 후 재출발, 완료 런 이어하기 숨김 |

Stage 4~6은 현재 Stage 1~3 보스 전투를 재사용한다. 진행 흐름 통과와 6개 고유 보스 콘텐츠 완성은 별개다. 강제 처치·스테이지 건너뛰기를 사용했다면 정상 전투 클리어로 기록하지 않는다.

## 한 사람 실행 순서

1. 새 Development 빌드를 위 테스트 프로필로 시작한다. 메뉴에서 싱글플레이를 선택하고 탱커 직업과 기원 루트를 선택한다. 대기실에서 이동·카메라·G NPC·미니맵을 본다. 훈련소는 첫 정규 전투 전 잠겨 있어야 한다.
2. 준비를 끝내지 않은 채 북쪽(+Z) 문에서 G를 눌러 차단 안내를 확인한다. 준비 후 G로 Stage 1에 진입해 AI 3명의 역할과 HUD의 `이번 도전 부활 4 / 4`를 확인한다. UI를 클릭해도 타겟·공격이 바뀌지 않아야 한다.
3. 전투에서 X 포션과 스킬을 사용한다. 사망·관전·이리스 부활과 전멸 재도전을 별도 시도에서 확인한다. 부활 카운트 4→3→2→1→0 및 재도전 4/4를 기록한다. AI 부활이 있으면 같은 수치를 쓴다.
4. 정상 전투로 Stage 1~3을 클리어하고 R/F/V·카드를 선택한다. Stage 4 시작 전에 저장 정보에 `Stage 4 시작`과 현재 직업/루트가 보이는지 확인한다.
5. Stage 4에서 HP·자원을 소모한 뒤 메뉴 복귀를 확인하고 종료한다. **같은 테스트 프로필**로 다시 실행해 이어하기를 선택한다. 대기실 북쪽 문으로 Stage 4를 시작해 이전 선택은 유지되고 HP·자원은 경계 상태, 부활은 4/4인지 확인한다.
6. Stage 4~6을 정상 완료한다. 결과에서 대기실 복귀 후 새로 출발하고, 종료·재실행 시 완료 런이 이어하기 대상으로 남지 않는지 본다. 훈련장 왕복과 미니맵 재표시도 확인한다.

실패를 발견하면 `BUILD_INFO.txt` 시각, Stage/직업/루트, 재현 입력, 기대·실제 화면, `SoloTest.log`의 마지막 오류 부분을 기록한다. 코드를 고친 뒤에는 실패 단계와 직접 영향을 받는 흐름만 재검사한다.

## 이후 4인 Steam 체크리스트 — 이번 결과에 포함하지 않음

서로 다른 Steam 계정 4개의 참가/초대, 클라이언트 3명의 이동·전투·회복 동기화, 각자 선택/준비 동기화, 동시 부활, 접속 해제→AI→동일 계정 복귀, 호스트 종료, 4인 Stage 전환/저장 재합류/Stage 6 완료, 지연·연결 불안정을 확인한다. 현재 `BUILD_INFO.txt`의 Steam configured는 `False`다.

## Boss defeat, settlement, and central portal (Unity Play pending)

Use the original Unity project and a separate solo test profile. These are manual checks, not passes from the managed test suite.

1. Enter Stage 1 with one player and three AI. Kill the boss once. Confirm boss attacks, queued damage, and combat timer stop; the defeat camera plays once and restores the player camera. Check a dead player stays dead and Iris shared coins do not change.
2. During the cutscene, use the existing Interact skip input. Confirm one human can skip in solo, no reward/portal appears early, and camera/input restore. Repeat without skip and with missing death animation resources.
3. Finish the card and R choices. Confirm one central portal appears only afterward. Use G at the portal; confirm the same party returns to the lobby with the build intact. Use the north door and confirm Stage 2 approach, not a new Stage 1 run.
4. Stop Play or quit just after kill, during cutscene, and during reward choice in separate test runs. Relaunch Continue and enter through the north door. Confirm the pending reward resumes without a second boss kill or duplicate card. Finish choices, confirm the next-stage checkpoint, then repeat Continue to ensure no replay.
5. Clear Stage 6. Confirm result once, no Stage 7, then use the portal to return to lobby. Relaunch and confirm the completed run is absent from Continue.
6. Retry a wiped stage and repeat its kill; verify a fresh attempt, one defeat cutscene, and shared Iris 4/4 at retry start. A live-session reconnect must join the current phase without replaying the cutscene; this requires a separate Steam test.

Record build version, Stage, exact steps, Console/Player.log excerpt, expected and actual result for each failure. Current status: managed C# and save/network checks passed; all Play checks above are unverified. Four-account Steam sync and Stage 4-6 unique boss content are later work.

## Regression: card choice appears to freeze Stage 1 (2026-10-25)

A valid offline-local settlement.json from the reported run contains the confirmed Stage 1 clear and three offered cards, but no selected card. The original checkpoint.json was absent. These files were inspected read-only and must not be deleted for this test.

1. Stop Play, wait for the original Unity project's script import, and confirm Console has no compile errors.
2. From the menu choose Continue. The valid pending-clear journal must make Continue available even when there is no checkpoint.json. At the lobby north door, enter the pending reward without fighting the Stage 1 boss again.
3. Click one card once. The card window should close and the selected card should appear in the build. Confirm the central return portal opens, then use G to return to the lobby and the north door to start Stage 2.
4. Repeat with a fresh solo run to check that a Stage 1 start checkpoint is created. If it is not, record the first Console warning prefixed [Campaign checkpoint] and the save warning shown on the HUD. Do not delete the existing journal to make the test pass.
5. A click on the editable HUD's card button and its Choice 1/2/3 shortcut should produce the same one-time result. A skill picker must not cover the card picker.

Managed checks passed after the fix. Unity Play for these steps remains unverified; a real four-account Steam session is separate.
Dev shortcut: in Editor/Development Build solo combat, click the upper-right TEST boss-clear button. This marks development use; use a normal kill or the existing pending settlement Continue flow to validate regular checkpoint persistence.

## 저장·이어하기 중단 검증 (빌드 2026-10-25T07:36:14Z, protocol 16)

각 행은 **서로 다른** `--raid-test-profile=save-audit-01` 등의 이름으로 새 런에서 시작한다. 위 개발용 보스 클리어 버튼과 `Shift+F1`은 저장을 제외하므로 이 검사에 사용하지 않는다. 종료 전후의 `checkpoint.json`, `settlement.json`, `progress.json` 및 `.bak`를 읽거나 별도 위치에 복사해 비교한다. 일반 `campaign/offline-local` 폴더는 변경하거나 삭제하지 않는다. 강제 종료는 해당 테스트 빌드의 PID와 실행 파일 경로를 확인한 뒤 그 프로세스에만 적용한다.

| ID | 종료 지점 | 재실행 후 기대 결과 | 현재 결과 |
|---|---|---|---|
| S1 | Stage 1 R·카드 확정, 포탈로 대기실 복귀 | 이어하기 대기실, 북쪽 문은 Stage 2, R·카드 유지, 보상 반복 없음 | 실제 미검증 |
| S2 | Stage 2+ 전투 중 HP/자원/포션/부활 소모 | 마지막 시작 체크포인트, 같은 Stage 새 도전, 공용 부활 4/4 | 실제 미검증 |
| S3a | 보스 처치 확정 직후 | 저장된 정산이 있으면 재처치 없이 미완료 선택으로 복구 | 실제 미검증 |
| S3b | 처치 컷씬 도중 또는 끝 직후 | S3a와 동일, 카메라·입력 잠금 없음 | 실제 미검증 |
| S4 | R/F/V 또는 카드 후보 표시·선택 직후·일부 선택 후 | 후보 유지, 저장 확정된 선택만 유지, 카드 효과·중첩 1회 | 실제 미검증 |
| S5 | 포탈 활성·상호작용·북쪽 문 로딩 중 | 확정된 다음 Stage의 대기실 또는 안전한 시작 지점, 중복 보상 없음 | 실제 미검증 |
| S6 | Stage 6 컷씬·최종 정산 전후·대기실 복귀 | 미정산이면 최종 선택 계속, 완료 저장 후 이어하기 없음·Stage 7 없음 | 실제 미검증 |

오류 자료 묶음은 docs/part1/BUG_REPORT.md 절차를 따른다. S1~S6 중 실패 시 가능한 한 즉시 개발 테스트 · 오류 보고를 열어 체크리스트 번호와 기대/실제 결과를 적고 ZIP을 직접 전달한다. 강제 종료라면 같은 테스트 프로필로 재실행하자마자 먼저 내보낸다. 독립 파일 fixture 검사는 통과했지만 실제 게임 화면 클릭·강제 종료 뒤 이전 로그 포함·동시 저장 수집은 아직 미검증이다.

각 프로필에서 `RaidPart1.exe --raid-test-profile=<이름> -logFile <로그경로>`로 실행한다. 실패하면 빌드 시각, 프로필 ID, Stage, 종료 직전 화면/확정 선택, 재실행 화면, `Player.log` 또는 지정 로그의 첫 `[Campaign checkpoint]` 경고를 기록한다. 같은 세션의 Steam 재접속은 이 표의 파일 이어하기 검사가 아니다. 관리 코드 검사는 정산 파일 단독 이어하기, 확정 카드 원장·후보 일치, 손상된 파일/백업, Stage 1~6 정산 후 목적지를 통과했지만, 실제 강제 종료·재실행을 대신하지 않는다.

## 직접 편집 가능한 컷씬 확인 (Unity Play 미검증)

원본 Unity에서 Assets/Resources/UI/RaidLobbyLayout.asset의 Stage 1 Arrival/Defeat Presentation Prefab을 확인한다. 예제 Timeline의 카메라 키와 보스 표시 마커를 잠시 바꾸고 1명+AI 3명으로 입장/처치를 진행한다. 정상 종료와 Interact 건너뛰기에서 카메라·입력이 복구되고 전투/보상/포탈이 한 번만 진행되는지 확인한다. 프리팹 참조를 잠시 비워 기본 연출로 진행되는지 확인한 뒤 원래 참조로 돌린다. 재도전·중간 종료 후 이어하기에서 이전 프리팹 인스턴스나 이벤트가 남지 않는지 확인한다. 편집한 자산은 반복 Play에서도 유지되어야 한다. 현재 결과: 코드/원본 Unity 에디터 생성·참조 검사만 통과; Play와 4인 Steam 미검증.

