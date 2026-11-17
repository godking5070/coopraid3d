# ASTELLA · 원정의 명각 변형 26~30

앞선 [23번 원정의 명각 PNG](../2026-11-17-steam-title-study/Artifacts/23-expedition.png)를 기준 이미지로 사용해, 내장 `image_gen.imagegen`으로 만드는 입체 아트 로고 변형 5개다. 은빛 제목·절제된 보라색·멈춘 시간이라는 방향을 이어가며 글자 비례, 표면과 시계 장식의 구성을 비교한다. 시안의 이름은 비교용 작업명이다.

| 번호·ID | 방향 | 원본 PNG 경로 |
|---|---|---|
| `26-balanced` | 명각 · 균형 | `Artifacts/26-balanced.png` |
| `27-tall` | 명각 · 장신 | `Artifacts/27-tall.png` |
| `28-chiseled` | 명각 · 조각 | `Artifacts/28-chiseled.png` |
| `29-fractured` | 명각 · 단절 | `Artifacts/29-fractured.png` |
| `30-spring` | 명각 · 태엽 | `Artifacts/30-spring.png` |

정확한 표시명과 매핑은 `concepts.json`, 생성 지시문은 `prompts.json`, 결과 치수·해시·생성 기록은 `generation.json`을 기준으로 확인한다. 27·29번의 배경 번짐을 정리한 지시문과 27번의 최종 재생성 지시문은 `refinement-prompts.json`에 있다. 원본 투명 PNG와 표시용 축소 WebP는 별도로 보존한다. 로고는 래스터 컨셉 이미지이며 편집 가능한 글자 벡터나 확정된 브랜드 원본이 아니다.

비교 화면은 기존 메뉴 배치와 숲을 공유한다. 원래 배경·한글 및 메뉴 폰트는 `../2026-11-17-prism-engraved/woodland.png`와 같은 폴더의 `Fonts/`를 사용한다. 부모의 23번 PNG와 공유 폰트·배경은 이 변형 작업에서 변경하지 않는다.

표시용 WebP 변환 도구는 저장소 밖 `C:/Astella/title-expedition-variations-evidence-20261117/compress-logos.cjs`다. 원본 5개가 준비된 뒤 명시적으로 실행하며, 540픽셀·품질 0.75에서 시작해 합계 120KiB를 넘으면 480픽셀·품질 0.70으로 변환한다. 원본 PNG에 쓰지 않고 SHA256 전후 일치와 축소본 알파를 확인한다. 숨김 Edge는 고유 프로필로 실행하며 해당 인스턴스만 종료한다.

```powershell
node C:/Astella/title-expedition-variations-evidence-20261117/compress-logos.cjs
```

실행 결과는 같은 외부 폴더의 `compression.json`에서 치수·바이트·알파·원본 보존·`target120KBMet`를 확인한다. 최종 검사 기록은 이 폴더에도 복사했다.

브라우저에서 `index.html`을 열면 같은 숲과 메뉴 위에서 비교할 수 있다. `?concept=27-tall&capture=1`처럼 ID를 지정하면 안내를 숨긴 화면을 볼 수 있다. 채팅용 비교 화면은 `publish-inline.cjs`, 화면 검사·캡처는 `render-preview.cjs`가 만든다. 기존 부모 이미지와 배경은 유지한다.

`generation.json`과 `compression.json`에서 2172×724 원본 PNG 5개의 해시·투명 알파 보존을 확인했다. 최종 WebP는 최대 폭 540픽셀, 품질 0.75, 합계 110,416바이트다. `validation.json`은 1920×1080 5종과 1366×768 기본형의 브라우저 검사 통과를 기록한다. 글자의 ASTELLA 철자와 전체 형태는 별도 육안 검토했다. `native-validation.json`은 채팅용 카드·선택·확대·복귀와 이미지 로드 검사 결과다.

기준이 된 Steam 타이틀의 관찰과 출처는 [앞선 분석 문서](../2026-11-17-steam-title-study/analysis.md)에 있다. 이번에는 기존 ASTELLA 23번 이미지를 변형하며, 참고작의 원본 로고나 캐릭터를 제품 자산으로 가져오지 않는다. 실제 Unity에는 적용하지 않았다.
