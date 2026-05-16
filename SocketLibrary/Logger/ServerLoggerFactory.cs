using System.Runtime.CompilerServices;

namespace Library.Logger;

public static class ServerLoggerFactory
{
    public static IServerLogger CreateLogger([CallerFilePath] string filePath = "")
    {
        string className = Path.GetFileNameWithoutExtension(filePath);
        return new ServerLogger(className);
    }
}
