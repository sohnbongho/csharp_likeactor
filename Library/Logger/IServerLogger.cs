namespace Library.Logger;

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
        Console.WriteLine($"{_className}[Debug]{message}");
    }
    public void Info(string message)
    {
        Console.WriteLine($"{_className}[Info]{message}");
    }

    public void Error(string message)
    {
        Console.WriteLine($"{_className}[Error]{message}");
    }
}
