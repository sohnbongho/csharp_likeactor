using Library.ContInfo;

namespace Server;

public class Program
{
    static void Main(string[] args)
    {
        var server = new TcpServer(port: SessionConstInfo.ServerPort);
        server.Init();
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true; // 프로세스 강제 종료 막기
            server.Stop();   // 안전 종료 요청
        };


        server.Start();
    }
}
