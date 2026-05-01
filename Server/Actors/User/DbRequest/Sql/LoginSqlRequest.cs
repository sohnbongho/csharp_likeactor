using Library.Db.Sql;
using Library.MessageQueue;
using MySqlConnector;

namespace Server.Actors.User.DbRequest.Sql;

// ISqlRequest 구현 패턴 예시 — LoginRequest proto 및 DB 스키마 정의 후 완성
public class LoginSqlRequest : ISqlRequest
{
    public IMessageQueueReceiver? Session { get; }
    public bool IsCritical => false;

    private readonly string _userId;

    public LoginSqlRequest(IMessageQueueReceiver session, string userId)
    {
        Session = session;
        _userId = userId;
    }

    public async Task ExecuteAsync(MySqlConnection connection)
    {
        // TODO: 실제 쿼리 구현
        // var user = await connection.QueryFirstOrDefaultAsync<UserData>(
        //     "SELECT * FROM users WHERE user_id = @UserId", new { UserId = _userId });
        // var result = new LoginSqlResult { UserData = user };
        // await Session!.EnqueueMessageAsync(new InnerReceiveMessage { Message = result });
        await Task.CompletedTask;
    }
}
