namespace Library.ContInfo;

public static class ThreadConstInfo
{
    // CPU 코어 수 기반으로 결정, 최소 4개 보장
    public static readonly int MaxWorldThreadCount = Math.Max(4, Environment.ProcessorCount);

    public const int TickMillSecond = 100; // 초당 10프레임
}
