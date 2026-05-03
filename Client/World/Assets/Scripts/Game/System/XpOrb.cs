using Game.Player;
using UnityEngine;

namespace Game.System
{
    public class XpOrb : MonoBehaviour
    {
        [SerializeField] private float pickupRadius = 0.5f;
        [SerializeField] private float magnetRadius = 2.5f;
        [SerializeField] private float magnetSpeed = 8f;

        private int _value = 1;
        private Transform _player;

        public void SetValue(int value) => _value = value;

        private void Start()
        {
            var p = GameObject.FindWithTag("Player");
            if (p != null) _player = p.transform;
        }

        private void Update()
        {
            if (_player == null) return;
            var dist = Vector2.Distance(_player.position, transform.position);

            if (dist <= pickupRadius)
            {
                if (_player.TryGetComponent(out PlayerStats stats))
                    stats.AddXp(_value);
                Destroy(gameObject);
                return;
            }

            if (dist <= magnetRadius)
            {
                transform.position = Vector2.MoveTowards(
                    transform.position, _player.position, magnetSpeed * Time.deltaTime);
            }
        }
    }
}
