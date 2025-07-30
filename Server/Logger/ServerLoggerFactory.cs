using System.Runtime.CompilerServices;

namespace Server.Logger;

public static class ServerLoggerFactory
{
    public static IServerLogger CreateLogger([CallerFilePath] string filePath = "")
    {
        string className = Path.GetFileNameWithoutExtension(filePath); // "UserSession"

        return new ServerLogger(className);
    }
}
