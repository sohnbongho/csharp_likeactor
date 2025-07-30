namespace Server;

public class Program
{
    static async Task Main(string[] args)
    {
        var server = new TcpServer(port: 9000);
        await server.StartAsync();
    }
}
