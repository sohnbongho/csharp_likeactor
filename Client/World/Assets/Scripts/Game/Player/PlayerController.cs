using Game.Manager;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Player
{
    [RequireComponent(typeof(Rigidbody2D))]
    public class PlayerController : MonoBehaviour
    {
        [SerializeField] private float moveSpeed = 5f;
        [SerializeField] private float networkSendInterval = 0.1f; // 10Hz

        private Rigidbody2D _rb;
        private Vector2 _input;
        private Vector2 _prevInput;
        private NetworkManager _net;
        private float _sendTimer;

        public float MoveSpeed { get => moveSpeed; set => moveSpeed = value; }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _rb.gravityScale = 0f;
            _rb.freezeRotation = true;
        }

        private void Start()
        {
            _net = NetworkManager.Instance;
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) { _input = Vector2.zero; return; }

            float h = 0f, v = 0f;
            if (kb.aKey.isPressed) h -= 1f;
            if (kb.dKey.isPressed) h += 1f;
            if (kb.sKey.isPressed) v -= 1f;
            if (kb.wKey.isPressed) v += 1f;
            _input = new Vector2(h, v).normalized;
        }

        private void FixedUpdate()
        {
            _rb.linearVelocity = _input * moveSpeed;

            if (_net == null || !_net.IsAuthenticated)
                return;

            bool hasInput = _input != Vector2.zero;
            bool justStopped = !hasInput && _prevInput != Vector2.zero;

            _sendTimer += Time.fixedDeltaTime;
            if (justStopped || (hasInput && _sendTimer >= networkSendInterval))
            {
                _sendTimer = 0f;
                var pos = (Vector2)transform.position;
                _net.SendMove(pos.x, pos.y);
            }

            _prevInput = _input;
        }
    }
}
