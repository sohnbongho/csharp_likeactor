namespace Server.Logger;

public interface IServerLogger
{
    void Debug(string message);
    void Info(string message);
    void Error(string message);
}

public class ServerLogger : IServerLogger
{
    private readonly string _className;
    public ServerLogger(string className)
    {
        _className = className;
    }
    public void Debug(string message)
    {
        Console.WriteLine($"[Debug]{_className}:{message}");
    }
    public void Info(string message)
    {
        Console.WriteLine($"[Info]{_className}:{message}");
    }

    public void Error(string message)
    {
        Console.WriteLine($"[Error]{_className}:{message}");
    }
}
