using System.Security.Cryptography;
using Library.Db.Cache;
using StackExchange.Redis;

namespace Server.AdminApi;

public class SessionKeyStore
{
    private readonly IDatabase _redis;
    private readonly AdminApiConfig _config;

    public SessionKeyStore(CacheWorkerManager cacheManager, AdminApiConfig config)
    {
        _redis = cacheManager.GetDatabase();
        _config = config;
    }

    public async Task<string> IssueAsync(string userId)
    {
        var key = GenerateKey();
        await _redis.StringSetAsync(
            _config.RedisKeyPrefix + key,
            userId,
            TimeSpan.FromMinutes(_config.SessionKeyTtlMinutes));
        return key;
    }

    public async Task<string?> ValidateAsync(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        var redisKey = _config.RedisKeyPrefix + key;
        var userId = await _redis.StringGetAsync(redisKey);
        if (userId.IsNullOrEmpty) return null;

        await _redis.KeyExpireAsync(redisKey, TimeSpan.FromMinutes(_config.SessionKeyTtlMinutes));
        return userId.ToString();
    }

    public Task RevokeAsync(string key)
    {
        return _redis.KeyDeleteAsync(_config.RedisKeyPrefix + key);
    }

    private static string GenerateKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }
}
