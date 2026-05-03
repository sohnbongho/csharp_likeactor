using Game.Manager;
using Messages;
using UnityEngine;

namespace Game.Network.Handler
{
    public class ConnectedResponseHandler : IMessageHandler
    {
        private readonly NetworkManager _net;

        public ConnectedResponseHandler(NetworkManager net) { _net = net; }

        public void Handle(MessageWrapper message)
        {
            Debug.Log("[Network] ConnectedResponse 수신 → LoginRequest 전송");
            _net.SendLogin();
        }
    }
}
