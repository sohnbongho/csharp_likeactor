using Game.Manager;
using Messages;
using UnityEngine;

namespace Game.Network.Handler
{
    public class EnterWorldResponseHandler : IMessageHandler
    {
        private readonly NetworkManager _net;

        public EnterWorldResponseHandler(NetworkManager net) { _net = net; }

        public void Handle(MessageWrapper message)
        {
            var resp = message.EnterWorldResponse;
            Debug.Log($"[Network] EnterWorldResponse: success={resp.Success}");
            _net.RaiseEnterWorldResult(resp.Success);
        }
    }
}
