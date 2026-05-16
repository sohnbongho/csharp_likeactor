using Library.Db;
using Microsoft.Extensions.Configuration;

namespace GameServer;

public class Program
{
    static void Main(string[] args)
    {
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .Build();

        var port = int.Parse(configuration["Server:Port"] ?? "9001");

        var dbConfig = new DbConfig
        {
            MySqlConnectionString = configuration["Database:MySql:ConnectionString"] ?? "",
            RedisConnectionString = configuration["Database:Redis:ConnectionString"] ?? "",
            RedisBroadcastChannel = configuration["Database:Redis:BroadcastChannel"] ?? "server:notice",
            SqlWorkerCount        = int.Parse(configuration["DbWorker:SqlWorkerCount"]    ?? "8"),
            SqlChannelCapacity    = int.Parse(configuration["DbWorker:SqlChannelCapacity"] ?? "500"),
            CacheWorkerCount      = int.Parse(configuration["DbWorker:CacheWorkerCount"]  ?? "4"),
            CacheChannelCapacity  = int.Parse(configuration["DbWorker:CacheChannelCapacity"] ?? "1000"),
            RetryBaseDelayMs      = int.Parse(configuration["DbWorker:RetryBaseDelayMs"] ?? "500"),
            RetryMaxDelayMs       = int.Parse(configuration["DbWorker:RetryMaxDelayMs"]  ?? "30000"),
        };

        var authTokenPrefix = configuration["AuthToken:RedisKeyPrefix"] ?? "auth:token:";

        var server = new TcpGameServer(port, dbConfig, authTokenPrefix);
        server.Init();
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            server.Stop();
        };

        server.Start();
    }
}
