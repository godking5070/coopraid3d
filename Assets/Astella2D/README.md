# Astella 2D · 공명 원정

캐릭터 컨셉 브랜치의 50° 쿼터뷰 도트 캐릭터를 사용하는 별도 2D 게임이다. XY 평면, 정사영 카메라, SpriteRenderer, Rigidbody2D, CircleCollider2D로 구성한다. 기존 3D 씬·프리팹·빌드 목록·캠페인 저장 형식은 변경하지 않는다.

## 실행

원본 프로젝트 `C:/Astella/Astella`를 Unity 6000.3.26f1에서 열고 **Astella > 2D > Play Game**을 선택한다. 에디터가 닫혀 있으면 저장소 루트의 `Play-Astella2D.cmd`를 실행할 수도 있다.

생성 도구는 `Assets/Astella2D/Scenes/Astella2D.unity`를 Unity API로 생성한다. 같은 경로에 사용자가 편집한 씬이 있으면 보존하고 검증 후 연다. 다른 씬에 저장하지 않은 변경이 있으면 이를 덮어쓰지 않는다. Unity 에디터를 Codex worktree에서 열지 않는다.

**Astella > 2D > Build Windows Player**는 이 2D 씬만 별도 `Builds/Astella2DWindows/Astella2D.exe`로 빌드한다. 기존 3D 빌드와 EditorBuildSettings는 유지한다. 최초 import와 실행·빌드에는 활성화된 Unity 라이선스가 필요하다.

## 현재 게임 루프

2026-11-23부터 이 2D 게임이 Astella의 본편이다. 기존 3D 레이드 코드와 씬은 보존한다.

- 13종 중 캐릭터 선택: 수호기사, 랜스 창기사, 성전 망치 기사, 시간술사, 음유시인, 연금술사, 쌍검 암살자, 용창사, 숲의 궁수, 기공사, 성운술사, 인과술사, 이리스. 선택 화면에 역할·체력과 Q/E/R 기술 설명을 표시한다.
- 직접 조작하는 캐릭터 1명과 AI 동료 3명. 동료는 선택한 역할에 맞춰 수호·치유·딜러로 균형 있게 편성한다.
- 역할별 기술 세트 5종(`Runtime/Astella2DKits.cs`). 수호는 강타·파티 방벽, 치유는 회복·소생, 근접은 고피해·전투 집중, 원거리는 관통 사격·후퇴 도약, 마법은 지연 폭발·보호막을 쓴다.
- 원정 3회는 보스 체력·장판 피해·시전 속도와 패턴 목록이 다르다. 두 번째 원정부터 추적 낙성·회전 광선, 마지막 원정에 봉쇄 파동(밖→안)·십자 절단이 추가된다. 첫 시전은 항상 표적 원형이며 같은 패턴을 연속으로 쓰지 않는다.
- 장판은 테두리, 시전에 따라 차오르는 채움, 직전 점멸로 예고한다. 예고 종료 시 실제 원반 교차 판정으로 피해를 적용한다.
- 쓰러지면 전투마다 공유 부활 2회 안에서 2.5초 뒤 체력 50%와 짧은 무적으로 다시 일어난다. 부활이 없을 때 쓰러지면 패배한다.
- 격파 후 축복 8종 중 무작위 3개에서 하나를 고른다(공격력·체력·대시·궁극기 충전·받는 피해·부활 횟수·동료 공격력·기술 대기시간).
- 피해·회복 숫자, 화면 흔들림, 코드로 합성한 효과음, 발밑 링(플레이어 하늘색, 동료 역할색, 보호 중 파란색)과 조준 표시. 보스는 원정마다 색조가 다르고 충돌체가 있어 통과할 수 없다.
- 3회 전투 완료, 패배, 재도전, 캐릭터 선택 복귀, 일시정지. 결과 화면에 시간·준 피해·피격·회피·부활 횟수를 표시한다.

| 입력 | 동작 |
| --- | --- |
| WASD / 기존 이동 키 설정 | XY 이동 |
| 마우스 | 조준·바라보는 방향 |
| 마우스 왼쪽 / J | 기본 공격(누르고 있으면 연속). 근접은 사거리 안에서 사용 |
| Q | 직업 주 기술 |
| E | 직업 보조 기술 |
| R | 공명 100% 궁극기 |
| SPACE | 무적 대시 |
| ESC | 일시정지 메뉴 |
| F1 | 조작 안내 |

현재 전투는 새 2D 판정으로 만든 싱글플레이 원정이다. 기존 3D의 직업별 9키 스킬 처리기·Steam/LAN 동기화·전체 보스와 캠페인을 이식한 결과는 아니다. 캐릭터 원본은 방향별 대기 그림 8장뿐이다. 이동은 방향 전환과 바운스로, 공격은 별도 타격·투사체 효과로 표시한다. 걷기·공격 전용 캐릭터 프레임이나 보스의 별도 동작 프레임은 아직 없다.

수치는 `Astella2DKits.cs`의 기술·원정·축복 표에서 조정한다. AI가 조종하는 근접 캐릭터 기준 첫 원정은 약 40초에 끝난다.

## 자산과 편집

- `Resources/Astella2D/Characters/`: 컨셉 브랜치의 원본 RGBA 시트 13장. `character-provenance.json`에 소스 커밋·경로·해시·방향·피벗·요청문 출처를 기록했다.
- 프레임 128×144, 4열×2행. 윗줄 s/se/e/ne, 아랫줄 n/nw/w/sw. 발 피벗 `(0.5, 16/144)`, PPU 96.
- `arena-sanctuary.png`, `crystal-warden.png`: 내장 ImageGen으로 만든 새 전투 배경과 투명 보스. 정확한 요청문·원본 경로·해시는 `art-generation.json`에 있다.
- 기존 MaruBuri와 Pretendard를 새 Resources 폰트 경로로 Unity API를 통해 복사한다. 원본 폰트와 metadata GUID는 유지한다.
- 새 2D 텍스처는 Point 필터·mipmap 없음·무압축으로 가져온다.
- 새 파일의 metadata는 기존 Prism 패키지와 같은 새 자산 전용 방식의 `Tools/Write-Astella2DMetas.cjs`로 작성할 수 있다. 새 Astella2D 경로만 대상으로 하며 이미 있는 `.meta`는 교체하지 않고 기존 GUID 충돌도 검사한다.
- 전투 UI는 현재 `RaidUIPresentation`이 참조하는 Prism HUD의 복제본 `Presentation/PrismCombatHUD.prefab`이다. 플레이어 체력과 중앙 궁극기, 보스 체력·시전, 파티 4행, 8칸 스킬 배치·아이콘·툴팁을 2D 데이터에 연결한다. 구현된 2D 기술만 버튼을 활성화하고 오른쪽 게이지에는 대시 준비와 남은 시간을 표시한다. 2D 프리팹은 Unity에서 별도로 편집할 수 있다.
- 보상·패배 화면은 `RaidUIPresentation`의 `skill-reward`·`wipe` 프리팹을 사용한다. 13종 캐릭터 선택과 일시정지는 2D 전용 배치를 유지하며 활성 Prism 폼의 외형을 적용한다.

## 검사

```powershell
& Tests/Run-Astella2DChecks.ps1
& Tests/Run-Astella2DManagedChecks.ps1 -OriginalProject 'C:/Astella/Astella'
```

첫 검사는 Unity와 독립적인 판정 경계 검사와 기술·원정·축복·피해 규칙 검사, 두 번째 검사는 원본 csproj의 참조로 현재 런타임·에디터 소스를 컴파일한다. Unity import·Play·렌더링 검증을 대신하지 않는다.

빌드한 실행 파일은 `--astella2d-smoke=<폴더>`로 실행하면 자동 조종 전투를 40초 진행하고 스크린샷·프레임 시간·전투 결과 JSON을 남긴 뒤 종료한다. `--astella2d-character=N`으로 캐릭터를 고른다.

실제 Unity Play 검사는 원본 프로젝트에서 아래 entry point로 실행한다. `-nographics`와 `-quit`를 붙이지 않는다. 검사가 Play를 구동하고 스스로 종료한다.

```powershell
& 'C:/Program Files/Unity/Hub/Editor/6000.3.26f1/Editor/Unity.exe' `
  -batchmode -projectPath 'C:/Astella/Astella' `
  -executeMethod Astella.TwoD.Editor.Astella2DEditor.RunChecks `
  -logFile 'C:/Astella/2d-evidence-20261120/unity-checks.log' `
  --astella2d-evidence=C:/Astella/2d-evidence-20261120
```

검사는 메뉴 → 전투 시작 → 파티 선택과 Rigidbody2D 이동 → Prism 버튼의 대시·기본 공격 → 수호 Q/E/R과 HUD 대기시간 → 쓰러짐·부활 → 부활 소진 후 패배 → 메뉴 → 재시작과 첫 장판 피해 → 원정 1·2 승리와 실제 축복 버튼 → 원정 3 승리와 완료 화면 → 13종 전원의 8방향 그림·체력·Q/E/R 사용 → 캐릭터 선택 화면 → 자동 조종 60초 전투 순으로 진행한다. 장판·보상·원정 3·완료·메뉴·자동 전투 화면을 PNG로 저장하고 결과를 JSON에 기록한다. 사람의 키보드·마우스 조작, 효과음 청취, 다른 해상도는 별도 확인 범위다.

2026-11-20 실제 수행 결과와 미검증 범위는 `VALIDATION.md`에 구분했다.
