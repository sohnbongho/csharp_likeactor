using Game.Manager;
using Messages;
using UnityEngine;

namespace Game.Network.Handler
{
    public class LoginResponseHandler : IMessageHandler
    {
        private readonly NetworkManager _net;

        public LoginResponseHandler(NetworkManager net) { _net = net; }

        public void Handle(MessageWrapper message)
        {
            var resp = message.LoginResponse;
            if (resp.Success)
            {
                Debug.Log("[Network] 로그인 성공");
                _net.RaiseLoginSuccess();
            }
            else
            {
                Debug.LogWarning($"[Network] 로그인 실패: errorCode={resp.ErrorCode}");
                _net.RaiseLoginFailed(resp.ErrorCode);
            }
        }
    }
}
