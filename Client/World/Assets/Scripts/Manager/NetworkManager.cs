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

        public event Action OnConnected;
        public event Action OnLoginSuccess;
        public event Action<int> OnLoginFailed;
        public event Action<bool> OnEnterWorldResult;
        public event Action<bool> OnGameOverResult;

        private TcpGameClient _client;
        private MessageDispatcher _dispatcher;
        private bool _isAuthenticated;
        private float _lastKeepAliveSentAt;
        private const float KeepAliveIntervalSeconds = 3f;

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
            var ok = await _client.ConnectAsync(serverHost, serverPort);
            if (ok) OnConnected?.Invoke();
            return ok;
        }

        public void Disconnect()
        {
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

            if (_isAuthenticated && _client.IsConnected)
            {
                if (Time.realtimeSinceStartup - _lastKeepAliveSentAt >= KeepAliveIntervalSeconds)
                {
                    _lastKeepAliveSentAt = Time.realtimeSinceStartup;
                    _client.Send(new MessageWrapper { KeepAliveRequest = new KeepAliveRequest() });
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
