# 1부 스테이지 컷씬 제작

원본 Unity 프로젝트 `C:/coopraid/coopraid3d`에서 편집한다. 작업용 worktree는 Unity로 열지 않는다. Timeline 1.8.13은 이미 설치되어 있다.

## 예제와 Stage 1 연결

1. Play Mode를 끈 뒤 `Tools > RaidRogue > Cutscenes > Create Editable Examples`를 실행한다. 생성 위치는 `Assets/Cutscenes/Examples/`다. 이미 있는 예제는 덮어쓰지 않는다.
2. `ArrivalExample.prefab`·`ArrivalExample.playable`·`ArrivalExampleCamera.anim`이 입장 예제이며, 같은 이름의 `Defeat...` 3개가 처치 예제다. 세 파일을 함께 복제해 다른 스테이지용 이름으로 바꾼다. 복제한 프리팹의 PlayableDirector가 복제한 Timeline을 가리키고, RaidStageCutscenePresentation의 Bindings가 그 Timeline의 트랙을 가리키는지 확인한다.
3. `Tools > RaidRogue > Cutscenes > Connect Examples to Stage 1 (Empty Slots Only)`를 실행하면 `Assets/Resources/UI/RaidLobbyLayout.asset`의 Stage 1 빈 입장·처치 칸만 연결한다. 이미 지정된 칸은 유지된다. 기존 UI 프리팹은 변경하지 않는다.
4. 그 뒤부터는 `RaidLobbyLayout.asset`의 `Boss Arrivals`와 `Boss Defeats` 배열에서 Stage 1의 `Presentation Prefab`을 직접 교체한다. Stage 2~6에는 스테이지 번호가 다른 배열 요소를 추가해 같은 프리팹을 재사용하거나 복제본을 연결한다. 공유 원본을 스테이지별로 다르게 편집하려면 먼저 복제한다.

## Timeline 편집

### 기존 `.anim`에 바로 녹화

1. Play Mode를 끄고 편집 중인 에셋을 저장한 뒤 `Assets/Cutscenes/Examples/CutscenePreview.unity`를 연다. 기본적으로 활성화된 `Arrival preview`가 선택 대상이다. `Defeat preview`를 편집하려면 Arrival을 끄고 Defeat을 켠다.
2. `Tools > RaidRogue > Cutscenes > Prepare Direct .anim Editing in Preview`를 한 번 실행하고 씬을 저장한다. Hierarchy에서 프리뷰 인스턴스를 선택하지 않았어도 활성화된 프리뷰 하나를 자동으로 찾는다. 이 명령은 미리보기 카메라에 기존 `.anim`을 참조하는 Animator Controller를 연결한다. 런타임 컷씬 프리팹과 `.anim` 키는 바꾸지 않는다.
3. Timeline 창의 **Preview**와 녹화를 끈다. Hierarchy에서 `Presentation Camera`를 선택하고 `Window > Animation > Animation`을 연다. Animation 창 상단 클립이 `ArrivalExampleCamera` 또는 `DefeatExampleCamera`인지 확인한다.
4. Animation 창의 빨간 녹화 버튼을 켜고 원하는 시각으로 이동한다. Scene 창에서 선택된 카메라를 `W`로 이동하거나 `E`로 회전하면 **그 `.anim`에 키가 바로 기록된다**. Game 창에서 구도를 확인한다. 끝나면 녹화를 끄고 저장한다.

Game 창이 움직이지 않으면 Timeline Preview가 꺼져 있는지, 선택한 `Presentation Camera`의 Camera 컴포넌트가 켜져 있는지, 활성화된 입장/처치 프리뷰가 하나뿐인지 확인한다. 예제 컷씬을 여러 스테이지가 공유하면 `.anim` 수정은 모두에 적용된다. 독립 연출은 세트 전체를 먼저 복제한다.

- 프리팹을 열고 `PlayableDirector`의 Timeline을 연다 (`Window > Sequencing > Timeline`). 예제의 `Presentation Camera Motion` 트랙은 프리팹 안의 `Presentation Camera` Animator에 바인딩된다. 카메라 이동은 `.anim` 클립의 로컬 위치 키프레임, 화각은 카메라 컴포넌트에서 편집한다. 회전과 화각도 키프레임으로 만들 수 있다.
- 프리팹 루트는 런타임 보스 위치를 원점으로 삼는다. 카메라의 로컬 좌표로 연출한다. 월드 절대 좌표를 키프레임에 굳히지 않는다. 파티 배치와 보스 위치는 `RaidLobbyLayout.asset`의 해당 Stage `Party Center`, `Boss Position`, 거리·간격에서 조절한다.
- Timeline의 Marker Track에 있는 `RaidCutsceneVisualMarker`를 옮겨 입장 시 보스 공개 (`RevealBoss`) 또는 처치 시 숨김 (`HideBoss`) 시점을 조절한다. 마커가 없으면 스테이지 설정의 `Reveal Seconds`/`Departure Seconds`를 쓴다. Timeline 길이는 마지막 클립 길이이며 0.5~120초를 지원한다. `After Cutscene Seconds`는 입장 연출 뒤 전투 카운트다운 전 대기다.
- 보스 애니메이션은 Timeline에 Animation Track을 추가하고, 프리팹의 `RaidStageCutscenePresentation > Bindings`에 해당 Track Asset과 `BossAnimator`를 명시적으로 연결한다. 런타임 보스 Animator가 없다면 기본 연출로 진행한다. 기존 `Arrival Animation Trigger`/`Death Animation Trigger`도 보스 Animator에 남아 있는 기본 경로다.
- 이펙트·효과음·제목·자막은 컷씬 프리팹의 자식 오브젝트에 넣고 Timeline 트랙과 필요한 프리팹 내부 컴포넌트를 명시적으로 바인딩한다. 전투 HUD 자체를 컷씬 프리팹에 복제하지 않는다. 재생 중 생성되는 연출 객체는 프리팹 인스턴스와 함께 정리된다.
- `Tools > RaidRogue > Cutscenes > Validate Stage Bindings`는 지정된 프리팹의 필수 Director·Camera·Timeline 누락을 확인한다. 비어 있는 스테이지 칸은 기본 카메라 연출을 사용한다.

## 미리보기와 플레이 확인

`Tools > RaidRogue > Cutscenes > Create Isolated Preview Scene`을 실행하면 `Assets/Cutscenes/Examples/CutscenePreview.unity`가 생성된다. 기존 미리보기 씬은 덮어쓰지 않는다. 이 씬에는 더미 보스, 바닥, 입장·처치 프리팹 인스턴스만 있으며 StageManager가 없다. 원하는 프리팹만 활성화하고 PlayableDirector를 선택해 Timeline 창의 미리보기로 클립을 스크럽한다. Scene 뷰에서 프리팹 카메라 프리뷰를 확인한다. 이 과정에서는 저장·보상·전투가 실행되지 않는다. 실제 입장·처치 순서와 보스 표시 시점은 원본 프로젝트 Play Mode에서 1명+AI 3명으로 확인한다.

확인 순서: Stage 1 북쪽 문 출발 → 진입로에서 보스 비표시 → 필드 진입·파티 소환 → 입장 Timeline·마커 → 전투 → 보스 처치 → 처치 Timeline·마커 → 보상·포탈. 한 번은 정상 종료, 한 번은 파티 공통 건너뛰기를 사용한다. 이후 재도전과 중간 종료·이어하기에서 카메라·입력·보상 중복을 확인한다. 편집한 카메라·클립 설정은 자동 생성 코드가 재지정하지 않는다.

컷씬은 시각·청각 연출과 공개/숨김 시각만 지정한다. 파티 소환, 입력 잠금, 보스 피해·AI, 전투 시작, 처치 확정, 저장, 보상, 포탈은 기존 호스트 권한 코드가 관리한다. Timeline 신호가 없어도 단계는 호스트 시간에 따라 끝난다. 리소스나 바인딩이 빠지면 기존 고정 카메라 연출로 진행한다. 멀티플레이는 `Time.timeScale`로 정지하지 않는다.
