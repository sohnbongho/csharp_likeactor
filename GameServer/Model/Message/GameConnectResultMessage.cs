using Library.Model;

namespace GameServer.Model.Message;

public enum GameConnectErrorCode { Success = 0, InvalidToken = 1, ServerError = 2 }

public class GameConnectResultMessage : IInnerServerMessage
{
    public bool Success { get; init; }
    public GameConnectErrorCode ErrorCode { get; init; } = GameConnectErrorCode.InvalidToken;
    public ulong AccountId { get; init; }
    public string UserId { get; init; } = string.Empty;
}
