namespace Library.ContInfo;

public static class SessionConstInfo
{
    public const int MaxUserSessionPoolSize = 10000;
    public const int MaxBufferSize = 8192;
    public const int ServerPort = 9000;
    // 동시 수락 채널 수. Accept 완료 시 즉시 재게시되므로 backlog를 빠르게 소진하기 위해 충분히 크게 둔다.
    public const int MaxAcceptSessionCount = 128;

    public const int MaxListenerBackLog = 512; // 서버가 동시에 처리하지 못하는 연결 요청을 임시로 쌓아둘 수 있는 큐의 크기
    public const int MaxSendQueueSize = 200;    // 세션당 최대 송신 큐 크기 (초과 시 해당 세션 강제 종료)
    public const int MaxMessageBodySize = MaxBufferSize - 2; // 2바이트 길이 헤더를 제외한 메시지 최대 크기
    public const int MaxTimerPerSession = 100;              // 세션당 최대 타이머 수
    public const int FloodWindowSeconds = 10;              // 플러드 감지 윈도우 (초)
    public const int MaxConnectionsPerWindow = 30;         // 윈도우 내 최대 허용 연결 수 (초과 시 즉시 밴)
    public const int BanDurationMinutes = 10;              // 밴 지속 시간 (분)
    public const int MaxMessagesPerTick = 50;               // tick당 세션 하나에서 처리할 최대 메시지 수
    public const int MaxMessageChannelCapacity = 1000;      // 세션당 수신 채널 용량 (초과 시 세션 강제 종료)
    public const int KeepAliveIntervalSeconds = 3;          // 클라이언트 KeepAlive 전송 주기
    public const int KeepAliveTimeoutSeconds = 10;          // 서버 KeepAlive 타임아웃 (초과 시 세션 종료)
}
