namespace Server.AdminApi;

public sealed class AdminApiConfig
{
    public bool Enabled { get; init; } = true;
    public int HttpsPort { get; init; } = 9001;
    public string[] Admins { get; init; } = Array.Empty<string>();
    public int SessionKeyTtlMinutes { get; init; } = 60;
    public string RedisKeyPrefix { get; init; } = "admin:session:";
}
