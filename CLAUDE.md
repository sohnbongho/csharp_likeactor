# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Git 정책

이 프로젝트에서는 `git commit` 및 `git push` 를 **절대 실행하지 않는다**.
코드 변경 후 commit/push가 필요하면 반드시 사용자에게 직접 하도록 안내한다.

## Language

모든 응답은 중간 과정 설명 포함 한국어로 작성한다. 일본어·영어 혼용 금지.

## Build & Run Commands

```bash
# Build entire solution
dotnet build Server.sln -c Release

# Run LoginServer
dotnet run --project LoginServer/LoginServer.csproj

# Run GameServer
dotnet run --project GameServer/GameServer.csproj

# Run test client
dotnet run --project DummyClient/DummyClient.csproj
```

**Protobuf regeneration** (after editing `Scripts/message.proto`):
```bash
# Linux (WSL) — Linux protoc binary required (protoc-27.3-linux-x86_64)
cd Scripts && /tmp/protoc/bin/protoc -I=. --csharp_out=. message.proto && cp Message.cs ../SocketLibrary/DTO/Message.cs
```

There are no automated tests; `DummyClient` is the integration test — it connects, logs in via LoginServer, connects to GameServer with auth token, and exchanges KeepAlive messages.

## Project Structure

```
Server.sln
├── SocketLibrary/ # Shared framework (Class Library, .NET 8) — namespace: Library.*
├── LoginServer/   # Login server, port 9000, AdminApi port 9010 (Console App, .NET 8)
├── GameServer/    # Game server, port 9001 (Console App, .NET 8)
├── DummyClient/   # Integration test client (Console App, .NET 8)
└── Scripts/       # Proto build script + message.proto
```

## Architecture

This is an actor-like MMO server framework. Each connected user is an isolated actor with its own message queue, preventing shared-state races.

### Concurrency Model

Two orthogonal pools distribute work by `SessionId % PoolSize`:

| Pool | Class | Count | Purpose |
|------|-------|-------|---------|
| Worker threads | `ThreadPoolManager` → `TickThreadWorker[]` | 4 | Periodic tick (100 ms) per user via `ITickable` |
| Message workers | `MessageQueueWorkerManager` → `MessageQueueWorker[]` | 8 | Drain `Channel<T>` message queues, call handlers |

Because a user always lands on the same thread and same worker, its state needs no internal locking.

### UserSession Lifecycle

`UserObjectPoolManager` pre-allocates a `ConcurrentQueue<UserSession>` (10,000 slots). On accept, a session is leased from the pool; on disconnect it is reset and returned.

Each `UserSession` owns:
- `ReceiverHandler` — `SocketAsyncEventArgs`-based receive loop
- `SenderHandler` — drain queue → serialize → send
- `MessageQueueDispatcher` — routes parsed messages to handlers
- `TimerScheduleManager` — per-session timers, ticked by `TickThreadWorker`

### Message Flow

```
Client bytes
  → ReceiveParser (2-byte length prefix + protobuf body)
  → MessageQueueWorker (enqueued via Channel<T>)
  → MessageQueueDispatcher
  → [RemoteMessageHandlerAsyncAttribute] handler
  → SenderHandler.Send() (ConcurrentQueue → serialize → write)
  → Client bytes
```

### Adding a New Message Handler

Handlers are discovered by reflection at startup — no registration code needed.

1. Add the payload case to `Scripts/message.proto` and regenerate.
2. Create a class implementing `IRemoteMessageHandlerAsync` (or sync variant).
3. Decorate it with `[RemoteMessageHandlerAsyncAttribute(MessageWrapper.PayloadOneofCase.YourCase)]`.

```csharp
[RemoteMessageHandlerAsyncAttribute(MessageWrapper.PayloadOneofCase.KeepAliveRequest)]
public class KeepAliveRequestHandler : IRemoteMessageHandlerAsync
{
    public async UniTask HandleAsync(UserSession session, MessageWrapper wrapper)
    {
        // ...
    }
}
```

`InnerMessageHandlerAttribute` / `InnerMessageHandlerManager` follow the same pattern for server-internal messages.

### Key Configuration

`SocketLibrary/ContInfo/SessionConstInfo.cs` — buffer size (8192), pool sizes, KeepAlive interval  
`SocketLibrary/ContInfo/ThreadConstInfo.cs` — thread counts, tick interval (100 ms), worker delay (10 ms)  
`LoginServer/appsettings.json` — port 9000, AdminApi port 9010, auth token TTL (30s)  
`GameServer/appsettings.json` — port 9001, Redis auth token key prefix

### Logging Convention

Use lazy-delegate form to avoid string allocation on disabled levels:
```csharp
_logger.Debug(() => $"session {session.SessionId} connected");
```
