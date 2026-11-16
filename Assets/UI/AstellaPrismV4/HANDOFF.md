# Prism V4 · 메인 작업 인계

## 전달 범위와 원본

시각 기준은 `UI-Concepts/2026-11-16-prism-v4/02-continuous-fill.png`입니다. 보스 HP 바 양쪽 보석과 얇은 시전바는 남고, 하단 HP·직업 자원바의 양끝 보석 네 개는 없습니다. 하단 은색 삼각 프레임 내부까지 채움과 트랙이 연속되어야 합니다. 중앙 `V`와 자수정 프리즘, 4인 파티, 8개 스킬 슬롯을 유지합니다.

이 패키지는 평면 시안에서 새로 제작한 독립 RGBA 자산과 편집용 소스입니다. 시안에 구워진 숫자·문자·배경을 그대로 잘라 붙인 런타임 UI가 아닙니다. 구성과 실제 치수는 `Source/art-manifest.json`이 기준입니다. 새 스킬·직업·역할 아이콘 그림은 제작·포함하지 않으며 기존 메인 아이콘을 재사용합니다. 아이콘 폴더와 manifest의 아이콘 파일 경로도 없습니다. 중앙 자수정 프리즘은 HUD 구조물로 포함합니다.

원본 프로젝트 감사 대상은 `C:/Astella/Astella`였습니다. 이 패키지 제작 중 원본 프로젝트의 자산·코드·씬·프리팹·프로필을 수정하지 않았습니다. 통합은 새 폴더를 가져온 다음 메인 담당자가 수행합니다.

## 독립 도구의 경계

| 파일 | 책임 |
|---|---|
| `Runtime/PrismV4Gauge.cs` | 전체 텍스처를 압축하지 않는 일반 Image 기반 채움 표시. 선택적 `Image sourceFill`을 표시 입력으로 사용 |
| `Runtime/PrismV4Bindings.cs` | 조립된 뷰의 참조 묶음. 게임 수치·입력 동작·로컬 플레이어 탐색 없음 |
| `Editor/PrismV4Importer.cs` | 새 패키지 Sprite의 import 설정 |
| `Editor/PrismV4PrefabBuilder.cs` | 새 패키지 `Prefabs/` 아래에 독립 구성 예제 생성 |
| `Editor/PrismV4GaugeEditor.cs` | 기본 Image 설정과 함께 소스 Image·미리보기 값·채움 영역·방향을 Inspector에 노출 |

메뉴는 `Tools > Astella > Prism V4 > Import Sprites`, `Tools > Astella > Prism V4 > Build Example Prefabs`입니다. 프리팹 생성은 기존 파일을 덮지 않는 새 버전 방식으로 수행합니다. 원본 HUD 복제·활성화, 프로필 교체, 씬 저장, Unity 실행, Play 전환, YAML 직접 수정은 수행하지 않습니다.

예제는 Boss, Player, PartyRow, SkillSlot, Center 역할을 독립적으로 확인할 수 있도록 구성합니다. 스킬 슬롯과 파티 배지의 artwork `Image`는 빈 바인딩 지점입니다. 대체 그림을 그리거나 fallback 이미지 파일을 자동으로 읽지 않습니다. 예제 프리팹이 생성되었다고 기존 메인 HUD에 연결된 것은 아닙니다.

## 메인 브리지 계약

메인에는 `AstellaOrnateHUD`, `AstellaPlayerHUDV3`, `AstellaClassJobHUD`, `AstellaClassUltimateHUD`, `AstellaPartyHUDV4`, `AstellaBossHUDV4`가 별도 책임을 갖고 있습니다. 새 표시 컴포넌트로 직렬화 필드 타입을 직접 바꾸지 말고, 기존 소스 Image 또는 전용 브리지를 사용합니다. 기존 `AstellaPointedGauge` 타입의 필드에 `PrismV4Gauge`를 강제로 대입할 수는 없습니다.

| 새 표시 | 실제 소스와 기존 책임 |
|---|---|
| 플레이어 HP | `AstellaOrnateHUD.playerHpFill`, `playerHpText`. 로컬 네트워크 아바타 상태 또는 `PlayerController.currentHp/maxHp`에서 가져오는 기존 흐름 보존 |
| 우측 직업 자원 | `AstellaOrnateHUD.resourceFill/resourceText` 또는 `AstellaClassJobHUD`의 주 자원 선택·표시 단위 규칙. `.ultimate` 제외 |
| 중앙 궁극기 | `AstellaClassUltimateHUD`의 `.ultimate` 전용 자원, `ChargeRatio`, `CurrentState`, 실제 가용성. `AstellaPlayerHUDV3.IsReady/ReadinessReason`과 독립 상태 계약 유지 |
| 보스 HP·이름 | `AstellaOrnateHUD.bossHpFill`, `bossPercentText`, `bossTitleText`; `BossController.HpRatio` 및 실제 스테이지·보스명 |
| 시전 게이지 | `AstellaOrnateHUD.castFill`, `castRoot`, `castNameText`, `castTimeText`. 일반 시전·그로기 표시와 원래 숨김 조건 유지 |
| 보스 대상 줄 | `AstellaBossHUDV4.targetRoot/targetLine/targetText`. 실제 `aggroTarget`과 시전·대기 배치 책임 보존 |
| 파티 행 | `AstellaPartyHUDV4.source`의 `PartyListUI.memberRows`, 각 `Row.sourceIndex`. 기존 타깃 선택·이름·직업·HP·보호막·사망·선택·어그로·상태 표시 유지 |
| 8개 스킬·중앙 V | `AstellaOrnateHUD.slots`의 `SlotBinding`: 기존 `button`, `slot`, `sourceLabel/sourceStatus/sourceKey`, `keycap`, `cooldown`, `cooldownShade`, `artwork`, `fallbackArt` 연결 유지 |

### 직업 자원과 궁극기의 분리

우측 바의 `60 / 100`은 예시입니다. `AstellaClassJobHUD`는 직업 정의의 자원 중 `.ultimate`로 끝나는 ID를 건너뛰며, 실제 최대값이 유효한 주 자원을 고릅니다. 드래곤 브레이커의 루트별 heat 예외 및 `NineKeyDamageKit.DisplayResource/DisplayMaximum`의 표시 단위 변환도 보존해야 합니다. 원본 그림의 예시 최대값을 게임 수치로 쓰지 않습니다.

중앙 프리즘은 `.ultimate` 전용 자원과 궁극기 사용 가능 상태를 표시합니다. 자원이 가득 차도 사망·잠김·조작 제한·재사용 대기·비용 등으로 사용 불가능하면 준비 상태로 표시하지 않습니다. 별도 궁극기 비용이나 새 자원을 만들지 않습니다.

기존 `AstellaOrnateHUD.secondaryResourceFill/secondaryResourceText`는 궁극기 계기용입니다. 특히 `AstellaPlayerHUDV3.compactUltimateValue=true`는 `secondaryResourceText`를 `.ultimate` 값으로 갱신합니다. 새 우측 직업 자원바를 이 필드에 연결하면 궁극기 값으로 덮일 수 있으므로, 주 자원용 뷰와 중앙 궁극기용 뷰를 분리합니다.

### 키·아이콘·행동

`1/2/3/4/Q/Space/R/F/V`는 배치 예시입니다. 키 텍스트는 `LocalInputBindings.Label`과 기존 `SlotBinding.bindingOverride` 또는 `CharacterSlots.Binding`을 통해 실제 설정을 표시합니다. 예시 `V`를 PNG에 굽거나 실제 재설정을 무시하지 않습니다. 파티 단축키도 기존 `Party1`~`Party4` 바인딩을 따릅니다.

실제 스킬은 기존 `Button` 콜백과 `SkillHotbarUI`가 처리합니다. 새 프레임을 연결하면서 스킬 슬롯을 추가하거나 기존 이벤트 리스너를 다시 등록하지 않습니다. artwork와 기존 `fallbackArt`는 메인에 이미 연결된 아이콘 자산을 유지합니다. 이 패키지의 빈 `Image` 바인딩은 새 아이콘이나 fallback 파일을 공급하지 않습니다. 새로운 프리즘 구조물에 기존 클래스 팔레트 머티리얼을 무조건 덮어씌우지 않습니다.

## 레이어·채움 계약

기본 순서는 고정 트랙 → 값에 따른 채움 → 고정 프레임 → 고정 키 배지·런타임 텍스트입니다. 텍스트와 버튼은 채움 마스크 밖에 둡니다. 슬롯 아이콘과 재사용 대기 오버레이는 슬롯 내부 형태로 제한합니다. 상태별 프레임·오버레이는 하나의 상태 정책으로 교체하고 여러 프레임을 겹치지 않습니다.

게이지의 전체 텍스처와 프레임 위치·크기는 값 변화에 따라 바꾸지 않습니다. 표시 비율만 제한합니다. `0%`에서는 활성 채움이 없고, `100%`에서는 양쪽 삼각 내부 끝까지 채워집니다. 부분 충전에서는 남은 영역에 동일한 내부 형태의 트랙을 표시하며 남색 배경 구멍을 만들지 않습니다. `60 / 100` 텍스트와 실제 표시 비율도 일치시킵니다. 잘린 삼각·팔각 형태에 기본 사각 crop만 적용해 내부 끝을 비워두지 않습니다.

## 새 폴더로 가져오는 순서

1. `Assets/UI/AstellaPrismV4/`가 기존 메인 폴더와 충돌하지 않는지 확인한 뒤 새 폴더로 가져옵니다. 기존 자산의 `.meta` GUID를 복사하거나 재사용하지 않습니다.
2. 두 메뉴로 Sprite 설정과 독립 예제를 확인합니다. import 설정은 Single·FullRect·입력 Alpha·Bilinear·Clamp·mipmap 없음·비압축을 기준으로 합니다.
3. 활성 HUD 원본을 보존하고 메인에서 검토용 복사본을 만든 뒤, 위 계약대로 표시만 연결하는 브리지를 작성합니다. 이 패키지는 이 복사나 활성 전환을 실행하지 않습니다.
4. 실제 Player/Party/Boss 흐름이 새 뷰 하나에 중복으로 쓰지 않도록 기존 표시 writer와 새 브리지의 소유권을 정리합니다. 스킬 사용과 타기팅의 기존 소유권은 유지합니다.
5. 아래 검증 후에만 메인 담당자가 활성 프리팹·프로필 전환을 결정합니다.

## 메인 적용 후 확인할 항목

다음은 인계 이후의 확인 항목이며 완료 기록이 아닙니다.

- PNG의 RGBA·투명 외곽·manifest 경로·치수와 import 결과. 글자·숫자·남색 사각 배경이 PNG에 굽혀 들어가지 않았는지 확인. 새 스킬·역할 아이콘 파일이 포함되지 않았고 빈 artwork에 기존 메인 아이콘만 연결했는지도 확인.
- 0%, 1%, 50%, 60%, 100% 채움에서 텍스처 고정, 삼각 끝 연속, 0% 숨김, 100% 전체 내측 형태 확인.
- HP·직업 자원·중앙 궁극기가 서로 다른 실제 값으로 갱신되는지 확인. 직업·루트 전환과 토큰형 자원의 표시 규칙 포함.
- 단축키 재설정, 스킬 클릭·툴팁·재사용 대기·사용 불가·중앙 궁극기 가용성 확인.
- 파티 입장·퇴장·사망·부활·보호막·타깃 선택, 보스 시전·그로기·대상 줄의 정상 갱신 확인.
- 씬 재로드 후 참조 유지, 창 크기·화면비 변경과 기존 HUD 편집 ID/이동·배율 기능 확인.
- Unity 컴파일·실제 Play 렌더링과 메인 프로젝트의 필수 검사를 실행하고 별도로 결과 기록.

현재 전달물은 이 작업을 시작할 수 있는 독립 자산과 예제 준비 범위입니다. 메인 게임 연결·라이브 검증 완료를 주장하지 않습니다.
