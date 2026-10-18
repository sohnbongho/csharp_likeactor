# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

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
# Windows (Git Bash) — protoc 34.1 is checked in under Scripts/
cd Scripts && ./protoc-34.1-win64/bin/protoc.exe -I=. --csharp_out=. message.proto && cp Message.cs ../SocketLibrary/DTO/Message.cs
```
The Unity client (`../unity_samkuk`, handled by the `unity_samkuk` Claude session) copies the same `Message.cs` to `Assets/Scripts/Net/Proto/`.

**Game data for co-op battles** (`GameServer/Data/*.json`, copied to the build output): exported from the Unity project's ScriptableObjects. Primary exporter is the Unity editor menu `Samkuk > Export Server Data`; `python Scripts/export_unity_data.py` is the editor-less fallback. Do not hand-edit the JSON.

There are no automated tests; `DummyClient` is the integration test — it connects, logs in via LoginServer, connects to GameServer with auth token, and exchanges KeepAlive messages.
```bash
# co-op battle test: 4 clients per room, 1 round (both servers must be running)
dotnet run --project DummyClient/DummyClient.csproj -- --clients 4 --coop 4 1
```

## Project Structure

```
Server.sln
├── SocketLibrary/ # Shared framework (Class Library, .NET 8) — namespace: Library.*
├── LoginServer/   # Login server, port 9100, AdminApi port 9110 (Console App, .NET 8)
├── GameServer/    # Game server, port 9101 (Console App, .NET 8)
├── DummyClient/   # Integration test client (Console App, .NET 8)
├── Scripts/       # protoc + message.proto + Unity data exporter
└── Docs/          # Design docs (coop-design.md: server-authoritative co-op)
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

### Co-op Battle (server-authoritative)

Design: `Docs/coop-design.md`. The server simulates the whole battle; Unity only renders snapshots and sends input.

- `GameServer/Rooms/Room.cs` — room actor (`ITickable` on `RoomThreadManager`, 50 ms tick). Session threads only `Enqueue(Action<Room>)`; all state changes and sends happen on the room thread. Members are validated by `SessionId` because sessions are pooled and reused.
- `GameServer/Battle/Sim/BattleWorld.cs` — the simulation (ported from Unity `EnemyManager`, `EnemySpawner`, `StageController`, `PlayerHealth`, `ExpGemManager`, `Weapon*`). `Step()` at 20 Hz, `BuildSnapshot(viewer)` at 10 Hz with an 18-unit area of interest. Events accumulate in `Events` until the room sends a snapshot.
- `GameServer/Battle/Sim/WeaponSystem.cs`, `UpgradeGenerator.cs` — weapon rules and level-up choices. Keep formulas identical to the Unity originals; the client draws from events (`WeaponFire`, `AreaFx`, `ProjectileSpawn`) and never computes damage.
- `GameServer/Battle/Data/GameDataStore.cs` — loads `Data/*.json` at startup; ids are Unity asset file names (`Enemy_Soldier`, `Weapon_Bow`, `Hero_LiuBei`, `Stage_YellowTurban`).
- Snapshot bodies over 8190 bytes are split into several `BattleSnapshotNoti` with the same tick (see design doc 3.2).

### Key Configuration

`SocketLibrary/ContInfo/SessionConstInfo.cs` — buffer size (8192), pool sizes, KeepAlive interval  
`SocketLibrary/ContInfo/ThreadConstInfo.cs` — thread counts, tick interval (100 ms), worker delay (10 ms)  
`LoginServer/appsettings.json` — port 9100, AdminApi port 9110, auth token TTL (30s)  
`GameServer/appsettings.json` — port 9101, Redis auth token key prefix

### Logging Convention

Use lazy-delegate form to avoid string allocation on disabled levels:
```csharp
_logger.Debug(() => $"session {session.SessionId} connected");
```
