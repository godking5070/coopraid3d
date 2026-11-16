# Prism V4 메뉴·보조 HUD 전달본

승인된 페인팅 Prism의 은빛 금속·보라색 바탕을 메뉴와 보조 UI로 확장했다. 새 아이콘은 포함하지 않는다. 4개 표면 PNG는 기존 슬롯의 페인팅 바탕/프레임을 손실 없이 복사했으며 독립 GUID와 32px Sprite Border를 가진다. Unity Image는 Sliced, pixelsPerUnitMultiplier=2로 사용한다.

실제 적용은 원본 프로젝트의 ingame-move-system 브랜치에 있다. 활성 경로: `Assets/UI/AstellaPrismV4/AppliedRemaining/Applied-20261116-172147-686/RaidHUD.prefab`. 메인 HUD, 뷰 10개, 반복 템플릿 7개, 구성대 1개를 개별 prefab 사본으로 제작했고 기존 원본은 유지했다. 프로필의 theme/buildStationPrefab과 HUD/뷰/템플릿 참조가 새 사본을 가리킨다. 기존 45개 전투 HUD PNG와 게이지 형상은 유지했다.

- manifest.json: 표면별 원본·SHA256·사용 설정
- screenshots/: 1920×1080 실제 Play 캡처와 별도 카탈로그 디자인 렌더
- authoring.json: 최초 적용 및 원본 보존 결과
- ui-checks.json: 최종 UI 검사 6개 결과
- catalog-preview.json: 디자인 렌더 9개 및 검증 경계

실제 Play로 홈/설정 탭/직업·기원 선택·확정·로비·HUD 배치 편집 취소·난이도 표면을 확인했다. design- 접두 이미지는 상태 전환을 실행하지 않은 GPU 디자인 렌더이며 일부 빈 텍스트는 샘플이다. 실제 보상·부활·전멸·결과 상태 전환, 전체 직업, 다인 Steam, Windows 재빌드는 이번 작업에서 검증하지 않았다. 상세 원본 프로젝트 문서는 docs/part1/PRISM_V4_REMAINING_UI_2026-11-16.md에 있다.
