using GameServer.Actors.User.DbRequest.Sql;
using Library.MessageQueue;
using Library.MessageQueue.Attributes.Remote;
using Messages;

namespace GameServer.Actors.User.Handler.Remote;

[RemoteMessageHandlerAsyncAttribute(MessageWrapper.PayloadOneofCase.GameOverReport)]
public class GameOverReportHandler : IRemoteMessageHandlerAsync
{
    public Task<bool> HandleAsync(IMessageQueueReceiver receiver, MessageWrapper message)
    {
        if (receiver is not UserSession session || session.AccountData == null)
            return Task.FromResult(true);

        var report = message.GameOverReport;

        session.EnqueueSqlRequest(new SaveScoreSqlRequest(
            session,
            session.AccountData.AccountId,
            report.Score,
            report.KillCount,
            report.SurviveSeconds));

        return Task.FromResult(true);
    }
}
