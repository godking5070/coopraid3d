# 보스 등장·사망 애니메이션 연결

현재 예제에는 카메라 애니메이션과 보스 표시/숨김 시점만 들어 있다. 보스별 등장·사망 모션 클립은 아직 없으므로, 원하는 클립을 Unity 원본 프로젝트에서 만든 뒤 아래 방법 중 하나로 연결한다. 카메라 컷씬과 전투·보상 전환은 기존 시스템이 계속 관리한다.

## 가장 쉬운 예제 편집 순서

1. 원본 Unity에서 `Assets/Cutscenes/Examples/CutscenePreview.unity`를 연다. Play Mode는 끈다.
2. Hierarchy의 `Arrival preview`를 선택하고 `Tools > RaidRogue > Cutscenes > Set Up Boss Motion for Active Preview`를 **한 번** 실행한다. `Defeat preview`를 선택해 같은 명령을 한 번 더 실행한다.
3. 이 명령은 각 Timeline에 `Boss Motion` 트랙과 아주 단순한 크기 변화 예제 클립을 만들고, 프리뷰 더미 보스와 실제 컷씬 프리팹의 `BossAnimator` 바인딩을 연결한다. 기존 카메라 트랙·클립은 바꾸지 않는다. 이미 있는 `Boss Motion` 클립도 덮어쓰지 않는다.
4. Timeline에서 `Boss Motion` 클립을 원하는 보스 모션으로 교체하거나 예제 클립의 키를 수정한다. 입장 모션은 `RevealBoss` 표시 시점 이후에 배치한다. 작업 후 프리뷰 씬과 에셋을 저장한다.
5. Stage 1 진입과 처치를 각각 Play Mode에서 확인한다. 예제 크기 변화는 연결 시험용이며 고유 보스 애니메이션이 아니다.

### 보스 모션 키를 직접 찍기

1. Hierarchy에서 편집할 `Arrival preview`를 선택한다. 처치를 편집할 때는 `Arrival preview`를 끄고 `Defeat preview`를 켠 다음 선택한다.
2. `Tools > RaidRogue > Cutscenes > Prepare Direct Boss .anim Editing in Preview`를 실행한다. 더미 보스가 자동 선택되고, 선택한 Timeline의 `Boss Motion` 클립을 직접 편집할 준비가 된다.
3. `Window > Animation > Animation`에서 클립이 `ArrivalExampleBoss` 또는 `DefeatExampleBoss`인지 확인한다. 원하는 **클립 내부 시각**을 클릭하고 빨간 녹화 버튼을 켠다. 예제 입장 보스 클립은 Timeline의 1초에 시작하므로, Animation 창의 0초가 전체 컷씬의 1초(`RevealBoss`)다.
4. Scene 뷰에서 **더미 보스 오브젝트 자체**의 크기(`R`)나 회전(`E`)을 바꾼다. Scene 뷰의 관찰 카메라를 움직여서는 키가 생기지 않는다. 시간이 다른 위치로 이동해 다른 자세를 만들면 각각 키가 기록된다.
5. 녹화를 끄고 저장한다. Timeline 재생과 Game 뷰에서 확인한다. 프리뷰 더미 보스는 실제 보스 모델이 아니므로, 최종 모델의 고유 부위 모션은 해당 모델의 애니메이션 클립으로 교체해야 한다.

녹화 버튼이 회색이면 Timeline Preview/녹화를 끄고 2번 메뉴를 다시 실행한다. 기존 카메라 `.anim`과 보스 `.anim`은 별개이며, 각각 선택한 오브젝트에서 편집한다.

다른 스테이지용 컷씬을 만들 때는 입장·처치 프리팹과 Timeline을 복제하고, 해당 스테이지 설정에 연결한다. 실행 명령은 현재 선택한 예제 프리뷰에만 적용하며 정규 전투 저장은 건드리지 않는다.

## 간단한 방법: 보스 Animator 트리거

1. 보스용 Animator Controller에 등장과 사망 상태 및 각각의 Trigger 파라미터를 만든다. 상태에 제작한 클립을 배치한다.
2. `Assets/Resources/UI/RaidLobbyLayout.asset`의 해당 Stage `Boss Arrivals`에서 `Boss Animator Controller`와 `Arrival Animation Trigger`를 설정한다.
3. 같은 Stage의 `Boss Defeats`에서 `Death Animation Trigger`를 설정한다. 등장과 사망은 같은 Controller를 사용한다.
4. 등장 트리거는 보스 표시 시점(`RevealBoss` 마커 또는 `Reveal Seconds`)에, 사망 트리거는 호스트가 처치를 확정한 뒤 재생된다. 실제 트리거가 없으면 경고가 기록되며 사망 때는 기존 임시 쓰러짐 연출을 사용한다.

Animator가 보스의 3D 외형에 없으면 런타임에 `[Visual_3D]`에 만든다. 실제 보스 모델에 Animator가 이미 있으면 그 컴포넌트를 사용한다. 예제 보스는 임시 실린더이며, 모델 교체나 고유 클립 제작은 별도 작업이다.

## Timeline에 정확히 맞추는 방법

1. 입장/처치 Timeline에 Animation Track을 추가하고 보스 모션 클립을 놓는다. 입장 모션은 `RevealBoss` 마커 이후부터 보이도록 배치한다.
2. 예제 프리뷰 씬에서 트랙을 더미 보스의 Animator에 바인딩해 미리 본다. 새로 프리뷰 씬을 생성할 때 더미에 Animator가 자동으로 붙는다. 이미 만든 프리뷰 씬에는 Unity 에디터에서 더미 보스에 Animator를 추가하면 된다.
3. 실제 컷씬 프리팹의 `Raid Stage Cutscene Presentation > Bindings`에 같은 트랙을 추가하고 대상은 `BossAnimator`로 지정한다. 프리뷰 씬의 바인딩만으로는 런타임 보스에 연결되지 않는다.
4. Timeline 보스 트랙이 정상 재생되면 트리거보다 우선하며, 처치 때 임시 쓰러짐 연출도 실행하지 않는다. 트랙 바인딩/리소스가 실패하면 기존 컷씬 폴백과 쓰러짐 연출을 사용한다.

컷씬은 보스 위치를 기준으로 생성된다. 카메라나 보스 모션의 위치를 조정할 때 프리뷰의 더미 보스 원점을 기준으로 작업한다. Stage 1만 현재 예제 컷씬 프리팹에 연결되어 있으며, Stage 2~6은 해당 Stage의 입장·처치 프리팹을 별도로 설정해야 한다. 씬·프리팹 YAML은 직접 수정하지 않는다.
