# Astella 2D · 공명 원정

캐릭터 컨셉 브랜치의 50° 쿼터뷰 도트 캐릭터를 사용하는 별도 2D 게임이다. XY 평면, 정사영 카메라, SpriteRenderer, Rigidbody2D, CircleCollider2D로 구성한다. 기존 3D 씬·프리팹·빌드 목록·캠페인 저장 형식은 변경하지 않는다.

## 실행

원본 프로젝트 `C:/Astella/Astella`를 Unity 6000.3.26f1에서 열고 **Astella > 2D > Play Game**을 선택한다. 에디터가 닫혀 있으면 저장소 루트의 `Play-Astella2D.cmd`를 실행할 수도 있다.

생성 도구는 `Assets/Astella2D/Scenes/Astella2D.unity`를 Unity API로 생성한다. 같은 경로에 사용자가 편집한 씬이 있으면 보존하고 검증 후 연다. 다른 씬에 저장하지 않은 변경이 있으면 이를 덮어쓰지 않는다. Unity 에디터를 Codex worktree에서 열지 않는다.

**Astella > 2D > Build Windows Player**는 이 2D 씬만 별도 `Builds/Astella2DWindows/Astella2D.exe`로 빌드한다. 기존 3D 빌드와 EditorBuildSettings는 유지한다. 최초 import와 실행·빌드에는 활성화된 Unity 라이선스가 필요하다.

## 현재 게임 루프

- 13종 중 캐릭터 선택: 수호기사, 랜스 창기사, 성전 망치 기사, 시간술사, 음유시인, 연금술사, 쌍검 암살자, 용창사, 숲의 궁수, 기공사, 성운술사, 인과술사, 이리스.
- 직접 조작하는 캐릭터 1명과 AI 동료 3명으로 수호자와 전투한다.
- 이동·조준·기본 공격, 범위 기술, 파티 회복과 쓰러진 동료 소생, 공명 궁극기, 짧은 무적 대시.
- 원형·도넛·둥근 끝 직선·부채꼴·다중 낙성 장판. 예고 종료 시 실제 원반 교차 판정으로 피해를 적용한다.
- 수호자 체력 절반부터 공격 간격이 줄어든다. 격파 후 공격력·체력·대시 축복 중 하나를 선택해 다음 전투로 진행한다.
- 3회 전투 완료, 패배, 재도전, 캐릭터 선택 복귀, 일시정지.

| 입력 | 동작 |
| --- | --- |
| WASD / 기존 이동 키 설정 | XY 이동 |
| 마우스 | 조준·바라보는 방향 |
| 마우스 왼쪽 / J | 기본 공격. 근접은 사거리 안에서 사용 |
| Q | 공명 범위 기술 |
| E | 파티 회복·소생 |
| R | 공명 100% 궁극기 |
| SPACE | 무적 대시 |
| ESC | 일시정지 메뉴 |
| F1 | 조작 안내 |

현재 전투는 새 2D 판정으로 만든 싱글플레이 원정이다. 13종의 외형·역할·근접/원거리 구분을 연결했으며, 기존 3D의 직업별 9키 스킬 처리기·Steam/LAN 동기화·전체 보스와 캠페인을 이식한 결과는 아니다. 캐릭터 원본은 방향별 대기 그림 8장뿐이다. 이동은 방향 전환과 바운스로, 공격은 별도 타격·투사체 효과로 표시한다. 걷기·공격 전용 캐릭터 프레임이나 보스의 별도 동작 프레임은 아직 없다.

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

첫 검사는 Unity와 독립적인 판정 경계 검사, 두 번째 검사는 원본 csproj의 참조로 현재 런타임·에디터 소스를 컴파일한다. Unity import·Play·렌더링 검증을 대신하지 않는다.

실제 Unity Play 검사는 원본 프로젝트에서 아래 entry point로 실행한다. `-nographics`와 `-quit`를 붙이지 않는다. 검사가 Play를 구동하고 스스로 종료한다.

```powershell
& 'C:/Program Files/Unity/Hub/Editor/6000.3.26f1/Editor/Unity.exe' `
  -batchmode -projectPath 'C:/Astella/Astella' `
  -executeMethod Astella.TwoD.Editor.Astella2DEditor.RunChecks `
  -logFile 'C:/Astella/2d-evidence-20261120/unity-checks.log' `
  --astella2d-evidence=C:/Astella/2d-evidence-20261120
```

검사는 메뉴 → 전투 시작 → 파티 선택과 Rigidbody2D 이동 → Prism 버튼의 대시·기본 공격 → 패배 화면의 메뉴 버튼 → 재시작 순으로 진행한다. 재시작 후 첫 원형 장판과 보스 시전바를 Game View PNG로 저장한다. 장판 피해 후 플레이어 체력 게이지가 갱신되는지 확인하고 결과를 JSON에 기록한다. 수동 입력·승리·축복 선택·전체 13종·다른 해상도는 별도 Play 검증 범위다.

2026-11-20 실제 수행 결과와 미검증 범위는 `VALIDATION.md`에 구분했다.
