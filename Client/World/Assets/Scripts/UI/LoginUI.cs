using Game.Manager;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Game.UI
{
    public class LoginUI : MonoBehaviour
    {
        [SerializeField] private InputField userIdField;
        [SerializeField] private InputField passwordField;
        [SerializeField] private Button loginButton;
        [SerializeField] private Text statusText;
        [SerializeField] private string gameSceneName = "GameScene";
        [SerializeField] private ulong worldId = 1;

        private void Start()
        {
            if (loginButton != null) loginButton.onClick.AddListener(OnLoginClicked);

            if (NetworkManager.Instance != null)
            {
                if (userIdField != null) userIdField.text = NetworkManager.Instance.userId;
                if (passwordField != null) passwordField.text = NetworkManager.Instance.password;

                NetworkManager.Instance.OnLoginSuccess += HandleLoginSuccess;
                NetworkManager.Instance.OnLoginFailed += HandleLoginFailed;
                NetworkManager.Instance.OnEnterWorldResult += HandleEnterWorldResult;
            }
        }

        private void OnDestroy()
        {
            if (NetworkManager.Instance != null)
            {
                NetworkManager.Instance.OnLoginSuccess -= HandleLoginSuccess;
                NetworkManager.Instance.OnLoginFailed -= HandleLoginFailed;
                NetworkManager.Instance.OnEnterWorldResult -= HandleEnterWorldResult;
            }
        }

        private async void OnLoginClicked()
        {
            if (NetworkManager.Instance == null)
            {
                SetStatus("NetworkManager 없음");
                return;
            }

            if (loginButton != null) loginButton.interactable = false;

            if (userIdField != null) NetworkManager.Instance.userId = userIdField.text;
            if (passwordField != null) NetworkManager.Instance.password = passwordField.text;

            SetStatus("서버 연결 중...");
            var ok = await NetworkManager.Instance.ConnectAsync();
            if (!ok)
            {
                SetStatus("서버 연결 실패");
                if (loginButton != null) loginButton.interactable = true;
            }
            else
            {
                SetStatus("로그인 중...");
            }
        }

        private void HandleLoginSuccess()
        {
            SetStatus("월드 입장 중...");
            NetworkManager.Instance.SendEnterWorld(worldId);
        }

        private void HandleLoginFailed(int errorCode)
        {
            SetStatus($"로그인 실패 (code={errorCode})");
            if (loginButton != null) loginButton.interactable = true;
        }

        private void HandleEnterWorldResult(bool success)
        {
            if (success)
                SceneManager.LoadScene(gameSceneName);
            else
            {
                SetStatus("월드 입장 실패");
                if (loginButton != null) loginButton.interactable = true;
            }
        }

        private void SetStatus(string text)
        {
            if (statusText != null) statusText.text = text;
            Debug.Log($"[LoginUI] {text}");
        }
    }
}
