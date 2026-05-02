using Dapper;
using Library.Db.Sql;
using Library.MessageQueue;
using Library.MessageQueue.Message;
using Library.Security;
using MySqlConnector;
using Server.Model.Message;

namespace Server.Actors.User.DbRequest.Sql;

public class LoginSqlRequest : ISqlRequest
{
    public IMessageQueueReceiver? Session { get; }
    public bool IsCritical => false;

    private readonly string _userId;
    private readonly byte[] _clientHashBytes;

    public LoginSqlRequest(IMessageQueueReceiver session, string userId, byte[] clientHashBytes)
    {
        Session = session;
        _userId = userId;
        _clientHashBytes = clientHashBytes;
    }

    private record AccountRow(ulong AccountId, string UserId, string PasswordHash, string Salt, byte Status);

    public async Task ExecuteAsync(MySqlConnection connection)
    {
        var row = await connection.QueryFirstOrDefaultAsync<AccountRow>(
            "SELECT account_id AS AccountId, user_id AS UserId, password_hash AS PasswordHash, salt AS Salt, status AS Status " +
            "FROM accounts WHERE user_id = @UserId",
            new { UserId = _userId });

        LoginResultMessage result;

        if (row == null || !PasswordHashHelper.Verify(_clientHashBytes, row.PasswordHash, row.Salt))
        {
            result = new LoginResultMessage { Success = false, ErrorCode = LoginErrorCode.InvalidCredentials };
        }
        else if (row.Status == 1)
        {
            result = new LoginResultMessage { Success = false, ErrorCode = LoginErrorCode.Banned };
        }
        else
        {
            await connection.ExecuteAsync(
                "UPDATE accounts SET last_login_at = UTC_TIMESTAMP() WHERE account_id = @AccountId",
                new { AccountId = row.AccountId });

            result = new LoginResultMessage
            {
                Success = true,
                ErrorCode = LoginErrorCode.Success,
                AccountId = row.AccountId,
                UserId = row.UserId
            };
        }

        await Session!.EnqueueMessageAsync(new InnerReceiveMessage { Message = result });
    }
}
