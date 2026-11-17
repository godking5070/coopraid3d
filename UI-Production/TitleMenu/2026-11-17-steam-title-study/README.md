# ASTELLA · Steam 타이틀 연구 21~25

공식 Steam 헤더 5개에서 글자와 장식의 구성 원리를 관찰하고, 아스텔라의 은빛·보라색 UI와 멈춘 시간 모티프에 맞춰 만든 타이틀 시안이다. 로고 PNG 5개는 **내장 `image_gen.imagegen`으로 생성한 래스터 컨셉 아트**다. 편집 가능한 글자 벡터나 최종 확정 브랜드 자산이 아니며, 실제 Unity 타이틀 화면에는 적용하지 않았다.

비교에는 같은 숲 배경·메뉴 배치를 사용한다. 은색 금속과 보라색은 승인된 Prism UI의 시각 언어이고, 시계는 기존 이야기의 멈춘 시간을 해석한 장식이다. 숲과 수정은 이 미리보기의 아트 방향이며, 새로운 성좌·종교·역사 설정을 확정하지 않는다. 시안별 이름도 비교를 위한 작업명이다.

| 번호·ID | 작업명 | 공식 참고작 | 가져온 구성 원리 |
|---|---|---|---|
| `21-clockglyph` | 시간의 첫 글자 | [CHRONO TRIGGER](https://store.steampowered.com/app/613830/) | 글자와 시간 장치의 결합 |
| `22-violetinitial` | 보라의 각인 | [Lies of P](https://store.steampowered.com/app/1627720/) | 한 글자의 크기·색 강조 |
| `23-expedition` | 원정의 명각 | [Clair Obscur: Expedition 33](https://store.steampowered.com/app/1903340/) | 큰 제목과 보조 형태의 크기 위계 |
| `24-luminous` | 빛의 필사 | [Ori and the Will of the Wisps](https://store.steampowered.com/app/1057090/) | 유기적인 붓획과 제한된 빛 |
| `25-ornament` | 태엽의 문장 | [Hollow Knight](https://store.steampowered.com/app/367520/) | 글자 밖의 상하 대칭 장식 |

공식 이미지에서 실제로 관찰한 내용과 우리의 적용 해석은 [analysis.md](analysis.md)에 나누어 기록했다. 참고작의 로고·서체 윤곽·문양·캐릭터를 그대로 합성하거나 재현하는 작업은 아니다. 각 시안에 제공한 참고 이미지의 역할과 생성 지시문은 [prompts.json](prompts.json)에 있다.

| 파일 | 역할 |
|---|---|
| [index.html](index.html) | 공유 숲 배경 위에서 5개 로고와 같은 메뉴를 비교하는 브라우저 미리보기 |
| [concepts.json](concepts.json) | 21~25의 ID·작업명·PNG 경로·Steam 참고작 매핑 |
| [prompts.json](prompts.json), [generation.json](generation.json) | 내장 생성 도구의 지시문·참고 역할과 생성 결과 기록 |
| `Artifacts/<id>.png` | 원본 투명 알파 PNG. 재편집·검토의 기준 파일 |
| `Artifacts/<id>.webp` | 채팅·가벼운 표시를 위한 비율 유지 축소본. 원본 PNG와 별도로 보존 |
| `ReferencePreviews/<appid>.jpg` | 공식 Steam 헤더의 작은 비교 분석용 썸네일 |
| [sources.json](sources.json) | 공식 Steam 페이지·다운로드 URL·원본 참고 이미지의 해시와 조사 기록 |
| [visual-analysis.json](visual-analysis.json) | 실제 헤더의 글자형·장식 위치 관찰과 시각적 가독성 판단 |
| [validation.json](validation.json), [native-validation.json](native-validation.json) | 일반 화면 렌더와 채팅용 비교 페이지의 확인 결과 |
| [compression.json](compression.json), [review-assets.json](review-assets.json) | PNG 보존·WebP 알파와 비교·배경 JPEG 파생 과정 |
| [inline-background.jpg](inline-background.jpg) | 채팅 미리보기용 공유 숲 배경의 축소 JPEG |
| `preview-<id>-1920.png`, `preview-21-clockglyph-1366.png` | `render-preview.cjs`가 만드는 디자인 화면 캡처 |
| [render-preview.cjs](render-preview.cjs) | 별도 숨김 Edge로 브라우저 화면·자원·선택 조작을 검사하는 도구 |
| [publish-inline.cjs](publish-inline.cjs) | WebP·썸네일·배경을 포함한 Orca 채팅용 비교 페이지 작성 도구 |

`ReferencePreviews`는 타사 타이틀의 분석 자료다. **게임 프로젝트의 `Assets`로 반입하거나 아스텔라 제품 이미지에 합성하지 않는다.** 공식 헤더 원본은 외부 evidence 폴더에 있으며, 이 폴더의 작은 썸네일은 비교 화면을 위한 파생본이다. JSON에 남아 있는 외부 절대 경로는 조사 당시 파일 위치를 기록한 것으로, 미리보기의 의존 경로는 아니다.

원래 배경과 폰트는 `../2026-11-17-prism-engraved/`를 공유한다. 일반 HTML은 `woodland.png`와 `Fonts/MaruBuri-SemiBold.ttf`, `Fonts/Pretendard-Medium.ttf`를 사용하므로 해당 형제 폴더도 함께 보존한다. 이 폰트는 메뉴와 한글 표시에 사용하며, 생성 로고 PNG가 편집 가능한 MaruBuri 글자 윤곽이라는 의미는 아니다. 채팅 페이지 작성 도구는 같은 공유 폴더의 `menu-overlay-1920.png`도 읽는다.

브라우저에서 `index.html`을 열면 21번이 기본으로 표시된다. 주소 끝에 다음 쿼리를 붙이면 원하는 상태를 바로 볼 수 있다.

```text
index.html?concept=21-clockglyph
index.html?concept=22-violetinitial
index.html?concept=23-expedition
index.html?concept=24-luminous
index.html?concept=25-ornament
index.html?concept=21-clockglyph&saved=1
index.html?concept=21-clockglyph&capture=1
```

`saved=1`은 예시 저장 체크박스와 이어하기 표시를 켠다. 실제 저장 파일을 읽거나 쓰지 않는다. `capture=1`은 검토용 상단·하단 안내와 시안 선택 탭을 숨긴다. 메뉴 클릭은 선택 표시만 바꾸며 게임 시작·설정 변경·저장·종료를 실행하지 않는다.

저장소 루트에서 다음 명령으로 별도 브라우저 디자인 검사를 실행할 수 있다. Node 26과 Microsoft Edge를 사용하며 Unity를 실행하지 않는다.

```powershell
node UI-Production/TitleMenu/2026-11-17-steam-title-study/render-preview.cjs
```

도구는 각 시안의 1920×1080 화면과 21번의 1366×768 화면을 캡처한다. 상세 결과 파일은 기본적으로 `C:/Astella/title-steam-study-evidence-20261117/render-validation-latest.json`과 같은 폴더의 시간별 JSON·브라우저 로그에 기록된다. 원본 PNG 보존·표시용 WebP의 치수와 알파 정보는 외부 `compression.json`, 참고·배경 JPEG 파생 과정은 `review-assets.json`을 확인한다.

`ORCA_CHAT_SCRATCH_DIR`가 설정된 Orca 환경에서는 다음 명령으로 채팅용 비교 페이지를 만들 수 있다.

```powershell
node UI-Production/TitleMenu/2026-11-17-steam-title-study/publish-inline.cjs
```

저장된 결과는 다음과 같다.

- [generation.json](generation.json): 원본 PNG 5개의 `ASTELLA` 철자를 육안 확인했고, 알파 샘플·해시와 원본 보존을 기록했다.
- [validation.json](validation.json): 5개 시안의 1920×1080 화면과 21번의 1366×768 화면, 총 6개가 `passed=true`다.
- [native-validation.json](native-validation.json): 공식 참고 5개와 새 로고 5개 표시, 카드·탭 선택, 확대·복귀를 확인했다. `passed=true`이며 원격 이미지 요청은 0개다.
- [compression.json](compression.json): PNG 보존과 WebP 알파 비교가 통과했다. 표시용 WebP 5개 합계는 113,418바이트로 120KiB 목표 안이다.
- [review-assets.json](review-assets.json): 비교 썸네일 5개와 배경 JPEG를 만든 뒤 원본 보존을 확인했다.

위 결과는 브라우저 디자인 미리보기의 확인이다. 최종 브랜드는 미확정이며 Unity에는 적용하지 않았다.
