# ASTELLA 타이틀 5종 시안

과한 보석 광택·시계 장식은 줄이고, 단순한 글자형보다 장식이 있는 다섯 방향을 제작했다. 로고는 실제 MaruBuri 글리프 윤곽과 편집 가능한 SVG 선·면을 조합했다. 이번 5종 로고는 이미지 생성 도구로 제작하지 않았다. 기존 AI 생성 숲 배경은 사용자의 요청에 따라 그대로 유지한다.

| 번호 | 시안 | 구조 |
|---|---|---|
| 1 | 프리즘 세공 | 기존 HUD의 얕은 금속 베벨·삼각 끝선·작은 보라 연결부 |
| 2 | 멈춘 시계 | 비대칭 열린 시계와 12눈금·얇은 각인선 |
| 3 | 성소의 인장 | 챔퍼 금속 끝장식·수직 중심·대칭 인장선 |
| 4 | 균열과 봉인 | 넓은 워드마크·엇갈린 금속선·3개 보라 파편 |
| 5 | 원정 기록 | 자간을 넓힌 활판 제목·고서 표지의 이중 금속선 |

디자인 이름은 시각적 방향을 구분하기 위한 명칭이며 새로운 공식 세계관 상징이나 지명을 정한 것이 아니다. 시계탑·열두 번째 종소리·멈춘 시간은 기존 스토리에서 가져왔다. 서사 근거는 메인의 `Assets/Scripts/StageManager.cs:304–308`, `Assets/Scripts/BossMarionetteNormal.cs:499–501`이다. 강한 스포일러 도상이나 새 슬로건을 넣지 않았다.

`index.html`을 열면 같은 배경과 같은 메뉴에서 1~5번을 전환할 수 있다. `?capture=1&concept=02-time` 같은 주소는 검토 도구를 숨겨 촬영한다. 메뉴 6개는 선택 표시만 바꾸며 실제 게임·저장·종료 기능을 실행하지 않는다. `예시 저장 있음` 체크와 키보드 방향키·Home·End로 상태·포커스를 확인한다. Unity 적용·실제 Play 검증을 뜻하지 않는다.

- `Concepts/*.svg`: 개별 편집 가능한 투명 로고 5개. 정확한 표기 ASTELLA.
- `preview-01-prism-1920.png`~`preview-05-chronicle-1920.png`: 각 시안의 1920×1080 Edge 렌더.
- `preview-01-prism-1366.png`: 1366×768에서의 배치 확인.
- `astella-wordmark.svg`, `wordmark-build.json`, `build-wordmark.ps1`: 원래 폰트 윤곽과 글자별 위치 기록. `build-concepts.cjs`는 1번과 목록을 재생성하며 2~5번 SVG는 개별 저작 파일이다.
- `woodland.png`: 메인 `title-dreams-environment.png`의 바이트 동일 복사본. `reference-original.png`는 사용자가 첨부한 비교 화면.
- `Fonts/`: 기존 게임의 MaruBuri SemiBold·Pretendard Medium 원본과 라이선스.
- `render-preview.cjs`, `validation.json`: 5종/두 해상도의 폰트·이미지·문구·배치·선택·키보드 검사.
- `menu-overlay-1920.png`, `inline-background.jpg`, `publish-inline.cjs`, `prepare-inline.ps1`: 채팅용 미리보기. 배경 1장·공통 메뉴 1장과 각 SVG를 합성해 같은 화면에서 교체한다.

원본 메인의 이전 시안, Unity 스크립트·씬·프리팹·프로필은 변경하지 않았다. 현재 폴더만 독립 제작본으로 추가한다.
