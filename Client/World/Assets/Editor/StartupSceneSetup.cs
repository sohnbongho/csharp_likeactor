using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// 빌드 시작 시 LoginScene이 첫 씬이 되도록 보장하고,
    /// Editor의 ▶ Play 시작 시에도 항상 LoginScene부터 시작하도록 자동 설정한다.
    /// </summary>
    [InitializeOnLoad]
    public static class StartupSceneSetup
    {
        private const string LoginScenePath = "Assets/Scenes/LoginScene.unity";
        private const string GameScenePath  = "Assets/Scenes/GameScene.unity";

        static StartupSceneSetup()
        {
            EditorApplication.delayCall += SyncEditorPlayModeStartScene;
        }

        // ───────── 메뉴: 빌드 순서 정렬 ─────────

        [MenuItem("Tools/Build Scenes/Configure Build Order (Login → Game)")]
        public static void ConfigureBuildOrder()
        {
            var login = AssetDatabase.LoadAssetAtPath<SceneAsset>(LoginScenePath);
            var game  = AssetDatabase.LoadAssetAtPath<SceneAsset>(GameScenePath);

            if (login == null)
            {
                Debug.LogError($"[StartupSceneSetup] {LoginScenePath} 가 없습니다. 먼저 'Tools → Build Scenes → Login Scene'을 실행하세요.");
                return;
            }

            var scenes = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(LoginScenePath, true)
            };
            if (game != null)
                scenes.Add(new EditorBuildSettingsScene(GameScenePath, true));

            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("[StartupSceneSetup] Build Settings 정리 완료: LoginScene(0)" + (game != null ? ", GameScene(1)" : ""));

            SyncEditorPlayModeStartScene();
        }

        // ───────── 메뉴: Editor Play도 LoginScene부터 ─────────

        [MenuItem("Tools/Build Scenes/Editor Play Starts From Login")]
        public static void ToggleEditorPlayFromLogin()
        {
            var current = EditorSceneManager.playModeStartScene;
            if (current != null && AssetDatabase.GetAssetPath(current) == LoginScenePath)
            {
                EditorSceneManager.playModeStartScene = null;
                Debug.Log("[StartupSceneSetup] Editor Play 시작 씬: (현재 씬 그대로)");
            }
            else
            {
                var login = AssetDatabase.LoadAssetAtPath<SceneAsset>(LoginScenePath);
                if (login == null)
                {
                    Debug.LogError($"[StartupSceneSetup] {LoginScenePath} 없음");
                    return;
                }
                EditorSceneManager.playModeStartScene = login;
                Debug.Log("[StartupSceneSetup] Editor Play 시작 씬: LoginScene 고정");
            }
        }

        [MenuItem("Tools/Build Scenes/Editor Play Starts From Login", true)]
        private static bool ToggleEditorPlayFromLogin_Validate()
        {
            var current = EditorSceneManager.playModeStartScene;
            bool isOn = current != null && AssetDatabase.GetAssetPath(current) == LoginScenePath;
            Menu.SetChecked("Tools/Build Scenes/Editor Play Starts From Login", isOn);
            return true;
        }

        // ───────── 자동 동기화 ─────────

        private static void SyncEditorPlayModeStartScene()
        {
            var login = AssetDatabase.LoadAssetAtPath<SceneAsset>(LoginScenePath);
            if (login != null && EditorSceneManager.playModeStartScene == null)
                EditorSceneManager.playModeStartScene = login;
        }
    }
}
