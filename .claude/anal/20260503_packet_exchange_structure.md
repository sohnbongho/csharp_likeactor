# 클라이언트-서버 패킷 송수신 구조 분석

날짜: 2026-05-03

## 요약

2바이트 길이 헤더 + Protobuf 바디 프레이밍을 사용하며, 수신된 패킷은 소켓 콜백 → Channel 큐 → Tick 루프 → 핸들러 순으로 흐른다. 클라이언트(DummyClient)와 서버(Server)가 동일한 Library의 ReceiverHandler/SenderHandler/ReceiveParser를 공유한다.

## 대상

- `Scripts/message.proto` — 메시지 스키마
- `Library/Network/ReceiveParser.cs` — 바이트 스트림 → MessageWrapper 파싱
- `Library/Network/ReceiverHandler.cs` — 소켓 비동기 수신 루프
- `Library/Network/SenderHandler.cs` — 직렬화 + 비동기 송신 큐
- `Library/MessageQueue/MessageQueueDispatcher.cs` — 메시지 타입별 핸들러 라우팅
- `Library/MessageQueue/Attributes/Remote/RemoteMessageHandlerManager.cs` — 핸들러 리플렉션 등록
- `Library/MessageQueue/Message/RemoteReceiveMessage.cs` — 수신 envelope 풀
- `Library/Worker/TickThreadWorker.cs` — 100ms 주기 Tick 루프
- `Server/Actors/User/UserSession.cs` — 서버 세션 (채널 소유)
- `DummyClient/Session/UserSession.cs` — 클라이언트 세션 (동일 구조)
- `Server/Actors/User/Handler/Remote/` — 서버 핸들러 3종
- `DummyClient/Session/Handler/Remote/` — 클라이언트 핸들러 3종

---

## 프로토콜 프레이밍

```
┌──────────────┬──────────────────────────────┐
│  Header(2B)  │        Body (N bytes)        │
│  ushort LE   │  Protobuf MessageWrapper     │
└──────────────┴──────────────────────────────┘
```

- **헤더**: 2바이트 Little-Endian ushort — 바디 바이트 수
- **바디**: `MessageWrapper` (oneof payload 포함)
- `MessageWrapper.message_size` 필드는 proto 정의에만 있고, 실제 전송 헤더와 별개임 (파싱 시 헤더의 ushort 사용)

### 정의된 메시지 타입 (message.proto)

| 방향 | 메시지 | 용도 |
|------|--------|------|
| S→C | ConnectedResponse | 접속 완료 알림 |
| C→S | LoginRequest | 로그인 (userId + SHA256 해시) |
| S→C | LoginResponse | 로그인 결과 (success/errorCode) |
| C→S | KeepAliveRequest | 연결 유지 ping |
| S→C | KeepAliveNoti | 연결 유지 응답 |
| C→S | EnterWorldRequest | 월드 입장 요청 |
| S→C | EnterWorldResponse | 월드 입장 결과 |
| S→C | ErrorResponse | 서버 오류 응답 |

---

## 흐름 분석

### 수신 경로 (바이트 → 핸들러)

```
소켓 바이트 도착
  └─ ReceiverHandler.OnReceiveCompleted() [IOCP 콜백]
       └─ ReceiveParser.Parse(bytesTransferred)
            └─ List<MessageWrapper> (한 번에 여러 메시지 가능)
                 └─ foreach msg:
                      RemoteReceiveMessage.Rent(msg)  ← 풀에서 envelope 재사용
                      receiver.EnqueueMessageAsync(envelope)
                           └─ Channel<IMessageQueue>.Writer.TryWrite()

  [100ms마다 TickThreadWorker]
       └─ session.TickAsync()
            └─ while Channel.Reader.TryRead() (최대 MaxMessagesPerTick)
                 └─ MessageQueueDispatcher.OnRecvMessageAsync()
                      └─ RemoteMessageHandlerManager.OnRecvMessageAsync()
                           └─ handler.HandleAsync(receiver, messageWrapper)
```

### 송신 경로 (핸들러 → 소켓)

```
handler.HandleAsync() 내부:
  receiver.Send(MessageWrapper)
       └─ SenderHandler.Send()
            └─ _pendingSendQueue.Enqueue(message)
            └─ CAS(_isSending 0→1) 성공 시 ProcessSendQueue() 진입

  ProcessSendQueue():
       └─ _pendingSendQueue.TryDequeue()
       └─ TrySerializeToBuffer()
            ├─ _sendBuffer[0..1] = bodyLength (ushort LE)
            └─ message.WriteTo(_sendBuffer[2..])
       └─ socket.SendAsync(_sendEventArgs)
       └─ OnSendCompleted() → 다음 메시지 ProcessSendQueue() 재진입
```

### 세션 연결 ~ 종료 전체 시퀀스

```
[접속]
  Client.ConnectAsync()
       └─ socket 연결 후 ReceiverHandler.StartReceive() 시작

[서버 → 클라이언트: ConnectedResponse]
  Acceptor.AcceptUser()
       └─ session.Bind(socket) → ReceiverHandler, SenderHandler 초기화
       └─ session.Send(ConnectedResponse)

[클라이언트 → 서버: LoginRequest]
  ConnectedResponseHandler.HandleAsync()
       └─ session.Send(LoginRequest { UserId, PasswordHash })

[서버 → 클라이언트: LoginResponse (DB 경유)]
  LoginRequestHandler
       └─ session.EnqueueSqlRequest(LoginSqlRequest)
            └─ SqlWorkerManager → SqlWorker → MySQL 쿼리
            └─ LoginSqlRequest.ExecuteAsync()
                 └─ session.EnqueueMessageAsync(InnerReceiveMessage { LoginResultMessage })
  LoginResultHandler (Inner 핸들러)
       └─ session.Send(LoginResponse { success=true })

[KeepAlive 핑퐁 루프]
  클라이언트 LoginResponseHandler
       └─ success 시 → Send(KeepAliveRequest)
  서버 KeepAliveRequestHandler
       └─ receiver.Send(KeepAliveNoti)
  클라이언트 KeepAliveNotiHandler
       └─ receiver.Send(KeepAliveRequest)
  → 이후 무한 반복

[접속 해제]
  ReceiverHandler: BytesTransferred=0 또는 SocketError
       └─ Disconnected() → session.Dispose()
  UserSession.Dispose()
       └─ LogoutSqlRequest 큐잉 (IsCritical=true, 재시도 보장)
       └─ UserObjectPool 반환
```

---

## 구조 분석

### ReceiveParser — 상태 머신 기반 조각 조립

`Header → Body → Header` 두 상태만 존재. 소켓 수신은 TCP 특성상 메시지 경계가 없으므로:
- 헤더가 부분 도착 → 다음 수신까지 `_remainedOffset`에 보존
- 바디가 부분 도착 → `Buffer.BlockCopy`로 앞으로 당겨 이어 받기
- 한 번의 수신에 여러 메시지 포함 가능 → 최대 10,000개까지 루프 처리

### RemoteReceiveMessage — 수신 envelope 풀

핫 패스(수신 콜백)에서 매 메시지마다 생성되는 envelope을 `ConcurrentQueue` 기반 정적 풀로 재사용. 최대 크기는 `MaxUserSessionPoolSize`(10,000)로 제한하여 OOM 방지.

### MessageQueueDispatcher — 세션 전역 싱글톤

핸들러 맵이 모두 static이므로 Dispatcher 자체도 싱글톤. 세션 10,000개 × Dispatcher/Manager 3개 = **30,000 객체 절약**.

### RemoteMessageHandlerManager — 리플렉션 기반 핸들러 등록

서버 기동 시 1회만 `AppDomain.CurrentDomain.GetAssemblies()` 스캔, `[RemoteMessageHandlerAsyncAttribute]`가 붙은 클래스를 `PayloadCase → handler` 딕셔너리에 등록. 이후는 O(1) 딕셔너리 룩업.

### SenderHandler — Lock-Free 송신 큐

`_isSending` 플래그를 `Interlocked.CompareExchange`로 관리:
- 이미 sending 중이면 큐에만 넣고 반환 (중복 진입 방지)
- Lost Wakeup 방지: sending 플래그 내린 뒤 큐 재확인

---

## 동시성 / 스레드 안전성

| 구간 | 동시성 처리 방식 |
|------|----------------|
| 소켓 수신 콜백 → Channel | `SocketAsyncEventArgs` IOCP 콜백 1개 → TryWrite (Channel 내부 lock) |
| Channel → TickAsync | `SingleReader=true` 선언으로 Channel 내부 최적화 경로 사용 |
| TickAsync | `TickThreadWorker` 1개 스레드가 세션 전담 → 세션 내부 무락 |
| 송신 큐 | `ConcurrentQueue` + CAS 플래그로 lock-free |
| 핸들러 등록 | double-checked lock + `volatile bool _initialized` |
| 세션 Dispose | `Interlocked.CompareExchange(_disposedFlag)` 1회 보장 |

---

## 주요 발견

- **클라이언트/서버 코드 공유**: `ReceiverHandler`, `SenderHandler`, `ReceiveParser`, `MessageQueueDispatcher`가 Library에 있어 DummyClient와 Server가 동일한 송수신 스택을 사용. 구조 검증이 쉬운 설계.

- **KeepAlive는 서버 주도 ping-pong**: 서버가 KeepAliveNoti를 보내면 클라이언트가 KeepAliveRequest로 응답하는 구조. 클라이언트가 먼저 보내는 것이 아님.

- **로그인 결과는 Inner 메시지 경유**: DB 워커가 직접 소켓에 쓰지 않고 `InnerReceiveMessage`로 세션 채널에 넣어 Tick 루프에서 처리. DB 스레드와 세션 스레드 경계가 명확히 분리됨.

- **미인증 세션 격리**: `IsBlockedBeforeAuth()`에서 LoginRequest/KeepAliveRequest 외 모든 메시지를 차단 + 즉시 Disconnect. 인증 전 임의 메시지 전송 불가.

- **로그인 횟수 제한**: 세션당 분당 5회 (`TryConsumeLoginAttempt`), 슬라이딩 윈도우 방식.

---

## 관련 파일

| 파일 | 역할 |
|------|------|
| `Scripts/message.proto` | 전체 메시지 스키마 정의 |
| `Library/Network/ReceiveParser.cs` | 상태 머신 기반 바이트→MessageWrapper 파싱 |
| `Library/Network/ReceiverHandler.cs` | IOCP 비동기 수신 루프 |
| `Library/Network/SenderHandler.cs` | lock-free 비동기 송신 큐 |
| `Library/MessageQueue/MessageQueueDispatcher.cs` | 메시지 타입별 핸들러 라우팅 (싱글톤) |
| `Library/MessageQueue/Message/RemoteReceiveMessage.cs` | 수신 envelope 오브젝트 풀 |
| `Library/Worker/TickThreadWorker.cs` | 100ms 주기 세션 Tick 드라이버 |
| `Server/Actors/User/UserSession.cs` | 서버 세션, Channel 소유, ITickable 구현 |
| `DummyClient/Session/UserSession.cs` | 클라이언트 세션 (동일 패턴) |
| `Server/Actors/User/Handler/Remote/LoginRequestHandler.cs` | 로그인 요청 → DB 큐잉 |
| `Server/Actors/User/Handler/Remote/KeepAliveRequestHandler.cs` | KeepAlive 요청 → Noti 응답 |
| `DummyClient/Session/Handler/Remote/ConnectedResponseHandler.cs` | 접속 확인 → LoginRequest 전송 |
| `DummyClient/Session/Handler/Remote/LoginResponseHandler.cs` | 로그인 성공 → KeepAlive 시작 |
| `DummyClient/Session/Handler/Remote/KeepAliveNotiHandler.cs` | Noti 수신 → Request 응답 |
