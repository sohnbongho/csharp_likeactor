using Game.Manager;
using UnityEngine;

namespace Game.Enemy
{
    public class EnemySpawner : MonoBehaviour
    {
        [SerializeField] private GameObject enemyPrefab;
        [SerializeField] private Transform playerTransform;
        [SerializeField] private float baseSpawnInterval = 1f;
        [SerializeField] private float minSpawnInterval = 0.1f;
        [SerializeField] private float spawnRadius = 8f;
        [SerializeField] private float difficultyRampSeconds = 60f;
        [SerializeField] private bool verboseLog = false;

        private float _timer = 0f;
        private bool _firstFrameLogged;

        private void Update()
        {
            if (GameManager.Instance == null)
            {
                if (verboseLog && !_firstFrameLogged) Debug.LogWarning("[Spawner] GameManager.Instance == null");
                _firstFrameLogged = true;
                return;
            }
            if (GameManager.Instance.State != GameState.Playing)
            {
                if (verboseLog && !_firstFrameLogged) Debug.LogWarning($"[Spawner] State={GameManager.Instance.State} (Playing 아님)");
                _firstFrameLogged = true;
                return;
            }
            if (enemyPrefab == null || playerTransform == null)
            {
                if (verboseLog && !_firstFrameLogged) Debug.LogWarning($"[Spawner] enemyPrefab={enemyPrefab}, player={playerTransform}");
                _firstFrameLogged = true;
                return;
            }

            _timer -= Time.deltaTime;
            if (_timer > 0f) return;

            var t = Mathf.Clamp01(GameManager.Instance.ElapsedSeconds / difficultyRampSeconds);
            _timer = Mathf.Lerp(baseSpawnInterval, minSpawnInterval, t);
            Spawn();
        }

        private void Spawn()
        {
            var angle = Random.Range(0f, Mathf.PI * 2f);
            var pos = (Vector2)playerTransform.position + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * spawnRadius;
            var enemy = Instantiate(enemyPrefab, pos, Quaternion.identity);
            if (enemy.TryGetComponent(out EnemyController ctrl))
                ctrl.Init(playerTransform);
            if (verboseLog) Debug.Log($"[Spawner] 적 스폰 @ {pos} (총 적 수: {GameObject.FindObjectsByType<EnemyController>(FindObjectsSortMode.None).Length})");
        }
    }
}
