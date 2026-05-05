using Game.Manager;
using Messages;

namespace Game.Network.Handler
{
    public class MoveResponseHandler : IMessageHandler
    {
        private readonly NetworkManager _net;

        public MoveResponseHandler(NetworkManager net) { _net = net; }

        public void Handle(MessageWrapper message)
        {
            var resp = message.MoveResponse;
            _net.RaiseMoveResult(resp.X, resp.Y);
        }
    }
}
