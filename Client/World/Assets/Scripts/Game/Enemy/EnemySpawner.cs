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
        [SerializeField] private float spawnRadius = 12f;
        [SerializeField] private float difficultyRampSeconds = 60f;

        private float _timer;

        private void Update()
        {
            if (GameManager.Instance == null || GameManager.Instance.State != GameState.Playing) return;
            if (enemyPrefab == null || playerTransform == null) return;

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
        }
    }
}
