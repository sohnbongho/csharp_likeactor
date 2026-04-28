# WorldThread / LobbyThread 분석

날짜: 2026-04-28

## 요약

`LobbyThreadManager`는 월드 입장 전 유저를 처리하는 단일 전용 스레드이고, `WorldThreadManager`는 월드별로 유저를 분산 처리하는 N개의 스레드 풀이다. 두 매니저 모두 내부적으로 `TickThreadWorker`를 사용하며, 유저는 `WorldId`에 따라 어느 하나에만 귀속된다.

---

## 대상

| 파일 | 클래스 |
|------|--------|
| `Library/World/LobbyThreadManager.cs` | `LobbyThreadManager` |
| `Library/World/WorldThreadManager.cs` | `WorldThreadManager` |
| `Library/Worker/TickThreadWorker.cs` | `TickThreadWorker` |
| `Library/Worker/Interface/ITickable.cs` | `ITickable` |
| `Server/Actors/UserObjectPoolManager.cs` | `UserObjectPoolManager` |
| `Server/Actors/User/UserSession.cs` | `UserSession` |
| `Server/TcpServer.cs` | `TcpServer` |
| `Library/ContInfo/ThreadConstInfo.cs` | `ThreadConstInfo` |

---

## 흐름 분석

### 서버 시작

```
TcpServer 생성
  → LobbyThreadManager 생성   (TickThreadWorker id=0 × 1개)
  → WorldThreadManager 생성   (TickThreadWorker id=1..N × N개)
  → 두 매니저 모두 .Start()
```

N = `max(4, Environment.ProcessorCount)` — CPU 코어 수 기반, 최소 4 보장.

### 유저 접속 시

```
TCPAcceptor.OnAccepted(socket)
  → UserObjectPoolManager.AcceptUser(socket)
      → 세션 풀에서 UserSession 렌트
      → session.Reinitialize(newSessionId)
      → _activeSessions에 등록
      → session.Bind(socket)           // 수신 루프 시작
      → LobbyThreadManager.Add(session) // 항상 로비 스레드로 배치
```

접속 직후 `WorldId = 0`이며, 로비 스레드(`id=0` TickThreadWorker)에 배치된다.

### Tick 루프 (100ms 주기)

```
TickThreadWorker.RunAsync()
  루프 반복:
    foreach session in _sessions.Values
      → session.TickAsync()
            → Channel에서 메시지 최대 N건 드레인
            → MessageQueueDispatcher.OnRecvMessageAsync() 호출
            → TimerScheduleManager.Tick()
    잔여 시간 = 100ms - 경과 시간
    Task.Delay(잔여시간)
```

### 월드 이동

```
핸들러 내부 (= 현재 tick 스레드 내)
  → session.MoveToWorld(worldId)
      → UserObjectPoolManager.MoveToWorld(session, worldId)
          → 현재 스레드(Lobby or World)에서 Remove
          → session.SetWorldId(worldId)
          → 새 스레드(World or Lobby)에 Add
```

**중요**: `MoveToWorld`는 반드시 핸들러(즉 tick 스레드) 실행 도중에 호출해야 한다. 이 제약 덕분에 Remove→Add 사이에 해당 세션의 `TickAsync`가 재진입하지 않는다.

### 유저 접속 해제

```
session.Disconnect()
  → session.Dispose()
      → UserObjectPoolManager.RemoveUser(session)
          → WorldId == 0 → LobbyThreadManager.Remove
          → WorldId != 0 → WorldThreadManager.Remove
          → _activeSessions에서 제거
          → 세션 풀로 반환
```

---

## 구조 분석

### LobbyThreadManager vs WorldThreadManager

| 구분 | LobbyThreadManager | WorldThreadManager |
|------|--------------------|--------------------|
| 내부 워커 수 | 1개 (`TickThreadWorker id=0`) | N개 (`id=1..N`) |
| 배치 기준 | 모든 로비 유저 공통 | `worldId % N` |
| 설계 의도 | 월드 입장 전은 동시성 간섭 없음, 단일 스레드로 단순화 | 같은 월드의 유저는 같은 스레드 → 유저 간 전투 시 추가 잠금 불필요 |

### TickThreadWorker 설계

- 세션 저장소: `ConcurrentDictionary<ulong, ITickable>` → Add/Remove가 tick 루프와 동시에 일어나도 안전
- 취소: `CancellationTokenSource` → `StopAsync()`가 `Cancel()`하고 task를 `await`로 정리
- tick 오버런 보정: `Stopwatch`로 실제 경과 시간을 측정해 남은 시간만 `Task.Delay` → 틱 주기 드리프트 방지

### ITickable 인터페이스

```csharp
interface ITickable {
    ValueTask TickAsync();
    ulong SessionId { get; }
}
```

`UserSession`이 구현하며, `SessionId`를 키로 `ConcurrentDictionary`에 등록된다. `ValueTask` 사용으로 대부분 메시지가 없는 경우의 힙 할당을 최소화한다.

---

## 동시성 / 스레드 안전성

### 채널 기반 메시지 격리

```
네트워크 수신 스레드  →  Channel<IMessageQueue>.Writer.TryWrite()
tick 스레드           ←  Channel<IMessageQueue>.Reader.TryRead()
```

`SingleReader = true` 옵션으로 tick 스레드만 읽는다고 선언 → 내부 동기화 비용 최소화.

### 세션 귀속 보장

`WorldId % N` 고정 매핑으로 같은 월드 유저는 항상 같은 `TickThreadWorker`에서 실행된다. 따라서 동일 월드 내 유저 간 상태 접근 시 추가 락이 필요 없다.

### Add/Remove 경쟁

`TickThreadWorker._sessions`는 `ConcurrentDictionary`이므로 tick 루프 중 `foreach` 도중에 다른 스레드가 Add/Remove해도 안전하다 (스냅샷이 아닌 실시간 반영).

### MoveToWorld 재진입 위험

`MoveToWorld`를 tick 스레드 바깥(예: 네트워크 수신 스레드)에서 호출하면 Remove 직후 TickAsync가 재진입해 엉뚱한 스레드에서 처리될 수 있다. 코드 주석으로 "반드시 현재 tick 스레드 내에서 호출"이라는 계약이 명시되어 있으나, 컴파일 타임 강제 수단은 없다.

### ShutdownAll 경쟁

`ShutdownAll`은 `_stopping = true` 설정 후 활성 세션을 `Disconnect` 호출하지만, 이미 체결된 Accept와의 경쟁은 `_shutdownLock`으로 보호된다.

---

## 주요 발견

1. **Lobby = id 0, World = id 1..N 명시적 분리**: `TickThreadWorker` 생성자에 id를 넘겨 로그/디버깅 시 로비와 월드 스레드를 구분할 수 있다.

2. **MaxMessagesPerTick 제한**: `UserSession.TickAsync`는 한 tick당 메시지 처리량을 `SessionConstInfo.MaxMessagesPerTick`으로 제한한다. 폭주 클라이언트가 다른 유저의 tick을 굶기지 않도록 보호하는 공정성 장치다.

3. **MoveToWorld 호출 계약이 런타임 보장 없음**: `// 반드시 현재 tick 스레드 내에서 호출` 주석에 의존한다. 미래에 핸들러를 `async`로 확장할 경우 `await` 이후 재개 스레드가 달라질 수 있어 위반 가능성이 있다.

4. **LobbyThread는 단일 스레드**: 접속 유저 수가 많아지면 로비 스레드가 병목이 될 수 있다. 로비 체류 시간이 짧을수록 영향이 적다.

5. **World 스레드 수 런타임 결정**: `Environment.ProcessorCount` 기반이라 배포 환경에 따라 스레드 수가 달라진다. 월드 분배(`worldId % N`) 결과도 환경마다 다르므로 멀티 인스턴스 배포 시 유의해야 한다.

---

## 관련 파일

| 파일 | 역할 |
|------|------|
| `Library/World/LobbyThreadManager.cs` | 단일 로비 스레드 래퍼 |
| `Library/World/WorldThreadManager.cs` | 멀티 월드 스레드 풀 매니저 |
| `Library/Worker/TickThreadWorker.cs` | 실제 tick 루프 실행체 |
| `Library/Worker/Interface/ITickable.cs` | tick 대상 인터페이스 |
| `Server/Actors/UserObjectPoolManager.cs` | 유저 생명주기 + 스레드 배치 |
| `Server/Actors/User/UserSession.cs` | tick/메시지/타이머 통합 세션 |
| `Server/TcpServer.cs` | 두 매니저의 생성·시작·종료 조율 |
| `Library/ContInfo/ThreadConstInfo.cs` | 스레드 수·틱 주기 상수 |
