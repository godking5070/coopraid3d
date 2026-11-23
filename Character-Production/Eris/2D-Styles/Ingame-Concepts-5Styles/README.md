# 이리스(Iris) 인게임 2D 캐릭터 리소스 시안 5종

아스텔라(Astella)의 현재 게임 컨셉(4인 협동 하향 45~50° 쿼터뷰 보스 레이드, 황혼 천문대 현무암 전장, 붉은/청록 장판 예고)에 맞춰, 이리스 원화 일러스트(`Character-Production/Eris/Texturing/Reference/Iris_Concept_UserReference.png`)를 기반으로 서로 다른 5가지 그림체의 인게임 2D 캐릭터 스프라이트를 제작했습니다.

## 5가지 그림체 구성

| 번호 | 파일 | 그림체 | 아스텔라 인게임 적합 포인트 |
| --- | --- | --- | --- |
| 01 | `Sprites/01-handpainted-fantasy.png` | **핸드페인팅 판타지 2D** (무외곽선 회화풍) | 수호석상(`StoneGuardian.png`) 및 현무암 전장(`StoneGuardianArena.png`)과 가장 자연스럽게 어우러지는 입체 회화 질감과 황금 림라이트 |
| 02 | `Sprites/02-crisp-action-cel.png` | **선명한 액션 셀 쉐이딩** (짙은 남색 외곽선) | 빠른 보스 패턴 회피와 붉은 위험 장판 위에서 캐릭터 위치와 실루엣이 즉각 읽히는 최고 수준의 레이드 시인성 |
| 03 | `Sprites/03-hd-pixel-art.png` (+ `03-hd-pixel-art-256x288.png`) | **모던 HD 픽셀아트** (HD-2D 도트) | 입체적 전장 위에 정사각형 도트와 1px 남색 외곽선으로 선명하게 대비되는 HD-2D 감성 (256×288 1:1 원본 도트 병행 제공) |
| 04 | `Sprites/04-storybook-gouache.png` | **동화풍 수채·과슈 2D** (섬세한 세피아 선화) | 별빛 천문대 세계관과 이리스 원화의 포근하고 신비로운 분위기를 감성적으로 살린 일러스트형 2D |
| 05 | `Sprites/05-spine-modular-flat.png` | **모던 스파인 리깅형 2D** (플랫 벡터·파츠 분리) | Spine / Unity 2D Animation 본 애니메이션으로 12개 직업과 스킬 모션을 확장하기 좋은 깔끔한 기하학 2D |

## 리소스 규격 및 Unity 임포트 기준

- **캔버스 크기**: `512 × 576 px` (투명 PNG, 알파 채널 포함)
- **발 기준점(Pivot)**: 이미지 좌상단 기준 `(256, 512) px` → Unity Sprite Editor 정규화 좌표(좌하단 원점) `(0.5, 0.111111)`
- **카메라 시점**: 아스텔라 기본 쿼터뷰(하향 45~50°) 기준의 전신 인게임 대기 자세(티팟 제외, 전투·이동 대응 빈손 자세)
- **Unity Import 권장**:
  - `Texture Type`: `Sprite (2D and UI)`, `Alpha Is Transparency` 활성화
  - 스타일 01·02·04·05: `Filter Mode = Bilinear`
  - 스타일 03(HD 픽셀아트): `Filter Mode = Point (no filter)`, `Compression = None` (`Sprites/03-hd-pixel-art-256x288.png` 1:1 원본 또는 2배 정수 확대 `03-hd-pixel-art.png` 사용 권장)

## 미리보기 확인 방법

`index.html`을 로컬 웹 서버(또는 브라우저)로 열면 5가지 그림체를 **수호석상 레이드 전장(`StoneGuardianArena.png`) 위에 직접 배치**하고, 중앙 내려찍기·외곽 고리·집합 장판 위에서의 시인성과 크기를 비교할 수 있습니다.
