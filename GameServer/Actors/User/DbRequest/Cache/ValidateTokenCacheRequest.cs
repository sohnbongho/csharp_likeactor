using GameServer.Model.Message;
using Library.Db.Cache;
using Library.MessageQueue;
using Library.MessageQueue.Message;
using StackExchange.Redis;

namespace GameServer.Actors.User.DbRequest.Cache;

public class ValidateTokenCacheRequest : ICacheRequest
{
    public IMessageQueueReceiver? Session { get; }

    private readonly string _token;
    private readonly string _keyPrefix;

    public ValidateTokenCacheRequest(IMessageQueueReceiver session, string token, string keyPrefix)
    {
        Session = session;
        _token = token;
        _keyPrefix = keyPrefix;
    }

    // Redis 6.2 미만은 GETDEL 미지원 → Lua 스크립트로 atomic GET+DEL 처리
    private static readonly LuaScript _getDelScript = LuaScript.Prepare(
        "local v = redis.call('GET', @key)\n" +
        "if v then redis.call('DEL', @key) end\n" +
        "return v");

    public async Task ExecuteAsync(IDatabase redis)
    {
        var key = $"{_keyPrefix}{_token}";
        var value = (RedisValue)await redis.ScriptEvaluateAsync(_getDelScript, new { key = (RedisKey)key });

        GameConnectResultMessage result;

        if (!value.HasValue)
        {
            result = new GameConnectResultMessage { Success = false, ErrorCode = GameConnectErrorCode.InvalidToken };
        }
        else
        {
            var parts = value.ToString().Split(':', 2);
            if (parts.Length != 2 || !ulong.TryParse(parts[0], out var accountId))
            {
                result = new GameConnectResultMessage { Success = false, ErrorCode = GameConnectErrorCode.InvalidToken };
            }
            else
            {
                result = new GameConnectResultMessage
                {
                    Success = true,
                    ErrorCode = GameConnectErrorCode.Success,
                    AccountId = accountId,
                    UserId = parts[1]
                };
            }
        }

        if (Session == null) return;
        await Session.EnqueueMessageAsync(new InnerReceiveMessage { Message = result });
    }
}
