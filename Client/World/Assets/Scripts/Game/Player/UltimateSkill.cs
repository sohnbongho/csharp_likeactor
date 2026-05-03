using System;
using Game.Enemy;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Player
{
    public class UltimateSkill : MonoBehaviour
    {
        [SerializeField] private float radius = 6f;
        [SerializeField] private float cooldown = 10f;
        [SerializeField] private int damage = 100;
        [SerializeField] private LayerMask enemyMask;

        private float _readyAt;

        public float Radius { get => radius; set => radius = value; }
        public float Cooldown { get => cooldown; set => cooldown = Mathf.Max(1f, value); }
        public int Damage { get => damage; set => damage = value; }
        public float RemainingCooldown => Mathf.Max(0f, _readyAt - Time.time);

        public event Action<Vector2> OnUltimateCast;

        private void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
            if (Time.time < _readyAt) return;

            _readyAt = Time.time + cooldown;
            Cast();
        }

        private void Cast()
        {
            var hits = Physics2D.OverlapCircleAll(transform.position, radius, enemyMask);
            foreach (var hit in hits)
            {
                if (hit.TryGetComponent(out EnemyController enemy))
                    enemy.TakeDamage(damage);
            }
            OnUltimateCast?.Invoke(transform.position);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.3f, 0.6f, 1f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
