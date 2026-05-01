using Library.MessageQueue;
using MySqlConnector;

namespace Library.Db.Sql;

public interface ISqlRequest
{
    IMessageQueueReceiver? Session { get; }
    bool IsCritical { get; }
    Task ExecuteAsync(MySqlConnection connection);
}
