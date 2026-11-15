using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace EchoesOfFailure
{
    /// <summary>
    /// Creates the complete hackathon game from a deliberately tiny scene.
    /// Keeping construction here makes the project easy to move, build, and demo.
    /// </summary>
    public sealed class EchoesGame : MonoBehaviour
    {
        public static EchoesGame Instance { get; private set; }

        private readonly List<EchoSpring> echoes = new List<EchoSpring>();
        private readonly List<Texture2D> generatedTextures = new List<Texture2D>();
        private readonly List<Material> generatedMaterials = new List<Material>();
        private readonly List<float> sectorHeights = new List<float>();

        private const int TotalSectors = 9;
        private const int CheckpointInterval = 2;
        private const float GenerationLookAhead = 14f;
        private const float RespawnProtectionDuration = 1.1f;
        private const float RespawnEchoClearRadius = 2.2f;

        private enum PresentationState
        {
            Home,
            Story,
            Playing,
            Paused
        }

        private readonly Color backgroundColor = Hex("#05080D");
        private readonly Color distantColor = Hex("#111820");
        private readonly Color platformColor = Hex("#1B252D");
        private readonly Color platformTopColor = Hex("#82909A");
        private readonly Color playerColor = Hex("#EEF2F3");
        private readonly Color echoColor = Hex("#6FE7FF");
        private readonly Color hazardColor = Hex("#FF7047");
        private readonly Color exitColor = Hex("#F0C987");

        private Sprite squareSprite;
        private Sprite circleSprite;
        private Sprite ringSprite;
        private Sprite robotIdle;
        private Sprite robotDriveA;
        private Sprite robotDriveB;
        private Sprite robotJump;
        private Sprite robotHurt;
        private Sprite platformLeftSprite;
        private Sprite platformMiddleSprite;
        private Sprite platformRightSprite;
        private Sprite wallPanelLeftSprite;
        private Sprite wallPanelMiddleSprite;
        private Sprite wallPanelRightSprite;
        private Sprite monitorSprite;
        private Sprite consoleSprite;
        private Sprite gearSprite;
        private Sprite failureLampSprite;
        private Sprite checkpointLampSprite;
        private Sprite finalLampSprite;
        private Sprite checkpointModuleSprite;
        private Sprite warningPanelSprite;
        private Sprite columnSprite;
        private Sprite columnCapSprite;
        private Sprite lightNoiseSprite;
        private Sprite lightRingNoiseSprite;
        private Sprite lightConeSprite;
        private Sprite windowLightSprite;
        private Sprite titleArtSprite;
        private Sprite leftFadeSprite;
        private Sprite scanlineSprite;

        private AudioClip jumpClip;
        private AudioClip deathClip;
        private AudioClip echoClip;
        private AudioClip bounceClip;
        private AudioClip victoryClip;
        private AudioClip ambienceClip;
        private AudioClip typeTickClip;
        private AudioClip menuConfirmClip;
        private AudioClip pauseClip;
        private AudioClip landClip;
        private AudioSource sfxSource;
        private AudioSource ambienceSource;

        private EchoesPlayerController player;
        private Transform worldRoot;
        private Transform geometryRoot;
        private Transform sectorBackdropRoot;
        private Transform echoContainer;
        private Vector3 spawnPoint = new Vector3(-4.8f, 0.72f, 0f);
        private Vector3 checkpointSpawnPoint;
        private Vector3 exitPosition;
        private Text deathCountText;
        private Text sectorText;
        private Text seedText;
        private Text altitudeText;
        private Text promptText;
        private Text victoryTitleText;
        private Text victoryDetailText;
        private CanvasGroup victoryPanel;
        private CanvasGroup firstDeathBanner;
        private Text firstDeathBannerText;
        private CanvasGroup checkpointBanner;
        private Text checkpointBannerText;
        private RectTransform progressFill;
        private Image impactFlash;
        private CanvasGroup gameplayHud;
        private CanvasGroup titlePanel;
        private CanvasGroup storyPanel;
        private CanvasGroup pausePanel;
        private Text storyText;
        private Text storyContinueText;
        private Font uiFont;

        private bool respawning;
        private bool won;
        private int deathCount;
        private int towerSeed;
        private int generatedSectorCount;
        private int highestSectorReached;
        private int activeCheckpointSector;
        private int towerGenerationId;
        private float topGeneratedY;
        private float previousPlatformX;
        private float previousLaunchX;
        private float promptRefreshTimer;
        private float respawnProtectionTimer;
        private System.Random towerRandom;
        private PresentationState presentationState = PresentationState.Home;
        private bool storyTyping;
        private bool storyComplete;
        private bool skipStoryTyping;

        public bool IsWon => won;
        public bool IsRespawning => respawning;
        public bool IsPlayerProtected => respawning || respawnProtectionTimer > 0f || won;
        public Color EchoColor => echoColor;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            Application.targetFrameRate = 120;
            Physics2D.gravity = new Vector2(0f, -9.81f);
            BuildGame();
        }

        private void Update()
        {
            if (presentationState == PresentationState.Home)
            {
                if (ContinuePressed())
                {
                    BeginStory();
                }
                return;
            }

            if (presentationState == PresentationState.Story)
            {
                if (PausePressed())
                {
                    ShowHomeScreen();
                }
                else if (ContinuePressed())
                {
                    if (storyTyping)
                    {
                        skipStoryTyping = true;
                    }
                    else if (storyComplete)
                    {
                        BeginRun();
                    }
                }
                return;
            }

            if (presentationState == PresentationState.Paused)
            {
                if (PausePressed() || ContinuePressed())
                {
                    ResumeRun();
                }
                else if (RestartPressed())
                {
                    ResumeRun();
                    RestartRun();
                }
                else if (TitlePressed())
                {
                    ReturnToTitle();
                }
                return;
            }

            if (PausePressed() && !won)
            {
                PauseRun();
                return;
            }

            respawnProtectionTimer = Mathf.Max(0f, respawnProtectionTimer - Time.deltaTime);

            if (RestartPressed())
            {
                RestartRun();
            }

            if (player != null && !won)
            {
                EnsureTowerAhead();
                UpdateSectorProgress();
                if (!respawning && player.transform.position.y < checkpointSpawnPoint.y - 8.5f)
                {
                    KillPlayer(player);
                }
            }

            promptRefreshTimer -= Time.unscaledDeltaTime;
            if (promptRefreshTimer <= 0f)
            {
                promptRefreshTimer = 0.15f;
                RefreshPrompt();
            }
        }

        private void BuildGame()
        {
            CreateGeneratedSprites();
            LoadResources();

            worldRoot = new GameObject("World").transform;
            CreateCamera();
            CreateBackground(worldRoot);
            CreateAmbientParticles();

            geometryRoot = new GameObject("Procedural Geometry").transform;
            geometryRoot.SetParent(worldRoot, false);
            sectorBackdropRoot = new GameObject("Generated Sector Backdrops").transform;
            sectorBackdropRoot.SetParent(worldRoot, false);
            echoContainer = new GameObject("Persistent Echoes").transform;
            echoContainer.SetParent(worldRoot, false);

            BuildProceduralTower(true);
            CreatePlayer(worldRoot);
            CreateUI();
            CreateAudio();
            RefreshUI();
            ShowHomeScreen();
        }

        private void CreateGeneratedSprites()
        {
            squareSprite = CreateSprite("Runtime Square", 16, (x, y, size) => Color.white);
            circleSprite = CreateSprite("Runtime Glow", 96, (x, y, size) =>
            {
                Vector2 uv = new Vector2((x + 0.5f) / size, (y + 0.5f) / size);
                float distance = Vector2.Distance(uv, Vector2.one * 0.5f) * 2f;
                return new Color(1f, 1f, 1f, Mathf.Clamp01(1f - distance));
            });
            ringSprite = CreateSprite("Runtime Ring", 96, (x, y, size) =>
            {
                Vector2 uv = new Vector2((x + 0.5f) / size, (y + 0.5f) / size);
                float distance = Vector2.Distance(uv, Vector2.one * 0.5f) * 2f;
                float alpha = Mathf.Clamp01(1f - Mathf.Abs(distance - 0.72f) * 15f);
                return new Color(1f, 1f, 1f, alpha);
            });
            leftFadeSprite = CreateSprite("Runtime Left Fade", 128, (x, y, size) =>
            {
                float alpha = 1f - Mathf.SmoothStep(0.22f, 0.98f, (x + 0.5f) / size);
                return new Color(1f, 1f, 1f, alpha);
            });
            scanlineSprite = CreateSprite("Runtime Scanlines", 8, (x, y, size) =>
            {
                float alpha = y % 4 == 0 ? 0.72f : 0f;
                return new Color(1f, 1f, 1f, alpha);
            });
        }

        private Sprite CreateSprite(string spriteName, int size, Func<int, int, int, Color> pixel)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = spriteName + " Texture",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            Color[] colors = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    colors[y * size + x] = pixel(x, y, size);
                }
            }
            texture.SetPixels(colors);
            texture.Apply(false, false);
            generatedTextures.Add(texture);
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), Vector2.one * 0.5f, size, 0, SpriteMeshType.FullRect);
        }

        private void LoadResources()
        {
            robotIdle = SpriteFromTexture("Echoes/Robot/robot_blueBody", 78f);
            robotDriveA = SpriteFromTexture("Echoes/Robot/robot_blueDrive1", 78f);
            robotDriveB = SpriteFromTexture("Echoes/Robot/robot_blueDrive2", 78f);
            robotJump = SpriteFromTexture("Echoes/Robot/robot_blueJump", 78f);
            robotHurt = SpriteFromTexture("Echoes/Robot/robot_blueHurt", 78f);

            platformLeftSprite = SpriteFromTexture("Echoes/Industrial/platformIndustrial_087", 140f);
            platformMiddleSprite = SpriteFromTexture("Echoes/Industrial/platformIndustrial_088", 140f);
            platformRightSprite = SpriteFromTexture("Echoes/Industrial/platformIndustrial_089", 140f);
            wallPanelLeftSprite = SpriteFromTexture("Echoes/Industrial/platformIndustrial_073", 140f);
            wallPanelMiddleSprite = SpriteFromTexture("Echoes/Industrial/platformIndustrial_074", 140f);
            wallPanelRightSprite = SpriteFromTexture("Echoes/Industrial/platformIndustrial_075", 140f);
            monitorSprite = SpriteFromTexture("Echoes/Industrial/platformIndustrial_032", 140f);
            consoleSprite = SpriteFromTexture("Echoes/Industrial/platformIndustrial_045", 140f);
            gearSprite = SpriteFromTexture("Echoes/Industrial/platformIndustrial_067", 140f);
            failureLampSprite = SpriteFromTexture("Echoes/Industrial/platformIndustrial_056", 140f);
            checkpointLampSprite = SpriteFromTexture("Echoes/Industrial/platformIndustrial_039", 140f);
            finalLampSprite = SpriteFromTexture("Echoes/Industrial/platformIndustrial_040", 140f);
            checkpointModuleSprite = SpriteFromTexture("Echoes/Industrial/platformIndustrial_090", 140f);
            warningPanelSprite = SpriteFromTexture("Echoes/Industrial/platformIndustrial_081", 140f);
            columnSprite = SpriteFromTexture("Echoes/Industrial/platformIndustrial_085", 140f);
            columnCapSprite = SpriteFromTexture("Echoes/Industrial/platformIndustrial_086", 140f);
            lightNoiseSprite = SpriteFromTexture("Echoes/Lighting/circle_b_noise", 512f);
            lightRingNoiseSprite = SpriteFromTexture("Echoes/Lighting/circle_rings_b_noise", 512f);
            lightConeSprite = SpriteFromTexture("Echoes/Lighting/cone_composed_c_noise", 512f);
            windowLightSprite = SpriteFromTexture("Echoes/Lighting/window_h_noise", 512f);
            titleArtSprite = SpriteFromTexture("Echoes/V4/title_environment_v4", 100f, true);
            uiFont = Resources.Load<Font>("Echoes/V4/LiberationSans") ??
                     Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            jumpClip = Resources.Load<AudioClip>("Echoes/Audio/forceField_000");
            deathClip = Resources.Load<AudioClip>("Echoes/Audio/lowFrequency_explosion_000");
            echoClip = Resources.Load<AudioClip>("Echoes/Audio/laserRetro_004");
            bounceClip = Resources.Load<AudioClip>("Echoes/Audio/thrusterFire_000");
            victoryClip = Resources.Load<AudioClip>("Echoes/Audio/doorOpen_002");
            ambienceClip = Resources.Load<AudioClip>("Echoes/Audio/computerNoise_002");
        }

        private Sprite SpriteFromTexture(string path, float pixelsPerUnit, bool pointFilter = false)
        {
            Texture2D texture = Resources.Load<Texture2D>(path);
            if (texture == null)
            {
                return squareSprite;
            }

            if (pointFilter)
            {
                texture.filterMode = FilterMode.Point;
            }

            return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), Vector2.one * 0.5f,
                pixelsPerUnit, 0, SpriteMeshType.FullRect);
        }

        private void CreateCamera()
        {
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5.5f;
            camera.backgroundColor = backgroundColor;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.nearClipPlane = -10f;
            camera.farClipPlane = 100f;
            cameraObject.transform.position = new Vector3(0.4f, 3.1f, -10f);
            cameraObject.AddComponent<AudioListener>();
            EchoesCameraFollow follow = cameraObject.AddComponent<EchoesCameraFollow>();
            follow.Configure(0f, 3.1f, 200f);
        }

        private void CreateBackground(Transform world)
        {
            Transform background = new GameObject("Foundry Architecture").transform;
            background.SetParent(world, false);

            CreateVisual("Abyss", new Vector2(0f, 22f), new Vector2(30f, 64f), backgroundColor, -100, background);
            CreateVisual("Left Megastructure", new Vector2(-6.25f, 22f), new Vector2(3.4f, 64f),
                Hex("#0B1117"), -98, background);
            CreateVisual("Right Megastructure", new Vector2(6.25f, 22f), new Vector2(3.4f, 64f),
                Hex("#0B1117"), -98, background);
            CreateVisual("Central Lift Void", new Vector2(0f, 22f), new Vector2(1.4f, 64f),
                Hex("#080C11"), -97, background);
            CreateVisual("Atmospheric Shaft", new Vector2(0f, 22f), new Vector2(13f, 54f),
                WithAlpha(Hex("#27414C"), 0.075f), -96, background, windowLightSprite);

            for (int i = 0; i < 16; i++)
            {
                float y = -1f + i * 3.15f;
                float side = i % 2 == 0 ? -1f : 1f;
                Sprite panel = i % 3 == 0 ? wallPanelLeftSprite : (i % 3 == 1 ? wallPanelMiddleSprite : wallPanelRightSprite);
                CreateVisual("Distant Service Module " + i, new Vector2(side * 5.25f, y), new Vector2(1.65f, 1.65f),
                    WithAlpha(Hex("#34414A"), 0.42f), -93, background, panel);
                if (i % 4 == 1)
                {
                    CreateVisual("Distant Turbine " + i, new Vector2(-side * 5.2f, y + 0.8f), new Vector2(1.25f, 1.25f),
                        WithAlpha(Hex("#26333B"), 0.38f), -94, background, gearSprite);
                }
            }
        }

        private void CreateAmbientParticles()
        {
            GameObject dust = new GameObject("Volumetric Data Dust");
            dust.transform.SetParent(Camera.main.transform, false);
            dust.transform.localPosition = new Vector3(0f, 0f, 8f);
            ParticleSystem particles = dust.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = particles.main;
            main.loop = true;
            main.duration = 4f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.015f, 0.07f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.035f);
            main.startColor = new ParticleSystem.MinMaxGradient(WithAlpha(platformTopColor, 0.07f), WithAlpha(playerColor, 0.16f));
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 42;
            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 3.2f;
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(13f, 11f, 0.2f);
            ParticleSystemRenderer renderer = dust.GetComponent<ParticleSystemRenderer>();
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Particles/Standard Unlit");
            if (shader != null)
            {
                Material material = new Material(shader) { color = Color.white };
                generatedMaterials.Add(material);
                renderer.material = material;
            }
            renderer.sortingOrder = -70;
            particles.Play();
        }

        private void BuildProceduralTower(bool reseed)
        {
            towerGenerationId++;
            if (reseed || towerRandom == null)
            {
                towerSeed = (Environment.TickCount ^ unchecked((int)DateTime.UtcNow.Ticks)) & 0x7fffffff;
                towerSeed = 100000 + towerSeed % 900000;
            }
            towerRandom = new System.Random(towerSeed);
            generatedSectorCount = 0;
            highestSectorReached = 0;
            activeCheckpointSector = 0;
            topGeneratedY = 0f;
            previousPlatformX = 0f;
            previousLaunchX = 4.25f;
            sectorHeights.Clear();
            sectorHeights.Add(0f);
            spawnPoint = new Vector3(-4.65f, 0.72f, 0f);
            checkpointSpawnPoint = spawnPoint;

            Color startAccent = PaletteColor(0);
            CreatePlatform("Origin Deck", new Vector2(0f, -0.5f), new Vector2(13f, 1f),
                geometryRoot, startAccent, false, 0);
            CreateHazard("Origin Failure Terminal", new Vector2(previousLaunchX, 0.78f), new Vector2(0.84f, 1.18f), geometryRoot);
            CreateSectorBackdrop(0f, 0, startAccent);

            for (int i = 0; i < 4; i++)
            {
                GenerateNextSector();
            }
        }

        private void EnsureTowerAhead()
        {
            if (towerRandom == null || player == null)
            {
                return;
            }
            while (generatedSectorCount < TotalSectors && topGeneratedY < player.transform.position.y + GenerationLookAhead)
            {
                GenerateNextSector();
            }
        }

        private void GenerateNextSector()
        {
            generatedSectorCount++;
            int sector = generatedSectorCount;
            float rise = Mathf.Lerp(1.95f, 2.18f, (float)towerRandom.NextDouble());
            topGeneratedY += rise;

            float horizontalShift = Mathf.Lerp(-1.28f, 1.28f, (float)towerRandom.NextDouble());
            if (Mathf.Abs(horizontalShift) < 0.34f)
            {
                horizontalShift = Mathf.Sign(horizontalShift == 0f ? (sector % 2 == 0 ? -1f : 1f) : horizontalShift) * 0.34f;
            }
            float platformX = Mathf.Clamp(previousLaunchX + horizontalShift, -2.85f, 2.85f);
            float width = Mathf.Lerp(5.65f, 6.55f, (float)towerRandom.NextDouble());
            bool checkpoint = sector % CheckpointInterval == 0 && sector < TotalSectors;
            Color accent = PaletteColor(sector);
            string label = checkpoint ? "CHECKPOINT " + (sector / CheckpointInterval).ToString("00") : "SECTOR " + sector.ToString("00");

            CreatePlatform(label, new Vector2(platformX, topGeneratedY), new Vector2(width, 0.62f),
                geometryRoot, accent, checkpoint, sector);
            sectorHeights.Add(topGeneratedY + 0.31f);
            CreateSectorBackdrop(topGeneratedY, sector, accent);

            if (sector < TotalSectors)
            {
                float approachDirection = Mathf.Sign(platformX - previousLaunchX);
                if (Mathf.Abs(approachDirection) < 0.5f)
                {
                    approachDirection = sector % 2 == 0 ? -1f : 1f;
                }
                float hazardOffset = approachDirection * (width * 0.5f - 0.72f);
                previousLaunchX = platformX + hazardOffset;
                CreateHazard("Failure Terminal " + sector.ToString("00"),
                    new Vector2(previousLaunchX, topGeneratedY + 0.82f), new Vector2(0.84f, 1.18f), geometryRoot);
            }
            else
            {
                exitPosition = new Vector3(platformX, topGeneratedY + 1.52f, 0f);
                CreateExit(exitPosition, geometryRoot);
            }

            previousPlatformX = platformX;
        }

        private void CreateSectorBackdrop(float y, int sector, Color accent)
        {
            float side = sector % 2 == 0 ? -1f : 1f;
            Sprite panel = sector % 3 == 0 ? wallPanelLeftSprite : (sector % 3 == 1 ? wallPanelMiddleSprite : wallPanelRightSprite);
            CreateVisual("Service Bay " + sector.ToString("00"), new Vector2(side * 5.0f, y + 0.78f),
                new Vector2(1.75f, 1.75f), WithAlpha(Hex("#52606A"), 0.48f), -84, sectorBackdropRoot, panel);
            CreateVisual("Bay Instrument " + sector.ToString("00"), new Vector2(side * 5.0f, y + 0.78f),
                new Vector2(0.82f, 0.82f), WithAlpha(Hex("#83909A"), 0.28f), -82, sectorBackdropRoot,
                sector % 2 == 0 ? monitorSprite : consoleSprite);

            if (sector % 3 == 1)
            {
                CreateVisual("Vent Turbine " + sector.ToString("00"), new Vector2(-side * 5.05f, y + 0.9f),
                    new Vector2(1.15f, 1.15f), WithAlpha(Hex("#4A5861"), 0.30f), -85, sectorBackdropRoot, gearSprite);
            }
            if (sector % 4 == 2)
            {
                CreateVisual("Inspection Light " + sector.ToString("00"), new Vector2(-side * 4.25f, y + 1.25f),
                    new Vector2(3.8f, 5.2f), WithAlpha(accent, 0.045f), -86, sectorBackdropRoot, lightConeSprite);
            }
        }

        private Color PaletteColor(int sector)
        {
            return sector > 0 && sector % CheckpointInterval == 0 ? exitColor : echoColor;
        }

        private GameObject CreatePlatform(string objectName, Vector2 position, Vector2 size, Transform parent,
            Color accent, bool checkpoint, int sector)
        {
            GameObject platform = new GameObject(objectName);
            platform.transform.SetParent(parent, false);
            platform.transform.position = position;
            platform.AddComponent<GroundSurface>();
            BoxCollider2D collider = platform.AddComponent<BoxCollider2D>();
            collider.size = size;
            collider.usedByEffector = true;
            PlatformEffector2D effector = platform.AddComponent<PlatformEffector2D>();
            effector.useOneWay = true;
            effector.useOneWayGrouping = true;
            effector.surfaceArc = 165f;

            CreateVisual("Platform Shadow", new Vector2(0f, -0.08f), new Vector2(size.x, size.y * 0.86f),
                Hex("#10171D"), -1, platform.transform);
            int tileCount = Mathf.Max(3, Mathf.CeilToInt(size.x));
            float tileWidth = size.x / tileCount;
            for (int i = 0; i < tileCount; i++)
            {
                Sprite tile = i == 0 ? platformLeftSprite : (i == tileCount - 1 ? platformRightSprite : platformMiddleSprite);
                float localX = -size.x * 0.5f + tileWidth * (i + 0.5f);
                CreateVisual("Deck Segment " + i, new Vector2(localX, 0f), new Vector2(tileWidth * 1.025f, size.y),
                    Hex("#9AA4AA"), 1, platform.transform, tile);
            }

            if (checkpoint)
            {
                CreateCheckpoint(platform, position, size, accent, sector);
            }
            return platform;
        }

        private void CreateCheckpoint(GameObject platform, Vector2 position, Vector2 size, Color accent, int sector)
        {
            GameObject triggerObject = new GameObject("Checkpoint Trigger " + sector.ToString("00"));
            triggerObject.transform.SetParent(platform.transform, false);
            triggerObject.transform.localPosition = new Vector3(0f, 0.72f, 0f);
            BoxCollider2D trigger = triggerObject.AddComponent<BoxCollider2D>();
            trigger.isTrigger = true;
            trigger.size = new Vector2(size.x * 0.72f, 1.05f);
            EchoesCheckpoint checkpoint = triggerObject.AddComponent<EchoesCheckpoint>();
            checkpoint.Configure(towerGenerationId, sector,
                new Vector3(position.x, position.y + size.y * 0.5f + 0.68f, 0f));

            Transform glow = CreateVisual("Memory Light", new Vector2(0f, 0.82f), new Vector2(2.15f, 2.15f),
                WithAlpha(exitColor, 0.10f), 3, platform.transform, circleSprite).transform;
            glow.gameObject.AddComponent<EchoesPulseVisual>().Configure(new Vector2(2.15f, 2.15f), 0.055f, 1.45f);
            CreateVisual("Memory Module", new Vector2(0f, 0.82f), new Vector2(0.82f, 0.82f),
                playerColor, 5, platform.transform, checkpointModuleSprite);
            CreateVisual("Memory Lamp", new Vector2(0f, 1.20f), new Vector2(0.54f, 0.54f),
                playerColor, 6, platform.transform, checkpointLampSprite);
        }

        private void CreateHazard(string objectName, Vector2 position, Vector2 size, Transform parent)
        {
            GameObject hazard = new GameObject(objectName);
            hazard.transform.SetParent(parent, false);
            hazard.transform.position = position;
            EchoesHazard hazardScript = hazard.AddComponent<EchoesHazard>();
            BoxCollider2D collider = hazard.AddComponent<BoxCollider2D>();
            collider.isTrigger = true;
            collider.size = size;
            Transform glow = CreateVisual("Terminal Heat", new Vector2(0f, 0.05f), new Vector2(1.35f, 1.35f),
                WithAlpha(hazardColor, 0.11f), 5, hazard.transform, circleSprite).transform;
            glow.gameObject.AddComponent<EchoesPulseVisual>().Configure(new Vector2(1.35f, 1.35f), 0.045f, 2.6f);
            CreateVisual("Failure Lamp", Vector2.zero, new Vector2(1.0f, 1.0f),
                playerColor, 8, hazard.transform, failureLampSprite);
            CreateVisual("Failure Warning", new Vector2(0f, -0.26f), new Vector2(0.46f, 0.46f),
                playerColor, 9, hazard.transform, warningPanelSprite);
            hazardScript.Configure();
        }

        private void CreateDeathZone(Transform parent)
        {
            GameObject zone = new GameObject("Abyss Death Zone");
            zone.transform.SetParent(parent, false);
            zone.transform.position = new Vector3(0f, -4.25f, 0f);
            BoxCollider2D collider = zone.AddComponent<BoxCollider2D>();
            collider.isTrigger = true;
            collider.size = new Vector2(30f, 2f);
            zone.AddComponent<EchoesHazard>();
        }

        private void CreateExit(Vector2 position, Transform parent)
        {
            GameObject exit = new GameObject("Signal Relay");
            exit.transform.SetParent(parent, false);
            exit.transform.position = position;
            BoxCollider2D trigger = exit.AddComponent<BoxCollider2D>();
            trigger.isTrigger = true;
            trigger.size = new Vector2(1.45f, 2.25f);
            EchoesExit exitScript = exit.AddComponent<EchoesExit>();

            CreateVisual("Relay Bloom", new Vector2(0f, 0.08f), new Vector2(4.4f, 4.4f),
                WithAlpha(exitColor, 0.16f), 4, exit.transform, circleSprite);
            Transform ring = CreateVisual("Relay Signal", new Vector2(0f, 0.12f), new Vector2(2.25f, 2.25f),
                WithAlpha(exitColor, 0.72f), 6, exit.transform, ringSprite).transform;
            CreateVisual("Left Relay Column", new Vector2(-0.68f, -0.05f), new Vector2(0.62f, 2.5f),
                Hex("#9AA4AA"), 7, exit.transform, columnSprite);
            CreateVisual("Right Relay Column", new Vector2(0.68f, -0.05f), new Vector2(0.62f, 2.5f),
                Hex("#9AA4AA"), 7, exit.transform, columnSprite);
            CreateVisual("Relay Console", new Vector2(0f, 0.05f), new Vector2(0.82f, 0.82f),
                playerColor, 8, exit.transform, monitorSprite);
            CreateVisual("Relay Lamp", new Vector2(0f, 0.72f), new Vector2(0.62f, 0.62f),
                playerColor, 9, exit.transform, finalLampSprite);
            exitScript.Configure(ring);
        }

        private void CreateWorldLabels(Transform world)
        {
            CreateWorldText("01  /  THE FIRST FALL", new Vector2(-5.7f, 1.65f), 0.13f, platformTopColor, world);
            CreateWorldText("02  /  THE CHOICE", new Vector2(2.35f, 4.15f), 0.13f, platformTopColor, world);
            CreateWorldText("03  /  THE ASCENT", new Vector2(-4.45f, 6.75f), 0.13f, platformTopColor, world);
            CreateWorldText("EVERY FALL LEAVES A WAY FORWARD", new Vector2(1.55f, 11f), 0.10f,
                WithAlpha(exitColor, 0.82f), world);
        }

        private void CreateWorldText(string value, Vector2 position, float characterSize, Color color, Transform parent)
        {
            GameObject textObject = new GameObject(value);
            textObject.transform.SetParent(parent, false);
            textObject.transform.position = position;
            TextMesh textMesh = textObject.AddComponent<TextMesh>();
            textMesh.text = value;
            textMesh.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            textMesh.fontSize = 48;
            textMesh.characterSize = characterSize;
            textMesh.anchor = TextAnchor.MiddleLeft;
            textMesh.alignment = TextAlignment.Left;
            textMesh.color = color;
            MeshRenderer renderer = textObject.GetComponent<MeshRenderer>();
            renderer.sortingOrder = -20;
        }

        private void CreatePlayer(Transform parent)
        {
            GameObject playerObject = new GameObject("Player Robot");
            playerObject.transform.SetParent(parent, false);
            playerObject.transform.position = spawnPoint;

            Rigidbody2D rigidbody = playerObject.AddComponent<Rigidbody2D>();
            rigidbody.gravityScale = 3f;
            rigidbody.freezeRotation = true;
            rigidbody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rigidbody.interpolation = RigidbodyInterpolation2D.Interpolate;
            rigidbody.sleepMode = RigidbodySleepMode2D.NeverSleep;

            CapsuleCollider2D collider = playerObject.AddComponent<CapsuleCollider2D>();
            collider.size = new Vector2(0.82f, 1.28f);
            collider.direction = CapsuleDirection2D.Vertical;
            collider.sharedMaterial = new PhysicsMaterial2D("Zero Friction")
            {
                friction = 0f,
                bounciness = 0f
            };

            GameObject glow = CreateVisual("Robot Glow", new Vector2(0f, -0.05f), new Vector2(1.85f, 1.85f),
                WithAlpha(echoColor, 0.12f), 18, playerObject.transform, circleSprite);
            glow.transform.localPosition = new Vector3(0f, -0.05f, 0f);

            GameObject visual = new GameObject("Robot Visual");
            visual.transform.SetParent(playerObject.transform, false);
            visual.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            visual.transform.localScale = Vector3.one * 0.82f;
            SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>();
            renderer.sprite = robotIdle;
            renderer.color = playerColor;
            renderer.sortingOrder = 20;

            TrailRenderer trail = playerObject.AddComponent<TrailRenderer>();
            trail.time = 0.24f;
            trail.minVertexDistance = 0.06f;
            trail.widthCurve = AnimationCurve.Linear(0f, 0.24f, 1f, 0f);
            Gradient trailGradient = new Gradient();
            trailGradient.SetKeys(
                new[] { new GradientColorKey(echoColor, 0f), new GradientColorKey(Hex("#A878FF"), 1f) },
                new[] { new GradientAlphaKey(0.55f, 0f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = trailGradient;
            Shader trailShader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            if (trailShader != null)
            {
                Material trailMaterial = new Material(trailShader) { color = Color.white };
                generatedMaterials.Add(trailMaterial);
                trail.material = trailMaterial;
            }
            trail.sortingOrder = 17;

            player = playerObject.AddComponent<EchoesPlayerController>();
            player.Configure(rigidbody, collider, renderer, robotIdle, robotDriveA, robotDriveB, robotJump, robotHurt,
                new Vector2(spawnPoint.x, 0.12f));
            Camera.main.GetComponent<EchoesCameraFollow>().Target = playerObject.transform;
        }

        private void CreateUI()
        {
            GameObject canvasObject = new GameObject("HUD");
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();

            Font font = uiFont ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            GameObject gameplayHudObject = new GameObject("Gameplay HUD");
            gameplayHudObject.transform.SetParent(canvasObject.transform, false);
            RectTransform gameplayHudRect = gameplayHudObject.AddComponent<RectTransform>();
            gameplayHudRect.anchorMin = Vector2.zero;
            gameplayHudRect.anchorMax = Vector2.one;
            gameplayHudRect.offsetMin = Vector2.zero;
            gameplayHudRect.offsetMax = Vector2.zero;
            gameplayHud = gameplayHudObject.AddComponent<CanvasGroup>();

            CreateUIPanel("Identity Accent", gameplayHudObject.transform, echoColor,
                new Vector2(38f, -48f), new Vector2(4f, 68f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            Text title = CreateUIText("Game Identity", gameplayHudObject.transform, font, 25, FontStyle.Bold, playerColor,
                new Vector2(58f, -39f), new Vector2(520f, 40f), new Vector2(0f, 1f), new Vector2(0f, 1f), TextAnchor.MiddleLeft);
            title.text = "Echoes of Failure";
            Text subtitle = CreateUIText("Game Objective", gameplayHudObject.transform, font, 14, FontStyle.Normal,
                WithAlpha(platformTopColor, 0.96f), new Vector2(59f, -72f), new Vector2(620f, 28f),
                new Vector2(0f, 1f), new Vector2(0f, 1f), TextAnchor.MiddleLeft);
            subtitle.text = "Climb. Fall. Leave a way forward.";

            GameObject statusPanel = CreateUIPanel("Run Status", gameplayHudObject.transform, WithAlpha(Hex("#071019"), 0.88f),
                new Vector2(-42f, -42f), new Vector2(380f, 154f), new Vector2(1f, 1f), new Vector2(1f, 1f));
            CreateUIPanel("Status Accent", statusPanel.transform, exitColor, new Vector2(-184f, 0f),
                new Vector2(4f, 126f), Vector2.one * 0.5f, Vector2.one * 0.5f);
            deathCountText = CreateUIText("Echo Counter", statusPanel.transform, font, 21, FontStyle.Bold, echoColor,
                new Vector2(-20f, 42f), new Vector2(310f, 36f), Vector2.one * 0.5f, Vector2.one * 0.5f, TextAnchor.MiddleRight);
            sectorText = CreateUIText("Sector Counter", statusPanel.transform, font, 18, FontStyle.Bold, exitColor,
                new Vector2(-20f, 9f), new Vector2(310f, 32f), Vector2.one * 0.5f, Vector2.one * 0.5f, TextAnchor.MiddleRight);
            seedText = CreateUIText("Seed", statusPanel.transform, font, 12, FontStyle.Normal, WithAlpha(platformTopColor, 0.84f),
                new Vector2(-20f, -23f), new Vector2(310f, 26f), Vector2.one * 0.5f, Vector2.one * 0.5f, TextAnchor.MiddleRight);
            altitudeText = CreateUIText("Altitude", statusPanel.transform, font, 12, FontStyle.Bold, echoColor,
                new Vector2(-20f, -50f), new Vector2(310f, 26f), Vector2.one * 0.5f, Vector2.one * 0.5f, TextAnchor.MiddleRight);
            GameObject progressTrack = CreateUIPanel("Progress Track", statusPanel.transform, WithAlpha(platformTopColor, 0.22f),
                new Vector2(12f, -68f), new Vector2(320f, 5f), Vector2.one * 0.5f, Vector2.one * 0.5f);
            GameObject progressObject = CreateUIPanel("Progress Fill", progressTrack.transform, echoColor,
                new Vector2(-160f, 0f), new Vector2(0f, 5f), Vector2.one * 0.5f, Vector2.one * 0.5f);
            progressFill = progressObject.GetComponent<RectTransform>();
            progressFill.pivot = new Vector2(0f, 0.5f);

            GameObject promptPanel = CreateUIPanel("Objective Card", gameplayHudObject.transform, WithAlpha(Hex("#071019"), 0.90f),
                new Vector2(42f, 42f), new Vector2(870f, 92f), Vector2.zero, Vector2.zero);
            CreateUIPanel("Objective Accent", promptPanel.transform, echoColor, new Vector2(-427f, 0f),
                new Vector2(5f, 66f), Vector2.one * 0.5f, Vector2.one * 0.5f);
            promptText = CreateUIText("Objective", promptPanel.transform, font, 17, FontStyle.Normal, playerColor,
                new Vector2(22f, 0f), new Vector2(800f, 62f), Vector2.one * 0.5f, Vector2.one * 0.5f, TextAnchor.MiddleLeft);
            promptText.horizontalOverflow = HorizontalWrapMode.Wrap;
            promptText.verticalOverflow = VerticalWrapMode.Overflow;

            GameObject banner = CreateUIPanel("First Death Message", gameplayHudObject.transform, WithAlpha(Hex("#071019"), 0.96f),
                new Vector2(0f, 175f), new Vector2(700f, 82f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            firstDeathBanner = banner.AddComponent<CanvasGroup>();
            firstDeathBanner.alpha = 0f;
            firstDeathBanner.blocksRaycasts = false;
            firstDeathBanner.interactable = false;
            CreateUIPanel("Death Message Accent", banner.transform, echoColor, new Vector2(-346f, 0f),
                new Vector2(5f, 62f), Vector2.one * 0.5f, Vector2.one * 0.5f);
            firstDeathBannerText = CreateUIText("Banner Text", banner.transform, font, 21, FontStyle.Bold, playerColor,
                new Vector2(14f, 0f), new Vector2(640f, 64f), Vector2.one * 0.5f, Vector2.one * 0.5f, TextAnchor.MiddleCenter);
            firstDeathBannerText.text = "An echo remains where you last stood.";

            GameObject checkpointPanel = CreateUIPanel("Checkpoint Message", gameplayHudObject.transform, WithAlpha(Hex("#071019"), 0.96f),
                new Vector2(0f, 82f), new Vector2(620f, 66f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            checkpointBanner = checkpointPanel.AddComponent<CanvasGroup>();
            checkpointBanner.alpha = 0f;
            checkpointBanner.blocksRaycasts = false;
            checkpointBannerText = CreateUIText("Checkpoint Text", checkpointPanel.transform, font, 19, FontStyle.Bold,
                exitColor, Vector2.zero, new Vector2(580f, 54f), Vector2.one * 0.5f, Vector2.one * 0.5f,
                TextAnchor.MiddleCenter);

            GameObject flashObject = CreateUIPanel("Impact Flash", canvasObject.transform, Color.clear,
                Vector2.zero, Vector2.zero, Vector2.zero, Vector2.one);
            RectTransform flashRect = flashObject.GetComponent<RectTransform>();
            flashRect.offsetMin = Vector2.zero;
            flashRect.offsetMax = Vector2.zero;
            impactFlash = flashObject.GetComponent<Image>();

            GameObject victory = CreateUIPanel("Victory Panel", canvasObject.transform, WithAlpha(Hex("#050A0F"), 0.96f),
                Vector2.zero, Vector2.zero, Vector2.zero, Vector2.one);
            RectTransform victoryRect = victory.GetComponent<RectTransform>();
            victoryRect.offsetMin = Vector2.zero;
            victoryRect.offsetMax = Vector2.zero;
            victoryPanel = victory.AddComponent<CanvasGroup>();
            victoryPanel.alpha = 0f;
            victoryPanel.blocksRaycasts = false;
            victoryPanel.interactable = false;

            CreateVisualUIRing(victory.transform, new Vector2(0f, 85f), new Vector2(420f, 420f), WithAlpha(exitColor, 0.16f));
            victoryTitleText = CreateUIText("Victory Title", victory.transform, font, 62, FontStyle.Bold, exitColor,
                new Vector2(0f, 115f), new Vector2(1500f, 170f), Vector2.one * 0.5f, Vector2.one * 0.5f, TextAnchor.MiddleCenter);
            victoryTitleText.text = "You built the way out.";
            victoryDetailText = CreateUIText("Victory Detail", victory.transform, font, 27, FontStyle.Normal, playerColor,
                new Vector2(0f, -15f), new Vector2(1500f, 150f), Vector2.one * 0.5f, Vector2.one * 0.5f, TextAnchor.MiddleCenter);
            Text restartText = CreateUIText("Restart Hint", victory.transform, font, 17, FontStyle.Normal, platformTopColor,
                new Vector2(0f, -175f), new Vector2(1000f, 65f), Vector2.one * 0.5f, Vector2.one * 0.5f, TextAnchor.MiddleCenter);
            restartText.text = "Press R to begin a new recovery.";

            GameObject titleRoot = CreateUIPanel("Home Screen", canvasObject.transform, backgroundColor,
                Vector2.zero, Vector2.zero, Vector2.zero, Vector2.one);
            titlePanel = titleRoot.AddComponent<CanvasGroup>();
            GameObject titleBackground = CreateUIPanel("Home Artwork", titleRoot.transform, Color.white,
                Vector2.zero, Vector2.zero, Vector2.zero, Vector2.one);
            Image titleArtworkImage = titleBackground.GetComponent<Image>();
            titleArtworkImage.sprite = titleArtSprite;
            titleArtworkImage.preserveAspect = false;
            GameObject titleFade = CreateUIPanel("Home Contrast", titleRoot.transform, Hex("#050A0F"),
                Vector2.zero, Vector2.zero, Vector2.zero, Vector2.one);
            Image titleFadeImage = titleFade.GetComponent<Image>();
            titleFadeImage.sprite = leftFadeSprite;
            titleFadeImage.type = Image.Type.Sliced;

            Text homeEyebrow = CreateUIText("Home Eyebrow", titleRoot.transform, font, 15, FontStyle.Bold, echoColor,
                new Vector2(112f, 224f), new Vector2(620f, 34f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), TextAnchor.MiddleLeft);
            homeEyebrow.text = "A recovery story";
            Text homeTitle = CreateUIText("Home Title", titleRoot.transform, font, 72, FontStyle.Bold, playerColor,
                new Vector2(108f, 134f), new Vector2(900f, 110f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), TextAnchor.MiddleLeft);
            homeTitle.text = "Echoes of Failure";
            Text homeTagline = CreateUIText("Home Tagline", titleRoot.transform, font, 24, FontStyle.Normal, WithAlpha(playerColor, 0.86f),
                new Vector2(112f, 55f), new Vector2(740f, 72f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), TextAnchor.MiddleLeft);
            homeTagline.text = "Every fall leaves a way forward.";
            GameObject beginCard = CreateUIPanel("Begin Card", titleRoot.transform, WithAlpha(Hex("#071019"), 0.88f),
                new Vector2(112f, -102f), new Vector2(420f, 78f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
            CreateUIPanel("Begin Accent", beginCard.transform, exitColor, new Vector2(-207f, 0f),
                new Vector2(6f, 60f), Vector2.one * 0.5f, Vector2.one * 0.5f);
            Text beginText = CreateUIText("Begin Text", beginCard.transform, font, 19, FontStyle.Bold, playerColor,
                new Vector2(18f, 10f), new Vector2(360f, 30f), Vector2.one * 0.5f, Vector2.one * 0.5f, TextAnchor.MiddleLeft);
            beginText.text = "Begin recovery";
            Text beginHint = CreateUIText("Begin Hint", beginCard.transform, font, 12, FontStyle.Normal, platformTopColor,
                new Vector2(18f, -17f), new Vector2(360f, 24f), Vector2.one * 0.5f, Vector2.one * 0.5f, TextAnchor.MiddleLeft);
            beginHint.text = "ENTER  or  SPACE";
            Text homeControls = CreateUIText("Home Controls", titleRoot.transform, font, 12, FontStyle.Normal,
                WithAlpha(platformTopColor, 0.76f), new Vector2(112f, -188f), new Vector2(680f, 28f),
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), TextAnchor.MiddleLeft);
            homeControls.text = "A / D move   ·   SPACE jump   ·   ESC pause";

            GameObject storyRoot = CreateUIPanel("Story Screen", canvasObject.transform, backgroundColor,
                Vector2.zero, Vector2.zero, Vector2.zero, Vector2.one);
            storyPanel = storyRoot.AddComponent<CanvasGroup>();
            GameObject storyBackground = CreateUIPanel("Story Artwork", storyRoot.transform, WithAlpha(Color.white, 0.38f),
                Vector2.zero, Vector2.zero, Vector2.zero, Vector2.one);
            Image storyArtworkImage = storyBackground.GetComponent<Image>();
            storyArtworkImage.sprite = titleArtSprite;
            storyArtworkImage.preserveAspect = false;
            GameObject storyFade = CreateUIPanel("Story Contrast", storyRoot.transform, Hex("#050A0F"),
                Vector2.zero, Vector2.zero, Vector2.zero, Vector2.one);
            storyFade.GetComponent<Image>().sprite = leftFadeSprite;
            CreateUIPanel("Story Accent", storyRoot.transform, echoColor, new Vector2(108f, 12f),
                new Vector2(5f, 430f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
            Text storyLabel = CreateUIText("Story Label", storyRoot.transform, font, 14, FontStyle.Bold, echoColor,
                new Vector2(140f, 214f), new Vector2(720f, 32f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), TextAnchor.MiddleLeft);
            storyLabel.text = "RECOVERY RECORD  17";
            storyText = CreateUIText("Story Text", storyRoot.transform, font, 30, FontStyle.Normal, playerColor,
                new Vector2(140f, 28f), new Vector2(840f, 340f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), TextAnchor.UpperLeft);
            storyText.horizontalOverflow = HorizontalWrapMode.Wrap;
            storyText.verticalOverflow = VerticalWrapMode.Overflow;
            storyText.lineSpacing = 1.3f;
            storyContinueText = CreateUIText("Story Continue", storyRoot.transform, font, 14, FontStyle.Bold, exitColor,
                new Vector2(140f, -208f), new Vector2(700f, 34f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), TextAnchor.MiddleLeft);
            storyContinueText.text = string.Empty;

            GameObject pauseRoot = CreateUIPanel("Pause Screen", canvasObject.transform, WithAlpha(Hex("#03070B"), 0.94f),
                Vector2.zero, Vector2.zero, Vector2.zero, Vector2.one);
            pausePanel = pauseRoot.AddComponent<CanvasGroup>();
            CreateUIPanel("Pause Accent", pauseRoot.transform, exitColor, new Vector2(0f, 116f),
                new Vector2(96f, 5f), Vector2.one * 0.5f, Vector2.one * 0.5f);
            Text pauseTitle = CreateUIText("Pause Title", pauseRoot.transform, font, 54, FontStyle.Bold, playerColor,
                new Vector2(0f, 54f), new Vector2(700f, 80f), Vector2.one * 0.5f, Vector2.one * 0.5f, TextAnchor.MiddleCenter);
            pauseTitle.text = "Paused";
            Text pauseBody = CreateUIText("Pause Body", pauseRoot.transform, font, 18, FontStyle.Normal, WithAlpha(playerColor, 0.82f),
                new Vector2(0f, -36f), new Vector2(760f, 120f), Vector2.one * 0.5f, Vector2.one * 0.5f, TextAnchor.MiddleCenter);
            pauseBody.text = "ESC or ENTER   Resume\nR   Restart this run\nT   Return to title";
            pauseBody.lineSpacing = 1.45f;
            Text pauseNote = CreateUIText("Pause Note", pauseRoot.transform, font, 13, FontStyle.Normal, platformTopColor,
                new Vector2(0f, -152f), new Vector2(760f, 42f), Vector2.one * 0.5f, Vector2.one * 0.5f, TextAnchor.MiddleCenter);
            pauseNote.text = "Your echoes will wait for you.";

            CreateScreenTreatment(canvasObject.transform);
        }

        private void CreateScreenTreatment(Transform canvas)
        {
            GameObject scanlines = CreateUIPanel("Pixel Scanlines", canvas, WithAlpha(Hex("#10212A"), 0.055f),
                Vector2.zero, Vector2.zero, Vector2.zero, Vector2.one);
            Image scanlineImage = scanlines.GetComponent<Image>();
            scanlineImage.sprite = scanlineSprite;
            scanlineImage.type = Image.Type.Tiled;

            CreateUIPanel("Top Frame", canvas, WithAlpha(backgroundColor, 0.72f), new Vector2(0f, -12f),
                new Vector2(0f, 24f), new Vector2(0f, 1f), new Vector2(1f, 1f));
            CreateUIPanel("Bottom Frame", canvas, WithAlpha(backgroundColor, 0.80f), new Vector2(0f, 14f),
                new Vector2(0f, 28f), new Vector2(0f, 0f), new Vector2(1f, 0f));
            CreateUIPanel("Left Frame", canvas, WithAlpha(backgroundColor, 0.72f), new Vector2(8f, 0f),
                new Vector2(16f, 0f), new Vector2(0f, 0f), new Vector2(0f, 1f));
            CreateUIPanel("Right Frame", canvas, WithAlpha(backgroundColor, 0.72f), new Vector2(-8f, 0f),
                new Vector2(16f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f));
        }

        private void CreateVisualUIRing(Transform parent, Vector2 position, Vector2 size, Color color)
        {
            GameObject ring = new GameObject("Victory Glow");
            ring.transform.SetParent(parent, false);
            RectTransform rect = ring.AddComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = Vector2.one * 0.5f;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            Image image = ring.AddComponent<Image>();
            image.sprite = ringSprite;
            image.color = color;
            image.raycastTarget = false;
        }

        private GameObject CreateUIPanel(string objectName, Transform parent, Color color, Vector2 anchoredPosition,
            Vector2 size, Vector2 anchorMin, Vector2 anchorMax)
        {
            GameObject panel = new GameObject(objectName);
            panel.transform.SetParent(parent, false);
            RectTransform rect = panel.AddComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = (anchorMin + anchorMax) * 0.5f;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            Image image = panel.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return panel;
        }

        private Text CreateUIText(string objectName, Transform parent, Font font, int fontSize, FontStyle style,
            Color color, Vector2 anchoredPosition, Vector2 size, Vector2 anchorMin, Vector2 anchorMax, TextAnchor alignment)
        {
            GameObject textObject = new GameObject(objectName);
            textObject.transform.SetParent(parent, false);
            RectTransform rect = textObject.AddComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = (anchorMin + anchorMax) * 0.5f;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            Text text = textObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.alignment = alignment;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        private void CreateAudio()
        {
            sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
            sfxSource.spatialBlend = 0f;
            sfxSource.volume = 0.78f;

            typeTickClip = CreateToneClip("Terminal Key", 0.032f, 1120f, 0.26f);
            menuConfirmClip = CreateToneClip("Menu Confirm", 0.11f, 620f, 0.72f);
            pauseClip = CreateToneClip("Pause Relay", 0.075f, 340f, 0.55f);
            landClip = CreateToneClip("Robot Land", 0.045f, 185f, 0.38f);

            ambienceSource = gameObject.AddComponent<AudioSource>();
            ambienceSource.clip = ambienceClip;
            ambienceSource.loop = true;
            ambienceSource.playOnAwake = false;
            ambienceSource.spatialBlend = 0f;
            ambienceSource.volume = 0.075f;
            if (ambienceClip != null)
            {
                ambienceSource.Play();
            }
        }

        private static AudioClip CreateToneClip(string clipName, float duration, float frequency, float brightness)
        {
            const int sampleRate = 44100;
            int sampleCount = Mathf.Max(1, Mathf.CeilToInt(sampleRate * duration));
            AudioClip clip = AudioClip.Create(clipName, sampleCount, 1, sampleRate, false);
            float[] samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                float time = i / (float)sampleRate;
                float progress = i / (float)sampleCount;
                float envelope = Mathf.Pow(1f - progress, 3f) * Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, progress * 18f));
                float fundamental = Mathf.Sin(time * frequency * Mathf.PI * 2f);
                float harmonic = Mathf.Sin(time * frequency * 2f * Mathf.PI * 2f) * brightness;
                samples[i] = (fundamental + harmonic) * envelope * 0.26f;
            }
            clip.SetData(samples, 0);
            return clip;
        }

        private void ShowHomeScreen()
        {
            StopAllCoroutines();
            presentationState = PresentationState.Home;
            storyTyping = false;
            storyComplete = false;
            skipStoryTyping = false;
            Time.timeScale = 0f;
            SetCanvasGroup(titlePanel, true);
            SetCanvasGroup(storyPanel, false);
            SetCanvasGroup(pausePanel, false);
            SetCanvasGroup(gameplayHud, false);
            SetCanvasGroup(victoryPanel, false);
            if (player != null)
            {
                player.SetControlEnabled(false);
                player.StopMotion();
            }
            if (ambienceSource != null)
            {
                ambienceSource.volume = 0.04f;
            }
        }

        private void BeginStory()
        {
            presentationState = PresentationState.Story;
            storyTyping = true;
            storyComplete = false;
            skipStoryTyping = false;
            storyText.text = string.Empty;
            storyContinueText.text = string.Empty;
            SetCanvasGroup(titlePanel, false);
            SetCanvasGroup(storyPanel, true);
            PlayClip(menuConfirmClip, 0.30f, 1f);
            StartCoroutine(TypeOpeningStory());
        }

        private IEnumerator TypeOpeningStory()
        {
            const string story = "Unit E-17 wakes beneath the ruins of Helix Station.\n\n" +
                                 "The lift is dead. The only way out is above.\n\n" +
                                 "Every fall leaves an echo — a foothold for the next attempt.\n\n" +
                                 "Help it remember the way home.";
            for (int i = 0; i < story.Length; i++)
            {
                if (skipStoryTyping)
                {
                    storyText.text = story;
                    break;
                }
                storyText.text += story[i];
                if (!char.IsWhiteSpace(story[i]) && i % 2 == 0)
                {
                    PlayClip(typeTickClip, 0.055f, 0.92f + (i % 5) * 0.025f);
                }
                yield return new WaitForSecondsRealtime(story[i] == '.' ? 0.18f : 0.018f);
            }
            storyTyping = false;
            storyComplete = true;
            storyContinueText.text = "ENTER or SPACE to begin   ·   ESC to return";
            PlayClip(menuConfirmClip, 0.20f, 1.18f);
        }

        private void BeginRun()
        {
            StopAllCoroutines();
            presentationState = PresentationState.Playing;
            Time.timeScale = 1f;
            SetCanvasGroup(titlePanel, false);
            SetCanvasGroup(storyPanel, false);
            SetCanvasGroup(pausePanel, false);
            SetCanvasGroup(gameplayHud, true);
            if (ambienceSource != null)
            {
                ambienceSource.volume = 0.085f;
            }
            PlayClip(menuConfirmClip, 0.30f, 1.12f);
            RestartRun();
        }

        private void PauseRun()
        {
            presentationState = PresentationState.Paused;
            Time.timeScale = 0f;
            SetCanvasGroup(pausePanel, true);
            if (player != null)
            {
                player.SetControlEnabled(false);
            }
            if (ambienceSource != null)
            {
                ambienceSource.volume = 0.025f;
            }
            PlayClip(pauseClip, 0.22f, 0.92f);
        }

        private void ResumeRun()
        {
            presentationState = PresentationState.Playing;
            Time.timeScale = 1f;
            SetCanvasGroup(pausePanel, false);
            if (player != null)
            {
                player.SetControlEnabled(true);
            }
            if (ambienceSource != null)
            {
                ambienceSource.volume = 0.085f;
            }
            PlayClip(pauseClip, 0.18f, 1.18f);
        }

        private void ReturnToTitle()
        {
            Time.timeScale = 1f;
            presentationState = PresentationState.Playing;
            RestartRun();
            ShowHomeScreen();
        }

        private static void SetCanvasGroup(CanvasGroup group, bool visible)
        {
            if (group == null)
            {
                return;
            }
            group.alpha = visible ? 1f : 0f;
            group.interactable = visible;
            group.blocksRaycasts = visible;
        }

        public void PlayJumpSound()
        {
            PlayClip(jumpClip, 0.22f, 1.35f);
        }

        public void PlayLandSound()
        {
            PlayClip(landClip, 0.11f, 0.88f);
        }

        public void BouncePlayer(EchoesPlayerController target, float strength, EchoSpring spring)
        {
            if (target == null || IsPlayerProtected)
            {
                return;
            }
            target.Bounce(strength);
            spring.Pulse();
            PlayClip(bounceClip, 0.38f, 1.25f);
            SpawnBurst(target.transform.position + Vector3.down * 0.4f, echoColor, 16, 3.7f, 0.55f);
            Camera.main.GetComponent<EchoesCameraFollow>().AddTrauma(0.18f);
            FlashImpact(echoColor, 0.10f, 0.16f);
        }

        public void KillPlayer(EchoesPlayerController victim)
        {
            if (victim == null || victim != player || IsPlayerProtected)
            {
                return;
            }
            StartCoroutine(DeathAndRespawn());
        }

        private IEnumerator DeathAndRespawn()
        {
            respawning = true;
            deathCount++;
            Vector2 anchor = player.HasSafeAnchor ? player.SafeAnchor : new Vector2(spawnPoint.x, 0.12f);
            player.BeginDeath();
            PlayClip(deathClip, 0.55f, 1f);
            SpawnBurst(player.transform.position, hazardColor, 20, 4.2f, 0.6f);
            Camera.main.GetComponent<EchoesCameraFollow>().AddTrauma(0.62f);
            FlashImpact(hazardColor, 0.24f, 0.32f);

            EchoSpring spring = SpawnOrRefreshEcho(anchor);
            PlayClip(echoClip, 0.48f, 1.18f);
            SpawnBurst(spring.transform.position + Vector3.up * 0.15f, echoColor, 20, 3.2f, 0.7f);
            RefreshUI();

            if (deathCount == 1)
            {
                StartCoroutine(ShowFirstDeathBanner());
            }

            yield return new WaitForSeconds(0.42f);
            Vector3 safeRespawn = ResolveSafeRespawn(checkpointSpawnPoint);
            RemoveEchoesNear(safeRespawn, RespawnEchoClearRadius);
            respawnProtectionTimer = RespawnProtectionDuration;
            player.Respawn(safeRespawn);
            respawning = false;
        }

        private Vector3 ResolveSafeRespawn(Vector3 desiredPosition)
        {
            float[] horizontalOffsets = { 0f, -0.75f, 0.75f, -1.5f, 1.5f };
            for (int i = 0; i < horizontalOffsets.Length; i++)
            {
                Vector3 candidate = desiredPosition + Vector3.right * horizontalOffsets[i];
                Collider2D[] overlaps = Physics2D.OverlapBoxAll(candidate, new Vector2(0.9f, 1.36f), 0f);
                bool blocked = false;
                for (int j = 0; j < overlaps.Length; j++)
                {
                    if (overlaps[j] != null && overlaps[j].GetComponent<EchoesHazard>() != null)
                    {
                        blocked = true;
                        break;
                    }
                }
                if (!blocked)
                {
                    return candidate;
                }
            }
            return desiredPosition + Vector3.up * 1.5f;
        }

        private void RemoveEchoesNear(Vector3 position, float radius)
        {
            for (int i = echoes.Count - 1; i >= 0; i--)
            {
                EchoSpring echo = echoes[i];
                if (echo == null)
                {
                    echoes.RemoveAt(i);
                    continue;
                }
                if (Vector2.Distance(echo.transform.position, position) <= radius)
                {
                    echo.gameObject.SetActive(false);
                    Destroy(echo.gameObject);
                    echoes.RemoveAt(i);
                }
            }
        }

        private EchoSpring SpawnOrRefreshEcho(Vector2 anchor)
        {
            for (int i = echoes.Count - 1; i >= 0; i--)
            {
                if (echoes[i] == null)
                {
                    echoes.RemoveAt(i);
                    continue;
                }
                if (Vector2.Distance(echoes[i].transform.position, anchor) < 1.15f)
                {
                    echoes[i].Refresh();
                    return echoes[i];
                }
            }

            EchoSpring echo = EchoSpring.Create(anchor, echoContainer, checkpointLampSprite, ringSprite, circleSprite, echoColor);
            echoes.Add(echo);
            return echo;
        }

        public void Win()
        {
            if (won || respawning)
            {
                return;
            }
            won = true;
            player.SetControlEnabled(false);
            player.StopMotion();
            PlayClip(victoryClip, 0.72f, 0.95f);
            SpawnBurst(exitPosition, exitColor, 58, 7f, 1.5f);
            Camera.main.GetComponent<EchoesCameraFollow>().AddTrauma(0.48f);
            FlashImpact(exitColor, 0.34f, 0.55f);
            victoryDetailText.text = "E-17 remembered how to rise.\n\n" +
                                     (deathCount == 1 ? "One fall became the path home." :
                                         deathCount + " falls became the path home.");
            StartCoroutine(FadeCanvas(victoryPanel, 0f, 1f, 0.7f));
        }

        private void RestartRun()
        {
            StopAllCoroutines();
            respawning = false;
            respawnProtectionTimer = RespawnProtectionDuration;
            won = false;
            deathCount = 0;
            for (int i = echoes.Count - 1; i >= 0; i--)
            {
                if (echoes[i] != null)
                {
                    echoes[i].gameObject.SetActive(false);
                    Destroy(echoes[i].gameObject);
                }
            }
            echoes.Clear();
            ClearChildren(geometryRoot);
            ClearChildren(sectorBackdropRoot);
            BuildProceduralTower(true);
            victoryPanel.alpha = 0f;
            firstDeathBanner.alpha = 0f;
            checkpointBanner.alpha = 0f;
            impactFlash.color = Color.clear;
            player.Respawn(spawnPoint);
            Camera.main.GetComponent<EchoesCameraFollow>().SnapTo(player.transform.position.y + 2f);
            RefreshUI();
        }

        private static void ClearChildren(Transform parent)
        {
            if (parent == null)
            {
                return;
            }
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                GameObject child = parent.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }
        }

        public void ActivateCheckpoint(int generationId, int sector, Vector3 respawnPosition)
        {
            if (generationId != towerGenerationId || sector <= activeCheckpointSector || won)
            {
                return;
            }
            activeCheckpointSector = sector;
            checkpointSpawnPoint = respawnPosition;
            RemoveEchoesNear(checkpointSpawnPoint, RespawnEchoClearRadius);
            player.SetSafeAnchor(new Vector2(respawnPosition.x, respawnPosition.y - 0.63f));
            checkpointBannerText.text = "Checkpoint " + (sector / CheckpointInterval).ToString("00") + " secured. Memory stable.";
            PlayClip(echoClip, 0.34f, 0.82f);
            SpawnBurst(respawnPosition, PaletteColor(sector), 26, 3.7f, 0.9f);
            Camera.main.GetComponent<EchoesCameraFollow>().AddTrauma(0.28f);
            StartCoroutine(ShowCheckpointBanner());
            RefreshUI();
        }

        private IEnumerator ShowFirstDeathBanner()
        {
            yield return FadeCanvas(firstDeathBanner, 0f, 1f, 0.24f);
            yield return new WaitForSeconds(1.25f);
            yield return FadeCanvas(firstDeathBanner, 1f, 0f, 0.45f);
        }

        private IEnumerator ShowCheckpointBanner()
        {
            yield return FadeCanvas(checkpointBanner, 0f, 1f, 0.18f);
            yield return new WaitForSeconds(1.05f);
            yield return FadeCanvas(checkpointBanner, 1f, 0f, 0.38f);
        }

        private IEnumerator FadeCanvas(CanvasGroup canvasGroup, float from, float to, float duration)
        {
            canvasGroup.alpha = from;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                canvasGroup.alpha = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, elapsed / duration));
                yield return null;
            }
            canvasGroup.alpha = to;
        }

        private void RefreshUI()
        {
            if (deathCountText != null)
            {
                deathCountText.text = "Echoes  " + deathCount.ToString("00");
            }
            if (sectorText != null)
            {
                sectorText.text = "Sector  " + highestSectorReached.ToString("00") + " / " + TotalSectors.ToString("00");
                seedText.text = "Tower seed  " + towerSeed;
            }
            if (progressFill != null)
            {
                progressFill.sizeDelta = new Vector2(320f * highestSectorReached / TotalSectors, 5f);
            }
            RefreshPrompt();
        }

        private void UpdateSectorProgress()
        {
            int reached = 0;
            for (int i = 1; i < sectorHeights.Count; i++)
            {
                if (player.transform.position.y >= sectorHeights[i] - 1.05f)
                {
                    reached = i;
                }
            }
            if (reached > highestSectorReached)
            {
                highestSectorReached = reached;
                RefreshUI();
            }
            if (altitudeText != null)
            {
                altitudeText.text = Mathf.Max(0f, player.transform.position.y).ToString("000.0") + " m" +
                                    (activeCheckpointSector > 0 ? "   ·   memory " + activeCheckpointSector.ToString("00") : string.Empty);
            }
        }

        private void RefreshPrompt()
        {
            if (promptText == null || player == null)
            {
                return;
            }
            if (won)
            {
                promptText.text = "The way out was built from every fall.";
            }
            else if (respawning)
            {
                promptText.text = "Forming an echo… restoring E-17 from memory.";
            }
            else if (deathCount == 0)
            {
                promptText.text = "Move with A / D. Jump with Space. Touch the coral terminal to record your first echo.";
            }
            else if (highestSectorReached == 0)
            {
                promptText.text = "Step onto the cyan echo. It remembers where you stood.";
            }
            else if (highestSectorReached < TotalSectors)
            {
                promptText.text = "Keep climbing. Place each echo where the next attempt can reach it.";
            }
            else
            {
                promptText.text = "The exit signal is close. One last climb.";
            }
        }

        private void FlashImpact(Color color, float peakAlpha, float duration)
        {
            if (impactFlash != null)
            {
                StartCoroutine(FlashImpactRoutine(color, peakAlpha, duration));
            }
        }

        private IEnumerator FlashImpactRoutine(Color color, float peakAlpha, float duration)
        {
            impactFlash.color = WithAlpha(color, peakAlpha);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                impactFlash.color = WithAlpha(color, Mathf.Lerp(peakAlpha, 0f, elapsed / duration));
                yield return null;
            }
            impactFlash.color = Color.clear;
        }

        private void PlayClip(AudioClip clip, float volume, float pitch)
        {
            if (clip == null || sfxSource == null)
            {
                return;
            }
            sfxSource.pitch = pitch;
            sfxSource.PlayOneShot(clip, volume);
        }

        public void SpawnBurst(Vector3 position, Color color, int count, float speed, float lifetime)
        {
            GameObject burst = new GameObject("Energy Burst");
            burst.transform.position = position;
            ParticleSystem particles = burst.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = particles.main;
            main.loop = false;
            main.duration = 0.12f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.7f, lifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.55f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.11f);
            main.startColor = color;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.Destroy;
            main.maxParticles = Mathf.Max(48, count);
            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.35f;
            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = particles.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(color, 0f), new GradientColorKey(Color.white, 0.4f), new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.85f, 0.45f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = gradient;
            ParticleSystemRenderer renderer = burst.GetComponent<ParticleSystemRenderer>();
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Particles/Standard Unlit");
            if (shader != null)
            {
                Material material = new Material(shader) { color = Color.white };
                generatedMaterials.Add(material);
                renderer.material = material;
            }
            renderer.sortingOrder = 50;
            particles.Play();
        }

        private GameObject CreateVisual(string objectName, Vector2 localPosition, Vector2 localScale, Color color,
            int sortingOrder, Transform parent, Sprite sprite = null)
        {
            GameObject visual = new GameObject(objectName);
            visual.transform.SetParent(parent, false);
            visual.transform.localPosition = new Vector3(localPosition.x, localPosition.y, 0f);
            visual.transform.localScale = new Vector3(localScale.x, localScale.y, 1f);
            SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite != null ? sprite : squareSprite;
            renderer.color = color;
            renderer.sortingOrder = sortingOrder;
            return visual;
        }

        private static Color Hex(string hex)
        {
            return ColorUtility.TryParseHtmlString(hex, out Color value) ? value : Color.white;
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        private static bool RestartPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.R);
#endif
        }

        private static bool ContinuePressed()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            return keyboard != null && (keyboard.enterKey.wasPressedThisFrame ||
                                        keyboard.numpadEnterKey.wasPressedThisFrame ||
                                        keyboard.spaceKey.wasPressedThisFrame);
#else
            return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) ||
                   Input.GetKeyDown(KeyCode.Space);
#endif
        }

        private static bool PausePressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }

        private static bool TitlePressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.T);
#endif
        }
    }

    public sealed class GroundSurface : MonoBehaviour
    {
    }

    public sealed class EchoesPlayerController : MonoBehaviour
    {
        private const float MoveSpeed = 5.8f;
        private const float JumpVelocity = 10.8f;
        private const float CoyoteTime = 0.17f;
        private const float JumpBuffer = 0.16f;

        private Rigidbody2D body;
        private CapsuleCollider2D capsule;
        private SpriteRenderer visual;
        private Sprite idleSprite;
        private Sprite driveA;
        private Sprite driveB;
        private Sprite jumpSprite;
        private Sprite hurtSprite;
        private float horizontalInput;
        private float coyoteTimer;
        private float jumpBufferTimer;
        private float animationTimer;
        private bool alternateDriveFrame;
        private bool grounded;
        private bool wasGrounded;
        private bool controlEnabled = true;
        private bool dead;
        private float landSoundCooldown;

        public Vector2 SafeAnchor { get; private set; }
        public bool HasSafeAnchor { get; private set; }

        public void Configure(Rigidbody2D targetBody, CapsuleCollider2D targetCapsule, SpriteRenderer targetVisual,
            Sprite idle, Sprite driveFrameA, Sprite driveFrameB, Sprite jumping, Sprite hurt, Vector2 initialAnchor)
        {
            body = targetBody;
            capsule = targetCapsule;
            visual = targetVisual;
            idleSprite = idle;
            driveA = driveFrameA;
            driveB = driveFrameB;
            jumpSprite = jumping;
            hurtSprite = hurt;
            SafeAnchor = initialAnchor;
            HasSafeAnchor = true;
        }

        private void Update()
        {
            ReadInput();
            UpdateGrounding();
            landSoundCooldown = Mathf.Max(0f, landSoundCooldown - Time.unscaledDeltaTime);
            if (grounded && !wasGrounded && controlEnabled && !dead && landSoundCooldown <= 0f)
            {
                landSoundCooldown = 0.12f;
                EchoesGame.Instance.PlayLandSound();
            }
            wasGrounded = grounded;
            UpdateAnimation();

            if (!controlEnabled || dead)
            {
                horizontalInput = 0f;
                return;
            }

            coyoteTimer = grounded ? CoyoteTime : Mathf.Max(0f, coyoteTimer - Time.deltaTime);
            jumpBufferTimer = Mathf.Max(0f, jumpBufferTimer - Time.deltaTime);
            if (jumpBufferTimer > 0f && coyoteTimer > 0f)
            {
                Vector2 velocity = body.linearVelocity;
                velocity.y = JumpVelocity;
                body.linearVelocity = velocity;
                grounded = false;
                coyoteTimer = 0f;
                jumpBufferTimer = 0f;
                EchoesGame.Instance.PlayJumpSound();
            }
        }

        private void FixedUpdate()
        {
            if (body == null || !body.simulated)
            {
                return;
            }
            Vector2 velocity = body.linearVelocity;
            velocity.x = controlEnabled && !dead ? horizontalInput * MoveSpeed : 0f;
            body.linearVelocity = velocity;
        }

        private void ReadInput()
        {
            horizontalInput = 0f;
            bool jumpPressed = false;
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
                {
                    horizontalInput -= 1f;
                }
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
                {
                    horizontalInput += 1f;
                }
                jumpPressed = keyboard.spaceKey.wasPressedThisFrame || keyboard.wKey.wasPressedThisFrame ||
                              keyboard.upArrowKey.wasPressedThisFrame;
            }
#else
            horizontalInput = Input.GetAxisRaw("Horizontal");
            jumpPressed = Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow);
#endif
            if (jumpPressed && controlEnabled && !dead)
            {
                jumpBufferTimer = JumpBuffer;
            }
        }

        private void UpdateGrounding()
        {
            if (body == null || !body.simulated)
            {
                grounded = false;
                return;
            }

            grounded = false;
            float bestSurface = float.NegativeInfinity;
            RaycastHit2D[] hits = Physics2D.BoxCastAll(transform.position, new Vector2(0.64f, 0.08f), 0f,
                Vector2.down, 0.75f);
            for (int i = 0; i < hits.Length; i++)
            {
                Collider2D hit = hits[i].collider;
                if (hit == null || hit == capsule || hit.GetComponent<GroundSurface>() == null)
                {
                    continue;
                }
                float surface = hit.bounds.max.y;
                if (surface > bestSurface && body.linearVelocity.y <= 0.25f)
                {
                    bestSurface = surface;
                    grounded = true;
                }
            }

            if (grounded && controlEnabled && !dead)
            {
                SafeAnchor = new Vector2(transform.position.x, bestSurface + 0.12f);
                HasSafeAnchor = true;
            }
        }

        private void UpdateAnimation()
        {
            if (visual == null)
            {
                return;
            }
            if (dead)
            {
                visual.sprite = hurtSprite;
                return;
            }
            if (!grounded)
            {
                visual.sprite = jumpSprite;
            }
            else if (Mathf.Abs(horizontalInput) > 0.1f)
            {
                animationTimer -= Time.deltaTime;
                if (animationTimer <= 0f)
                {
                    animationTimer = 0.12f;
                    alternateDriveFrame = !alternateDriveFrame;
                }
                visual.sprite = alternateDriveFrame ? driveA : driveB;
            }
            else
            {
                visual.sprite = idleSprite;
            }
            if (Mathf.Abs(horizontalInput) > 0.05f)
            {
                visual.flipX = horizontalInput < 0f;
            }
        }

        public void Bounce(float strength)
        {
            if (dead || !controlEnabled)
            {
                return;
            }
            Vector2 velocity = body.linearVelocity;
            velocity.y = strength;
            body.linearVelocity = velocity;
            grounded = false;
            wasGrounded = false;
            coyoteTimer = 0f;
        }

        public void SetSafeAnchor(Vector2 anchor)
        {
            SafeAnchor = anchor;
            HasSafeAnchor = true;
        }

        public void BeginDeath()
        {
            dead = true;
            controlEnabled = false;
            wasGrounded = false;
            body.linearVelocity = Vector2.zero;
            body.simulated = false;
            capsule.enabled = false;
            visual.sprite = hurtSprite;
            visual.color = new Color(1f, 0.55f, 0.58f, 0.48f);
        }

        public void Respawn(Vector3 position)
        {
            body.simulated = false;
            transform.position = position;
            body.position = position;
            body.rotation = 0f;
            horizontalInput = 0f;
            coyoteTimer = 0f;
            jumpBufferTimer = 0f;
            animationTimer = 0f;
            grounded = false;
            wasGrounded = false;
            landSoundCooldown = 0.12f;
            body.simulated = true;
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            capsule.enabled = true;
            visual.color = Color.white;
            dead = false;
            controlEnabled = true;
            SetSafeAnchor(new Vector2(position.x, position.y - 0.63f));
            Physics2D.SyncTransforms();
            TrailRenderer trail = GetComponent<TrailRenderer>();
            if (trail != null)
            {
                trail.Clear();
            }
        }

        public void StopMotion()
        {
            body.linearVelocity = Vector2.zero;
        }

        public void SetControlEnabled(bool enabled)
        {
            controlEnabled = enabled;
        }
    }

    public sealed class EchoSpring : MonoBehaviour
    {
        private const float LaunchStrength = 14.4f;
        private const float BounceCooldown = 0.26f;
        private float cooldown;
        private float pulseTime;
        private Transform baseVisual;
        private Transform ringVisual;
        private Transform glowVisual;

        public static EchoSpring Create(Vector2 position, Transform parent, Sprite square, Sprite ring, Sprite glow, Color color)
        {
            GameObject springObject = new GameObject("Echo Spring");
            springObject.transform.SetParent(parent, false);
            springObject.transform.position = position;
            BoxCollider2D trigger = springObject.AddComponent<BoxCollider2D>();
            trigger.isTrigger = true;
            trigger.size = new Vector2(1.72f, 0.58f);
            trigger.offset = new Vector2(0f, 0.19f);
            EchoSpring spring = springObject.AddComponent<EchoSpring>();

            spring.glowVisual = CreateChild("Echo Glow", springObject.transform, glow, color, 12,
                new Vector2(2.1f, 1.2f), new Vector2(0f, 0.12f));
            spring.glowVisual.GetComponent<SpriteRenderer>().color = new Color(color.r, color.g, color.b, 0.21f);
            spring.ringVisual = CreateChild("Echo Ring", springObject.transform, ring, color, 14,
                new Vector2(1.55f, 0.65f), new Vector2(0f, 0.14f));
            spring.baseVisual = CreateChild("Echo Pad", springObject.transform, square, color, 15,
                new Vector2(1.12f, 0.18f), new Vector2(0f, 0.07f));
            CreateChild("Echo Core", springObject.transform, square, Color.white, 16,
                new Vector2(0.68f, 0.055f), new Vector2(0f, 0.12f));
            spring.pulseTime = 1f;
            return spring;
        }

        private static Transform CreateChild(string objectName, Transform parent, Sprite sprite, Color color,
            int order, Vector2 scale, Vector2 position)
        {
            GameObject child = new GameObject(objectName);
            child.transform.SetParent(parent, false);
            child.transform.localPosition = position;
            child.transform.localScale = new Vector3(scale.x, scale.y, 1f);
            SpriteRenderer renderer = child.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sortingOrder = order;
            return child.transform;
        }

        private void Update()
        {
            cooldown = Mathf.Max(0f, cooldown - Time.deltaTime);
            pulseTime = Mathf.Max(0f, pulseTime - Time.deltaTime * 2.2f);
            float idle = 1f + Mathf.Sin(Time.time * 4.5f) * 0.055f;
            float pulse = 1f + pulseTime * 0.5f;
            ringVisual.localScale = new Vector3(1.55f * idle * pulse, 0.65f * idle * pulse, 1f);
            glowVisual.localScale = new Vector3(2.1f * idle, 1.2f * idle, 1f);
            baseVisual.localScale = new Vector3(1.12f, 0.18f * (1f + pulseTime * 0.35f), 1f);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            TryBounce(other);
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            TryBounce(other);
        }

        private void TryBounce(Collider2D other)
        {
            if (cooldown > 0f)
            {
                return;
            }
            EchoesPlayerController target = other.GetComponent<EchoesPlayerController>();
            if (target == null)
            {
                return;
            }
            cooldown = BounceCooldown;
            EchoesGame.Instance.BouncePlayer(target, LaunchStrength, this);
        }

        public void Pulse()
        {
            pulseTime = 1f;
        }

        public void Refresh()
        {
            pulseTime = 1f;
        }
    }

    public sealed class EchoesHazard : MonoBehaviour
    {
        public void Configure()
        {
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            TryKill(other);
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            TryKill(other);
        }

        private static void TryKill(Collider2D other)
        {
            EchoesPlayerController player = other.GetComponent<EchoesPlayerController>();
            if (player != null && EchoesGame.Instance != null)
            {
                EchoesGame.Instance.KillPlayer(player);
            }
        }
    }

    public sealed class EchoesCheckpoint : MonoBehaviour
    {
        private int generationId;
        private int sector;
        private Vector3 respawnPosition;

        public void Configure(int checkpointGeneration, int checkpointSector, Vector3 checkpointRespawn)
        {
            generationId = checkpointGeneration;
            sector = checkpointSector;
            respawnPosition = checkpointRespawn;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (other.GetComponent<EchoesPlayerController>() != null)
            {
                EchoesGame.Instance.ActivateCheckpoint(generationId, sector, respawnPosition);
            }
        }
    }

    public sealed class EchoesPulseVisual : MonoBehaviour
    {
        private Vector2 baseScale;
        private float amplitude;
        private float frequency;

        public void Configure(Vector2 scale, float pulseAmplitude, float pulseFrequency)
        {
            baseScale = scale;
            amplitude = pulseAmplitude;
            frequency = pulseFrequency;
        }

        private void Update()
        {
            float pulse = 1f + Mathf.Sin(Time.time * frequency) * amplitude;
            transform.localScale = new Vector3(baseScale.x * pulse, baseScale.y * pulse, 1f);
        }
    }

    public sealed class EchoesExit : MonoBehaviour
    {
        private Transform ring;

        public void Configure(Transform pulseRing)
        {
            ring = pulseRing;
        }

        private void Update()
        {
            if (ring != null)
            {
                float pulse = 1f + Mathf.Sin(Time.time * 2.2f) * 0.08f;
                ring.localScale = Vector3.one * (2.25f * pulse);
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (other.GetComponent<EchoesPlayerController>() != null)
            {
                EchoesGame.Instance.Win();
            }
        }
    }

    public sealed class EchoesCameraFollow : MonoBehaviour
    {
        public Transform Target { get; set; }
        private float fixedX;
        private float minY;
        private float maxY;
        private float trauma;
        private Vector3 velocity;

        public void Configure(float x, float minimumY, float maximumY)
        {
            fixedX = x;
            minY = minimumY;
            maxY = maximumY;
        }

        private void LateUpdate()
        {
            if (Target == null)
            {
                return;
            }
            float targetY = Mathf.Clamp(Target.position.y + 2f, minY, maxY);
            trauma = Mathf.Max(0f, trauma - Time.unscaledDeltaTime * 1.7f);
            float shake = trauma * trauma * 0.32f;
            Vector2 noise = UnityEngine.Random.insideUnitCircle * shake;
            Vector3 destination = new Vector3(fixedX + noise.x, targetY + noise.y, -10f);
            transform.position = Vector3.SmoothDamp(transform.position, destination, ref velocity, 0.23f);
        }

        public void AddTrauma(float amount)
        {
            trauma = Mathf.Clamp01(trauma + amount);
        }

        public void SnapTo(float y)
        {
            trauma = 0f;
            velocity = Vector3.zero;
            transform.position = new Vector3(fixedX, Mathf.Clamp(y, minY, maxY), -10f);
        }
    }
}
