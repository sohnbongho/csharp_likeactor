namespace DummyClient;

public class Program
{
    static async Task Main(string[] args)
    {
        await Task.Delay(7000);
        
        var tcpDummyClient = new TcpDummyClient();
        await tcpDummyClient.StartAsync();
    }
}
