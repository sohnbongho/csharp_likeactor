using Library.Model;

namespace Server.Model.Message;

public enum LoginErrorCode { Success = 0, InvalidCredentials = 1, Banned = 2, ServerError = 3, RateLimited = 4 }

public class LoginResultMessage : IInnerServerMessage
{
    public bool Success { get; init; }
    public LoginErrorCode ErrorCode { get; init; }
    public ulong AccountId { get; init; }
    public string UserId { get; init; } = string.Empty;
}
