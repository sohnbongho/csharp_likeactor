namespace Library.ContInfo;

public static class SessionConstInfo
{
    public const int MaxUserSessionPoolSize = 10000;
    public const int MaxBufferSize = 8192;   
    public const int ServerPort = 9000;
    public const int MaxAcceptSessionCount = 4;

    public const int MaxListenerBackLog = 512; // 서버가 동시에 처리하지 못하는 연결 요청을 임시로 쌓아둘 수 있는 큐의 크기


}
