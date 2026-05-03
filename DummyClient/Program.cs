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

        var serverIp        = configuration["Client:ServerIp"] ?? "127.0.0.1";
        var serverPort      = int.Parse(configuration["Client:ServerPort"] ?? "9000");
        var maxClientCount  = int.Parse(configuration["Client:MaxClientCount"] ?? "1000");

        var tcpDummyClient = new TcpDummyClient(serverIp, serverPort, maxClientCount);
        tcpDummyClient.Init();

        await tcpDummyClient.StartAsync();
    }
}
