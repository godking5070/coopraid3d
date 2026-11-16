# 플레이어 바 캡션 정리 — 2026-11-16

사용자 요청에 따라 왼쪽 체력바의 고정 **HP 글자만 숨겼다.** 오른쪽 자원명 **여명의 성광**은 기존 위치·서체·표시·직업 데이터 연결을 유지했다. 체력 수치·채움, 자원 수치, 숫자 아웃라인과 이름·제목의 얇은 효과는 유지한다.

현재 검토용 HUD 복사본을 원본 Unity 에디터에서 API로 편집했다. 프리팹 변경은 `Health/Caption` GameObject의 `m_IsActive: 1 → 0` 한 곳뿐이다. 새 HUD 복사본이나 씬 인스턴스를 추가하지 않았고 기존 GUID·씬·profile 파일의 SHA256이 동일하다. 원본 프리팹과 `.meta`의 편집 전 백업은 `Logs/PlayerCaptionRefinement/Backup-*`에 있다. YAML 직접 편집은 하지 않았다.

모든 Text·RectTransform·효과·브리지 바인딩·다른 GameObject 활성 상태의 전후 스냅샷이 동일하다. 자원명은 기존 `(0,29)` 위치와 `jobSource.ResourceLabel` 연결을 유지하므로 직업에 따라 자원명이 바뀌는 정책도 유지한다. 숫자나 자원명을 고정 문자열로 덮어쓰지 않는다.

UI authoring 안전 검사 60개, 원본 Unity 컴파일과 보존 검사를 통과했다. 격리 프로필에서 직접 방 만들기 → 싱글 → 팔라딘·기원 선택 → 대기실 → Q 입력을 수행해, 자원명이 보이고 HP 캡션이 없으며 체력 `3,000`·자원 `0 / 100`·쿨타임 `30`이 표시되는 것을 확인했다. 관련 C# 오류·예외는 없었다.

- [실제 하단 표시](evidence/player-captions-20261116/captions-bottom.png): 전체 Play 화면의 해당 영역을 원본 크기로 잘랐다.
- [Play 전체 화면](evidence/player-captions-20261116/captions-play.png)
- [적용·참조·배치·효과 검사](evidence/player-captions-20261116/caption-refinement-report.json)
- [GUID·씬·profile 보존](evidence/player-captions-20261116/preservation.json)

명시적 메뉴는 `Tools/Astella/Refine Player Captions`와 `Tools/Astella/Validate Player Captions`다. 자동 실행 훅은 없다. 새 Windows 빌드와 다른 해상도·직업·멀티플레이는 이번 검사 범위 밖이다.
