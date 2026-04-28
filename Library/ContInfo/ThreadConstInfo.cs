namespace Library.ContInfo;

public static class ThreadConstInfo
{
    // CPU 코어 수 기반으로 결정, 최소 4개 보장
    public static readonly int MaxWorldThreadCount = Math.Max(4, Environment.ProcessorCount);

    // 로비 체류 시간이 짧으므로 월드 스레드 수의 절반, 최소 2개
    public static readonly int MaxLobbyThreadCount = Math.Max(2, Environment.ProcessorCount / 2);

    public const int TickMillSecond = 100; // 초당 10프레임
}
