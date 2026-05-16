using System.Threading.Channels;

namespace Library.Logger;

public interface IServerLogger
{
    void Debug(Func<string> message);
    void Info(Func<string> message);
    void Error(Func<string> message, Exception ex);
    void Warn(Func<string> message);
}

public class ServerLogger : IServerLogger
{
    // Console.WriteLine의 내부 락에 모든 스레드가 직렬화되는 병목을 제거하기 위해,
    // 단일 백그라운드 스레드가 채널을 드레인하며 출력한다.
    private static readonly Channel<string> _channel = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            AllowSynchronousContinuations = false,
        });

    private static readonly Task _writerTask = Task.Run(WriterLoopAsync);

    private static async Task WriterLoopAsync()
    {
        var reader = _channel.Reader;
        try
        {
            while (await reader.WaitToReadAsync())
            {
                while (reader.TryRead(out var line))
                    Console.WriteLine(line);
            }
        }
        catch
        {
            // 로거가 서버를 죽이지 않도록 무시
        }
    }

    private readonly string _className;

    public ServerLogger(string className)
    {
        _className = className;
    }

    public void Debug(Func<string> message)
        => _channel.Writer.TryWrite($"{_className}[Debug]{message()}");

    public void Info(Func<string> message)
        => _channel.Writer.TryWrite($"{_className}[Info]{message()}");

    public void Error(Func<string> message, Exception ex)
        => _channel.Writer.TryWrite($"{_className}[Error]{message()} {ex.Message}");

    public void Warn(Func<string> message)
        => _channel.Writer.TryWrite($"{_className}[Warn]{message()}");
}
