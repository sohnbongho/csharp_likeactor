using Library.ContInfo;
using Library.Model;
using Messages;
using System.Collections.Concurrent;

namespace Library.MessageQueue.Message;

public interface IMessageQueue
{

}

// 수신 envelope은 hot path에서 메시지마다 1회씩 생성되므로 간단한 정적 풀로 재사용.
public class RemoteReceiveMessage : IMessageQueue
{
    private static readonly ConcurrentQueue<RemoteReceiveMessage> _pool = new();
    private static readonly int MaxPoolSize = SessionConstInfo.MaxUserSessionPoolSize;
    private static int _poolCount;

    public MessageWrapper MessageWrapper { get; set; } = null!;

    public static RemoteReceiveMessage Rent(MessageWrapper wrapper)
    {
        if (_pool.TryDequeue(out var msg))
        {
            Interlocked.Decrement(ref _poolCount);
            msg.MessageWrapper = wrapper;
            return msg;
        }
        return new RemoteReceiveMessage { MessageWrapper = wrapper };
    }

    public static void Return(RemoteReceiveMessage msg)
    {
        // Increment-first: 초과 시 롤백해 정확한 상한 보장
        if (Interlocked.Increment(ref _poolCount) > MaxPoolSize)
        {
            Interlocked.Decrement(ref _poolCount);
            return;
        }

        msg.MessageWrapper = null!;
        _pool.Enqueue(msg);
    }
}

public class RemoteSendMessage : IMessageQueue
{
    public MessageWrapper MessageWrapper { get; set; } = null!;
}


public class InnerReceiveMessage : IMessageQueue
{
    public IInnerServerMessage Message { get; set; } = null!;

}