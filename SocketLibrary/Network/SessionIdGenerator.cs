namespace Library.Network;

public static class SessionIdGenerator
{
    private static ulong _nextId = 0;

    public static ulong Generate()
    {
        return Interlocked.Increment(ref _nextId);
    }
}

