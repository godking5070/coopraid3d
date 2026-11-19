이리스 — 사용자 제공 원화 기반 텍스처 작업본

열기: Iris_Concept_Textured.blend
원화와 전후 비교: Review/texture-comparison.html
원화: Reference/Iris_Concept_UserReference.png (2026-11-19 사용자 제공)

어깨 리깅 보정 (2026-11-19)
사용자가 표시한 어깨 관절에 쇄골 끝과 상완 시작점을 맞췄습니다.
양쪽에서 상완 시작점을 몸통 쪽으로 약 3.8cm, 위로 약 1.3cm 옮겼습니다.
상완과 전완의 회전을 다시 맞춰 손목과 손가락의 기존 궤적을 이어갑니다.
밝은 버전과 차분한 버전의 대기·걷기·달리기 6개 동작, 총 677프레임을 확인했습니다.
비교 페이지의 ‘어깨 회전점’에서 대기 36프레임의 전후를 나란히 볼 수 있습니다.
실제 쇄골·상완 본을 각 렌더 위에 표시했습니다. 색 점은 상완의 시작점입니다.
‘어깨 가중치’는 먼저 진행한 소매 가중치 수정의 전후입니다.

흰자위 보정 (2026-11-19)
눈꺼풀과 머리 그림자 아래에서 어두워지던 흰자위의 색과 밝기를 보정했습니다.
비교 페이지의 ‘눈 흰자위’에서 같은 조명의 대기 자세를 확대해 볼 수 있습니다.

아이보리 후드·남색 의상·금색 장식은 유지합니다.
원화의 따뜻한 크림 금발에서 연분홍 머리끝으로 이어지는 색감,
연한 하늘색 안감과 꽃 장식, 금장과 천의 재질 차이를 반영했습니다.
현재 메시의 형상과 얼굴·눈·별·장식 위치를 유지한 텍스처 작업입니다.

파일과 맵
Textures/Iris_Body_BaseColor.png / Roughness.png / Metallic.png / Normal.png
Textures/Iris_HairHood_BaseColor.png / Roughness.png / Metallic.png / Normal.png
  위 이름의 끝부분을 각 역할로 바꾼 4096×4096 PNG입니다.
  BaseColor는 sRGB, 나머지는 Non-Color 데이터입니다.
  Normal은 Blender의 Tangent 공간 노멀 맵입니다.
  HairHood는 머리카락뿐 아니라 후드·꽃·리본도 포함한 기존 Hair 메시용입니다.
Textures/Iris_Body_Emission.png
  흰자위에만 사용하는 sRGB 4096×4096 마스크입니다. 몸의 Emission Color와 연결됩니다.
Textures/Fabric_MicroWeave.png
  imagegen 기본 내장 도구로 생성한 미세 원단 질감 입력입니다.
  최종 캐릭터 맵은 이를 사용해 Blender에서 기존 UV에 굽습니다.
  생성 프롬프트와 방식은 imagegen-prompt.txt에 기록했습니다.

새 PNG 맵은 Blender 작업본에 내장되어 있습니다.
외부 PNG도 함께 보관해 엔진에서 재질을 연결하거나 후속 편집에 사용할 수 있습니다.
색·강도·재질 기준값은 texture-spec.json에 있습니다.
거칠기 맵은 Roughness 값이며, Smoothness 맵과 구분해 사용합니다.

모션 확인
상단 Layout에서 Armature를 선택하고 아래 Action Editor의 동작을 선택합니다.
Eris_Idle: 1–56 / Eris_Walk: 1–34 / Eris_Run: 1–39, 30 FPS.
기본 동작은 밝고 활발한 Happy Idle입니다. Space로 재생합니다.
이전 *_Calm 동작과 기존 T-Pose Action도 보존합니다.
기존 파일명의 Eris와 리그·Action 이름은 이전 작업과의 연결을 위해 유지합니다.

리깅
74개 본으로 몸과 머리카락이 움직입니다. 손목 소매와 등 옷깃에 이어 양쪽 어깨의
회전점과 윗소매 가중치를 보정했습니다.
수천 개로 조각난 기존 UV를 AI로 재배치하지 않고, 새 재질을 같은 UV에 굽습니다.
머리카락과 후드가 같은 메시이므로 머리카락 영역만 분홍 그라데이션을 적용합니다.

게임 엔진용
Exports/Eris_Idle.fbx, Eris_Walk.fbx, Eris_Run.fbx:
  새 텍스처와 기존 74개 본·캐릭터 메시·각 모션을 담은 별도 FBX입니다.
  Add Leaf Bones 꺼짐, 매 프레임 베이크, 키 단순화 없음.
  금속·거칠기·노멀·흰자위 발광 맵의 엔진별 재질 연결은 별도 확인이 필요합니다.
Unity 가중치 설정은 ../Animation/README.txt의 해당 안내를 따릅니다.
실제 Unity 실행·Avatar·전환·재질 렌더링은 미검증이며 머리카락 물리는 별도입니다.

검증 자료
texture-build-verification.json과 independent-verification.json은 컨셉 텍스처를 적용한 단계의 기록입니다.
color-verification.json에 표면 UV의 색과 금속성 검사 결과가 있습니다.
shoulder-repair-verification.json은 첫 윗소매 가중치 보정의 기록입니다.
shoulder-joint-verification.json은 관절 위치와 모션을 검증한 기록입니다.
eye-material-verification.json에 최신 작업본의 흰자위 보정과 리깅 보존 검사 결과가 있습니다.
Exports/export-verification.json / roundtrip-verification.json: FBX 내보내기·재수입 비교.
Exports/pbr-verification.json: 각 FBX에 내장된 4K 맵 9개와 역할·색 공간 연결 검사.
Review/before-* / after-*: 같은 자세·조명·카메라로 렌더링한 비교 이미지.

재생성
이미 리깅·모션을 완료한 Animation/Bright/Eris_Animated_Bright.blend를 입력으로,
build_textured_character.py --manifest texture-spec.json --output 새경로.blend --mode bake.
먼저 --mode preview로 임시 원본 크기 재질을 확인할 수 있습니다.
완료된 새 파일을 prepare_textured_view.py --report texture-build-verification.json으로
열기 좋게 정리할 수 있습니다. 이 단계는 새 출력의 해시를 확인한 뒤 뷰 설정만 저장합니다.
텍스처를 다시 만든 파일에 repair_shoulder_weights.py --output 보정본.blend를 실행하면
어깨 가중치 보정을 적용한 새 작업본이 생성됩니다.
그 작업본에 align_shoulder_joints.py --output 관절보정본.blend를 실행하면 관절 위치와 팔 동작을 맞춥니다.
관절 보정본에 restore_eye_whites.py --output 눈보정본.blend를 실행하면 흰자위 맵을 추가합니다.
기존 출력 파일·맵을 덮어쓰지 않으므로 새 출력 폴더를 사용합니다.
