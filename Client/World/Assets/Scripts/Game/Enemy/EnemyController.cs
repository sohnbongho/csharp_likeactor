using Game.Manager;
using Game.Player;
using Game.System;
using UnityEngine;

namespace Game.Enemy
{
    [RequireComponent(typeof(Rigidbody2D))]
    public class EnemyController : MonoBehaviour
    {
        [SerializeField] private int maxHp = 20;
        [SerializeField] private float moveSpeed = 2f;
        [SerializeField] private int contactDamage = 5;
        [SerializeField] private float contactInterval = 0.5f;
        [SerializeField] private int xpDrop = 1;
        [SerializeField] private GameObject xpOrbPrefab;

        private int _hp;
        private Rigidbody2D _rb;
        private Transform _player;
        private float _lastContactAt;

        public float MoveSpeed { get => moveSpeed; set => moveSpeed = value; }
        public int MaxHp { get => maxHp; set => maxHp = value; }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _rb.gravityScale = 0f;
            _rb.freezeRotation = true;
            _hp = maxHp;
        }

        public void Init(Transform playerTransform)
        {
            _player = playerTransform;
            _hp = maxHp;
        }

        private void FixedUpdate()
        {
            if (_player == null) return;
            var dir = ((Vector2)(_player.position - transform.position)).normalized;
            _rb.linearVelocity = dir * moveSpeed;
        }

        public void TakeDamage(int amount)
        {
            _hp -= amount;
            if (_hp <= 0) Die();
        }

        private void Die()
        {
            GameManager.Instance?.AddKill();
            if (xpOrbPrefab != null)
            {
                var orb = Instantiate(xpOrbPrefab, transform.position, Quaternion.identity);
                if (orb.TryGetComponent(out XpOrb xpOrb))
                    xpOrb.SetValue(xpDrop);
            }
            Destroy(gameObject);
        }

        private void OnCollisionStay2D(Collision2D collision)
        {
            if (Time.time - _lastContactAt < contactInterval) return;
            if (collision.collider.TryGetComponent(out PlayerStats stats))
            {
                _lastContactAt = Time.time;
                stats.TakeDamage(contactDamage);
            }
        }
    }
}
