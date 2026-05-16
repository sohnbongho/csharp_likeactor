using DummyClient.Session;
using Microsoft.Extensions.Configuration;

namespace DummyClient;

public class Program
{
    static async Task Main(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .Build();

        if (args.Contains("--seed"))
        {
            var connStr = configuration["Database:MySql:ConnectionString"] ?? "";
            var password = configuration["Seeder:Password"] ?? "Test1234!";
            var accountCount = int.Parse(configuration["Seeder:AccountCount"] ?? "10000");
            await AccountSeeder.SeedAsync(connStr, password, accountCount);
            return;
        }

        await Task.Delay(2000);

        var loginIp   = configuration["Client:LoginServer:Ip"] ?? "127.0.0.1";
        var loginPort = int.Parse(configuration["Client:LoginServer:Port"] ?? "9000");
        var gameIp    = configuration["Client:GameServer:Ip"] ?? "127.0.0.1";
        var gamePort  = int.Parse(configuration["Client:GameServer:Port"] ?? "9001");
        var maxClientCount = int.Parse(configuration["Client:MaxClientCount"] ?? "1000");

        UserSession.GameServerIp = gameIp;
        UserSession.GameServerPort = gamePort;

        var tcpDummyClient = new TcpDummyClient(loginIp, loginPort, maxClientCount);
        tcpDummyClient.Init();

        await tcpDummyClient.StartAsync();
    }
}
