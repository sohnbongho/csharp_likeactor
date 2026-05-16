# 코드 리뷰: LoginServer / GameServer 분리 구현

날짜: 2026-05-16

## 총평

단일 Server를 LoginServer + GameServer로 분리하고 Redis 토큰 기반 인증 핸드오프를 구현한 커밋. 전반적인 아키텍처 방향은 올바르며 핵심 동시성 패턴(Volatile, CAS, Channel)이 일관되게 적용되어 있다. 다만 DummyClient의 소켓 교체 코드에서 리소스 누수와 잠재적 race condition이 발견된다.

## 리뷰 대상

- `GameServer/TcpGameServer.cs`
- `GameServer/Actors/User/UserSession.cs`
- `GameServer/Actors/UserObjectPoolManager.cs`
- `GameServer/Actors/User/Handler/Remote/GameConnectRequestHandler.cs`
- `GameServer/Actors/User/Handler/Inner/GameConnectResultHandler.cs`
- `GameServer/Actors/User/DbRequest/Cache/ValidateTokenCacheRequest.cs`
- `GameServer/Model/Message/GameConnectResultMessage.cs`
- `LoginServer/TcpLoginServer.cs`
- `LoginServer/Actors/User/UserSession.cs`
- `LoginServer/Actors/User/Handler/Remote/LoginRequestHandler.cs`
- `LoginServer/Actors/User/Handler/Inner/LoginResultHandler.cs`
- `LoginServer/Actors/User/DbRequest/Sql/LoginSqlRequest.cs`
- `DummyClient/Session/UserSession.cs`
- `DummyClient/Session/UserObjectPoolManager.cs`
- `DummyClient/TcpDummyClient.cs`

---

## 심각도별 지적 사항

### 🔴 심각 (버그·보안·데이터 손실 위험)

#### 1. DummyClient 소켓 교체 시 구 핸들러 Dispose 누락 — 리소스 누수

**위치**: `DummyClient/Session/UserSession.cs:ConnectToGameServerAsync` (약 83~110행)

`ConnectToGameServerAsync`에서 구 `SenderHandler`와 `ReceiverHandler`를 새 객체로 교체할 때 기존 객체를 `Reset()`만 하고 `Dispose()`를 호출하지 않는다. 두 핸들러 모두 `SocketAsyncEventArgs`를 내부에 보유하며, `Dispose()`에서만 `_receiveEventArgs.Dispose()` 등 비관리 리소스를 해제한다.

```csharp
// 문제 코드
_receiver.Reset();   // SocketAsyncEventArgs가 해제되지 않음
_client.Dispose();
// ...
_sender = new SenderHandler();   // 구 SenderHandler의 SocketAsyncEventArgs 누수
_receiver = new ReceiverHandler(this);  // 구 ReceiverHandler의 SocketAsyncEventArgs 누수
```

세션 수(DummyClient 기본 1,000개) × 교체 횟수만큼 `SocketAsyncEventArgs`가 GC에 맡겨지며, 종료 전까지 Windows IOCP 핸들이 유지되는 문제가 생길 수 있다.

**수정 방향**: 교체 전 기존 핸들러를 `Dispose()` 호출할 것.
```csharp
_receiver.Dispose();
_sender.Dispose();
_client.Dispose();
// ...
_sender = new SenderHandler();
_receiver = new ReceiverHandler(this);
```

---

### 🟡 경고 (잠재적 문제·성능·동시성 위험)

#### 2. `GameConnectRequestHandler.AuthTokenPrefix`가 `internal static` mutable 필드

**위치**: `GameServer/Actors/User/Handler/Remote/GameConnectRequestHandler.cs:12`

```csharp
internal static string AuthTokenPrefix = "auth:token:";
```

`TcpGameServer` 생성자에서 한 번 설정 후 변경되지 않으나, `static` mutable 필드는 멀티스레드 환경에서 가시성이 보장되지 않는다(`volatile` 없음). 또한 핸들러가 `static` 필드를 통해 설정을 받는 패턴은 의존성이 숨겨져 테스트하기 어렵다.

**수정 방향**: `static readonly`로 변경하거나, 설정값을 `Program.cs`에서 `AppConfig` 등 공유 설정 객체에 담아 핸들러가 접근하도록 변경할 것.

#### 3. `LoginServer.UserSession.TryIssueAuthToken`이 Redis를 직접 호출 — CacheWorkerManager 우회

**위치**: `LoginServer/Actors/User/UserSession.cs:TryIssueAuthToken` (약 183~190행)

```csharp
internal async Task<string?> TryIssueAuthToken(ulong accountId, string userId)
{
    var token = Guid.NewGuid().ToString("N");
    var key = $"{_authTokenPrefix}{token}";
    var value = $"{accountId}:{userId}";
    var set = await _redisDb.StringSetAsync(key, value, TimeSpan.FromSeconds(_authTokenTtlSeconds));
    return set ? token : null;
}
```

모든 DB 요청은 `SqlWorkerManager` / `CacheWorkerManager`를 통해 retry, error handling, 채널 backpressure가 적용되는 구조인데, 이 경로만 `IDatabase`를 직접 사용한다. 결과적으로:
- Redis 연결 오류 시 재시도 없이 즉시 실패.
- `LoginResultHandler`(내부 메시지 핸들러, 즉 Tick 스레드 위)에서 `await`로 직접 Redis IO 발생 — Tick 스레드가 Redis latency에 묶임.
- `UserSession`이 `IDatabase`를 직접 의존해 10,000 세션 객체가 같은 참조를 공유하나 불필요한 설계 복잡도.

**수정 방향**: 토큰 발급도 `ICacheRequest` 구현체로 분리해 `CacheWorkerManager`를 통해 처리할 것.

#### 4. `await Task.Delay(1)` — race condition 완화책으로 신뢰 불가

**위치**: `DummyClient/Session/UserSession.cs:ConnectToGameServerAsync` (약 93행)

```csharp
Interlocked.Exchange(ref _disposedFlag, 1);
_receiver.Reset();   // 구소켓 종료 → IOCP 완료 이벤트 큐잉
_client.Dispose();
await Task.Delay(1); // ← IOCP 콜백이 완료되길 기대하는 heuristic
```

`_disposedFlag=1`로 인해 `EnqueueMessageAsync` → false → `Disconnected()` → `Dispose()`가 early return하는 경로가 있어 대부분 안전하다. 그러나 IOCP 스레드 스케줄링이 지연되는 부하 상황에서 1ms가 충분하지 않을 수 있고, 이 대기가 없어도 논리적으로는 flag로 보호된다. 불필요하거나 신뢰할 수 없는 heuristic이 코드에 남아 있다.

**수정 방향**: `Task.Delay(1)`을 제거하고, `_disposedFlag` 기반 보호만으로 충분함을 주석으로 명시할 것. 또는 `_receiver.Reset()` 후 IOCP 완료를 기다릴 구조적 방법(예: `SemaphoreSlim`)을 도입할 것.

#### 5. `ValidateTokenCacheRequest.Session!` null-forgiving 연산자

**위치**: `GameServer/Actors/User/DbRequest/Cache/ValidateTokenCacheRequest.cs:52`

```csharp
await Session!.EnqueueMessageAsync(new InnerReceiveMessage { Message = result });
```

`Session` 프로퍼티가 `IMessageQueueReceiver?`(nullable)로 선언되어 있어 컴파일러가 null 가능성을 경고하는데, `!`로 강제 무시한다. 생성자에서 항상 설정되므로 실제 null은 아니지만, 동일한 패턴이 다른 `ICacheRequest` 구현체에도 퍼질 경우 실수의 여지가 생긴다.

**수정 방향**: `Session` 타입을 non-nullable `IMessageQueueReceiver`로 선언하거나, null 체크 후 조기 반환 패턴을 사용할 것.

---

### 🔵 제안 (가독성·유지보수성·관례 개선)

#### 6. `LoginSqlRequest` — banned 체크가 password verify 이후

**위치**: `LoginServer/Actors/User/DbRequest/Sql/LoginSqlRequest.cs:42~47`

```csharp
if (row == null || !PasswordHashHelper.Verify(...))
    result = ... InvalidCredentials;
else if (row.Status == 1)
    result = ... Banned;
```

현재 순서에서는 banned 사용자도 올바른 비밀번호를 입력해야 Banned 응답을 받는다. 보안 관점에서는 의도적일 수 있으나(계정 존재 여부 노출 방지), 운영자 의도와 다를 수 있다. 주석으로 의도를 명시하면 좋겠다.

#### 7. `GameConnectResultMessage` — `init` 전용 프로퍼티에 기본값 불일치 가능성

**위치**: `GameServer/Model/Message/GameConnectResultMessage.cs`

```csharp
public class GameConnectResultMessage : IInnerServerMessage
{
    public bool Success { get; init; }
    public GameConnectErrorCode ErrorCode { get; init; }
    public ulong AccountId { get; init; }
    public string UserId { get; init; } = string.Empty;
}
```

`Success = false`가 기본값인데 `ErrorCode`의 기본값은 `Success = 0`이라 기본 생성 시 `Success=false, ErrorCode=Success`로 모순된 상태가 가능하다. 실제로는 항상 명시적으로 설정하므로 버그는 아니지만, `ErrorCode = GameConnectErrorCode.InvalidToken`을 기본값으로 지정하거나 required 키워드를 쓰는 것이 더 안전하다.

#### 8. `DummyClient.UserSession` — `_sender`, `_receiver`가 `readonly`에서 mutable로 변경

**위치**: `DummyClient/Session/UserSession.cs:13~14`

LoginServer → GameServer 전환을 위해 `readonly`가 제거되었다. 이 변경 자체는 필요하나, mutable 필드가 된 만큼 `ConnectToGameServerAsync` 외의 경로에서 실수로 재할당될 위험이 생겼다. 접근 제한을 `private`으로 유지하는 것이 현재 코드에서 이미 지켜지고 있으므로 양호하나, 주석으로 "GameServer 재연결 시에만 교체"를 명시할 것을 권장한다.

---

## 잘된 점

- **Lua 스크립트를 활용한 원자적 GET+DEL**: `ValidateTokenCacheRequest`에서 Redis 6.2 미만 호환을 위해 Lua 스크립트로 원자적 토큰 소비를 구현했다. 토큰 재사용(replay attack) 방지가 자연스럽게 해결된다.
- **`_disposedFlag` CAS 패턴 일관 적용**: GameServer / LoginServer 모두 `_disposedFlag`로 이중 dispose를 방지하는 패턴이 일관되게 유지된다.
- **DummyClient 상태 카운터 설계**: `LoginServerCount`, `GameServerCount`, `DisconnectedCount`를 `Volatile.Read` + `Interlocked` 조합으로 올바르게 관리하며 모니터링 가시성을 높였다.
- **토큰 TTL이 설정 파일로 분리**: `AuthToken:TtlSeconds`를 `appsettings.json`에서 읽어 하드코딩을 피했다.
- **`LoginResultHandler`에서 실패 시 토큰 미발급**: 인증 실패 → 토큰 미생성 경로가 명확하게 분기되어 있다.

---

## 동시성 / 스레드 안전성

- **`GameConnectRequestHandler.AuthTokenPrefix`**: `volatile` 없는 `static string`. 앞서 언급한 대로 write-once 용도라도 가시성 보장이 필요하다.
- **DummyClient `ConnectToGameServerAsync`의 필드 교체**: `_client`, `_sender`, `_receiver` 교체는 Tick 스레드(TickAsync)와 동시에 실행될 수 있다. `Tick` → `TickAsync` → 메시지 처리 중 `_sender.Send()`를 호출하는 경로가 존재하는데, `_sender` 참조를 교체하는 동안 Tick 스레드에서 구 `_sender`를 사용할 수 있다. 현재는 `_disposedFlag=1`로 TickAsync 초반에 early return을 유도하지만, TickAsync가 이미 메시지 루프에 진입한 상태라면 구 _sender로 Send가 발생할 수 있다. 실제로는 DummyClient 특성상(단일 클라이언트 세션) 영향이 제한적이나, 서버 코드에 동일 패턴을 적용하면 위험하다.
- **LoginServer `UserSession._redisDb`**: StackExchange.Redis의 `IDatabase`는 thread-safe하므로 10,000 세션이 같은 인스턴스를 공유해도 IO 안전성은 보장된다.

---

## 요약 체크리스트

| 항목 | 상태 | 비고 |
|------|------|------|
| 버그 없음 | ⚠️ | DummyClient 구 핸들러 Dispose 누락 |
| 예외 처리 적절 | ✅ | try/catch, null 체크 전반적으로 적절 |
| 동시성 안전 | ⚠️ | `AuthTokenPrefix` volatile 누락, DummyClient 필드 교체 race |
| 네이밍 명확 | ✅ | LoginServer/GameServer 분리가 명칭에 잘 반영됨 |
| 불필요한 복잡도 없음 | ⚠️ | `Task.Delay(1)` heuristic, IDatabase 직접 의존 |
