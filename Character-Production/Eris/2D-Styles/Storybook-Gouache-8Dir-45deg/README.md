# 이리스(Iris) 동화풍 수채·과슈 45° 탑다운 8방향 스프라이트

3D 모델링 참고 없이, 순수 2D **04번 동화풍 수채·과슈**(`../Ingame-Concepts-5Styles/Sprites/04-storybook-gouache.png`)와 이리스 원화 일러스트(`../../Texturing/Reference/Iris_Concept_UserReference.png`)만 기준으로 제작한 **하향 45° 탑다운 8방향 대기 리소스**입니다.

## 파일 구성

- **4×2 통합 시트**: `Iris_StorybookGouache_45deg_8Dir.png` (`2048 × 1152 px`, 4열 × 2행, 셀 크기 `512 × 576 px`)
  - 1행 (좌→우): `↓ 정면(S)` · `↘ 우전방(SE)` · `→ 우측(E)` · `↗ 우후방(NE)`
  - 2행 (좌→우): `↑ 후면(N)` · `↖ 좌후방(NW)` · `← 좌측(W)` · `↙ 좌전방(SW)`
- **개별 8방향 투명 RGBA PNG** (`Sprites/Iris_Gouache_45deg_{s,se,e,ne,n,nw,w,sw}.png`):
  - 규격: `512 × 576 px`
  - 발 기준점(Pixel Pivot, 좌상단 원점): `(256, 512) px`
  - Unity Normalized Pivot(좌하단 원점): `(0.5, 0.111111)`
- **미리보기 & 메타데이터**:
  - 인터랙티브 전장 8방향 뷰어: `index.html` (오프라인 바로 열기 지원, 바닥 클릭 / WASD / Q·E 회전 / 8방향 링 동시 배치)
  - 정적 비교 시트: `Preview-Gouache-8Dir-Sheet.jpg`, `Preview-Gouache-8Dir-Arena.jpg`
  - 좌표·해시 정보: `manifest.json`

## Unity 2D Sprite 임포트 기준

1. `Iris_StorybookGouache_45deg_8Dir.png` (또는 개별 `Sprites/*.png`)를 원본 Unity 프로젝트(`C:\Astella\Astella\Assets\...`)에 복사합니다.
2. `Texture Type = Sprite (2D and UI)`, `Alpha Is Transparency = On`, `Filter Mode = Bilinear`로 설정합니다.
3. 통합 시트 사용 시 `Sprite Mode = Multiple` → Sprite Editor에서 `Grid By Cell Size` (`512 × 576 px`)로 자르고 모든 셀의 피벗을 **`(0.5, 0.111111)`**로 맞춥니다.
