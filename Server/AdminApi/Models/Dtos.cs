namespace Server.AdminApi.Models;

public class LoginRequestDto
{
    public string UserId { get; set; } = "";
    public string Password { get; set; } = "";
}

public class LoginResponseDto
{
    public string SessionKey { get; set; } = "";
    public int ExpiresInMinutes { get; set; }
}

public class SessionDto
{
    public ulong SessionId { get; set; }
    public string? UserId { get; set; }
    public ulong WorldId { get; set; }
    public bool IsAuthenticated { get; set; }
}

public class ScoreDto
{
    public ulong ScoreId { get; set; }
    public ulong AccountId { get; set; }
    public uint Score { get; set; }
    public uint KillCount { get; set; }
    public uint SurviveSeconds { get; set; }
    public DateTime PlayedAt { get; set; }
}

public class NoticeRequestDto
{
    public string Message { get; set; } = "";
}

public class HealthDto
{
    public string Status { get; set; } = "ok";
    public string Db { get; set; } = "unknown";
    public string Redis { get; set; } = "unknown";
}
