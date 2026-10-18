using System.Net.Sockets;

namespace Library.Network;

// 소켓의 유일한 소유자. 소켓을 닫는 코드는 이 클래스에만 존재한다.
// Receiver/Sender는 이 객체를 참조만 하며, 오류 감지 시 세션에 통보만 하고 닫지 않는다.
public sealed class UserConnectionComponent : IDisposable
{
    public Socket Socket { get; }

    private int _closed; // 0: open, 1: closed

    public bool IsClosed => Volatile.Read(ref _closed) != 0;

    public UserConnectionComponent(Socket socket)
    {
        Socket = socket;
        Socket.NoDelay = true;

        // 30초 무활동 시 OS가 keepalive 프로브 전송, 3회 실패 시 연결 강제 종료
        Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        Socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 30);
        Socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 5);
        Socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 3);
    }

    // 멱등: 여러 스레드에서 여러 번 호출돼도 실제 종료는 1회만 수행된다.
    // 소켓 참조를 null로 만들지 않으므로 호출측에 캡처/null 방어 코드가 필요 없다.
    public void Close()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
            return;

        try { Socket.Shutdown(SocketShutdown.Both); }
        catch (SocketException) { }
        catch (ObjectDisposedException) { }

        Socket.Close();
    }

    // 강제 종료(abortive close): FIN 대신 RST를 보내 로컬 포트가 TIME_WAIT에 묶이지 않게 한다.
    // 짧은 수명의 연결을 대량으로 열고 닫는 클라이언트(부하 테스트 등)에서 임시 포트 고갈을 막는 용도.
    // 서버의 일반 종료에는 Close()를 사용한다.
    public void Abort()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
            return;

        try { Socket.LingerState = new LingerOption(true, 0); }
        catch (SocketException) { }
        catch (ObjectDisposedException) { }

        Socket.Close();
    }

    public void Dispose() => Close();
}
