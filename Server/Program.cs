using Library.ContInfo;
using Library.Db;
using Microsoft.Extensions.Configuration;
using Server.AdminApi;

namespace Server;

public class Program
{
    static void Main(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .Build();

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

        var adminConfig = new AdminApiConfig
        {
            Enabled              = bool.Parse(configuration["AdminApi:Enabled"] ?? "true"),
            HttpsPort            = int.Parse(configuration["AdminApi:HttpsPort"] ?? "9001"),
            Admins               = configuration.GetSection("AdminApi:Admins").Get<string[]>() ?? Array.Empty<string>(),
            SessionKeyTtlMinutes = int.Parse(configuration["AdminApi:SessionKeyTtlMinutes"] ?? "60"),
            RedisKeyPrefix       = configuration["AdminApi:RedisKeyPrefix"] ?? "admin:session:",
        };

        var server = new TcpServer(port: SessionConstInfo.ServerPort, dbConfig, adminConfig);
        server.Init();
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            server.Stop();
        };

        server.Start();
    }
}
