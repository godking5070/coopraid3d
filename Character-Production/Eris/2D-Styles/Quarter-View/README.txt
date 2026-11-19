이리스 50° 쿼터뷰 대기 스프라이트

SampleScene 기본 3D 카메라의 하향 각도 50°를 기준으로 왼쪽 앞(↙) 대기 자세를 그렸습니다.
두 그림에 같은 왼쪽 앞 방향과 발 기준점을 사용했습니다.

Iris_Idle_Faceted_Quarter_sw.png
다면 컬러 블록 첫 보정본 기준, 캔버스 512 × 576 px, 발 기준점 (256, 512) px

Iris_Idle_Pixel_Quarter_sw.png
기본형 도트 기준, 캔버스 128 × 144 px, 발 기준점 (64, 128) px

기준점 좌표의 원점은 프레임의 왼쪽 위입니다. 두 캔버스는 투명 PNG이며
가까운 부츠 밑단을 각 프레임의 같은 상대 높이에 맞췄습니다. 도트는 Point 필터로 확대합니다.

제작 기록

내장 ImageGen을 사용했습니다. Image 2는 ../../Texturing/Iris_Concept_Textured.blend에서
프레임 1을 기준으로 하향 50°, 캐릭터 상대 수평각 45°의 원근 카메라로 만든 방향 가이드입니다.

다면 컬러 블록

Image 1은 ../Eight-Directions/Faceted/Sprites/Iris_Idle_Faceted_sw.png 입니다.

Use case: style-transfer with identity and viewpoint guidance. Create one full-body transparent 2D RPG idle sprite of Iris for a real quarter-view game. Image 1 is the approved southwest Iris sprite and is the master for identity, costume, body proportions and drawing style. Image 2 is a Blender render of the same character seen from the game's elevated camera. Match the precise viewpoint of Image 2: perspective camera pitched DOWN 50 degrees over the floor, with the character turned 45 degrees toward screen lower left (southwest). Match its occlusion and depth cues. The broad upper surface of the cream hood is visibly exposed, the face and torso are foreshortened, the coat hem curves in perspective, one boot is higher on the screen than the other, and the tops of both boots are clearly visible. The head is substantially seen from above, as in Image 2, and is gazing naturally forward toward lower left. Keep both soles planted on the same imaginary floor, body balanced and upright in her relaxed standing idle. Empty hands down, fingers grouped naturally below cuffs. Preserve Image 1's scale of head relative to body, hairstyle and flower placement. The hood has the dark navy band and blue petal marks; the sky-blue flower with a gold star center rests on her anatomical left temple (image-right from this angle), with navy ribbons; ivory-to-pink curled hair, deep navy and ivory dress, gold trim and paired buttons, puffy sleeves and cream fur boots. The nearer visible eye is blue with a gold star and a small white sclera. The far eye should be partly occluded as appropriate from this height. Same cheerful gentle expression. The finished sprite is one complete centrally framed character with all hood and boots visible, occupying about 76 percent of the frame height, generous empty margins. True alpha transparency in the background and around limbs and curl gaps. No scenery, floor graphic, drop shadow, text, UI, particles, extra figures, props, or equipment.

Render in the faceted color-block illustration style of Image 1. Use fine flat connected angular color planes in her peach-pink hair, ivory hood, ivory sleeve fabric, navy cloth and chunky boots. The design reads as an original hand-painted geometric chibi game sprite. Keep soft pastel palette, navy-and-gold dress contrast, and clean outer silhouette. Keep the first approved posture's relaxed shoulder alignment. Use the 3D render only to set the angle and the overlap of forms. Do not use outlined pixel art, realistic volumetric 3D material, or a flat eye-level angle.

도트

Image 1은 ../Eight-Directions/Sprites/Iris_Idle_sw.png 를 최근접 보간으로 8배 확대해 사용했습니다.

Use case: style-transfer with identity and viewpoint guidance. Create one full-body transparent 2D RPG idle sprite of Iris for a real quarter-view game. Image 1 is the approved southwest Iris sprite and is the master for identity, costume, body proportions and drawing style. Image 2 is a Blender render of the same character seen from the game's elevated camera. Match the precise viewpoint of Image 2: perspective camera pitched DOWN 50 degrees over the floor, with the character turned 45 degrees toward screen lower left (southwest). Match its occlusion and depth cues. The broad upper surface of the cream hood is visibly exposed, the face and torso are foreshortened, the coat hem curves in perspective, one boot is higher on the screen than the other, and the tops of both boots are clearly visible. The head is substantially seen from above, as in Image 2, and is gazing naturally forward toward lower left. Keep both soles planted on the same imaginary floor, body balanced and upright in her relaxed standing idle. Empty hands down, fingers grouped naturally below cuffs. Preserve Image 1's scale of head relative to body, hairstyle and flower placement. The hood has the dark navy band and blue petal marks; the sky-blue flower with a gold star center rests on her anatomical left temple (image-right from this angle), with navy ribbons; ivory-to-pink curled hair, deep navy and ivory dress, gold trim and paired buttons, puffy sleeves and cream fur boots. The nearer visible eye is blue with a gold star and a small white sclera. The far eye should be partly occluded as appropriate from this height. Same cheerful gentle expression. The finished sprite is one complete centrally framed character with all hood and boots visible, occupying about 76 percent of the frame height, generous empty margins. True alpha transparency in the background and around limbs and curl gaps. No scenery, floor graphic, drop shadow, text, UI, particles, extra figures, props, or equipment.

Render as true old-school RPG sprite pixel art in Image 1's classic style. Image 1 is magnified by nearest-neighbor to show authentic square pixels and the stepped darkest navy outline. Build the new drawing on one clean orthogonal square pixel grid, as a compact roughly 54–62 pixel wide by 96 pixel tall character, each logical pixel drawn at the same integer enlargement so the bitmap can be scaled back to a 128-by-144 game frame. Clear one-pixel outlines and carefully grouped 2–3-tone clusters, no gradients, no antialiasing or fine high-resolution linework. Match exact proportions and pixel density of Image 1 when viewed at gameplay size. Use the second image only to establish the view angle and occlusion. Keep good shape separation between cuff, small relaxed hand, skirt and hair.
