# 동접 1만 수용 가능성 분석

날짜: 2026-04-28

## 요약

세션 풀 크기는 정확히 10,000으로 설계되어 있고 I/O 모델(IOCP + SAEA)도 대규모 연결에 적합하다. 단, 시작 시 메모리 사용량이 크고, 레이트 리미터·수신 파서의 할당 패턴이 고부하 시 병목이 될 수 있어 **조건부 가능**이다.

---

## 대상

| 파일 | 역할 |
|------|------|
| `Library/ContInfo/SessionConstInfo.cs` | 세션 풀 크기, 버퍼, 큐 상수 |
| `Library/ContInfo/ThreadConstInfo.cs` | 스레드 수, 틱 주기 |
| `Library/ObjectPool/ObjectPool.cs` | 세션 사전 할당 풀 |
| `Library/Network/ReceiverHandler.cs` | SAEA 기반 수신 루프 |
| `Library/Network/SenderHandler.cs` | 비동기 송신 큐 |
| `Library/Network/ReceiveParser.cs` | 2-byte length prefix 파서 |
| `Library/Network/UserConnectionComponent.cs` | 소켓 옵션 설정 |
| `Library/MessageQueue/Message/RemoteReceiveMessage.cs` | 수신 envelope 풀 |
| `Library/MessageQueue/MessageQueueDispatcher.cs` | 핸들러 싱글톤 |
| `Library/Timer/TimerScheduleManager.cs` | 세션 타이머 |
| `Server/Acceptor/Acceptor.cs` | Accept 루프, 레이트 리미터 |
| `Server/Actors/UserObjectPoolManager.cs` | 세션 생명주기 |

---

## 구조 분석

### 세션 풀 — 정확히 1만 슬롯

```csharp
public const int MaxUserSessionPoolSize = 10000;
```

풀 고갈 시 신규 연결을 소켓 레벨에서 즉시 차단한다(`socket.Close()`).  
10,000번째 동접은 허용되고, 10,001번째는 거부된다. 상한은 명확하다.

### I/O 모델 — IOCP + SAEA (적합)

- 수신: 세션당 `SocketAsyncEventArgs` 1개 (`ReceiverHandler`)
- 송신: 세션당 `SocketAsyncEventArgs` 1개 (`SenderHandler`)
- Windows IOCP 기반이므로 10,000 동접에서 스레드를 대량 소비하지 않는다.
- `ReceiveAsync`/`SendAsync` 완료는 IOCP 완료 포트를 통해 .NET ThreadPool로 전달된다.

### 틱 스레드 처리량 — 수치 검증

8코어 기준 스레드 구성:

| 풀 | 워커 수 | 세션 할당(10,000명, 전원 로비 가정) |
|---|---|---|
| 로비(LobbyThread) | 4 | 2,500/워커 |
| 월드(WorldThread) | 8 | 분산 후 ~1,250/워커 |

한 틱(100ms) 동안 워커가 2,500개 세션을 순회하면:
- 메시지 없는 `TickAsync` ≈ 수µs → 2,500 × 2µs = **5ms** (여유 충분)
- 핸들러에 외부 I/O(DB, 파일)가 없는 한 틱 오버런 없음

---

## 메모리 추정

### 세션 1개당 고정 메모리

| 항목 | 크기 |
|------|------|
| `ReceiveParser._buffer` | 8,192 B |
| `SenderHandler._sendBuffer` | 8,192 B |
| `SocketAsyncEventArgs` × 2 | ~1 KB |
| `Channel<IMessageQueue>` (내부 구조체) | ~200 B |
| `TimerScheduleManager` (PriorityQueue) | ~500 B |
| 기타 필드, 헤더 | ~1 KB |
| **합계** | **≈ 19 KB** |

### 풀 사전 할당 시 전체 메모리

```
10,000 세션 × 19 KB ≈ 190 MB (버퍼만)
소켓 핸들 + OS 수준 버퍼(~4 KB/소켓): 40 MB
합계: ~230 MB + 런타임/힙 오버헤드
```

서버 프로세스가 **최소 512 MB, 권장 1 GB 이상의 RAM** 이 필요하다.

---

## 주요 발견

### 🔴 레이트 리미터가 부하 테스트를 차단할 수 있음

```csharp
public const int MaxConnectionsPerIpPerMinute = 20;
```

부하 테스트 도구가 단일 IP에서 10,000 연결을 보내면 20개 이후 전부 차단된다.  
운영 환경에서 유저가 분산된 IP를 갖는다면 문제없지만, 단일 IP 집중 시나리오(NAT 뒤 접속 포함)에서는 실제 동접도 막힐 수 있다.

### 🟡 `ReceiveParser.Parse`가 매 수신마다 `List<MessageWrapper>` 할당

```csharp
var messages = new List<MessageWrapper>(4); // 매 호출마다 new
```

10,000 세션이 초당 수십 회씩 수신하면 초당 수십만 건의 단기 객체가 GEN0 GC를 자주 유발한다.  
`ArrayPool<T>` 또는 `StackAllocMessageBuffer` 패턴으로 개선 가능.

### 🟡 `RemoteReceiveMessage` 풀 상한이 4,096으로 작음

```csharp
private const int MaxPoolSize = 4096;
```

10,000 세션이 동시에 메시지를 처리하면 풀 부족으로 매번 `new RemoteReceiveMessage()`가 발생한다.  
풀 크기를 `MaxUserSessionPoolSize` 이상(예: 20,000)으로 조정을 권장한다.

### 🟡 `Channel<IMessageQueue>`가 `UnboundedChannelOptions`

```csharp
Channel.CreateUnbounded<IMessageQueue>(new UnboundedChannelOptions { SingleReader = true, ... });
```

네트워크 수신 속도가 틱 처리 속도를 지속적으로 초과하면 채널이 무한 증가해 OOM 위험이 있다.  
각 세션의 채널 크기를 `CreateBounded`로 제한하거나, `MaxMessagesPerTick=50` 제한이 자연 배압 역할을 하는지 확인이 필요하다.

### 🔵 `MessageQueueDispatcher`가 싱글톤 — 10,000 세션 절약 확인됨

```csharp
// 세션 10,000개 × Dispatcher/Manager 3 객체 = 30,000 객체 할당을 제거.
public static MessageQueueDispatcher Instance { get; } = new();
```

의도적으로 메모리를 절약한 좋은 설계.

### 🔵 `Accept` 동시 처리 수 128 — 연결 폭주 시 분석 필요

```csharp
public const int MaxAcceptSessionCount = 128;
public const int MaxListenerBackLog = 512;
```

Backlog 512 + Accept 동시 처리 128: 짧은 시간 안에 수천 명이 동시 접속하는 이벤트(서버 오픈 등)에서는 backlog가 포화될 수 있다. 점진적 접속 분산이 필요하다.

---

## 종합 판단

| 항목 | 상태 | 비고 |
|------|------|------|
| 세션 풀 용량 | ✅ | 정확히 10,000 |
| I/O 모델(IOCP+SAEA) | ✅ | 대규모 연결에 적합 |
| 틱 처리 시간 여유 | ✅ | 핸들러가 가볍다면 문제없음 |
| 메모리(~230 MB+) | ⚠️ | 최소 1 GB RAM 필요 |
| 레이트 리미터 | ⚠️ | 집중 IP 시나리오에서 정상 유저 차단 가능 |
| Channel 배압 없음 | ⚠️ | 폭주 클라이언트가 메모리를 소진할 수 있음 |
| RemoteReceiveMessage 풀 | ⚠️ | 4,096은 10,000 동접 대비 작음 |
| ReceiveParser GC 압력 | ⚠️ | 고트래픽 시 GEN0 GC 빈도 높음 |

**결론**: 하드웨어(RAM 1 GB 이상, 8코어 이상)가 충분하고 유저 IP가 분산된 운영 환경이라면 **동접 1만은 수용 가능하다**.  
단, 서버 오픈 연결 폭주, 단일 IP 집중, 고트래픽 핸들러 사용 시에는 레이트 리미터·Channel 배압·GC 압력 세 가지를 반드시 해결해야 한다.
