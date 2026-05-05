using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Game.Manager
{
    public enum GameState { Idle, Playing, GameOver }

    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        public GameState State { get; private set; } = GameState.Idle;
        public int KillCount { get; private set; }
        public float ElapsedSeconds { get; private set; }

        public event Action OnGameStarted;
        public event Action OnGameOver;
        public event Action<int> OnKillCountChanged;

        private bool _fallbackMode;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        public void StartGame()
        {
            State = GameState.Playing;
            KillCount = 0;
            ElapsedSeconds = 0f;
            _fallbackMode = false;
            Time.timeScale = 1f;
            OnGameStarted?.Invoke();
        }

        public void AddKill()
        {
            KillCount++;
            OnKillCountChanged?.Invoke(KillCount);
        }

        public void GameOver()
        {
            if (State != GameState.Playing) return;
            State = GameState.GameOver;

            var score = KillCount * 10 + Mathf.FloorToInt(ElapsedSeconds);
            NetworkManager.Instance?.SendGameOver(score, KillCount, Mathf.FloorToInt(ElapsedSeconds));

            if (OnGameOver != null)
            {
                Time.timeScale = 0f;
                Debug.Log($"[GameManager] GameOver — timeScale=0, 구독자={OnGameOver.GetInvocationList().Length}명");
                OnGameOver.Invoke();
            }
            else
            {
                _fallbackMode = true;
                Debug.LogWarning($"[GameManager] GameOver (폴백) — 킬:{KillCount} 생존:{Mathf.FloorToInt(ElapsedSeconds)}초 | R키로 재시작");
            }
        }

        private void Update()
        {
            if (State == GameState.Playing)
            {
                ElapsedSeconds += Time.deltaTime;
                return;
            }

            if (_fallbackMode && Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
            {
                Time.timeScale = 1f;
                SceneManager.LoadScene(SceneManager.GetActiveScene().name);
            }
        }
    }
}
