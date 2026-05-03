using System.Collections.Generic;
using System.IO;
using Game.Enemy;
using Game.Manager;
using Game.Player;
using Game.Systems;
using Game.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Game.EditorTools
{
    public static class GameSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/GameScene.unity";
        private const string PrefabsDir = "Assets/Prefabs";
        private const string EnemyPrefabPath = "Assets/Prefabs/Enemy.prefab";
        private const string XpOrbPrefabPath = "Assets/Prefabs/XpOrb.prefab";
        private const string EnemyLayerName = "Enemy";
        private const string PlayerTag = "Player";

        [MenuItem("Tools/Build Scenes/Game Scene")]
        public static void Build()
        {
            EnsureTag(PlayerTag);
            int enemyLayer = EnsureLayer(EnemyLayerName);
            int enemyMask = 1 << enemyLayer;

            Directory.CreateDirectory(PrefabsDir);
            var xpOrbPrefab = BuildXpOrbPrefab();
            var enemyPrefab = BuildEnemyPrefab(xpOrbPrefab);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            ConfigureMainCamera();
            CreateBootstrap();

            var (gm, upgradeSystem) = CreateGameManager();
            var player = CreatePlayer(enemyMask);
            CreateSpawner(enemyPrefab, player.transform);
            ConnectUpgradeSystem(upgradeSystem, player);

            var canvas = CreateCanvas();
            CreateEventSystem();
            var hud = CreateHud(canvas, player);
            var upgradeUI = CreateUpgradePanel(canvas, upgradeSystem);
            var gameOverUI = CreateGameOverPanel(canvas);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath)!);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[GameSceneBuilder] {ScenePath} 생성 완료");

            AddToBuildSettings(ScenePath, 1);
        }

        // ───────── 레이어 / 태그 등록 ─────────

        private static int EnsureLayer(string name)
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");
            for (int i = 8; i < layers.arraySize; i++)
            {
                var prop = layers.GetArrayElementAtIndex(i);
                if (prop.stringValue == name) return i;
            }
            for (int i = 8; i < layers.arraySize; i++)
            {
                var prop = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(prop.stringValue))
                {
                    prop.stringValue = name;
                    tagManager.ApplyModifiedProperties();
                    Debug.Log($"[GameSceneBuilder] 레이어 추가: {name} (index {i})");
                    return i;
                }
            }
            Debug.LogError("[GameSceneBuilder] 사용 가능한 레이어 슬롯 없음");
            return 0;
        }

        private static void EnsureTag(string name)
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var tags = tagManager.FindProperty("tags");
            for (int i = 0; i < tags.arraySize; i++)
                if (tags.GetArrayElementAtIndex(i).stringValue == name) return;
            tags.arraySize++;
            tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = name;
            tagManager.ApplyModifiedProperties();
            Debug.Log($"[GameSceneBuilder] 태그 추가: {name}");
        }

        // ───────── 프리팹 ─────────

        private static GameObject BuildXpOrbPrefab()
        {
            var go = CreateCircleSprite("XpOrb", new Color(1f, 0.85f, 0.2f), 0.3f);
            go.AddComponent<XpOrb>();

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, XpOrbPrefabPath);
            Object.DestroyImmediate(go);
            return prefab;
        }

        private static GameObject BuildEnemyPrefab(GameObject xpOrbPrefab)
        {
            var go = CreateCircleSprite("Enemy", new Color(0.9f, 0.2f, 0.2f), 1f);
            go.layer = LayerMask.NameToLayer(EnemyLayerName);

            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;

            var col = go.AddComponent<CircleCollider2D>();
            col.radius = 0.5f;

            var ec = go.AddComponent<EnemyController>();
            var so = new SerializedObject(ec);
            so.FindProperty("xpOrbPrefab").objectReferenceValue = xpOrbPrefab;
            so.ApplyModifiedProperties();

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, EnemyPrefabPath);
            Object.DestroyImmediate(go);
            return prefab;
        }

        // ───────── 씬 구성 ─────────

        private static void ConfigureMainCamera()
        {
            var cam = Camera.main;
            if (cam == null) return;
            cam.orthographic = true;
            cam.orthographicSize = 8f;
            cam.transform.position = new Vector3(0f, 0f, -10f);
            cam.backgroundColor = new Color(0.1f, 0.1f, 0.15f);
            cam.clearFlags = CameraClearFlags.SolidColor;
        }

        private static void CreateBootstrap()
        {
            var go = new GameObject("_Bootstrap");
            go.AddComponent<GameSceneBootstrap>();
        }

        private static (GameManager gm, UpgradeSystem upgradeSystem) CreateGameManager()
        {
            var go = new GameObject("_GameManager");
            var gm = go.AddComponent<GameManager>();
            var us = go.AddComponent<UpgradeSystem>();
            return (gm, us);
        }

        private static GameObject CreatePlayer(int enemyMask)
        {
            var go = CreateCircleSprite("Player", new Color(0.3f, 0.7f, 1f), 1f);
            go.tag = PlayerTag;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;

            var col = go.AddComponent<CircleCollider2D>();
            col.radius = 0.5f;

            go.AddComponent<PlayerController>();
            go.AddComponent<PlayerStats>();

            var atk = go.AddComponent<AutoAttack>();
            var atkSo = new SerializedObject(atk);
            atkSo.FindProperty("enemyMask").intValue = enemyMask;
            atkSo.ApplyModifiedProperties();

            var ult = go.AddComponent<UltimateSkill>();
            var ultSo = new SerializedObject(ult);
            ultSo.FindProperty("enemyMask").intValue = enemyMask;
            ultSo.ApplyModifiedProperties();

            return go;
        }

        private static void CreateSpawner(GameObject enemyPrefab, Transform playerTransform)
        {
            var go = new GameObject("_Spawner");
            var spawner = go.AddComponent<EnemySpawner>();
            var so = new SerializedObject(spawner);
            so.FindProperty("enemyPrefab").objectReferenceValue = enemyPrefab;
            so.FindProperty("playerTransform").objectReferenceValue = playerTransform;
            so.ApplyModifiedProperties();
        }

        private static void ConnectUpgradeSystem(UpgradeSystem us, GameObject player)
        {
            var so = new SerializedObject(us);
            so.FindProperty("playerStats").objectReferenceValue = player.GetComponent<PlayerStats>();
            so.FindProperty("playerController").objectReferenceValue = player.GetComponent<PlayerController>();
            so.FindProperty("autoAttack").objectReferenceValue = player.GetComponent<AutoAttack>();
            so.FindProperty("ultimateSkill").objectReferenceValue = player.GetComponent<UltimateSkill>();
            so.ApplyModifiedProperties();
        }

        // ───────── UI ─────────

        private static GameObject CreateCanvas()
        {
            var go = new GameObject("Canvas");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            return go;
        }

        private static void CreateEventSystem()
        {
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }

        private static GameObject CreateHud(GameObject canvas, GameObject player)
        {
            var hud = CreateRect("Hud", canvas.transform);
            FillParent(hud);

            var levelText = CreateText(hud.transform, "LevelText", "Lv.1", 28, new Vector2(120, -40), new Vector2(200, 40), new Vector2(0f, 1f));
            levelText.alignment = TextAnchor.MiddleLeft;

            var hpBar = CreateSlider(hud.transform, "HpBar", new Vector2(120, -80), new Vector2(300, 20), new Vector2(0f, 1f), new Color(0.9f, 0.2f, 0.2f));
            var xpBar = CreateSlider(hud.transform, "XpBar", new Vector2(120, -110), new Vector2(300, 12), new Vector2(0f, 1f), new Color(0.4f, 0.9f, 0.4f));

            var timeText = CreateText(hud.transform, "TimeText", "00:00", 36, new Vector2(0, -40), new Vector2(300, 50), new Vector2(0.5f, 1f));
            var killText = CreateText(hud.transform, "KillText", "Kills: 0", 24, new Vector2(-120, -40), new Vector2(220, 40), new Vector2(1f, 1f));
            killText.alignment = TextAnchor.MiddleRight;

            var ultCdText = CreateText(hud.transform, "UltCdText", "궁극기 READY", 22, new Vector2(-120, 50), new Vector2(280, 40), new Vector2(1f, 0f));
            ultCdText.alignment = TextAnchor.MiddleRight;

            var hudUI = hud.AddComponent<HudUI>();
            var so = new SerializedObject(hudUI);
            so.FindProperty("playerStats").objectReferenceValue = player.GetComponent<PlayerStats>();
            so.FindProperty("ultimateSkill").objectReferenceValue = player.GetComponent<UltimateSkill>();
            so.FindProperty("hpBar").objectReferenceValue = hpBar;
            so.FindProperty("xpBar").objectReferenceValue = xpBar;
            so.FindProperty("levelText").objectReferenceValue = levelText;
            so.FindProperty("timeText").objectReferenceValue = timeText;
            so.FindProperty("killText").objectReferenceValue = killText;
            so.FindProperty("ultimateCooldownText").objectReferenceValue = ultCdText;
            so.ApplyModifiedProperties();
            return hud;
        }

        private static UpgradeUI CreateUpgradePanel(GameObject canvas, UpgradeSystem upgradeSystem)
        {
            var panel = CreateRect("UpgradePanel", canvas.transform);
            FillParent(panel);
            var bg = panel.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.7f);

            CreateText(panel.transform, "Title", "레벨업! 업그레이드 선택", 48, new Vector2(0, 200), new Vector2(900, 80), new Vector2(0.5f, 0.5f));

            var buttons = new Button[3];
            var titles = new Text[3];
            var descs = new Text[3];
            for (int i = 0; i < 3; i++)
            {
                float xPos = (i - 1) * 360f;
                var btn = CreateButton(panel.transform, $"Choice{i + 1}", "", new Vector2(xPos, -30));
                var btnRT = btn.GetComponent<RectTransform>();
                btnRT.sizeDelta = new Vector2(320, 200);

                var btnText = btn.transform.Find("Text").GetComponent<Text>();
                Object.DestroyImmediate(btnText.gameObject);

                titles[i] = CreateText(btn.transform, "Title", "(Title)", 26, new Vector2(0, 50), new Vector2(300, 50), new Vector2(0.5f, 0.5f));
                descs[i] = CreateText(btn.transform, "Description", "(Description)", 18, new Vector2(0, -30), new Vector2(300, 100), new Vector2(0.5f, 0.5f));
                descs[i].alignment = TextAnchor.UpperCenter;

                buttons[i] = btn.GetComponent<Button>();
            }

            var ui = panel.AddComponent<UpgradeUI>();
            var so = new SerializedObject(ui);
            so.FindProperty("upgradeSystem").objectReferenceValue = upgradeSystem;
            so.FindProperty("panel").objectReferenceValue = panel;

            var btnArr = so.FindProperty("choiceButtons");
            btnArr.arraySize = 3;
            for (int i = 0; i < 3; i++)
                btnArr.GetArrayElementAtIndex(i).objectReferenceValue = buttons[i];

            var titleArr = so.FindProperty("choiceTitles");
            titleArr.arraySize = 3;
            for (int i = 0; i < 3; i++)
                titleArr.GetArrayElementAtIndex(i).objectReferenceValue = titles[i];

            var descArr = so.FindProperty("choiceDescriptions");
            descArr.arraySize = 3;
            for (int i = 0; i < 3; i++)
                descArr.GetArrayElementAtIndex(i).objectReferenceValue = descs[i];

            so.ApplyModifiedProperties();

            panel.SetActive(false);
            return ui;
        }

        private static GameOverUI CreateGameOverPanel(GameObject canvas)
        {
            var panel = CreateRect("GameOverPanel", canvas.transform);
            FillParent(panel);
            var bg = panel.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.85f);

            CreateText(panel.transform, "Title", "GAME OVER", 72, new Vector2(0, 220), new Vector2(800, 100), new Vector2(0.5f, 0.5f));
            var scoreText = CreateText(panel.transform, "ScoreText", "", 32, new Vector2(0, 50), new Vector2(700, 200), new Vector2(0.5f, 0.5f));
            var statusText = CreateText(panel.transform, "StatusText", "서버 저장 중...", 22, new Vector2(0, -150), new Vector2(600, 40), new Vector2(0.5f, 0.5f));
            statusText.color = new Color(1f, 0.85f, 0.3f);

            var restart = CreateButton(panel.transform, "RestartButton", "Restart", new Vector2(0, -260));

            var ui = panel.AddComponent<GameOverUI>();
            var so = new SerializedObject(ui);
            so.FindProperty("panel").objectReferenceValue = panel;
            so.FindProperty("scoreText").objectReferenceValue = scoreText;
            so.FindProperty("statusText").objectReferenceValue = statusText;
            so.FindProperty("restartButton").objectReferenceValue = restart.GetComponent<Button>();
            so.FindProperty("loginSceneName").stringValue = "LoginScene";
            so.ApplyModifiedProperties();

            panel.SetActive(false);
            return ui;
        }

        // ───────── 공통 헬퍼 ─────────

        private static GameObject CreateCircleSprite(string name, Color color, float scale)
        {
            var go = new GameObject(name);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = MakeCircleSprite();
            sr.color = color;
            go.transform.localScale = Vector3.one * scale;
            return go;
        }

        private static Sprite _circleSprite;
        private static Sprite MakeCircleSprite()
        {
            if (_circleSprite != null) return _circleSprite;
            int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var center = new Vector2(size / 2f, size / 2f);
            var radius = size / 2f - 1;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), center);
                    tex.SetPixel(x, y, d <= radius ? Color.white : Color.clear);
                }
            tex.Apply();
            tex.filterMode = FilterMode.Bilinear;
            _circleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100);
            return _circleSprite;
        }

        private static GameObject CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static void FillParent(GameObject go)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static Text CreateText(Transform parent, string name, string content, int size, Vector2 anchoredPos, Vector2 sizeDelta, Vector2 anchor)
        {
            var go = CreateRect(name, parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.sizeDelta = sizeDelta;
            rt.anchoredPosition = anchoredPos;

            var text = go.AddComponent<Text>();
            text.text = content;
            text.fontSize = size;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return text;
        }

        private static Slider CreateSlider(Transform parent, string name, Vector2 anchoredPos, Vector2 sizeDelta, Vector2 anchor, Color fillColor)
        {
            var go = CreateRect(name, parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.sizeDelta = sizeDelta;
            rt.anchoredPosition = anchoredPos;

            var bg = CreateRect("Background", go.transform);
            FillParent(bg);
            var bgImg = bg.AddComponent<Image>();
            bgImg.color = new Color(0.15f, 0.15f, 0.15f, 0.8f);

            var fillArea = CreateRect("Fill Area", go.transform);
            FillParent(fillArea);
            var fillAreaRT = fillArea.GetComponent<RectTransform>();
            fillAreaRT.offsetMin = new Vector2(2, 2);
            fillAreaRT.offsetMax = new Vector2(-2, -2);

            var fill = CreateRect("Fill", fillArea.transform);
            var fillRT = fill.GetComponent<RectTransform>();
            fillRT.anchorMin = Vector2.zero;
            fillRT.anchorMax = Vector2.one;
            fillRT.offsetMin = Vector2.zero;
            fillRT.offsetMax = Vector2.zero;
            var fillImg = fill.AddComponent<Image>();
            fillImg.color = fillColor;

            var slider = go.AddComponent<Slider>();
            slider.targetGraphic = bgImg;
            slider.fillRect = fillRT;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = 1f;
            slider.transition = Selectable.Transition.None;
            return slider;
        }

        private static GameObject CreateButton(Transform parent, string name, string label, Vector2 anchoredPos)
        {
            var go = CreateRect(name, parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(220, 60);
            rt.anchoredPosition = anchoredPos;

            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.2f, 0.5f, 0.9f);

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;

            var labelText = CreateText(go.transform, "Text", label, 22, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
            var labelRT = labelText.GetComponent<RectTransform>();
            labelRT.anchorMin = Vector2.zero;
            labelRT.anchorMax = Vector2.one;
            labelRT.offsetMin = Vector2.zero;
            labelRT.offsetMax = Vector2.zero;
            return go;
        }

        private static void AddToBuildSettings(string scenePath, int targetIndex)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == scenePath);
            scenes.Insert(Mathf.Min(targetIndex, scenes.Count), new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log($"[GameSceneBuilder] Build Settings에 등록 (index {targetIndex})");
        }
    }
}
