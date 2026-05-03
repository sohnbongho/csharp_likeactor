using System.Collections.Generic;
using System.IO;
using Game.Manager;
using Game.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Game.EditorTools
{
    public static class LoginSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/LoginScene.unity";

        [MenuItem("Tools/Build Scenes/Login Scene")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            CreateNetworkManager();
            var canvas = CreateCanvas();
            CreateEventSystem();
            CreateLoginPanel(canvas);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath)!);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[LoginSceneBuilder] {ScenePath} 생성 완료");

            AddToBuildSettings(ScenePath, 0);
        }

        private static void CreateNetworkManager()
        {
            var go = new GameObject("_NetworkManager");
            go.AddComponent<NetworkManager>();
        }

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

        private static void CreateLoginPanel(GameObject canvas)
        {
            var panel = CreateRect("LoginPanel", canvas.transform);
            var panelRT = panel.GetComponent<RectTransform>();
            panelRT.anchorMin = Vector2.zero;
            panelRT.anchorMax = Vector2.one;
            panelRT.offsetMin = Vector2.zero;
            panelRT.offsetMax = Vector2.zero;
            var img = panel.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.5f);

            CreateText(panel.transform, "Title", "로그인", 48, new Vector2(0, -150), new Vector2(800, 80), new Vector2(0.5f, 1f));
            var userIdField = CreateInputField(panel.transform, "UserIdField", "User ID", InputField.ContentType.Standard, new Vector2(0, -300));
            var passwordField = CreateInputField(panel.transform, "PasswordField", "Password", InputField.ContentType.Password, new Vector2(0, -360));
            var loginBtn = CreateButton(panel.transform, "LoginButton", "Login", new Vector2(0, -440));
            var statusText = CreateText(panel.transform, "StatusText", "", 24, new Vector2(0, -510), new Vector2(600, 40), new Vector2(0.5f, 1f));
            statusText.color = new Color(1f, 0.85f, 0.3f);

            var loginUI = panel.AddComponent<LoginUI>();
            var so = new SerializedObject(loginUI);
            so.FindProperty("userIdField").objectReferenceValue = userIdField.GetComponent<InputField>();
            so.FindProperty("passwordField").objectReferenceValue = passwordField.GetComponent<InputField>();
            so.FindProperty("loginButton").objectReferenceValue = loginBtn.GetComponent<Button>();
            so.FindProperty("statusText").objectReferenceValue = statusText;
            so.FindProperty("gameSceneName").stringValue = "GameScene";

            var worldIdProp = so.FindProperty("worldId");
            if (worldIdProp != null)
                worldIdProp.boxedValue = 1UL;
            so.ApplyModifiedProperties();
        }

        private static GameObject CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
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

        private static GameObject CreateInputField(Transform parent, string name, string placeholder, InputField.ContentType contentType, Vector2 anchoredPos)
        {
            var go = CreateRect(name, parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(400, 40);
            rt.anchoredPosition = anchoredPos;

            var bg = go.AddComponent<Image>();
            bg.color = new Color(1f, 1f, 1f, 0.85f);
            bg.type = Image.Type.Sliced;

            var placeholderText = CreateText(go.transform, "Placeholder", placeholder, 18, Vector2.zero, new Vector2(-20, -10), new Vector2(0.5f, 0.5f));
            placeholderText.color = new Color(0.4f, 0.4f, 0.4f);
            placeholderText.fontStyle = FontStyle.Italic;
            placeholderText.alignment = TextAnchor.MiddleLeft;
            var phRT = placeholderText.GetComponent<RectTransform>();
            phRT.anchorMin = Vector2.zero;
            phRT.anchorMax = Vector2.one;
            phRT.offsetMin = new Vector2(10, 0);
            phRT.offsetMax = new Vector2(-10, 0);

            var inputText = CreateText(go.transform, "Text", "", 18, Vector2.zero, new Vector2(-20, -10), new Vector2(0.5f, 0.5f));
            inputText.color = Color.black;
            inputText.alignment = TextAnchor.MiddleLeft;
            var txtRT = inputText.GetComponent<RectTransform>();
            txtRT.anchorMin = Vector2.zero;
            txtRT.anchorMax = Vector2.one;
            txtRT.offsetMin = new Vector2(10, 0);
            txtRT.offsetMax = new Vector2(-10, 0);

            var input = go.AddComponent<InputField>();
            input.targetGraphic = bg;
            input.textComponent = inputText;
            input.placeholder = placeholderText;
            input.contentType = contentType;
            return go;
        }

        private static GameObject CreateButton(Transform parent, string name, string label, Vector2 anchoredPos)
        {
            var go = CreateRect(name, parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(200, 50);
            rt.anchoredPosition = anchoredPos;

            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.2f, 0.5f, 0.9f);

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;

            var label1 = CreateText(go.transform, "Text", label, 22, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
            var labelRT = label1.GetComponent<RectTransform>();
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
            Debug.Log($"[LoginSceneBuilder] Build Settings에 등록 (index {targetIndex})");
        }
    }
}
