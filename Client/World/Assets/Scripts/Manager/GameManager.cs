using System;
using UnityEngine;

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
            Time.timeScale = 0f;

            var score = KillCount * 10 + Mathf.FloorToInt(ElapsedSeconds);
            NetworkManager.Instance?.SendGameOver(score, KillCount, Mathf.FloorToInt(ElapsedSeconds));

            OnGameOver?.Invoke();
        }

        private void Update()
        {
            if (State == GameState.Playing)
                ElapsedSeconds += Time.deltaTime;
        }
    }
}
