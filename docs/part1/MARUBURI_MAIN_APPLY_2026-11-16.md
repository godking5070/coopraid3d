# 마루부리 제목 적용 — 2026-11-16

사용자가 선택한 마루부리 SemiBold를 제목·보스명·시전명 등 23곳에 적용했다. 오른쪽 게이지 숫자는 [새 비교 자료](MARUBURI_NUMERIC_FONT_REVIEW_2026-11-16.md)를 준비했으며 후보를 게임에 적용하지 않았다. 기존 숫자·본문·단축키·쿨타임 폰트는 유지한다.

## 적용과 보존

- 현재 HUD 및 제목이 있는 view 4개를 `Assets/UI/AstellaPrismV4/Typography/Applied-20261116-193901-716/`로 복사했다. profile과 SampleScene의 활성 HUD를 이 복사본으로 연결했다.
- 실제 정적 `MaruBuri-SemiBold.ttf`를 import하고 합성 Bold를 더하지 않았다. 제목에는 `#131022` uGUI Outline을 사용한다. fontSize 30 이상은 effectDistance `(1.3,-1.3)`, 나머지는 `(1,-1)`이다. 글리프 파일 자체는 수정하지 않았다.
- 기존 Outline을 재사용하고, 해당 제목의 테마 자동 적용만 해제해 런타임 테마가 선택한 서체를 덮어쓰지 않게 했다. 새 런타임 writer는 추가하지 않았다.
- 원본 프리팹 5개의 SHA256이 전후 동일하다. 복사본의 다른 Text 894개와 모든 RectTransform의 스냅샷이 적용 전후 동일하다. 바인딩과 스크립트 참조 검사도 통과했다.
- 역할 카드의 제목·설명이 섞인 rich-text 문단은 본문을 보존하도록 제외했다. 숫자 전용 필드, 쿨타임과 단축키는 변경 대상이 아니다.

명시적 메뉴는 `Tools/Astella/Apply MaruBuri Headings (Preserved Copies)`와 `Tools/Astella/Validate MaruBuri Typography`다. 자동 import·Play 훅은 없다. 처음 지정한 F12 단축키가 TextMeshPro 메뉴와 겹쳐 최종 도구에서는 제거했다.

## 라이선스

네이버 공식 안내는 마루부리에 OFL 1.1을 적용하며 상업용 소프트웨어에 포함·판매·배포할 수 있도록 허용한다. 폰트 자체 단독 판매는 금지하며 저작권 고지와 라이선스 전문을 동봉한다. [공식 안내](https://help.naver.com/service/30016/contents/18088?osType=PC&lang=ko)

실제 파일의 저작권 고지와 네이버 공식 예약명 문구 및 OFL 전문을 `Assets/StreamingAssets/ThirdPartyLicenses/MaruBuri-LICENSE.txt`에 넣었다. Unity 플레이어에 읽을 수 있는 파일로 포함되는 경로다. font importer의 includeFontData는 활성화되어 있다. 실제 새 Windows 빌드의 패키지 포함 결과는 아직 확인하지 않았다.

## 검사 결과

| 검사 | 결과 |
| --- | --- |
| Unity import·컴파일 | 원본 프로젝트에서 정적 TTF import와 C# 컴파일 성공. 이번 작업 로그에서 C# 오류·관련 예외 없음 |
| 적용 검사 | 제목 23개, 비대상 Text 894개·배치 보존, 원본 해시 5개 일치, 글리프·Outline·UI 참조 검사 통과 |
| 명령 | UI authoring 안전 검사 59개, 실제 Prism bridge 42개, hotbar 소유권 17개 통과 |
| 직접 Play | 격리 프로필에서 메인 → 방 만들기 → 혼자 시작 → 팔라딘 → 기원 선택 → 대기실. 변경된 제목과 실제 보스명 표시, Q 입력 후 쿨타임 확인 |
| 씬 보존 | 기존 비내장 문서 1,166개 중 1,164개 동일. 이전 HUD 비활성화·새 HUD root 등록, 새 instance 1개 추가. Texture 8개·Sprite 9개 내용 동일. 기존 문서 삭제 없음 |

이전 비활성 HUD에는 Unity가 `m_SizeDelta.y=0` override도 추가했다. 해당 RectTransform의 원본 값도 0이며 기존 씬에는 별도 Y override가 없었다. 정확한 대상·원본 값을 독립적으로 확인해 표시 변화가 없는 추가 항목으로 감사에 기록했다. 새 HUD instance 설정은 GUID·이름을 제외하면 이전 instance와 동일하다. 게임플레이·월드·컷씬 문서 변경은 없다.

## 증거와 미검증

- [적용 결과·제목별 경로·원본 해시](evidence/maruburi-number-20261116/maruburi-application-report.json)
- [씬 읽기 전용 감사](evidence/maruburi-number-20261116/scene-preservation-audit.json)
- [에디터 메뉴](evidence/maruburi-number-20261116/maruburi-editor-menu.png)
- [실제 Play 기원 선택](evidence/maruburi-number-20261116/maruburi-play-role.png)
- [실제 Play 대기실·Q 쿨타임](evidence/maruburi-number-20261116/maruburi-play-q.png)

새 숫자 후보는 WPF에서 실제 파일로 비교한 상태이며 Unity import·적용·검증을 하지 않았다. 전체 메뉴·모든 보스·모든 해상도 표시, 실제 멀티플레이와 새 Windows 빌드는 이번 확인 범위에 포함하지 않는다.
