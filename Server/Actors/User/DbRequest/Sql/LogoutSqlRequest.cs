using Library.Db.Sql;
using Library.MessageQueue;
using MySqlConnector;

namespace Server.Actors.User.DbRequest.Sql;

// IsCritical=true — 로그아웃 시 유저 정보 저장, 실패 시 성공까지 재시도
public class LogoutSqlRequest : ISqlRequest
{
    public IMessageQueueReceiver? Session => null; // 세션 응답 불필요
    public bool IsCritical => true;

    private readonly ulong _sessionId;

    public LogoutSqlRequest(ulong sessionId)
    {
        _sessionId = sessionId;
    }

    public async Task ExecuteAsync(MySqlConnection connection)
    {
        // TODO: 실제 유저 정보 저장 쿼리 구현
        // await connection.ExecuteAsync(
        //     "UPDATE users SET last_logout = NOW() WHERE session_id = @SessionId",
        //     new { SessionId = _sessionId });
        await Task.CompletedTask;
    }
}
