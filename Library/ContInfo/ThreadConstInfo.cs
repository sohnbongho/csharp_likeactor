namespace Library.ContInfo;

public static class ThreadConstInfo
{
    // CPU 코어 수 기반으로 결정, 최소 4개 보장
    public static readonly int MaxUserThreadCount = Math.Max(4, Environment.ProcessorCount);
    public static readonly int MaxMessageQueueWorkerCount = Math.Max(8, Environment.ProcessorCount * 2);

    public const int TickMillSecond = 100;          // 초당 10프레임
    public const int MessageQueueThreadDelay = 10;  // 메시지 큐 CPU 보호용
}
