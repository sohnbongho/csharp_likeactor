namespace DummyClient;

public class Program
{
    static async Task Main(string[] args)
    {
        if (args.Contains("--seed"))
        {
            await AccountSeeder.SeedAsync();
            return;
        }

        await Task.Delay(2000);

        var tcpDummyClient = new TcpDummyClient();
        tcpDummyClient.Init();

        await tcpDummyClient.StartAsync();
    }
}
