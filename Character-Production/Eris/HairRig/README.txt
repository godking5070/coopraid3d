Eris — Mixamo 전신 리그 + 머리카락 리그
2026-11-18

사용 파일
- Eris_HairRig.blend: Blender 편집용. 현재 Blender 세션에도 이 파일로 새로 저장했다.
- Eris_HairRig.fbx: 게임 엔진에 가져갈 캐릭터. 전신·머리카락 본과 텍스처를 함께 포함한다.
- verification.json: 실제 Blender 적용과 원본 보존, 빌더 검사 결과.
- independent-verification.json: 최종 편집 파일의 별도 수치 검사.
- fbx-roundtrip-verification.json: FBX를 독립된 폴더에서 다시 가져온 검사.
- Eris_HairRig.export.json: FBX 내장 이미지와 본 수, 파일 해시.

머리카락 구성
기존 Mixamo 본 52개에 머리카락 변형 본 22개를 추가했다. 총 74개다.
각 체인의 첫 본은 mixamorig:Head를 부모로 사용하며 이후 본은 순서대로 연결되어 있다.

Hair_Side.R_01 ~ 04 / Hair_Side.L_01 ~ 04: 양옆의 긴 머리
Hair_Back.R_01 ~ 04 / Hair_Back.L_01 ~ 04: 뒤쪽 머리
Hair_Front.R_01 ~ 03 / Hair_Front.L_01 ~ 03: 앞쪽의 두 컬

Hair 오브젝트의 10,447개 정점 웨이트를 다시 지정했다. 팔·몸통·다리 본의 기존 영향을
제거하고 머리 본과 새 머리카락 본만 사용한다. 정점당 최대 4개 본, 합계 1로 정규화했다.
두피·앞머리·머리 장식은 머리 본에 고정하고, 긴 부분은 체인과 두피의 경계를 부드럽게 연결했다.

Blender에서 확인
1. Eris_HairRig.blend를 열고 Armature를 선택한다.
2. Pose Mode에서 Armature의 Hair Controls 본 컬렉션을 사용한다.
3. Hair_로 시작하는 본을 선택해 R로 회전한다. 첫 본은 해당 머리카락 묶음 전체를 움직인다.
4. 검사한 본의 회전은 Alt+R로 해제하거나 Undo로 되돌린다.

Head 오브젝트는 이름과 달리 얼굴·몸·팔다리를 포함하는 전체 몸 메쉬다.
기존의 숨긴 원본 메쉬 두 개는 그대로 남겨 두었다. FBX에는 Armature, Head, Hair 세 개만 내보냈다.
원본 몸 메쉬·UV·전신 웨이트, Mixamo의 본 계층·레스트 자세와 기존 액션은 유지했다.

텍스처
.blend에는 기존 4096×4096 Base Color JPEG를 팩했고 FBX에도 같은 이미지 바이트를 내장했다.
기존 UV와 이미지 픽셀은 유지했다. 사용 중인 재질의 TEXTURE Mapping Z=inf가 실제 모델에서
이미지를 거의 단색으로 읽는 문제도 확인해, Head/Hair에만 Eris_Textured 재질 사본을 연결하고
Z를 1로 수정했다. 기존 재질은 Fake User를 설정해 원래 노드와 함께 보존했다.
Mixamo 최초 업로드용 정적 수정본은 ../Mixamo/에 별도로 있다.
이 폴더의 FBX는 Mixamo 리깅을 마친 이후의 추가 머리카락 리그 결과다.

검증과 범위
모든 머리카락 정점의 웨이트, 머리 회전, 각 머리 본의 변형, 팔·다리와 머리카락 사이의
영향 분리, 기존 전신 리그·액션 보존을 수치 검사했다. 앞/옆의 임시 회전 렌더도 확인했다.
FBX의 축·스케일·정점 위치·본·웨이트·UV·내장 이미지 복원은 별도 재가져오기 검사로 확인했다.
Unity의 실제 Humanoid Avatar 생성·게임 내 재생은 수행하지 않았다.
자동 흔들림·물리 시뮬레이션·충돌 설정은 추가하지 않았다. 현재는 수동 포즈 또는 게임 엔진의
추가 흔들림 제어에 연결할 수 있는 변형 본과 스키닝이다.

원본 보존
C:/Astella/Astella/modeling/Eris.blend와 eris.fbx는 덮어쓰지 않았다.
작업 직전 Blender의 미저장 상태를 .utmp/hair-rig-audit/에 별도 백업했다.

재생성
build_hair_rig.py는 Eris의 머리카락 토폴로지와 크기에 맞춘 전용 도구다. Blender와 NumPy를 사용한다.
수정 전 Mixamo 리깅 파일을 별도 Blender 프로세스에서 열고 다음처럼 실행한다.
  blender --background --factory-startup --disable-autoexec --python-exit-code 1 INPUT.blend --python build_hair_rig.py -- --output NEW.blend --report NEW-report.json
기존 출력 파일이나 이미 Hair_ 본이 있는 씬에는 중복 적용하지 않는다.
라이브 씬에 apply_hair_rig()를 부르면 현재 씬의 머리카락 본·웨이트를 바꾼다.
출력 경로를 지정하지 않으면 저장하지 않으며 pack_images=False가 기본이다.
새 경로를 지정한 경우에만 출력 사본을 저장한다. 적용 중 오류에 대비해 먼저 사본과 Undo 체크포인트를 만든다.
이 빌더는 머리카락 작업만 수행한다. 텍스처 Mapping 수정은 ../repair_texture_mapping.py의
repair_texture_mapping()을 따로 호출하거나, CLI에서 --output 새파일.blend를 전달한다.

FBX 재내보내기는 ../export_character_fbx.py를 사용한다. 완료된 .blend를 별도 프로세스에서 열고
--output 새파일.fbx를 전달한다. 기존 파일은 덮어쓰지 않으며 Add Leaf Bones와 Bake Animation을 끈다.
