using Game.Manager;
using Game.Player;
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

        private void Start()
        {
            TryFindPlayer();
        }

        private void TryFindPlayer()
        {
            if (playerTransform != null) return;
            var pc = FindFirstObjectByType<PlayerController>();
            if (pc != null)
            {
                playerTransform = pc.transform;
                Debug.Log($"[Spawner] 플레이어 자동 탐색 성공: {pc.gameObject.name}");
            }
            else
            {
                Debug.LogWarning("[Spawner] 플레이어를 찾지 못했습니다. Inspector에서 playerTransform을 직접 연결하거나 PlayerController가 씬에 있는지 확인하세요.");
            }
        }

        private void Update()
        {
            if (GameManager.Instance == null || GameManager.Instance.State != GameState.Playing)
                return;

            if (enemyPrefab == null)
            {
                Debug.LogWarning("[Spawner] enemyPrefab이 없습니다. Inspector에서 연결하세요.");
                return;
            }

            if (playerTransform == null)
            {
                TryFindPlayer();
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
            if (verboseLog)
                Debug.Log($"[Spawner] 적 스폰 @ {pos}");
        }
    }
}
