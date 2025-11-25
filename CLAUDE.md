# CLAUDE.md - AI Assistant Guide

This document provides comprehensive guidance for AI assistants working on this C# MMO Server codebase.

## Project Overview

**Project Name:** C# Actor-Like MMO Server
**Framework:** .NET 8.0
**Architecture:** Actor-like message queue system with multi-threaded worker pools
**Primary Language:** C# with nullable reference types enabled

### Core Features
- Google Protocol Buffers for client-server communication
- Multi thread-pool distributed processing
- User session object pooling (10,000 pre-allocated sessions)
- Per-user async message queues for thread-safe state management
- Attribute-based message dispatcher (Open-Closed Principle)
- Timer-based tick system (10 ticks/second)
- High-performance async I/O using SocketAsyncEventArgs pattern

---

## Architecture & Design Patterns

### Actor-Like Pattern
Each `UserSession` acts as an independent actor:
- **Isolated State**: Each user has their own state, never accessed by multiple threads simultaneously
- **Message Queue**: All interactions happen through message passing
- **Sequential Processing**: Messages are processed one at a time per user
- **Load Balanced**: Users distributed across worker threads via `SessionId % WorkerCount`

### Key Design Patterns

1. **Object Pool Pattern**
   - Pre-allocated UserSession objects (10,000)
   - SocketAsyncEventArgs pooling for accept operations
   - Minimizes GC pressure and allocation overhead

2. **Attribute-Based Dispatcher** (Strategy + Open-Closed)
   - Handlers registered via `[RemoteMessageHandlerAttribute]` and `[InnerMessageHandlerAttribute]`
   - Reflection-based discovery at startup
   - Add new message types by creating new handler classes (no central switch needed)

3. **Factory Pattern**
   - `UserSession.Of()` for session creation
   - Encapsulates handler registration and initialization

4. **Command Pattern**
   - Messages are commands queued and processed asynchronously
   - `RemoteReceiveMessage`, `RemoteSendMessage`, `InnerReceiveMessage`

5. **Worker Thread Pattern**
   - Configurable thread pool (8 message queue workers, 4 tick workers)
   - Each worker handles subset of users

6. **Async Event Args Pattern**
   - Zero-allocation async I/O for network operations
   - Event-driven completion callbacks

---

## Project Structure

### Solution Layout

```
csharp_likeactor/
├── Server/                    # Main executable project
│   ├── Server.sln            # Solution file
│   ├── Server.csproj         # Project file (depends on Library)
│   ├── Program.cs            # Entry point
│   ├── TcpServer.cs          # Server initialization and lifecycle
│   ├── Acceptor/             # TCP connection acceptance
│   │   ├── Acceptor.cs
│   │   └── SocketAsyncEventArgsPool.cs
│   ├── Actors/               # User actors
│   │   ├── UserSession.cs    # Main user actor implementation
│   │   ├── UserObjectPoolManager.cs
│   │   └── User/Handler/     # Message handlers
│   │       ├── Remote/       # Client message handlers
│   │       └── Inner/        # Server-internal handlers
│   └── Model/Message/        # Server-specific message types
│
├── Library/                   # Shared infrastructure library
│   ├── Library.csproj        # .NET 8.0, Google.Protobuf 3.27.3
│   ├── ContInfo/             # Configuration constants
│   ├── DTO/                  # Protocol Buffer generated code
│   ├── Logger/               # Logging abstraction
│   ├── MessageQueue/         # Core message queue system
│   │   ├── MessageQueueWorker.cs
│   │   ├── MessageQueueDispatcher.cs
│   │   ├── Attributes/       # Handler registration
│   │   │   ├── Remote/       # Remote message handler interfaces
│   │   │   └── Inner/        # Inner message handler interfaces
│   │   └── Message/          # Message wrapper types
│   ├── Model/                # Domain models
│   ├── Network/              # TCP networking layer
│   │   ├── ReceiverHandler.cs
│   │   ├── SenderHandler.cs
│   │   └── ReceiveParser.cs
│   ├── ObjectPool/           # Generic pooling infrastructure
│   ├── Timer/                # Timer scheduling system
│   └── Worker/               # Thread pool management
│
├── DummyClient/              # Test client project
│   ├── DummyClient.csproj
│   ├── Program.cs
│   ├── TcpDummyClient.cs
│   └── Session/              # Mirrors server structure
│       ├── UserSession.cs
│       └── Handler/Remote/
│
└── Scripts/                  # Protocol Buffer definitions
    ├── message.proto         # Protocol definitions
    └── protoc/               # Protobuf compiler tools
```

---

## Key Components Deep Dive

### 1. UserSession (`Server/Actors/User/UserSession.cs`)

The core actor implementation managing individual user connections.

```csharp
public class UserSession : IDisposable, ITickable, IMessageQueueReceiver, ISessionUsable
```

**Key Responsibilities:**
- Manages user connection lifecycle
- Owns network handlers (`ReceiverHandler`, `SenderHandler`)
- Contains message queue dispatcher
- Manages timer schedules
- Processes messages from queue
- Implements `Tick()` for periodic updates

**Important Methods:**
- `UserSession.Of()` - Factory method (registers handlers)
- `OnRecvMessageAsync()` - Processes received messages
- `Tick()` - Called 10 times per second for periodic logic
- `Disconnect()` - Graceful disconnect handling
- `Dispose()` - Cleanup and return to pool

**Location:** `/Server/Actors/User/UserSession.cs`

### 2. Message Queue System (`Library/MessageQueue/`)

**MessageQueueWorker** (`MessageQueueWorker.cs`)
- Runs on dedicated threads (8 workers by default)
- Uses `System.Threading.Channels` for lock-free queuing
- Routes messages to appropriate UserSession
- 10ms delay per iteration to prevent CPU spinning

**MessageQueueDispatcher** (`MessageQueueDispatcher.cs`)
- Routes messages to registered handlers
- Supports both sync (`IMessageHandler`) and async (`IMessageHandlerAsync`)
- Two types: `RemoteMessageQueueDispatcher` and `InnerMessageQueueDispatcher`

**Key Configuration** (`Library/ContInfo/ThreadConstInfo.cs`):
```csharp
MaxMessageQueueWorkerCount = 8
MessageQueueThreadDelay = 10 (milliseconds)
```

### 3. Network Layer (`Library/Network/`)

**ReceiverHandler** (`ReceiverHandler.cs`)
- Async receive using SocketAsyncEventArgs
- Uses `ReceiveParser` for TCP framing
- Deserializes Protocol Buffer messages
- Enqueues to message queue

**SenderHandler** (`SenderHandler.cs`)
- Async send with internal queue
- Serializes messages to Protocol Buffers
- Length-prefixed protocol (2 bytes + payload)

**ReceiveParser** (`ReceiveParser.cs`)
- State machine: Header → Body
- Handles partial messages across multiple receives
- 2-byte length prefix protocol
- Max buffer: 8,192 bytes

**Protocol Format:**
```
[2 bytes: message length][N bytes: protobuf payload]
```

### 4. Acceptor (`Server/Acceptor/Acceptor.cs`)

High-performance connection acceptance:
- Pre-allocated `SocketAsyncEventArgs` pool
- 4 concurrent accept operations
- Event-driven with `OnAccepted` callback
- Port 9000 by default

### 5. Handler System

**Two Message Types:**

1. **Remote Messages** (Client ↔ Server)
   - Uses Protocol Buffers (`MessageWrapper`)
   - Handler location: `Server/Actors/User/Handler/Remote/`

   Example:
   ```csharp
   [RemoteMessageHandlerAsyncAttribute(MessageWrapper.PayloadOneofCase.KeepAliveRequest)]
   public class KeepAliveRequestHandler : IRemoteMessageHandlerAsync
   {
       public async Task HandleAsync(UserSession session, RemoteReceiveMessage message)
       {
           // Process message
           var request = message.MessageWrapper.KeepAliveRequest;

           // Send response
           var response = new MessageWrapper { /* ... */ };
           session.EnqueueSendMessage(response);
       }
   }
   ```

2. **Inner Messages** (Server Internal)
   - C# classes implementing `IInnerServerMessage`
   - Handler location: `Server/Actors/User/Handler/Inner/`

   Example:
   ```csharp
   [InnerMessageHandlerAsyncAttribute(typeof(InnerTestMessage))]
   public class InnerMessageTestHandler : IInnerMessageHandlerAsync
   {
       public async Task HandleAsync(UserSession session, InnerReceiveMessage message)
       {
           var innerMsg = (InnerTestMessage)message.InnerMessage;
           // Process inner message
       }
   }
   ```

**Handler Registration:**
- Automatic via reflection at startup
- Scans assembly for `[RemoteMessageHandlerAttribute]` and `[InnerMessageHandlerAttribute]`
- Registers to appropriate dispatcher
- No manual registration needed

---

## Message Flow

### Remote Message Flow (Client → Server)

```
Client sends message
    ↓
ReceiverHandler receives bytes (SocketAsyncEventArgs)
    ↓
ReceiveParser parses length-prefixed protocol
    ↓
Deserialize to MessageWrapper (Protobuf)
    ↓
Wrap in RemoteReceiveMessage
    ↓
Enqueue to MessageQueueWorker (SessionId % 8)
    ↓
MessageQueueWorker dequeues
    ↓
Calls UserSession.OnRecvMessageAsync()
    ↓
MessageQueueDispatcher routes to handler
    ↓
RemoteMessageHandler processes
    ↓
Handler creates response and calls session.EnqueueSendMessage()
    ↓
SenderHandler serializes and sends
```

### Inner Message Flow (Server Internal)

```
Code creates IInnerServerMessage
    ↓
Wrap in InnerReceiveMessage
    ↓
Enqueue to target user's MessageQueueWorker
    ↓
MessageQueueWorker processes
    ↓
MessageQueueDispatcher routes to InnerMessageHandler
    ↓
Handler processes message
```

**Critical Insight:** All messages for a specific user are processed sequentially by their assigned worker, ensuring thread-safe access to user state without locks.

---

## Development Workflows

### Adding a New Message Type

#### 1. Define in Protocol Buffer (`Scripts/message.proto`)

```protobuf
message NewFeatureRequest {
    int32 feature_id = 1;
    string data = 2;
}

message NewFeatureResponse {
    bool success = 1;
}

message MessageWrapper {
    oneof payload {
        // ... existing messages ...
        NewFeatureRequest new_feature_request = 10;
        NewFeatureResponse new_feature_response = 11;
    }
}
```

#### 2. Regenerate Protocol Buffer Code

```bash
cd Scripts
./protoc/protoc --csharp_out=. message.proto
# Copy generated Message.cs to Library/DTO/
```

#### 3. Create Handler (`Server/Actors/User/Handler/Remote/`)

Create `NewFeatureRequestHandler.cs`:

```csharp
using Library.MessageQueue.Attributes.Remote;
using Library.MessageQueue.Message;
using Library.DTO;

namespace Server.Actors.User.Handler.Remote;

[RemoteMessageHandlerAsyncAttribute(MessageWrapper.PayloadOneofCase.NewFeatureRequest)]
public class NewFeatureRequestHandler : IRemoteMessageHandlerAsync
{
    public async Task HandleAsync(UserSession session, RemoteReceiveMessage message)
    {
        var request = message.MessageWrapper.NewFeatureRequest;

        // Business logic here
        Console.WriteLine($"Feature ID: {request.FeatureId}");

        // Send response
        var response = new MessageWrapper
        {
            NewFeatureResponse = new NewFeatureResponse
            {
                Success = true
            }
        };

        session.EnqueueSendMessage(response);
    }
}
```

#### 4. Build and Test

The handler is automatically registered via reflection. No manual registration needed!

### Adding an Inner Message

#### 1. Define Message Class (`Server/Model/Message/`)

```csharp
using Library.Model;

namespace Server.Model.Message;

public class CustomInnerMessage : IInnerServerMessage
{
    public int Data { get; set; }
    public string Info { get; set; }
}
```

#### 2. Create Handler (`Server/Actors/User/Handler/Inner/`)

```csharp
using Library.MessageQueue.Attributes.Inner;
using Library.MessageQueue.Message;
using Server.Model.Message;

namespace Server.Actors.User.Handler.Inner;

[InnerMessageHandlerAsyncAttribute(typeof(CustomInnerMessage))]
public class CustomInnerMessageHandler : IInnerMessageHandlerAsync
{
    public async Task HandleAsync(UserSession session, InnerReceiveMessage message)
    {
        var innerMsg = (CustomInnerMessage)message.InnerMessage;
        // Process message
    }
}
```

#### 3. Send Inner Message

```csharp
// From anywhere in the codebase
var innerMsg = new CustomInnerMessage
{
    Data = 42,
    Info = "test"
};

// Enqueue to specific user's message queue
targetUserSession.EnqueueInnerMessage(innerMsg);
```

### Adding Tick-Based Logic

Tick happens 10 times per second (every 100ms) for all active users.

**Edit** `Server/Actors/User/UserSession.cs`:

```csharp
public void Tick()
{
    try
    {
        // Your periodic logic here
        // Example: Check buffs, cooldowns, position updates, etc.
    }
    catch (Exception e)
    {
        Console.WriteLine($"Tick exception: {e}");
    }
}
```

**Configuration:** `Library/ContInfo/ThreadConstInfo.cs`
- `MaxUserThreadCount = 4` (tick worker threads)
- `TickMillSecond = 100` (100ms = 10 ticks/second)

### Adding Timer-Based Actions

For non-tick based timers (scheduled tasks):

```csharp
// In UserSession
public void ScheduleAction()
{
    // Schedule a one-time action in 5 seconds
    _timerScheduler.Schedule(5000, () =>
    {
        Console.WriteLine("Action executed after 5 seconds");
    });

    // Schedule repeating action every 1 second
    _timerScheduler.ScheduleRepeating(1000, () =>
    {
        Console.WriteLine("Repeating action");
    });
}
```

---

## Coding Conventions

### Naming Conventions

- **Classes**: PascalCase (`UserSession`, `MessageQueueWorker`)
- **Interfaces**: PascalCase with 'I' prefix (`ITickable`, `IMessageHandler`)
- **Methods**: PascalCase (`OnRecvMessageAsync`, `Disconnect`)
- **Private fields**: underscore prefix (`_sessionId`, `_messageQueue`)
- **Parameters**: camelCase (`session`, `message`)
- **Async methods**: Suffix with "Async" (`HandleAsync`, `ProcessAsync`)

### File Organization

- **Handlers**: End with "Handler" (`KeepAliveRequestHandler.cs`)
- **Managers**: End with "Manager" (`UserObjectPoolManager.cs`)
- **One class per file**: Match filename to class name
- **Namespace matches folder structure**: `Server.Actors.User.Handler.Remote`

### Code Style

```csharp
// Use expression-bodied members for simple properties
public int SessionId => _sessionId;

// Use null-conditional operators
_receiverHandler?.Dispose();

// Use pattern matching
if (message is RemoteReceiveMessage remoteMsg)
{
    // Handle remote message
}

// Use LINQ for collections
var handlers = assembly.GetTypes()
    .Where(t => t.GetCustomAttribute<RemoteMessageHandlerAttribute>() != null)
    .ToList();

// Async/await for I/O operations
public async Task HandleAsync(UserSession session, RemoteReceiveMessage message)
{
    await Task.Delay(100); // Example async operation
}
```

### Error Handling

```csharp
// Always catch and log exceptions in message handlers
public async Task HandleAsync(UserSession session, RemoteReceiveMessage message)
{
    try
    {
        // Handler logic
    }
    catch (Exception e)
    {
        Console.WriteLine($"Handler error: {e}");
        // Don't let exceptions kill the message queue worker
    }
}

// Tick methods should also catch exceptions
public void Tick()
{
    try
    {
        // Tick logic
    }
    catch (Exception e)
    {
        Console.WriteLine($"Tick error: {e}");
    }
}
```

### Thread Safety

**DO:**
- Enqueue messages to modify user state
- Use concurrent collections for shared state (`ConcurrentQueue`, `ConcurrentDictionary`)
- Use `ReaderWriterLockSlim` for read-heavy, write-light scenarios

**DON'T:**
- Access UserSession fields from multiple threads directly
- Use locks within message handlers (not needed - sequential processing)
- Block on async operations (`Task.Result`, `Task.Wait()`)

---

## Configuration Constants

### Session Configuration (`Library/ContInfo/SessionConstInfo.cs`)
```csharp
MaxUserSessionPoolSize = 10000  // Pre-allocated user sessions
MaxBufferSize = 8192           // Network buffer size (8KB)
ServerPort = 9000              // TCP listen port
```

### Thread Configuration (`Library/ContInfo/ThreadConstInfo.cs`)
```csharp
MaxUserThreadCount = 4               // Tick worker threads
MaxMessageQueueWorkerCount = 8      // Message queue workers
TickMillSecond = 100                // 100ms (10 ticks/sec)
MessageQueueThreadDelay = 10        // 10ms delay per queue iteration
```

**Tuning Guidelines:**
- **MessageQueueWorkerCount**: 1-2 per CPU core recommended
- **UserThreadCount**: 1 per CPU core for tick processing
- **TickMillSecond**: Lower = more responsive, higher CPU usage
- **MessageQueueThreadDelay**: Prevents CPU spinning, tune based on latency requirements

---

## Testing with DummyClient

The `DummyClient` project mirrors the server handler structure.

### Running Tests

```bash
# Terminal 1: Start server
cd Server
dotnet run

# Terminal 2: Start dummy client
cd DummyClient
dotnet run
```

### Adding Client-Side Handler

When adding a server message handler, add corresponding client handler:

**Server Handler:** `Server/Actors/User/Handler/Remote/KeepAliveRequestHandler.cs`
**Client Handler:** `DummyClient/Session/Handler/Remote/KeepAliveNotiHandler.cs`

```csharp
namespace DummyClient.Session.Handler.Remote;

[RemoteMessageHandlerAttribute(MessageWrapper.PayloadOneofCase.KeepAliveNoti)]
public class KeepAliveNotiHandler : IRemoteMessageHandler
{
    public void Handle(UserSession session, RemoteReceiveMessage message)
    {
        var noti = message.MessageWrapper.KeepAliveNoti;
        Console.WriteLine($"[Client] KeepAlive received");

        // Send response
        var request = new MessageWrapper
        {
            KeepAliveRequest = new KeepAliveRequest()
        };
        session.EnqueueSendMessage(request);
    }
}
```

---

## Common Tasks Guide

### Task: Find where a message is handled

```bash
# Search for handler by message type
grep -r "KeepAliveRequest" Server/Actors/User/Handler/
```

Or use the attribute:
```bash
grep -r "MessageWrapper.PayloadOneofCase.KeepAliveRequest" Server/
```

**Expected location:** `Server/Actors/User/Handler/Remote/[MessageName]Handler.cs`

### Task: Debug message flow

Add logging at key points:

1. **Receive:** `Library/Network/ReceiverHandler.cs` - `OnParseComplete()`
2. **Queue:** `Library/MessageQueue/MessageQueueWorker.cs` - `StartWork()`
3. **Dispatch:** `Library/MessageQueue/MessageQueueDispatcher.cs` - `Dispatch()`
4. **Handle:** Your handler's `HandleAsync()` method
5. **Send:** `Library/Network/SenderHandler.cs` - `Send()`

### Task: Add logging

Use the logging abstraction:

```csharp
// In UserSession or handlers
private readonly IServerLogger _logger = ServerLoggerFactory.GetLogger();

_logger.LogInfo($"Processing message for session {_sessionId}");
_logger.LogError($"Error occurred: {exception.Message}");
```

**Logger Implementation:** `Library/Logger/ConsoleServerLogger.cs`

### Task: Monitor performance

Key metrics to track:

1. **Message Queue Depth**: `MessageQueueWorker` - check Channel.Reader.Count
2. **Tick Duration**: Time `UserSession.Tick()` execution
3. **Handler Duration**: Time `HandleAsync()` execution
4. **Active Sessions**: Count in `UserObjectPoolManager`
5. **Network I/O**: Track bytes sent/received in handlers

### Task: Handle user disconnect

Disconnects are handled automatically:

1. Network error triggers `ReceiverHandler.Disconnect()`
2. Enqueues `UserDisconnectMessage` (inner message)
3. `UserSession` processes disconnect
4. Session cleanup in `UserSession.Dispose()`
5. Session returned to pool

**Add custom disconnect logic in:**
```csharp
// UserSession.cs
public void Disconnect()
{
    // Your cleanup logic here
    // Save state, notify other players, etc.

    _receiverHandler?.Disconnect();
    _senderHandler?.Disconnect();
}
```

### Task: Send message to another user

```csharp
// From within a handler
public async Task HandleAsync(UserSession session, RemoteReceiveMessage message)
{
    // Get target user's session (implementation depends on your session management)
    var targetSession = GetUserSession(targetUserId);

    // Send inner message to target user
    var innerMsg = new CustomInnerMessage { /* data */ };
    targetSession.EnqueueInnerMessage(innerMsg);

    // Or send remote message (to their client)
    var remoteMsg = new MessageWrapper { /* data */ };
    targetSession.EnqueueSendMessage(remoteMsg);
}
```

---

## Important Files & Locations

### Core Files to Understand

| File Path | Purpose | Key Content |
|-----------|---------|-------------|
| `Server/Program.cs` | Entry point | Server initialization, signal handling |
| `Server/TcpServer.cs` | Server lifecycle | Acceptor, workers, tick system setup |
| `Server/Actors/User/UserSession.cs` | User actor | Message processing, tick, state management |
| `Library/MessageQueue/MessageQueueWorker.cs` | Message queue | Queue processing loop, message routing |
| `Library/MessageQueue/MessageQueueDispatcher.cs` | Dispatcher | Handler invocation, attribute registration |
| `Library/Network/ReceiverHandler.cs` | Network receive | Async receive, protocol parsing |
| `Library/Network/SenderHandler.cs` | Network send | Async send, message serialization |
| `Library/Network/ReceiveParser.cs` | Protocol parser | Length-prefix parsing state machine |
| `Server/Acceptor/Acceptor.cs` | Connection accept | High-perf async accept |
| `Scripts/message.proto` | Protocol definition | Protobuf message definitions |

### Configuration Files

- `Library/ContInfo/SessionConstInfo.cs` - Session and network config
- `Library/ContInfo/ThreadConstInfo.cs` - Thread pool configuration
- `Server/Server.csproj` - Project dependencies
- `Library/Library.csproj` - Library dependencies

### Handler Locations

- **Server Remote Handlers**: `Server/Actors/User/Handler/Remote/`
- **Server Inner Handlers**: `Server/Actors/User/Handler/Inner/`
- **Client Handlers**: `DummyClient/Session/Handler/Remote/`

---

## Protocol Buffer Workflow

### Protocol Definition (`Scripts/message.proto`)

```protobuf
syntax = "proto3";

option csharp_namespace = "Library.DTO";

// Individual messages
message KeepAliveRequest { }
message KeepAliveNoti { }

// Wrapper with oneof
message MessageWrapper {
    oneof payload {
        KeepAliveRequest keep_alive_request = 1;
        KeepAliveNoti keep_alive_noti = 2;
        // Add new messages here
    }
}
```

### Generating C# Code

```bash
cd Scripts
./protoc/protoc --csharp_out=. message.proto
# Generated: Message.cs

# Copy to Library
cp Message.cs ../Library/DTO/Message.cs
```

**Tools:**
- `Scripts/protoc/protoc` - Protocol Buffer compiler v28.0
- `Scripts/protoc/old/protoc` - v23.2 (legacy)

### Using Generated Code

```csharp
using Library.DTO;

// Create message
var wrapper = new MessageWrapper
{
    KeepAliveRequest = new KeepAliveRequest()
};

// Serialize
byte[] bytes = wrapper.ToByteArray();

// Deserialize
var parsed = MessageWrapper.Parser.ParseFrom(bytes);

// Check message type
if (wrapper.PayloadCase == MessageWrapper.PayloadOneofCase.KeepAliveRequest)
{
    var request = wrapper.KeepAliveRequest;
}
```

---

## Debugging Tips

### Common Issues

**1. Handler Not Invoked**
- Check attribute spelling: `[RemoteMessageHandlerAsyncAttribute(...)]`
- Verify message type in attribute matches protocol
- Ensure handler implements correct interface (`IRemoteMessageHandlerAsync`)
- Handler must be public class

**2. Connection Issues**
- Check port: Default is 9000
- Verify firewall settings
- Check `Acceptor` initialization in `TcpServer.cs`
- Review `SocketAsyncEventArgsPool` size

**3. Message Not Received**
- Verify length-prefix protocol (2 bytes)
- Check `ReceiveParser` state transitions
- Ensure client serializes correctly
- Max buffer size is 8,192 bytes

**4. Thread Deadlock**
- Never use `Task.Wait()` or `Task.Result` in async code
- Use `await` consistently
- Check for circular message sending

**5. High CPU Usage**
- Adjust `MessageQueueThreadDelay` (increase from 10ms)
- Reduce `TickMillSecond` frequency
- Profile handler execution time

### Logging Strategy

Add strategic logs:

```csharp
// Message received
Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Session {_sessionId} received {message.Type}");

// Handler invoked
Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Invoking handler for {messageType}");

// Message sent
Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Session {_sessionId} sent {message.Type}");
```

---

## Best Practices for AI Assistants

### When Reading Code

1. **Start with Program.cs** to understand initialization flow
2. **Read UserSession.cs** to understand the actor model
3. **Check handler attributes** to understand message routing
4. **Review Protocol Buffer definitions** before adding messages

### When Modifying Code

1. **Always read files before editing** - Never modify code you haven't read
2. **Follow the attribute pattern** - Don't break the dispatcher system
3. **Match naming conventions** - Handlers end with "Handler"
4. **Keep it simple** - Don't over-engineer solutions
5. **Thread safety** - Use message passing, not locks
6. **Error handling** - Always wrap handlers in try-catch
7. **Test with DummyClient** - Add matching client handlers

### When Adding Features

1. **Protocol first** - Define messages in `message.proto`
2. **Regenerate code** - Update `Message.cs`
3. **Create handlers** - Add server and client handlers
4. **Test end-to-end** - Run both server and client
5. **Document in CLAUDE.md** - Update this file

### Code Review Checklist

- [ ] Handler has correct attribute
- [ ] Handler implements correct interface
- [ ] Error handling with try-catch
- [ ] Async methods use await (never .Result/.Wait)
- [ ] Follows naming conventions
- [ ] No direct thread access to UserSession
- [ ] Message types match protocol definition
- [ ] Client handler added if needed
- [ ] No blocking operations in handlers
- [ ] Logging added for debugging

---

## Git Workflow

### Branch Strategy

- **Main branch**: Production-ready code
- **Feature branches**: `claude/` prefix for AI assistant work
- **Format**: `claude/claude-md-<session-id>`

### Commit Guidelines

```bash
# Good commit messages
git commit -m "Add KeepAlive message handler"
git commit -m "Fix message queue worker thread safety"
git commit -m "Update Protocol Buffer definitions"

# Bad commit messages
git commit -m "Update code"
git commit -m "Fix bug"
git commit -m "Changes"
```

### Push Protocol

```bash
# Always push to correct branch with -u flag
git push -u origin claude/claude-md-<session-id>

# Retry on network failure (up to 4 times with exponential backoff)
# 2s, 4s, 8s, 16s delays
```

---

## Quick Reference Commands

### Build & Run

```bash
# Build solution
cd Server
dotnet build

# Run server
dotnet run

# Run client
cd ../DummyClient
dotnet run

# Clean build
dotnet clean
dotnet build
```

### Protocol Buffers

```bash
# Generate C# code from proto
cd Scripts
./protoc/protoc --csharp_out=. message.proto

# Copy to library
cp Message.cs ../Library/DTO/
```

### Search & Navigation

```bash
# Find handler by message type
grep -r "KeepAliveRequest" Server/Actors/User/Handler/

# Find all handlers
find . -name "*Handler.cs"

# Find Protocol Buffer messages
grep "message " Scripts/message.proto

# Find configuration constants
grep -r "MaxUserSessionPoolSize" Library/ContInfo/
```

---

## Architecture Strengths

Understanding these will help you work with the codebase effectively:

1. **Scalability**: Thread pool distributes load across multiple workers
2. **Thread Safety**: Per-user message queues eliminate need for locks on user state
3. **Performance**: Object pooling and async I/O minimize allocations
4. **Extensibility**: Attribute-based handlers make adding messages trivial
5. **Separation of Concerns**: Clean separation between network, queue, and business logic
6. **Actor-like Isolation**: Each user's state is isolated and processes messages sequentially
7. **High Throughput**: Zero-allocation networking with SocketAsyncEventArgs
8. **Resource Efficiency**: Pre-allocated pools minimize GC pressure

---

## Key Metrics & Limits

- **Max Concurrent Users**: 10,000 (pool size)
- **Message Queue Workers**: 8 threads
- **Tick Workers**: 4 threads
- **Tick Frequency**: 10 per second (100ms)
- **Max Message Size**: 8,192 bytes
- **Server Port**: 9,000
- **Accept Operations**: 4 concurrent
- **Queue Delay**: 10ms per iteration

---

## Resources & References

### Internal Documentation
- This file: `CLAUDE.md`
- Project README: `README.md`
- Protocol definitions: `Scripts/message.proto`

### External Resources
- [Protocol Buffers Documentation](https://developers.google.com/protocol-buffers)
- [.NET 8.0 Documentation](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-8)
- [System.Threading.Channels](https://learn.microsoft.com/en-us/dotnet/api/system.threading.channels)
- [SocketAsyncEventArgs Pattern](https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socketasynceventargs)

---

## Version History

- **2025-11-25**: Initial comprehensive documentation created
  - Complete architecture analysis
  - Development workflow guidelines
  - Code conventions documented
  - Common tasks guide added
  - Best practices for AI assistants

---

## Contact & Support

For questions about this codebase or to report issues with this documentation:
- Update this `CLAUDE.md` file with new findings
- Document new patterns as they emerge
- Keep conventions section current with code changes

**Remember:** This is a living document. Update it as the codebase evolves!
