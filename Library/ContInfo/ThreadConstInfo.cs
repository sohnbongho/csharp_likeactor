namespace Library.ContInfo;

public static class ThreadConstInfo
{
    public const int MaxUserThreadCount = 4;
    
    public const int TickMillSecond = 100; // 초당 10프레임
    public const int MessageQueueThreadDelay = 10; // 메시지 큐 CPU 보호용

    public const int MaxMessageQueueWorkerCount = 8; // 
}
