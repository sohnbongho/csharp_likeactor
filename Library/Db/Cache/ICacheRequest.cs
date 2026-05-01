using Library.MessageQueue;
using StackExchange.Redis;

namespace Library.Db.Cache;

public interface ICacheRequest
{
    IMessageQueueReceiver? Session { get; }
    Task ExecuteAsync(IDatabase redis);
}
