using Game.Manager;
using Messages;
using UnityEngine;

namespace Game.Network.Handler
{
    public class GameOverResponseHandler : IMessageHandler
    {
        private readonly NetworkManager _net;

        public GameOverResponseHandler(NetworkManager net) { _net = net; }

        public void Handle(MessageWrapper message)
        {
            var resp = message.GameOverResponse;
            Debug.Log($"[Network] GameOverResponse: success={resp.Success}");
            _net.RaiseGameOverResult(resp.Success);
        }
    }
}
