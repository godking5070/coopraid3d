#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace RaidRogue.Editor
{
    /// <summary>
    /// 유니티 에디터 상단 메뉴 클릭 한 번(또는 스크립트 로드 시 자동)으로
    /// Step 1 ~ Step 6의 모든 오브젝트(전장, 50도 쿼터뷰 카메라, Stage 1~3 보스 + 4분할 타겟 링,
    /// 내 캐릭터(GCD/oGCD & 3단 진화 콤보) + 3명의 AI 파티원 봇(구출 & 4인 산개/4인 쉐어 협동 AI),
    /// 상단 STAGE 1/3 표시 및 보스 체력/캐스팅바 UI, 하단 4슬롯 스킬 핫바 UI,
    /// 보스 처치 시 BOSS CLEAR 배너 -> 1.5초 뒤 로그라이크 3택 카드 모달 UI -> Stage 3 클리어 시 VICTORY 결과창)를
    /// 100% 자동 생성·연결하는 빌더.
    /// </summary>
    [InitializeOnLoad]
    public static class SceneAutoBuilder
    {
        private const string GeneratedMaterialFolder = "Assets/Materials";
        private const string Step1ScenePath = "Assets/Scenes/Step1_TestScene.unity";
        private const string Step2ScenePath = "Assets/Scenes/Step2_TestScene.unity";
        private const string Step3ScenePath = "Assets/Scenes/Step3_TestScene.unity";
        private const string Step4ScenePath = "Assets/Scenes/Step4_TestScene.unity";
        private const string Step5ScenePath = "Assets/Scenes/Step5_TestScene.unity";
        private const string Step6ScenePath = "Assets/Scenes/Step6_TestScene.unity";

        static SceneAutoBuilder()
        {
            EditorApplication.delayCall += AutoSetupIfNeeded;
        }

        private static void AutoSetupIfNeeded()
        {
            BossController existingBoss = Object.FindFirstObjectByType<BossController>();
            RoguelikeCardManager existingCardMgr = Object.FindFirstObjectByType<RoguelikeCardManager>();
            PlayerCombatController existingCombat = Object.FindFirstObjectByType<PlayerCombatController>();
            SkillHotbarUI existingHotbar = Object.FindFirstObjectByType<SkillHotbarUI>();
            StageManager existingStageMgr = Object.FindFirstObjectByType<StageManager>();

            bool needsStep6Upgrade =
                existingBoss == null ||
                existingBoss.bossHpFillImage == null ||
                existingCardMgr == null ||
                existingCardMgr.cardSelectionModalRoot == null ||
                existingCombat == null ||
                existingHotbar == null ||
                existingStageMgr == null ||
                existingStageMgr.stageHeaderText == null ||
                existingStageMgr.victoryModalRoot == null ||
                Object.FindFirstObjectByType<PlayerController>() == null ||
                Object.FindFirstObjectByType<PartyBotAI>() == null;

            if (!needsStep6Upgrade)
            {
                return;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.isPlaying = false;
                EditorApplication.delayCall += AutoSetupIfNeeded;
                return;
            }

            BuildRaidScene();
        }

        [MenuItem("Tools/RaidRogue/Setup Scene", false, 1)]
        public static void SetupSceneMenu()
        {
            RunSetupMenu();
        }

        [MenuItem("Tools/RaidRogue/Setup Step 1", false, 10)]
        public static void SetupStep1Menu()
        {
            RunSetupMenu();
        }

        [MenuItem("Tools/RaidRogue/Setup Step 2", false, 11)]
        public static void SetupStep2Menu()
        {
            RunSetupMenu();
        }

        [MenuItem("Tools/RaidRogue/Setup Step 3", false, 12)]
        public static void SetupStep3Menu()
        {
            RunSetupMenu();
        }

        [MenuItem("Tools/RaidRogue/Setup Step 4", false, 13)]
        public static void SetupStep4Menu()
        {
            RunSetupMenu();
        }

        [MenuItem("Tools/RaidRogue/Setup Step 5", false, 14)]
        public static void SetupStep5Menu()
        {
            RunSetupMenu();
        }

        [MenuItem("Tools/RaidRogue/Setup Step 6", false, 15)]
        public static void SetupStep6Menu()
        {
            RunSetupMenu();
        }

        private static void RunSetupMenu()
        {
            if (EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = false;
                EditorApplication.delayCall += BuildRaidScene;
                return;
            }
            BuildRaidScene();
        }

        public static void BuildRaidScene()
        {
            if (EditorApplication.isPlaying)
            {
                return;
            }

            EnsureFoldersExist();

            Scene activeScene = SceneManager.GetActiveScene();
            ClearSceneRootObjects(activeScene);

            // 1. 조명 생성
            CreateLighting();

            // 2. 3D 전장 바닥 생성
            CreateBattlefieldArena();

            // 3. 중앙 보스 + 파판14 스타일 4분할 타겟 링 생성
            BossController boss = CreateBossWithTargetRing();

            // 4. 내 캐릭터 (근거리 딜러 + PlayerCombatController [GCD/oGCD & 3단 진화 콤보])
            PlayerController player = CreatePlayer(boss);

            // 5. 3명의 AI 파티원 봇 생성 (탱커 / 힐러(구출 포함) / 원딜 + 4인 산개 & 4인 쉐어 협동 AI)
            CreatePartyBots(boss, player);

            // 6. X축 50도 쿼터뷰 카메라 생성
            QuarterViewCamera quarterCam = CreateQuarterViewCamera(player.transform);

            // 7. UI EventSystem 생성
            CreateEventSystemIfNeeded();

            // 8. 전체 UI 및 StageManager / RoguelikeCardManager 생성
            CreateAllRaidStageAndCardUI(boss, player);

            if (boss.targetRing != null)
            {
                boss.targetRing.BuildTargetRing();
            }
            quarterCam.SnapToTargetImmediate();
            AlignSceneViewCamera(quarterCam.transform);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(activeScene);

            if (!string.IsNullOrEmpty(activeScene.path))
            {
                EditorSceneManager.SaveScene(activeScene);
            }
            EditorSceneManager.SaveScene(activeScene, Step1ScenePath, true);
            EditorSceneManager.SaveScene(activeScene, Step2ScenePath, true);
            EditorSceneManager.SaveScene(activeScene, Step3ScenePath, true);
            EditorSceneManager.SaveScene(activeScene, Step4ScenePath, true);
            EditorSceneManager.SaveScene(activeScene, Step5ScenePath, true);
            EditorSceneManager.SaveScene(activeScene, Step6ScenePath, true);
            AssetDatabase.Refresh();

            Selection.activeGameObject = player.gameObject;
            Debug.Log("[RaidRogue] Step 6 (보스 처치 시 카드 보상 지급 + Stage 1~3 다단계 보스 러시 루프 + 4인 산개/4인 쉐어/복합 기믹 + 최종 승리 결과창) 씬 자동 구축 완료! ▶(Play) 버튼을 눌러 테스트하세요.");
        }

        private static void CreateEventSystemIfNeeded()
        {
            GameObject esObj = new GameObject("EventSystem");
            esObj.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
            esObj.AddComponent<InputSystemUIInputModule>();
#else
            esObj.AddComponent<StandaloneInputModule>();
#endif
        }

        private static void AlignSceneViewCamera(Transform gameCamTransform)
        {
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView != null && gameCamTransform != null)
            {
                sceneView.LookAt(
                    new Vector3(0f, 1f, -1f),
                    Quaternion.Euler(50f, 0f, 0f),
                    17f
                );
                sceneView.Repaint();
            }
        }

        private static void EnsureFoldersExist()
        {
            if (!AssetDatabase.IsValidFolder(GeneratedMaterialFolder))
            {
                AssetDatabase.CreateFolder("Assets", "Materials");
            }
            if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            {
                AssetDatabase.CreateFolder("Assets", "Scenes");
            }
        }

        private static void ClearSceneRootObjects(Scene scene)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = roots.Length - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(roots[i]);
            }
        }

        private static void CreateLighting()
        {
            GameObject lightObj = new GameObject("Directional Light");
            Light light = lightObj.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.97f, 0.92f, 1f);
            light.intensity = 1.25f;
            light.shadows = LightShadows.Soft;
            lightObj.transform.rotation = Quaternion.Euler(52f, -35f, 0f);

            if (lightObj.GetComponent<UniversalAdditionalLightData>() == null)
            {
                lightObj.AddComponent<UniversalAdditionalLightData>();
            }

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.38f, 0.40f, 0.45f, 1f);
        }

        private static void CreateBattlefieldArena()
        {
            GameObject arenaRoot = new GameObject("Battlefield_Arena");

            GameObject floorPlane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floorPlane.name = "Arena_Floor_Plane";
            floorPlane.transform.SetParent(arenaRoot.transform, false);
            floorPlane.transform.position = Vector3.zero;
            floorPlane.transform.localScale = new Vector3(4.5f, 1f, 4.5f);

            Renderer floorRenderer = floorPlane.GetComponent<Renderer>();
            floorRenderer.sharedMaterial = GetOrCreateLitMaterialAsset("Mat_ArenaFloor", new Color(0.20f, 0.22f, 0.25f, 1f));

            GameObject borderObj = new GameObject("Arena_BorderRing");
            borderObj.transform.SetParent(arenaRoot.transform, false);
            LineRenderer borderLr = borderObj.AddComponent<LineRenderer>();
            borderLr.useWorldSpace = false;
            borderLr.loop = true;
            borderLr.startWidth = 0.12f;
            borderLr.endWidth = 0.12f;
            Color borderColor = new Color(0.42f, 0.46f, 0.52f, 0.65f);
            borderLr.startColor = borderColor;
            borderLr.endColor = borderColor;
            borderLr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            borderLr.receiveShadows = false;
            borderLr.sharedMaterial = GetOrCreateUnlitLineMaterialAsset();

            const int segments = 96;
            borderLr.positionCount = segments;
            for (int i = 0; i < segments; i++)
            {
                float rad = (float)i / segments * Mathf.PI * 2f;
                borderLr.SetPosition(i, new Vector3(Mathf.Sin(rad) * 18f, 0.02f, Mathf.Cos(rad) * 18f));
            }
        }

        private static BossController CreateBossWithTargetRing()
        {
            GameObject bossRoot = new GameObject("Boss");
            bossRoot.transform.position = Vector3.zero;
            bossRoot.transform.rotation = Quaternion.identity;
            bossRoot.transform.localScale = Vector3.one;

            GameObject bossBody = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            bossBody.name = "Boss_Body_Cylinder";
            bossBody.transform.SetParent(bossRoot.transform, false);
            bossBody.transform.localPosition = new Vector3(0f, 1.8f, 0f);
            bossBody.transform.localScale = new Vector3(2.2f, 1.8f, 2.2f);

            Renderer bodyRenderer = bossBody.GetComponent<Renderer>();
            bodyRenderer.sharedMaterial = GetOrCreateLitMaterialAsset("Mat_BossPurple", new Color(0.50f, 0.16f, 0.76f, 1f));

            GameObject bossVisor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bossVisor.name = "Boss_FrontVisor";
            bossVisor.transform.SetParent(bossRoot.transform, false);
            bossVisor.transform.localPosition = new Vector3(0f, 2.6f, 1.05f);
            bossVisor.transform.localScale = new Vector3(1.1f, 0.35f, 0.45f);
            Renderer visorRenderer = bossVisor.GetComponent<Renderer>();
            visorRenderer.sharedMaterial = GetOrCreateLitMaterialAsset("Mat_BossVisor", new Color(1f, 0.22f, 0.22f, 1f));

            GameObject ringObj = new GameObject("Boss_TargetRing");
            ringObj.transform.SetParent(bossRoot.transform, false);
            ringObj.transform.localPosition = Vector3.zero;
            ringObj.transform.localRotation = Quaternion.identity;
            ringObj.transform.localScale = Vector3.one;

            TargetRingRenderer ringRenderer = ringObj.AddComponent<TargetRingRenderer>();
            ringRenderer.outerRadius = 3.2f;
            ringRenderer.innerRadius = 1.4f;
            ringRenderer.groundOffset = 0.05f;
            ringRenderer.BuildTargetRing();

            BossController bossController = bossRoot.AddComponent<BossController>();
            bossController.bodyRenderer = bodyRenderer;
            bossController.targetRing = ringRenderer;
            bossController.rotationSpeed = 90f;
            bossController.popupHeightOffset = 4.6f;
            bossController.maxHp = 3000f;
            bossController.currentHp = 3000f;

            return bossController;
        }

        private static PlayerController CreatePlayer(BossController boss)
        {
            GameObject playerObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            playerObj.name = "Player_MeleeDPS";
            playerObj.transform.position = new Vector3(0f, 1.0f, -4.0f);
            playerObj.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            playerObj.transform.localScale = Vector3.one;

            Color yellowColor = new Color(1f, 0.85f, 0.12f, 1f);
            Renderer playerRenderer = playerObj.GetComponent<Renderer>();
            playerRenderer.sharedMaterial = GetOrCreateLitMaterialAsset("Mat_PlayerYellow", yellowColor);

            AttachFrontEyeCube(playerObj.transform, "Player_FrontEye_Cube");

            PlayerController playerController = playerObj.AddComponent<PlayerController>();
            playerController.targetBoss = boss;
            playerController.bodyRenderer = playerRenderer;
            playerController.normalBodyColor = yellowColor;
            playerController.defensiveBodyColor = new Color(0.15f, 0.52f, 1f, 1f);
            playerController.moveSpeed = 7.8f;

            PlayerCombatController combatController = playerObj.AddComponent<PlayerCombatController>();
            combatController.playerController = playerController;
            combatController.targetBoss = boss;
            combatController.baseGcdDuration = 2.0f;
            combatController.currentGcdDuration = 2.0f;
            combatController.skill3DefensiveCooldown = 15.0f;
            combatController.skill4GaleThrustCooldown = 8.0f;
            combatController.comboExpireDuration = 8.0f;

            playerController.combatController = combatController;
            boss.targetPlayer = playerObj.transform;
            return playerController;
        }

        private static void CreatePartyBots(BossController boss, PlayerController player)
        {
            GameObject partyRoot = new GameObject("PartyBots_AI");

            PartyBotAI tankBot = CreateSingleBot(
                partyRoot.transform,
                "Bot_Tank_Blue",
                PartyBotRole.Tank,
                new Vector3(0f, 1.0f, 4.8f),
                "Mat_BotTankBlue",
                new Color(0.18f, 0.52f, 1.0f, 1f),
                boss,
                player,
                null
            );

            boss.aggroTarget = tankBot.transform;

            CreateSingleBot(
                partyRoot.transform,
                "Bot_Healer_Green",
                PartyBotRole.Healer,
                new Vector3(-6.8f, 1.0f, -6.5f),
                "Mat_BotHealerGreen",
                new Color(0.18f, 0.88f, 0.38f, 1f),
                boss,
                player,
                tankBot
            );

            CreateSingleBot(
                partyRoot.transform,
                "Bot_RangedDPS_Orange",
                PartyBotRole.RangedDPS,
                new Vector3(6.6f, 1.0f, -6.0f),
                "Mat_BotRangedOrange",
                new Color(1.0f, 0.50f, 0.12f, 1f),
                boss,
                player,
                tankBot
            );
        }

        private static PartyBotAI CreateSingleBot(
            Transform parent,
            string botName,
            PartyBotRole role,
            Vector3 startPos,
            string matName,
            Color bodyColor,
            BossController boss,
            PlayerController player,
            PartyBotAI tankBot)
        {
            GameObject botObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            botObj.name = botName;
            botObj.transform.SetParent(parent, false);
            botObj.transform.position = startPos;

            Vector3 toBossXZ = new Vector3(-startPos.x, 0f, -startPos.z);
            if (toBossXZ.sqrMagnitude > 0.001f)
            {
                botObj.transform.rotation = Quaternion.LookRotation(toBossXZ.normalized, Vector3.up);
            }

            Renderer r = botObj.GetComponent<Renderer>();
            r.sharedMaterial = GetOrCreateLitMaterialAsset(matName, bodyColor);

            AttachFrontEyeCube(botObj.transform, $"{botName}_FrontEye");

            PartyBotAI botAi = botObj.AddComponent<PartyBotAI>();
            botAi.role = role;
            botAi.targetBoss = boss;
            botAi.humanPlayer = player;
            botAi.bodyRenderer = r;
            botAi.tankBotReference = tankBot != null ? tankBot : (role == PartyBotRole.Tank ? botAi : null);

            return botAi;
        }

        private static void AttachFrontEyeCube(Transform parentCapsule, string eyeName)
        {
            GameObject eyeCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            eyeCube.name = eyeName;
            eyeCube.transform.SetParent(parentCapsule, false);
            eyeCube.transform.localPosition = new Vector3(0f, 0.42f, 0.45f);
            eyeCube.transform.localScale = new Vector3(0.55f, 0.22f, 0.30f);

            Collider eyeCol = eyeCube.GetComponent<Collider>();
            if (eyeCol != null)
            {
                Object.DestroyImmediate(eyeCol);
            }

            Renderer eyeRenderer = eyeCube.GetComponent<Renderer>();
            eyeRenderer.sharedMaterial = GetOrCreateLitMaterialAsset("Mat_CharacterEye", new Color(0.12f, 0.15f, 0.20f, 1f));
        }

        private static QuarterViewCamera CreateQuarterViewCamera(Transform playerTransform)
        {
            GameObject camObj = new GameObject("Main Camera");
            camObj.tag = "MainCamera";

            Camera cam = camObj.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.09f, 0.10f, 0.13f, 1f);
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 200f;

            camObj.AddComponent<AudioListener>();
            if (camObj.GetComponent<UniversalAdditionalCameraData>() == null)
            {
                camObj.AddComponent<UniversalAdditionalCameraData>();
            }

            QuarterViewCamera qvCam = camObj.AddComponent<QuarterViewCamera>();
            qvCam.target = playerTransform;
            qvCam.pitchAngle = 50f;
            qvCam.yawAngle = 0f;
            qvCam.distance = 17f;
            qvCam.minDistance = 6f;
            qvCam.maxDistance = 30f;
            qvCam.zoomSpeed = 8f;
            qvCam.SnapToTargetImmediate();

            return qvCam;
        }

        // =====================================================================
        // Step 3 ~ Step 6 전체 UI 생성 (STAGE 1/3 상단 배지 + 보스바 + 핫바 + BOSS CLEAR 배너 + 카드 3택 + VICTORY 결과창)
        // =====================================================================
        private static void CreateAllRaidStageAndCardUI(BossController boss, PlayerController player)
        {
            Font defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (defaultFont == null)
            {
                defaultFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }

            Sprite fillSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

            GameObject canvasObj = new GameObject("RaidRogue_HUD_Canvas");
            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            canvasObj.AddComponent<GraphicRaycaster>();

            // -----------------------------------------------------------------
            // 0. 화면 최상단 중앙: Step 6 스테이지 진행 배지 (STAGE 1 / 3)
            // -----------------------------------------------------------------
            GameObject stageBadgeObj = new GameObject("Top_StageHeader_Badge");
            stageBadgeObj.transform.SetParent(canvasObj.transform, false);
            Image stageBadgeBg = stageBadgeObj.AddComponent<Image>();
            stageBadgeBg.color = new Color(0.08f, 0.10f, 0.16f, 0.95f);

            Outline stageOutline = stageBadgeObj.AddComponent<Outline>();
            stageOutline.effectColor = new Color(0.85f, 0.70f, 0.25f, 0.85f);
            stageOutline.effectDistance = new Vector2(2f, -2f);

            RectTransform stageBadgeRect = stageBadgeObj.GetComponent<RectTransform>();
            stageBadgeRect.anchorMin = new Vector2(0.5f, 1f);
            stageBadgeRect.anchorMax = new Vector2(0.5f, 1f);
            stageBadgeRect.pivot = new Vector2(0.5f, 1f);
            stageBadgeRect.anchoredPosition = new Vector2(0f, -10f);
            stageBadgeRect.sizeDelta = new Vector2(880f, 34f);

            GameObject stageTextObj = new GameObject("Stage_Header_Text");
            stageTextObj.transform.SetParent(stageBadgeObj.transform, false);
            Text stageHeaderText = stageTextObj.AddComponent<Text>();
            stageHeaderText.font = defaultFont;
            stageHeaderText.fontSize = 19;
            stageHeaderText.fontStyle = FontStyle.Bold;
            stageHeaderText.alignment = TextAnchor.MiddleCenter;
            stageHeaderText.color = new Color(0.85f, 0.65f, 1.0f, 1f);
            stageHeaderText.text = "STAGE 1 / 3  —  공허의 파수꾼 (입문 - 보라색 / HP 3,000)";

            RectTransform stageTextRect = stageTextObj.GetComponent<RectTransform>();
            stageTextRect.anchorMin = Vector2.zero;
            stageTextRect.anchorMax = Vector2.one;
            stageTextRect.offsetMin = Vector2.zero;
            stageTextRect.offsetMax = Vector2.zero;

            // -----------------------------------------------------------------
            // 1. 화면 상단 중앙: 보스 체력바 UI (50% 그로기/2페이즈 전환 눈금선 포함)
            // -----------------------------------------------------------------
            GameObject bossFrame = new GameObject("Top_BossHP_Frame");
            bossFrame.transform.SetParent(canvasObj.transform, false);
            Image frameBg = bossFrame.AddComponent<Image>();
            frameBg.color = new Color(0.05f, 0.06f, 0.09f, 0.92f);

            RectTransform frameRect = bossFrame.GetComponent<RectTransform>();
            frameRect.anchorMin = new Vector2(0.5f, 1f);
            frameRect.anchorMax = new Vector2(0.5f, 1f);
            frameRect.pivot = new Vector2(0.5f, 1f);
            frameRect.anchoredPosition = new Vector2(0f, -48f);
            frameRect.sizeDelta = new Vector2(880f, 74f);

            GameObject phaseTextObj = new GameObject("Boss_PhaseText");
            phaseTextObj.transform.SetParent(bossFrame.transform, false);
            Text phaseText = phaseTextObj.AddComponent<Text>();
            phaseText.font = defaultFont;
            phaseText.fontSize = 16;
            phaseText.fontStyle = FontStyle.Bold;
            phaseText.alignment = TextAnchor.MiddleCenter;
            phaseText.color = new Color(1f, 0.88f, 0.35f, 1f);
            phaseText.text = "1페이즈 (100%~50%) — 보스 처치(HP 0) 시 클리어 보상 로그라이크 카드 3택 팝업!";

            RectTransform phaseRect = phaseTextObj.GetComponent<RectTransform>();
            phaseRect.anchorMin = new Vector2(0f, 1f);
            phaseRect.anchorMax = new Vector2(1f, 1f);
            phaseRect.pivot = new Vector2(0.5f, 1f);
            phaseRect.anchoredPosition = new Vector2(0f, -5f);
            phaseRect.sizeDelta = new Vector2(-20f, 25f);

            GameObject hpBarBgObj = new GameObject("Boss_HP_Bg");
            hpBarBgObj.transform.SetParent(bossFrame.transform, false);
            Image hpBarBg = hpBarBgObj.AddComponent<Image>();
            hpBarBg.color = new Color(0.16f, 0.16f, 0.20f, 1f);

            RectTransform hpBgRect = hpBarBgObj.GetComponent<RectTransform>();
            hpBgRect.anchorMin = new Vector2(0f, 0f);
            hpBgRect.anchorMax = new Vector2(1f, 0f);
            hpBgRect.pivot = new Vector2(0.5f, 0f);
            hpBgRect.anchoredPosition = new Vector2(0f, 9f);
            hpBgRect.sizeDelta = new Vector2(-24f, 32f);

            GameObject hpFillObj = new GameObject("Boss_HP_Fill");
            hpFillObj.transform.SetParent(hpBarBgObj.transform, false);
            Image hpFillImg = hpFillObj.AddComponent<Image>();
            hpFillImg.sprite = fillSprite;
            hpFillImg.color = new Color(0.85f, 0.18f, 0.28f, 1f);
            hpFillImg.type = Image.Type.Filled;
            hpFillImg.fillMethod = Image.FillMethod.Horizontal;
            hpFillImg.fillOrigin = (int)Image.OriginHorizontal.Left;
            hpFillImg.fillAmount = 1f;

            RectTransform hpFillRect = hpFillObj.GetComponent<RectTransform>();
            hpFillRect.anchorMin = Vector2.zero;
            hpFillRect.anchorMax = Vector2.one;
            hpFillRect.offsetMin = Vector2.zero;
            hpFillRect.offsetMax = Vector2.zero;

            // 50% 그로기/2페이즈 전환 눈금선 표시
            CreateHpBarThresholdLine(hpBarBgObj.transform, "Line_50Percent", 0.50f, new Color(1f, 0.95f, 0.25f, 0.98f), 4.0f);

            GameObject hpTextObj = new GameObject("Boss_HP_Text");
            hpTextObj.transform.SetParent(hpBarBgObj.transform, false);
            Text hpText = hpTextObj.AddComponent<Text>();
            hpText.font = defaultFont;
            hpText.fontSize = 18;
            hpText.fontStyle = FontStyle.Bold;
            hpText.alignment = TextAnchor.MiddleCenter;
            hpText.color = Color.white;
            hpText.text = "STAGE 1 : 공허의 파수꾼   |   HP : 3000 / 3000 (100.0%)";

            RectTransform hpTextRect = hpTextObj.GetComponent<RectTransform>();
            hpTextRect.anchorMin = Vector2.zero;
            hpTextRect.anchorMax = Vector2.one;
            hpTextRect.offsetMin = Vector2.zero;
            hpTextRect.offsetMax = Vector2.zero;

            // -----------------------------------------------------------------
            // 2. 보스 체력바 아래: 보스 캐스팅 바 UI
            // -----------------------------------------------------------------
            GameObject castFrame = new GameObject("Top_BossCastBar_Frame");
            castFrame.transform.SetParent(canvasObj.transform, false);
            Image castBg = castFrame.AddComponent<Image>();
            castBg.color = new Color(0.08f, 0.08f, 0.12f, 0.94f);

            RectTransform castFrameRect = castFrame.GetComponent<RectTransform>();
            castFrameRect.anchorMin = new Vector2(0.5f, 1f);
            castFrameRect.anchorMax = new Vector2(0.5f, 1f);
            castFrameRect.pivot = new Vector2(0.5f, 1f);
            castFrameRect.anchoredPosition = new Vector2(0f, -128f);
            castFrameRect.sizeDelta = new Vector2(680f, 36f);

            GameObject castFillObj = new GameObject("Boss_Cast_Fill");
            castFillObj.transform.SetParent(castFrame.transform, false);
            Image castFillImg = castFillObj.AddComponent<Image>();
            castFillImg.sprite = fillSprite;
            castFillImg.color = new Color(1f, 0.55f, 0.12f, 1f);
            castFillImg.type = Image.Type.Filled;
            castFillImg.fillMethod = Image.FillMethod.Horizontal;
            castFillImg.fillOrigin = (int)Image.OriginHorizontal.Left;
            castFillImg.fillAmount = 0.5f;

            RectTransform castFillRect = castFillObj.GetComponent<RectTransform>();
            castFillRect.anchorMin = Vector2.zero;
            castFillRect.anchorMax = Vector2.one;
            castFillRect.offsetMin = new Vector2(4f, 4f);
            castFillRect.offsetMax = new Vector2(-4f, -4f);

            GameObject castTextObj = new GameObject("Boss_Cast_Text");
            castTextObj.transform.SetParent(castFrame.transform, false);
            Text castText = castTextObj.AddComponent<Text>();
            castText.font = defaultFont;
            castText.fontSize = 18;
            castText.fontStyle = FontStyle.Bold;
            castText.alignment = TextAnchor.MiddleCenter;
            castText.color = Color.white;
            castText.text = "시전 대기 중...";

            RectTransform castTextRect = castTextObj.GetComponent<RectTransform>();
            castTextRect.anchorMin = Vector2.zero;
            castTextRect.anchorMax = Vector2.one;
            castTextRect.offsetMin = Vector2.zero;
            castTextRect.offsetMax = Vector2.zero;

            // -----------------------------------------------------------------
            // 3. 화면 중앙 상단: 기믹 알림 배너
            // -----------------------------------------------------------------
            GameObject alertObj = new GameObject("Center_MechanicAlert_Text");
            alertObj.transform.SetParent(canvasObj.transform, false);
            Text alertText = alertObj.AddComponent<Text>();
            alertText.font = defaultFont;
            alertText.fontSize = 26;
            alertText.fontStyle = FontStyle.Bold;
            alertText.alignment = TextAnchor.MiddleCenter;
            alertText.color = new Color(0.25f, 1f, 0.45f, 1f);
            alertText.text = "";

            Shadow alertShadow = alertObj.AddComponent<Shadow>();
            alertShadow.effectColor = new Color(0f, 0f, 0f, 0.95f);
            alertShadow.effectDistance = new Vector2(2f, -2f);

            RectTransform alertRect = alertObj.GetComponent<RectTransform>();
            alertRect.anchorMin = new Vector2(0.5f, 1f);
            alertRect.anchorMax = new Vector2(0.5f, 1f);
            alertRect.pivot = new Vector2(0.5f, 1f);
            alertRect.anchoredPosition = new Vector2(0f, -172f);
            alertRect.sizeDelta = new Vector2(1420f, 54f);

            boss.bossHpFillImage = hpFillImg;
            boss.bossHpText = hpText;
            boss.bossPhaseText = phaseText;
            boss.bossCastBarRoot = castFrame;
            boss.bossCastFillImage = castFillImg;
            boss.bossCastText = castText;
            boss.mechanicAlertBannerText = alertText;

            // -----------------------------------------------------------------
            // 4. 화면 하단 중앙: Step 5 파판14 스타일 4슬롯 스킬 핫바 UI (SkillHotbarUI)
            // -----------------------------------------------------------------
            CreateBottomSkillHotbarUI(canvasObj.transform, defaultFont, fillSprite, player.combatController);

            // -----------------------------------------------------------------
            // 5. 좌측 상단: 조작 및 실시간 상태 HUD 패널
            // -----------------------------------------------------------------
            GameObject panelObj = new GameObject("HUD_TopLeft_Panel");
            panelObj.transform.SetParent(canvasObj.transform, false);
            Image panelImg = panelObj.AddComponent<Image>();
            panelImg.color = new Color(0.06f, 0.08f, 0.12f, 0.80f);

            RectTransform panelRect = panelObj.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = new Vector2(16f, -14f);
            panelRect.sizeDelta = new Vector2(495f, 220f);

            GameObject textObj = new GameObject("HUD_StatusText");
            textObj.transform.SetParent(panelObj.transform, false);
            Text statusText = textObj.AddComponent<Text>();
            statusText.font = defaultFont;
            statusText.fontSize = 14;
            statusText.lineSpacing = 1.20f;
            statusText.supportRichText = true;
            statusText.alignment = TextAnchor.UpperLeft;
            statusText.color = Color.white;
            statusText.text = "Play 버튼을 눌러 테스트를 시작하세요.";

            RectTransform textRect = textObj.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(14f, 10f);
            textRect.offsetMax = new Vector2(-14f, -10f);

            player.hudStatusText = statusText;

            // -----------------------------------------------------------------
            // 6. 화면 중앙: 보스 처치 시 1.5초간 표시되는 "BOSS CLEAR!" 연출 배너
            // -----------------------------------------------------------------
            GameObject clearBannerRoot = new GameObject("BossClear_CenterBanner");
            clearBannerRoot.transform.SetParent(canvasObj.transform, false);
            Image clearBannerBg = clearBannerRoot.AddComponent<Image>();
            clearBannerBg.color = new Color(0.04f, 0.06f, 0.12f, 0.90f);

            Outline clearOutline = clearBannerRoot.AddComponent<Outline>();
            clearOutline.effectColor = new Color(1f, 0.85f, 0.2f, 0.95f);
            clearOutline.effectDistance = new Vector2(3f, -3f);

            RectTransform clearBannerRect = clearBannerRoot.GetComponent<RectTransform>();
            clearBannerRect.anchorMin = new Vector2(0.5f, 0.5f);
            clearBannerRect.anchorMax = new Vector2(0.5f, 0.5f);
            clearBannerRect.pivot = new Vector2(0.5f, 0.5f);
            clearBannerRect.anchoredPosition = new Vector2(0f, 60f);
            clearBannerRect.sizeDelta = new Vector2(920f, 150f);

            GameObject clearTextObj = new GameObject("BossClear_Text");
            clearTextObj.transform.SetParent(clearBannerRoot.transform, false);
            Text clearBannerText = clearTextObj.AddComponent<Text>();
            clearBannerText.font = defaultFont;
            clearBannerText.fontSize = 44;
            clearBannerText.fontStyle = FontStyle.Bold;
            clearBannerText.supportRichText = true;
            clearBannerText.alignment = TextAnchor.MiddleCenter;
            clearBannerText.color = new Color(1f, 0.92f, 0.25f, 1f);
            clearBannerText.text = "★ BOSS CLEAR! ★";

            RectTransform clearTextRect = clearTextObj.GetComponent<RectTransform>();
            clearTextRect.anchorMin = Vector2.zero;
            clearTextRect.anchorMax = Vector2.one;
            clearTextRect.offsetMin = Vector2.zero;
            clearTextRect.offsetMax = Vector2.zero;
            clearBannerRoot.SetActive(false);

            // -----------------------------------------------------------------
            // 7. 화면 정중앙: 로그라이크 카드 3택 모달 UI & RoguelikeCardManager 생성
            // -----------------------------------------------------------------
            RoguelikeCardManager cardMgr = CreateRoguelikeCardModalUI(canvasObj.transform, defaultFont, boss, player);

            // -----------------------------------------------------------------
            // 8. Stage 3 최종 보스 처치 시 뜨는 "ALL STAGES CLEAR! VICTORY!" 결과창 및 StageManager 생성
            // -----------------------------------------------------------------
            CreateStageManagerAndVictoryModalUI(
                canvasObj.transform,
                defaultFont,
                boss,
                player,
                cardMgr,
                stageHeaderText,
                clearBannerRoot,
                clearBannerText
            );
        }

        private static void CreateStageManagerAndVictoryModalUI(
            Transform canvasTransform,
            Font font,
            BossController boss,
            PlayerController player,
            RoguelikeCardManager cardMgr,
            Text stageHeaderText,
            GameObject clearBannerRoot,
            Text clearBannerText)
        {
            GameObject stageMgrObj = new GameObject("StageManager");
            StageManager stageMgr = stageMgrObj.AddComponent<StageManager>();
            stageMgr.boss = boss;
            stageMgr.player = player;
            stageMgr.cardManager = cardMgr;
            stageMgr.stageHeaderText = stageHeaderText;
            stageMgr.bossClearBannerRoot = clearBannerRoot;
            stageMgr.bossClearBannerText = clearBannerText;

            boss.stageManager = stageMgr;
            player.stageManager = stageMgr;

            // 최종 승리 결과 모달창 ("ALL STAGES CLEAR! VICTORY!")
            GameObject victoryRoot = new GameObject("AllStagesClear_VictoryModalRoot");
            victoryRoot.transform.SetParent(canvasTransform, false);
            Image victoryOverlay = victoryRoot.AddComponent<Image>();
            victoryOverlay.color = new Color(0.02f, 0.03f, 0.07f, 0.90f);

            RectTransform victoryRect = victoryRoot.GetComponent<RectTransform>();
            victoryRect.anchorMin = Vector2.zero;
            victoryRect.anchorMax = Vector2.one;
            victoryRect.offsetMin = Vector2.zero;
            victoryRect.offsetMax = Vector2.zero;

            GameObject cardBox = new GameObject("Victory_ContentBox");
            cardBox.transform.SetParent(victoryRoot.transform, false);
            Image boxBg = cardBox.AddComponent<Image>();
            boxBg.color = new Color(0.09f, 0.11f, 0.18f, 0.98f);

            Outline boxOutline = cardBox.AddComponent<Outline>();
            boxOutline.effectColor = new Color(1f, 0.84f, 0.22f, 0.95f);
            boxOutline.effectDistance = new Vector2(4f, -4f);

            RectTransform boxRect = cardBox.GetComponent<RectTransform>();
            boxRect.anchorMin = new Vector2(0.5f, 0.5f);
            boxRect.anchorMax = new Vector2(0.5f, 0.5f);
            boxRect.pivot = new Vector2(0.5f, 0.5f);
            boxRect.anchoredPosition = new Vector2(0f, 15f);
            boxRect.sizeDelta = new Vector2(980f, 580f);

            GameObject summaryTextObj = new GameObject("Victory_SummaryText");
            summaryTextObj.transform.SetParent(cardBox.transform, false);
            Text summaryText = summaryTextObj.AddComponent<Text>();
            summaryText.font = font;
            summaryText.fontSize = 22;
            summaryText.lineSpacing = 1.35f;
            summaryText.supportRichText = true;
            summaryText.alignment = TextAnchor.MiddleCenter;
            summaryText.color = Color.white;
            summaryText.text = "★ ALL STAGES CLEAR! VICTORY! ★";

            RectTransform summaryRect = summaryTextObj.GetComponent<RectTransform>();
            summaryRect.anchorMin = new Vector2(0f, 0f);
            summaryRect.anchorMax = new Vector2(1f, 1f);
            summaryRect.offsetMin = new Vector2(40f, 120f);
            summaryRect.offsetMax = new Vector2(-40f, -30f);

            // [다시 시작] 버튼
            GameObject restartBtnObj = new GameObject("Restart_Button");
            restartBtnObj.transform.SetParent(cardBox.transform, false);
            Image restartBtnImg = restartBtnObj.AddComponent<Image>();
            restartBtnImg.color = new Color(0.18f, 0.62f, 0.95f, 1f);

            Button restartBtn = restartBtnObj.AddComponent<Button>();
            Outline btnOutline = restartBtnObj.AddComponent<Outline>();
            btnOutline.effectColor = new Color(1f, 0.92f, 0.35f, 1f);
            btnOutline.effectDistance = new Vector2(2f, -2f);

            RectTransform restartRect = restartBtnObj.GetComponent<RectTransform>();
            restartRect.anchorMin = new Vector2(0.5f, 0f);
            restartRect.anchorMax = new Vector2(0.5f, 0f);
            restartRect.pivot = new Vector2(0.5f, 0f);
            restartRect.anchoredPosition = new Vector2(0f, 32f);
            restartRect.sizeDelta = new Vector2(360f, 66f);

            GameObject restartTxtObj = new GameObject("Restart_ButtonText");
            restartTxtObj.transform.SetParent(restartBtnObj.transform, false);
            Text restartTxt = restartTxtObj.AddComponent<Text>();
            restartTxt.font = font;
            restartTxt.fontSize = 24;
            restartTxt.fontStyle = FontStyle.Bold;
            restartTxt.alignment = TextAnchor.MiddleCenter;
            restartTxt.color = Color.white;
            restartTxt.text = "▶ 다시 시작 (STAGE 1부터)";

            RectTransform restartTxtRect = restartTxtObj.GetComponent<RectTransform>();
            restartTxtRect.anchorMin = Vector2.zero;
            restartTxtRect.anchorMax = Vector2.one;
            restartTxtRect.offsetMin = Vector2.zero;
            restartTxtRect.offsetMax = Vector2.zero;

            victoryRoot.SetActive(false);

            stageMgr.victoryModalRoot = victoryRoot;
            stageMgr.victorySummaryText = summaryText;
            stageMgr.restartButton = restartBtn;
        }

        private static void CreateBottomSkillHotbarUI(
            Transform canvasTransform,
            Font font,
            Sprite fillSprite,
            PlayerCombatController combatController)
        {
            GameObject hotbarRoot = new GameObject("Bottom_SkillHotbar_Panel");
            hotbarRoot.transform.SetParent(canvasTransform, false);
            Image hotbarBg = hotbarRoot.AddComponent<Image>();
            hotbarBg.color = new Color(0.05f, 0.06f, 0.10f, 0.90f);

            RectTransform hotbarRect = hotbarRoot.GetComponent<RectTransform>();
            hotbarRect.anchorMin = new Vector2(0.5f, 0f);
            hotbarRect.anchorMax = new Vector2(0.5f, 0f);
            hotbarRect.pivot = new Vector2(0.5f, 0f);
            hotbarRect.anchoredPosition = new Vector2(0f, 18f);
            hotbarRect.sizeDelta = new Vector2(740f, 195f);

            GameObject summaryObj = new GameObject("Hotbar_GcdSummaryText");
            summaryObj.transform.SetParent(hotbarRoot.transform, false);
            Text summaryText = summaryObj.AddComponent<Text>();
            summaryText.font = font;
            summaryText.fontSize = 15;
            summaryText.supportRichText = true;
            summaryText.alignment = TextAnchor.MiddleCenter;
            summaryText.color = new Color(0.88f, 0.94f, 1f, 1f);
            summaryText.text = "현재 글로벌 쿨타임(GCD): <b>2.0초</b>  |  [1]·[2] 웨폰스킬(GCD) 사용 후 글쿨 중 [3]·[4] 어빌리티(oGCD) 끼워넣기 가능!";

            RectTransform summaryRect = summaryObj.GetComponent<RectTransform>();
            summaryRect.anchorMin = new Vector2(0f, 1f);
            summaryRect.anchorMax = new Vector2(1f, 1f);
            summaryRect.pivot = new Vector2(0.5f, 1f);
            summaryRect.anchoredPosition = new Vector2(0f, -6f);
            summaryRect.sizeDelta = new Vector2(-16f, 24f);

            SkillHotbarUI hotbarUI = hotbarRoot.AddComponent<SkillHotbarUI>();
            hotbarUI.combatController = combatController;
            hotbarUI.gcdSummaryText = summaryText;
            hotbarUI.slots = new HotbarSlotView[4];

            if (combatController != null)
            {
                combatController.hotbarUI = hotbarUI;
            }

            string[] keyLabels =
            {
                "[1] GCD",
                "[2] GCD",
                "[3] oGCD",
                "[4] oGCD"
            };

            string[] defaultStageLabels =
            {
                "1단계 (측면)",
                "1단계 (후면)",
                "oGCD 생존기",
                "oGCD 끼워넣기"
            };

            string[] defaultSkillNames =
            {
                "<b>섬광 베기</b>\n<size=13>기본 100</size>",
                "<b>그림자 베기</b>\n<size=13>기본 100</size>",
                "<b>개인 생존기</b>\n<size=13>3초간 피해 80% 감소</size>",
                "<b>질풍 찌르기</b>\n<size=13>즉발 추가 피해 180</size>"
            };

            Color[] defaultIconColors =
            {
                new Color(0.14f, 0.34f, 0.22f, 0.96f),
                new Color(0.14f, 0.25f, 0.42f, 0.96f),
                new Color(0.14f, 0.32f, 0.56f, 0.96f),
                new Color(0.12f, 0.42f, 0.42f, 0.96f)
            };

            float[] slotX = { -255f, -85f, 85f, 255f };

            for (int i = 0; i < 4; i++)
            {
                HotbarSlotView view = new HotbarSlotView();

                GameObject slotRoot = new GameObject($"SkillSlot_{i + 1}");
                slotRoot.transform.SetParent(hotbarRoot.transform, false);
                Image borderImg = slotRoot.AddComponent<Image>();
                borderImg.color = new Color(0.28f, 0.32f, 0.40f, 0.9f);
                Button slotBtn = slotRoot.AddComponent<Button>();

                RectTransform slotRect = slotRoot.GetComponent<RectTransform>();
                slotRect.anchorMin = new Vector2(0.5f, 0f);
                slotRect.anchorMax = new Vector2(0.5f, 0f);
                slotRect.pivot = new Vector2(0.5f, 0f);
                slotRect.anchoredPosition = new Vector2(slotX[i], 36f);
                slotRect.sizeDelta = new Vector2(150f, 124f);

                GameObject iconBgObj = new GameObject("Icon_Background");
                iconBgObj.transform.SetParent(slotRoot.transform, false);
                Image iconBgImg = iconBgObj.AddComponent<Image>();
                iconBgImg.color = defaultIconColors[i];

                RectTransform iconBgRect = iconBgObj.GetComponent<RectTransform>();
                iconBgRect.anchorMin = Vector2.zero;
                iconBgRect.anchorMax = Vector2.one;
                iconBgRect.offsetMin = new Vector2(5f, 5f);
                iconBgRect.offsetMax = new Vector2(-5f, -5f);

                GameObject keyBadgeObj = new GameObject("Keybind_Badge");
                keyBadgeObj.transform.SetParent(iconBgObj.transform, false);
                Text keyText = keyBadgeObj.AddComponent<Text>();
                keyText.font = font;
                keyText.fontSize = 15;
                keyText.fontStyle = FontStyle.Bold;
                keyText.alignment = TextAnchor.UpperLeft;
                keyText.color = new Color(1f, 0.95f, 0.4f, 1f);
                keyText.text = keyLabels[i];

                RectTransform keyRect = keyBadgeObj.GetComponent<RectTransform>();
                keyRect.anchorMin = new Vector2(0f, 1f);
                keyRect.anchorMax = new Vector2(1f, 1f);
                keyRect.pivot = new Vector2(0.5f, 1f);
                keyRect.anchoredPosition = new Vector2(0f, -5f);
                keyRect.sizeDelta = new Vector2(-12f, 22f);

                GameObject stageObj = new GameObject("Stage_Badge");
                stageObj.transform.SetParent(iconBgObj.transform, false);
                Text stageText = stageObj.AddComponent<Text>();
                stageText.font = font;
                stageText.fontSize = 14;
                stageText.fontStyle = FontStyle.Bold;
                stageText.alignment = TextAnchor.UpperRight;
                stageText.color = Color.white;
                stageText.text = defaultStageLabels[i];

                RectTransform stageRect = stageObj.GetComponent<RectTransform>();
                stageRect.anchorMin = new Vector2(0f, 1f);
                stageRect.anchorMax = new Vector2(1f, 1f);
                stageRect.pivot = new Vector2(0.5f, 1f);
                stageRect.anchoredPosition = new Vector2(0f, -25f);
                stageRect.sizeDelta = new Vector2(-12f, 20f);

                GameObject nameObj = new GameObject("SkillName_Text");
                nameObj.transform.SetParent(iconBgObj.transform, false);
                Text nameText = nameObj.AddComponent<Text>();
                nameText.font = font;
                nameText.fontSize = 16;
                nameText.lineSpacing = 1.15f;
                nameText.supportRichText = true;
                nameText.alignment = TextAnchor.MiddleCenter;
                nameText.color = Color.white;
                nameText.text = defaultSkillNames[i];

                RectTransform nameRect = nameObj.GetComponent<RectTransform>();
                nameRect.anchorMin = Vector2.zero;
                nameRect.anchorMax = Vector2.one;
                nameRect.offsetMin = new Vector2(6f, 6f);
                nameRect.offsetMax = new Vector2(-6f, -38f);

                GameObject cdOverlayObj = new GameObject("Cooldown_Overlay");
                cdOverlayObj.transform.SetParent(iconBgObj.transform, false);
                Image cdOverlayImg = cdOverlayObj.AddComponent<Image>();
                cdOverlayImg.sprite = fillSprite;
                cdOverlayImg.color = new Color(0.02f, 0.03f, 0.05f, 0.78f);
                cdOverlayImg.type = Image.Type.Filled;
                cdOverlayImg.fillMethod = Image.FillMethod.Vertical;
                cdOverlayImg.fillOrigin = (int)Image.OriginVertical.Bottom;
                cdOverlayImg.fillAmount = 0f;

                RectTransform cdOverlayRect = cdOverlayObj.GetComponent<RectTransform>();
                cdOverlayRect.anchorMin = Vector2.zero;
                cdOverlayRect.anchorMax = Vector2.one;
                cdOverlayRect.offsetMin = Vector2.zero;
                cdOverlayRect.offsetMax = Vector2.zero;
                cdOverlayObj.SetActive(false);

                GameObject cdNumObj = new GameObject("Cooldown_NumberText");
                cdNumObj.transform.SetParent(iconBgObj.transform, false);
                Text cdNumText = cdNumObj.AddComponent<Text>();
                cdNumText.font = font;
                cdNumText.fontSize = 30;
                cdNumText.fontStyle = FontStyle.Bold;
                cdNumText.alignment = TextAnchor.MiddleCenter;
                cdNumText.color = new Color(1f, 0.95f, 0.35f, 1f);
                cdNumText.text = "";

                Shadow cdShadow = cdNumObj.AddComponent<Shadow>();
                cdShadow.effectColor = Color.black;
                cdShadow.effectDistance = new Vector2(2f, -2f);

                RectTransform cdNumRect = cdNumObj.GetComponent<RectTransform>();
                cdNumRect.anchorMin = Vector2.zero;
                cdNumRect.anchorMax = Vector2.one;
                cdNumRect.offsetMin = Vector2.zero;
                cdNumRect.offsetMax = Vector2.zero;
                cdNumObj.SetActive(false);

                GameObject comboBarRoot = new GameObject($"ComboTimerBar_Slot{i + 1}");
                comboBarRoot.transform.SetParent(hotbarRoot.transform, false);
                Image comboBarBg = comboBarRoot.AddComponent<Image>();
                comboBarBg.color = new Color(0.14f, 0.15f, 0.20f, 0.95f);

                RectTransform comboBarRect = comboBarRoot.GetComponent<RectTransform>();
                comboBarRect.anchorMin = new Vector2(0.5f, 0f);
                comboBarRect.anchorMax = new Vector2(0.5f, 0f);
                comboBarRect.pivot = new Vector2(0.5f, 0f);
                comboBarRect.anchoredPosition = new Vector2(slotX[i], 10f);
                comboBarRect.sizeDelta = new Vector2(150f, 20f);

                GameObject comboFillObj = new GameObject("ComboTimer_Fill");
                comboFillObj.transform.SetParent(comboBarRoot.transform, false);
                Image comboFillImg = comboFillObj.AddComponent<Image>();
                comboFillImg.sprite = fillSprite;
                comboFillImg.color = new Color(1f, 0.88f, 0.20f, 1f);
                comboFillImg.type = Image.Type.Filled;
                comboFillImg.fillMethod = Image.FillMethod.Horizontal;
                comboFillImg.fillOrigin = (int)Image.OriginHorizontal.Left;
                comboFillImg.fillAmount = 1f;

                RectTransform comboFillRect = comboFillObj.GetComponent<RectTransform>();
                comboFillRect.anchorMin = Vector2.zero;
                comboFillRect.anchorMax = Vector2.one;
                comboFillRect.offsetMin = new Vector2(2f, 2f);
                comboFillRect.offsetMax = new Vector2(-2f, -2f);

                GameObject comboTxtObj = new GameObject("ComboTimer_Text");
                comboTxtObj.transform.SetParent(comboBarRoot.transform, false);
                Text comboTxt = comboTxtObj.AddComponent<Text>();
                comboTxt.font = font;
                comboTxt.fontSize = 13;
                comboTxt.fontStyle = FontStyle.Bold;
                comboTxt.alignment = TextAnchor.MiddleCenter;
                comboTxt.color = Color.black;
                comboTxt.text = "콤보 8.0s";

                RectTransform comboTxtRect = comboTxtObj.GetComponent<RectTransform>();
                comboTxtRect.anchorMin = Vector2.zero;
                comboTxtRect.anchorMax = Vector2.one;
                comboTxtRect.offsetMin = Vector2.zero;
                comboTxtRect.offsetMax = Vector2.zero;

                comboBarRoot.SetActive(false);

                view.slotButton = slotBtn;
                view.borderHighlightImage = borderImg;
                view.iconBackgroundImage = iconBgImg;
                view.keybindBadgeText = keyText;
                view.stageBadgeText = stageText;
                view.skillNameText = nameText;
                view.cooldownOverlayImage = cdOverlayImg;
                view.cooldownNumberText = cdNumText;
                view.comboTimerBarRoot = comboBarRoot;
                view.comboTimerFillImage = comboFillImg;
                view.comboTimerText = comboTxt;

                hotbarUI.slots[i] = view;
            }
        }

        private static void CreateHpBarThresholdLine(Transform parent, string name, float normalizedX, Color color, float width)
        {
            GameObject lineObj = new GameObject(name);
            lineObj.transform.SetParent(parent, false);
            Image img = lineObj.AddComponent<Image>();
            img.color = color;

            RectTransform rt = lineObj.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(normalizedX, 0f);
            rt.anchorMax = new Vector2(normalizedX, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(width, 6f);
        }

        private static RoguelikeCardManager CreateRoguelikeCardModalUI(
            Transform canvasTransform,
            Font font,
            BossController boss,
            PlayerController player)
        {
            GameObject managerObj = new GameObject("RoguelikeCardManager");
            RoguelikeCardManager cardMgr = managerObj.AddComponent<RoguelikeCardManager>();
            cardMgr.boss = boss;
            cardMgr.player = player;
            player.cardManager = cardMgr;

            GameObject modalRoot = new GameObject("RoguelikeCard_ModalRoot");
            modalRoot.transform.SetParent(canvasTransform, false);
            Image overlayImg = modalRoot.AddComponent<Image>();
            overlayImg.color = new Color(0.02f, 0.03f, 0.06f, 0.85f);

            RectTransform modalRect = modalRoot.GetComponent<RectTransform>();
            modalRect.anchorMin = Vector2.zero;
            modalRect.anchorMax = Vector2.one;
            modalRect.offsetMin = Vector2.zero;
            modalRect.offsetMax = Vector2.zero;

            GameObject headerObj = new GameObject("Modal_HeaderText");
            headerObj.transform.SetParent(modalRoot.transform, false);
            Text headerText = headerObj.AddComponent<Text>();
            headerText.font = font;
            headerText.fontSize = 32;
            headerText.fontStyle = FontStyle.Bold;
            headerText.alignment = TextAnchor.MiddleCenter;
            headerText.color = new Color(1f, 0.90f, 0.30f, 1f);
            headerText.text = "★ STAGE CLEAR! 클리어 보상 로그라이크 특성 카드를 1장 선택하세요 ★";

            RectTransform headerRect = headerObj.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0.5f, 0.5f);
            headerRect.anchorMax = new Vector2(0.5f, 0.5f);
            headerRect.pivot = new Vector2(0.5f, 0.5f);
            headerRect.anchoredPosition = new Vector2(0f, 255f);
            headerRect.sizeDelta = new Vector2(1400f, 65f);

            cardMgr.cardSelectionModalRoot = modalRoot;
            cardMgr.modalHeaderText = headerText;
            cardMgr.cardButtons = new Button[3];
            cardMgr.cardAccentBanners = new Image[3];
            cardMgr.cardTitleTexts = new Text[3];
            cardMgr.cardDescTexts = new Text[3];

            float[] cardXPositions = { -390f, 0f, 390f };

            for (int i = 0; i < 3; i++)
            {
                GameObject cardBtnObj = new GameObject($"CardButton_{i + 1}");
                cardBtnObj.transform.SetParent(modalRoot.transform, false);

                Image cardBg = cardBtnObj.AddComponent<Image>();
                cardBg.color = new Color(0.11f, 0.13f, 0.19f, 0.98f);

                Button btn = cardBtnObj.AddComponent<Button>();
                ColorBlock cb = btn.colors;
                cb.normalColor = Color.white;
                cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
                cb.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
                btn.colors = cb;

                Outline outline = cardBtnObj.AddComponent<Outline>();
                outline.effectColor = new Color(0.95f, 0.80f, 0.25f, 0.9f);
                outline.effectDistance = new Vector2(3f, -3f);

                RectTransform cardRect = cardBtnObj.GetComponent<RectTransform>();
                cardRect.anchorMin = new Vector2(0.5f, 0.5f);
                cardRect.anchorMax = new Vector2(0.5f, 0.5f);
                cardRect.pivot = new Vector2(0.5f, 0.5f);
                cardRect.anchoredPosition = new Vector2(cardXPositions[i], -15f);
                cardRect.sizeDelta = new Vector2(345f, 430f);

                GameObject accentObj = new GameObject("Card_AccentBanner");
                accentObj.transform.SetParent(cardBtnObj.transform, false);
                Image accentImg = accentObj.AddComponent<Image>();
                accentImg.color = new Color(1f, 0.5f, 0.2f, 1f);
                RectTransform accentRect = accentObj.GetComponent<RectTransform>();
                accentRect.anchorMin = new Vector2(0f, 1f);
                accentRect.anchorMax = new Vector2(1f, 1f);
                accentRect.pivot = new Vector2(0.5f, 1f);
                accentRect.anchoredPosition = Vector2.zero;
                accentRect.sizeDelta = new Vector2(0f, 14f);

                GameObject titleObj = new GameObject("Card_TitleText");
                titleObj.transform.SetParent(cardBtnObj.transform, false);
                Text titleTxt = titleObj.AddComponent<Text>();
                titleTxt.font = font;
                titleTxt.fontSize = 25;
                titleTxt.fontStyle = FontStyle.Bold;
                titleTxt.supportRichText = true;
                titleTxt.alignment = TextAnchor.MiddleCenter;
                titleTxt.color = new Color(1f, 0.85f, 0.3f, 1f);
                titleTxt.text = $"[특성 카드 {i + 1}]";

                RectTransform titleRect = titleObj.GetComponent<RectTransform>();
                titleRect.anchorMin = new Vector2(0f, 1f);
                titleRect.anchorMax = new Vector2(1f, 1f);
                titleRect.pivot = new Vector2(0.5f, 1f);
                titleRect.anchoredPosition = new Vector2(0f, -26f);
                titleRect.sizeDelta = new Vector2(-24f, 95f);

                GameObject descObj = new GameObject("Card_DescText");
                descObj.transform.SetParent(cardBtnObj.transform, false);
                Text descTxt = descObj.AddComponent<Text>();
                descTxt.font = font;
                descTxt.fontSize = 20;
                descTxt.lineSpacing = 1.35f;
                descTxt.supportRichText = true;
                descTxt.alignment = TextAnchor.MiddleCenter;
                descTxt.color = new Color(0.92f, 0.94f, 0.98f, 1f);
                descTxt.text = "카드 효과 설명";

                RectTransform descRect = descObj.GetComponent<RectTransform>();
                descRect.anchorMin = new Vector2(0f, 0f);
                descRect.anchorMax = new Vector2(1f, 1f);
                descRect.offsetMin = new Vector2(22f, 28f);
                descRect.offsetMax = new Vector2(-22f, -125f);

                cardMgr.cardButtons[i] = btn;
                cardMgr.cardAccentBanners[i] = accentImg;
                cardMgr.cardTitleTexts[i] = titleTxt;
                cardMgr.cardDescTexts[i] = descTxt;
            }

            modalRoot.SetActive(false);
            return cardMgr;
        }

        private static Material GetOrCreateLitMaterialAsset(string matName, Color color)
        {
            string assetPath = $"{GeneratedMaterialFolder}/{matName}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(assetPath);

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            if (mat == null)
            {
                mat = new Material(shader) { name = matName };
                AssetDatabase.CreateAsset(mat, assetPath);
            }
            else
            {
                mat.shader = shader;
            }

            mat.color = color;
            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", color);
            }
            if (mat.HasProperty("_Color"))
            {
                mat.SetColor("_Color", color);
            }
            if (mat.HasProperty("_Smoothness"))
            {
                mat.SetFloat("_Smoothness", 0.35f);
            }

            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material GetOrCreateUnlitLineMaterialAsset()
        {
            string assetPath = $"{GeneratedMaterialFolder}/Mat_LineUnlit.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(assetPath);

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }

            if (mat == null)
            {
                mat = new Material(shader)
                {
                    name = "Mat_LineUnlit",
                    color = Color.white
                };
                AssetDatabase.CreateAsset(mat, assetPath);
            }
            else
            {
                mat.shader = shader;
                mat.color = Color.white;
            }

            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
#endif
