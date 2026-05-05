using Game.Manager;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Game.UI
{
    public class GameOverUI : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private Text scoreText;
        [SerializeField] private Text statusText;
        [SerializeField] private Button restartButton;
        [SerializeField] private string loginSceneName = "LoginScene";

        private void Start()
        {
            Debug.Log($"[GameOverUI:Start] panel={panel}, restartButton={restartButton}, GameManager={GameManager.Instance}, NetworkManager={NetworkManager.Instance}");

            if (panel != null) panel.SetActive(false);
            if (restartButton != null)
            {
                restartButton.onClick.AddListener(OnRestartClicked);
                restartButton.interactable = false;
            }

            if (GameManager.Instance != null)
                GameManager.Instance.OnGameOver += HandleGameOver;
            else
                Debug.LogError("[GameOverUI:Start] GameManager.Instance가 null — GameOver 이벤트를 받을 수 없습니다!");

            if (NetworkManager.Instance != null)
                NetworkManager.Instance.OnGameOverResult += HandleGameOverResult;
        }

        private void OnDestroy()
        {
            if (GameManager.Instance != null)
                GameManager.Instance.OnGameOver -= HandleGameOver;
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.OnGameOverResult -= HandleGameOverResult;
        }

        private void HandleGameOver()
        {
            Debug.Log($"[GameOverUI:HandleGameOver] panel={panel}, restartButton={restartButton}");

            if (panel != null)
                panel.SetActive(true);
            else
                Debug.LogError("[GameOverUI:HandleGameOver] panel이 null! Inspector에서 Panel을 연결하세요. timeScale을 강제 복구합니다.");

            var gm = GameManager.Instance;
            if (scoreText != null && gm != null)
            {
                int t = Mathf.FloorToInt(gm.ElapsedSeconds);
                int score = gm.KillCount * 10 + t;
                scoreText.text = $"점수 {score}\n킬 {gm.KillCount}\n생존 {t / 60:D2}:{t % 60:D2}";
            }
            if (statusText != null) statusText.text = "서버 저장 중...";

            // panel이 없어도 버튼이라도 눌릴 수 있게 restartButton 활성화
            if (restartButton != null)
                restartButton.interactable = true;
        }

        private void HandleGameOverResult(bool success)
        {
            Debug.Log($"[GameOverUI:HandleGameOverResult] success={success}");
            if (statusText != null) statusText.text = success ? "저장 완료" : "저장 실패";
            if (restartButton != null) restartButton.interactable = true;
        }

        private void OnRestartClicked()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(loginSceneName);
        }
    }
}
