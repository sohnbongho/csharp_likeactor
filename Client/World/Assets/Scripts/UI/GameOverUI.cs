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
            if (panel != null) panel.SetActive(false);
            if (restartButton != null)
            {
                restartButton.onClick.AddListener(OnRestartClicked);
                restartButton.interactable = false;
            }

            if (GameManager.Instance != null)
                GameManager.Instance.OnGameOver += HandleGameOver;
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
            if (panel != null) panel.SetActive(true);

            var gm = GameManager.Instance;
            if (scoreText != null && gm != null)
            {
                int t = Mathf.FloorToInt(gm.ElapsedSeconds);
                int score = gm.KillCount * 10 + t;
                scoreText.text = $"점수 {score}\n킬 {gm.KillCount}\n생존 {t / 60:D2}:{t % 60:D2}";
            }
            if (statusText != null) statusText.text = "서버 저장 중...";
        }

        private void HandleGameOverResult(bool success)
        {
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
