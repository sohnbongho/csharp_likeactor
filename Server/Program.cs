using Library.ContInfo;

namespace Server;

public class Program
{
    static async Task Main(string[] args)
    {
        var server = new TcpServer(port: SessionConstInfo.ServerPort);
        server.Init();
        await server.StartAsync();
    }
}
