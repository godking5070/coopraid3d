# Prism V4 메뉴·보조 UI 가독성 정리

사용자의 “나머지 UI도 손봐 달라”는 요청에 따라 직업·기원 선택, 설정, 전투 결과, 보상, 툴팁과 보조 화면의 글자·간격을 정리했다. 현재 활성 복사본은 `Assets/UI/AstellaPrismV4/MenuPolish/Applied-20261116-220047-376/`이다. 기존 Prism 은색·보라색 재질과 게임 아이콘을 사용하며 새 아이콘을 제작하지 않았다.

사용자가 확정한 제목·이름의 얇은 마루부리 효과, 숫자만 두꺼운 아웃라인, 오른쪽 `여명의 성광` 표시, 왼쪽 고정 `HP` 글자 숨김을 유지했다. 제목은 MaruBuri SemiBold와 얇은 남보라 아웃라인, 설명·설정은 Pretendard Medium, 순수 숫자는 Oxanium Medium과 더 두꺼운 아웃라인을 사용한다. 한글이 섞인 본문을 숫자 폰트로 바꾸지 않는다. 기존 전투 HUD 숫자 18곳의 강화 효과와 주 자원·궁극기 데이터 연결은 보존한다.

## 적용 범위와 보존

원본 Unity의 Editor API로 다음 12개 자산을 복사해 연결했다. 씬·프리팹 YAML을 직접 편집하지 않았으며 이전 씬 HUD는 삭제하지 않고 비활성화했다.

- `RaidHUD.prefab`
- `View-character`, `View-origin`, `View-metrics`, `View-resurrection`, `View-settings`, `View-connection`, `View-wipe`, `View-skill-reward` 프리팹
- `BuildStation.prefab`, `Template-FormButton.prefab`, `Template-FormText.prefab`

이전 기준은 `3d9ebb5d793812198539a5b9b78be5aa40abc54e`다. [적용 기록](C:/Astella/remaining-ui-polish-20261116/application-report.json)은 12개 자산의 보존 검사를 통과했다. [원본 SHA256 감사](C:/Astella/remaining-ui-polish-20261116/source-preservation-audit.json)는 12개 원본의 적용 전·후·재계산 해시가 모두 같고, 기존 자산과 `.meta` 내용이 기준 커밋과 같음을 확인했다. 원본 HUD SHA256은 `caa2b4e637a77b48f62d9a9585ffd758aa927476d78a9539cb7c678344984e03`이다.

[씬 보존 감사](C:/Astella/remaining-ui-polish-20261116/scene-preservation-audit.json)는 이전 HUD 비활성화와 새 HUD 인스턴스 등록만 허용된 변경으로 확인했다. 기존 게임플레이·월드·컷씬 문서를 제거하거나 바꾸지 않았다. Unity 저장 과정에서 일부 내장 Sprite/Texture의 로컬 ID가 재생성됐지만 8개 Texture와 9개 Sprite의 실제 내용은 동일했다. 기존 에셋 GUID를 교체하지 않았다.

| 대상 | 변경 |
| --- | --- |
| 직업·기원 선택 | 12직업 카드를 264×132로 넓히고 설명을 정리했다. 팔라딘 기원 3카드는 350×390으로 확장해 제목·본문·확정 버튼 간격을 확보했다. |
| 설정 | 기존 액션·컨트롤 참조와 순서를 유지하며 `HUDLayout`, 이동 방식, `Interact` 행을 저작된 레지스트리에 추가했다. 화면·사운드·조작·카메라 탭과 하단 버튼의 가독성을 정리했다. Slider 손잡이는 기존 가로 이동 앵커 계약을 유지하며 실제 표시 크기 14×28로 보정했다. |
| 전투 결과 | 패널 높이를 640으로 늘려 이벤트와 닫기 버튼 간격을 확보했다. 수치 열은 숫자 폰트, 헤더·플레이어명은 한글 폰트를 사용한다. 런타임 Stage/전체 런 탭은 기존 저작 FormButton 템플릿으로 생성한다. |
| 보상 | 카드 제목·설명과 확정 스킬 보상 문단을 정리했다. 열린 카드 보상은 다른 모달이 덮고 있지 않을 때만 `RaidUIOrder.Raise`로 전투 HUD 앞에 유지한다. 네트워크·복원 경로도 같은 열린 상태 처리를 사용한다. |
| 툴팁·보조 화면 | 툴팁 본문은 왼쪽 정렬과 좌우 22·상하 32 여백을 사용한다. 실제 프레임은 112 높이이므로 150 높이 루트의 상단에 글자가 닿던 문제를 보정했다. 전멸·사망 관전·빌드 스테이션은 기존 동작과 위치를 유지하며 글자를 정리했다. |

빌드 스테이션의 알려진 고정 제목에서 폰트에 없는 장식 `✦` 두 개만 제거했다. 기존 기호·아이콘 대체 폰트는 보존한다. 세 폰트는 수정하지 않은 원본 파일이며 OFL 고지·전문을 `Assets/StreamingAssets/ThirdPartyLicenses/`에 포함한다. 출처와 배포 조건은 [폰트 README](../../Assets/UI/AstellaPrismV4/Typography/Fonts/README.md)에 있다.

Git에 TTF가 `text eol=lf`로 지정돼 바이너리 내부 줄바꿈 바이트가 변환되는 문제도 발견했다. `*.ttf -text` 예외를 추가하고 세 폰트를 현재 정상 원본 바이트로 다시 저장했다. MaruBuri·Oxanium의 로컬 파일과 기존 metadata는 수정하지 않았고, 기존 Git blob에 있던 변환만 복구했다. 검토 시 Git blob과 로컬 원본의 `git hash-object --no-filters`가 일치하는지 확인한다.

## 검사와 실제 Play

[최종 UI 명령 검사](C:/Astella/remaining-ui-polish-20261116/final-ui-checks/results.json)는 선택 6개·완료 6개, 실패 0개다. UI 저작 안전성, 파티봇 표시, Prism 브리지, HUD 배치, 로비 연습, 빌드 스테이션을 검사했다. 마지막 Slider·툴팁·보상 레이어 보정 뒤 모달 생명주기 검사도 27개 통과했다. 원본 Unity 런타임·Editor 컴파일을 완료하고 Play에 다시 진입했다.

[변경 전 카탈로그](C:/Astella/remaining-ui-polish-20261116/before/catalog-preview.json)와 [변경 후 카탈로그](C:/Astella/remaining-ui-polish-20261116/after/catalog-preview.json)는 각각 1920×1080 실제 GPU 렌더 9/9개 성공, 오류 0개다. 직업·스킬 설명은 실제 정의에서 가져온 디자인 샘플이며 결과·사망·전멸 문구 일부는 표시용 샘플이다. 이 카탈로그를 실제 보상·사망 전환 검증으로 집계하지 않는다. 두 렌더 모두 원래 활성 씬과 dirty 상태를 복원했다.

실제 Play는 원본 Unity에서 격리 프로필 `--raid-test-profile=training-removal-20261116`로 수행했다. 증거 폴더는 `C:/Astella/remaining-ui-polish-20261116/`이다.

| 실제 조작 | 관측한 결과와 한계 |
| --- | --- |
| 메인 메뉴 → 혼자 시작 → 직업·기원 선택 → 확정 | 12직업 목록과 팔라딘 3기원 표시·선택·확정 확인. 모든 직업의 전투 동작을 검증한 것은 아니다. |
| 설정 4탭·조작 목록 스크롤 | 화면·사운드·조작·카메라 전환, 이동 방식·Interact G 행과 스크롤 표시 확인. HUD 배치 창 열기·취소 확인. 키 재지정·저장 검증은 포함하지 않는다. |
| 이동기·R 툴팁 | 실제 스킬 설명과 한글 표시 확인. 최종 R 툴팁에서 글자가 프레임 안쪽 여백에 들어가는 것도 재확인했다. |
| Stage1 `Shift+F1` → 실제 카드 보상 | 기존 개발용 보스 처치 단축키로 보상 흐름 진입. 자연 전투 클리어나 밸런스 검증으로 집계하지 않는다. |
| 보상 중 F11 → STAGE1·전체 런 클릭 → Esc | 실제 기록 패널과 두 탭 전환·닫기 후 보상 복귀 확인. 최종 보정 후 같은 흐름을 재확인하고 Choice1로 카드 선택·보상 종료를 확인했다. |
| 별자리 조율대 | 실제 G 상호작용으로 전투 준비 패널을 열고 직업·스킬 아이콘·스테이지 표시와 닫기 버튼 동작을 확인했다. 전체 선택 조합 검증은 아니다. |

## 최종 보정 재확인

- [상세 보정 보고서](C:/Astella/remaining-ui-polish-20261116/detail-refinement.json): 복사본 HUD·settings만 수정했고, 87,566개 보존 계약 검사와 전투 HUD 전체 스냅샷 비교를 통과했다. 최초 12개 복사본 적용 검사는 103,981개다. 원본 12개와 씬 보존 감사도 마지막 보정 후 재확인했다.
- [설정 Slider](C:/Astella/remaining-ui-polish-20261116/final-02-sliders.png), [드래그 후](C:/Astella/remaining-ui-polish-20261116/final-03-slider-drag.png): 밝은 은보라 손잡이·트랙이 표시되고 마우스 감도 0.16→0.41 드래그 뒤 크기가 유지된다. 취소 후 다시 연 화면은 0.16으로 복원됐다.
- [최종 R 툴팁](C:/Astella/remaining-ui-polish-20261116/final-06-tooltip-verified.png): 본문 네 줄과 상하 여백을 확인했다.
- [보상 레이어 수정 후](C:/Astella/remaining-ui-polish-20261116/final-12-reward-fixed.png): 오른쪽 카드 제목을 가리던 적 목록이 보상 뒤로 이동했다. [설정 중첩](C:/Astella/remaining-ui-polish-20261116/final-13-settings-over-reward.png)은 카드 위에 유지된다. [전체 런 결과](C:/Astella/remaining-ui-polish-20261116/final-14-results-total.png)를 닫으면 [보상으로 복귀](C:/Astella/remaining-ui-polish-20261116/final-15-return-reward.png)하고, [Choice1 선택 완료](C:/Astella/remaining-ui-polish-20261116/final-16-card-selected.png) 후 실제 보상이 종료된다.
- [조율대 실제 화면](C:/Astella/remaining-ui-polish-20261116/final-08-station.png)을 확인했다. 테스트 후 Play를 종료해 원본 Editor를 편집 상태로 남겼다. 테스트 프로필의 저장은 일반 사용자 저장과 구분하며, 이번 작업 시작 전 일반 저장의 해시 기준은 수집하지 않아 전후 해시 보존 검사로 보고하지 않는다.
- 브랜치는 `ingame-move-system`이다. 이 문서와 해당 구현·복사본을 같은 작업 커밋으로 보존하고 `origin/ingame-move-system`으로 push한다. 커밋·전송 결과는 완료 응답에 기록한다.

이번 적용본에서 실제 멀티플레이, 전체 직업·기원 전투, 키 바인딩 저장, 화면 크기 변경, 새 Windows 빌드, 실제 전멸·개인 사망·부활·확정 스킬 보상 전환은 아직 검증하지 않았다. 이전 적용본의 검증 결과를 이번 작업의 새 증거로 합치지 않는다.
