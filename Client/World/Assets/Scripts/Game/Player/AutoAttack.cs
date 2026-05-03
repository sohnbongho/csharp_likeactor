using Game.Enemy;
using UnityEngine;

namespace Game.Player
{
    public class AutoAttack : MonoBehaviour
    {
        [SerializeField] private float range = 3f;
        [SerializeField] private float cooldown = 1f;
        [SerializeField] private int damage = 10;
        [SerializeField] private LayerMask enemyMask;

        private float _timer;

        public float Range { get => range; set => range = value; }
        public float Cooldown { get => cooldown; set => cooldown = Mathf.Max(0.1f, value); }
        public int Damage { get => damage; set => damage = value; }

        private void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;

            _timer = cooldown;
            DoAttack();
        }

        private void DoAttack()
        {
            var hits = Physics2D.OverlapCircleAll(transform.position, range, enemyMask);
            foreach (var hit in hits)
            {
                if (hit.TryGetComponent(out EnemyController enemy))
                    enemy.TakeDamage(damage);
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.5f, 0.5f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, range);
        }
    }
}
