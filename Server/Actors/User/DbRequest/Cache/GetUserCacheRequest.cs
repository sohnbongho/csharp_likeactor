using Library.Db.Cache;
using Library.MessageQueue;
using StackExchange.Redis;

namespace Server.Actors.User.DbRequest.Cache;

// ICacheRequest 구현 패턴 예시 — 캐시 키 스키마 및 결과 InnerMessage 정의 후 완성
public class GetUserCacheRequest : ICacheRequest
{
    public IMessageQueueReceiver? Session { get; }

    private readonly string _userId;

    public GetUserCacheRequest(IMessageQueueReceiver session, string userId)
    {
        Session = session;
        _userId = userId;
    }

    public async Task ExecuteAsync(IDatabase redis)
    {
        // TODO: 실제 캐시 조회 구현 (cache-aside 패턴)
        // var cached = await redis.StringGetAsync($"user:{_userId}");
        // if (cached.HasValue)
        // {
        //     var userData = JsonSerializer.Deserialize<UserData>(cached!);
        //     await Session!.EnqueueMessageAsync(new InnerReceiveMessage { Message = new CacheHitResult { UserData = userData } });
        // }
        // else
        // {
        //     SqlWorkerManager.Enqueue(new LoginSqlRequest(Session!, _userId));
        // }
        await Task.CompletedTask;
    }
}
