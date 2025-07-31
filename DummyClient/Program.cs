namespace DummyClient;

public class Program
{
    static async Task Main(string[] args)
    {
        await Task.Delay(1000);
        
        var tcpDummyClient = new TcpDummyClient();
        await tcpDummyClient.StartAsync();
    }
}
