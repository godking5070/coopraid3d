# Astella Prism V4 · 분리형 UI 리소스

승인된 `UI-Concepts/2026-11-16-prism-v4/02-continuous-fill.png`를 게임 UI로 조립할 수 있도록 새로 분리 제작한 전달 패키지입니다. 원본 시안은 평면 이미지이며, 이 패키지는 프레임·바탕·채움·마스크·상태 표시를 독립 RGBA PNG로 재구성한 결과입니다. 제공되는 SVG는 이 재구성의 편집 가능한 소스이며, 생성 이미지에 원래 존재하던 레이어를 복원한 파일은 아닙니다.

새 스킬·직업·역할 아이콘 그림은 포함하지 않습니다. 기존 메인 프로젝트의 아이콘을 재사용하도록 스킬 슬롯과 파티 배지에는 빈 `Image` 참조만 제공합니다. 대체 그림을 그리거나 fallback 파일을 자동으로 읽지 않습니다. 중앙 자수정 프리즘은 HUD 구조물로 포함합니다.

이 폴더는 메인 프로젝트에 자동 적용하지 않습니다. 기존 `AstellaPlayerHUDV3`, `AstellaPartyHUDV4`, `AstellaBossHUDV4`, 활성 HUD 프리팹·프로필·씬은 교체하지 않습니다. 실제 게임 데이터 연결은 [HANDOFF.md](HANDOFF.md)의 계약을 따라 별도 브리지에서 수행합니다.

## 구성

| 경로 | 용도 |
|---|---|
| `Source/art-manifest.json` | 자산별 실제 파일 경로·치수·레이어 및 채움 영역의 기준 |
| `Source/` | 제공되는 편집용 SVG와 제작 정보 |
| `Sprites/Boss/` | 보스 체력·시전 게이지의 독립 레이어 |
| `Sprites/Player/` | 플레이어 HP·직업 자원 게이지 레이어 |
| `Sprites/Party/` | 파티 행·체력·배지 관련 레이어 |
| `Sprites/Hub/` | 중앙 프리즘·프레임·상태 관련 레이어 |
| `Sprites/Skills/`, `Sprites/States/` | 슬롯·키 배지·입력 및 재사용 대기 상태 |
| `Runtime/` | 메인 게임 타입에 의존하지 않는 채움 표시와 참조 묶음 |
| `Editor/` | 이 패키지의 Sprite import 및 독립 예제 프리팹 생성 메뉴 |
| `Prefabs/` | 메뉴 실행 시 생성되는 독립 예제와 반복 실행의 새 버전 |

실제 자산 목록과 치수는 manifest를 기준으로 합니다. 시안의 HP·자원·파티명·단축키는 런타임 데이터가 아니며, PNG에는 고정 텍스트를 굽지 않습니다.

## 가져오기

1. 메인 담당자가 패키지를 새 `Assets/UI/AstellaPrismV4/` 폴더로 가져옵니다. 기존 폴더나 `.meta` 위에 덮어쓰지 않습니다.
2. Unity가 일반적으로 파일을 가져온 뒤 `Tools > Astella > Prism V4 > Import Sprites`를 실행합니다.
3. 독립 예제가 필요하면 `Tools > Astella > Prism V4 > Build Example Prefabs`를 실행합니다. 생성 범위는 이 폴더의 `Prefabs/`이며, 기존 예제가 있으면 새 이름으로 버전을 만듭니다.
4. 새 프리팹에서 구성과 데이터 연결 지점을 검토한 뒤 메인용 브리지를 만듭니다. 이 단계에서만 기존 HUD에 어떤 뷰를 연결할지 결정합니다.

스크립트는 Unity를 실행하거나 Play를 시작하지 않습니다. 씬·활성 프리팹·`RaidUIPresentation`을 자동 변경하지 않으며 Unity YAML도 직접 수정하지 않습니다. 새 PNG에는 새 `.meta` GUID를 사용합니다. 다른 패키지의 GUID를 재사용하지 않습니다.

Sprite 기준은 Single, Full Rect, 입력 Alpha, Alpha Is Transparency, Bilinear, Clamp, mipmap 없음, 비압축입니다. 중심 pivot은 `(0.5, 0.5)`, PPU는 `100`이며, 개별 치수와 추가 설정은 manifest와 importer를 따릅니다. 삼각 끝과 잘린 모서리는 임의로 9-slice 늘림을 적용하지 않습니다.

## 조립과 동작

기본 순서는 바탕 트랙 → 실제 값으로 드러나는 채움 → 은색 프레임 → 고정 위치의 런타임 텍스트입니다. 채움 텍스처는 전체 크기를 유지하고 표시 범위만 바꿉니다. HP가 가득 차면 양쪽 은색 삼각 끝 내부까지 채우며, 자원바는 활성 채움 뒤의 트랙도 오른쪽 끝까지 이어져야 합니다. 값이 0이면 활성 채움은 없고 트랙은 남습니다.

`PrismV4Gauge`는 일반 UI Image 기반의 표시 컴포넌트이며, 메인의 체력·전투 타입을 찾거나 게임 수치를 계산하지 않습니다. 선택적인 `sourceFill` Image 또는 명시적으로 전달하는 값으로 표시를 갱신합니다. `PrismV4Bindings`는 조립 부품의 참조 묶음으로, 실제 스킬 사용·타기팅·네트워크 동작을 수행하지 않습니다. 예제의 정적 값은 화면 확인용입니다.

우측 바는 직업의 주 자원이며 `.ultimate` 자원을 제외합니다. 중앙 `V` 프리즘은 별도의 궁극기 표시입니다. 두 계기를 같은 자원으로 연결하지 않습니다. `V`, `Space`를 포함한 모든 키와 값은 실제 설정에서 읽습니다. 스킬·파티의 빈 artwork `Image`는 메인의 기존 스킬·직업 아이콘에 연결하며, 이 패키지에는 `Sprites/Icons/` 폴더나 아이콘 파일 경로가 없습니다.

## 검증 범위

이 전달은 분리 자산·소스·import 도구·독립 조립 예제를 준비하는 범위입니다. 메인 씬 적용, 실제 전투 바인딩, Unity Play 렌더링, 해상도별 화면 검증을 완료했다는 의미는 아닙니다. 메인 인계 시 확인할 항목과 기존 컴포넌트의 연결 계약은 [HANDOFF.md](HANDOFF.md)에 정리했습니다.

2026-11-16 로컬 확인: Runtime/Editor C# 5개를 설치된 Unity 6000.3.26f1 참조 DLL로 컴파일했습니다. 실제 렌더러와 같은 `PrismV4Gauge.BuildClipPolygon`을 호출하는 독립 C# 검사 32개가 통과했습니다. 보스·시전·플레이어·파티의 0/50/100%와 중간 비율, 채움 면적의 단조 증가, UV 범위, 중앙 궁극기의 세로 채움을 확인했습니다. 이는 Unity Editor 메뉴 실행이나 GPU 렌더링 검사를 대신하지 않습니다.

제작 저장소에는 재현 가능한 검사 소스와 실행기 `UI-Production/PrismV4/tests/GaugeGeometryCheck.cs`, `Run-StaticChecks.ps1`, 실제 결과와 소스 해시를 기록한 `last-run.json`이 포함됩니다. PowerShell에서 `& ./UI-Production/PrismV4/tests/Run-StaticChecks.ps1`로 다시 실행할 수 있습니다. 컴파일 바이너리는 임시 폴더에만 생성합니다.

제작 저장소의 새 metadata는 모든 자산 확정 후 `node UI-Production/PrismV4/write-metas.cjs --write`로 생성합니다. 대상은 이 폴더와 `AstellaPrismV4.meta`뿐이며 기존 `.meta`를 덮지 않습니다. 기존 메인에 있는 `Assets/UI.meta`, `Assets.meta`는 전달 대상이 아닙니다.
