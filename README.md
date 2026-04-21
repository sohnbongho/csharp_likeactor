# c# MMO Server

- google protobuffer
- multi thread-pool를 이용한 분산 처리
- user pool 관리
- async/await, task을 이용한 유저별 메시지 큐
- attribute를 활용한 메시지 dispatcher (Open-Close규칙)
- 타이머 작업

---

## 프로젝트 구성

| 프로젝트 | 역할 |
|---------|------|
| `Library` | 서버/클라이언트 공통 프레임워크 (네트워킹, 스레딩, 메시지 큐 등) |
| `Server` | 실제 게임 서버 실행파일 |
| `DummyClient` | 통합 테스트용 클라이언트 |
| `Scripts/` | Protobuf `.proto` 파일 및 빌드 스크립트 |

---

## 핵심 아키텍처

### 액터 모델 기반 동시성

각 유저(`UserSession`)는 **독립된 메시지 큐**를 가진 액터입니다. 유저 상태에 대한 내부 락(lock)이 필요 없습니다.

유저는 `SessionId % 풀크기`로 항상 **동일한 스레드/워커에 고정**됩니다:

```
ThreadPoolManager (4개 스레드)
  → TickThreadWorker → 100ms마다 ITickable 유저들을 틱 처리

MessageQueueWorkerManager (8개 워커)
  → MessageQueueWorker → Channel<T>에서 메시지를 꺼내 핸들러 호출
```

### 메시지 처리 흐름

```
클라이언트 바이트
  → ReceiveParser (2바이트 길이헤더 + protobuf 본문 파싱)
  → MessageQueueWorker (Channel<T>에 enqueue)
  → MessageQueueDispatcher (핸들러 탐색)
  → [RemoteMessageHandlerAsyncAttribute] 핸들러 실행
  → SenderHandler (직렬화 → 소켓 전송)
  → 클라이언트
```

### 핸들러 등록 방식

리플렉션으로 **자동 탐색** — 별도 등록 코드 불필요:

```csharp
[RemoteMessageHandlerAsyncAttribute(MessageWrapper.PayloadOneofCase.KeepAliveRequest)]
public class KeepAliveRequestHandler : IRemoteMessageHandlerAsync
{
    public async Task HandleAsync(UserSession session, MessageWrapper wrapper) { ... }
}
```

### 유저 세션 풀링

`UserObjectPoolManager`가 `UserSession`을 **10,000개 미리 할당** (`ConcurrentQueue`). 접속 시 대여, 접속 종료 시 초기화 후 반환 → GC 압박 최소화.

---

## 빌드 및 실행

```bash
dotnet build Server/Server.sln                       # 전체 빌드
dotnet run --project Server/Server.csproj            # 서버 실행
dotnet run --project DummyClient/DummyClient.csproj  # 테스트 클라이언트
Scripts/build.bat                                    # proto 재생성 (Windows)
```

---

## Ubuntu 24.04 배포

### 1. .NET 8 런타임 설치

```bash
sudo apt-get update
sudo apt-get install -y dotnet-sdk-8.0
```

### 2. 파일 디스크립터 한도 상향

동시접속 수가 많을 경우 소켓 수가 기본 한도를 초과할 수 있다.

```bash
# /etc/security/limits.conf 에 추가
* soft nofile 65536
* hard nofile 65536
```

변경 후 재로그인하거나 `ulimit -n 65536` 으로 현재 세션에 즉시 적용.

### 3. 빌드 및 실행

```bash
dotnet build Server/Server.sln -c Release
dotnet run --project Server/Server.csproj -c Release
```

### 4. 단독 실행 바이너리 배포 (선택)

빌드 서버에서 .NET 없이 실행 가능한 단일 파일 생성:

```bash
dotnet publish Server/Server.csproj -c Release -r linux-x64 --self-contained -o ./publish
./publish/Server
```

### 5. systemd 서비스 등록 (선택)

```ini
# /etc/systemd/system/mmo-server.service
[Unit]
Description=C# MMO Server
After=network.target

[Service]
WorkingDirectory=/opt/mmo-server
ExecStart=/usr/bin/dotnet /opt/mmo-server/Server.dll
Restart=on-failure
RestartSec=5
LimitNOFILE=65536

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl daemon-reload
sudo systemctl enable mmo-server
sudo systemctl start mmo-server
sudo systemctl status mmo-server
```

---

## 주요 설정 파일

- `Library/ContInfo/SessionConstInfo.cs` — 포트(9000), 버퍼 크기, 풀 크기
- `Library/ContInfo/ThreadConstInfo.cs` — 스레드 수, 틱 간격(100ms)
