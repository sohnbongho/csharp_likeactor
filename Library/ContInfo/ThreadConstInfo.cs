namespace Library.ContInfo;

public static class ThreadConstInfo
{
    // CPU 코어 수 기반으로 결정, 최소 4개 보장
    public static readonly int MaxUserThreadCount = Math.Max(4, Environment.ProcessorCount);
    // 액터 모델 전제: 한 세션은 항상 같은 스레드/워커에서 실행되어야 함.
    // sessionId % Count 매핑이 두 풀에서 일치하도록 동일한 카운트 사용.
    public static readonly int MaxMessageQueueWorkerCount = MaxUserThreadCount;

    public const int TickMillSecond = 100;          // 초당 10프레임
}
