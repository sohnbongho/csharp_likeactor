using System;
using System.Collections.Generic;
using Messages;
using UnityEngine;

namespace Game.Network
{
    public interface IMessageHandler
    {
        void Handle(MessageWrapper message);
    }

    public class MessageDispatcher
    {
        private readonly Dictionary<MessageWrapper.PayloadOneofCase, IMessageHandler> _handlers
            = new Dictionary<MessageWrapper.PayloadOneofCase, IMessageHandler>();

        public void Register(MessageWrapper.PayloadOneofCase type, IMessageHandler handler)
        {
            _handlers[type] = handler;
        }

        public void Dispatch(MessageWrapper message)
        {
            if (_handlers.TryGetValue(message.PayloadCase, out var handler))
            {
                try { handler.Handle(message); }
                catch (Exception ex) { Debug.LogError($"[Dispatcher] {message.PayloadCase} 핸들러 예외: {ex}"); }
            }
            else
            {
                Debug.LogWarning($"[Dispatcher] 미등록 메시지: {message.PayloadCase}");
            }
        }
    }
}
