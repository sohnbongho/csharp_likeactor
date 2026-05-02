using Dapper;
using Library.Db.Sql;
using Library.MessageQueue;
using MySqlConnector;

namespace Server.Actors.User.DbRequest.Sql;

public class LogoutSqlRequest : ISqlRequest
{
    public IMessageQueueReceiver? Session => null;
    public bool IsCritical => true;

    private readonly ulong _accountId;

    public LogoutSqlRequest(ulong accountId)
    {
        _accountId = accountId;
    }

    public async Task ExecuteAsync(MySqlConnection connection)
    {
        await connection.ExecuteAsync(
            "UPDATE accounts SET last_login_at = UTC_TIMESTAMP() WHERE account_id = @AccountId",
            new { AccountId = _accountId });
    }
}
