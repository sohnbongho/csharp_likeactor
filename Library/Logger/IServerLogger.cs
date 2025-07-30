namespace Library.Logger;

public interface IServerLogger
{
    void Debug(Func<string> message);
    void Info(Func<string> message);
    void Error(Func<string> message, Exception ex);
}

public class ServerLogger : IServerLogger
{
    private readonly string _className;
    public ServerLogger(string className)
    {
        _className = className;
    }
    public void Debug(Func<string> message)
    {
        Console.WriteLine($"{_className}[Debug]{message}");
    }
    public void Info(Func<string> message)
    {
        Console.WriteLine($"{_className}[Info]{message}");
    }

    public void Error(Func<string> message, Exception ex)
    {
        Console.WriteLine($"{_className}[Error]{message}{ex.Message}");
    }
}
