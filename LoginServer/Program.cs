using Library.Db;
using LoginServer.AdminApi;
using Microsoft.Extensions.Configuration;

namespace LoginServer;

public class Program
{
    static void Main(string[] args)
    {
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .Build();

        var port = int.Parse(configuration["Server:Port"] ?? "9000");

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
            HttpsPort            = int.Parse(configuration["AdminApi:HttpsPort"] ?? "9010"),
            Admins               = configuration.GetSection("AdminApi:Admins").Get<string[]>() ?? Array.Empty<string>(),
            SessionKeyTtlMinutes = int.Parse(configuration["AdminApi:SessionKeyTtlMinutes"] ?? "60"),
            RedisKeyPrefix       = configuration["AdminApi:RedisKeyPrefix"] ?? "admin:session:",
        };

        var authTokenPrefix = configuration["AuthToken:RedisKeyPrefix"] ?? "auth:token:";
        var authTokenTtlSeconds = int.Parse(configuration["AuthToken:TtlSeconds"] ?? "30");

        var server = new TcpLoginServer(port, dbConfig, adminConfig, authTokenPrefix, authTokenTtlSeconds);
        server.Init();
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            server.Stop();
        };

        server.Start();
    }
}
