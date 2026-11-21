# Astella 수호석상 전장 시안

시점이 고정된 수호석상 전장에서 바닥, 캐릭터, 위험 범위를 겹쳐 보는 시안입니다. index.html을 열고 원 안의 바닥을 클릭하거나 탭하면 이리스가 이동합니다. 중앙 내려찍기·외곽 고리·집합의 범위를 따로 볼 수 있고, 연계 패턴 재생에서는 중앙과 외곽을 순서대로 피할 수 있습니다.

- StoneGuardianArena.png는 생성한 1672 × 941 px 전장 배경입니다. 보스와 바닥 장판은 HTML에서 따로 그립니다.
- StoneGuardian.png는 아이보리 갑주와 남색 성문 방패를 반영한 투명 시안입니다. 참조 원화는 보스 컨셉 브랜치의 ffb4395842f5ca99b4c623ba969a08a4d63281de 커밋에 있는 ConceptArt/BossOverview/previews/p1-s1-normal.jpg입니다. 변신 전 모습이며 배치용 정지 컷입니다.
- IrisRigPreview.png는 Character-Production/Eris/Texturing/Iris_Concept_Textured.blend에서 렌더링했습니다. 같은 리그와 달리기 주기를 남·동·북·서에서 본 112 × 112 px 캡처입니다. 행마다 정지 1장과 달리기 8장이 있습니다. 원본 Blender 파일 SHA-256은 bbf489796eb63aaf3c978f10ed36728c07f812c36265f90e0a897589561c4d64입니다.
- RaidPreview.jpg는 브라우저에서 실제로 그린 중앙 내려찍기 화면입니다.

시안의 중앙 반경은 8m, 외곽 고리는 8–17m입니다. 하드 기본값을 참조해 4초에 중앙, 1.05초 뒤 외곽을 타격하며 붉은 범위는 각 타격 0.4초 전에 표시합니다. 집합은 반경 4m를 표시합니다. 근거는 Assets/Scripts/GuardianNormalData.cs의 GuardianHardSettings와 docs/part1/GUARDIAN_HARD.md에 있습니다.

Chrome에서 중앙과 외곽 범위가 서로 다른 구간에 표시되는지, 같은 달리기 주기가 8→1로 이어지는지, 중앙과 외곽을 차례로 피할 수 있는지 확인했습니다. 390px 브라우저 화면에서 터치로 집합 범위에 들어가는 것도 확인했습니다. 브라우저 판정은 이리스의 발 위치 한 점을 사용합니다. 보스 동작, Unity 충돌·피격, 멀티플레이, 실제 휴대폰 실행은 미검증입니다.

Unity 원본의 확인 기준은 Stage 1 하드 변신 전의 광장 내려찍기입니다. 고정 시점에서 배경과 18m 원형 전투 바닥의 가장자리를 맞춘 뒤, 이리스와 보스를 배치해 중앙·외곽 경계와 캐릭터가 겹쳐도 구역을 읽을 수 있는지 확인합니다.

## 생성 프롬프트

배경과 보스 정지 컷은 내장 이미지 생성 도구를 사용했습니다.

### 전장 배경

Use case: stylized-concept.
Asset: clean background plate for a single-screen four-player fantasy raid, landscape 16:9. Create the empty Stage 1 Stone Guardian plaza for the original game Astella. This image will sit underneath actual moving 3D characters and engine-rendered attack markers. It should look like a polished in-game camera, richly art-directed and highly readable.
Fixed orthographic gameplay camera looking forty-five to fifty degrees downward from the front, symmetrical central composition. The only playable platform is one complete flat circular gray-blue basalt plaza, projected as a wide oval. Keep the entire near edge and the entire far edge visible with a comfortable outer margin. Center of the oval roughly 50% across, 54% down the image. Aim for the oval to fill about 82% of the canvas width and 73% of the canvas height, with most of the view dedicated to traversable floor. No horizon. At the outer ring, ancient observatory masonry, very low stone lip in front, damaged vaulted arcade and sparse ornamental pillars behind, descending stonework into a shadowed ravine, restrained warm lamp light, cool blue hour atmosphere. Give the arena a strong, distinctive shape without crowding its play area. Treat the silhouette and edges with the finish of hand-painted stylized 3D environment art. Fine weathering and chipped beveled stone at the raised perimeter, warm antique gold only at small edge accents. Quiet low-contrast desaturated slate stone in the interior, broad clean stone tiles with subdued joints and gently mottled lighting. Only a very faint stone inlay at the exact center for position registration, no bright concentric circles and no bright floor markings. The center must remain completely flat and unobstructed. The stone floor must remain legible under an overlay of saturated red, cyan or violet. Restrained sophisticated blue-gray, graphite and mild warm amber color. Good ambient depth and contact shadows at outer architecture. Do not put foreground pillars or props on top of the main arena. This is an empty usable background asset. No characters, guardian, statues in the floor space, weapons, portraits, combat effects, magical glowing ground lines, UI, letters, text or watermark. Draw a real scene with artful finished rendering, not a platform diagram.

### 수호석상

Use case: stylized-concept. Asset: ONE isolated in-game scale render of the Stone Guardian in Astella, true transparent background. Image 1 is the original character model sheet and the exact design reference. Translate only its primary full-body character from the left of the sheet into a beautiful stylized 3D game render. Preserve the existing proportions and all readable design motifs: a classical stern pale stone human face under a crested stone helmet, layered faceted ivory and warm pale gray limestone slabs, navy inserts and aged gold trim, navy shield with the cream castle gate and a four-point gold star, gold round shoulder joints, gold star on the chest and navy hanging cloth, heavy light stone club with navy band and gold crest in the right hand, shield in the left. He should look like an immense sentry of the observatory. Match the light, clean sculpted edges and selective faceting of the design, with refined lighting and subtle light stone surface grain rather than a new species or bulky rock monster. Neutral grounded ready pose with feet apart, slightly bent elbows, shield turned to show its crest and held to the side so the chest can be read. Angle: raised three-quarter gameplay view looking about 35 degrees downward, slightly from the statue's front so helmet top and both feet are visible. Soft blue evening ambient lighting and restrained warm glints on gold. Show the entire single character from crest, full club and shield to both feet, with generous transparent margins. No model sheet, diagrams, other poses, surroundings, background, pedestal, shadow baked underneath, text, effects or watermark.
