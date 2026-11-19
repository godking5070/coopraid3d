이리스(Iris) 캐릭터 제작 파일 — 기존 Eris 경로 유지

Texturing/Iris_Concept_Textured.blend
  사용자가 제공한 컨셉을 반영한 최신 텍스처·리깅·모션 작업본.
  따뜻한 크림색 머리와 분홍빛 끝부분, 아이보리 의상·네이비·금색 포인트를 표현한다.
  기존 UV의 4K PBR 맵과 Happy Idle / Happy Walk / Running을 사용한다.
  최신 작업본에는 양쪽 어깨 회전점과 상완·소매 가중치 보정이 적용됐다.
  텍스처·게임 엔진용 파일·검증 범위는 Texturing/README.txt를 참고한다.

Animation/Bright/Eris_Animated_Bright.blend
  밝고 활발한 성격의 대기·걷기·달리기를 적용한 텍스처 작업 전 단계의 보존본.
  Happy Idle / Happy Walk / Running. 기존 손목 소매와 등/옷깃 리깅 수정을 유지한다.
Animation/Bright/Exports/Eris_Idle.fbx, Eris_Walk.fbx, Eris_Run.fbx
  각 모션과 메시·머리카락 본·텍스처를 함께 포함한 게임 엔진용 FBX.

Animation/Eris_Animated.blend와 Animation/Exports/
  성격 확인 전에 만든 차분한 모션 초안 및 의상 리깅 수정 단계의 보존본.

HairRig/Eris_HairRig.blend
  Mixamo의 전신 리깅을 유지하고 머리카락 전용 본과 웨이트를 추가한 편집용 파일.
HairRig/Eris_HairRig.fbx
  위 리깅을 게임 엔진으로 가져가기 위한 FBX. 텍스처를 파일 안에 포함한다.

Mixamo/Eris_Mixamo_Textured.fbx
  Mixamo에 처음 올리는 단계의 정적 메쉬. 기존 eris.fbx에 누락된 JPEG를 내장한 별도 수정본.
  이 파일에는 Mixamo 전신 리그나 이후의 머리카락 리그가 없다.

제작 순서
  정적 모델 → Mixamo 전신 리깅 → Blender에서 머리카락 본·웨이트 추가 → 게임 엔진.
  현재 컨셉 텍스처·모션·리깅 수정까지 확인할 때는 Texturing 폴더의 최신 파일을 사용한다.
  텍스처 작업 전의 밝은 모션 단계는 Animation/Bright 폴더에 보존한다.
  머리카락 리깅만 추가한 이전 작업 단계는 HairRig 폴더에 보존한다.

각 폴더의 README와 검증 기록에서 작업 범위 및 확인한 항목을 확인한다.
원본 C:/Astella/Astella/modeling/Eris.blend와 eris.fbx는 보존한다.
