# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run Commands

```bash
# Build entire solution
dotnet build Server/Server.sln -c Release

# Run server
dotnet run --project Server/Server.csproj

# Run test client
dotnet run --project DummyClient/DummyClient.csproj
```

**Protobuf regeneration** (after editing `Scripts/message.proto`):
```bash
Scripts/build.bat   # Runs protoc, copies Message.cs to Library/DTO/
```

There are no automated tests; `DummyClient` is the integration test — it connects and exchanges KeepAlive messages.

## Project Structure

```
Server.sln
├── Library/       # Shared framework (Class Library, .NET 8)
├── Server/        # Game server executable (Console App, .NET 8)
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

`Library/ContInfo/SessionConstInfo.cs` — port (9000), buffer size (8192), pool sizes  
`Library/ContInfo/ThreadConstInfo.cs` — thread counts, tick interval (100 ms), worker delay (10 ms)

### Logging Convention

Use lazy-delegate form to avoid string allocation on disabled levels:
```csharp
_logger.Debug(() => $"session {session.SessionId} connected");
```
