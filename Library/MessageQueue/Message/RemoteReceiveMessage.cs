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
    private const int MaxPoolSize = 4096;
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
        if (Volatile.Read(ref _poolCount) >= MaxPoolSize)
            return;

        msg.MessageWrapper = null!;
        _pool.Enqueue(msg);
        Interlocked.Increment(ref _poolCount);
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