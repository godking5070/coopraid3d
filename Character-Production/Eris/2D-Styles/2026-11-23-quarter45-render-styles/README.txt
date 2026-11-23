이리스 45도 쿼터뷰 2D 그림체 시안 5종 (2026-11-23)

방식
  이미지 생성 대신 텍스처·리깅이 끝난 이리스 3D 작업본을 Blender에서 렌더링해
  2D 그림체로 후처리했습니다. 같은 모델과 리그에서 나오므로 프레임마다 의상·비율이
  흔들리지 않습니다. 같은 파이프라인으로 대기·걷기·달리기 애니메이션 시트를 이어서 만들 수 있습니다.

  원본: godking5070/캐릭터-컨셉아트 브랜치
        Character-Production/Eris/Texturing/Iris_Concept_Textured.blend
  자세: Eris_Idle_Calm 360프레임 (얼굴·꽃 장식이 잘 보이고 손을 내린 대기 자세)
  카메라: 직교(Orthographic), 45도 내려다봄, 45도 회전. 이리스는 화면 왼쪽 아래를 봅니다.
  조명: 화면 왼쪽 위·정면 키 라이트(노멀로 계산) + 렌더된 그림자(후드→얼굴, 치마→다리) + AO

결과 (모두 투명 배경 PNG)
  01-anime-cel            애니메 셀: 남보라 외곽선, 2단 음영(라벤더 그림자), 왼쪽 림라이트
  02-pixel-16bit          16비트 픽셀: 22색 팔레트, 6×6 블록 다운샘플, 1px 선택 외곽선
                          -game.png가 실제 크기(142×176) 원본, 무접미 파일은 6배 확대본
  03-watercolor-storybook 수채 동화: 물감 번짐·가장자리 고임, 종이 여백, 연필 밑선
  04-manga-screentone     만화 잉크: 흑백 펜선 + 45도 망점 스크린톤 + 파랑 별색(꽃·눈·문양)
  05-flat-vector          플랫 벡터: 선 없이 면 분할, 색조가 바뀌는 3단 음영, 같은 색 계열 외곽 띠

  *-game.png         게임 화면 크기 미리보기(높이 180px, 캐릭터 약 160px)
  contact-sheet.png  5종 큰 크기 비교 (회색 배경, 왼쪽부터 01→05)
  game-scale-sheet.png  5종 게임 크기 비교

다시 만들기 (Blender 5.3)
  1) 원본 작업본 꺼내기
     git show "origin/godking5070/캐릭터-컨셉아트:Character-Production/Eris/Texturing/Iris_Concept_Textured.blend" > iris.blend
     (PowerShell에서는 cmd /c 로 감싸 바이너리가 깨지지 않게 합니다)
  2) 데이터 패스 렌더 (albedo, normal, depth, lambert, key, ao → EXR)
     blender -b iris.blend --python tools/render_passes.py -- 패스폴더 Eris_Idle_Calm 360 1280
  3) 그림체 합성
     blender -b --python tools/stylize.py -- 패스폴더 출력폴더 [styles=cel,pixel,watercolor,manga,flat]
  다른 자세·방향: 2)의 동작/프레임 인자와 yaw=각도 인자를 바꿉니다. 원본 .blend는 수정하지 않습니다.

확인한 것
  Blender 5.3 백그라운드 렌더와 합성을 실제로 실행했고 5종 큰 이미지·게임 크기 이미지를 눈으로 검토했습니다.

미확인·한계
  Unity 임포트, 실제 게임 배경 위 가독성, 애니메이션 프레임 연속성은 아직 확인하지 않았습니다.
  수채·플랫 벡터는 밝은 바닥에서 윤곽이 약할 수 있습니다.
  흰 옷과 연한 머리 비중이 커서 게임 크기에서는 셀·만화 잉크가 가장 또렷합니다.
  고개를 크게 숙이거나 돌리는 프레임에서는 후드와 머리카락이 얼굴을 가릴 수 있습니다.
