에리스 — 애니메이션 및 손·손목·등 의상 리깅 기록

최신 작업본: Bright/Eris_Animated_Bright.blend
2026-11-19 사용자가 확정한 “밝고 활발하게” 성격을 적용했습니다.
Happy Idle / Happy Walk / Running과 최신 프레임·사용법은 Bright/README.txt를 참고합니다.
게임 엔진용 최신 파일은 Bright/Exports/에 있습니다.
아래 동작·프레임·검사 기록은 이전 차분한 초안의 보존 기록입니다.
Bright 작업본 안에도 기존 동작이 *_Calm 이름으로 보존되어 있습니다.

열기: Eris_Animated.blend
기본 선택: Eris_Idle, 30 FPS. Space로 재생합니다.
상단 Layout 작업 공간을 선택한 다음 Armature를 선택하고 아래 Action Editor의 동작 목록에서
Eris_Idle / Eris_Walk / Eris_Run을 선택합니다.
클립을 바꿀 때 재생 끝 프레임도 아래 값으로 맞춥니다.

Eris_Idle: 1–432 (14.37초), Mixamo Idle
  작은 호흡과 체중 이동이 있는 편안한 대기.
Eris_Walk: 1–38 (1.23초), Mixamo Female Walk
  Speed 45, Stride 45. 보폭과 속도를 조금 줄인 걷기.
Eris_Run: 1–78 (2.57초), Mixamo Jogging
  공격적인 전력 질주보다 가볍고 부드러운 달리기.

이전 초안은 성격을 확인하기 전 컨셉 외형의 차분하고 부드러운 인상을
임시 기준으로 삼았습니다. 이후 사용자가 밝고 활발한 성격을 확정하여
현재 성격에 맞춘 새 모션은 Bright/에 별도 적용했습니다.

원본 내려받기
Source/의 FBX는 로그인된 Mixamo의 ERIS 캐릭터에서 직접 내려받은 원본입니다.
FBX Binary / Without Skin / 30 FPS / Keyframe Reduction none.
걷기·달리기는 Mixamo In Place를 사용했습니다. 세 클립 모두 적용 시 시작 XY를
기존 리그에 맞추고 순이동을 제거했으며 마지막 4프레임을 반복 연결했습니다.
세부 조절값은 clips.json, 실제 프레임·원본 SHA256은 motion-verification.json에 있습니다.

사용할 파일
Eris_Animated.blend: 수정된 의상 가중치, 기존 몸/머리카락 리그, 세 Action,
  내장 텍스처가 포함된 Blender 작업본.
Exports/Eris_Idle.fbx, Eris_Walk.fbx, Eris_Run.fbx:
  각각 캐릭터 메시·텍스처·74개 본·한 모션을 포함한 FBX.
  Add Leaf Bones 꺼짐, 매 프레임 베이크, 키 단순화 없음.
Source/의 Without Skin 원본과 최종 Exports/ 파일을 구분해 사용합니다.

리깅 점검 범위
양손 각 15개 손가락 본, 손목, 팔, Spine/Spine1/Spine2, 목/머리 계층을 검사했습니다.
의상 소매·커프스에 섞인 손가락 가중치와 등/어깨 옷깃에 섞인 머리 가중치를
해당 의상 메시의 연결 성분 안에서 수정했습니다.
실제 손 피부·목·얼굴 가중치, 기존 74개 본의 기준 자세/계층,
머리카락 가중치·형상·UV·텍스처는 보존합니다.
자세별 변형 검사와 수정 범위는 rig-verification.json을 참고합니다.

Unity 가중치 설정 주의
원본 몸 메시부터 정점당 최대 9개 본 가중치가 있고, 수정한 옷깃에는 최대 7개를 씁니다.
현재 작업본은 이 가중치를 보존합니다. 4개만 남기는 단순 절삭을 가상 검사했을 때
옷깃의 일부 짧은 변에서 변형이 커져, 최종 파일에 일괄 4개 제한을 적용하지 않았습니다.
가중치를 보존해 가져오려면 다음 설정을 확인합니다.
  Model Importer: Skin Weights = Custom, Max Bones/Vertex = 12 이상,
    Min Bone Weight = 0 (작은 가중치도 보존).
  Quality Settings: Skin Weights = Unlimited.
  SkinnedMeshRenderer: Quality = Auto.
이 설정을 원본 Unity 프로젝트에 자동 적용하지는 않았으며 실제 Unity 렌더링은 미검증입니다.
4개 제한이 반드시 필요한 플랫폼은 별도 제한용 의상 웨이트 조정이 필요합니다.
검사 비교: skin-influence-compatibility.json
Unity 공식 문서:
https://docs.unity.com/en-us/engine/6000.3/script-reference/unityeditor/modelimporterskinweights
https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/skinnedmeshrenderer/quality

머리카락 본 22개는 머리를 따라가며 그대로 편집 가능합니다.
Mixamo에는 추가 머리카락 본의 키가 없으므로 자동 흔들림/충돌 물리는 추가하지 않았습니다.
달리기에서 긴 머리나 옷을 물리적으로 흔들려면 별도 보조 애니메이션/물리가 필요합니다.

검증 자료
build-verification.json: 원본 보존과 최종 작업본 해시.
rig-verification.json: 의상 가중치 수정 및 스트레스 자세 검사.
motion-verification.json: 실제 세 클립 적용, 본/형상 보존, 반복/제자리 검사.
independent-verification.json: 저장 후 다시 연 작업본의 독립 보존 검사 14항목.
actual-motion-rig-verification.json: 세 클립 전체 548프레임과 9개 집중 확인 자세 비교.
  소매 안쪽 일부 짧은 변의 접힘은 원본에도 같은 크기로 존재합니다.
  비교한 집중 자세에서 새로 2배 이상 늘어난 의상 변은 없었습니다.
Exports/export-verification.json: 내보낸 이미지/파일 해시와 비교용 자세 표본.
Exports/roundtrip-verification.json: 새 장면에서 FBX를 재수입한 비교 결과.
Preview/: 최종 작업본을 CPU로 렌더링한 포즈 확인 이미지.
  rig-cuff-before/after: 엄지 60도 굽힘에서 소매 커프스가 끌려가는 현상 비교.
  rig-back-before/after: 고개 20도 회전에서 등 옷깃 비교. 관찰을 위해 머리카락은 숨겼습니다.
  위 리깅 비교 그림은 승인된 가중치 수정안의 스트레스 검사 장면입니다.

Blender에서의 변형 및 FBX 재수입 검사와 Unity 실행 검증은 별개입니다.
Unity Animator의 상태 전환, 이동 속도, Humanoid Avatar와 런타임 충돌은 미검증입니다.
이 작업에서는 Unity 씬/프리팹이나 기존 HairRig 원본을 덮어쓰지 않았습니다.

재생성
기존 HairRig/Eris_HairRig.blend 또는 보관한 현재 작업본을 입력으로 사용하고
Blender 5.3에서 build_animated_character.py --output 새경로.blend를 실행합니다.
기존 출력 경로는 덮어쓰지 않습니다. export_animations.py로 새 폴더에 FBX를 내보냅니다.
지원 스크립트는 에리스의 현재 정점 수·연결 성분을 확인하며 다른 모델에는 사용하지 않습니다.
