using Dapper;
using Library.Db.Sql;
using Library.MessageQueue;
using Library.MessageQueue.Message;
using Messages;
using MySqlConnector;

namespace Server.Actors.User.DbRequest.Sql;

public class SaveScoreSqlRequest : ISqlRequest
{
    public IMessageQueueReceiver? Session { get; }
    public bool IsCritical => false;

    private readonly ulong _accountId;
    private readonly int _score;
    private readonly int _killCount;
    private readonly int _surviveSeconds;

    public SaveScoreSqlRequest(IMessageQueueReceiver session, ulong accountId, int score, int killCount, int surviveSeconds)
    {
        Session = session;
        _accountId = accountId;
        _score = score;
        _killCount = killCount;
        _surviveSeconds = surviveSeconds;
    }

    public async Task ExecuteAsync(MySqlConnection connection)
    {
        await connection.ExecuteAsync(
            "INSERT INTO scores (account_id, score, kill_count, survive_seconds) VALUES (@AccountId, @Score, @KillCount, @SurviveSeconds)",
            new
            {
                AccountId = _accountId,
                Score = _score,
                KillCount = _killCount,
                SurviveSeconds = _surviveSeconds
            });

        await Session!.EnqueueMessageAsync(new RemoteSendMessage
        {
            MessageWrapper = new MessageWrapper
            {
                GameOverResponse = new GameOverResponse { Success = true }
            }
        });
    }
}
