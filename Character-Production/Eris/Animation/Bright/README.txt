에리스 — 밝고 활발한 대기·걷기·달리기

2026-11-19 사용자가 확정한 성격은 “밝고 활발하게”입니다.
이 폴더가 현재 성격에 맞춘 최신 모션 작업본입니다.

열기: Eris_Animated_Bright.blend
기본 동작: Eris_Idle, 30 FPS. Space로 재생합니다.
상단 Layout 작업 공간에서 Armature를 선택한 다음 아래 Action Editor의
동작 목록에서 Eris_Idle / Eris_Walk / Eris_Run을 선택합니다.
동작을 바꾸면 재생 시작/끝 프레임도 다음 범위로 맞춥니다.

Eris_Idle: 1–56 (1.83초), Mixamo Happy Idle
  무릎과 상체에 가벼운 반동이 있는 생기 있는 대기.
Eris_Walk: 1–34 (1.10초), Mixamo Happy Walk
  경쾌한 보폭과 팔 흔들림이 있는 걷기.
Eris_Run: 1–39 (1.27초), Mixamo Running
  이전 조깅보다 팔·다리 움직임이 큰 힘찬 달리기.
프레임 수는 실제 motion-verification.json에서 확인한 값입니다.

이전 차분한 동작은 Eris_Idle_Calm / Eris_Walk_Calm / Eris_Run_Calm으로
같은 작업본에 보존되어 비교할 수 있습니다. 기존 상위 폴더의
Eris_Animated.blend와 Source/Exports 파일도 그대로 보존합니다.

내려받기와 적용
Source/의 파일은 로그인된 Mixamo ERIS 캐릭터에서 실제로 내려받았습니다.
FBX Binary / Without Skin / 30 FPS / Keyframe Reduction none.
Happy Idle: Overdrive 55. Happy Walk와 Running: Overdrive 50.
세 동작 모두 Character Arm-Space 60, Mirror 꺼짐, Trim 0–100.
걷기·달리기는 Mixamo In Place를 사용했습니다. 적용 시 세 동작 모두
시작 XY 위치를 맞추고 순이동을 제거했으며 마지막 4프레임을 반복 연결했습니다.
전체 선택 근거와 설정은 clips.json, 원본 SHA256과 적용 검사는
motion-verification.json을 참고합니다.

게임 엔진용 파일
Exports/Eris_Idle.fbx, Eris_Walk.fbx, Eris_Run.fbx:
  각각 현재 캐릭터 메시·내장 텍스처·74개 본·한 모션을 포함합니다.
  Add Leaf Bones 꺼짐, 매 프레임 베이크, 키 단순화 없음.
Source/의 Without Skin 원본과 구분해 사용합니다.

리깅과 확인 범위
손목 소매와 등/옷깃을 수정한 기존 리그의 가중치를 그대로 사용합니다.
몸·손·목·얼굴·머리카락 가중치, 본 74개의 기준 자세/계층,
메시·UV·마테리얼은 이 모션 교체에서 변경하지 않습니다.
머리카락 본 22개는 머리를 따라가며, 자동 흔들림/충돌 물리는 별도 작업입니다.
Unity 가져오기에는 상위 ../README.txt의 “Unity 가중치 설정 주의”가
그대로 적용됩니다. 실제 Unity 실행·Avatar·상태 전환은 미검증입니다.

검증 자료
build-verification.json: 작업본·원본 보존, 기존 동작 보관, 정적 리그와 텍스처 검사.
motion-verification.json: 실제 세 동작의 적용·반복·제자리·유한값 검사.
independent-verification.json: 새 작업본을 다시 열어 수행한 독립 보존/재생 검사.
Exports/roundtrip-verification.json: 별도 장면에서 최종 FBX를 다시 불러온 비교.
Preview/: 현재 동작의 포즈 확인 이미지.

재생성은 이미 의상 리깅을 수정한 작업본을 입력으로
build_bright_character.py --output 새경로.blend를 사용합니다.
리깅 패치를 다시 적용하지 않으며 기존 출력 파일은 덮어쓰지 않습니다.
