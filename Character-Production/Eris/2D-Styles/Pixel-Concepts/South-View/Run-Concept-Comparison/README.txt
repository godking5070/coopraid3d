# 이리스 오른쪽 달리기 3가지 컨셉

확정된 오른쪽 달리기 포즈를 기준으로 그림체 세 가지를 비교합니다. index.html을 열고 좌우 컨셉과 프레임 번호를 고릅니다. 재생 속도는 1×와 0.5×가 있고, 얼굴·손목과 소매·발목과 부츠를 확대할 수 있습니다.

- 08 디더링 도트는 ../Dither-Locomotion/Iris_08_Run_8Dir.png의 오른쪽 행입니다.
- 09 다면 컬러 블록은 후드와 소매, 머리카락에 각진 명암 면을 넣었습니다.
- 10 둥근 치비는 머리카락과 부츠를 둥글게 처리하고 윤곽과 채색을 간결하게 정리했습니다.

Iris_Run_Concepts.png는 배경이 투명한 1024 × 480 px 아틀라스입니다. 위에서부터 08, 09, 10 순이며 각 행은 왼쪽부터 프레임 1–8입니다. 세 스타일 모두 128 × 160 px 셀, 프레임당 90ms, 한 주기 720ms입니다. Unity 원본 프로젝트의 Sprite Editor에서 셀 크기로 그리드를 자릅니다. 아래에서부터의 정규화 피벗은 (0.5, 0.2)이며 Point 필터와 압축 없음으로 가져옵니다.

Iris_Run_Concepts_Frames.png에는 24장의 프레임 번호가 있습니다. Iris_Run_Concepts_Preview.gif에서는 세 버전이 같은 프레임을 반복 재생합니다.

## 검수

- 09와 10의 후드 높이와 부츠 최하단을 08과 프레임별로 맞췄습니다. 얼굴은 후드 높이를 기준으로 정렬한 뒤, 스타일마다 같은 눈과 입을 사용했습니다.
- 24장이 투명 배경 위에서 각각 연결된 실루엣인지 확인했습니다. 손목·소매와 발목·부츠, 치마와 다리의 겹침을 원본 크기와 확대판으로 확인했습니다.
- 09와 10의 가까운 손목 주변 피부 픽셀 중심은 08과 최대 1.26px, 0.92px 차이였습니다. 부츠 버클 색 영역의 중심 차이는 최대 3.46px, 3.55px였습니다. 8→1번의 손 위치 변화는 1.46px, 2.13px였습니다.
- Chrome의 740px과 390px 화면에서 모든 선택 프레임의 픽셀을 아틀라스와 대조했습니다. 1×와 0.5×에서 1→8→1을 확인했고, GIF의 8장과 90ms 간격을 확인했습니다. Unity와 실제 휴대폰 재생은 미검증입니다.

## 생성 프롬프트

09와 10은 내장 이미지 생성 도구를 사용했습니다. 두 프롬프트의 첫 번째 입력은 08의 오른쪽 8장을 4×2로 놓은 자세 가이드입니다. 두 번째 입력은 각각 ../Sprites/09-facets.png, ../Sprites/10-round-chibi.png입니다. 생성된 시트를 분리해 투명 가장자리를 다듬고, 08의 셀과 높이를 맞춘 뒤 스타일마다 한 얼굴을 고정했습니다.

### 09 다면 컬러 블록

Use case: style-transfer. Asset: actual transparent 2D game sprite sheet for Iris with eight successive running poses. Image 1 is an exact locked custom 4-by-2 running animation guide, 1–4 over 5–8, right-facing slightly elevated side profile. Image 2 is the existing standing art style reference for Iris. Repaint Image 1 into the graphic treatment specified below. Draw exactly 8 complete sprites, in the same 4-by-2 grid with generous transparent margins and identical scale. Preserve each frame's projected pose, the head lean and head location, pelvis, order of the arms and legs, position/size of the boots, sequence and offset. Keep the running body leaning forward in all eight, near and far limb motion alternating exactly with the guide, the wrist attached to its sleeve, the feet attached to their boots. Maintain the same small determined happy profile eye and mouth, exact head tilt and proportion across all eight, consistent hair locks and outline of hood. Pale blonde above pink lengths, ivory hood with indigo band and pale blue crosses, same ivory sleeves and gold-trim navy coat, cream boots, warm highlights. Reference stays a profile with only the visible eye. True alpha outside silhouettes. No cells or characters touching, missing limbs, duplicate poses, motion effects, labels, floor, shadows, UI, blue background or checkerboard.
Style target: concept 09, crisp faceted color blocks. Make the triangular and beveled color planes genuinely visible at small scale, inspired by Image 2. Use 2–3 broad polygonal value shapes on each hood panel and side of a boot or sleeve, and clearly carved salmon and cream planes down each curl. Sparse thin plum contour only where helpful. Bold dark indigo skirt, pale cream top plane, highlight golden metallic polygon. No soft shadow gradients, dithering, visible diagonal noise, extra accessories, black graphic outlines or 3D realism. Hard square stepped gameplay pixel edges.

### 10 둥근 치비

Use case: style-transfer. Asset: actual transparent 2D game sprite sheet for Iris with eight successive running poses. Image 1 is an exact locked custom 4-by-2 running animation guide, 1–4 over 5–8, right-facing slightly elevated side profile. Image 2 is the existing standing art style reference for Iris. Repaint Image 1 into the graphic treatment specified below. Draw exactly 8 complete sprites, in the same 4-by-2 grid with generous transparent margins and identical scale. Preserve each frame's projected pose, the head lean and head location, pelvis, order of the arms and legs, position/size of the boots, sequence and offset. Keep the running body leaning forward in all eight, near and far limb motion alternating exactly with the guide, the wrist attached to its sleeve, the feet attached to their boots. Maintain the same small determined happy profile eye and mouth, exact head tilt and proportion across all eight, consistent hair locks and outline of hood. Pale blonde above pink lengths, ivory hood with indigo band and pale blue crosses, same ivory sleeves and gold-trim navy coat, cream boots, warm highlights. Reference stays a profile with only the visible eye. True alpha outside silhouettes. No cells or characters touching, missing limbs, duplicate poses, motion effects, labels, floor, shadows, UI, blue background or checkerboard.
Style target: concept 10, clear rounded chibi graphic. A playful plush interpretation of the same runner with rounded simple hood shape and rounded cuffs and boot toes, tidy raised pink curls, and subtle selective dark plum contour. Simplify to a few strong clean shapes with broad peach, bubblegum pink, cream and muted indigo areas, pale blush, compact mitt fists with one clean wrist contour, boot highlight curves, and a small blue/gold visible eye. The slightly larger rounded shapes must remain the same size throughout. Pitch and limb reach are locked to Image 1. No glitter, block shading, checker patterns, airbrush gradients, extra accessories, or isolated pixels. Fewer smoother curves of square gameplay pixels.
