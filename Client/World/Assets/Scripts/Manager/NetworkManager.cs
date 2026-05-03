using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Game.Network;
using Game.Network.Handler;
using Google.Protobuf;
using Messages;
using UnityEngine;

namespace Game.Manager
{
    public class NetworkManager : MonoBehaviour
    {
        public static NetworkManager Instance { get; private set; }

        [Header("Server")]
        public string serverHost = "127.0.0.1";
        public int serverPort = 9000;

        [Header("Account (테스트용)")]
        public string userId = "user_00001";
        public string password = "Test1234!";

        [Header("Debug")]
        [SerializeField] private bool verboseKeepAliveLog = true;
        [SerializeField] private bool periodicStatusLog = true;

        public event Action OnConnected;
        public event Action OnLoginSuccess;
        public event Action<int> OnLoginFailed;
        public event Action<bool> OnEnterWorldResult;
        public event Action<bool> OnGameOverResult;

        private TcpGameClient _client;
        private MessageDispatcher _dispatcher;
        private bool _isAuthenticated;
        private float _lastKeepAliveSentAt = float.NegativeInfinity;
        private float _lastStatusLogAt;
        private const float KeepAliveIntervalSeconds = 3f;
        private const float StatusLogIntervalSeconds = 1f;

        public bool IsConnected => _client != null && _client.IsConnected;
        public bool IsAuthenticated => _isAuthenticated;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            _client = new TcpGameClient();
            _dispatcher = new MessageDispatcher();

            _dispatcher.Register(MessageWrapper.PayloadOneofCase.ConnectedResponse, new ConnectedResponseHandler(this));
            _dispatcher.Register(MessageWrapper.PayloadOneofCase.LoginResponse, new LoginResponseHandler(this));
            _dispatcher.Register(MessageWrapper.PayloadOneofCase.EnterWorldResponse, new EnterWorldResponseHandler(this));
            _dispatcher.Register(MessageWrapper.PayloadOneofCase.GameOverResponse, new GameOverResponseHandler(this));
        }

        public async Task<bool> ConnectAsync()
        {
            Debug.Log($"[Net] ConnectAsync 시작 → {serverHost}:{serverPort}");
            var ok = await _client.ConnectAsync(serverHost, serverPort);
            Debug.Log($"[Net] ConnectAsync 결과: {ok}");
            if (ok)
            {
                _lastKeepAliveSentAt = Time.realtimeSinceStartup;
                OnConnected?.Invoke();
            }
            return ok;
        }

        public void Disconnect()
        {
            Debug.LogWarning($"[Net] Disconnect 호출 (auth={_isAuthenticated})");
            _client?.Disconnect();
            _isAuthenticated = false;
        }

        public void SendLogin()
        {
            byte[] hash;
            using (var sha = SHA256.Create())
                hash = sha.ComputeHash(Encoding.UTF8.GetBytes(password));

            _client.Send(new MessageWrapper
            {
                LoginRequest = new LoginRequest
                {
                    UserId = userId,
                    PasswordHash = ByteString.CopyFrom(hash)
                }
            });
        }

        public void SendEnterWorld(ulong worldId)
        {
            _client.Send(new MessageWrapper
            {
                EnterWorldRequest = new EnterWorldRequest { WorldId = worldId }
            });
        }

        public void SendGameOver(int score, int killCount, int surviveSeconds)
        {
            _client.Send(new MessageWrapper
            {
                GameOverReport = new GameOverReport
                {
                    Score = score,
                    KillCount = killCount,
                    SurviveSeconds = surviveSeconds
                }
            });
        }

        internal void RaiseLoginSuccess()
        {
            _isAuthenticated = true;
            _lastKeepAliveSentAt = Time.realtimeSinceStartup;
            Debug.Log($"[Net] 로그인 성공 → 인증 ON, KeepAlive 카운터 리셋");
            OnLoginSuccess?.Invoke();
        }

        internal void RaiseLoginFailed(int errorCode) => OnLoginFailed?.Invoke(errorCode);
        internal void RaiseEnterWorldResult(bool success) => OnEnterWorldResult?.Invoke(success);
        internal void RaiseGameOverResult(bool success) => OnGameOverResult?.Invoke(success);

        private void Update()
        {
            if (_client == null) return;

            while (_client.ReceiveQueue.TryDequeue(out var message))
                _dispatcher.Dispatch(message);

            var now = Time.realtimeSinceStartup;
            var connected = _client.IsConnected;

            // 1초마다 현재 상태 로그 (KeepAlive가 안 가는 원인 추적용)
            if (periodicStatusLog && now - _lastStatusLogAt >= StatusLogIntervalSeconds)
            {
                _lastStatusLogAt = now;
                Debug.Log($"[Net status] connected={connected} auth={_isAuthenticated} sinceLastSend={(now - _lastKeepAliveSentAt):F1}s");
            }

            // 연결되어 있으면 인증 여부와 무관하게 KeepAlive 주기 송신.
            // 서버는 인증된 세션에 한해 타임아웃을 검사하므로 송신해도 안전하다.
            if (connected)
            {
                if (now - _lastKeepAliveSentAt >= KeepAliveIntervalSeconds)
                {
                    _lastKeepAliveSentAt = now;
                    _client.Send(new MessageWrapper { KeepAliveRequest = new KeepAliveRequest() });
                    if (verboseKeepAliveLog)
                        Debug.Log($"[Net] KeepAlive 송신 (t={now:F1}, auth={_isAuthenticated})");
                }
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                _client?.Disconnect();
                Instance = null;
            }
        }
    }
}
