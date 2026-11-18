Eris — Mixamo용 텍스처 내장 FBX
2026-11-18

사용할 파일: Eris_Mixamo_Textured.fbx
Mixamo의 Upload Character에서 이 파일을 선택한다.
FBX 자체에 Base Color JPEG가 들어 있으므로 옆의 JPG를 별도로 업로드할 필요는 없다.
Eris_BaseColor.jpg는 재질 재연결과 후속 텍스처 작업을 위한 별도 사본이다.

확인한 원인
- C:/Astella/Astella/modeling/eris.fbx에는 텍스처 연결과 UV가 있지만 이미지 본문이 없었다.
- Video.Content가 없는 외부 파일 경로만 기록되어 다른 PC나 Mixamo에서는 원본 이미지를 읽을 수 없었다.
- 이 수정본은 업로드용 원본의 이미지 데이터를 FBX에 내장한 별도 파일이다.
- 기존 .blend와 FBX, 사용자 편집 중인 Blender 세션을 덮어쓰거나 저장하지 않는다.

Blender에서 직접 다시 내보낼 때
1. Image Texture의 Color가 Principled BSDF의 Base Color에 연결되어 있는지 확인한다.
2. File > Export > FBX에서 Path Mode를 Copy로 선택한다.
3. 바로 옆의 작은 상자 아이콘(Embed Textures)도 켠다. Copy만 선택하면 내장되지 않는다.
4. 새 이름으로 내보낸 뒤, 이미지 원본이 없는 별도 폴더에서 FBX를 다시 가져와 확인한다.
   원본 이미지가 같은 PC에 있으면 이미지가 빠진 FBX도 정상처럼 보일 수 있다.

검증 결과와 정확한 파일 해시는 verification.json을 확인한다.
Mixamo 웹사이트에 이 수정본을 실제 업로드한 결과는 별도 확인이 필요하다.

공식 안내
Adobe: https://helpx.adobe.com/creative-cloud/help/mixamo-rigging-animation.html
Blender: https://docs.blender.org/api/5.2/bpy.ops.export_scene.html
