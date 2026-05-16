namespace Library.Network;

public static class PacketStats
{
    private static long _totalReceived;
    private static long _totalSent;

    public static void IncrementReceived() => Interlocked.Increment(ref _totalReceived);
    public static void IncrementSent() => Interlocked.Increment(ref _totalSent);

    public static (long received, long sent) Snapshot()
        => (Interlocked.Read(ref _totalReceived), Interlocked.Read(ref _totalSent));
}
