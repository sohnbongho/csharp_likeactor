# LobbyThreadManager N개 워커 확장

날짜: 2026-04-28

## 목표

- `LobbyThreadManager`를 단일 `TickThreadWorker`에서 N개 워커 배열로 확장한다
- 로비 체류 유저가 많을 때 발생하는 단일 스레드 병목을 해소한다
- 호출자(TcpServer, UserObjectPoolManager, DummyClient 등)는 코드 변경 없이 동작해야 한다

---

## 현황 분석

### LobbyThreadManager (현재)

```csharp
public class LobbyThreadManager
{
    private readonly TickThreadWorker _worker = new(0);

    public void Start() => _worker.Start();
    public Task StopAsync() => _worker.StopAsync();
    public void Add(ITickable session) => _worker.Add(session);
    public void Remove(ITickable session) => _worker.Remove(session);
}
```

- 단일 `TickThreadWorker(id=0)` 하나만 사용
- 모든 로비 유저가 동일 스레드에서 tick/message 처리됨

### 호출자 현황

| 파일 | 호출 메서드 |
|------|-------------|
| `Server/TcpServer.cs` | `Start()`, `StopAsync()` |
| `Server/Actors/UserObjectPoolManager.cs` | `Add(session)`, `Remove(session)` |
| `DummyClient/TcpDummyClient.cs` | `Start()`, `StopAsync()` |
| `DummyClient/Session/UserObjectPoolManager.cs` | `Add(session)`, `Remove(session)` |

**핵심 관찰**: `Add`/`Remove` 시그니처가 이미 `ITickable`을 받고, `ITickable`은 `SessionId`를 포함한다.  
→ 분배 키(`sessionId % N`)를 `LobbyThreadManager` 내부에서 계산할 수 있으므로 **호출자 코드 변경이 불필요**하다.

### WorldThreadManager (참조 모델)

```csharp
public class WorldThreadManager
{
    private readonly TickThreadWorker[] _workers;
    private readonly ulong _workerCount;

    public void Add(ITickable session, ulong worldId) => GetWorker(worldId).Add(session);
    public void Remove(ITickable session, ulong worldId) => GetWorker(worldId).Remove(session);
    private TickThreadWorker GetWorker(ulong worldId) => _workers[worldId % _workerCount];
}
```

`WorldThreadManager`는 `worldId`를 분배 키로 사용한다.  
로비는 `WorldId`가 없으므로 `sessionId`를 분배 키로 사용하면 된다.

### ThreadConstInfo (현재)

```csharp
public static class ThreadConstInfo
{
    public static readonly int MaxWorldThreadCount = Math.Max(4, Environment.ProcessorCount);
    public const int TickMillSecond = 100;
}
```

`MaxLobbyThreadCount` 상수가 없다 → 추가 필요.

---

## 설계 방향

### 핵심 아이디어

`WorldThreadManager`와 동일한 구조를 `LobbyThreadManager`에 적용한다.  
분배 키만 `worldId → sessionId`로 바꾸고, 공개 API 시그니처는 유지한다.

```
변경 전: Add(ITickable)   → _worker.Add(obj)
변경 후: Add(ITickable)   → _workers[obj.SessionId % N].Add(obj)
```

### 워커 ID 방침

현재 `WorldThreadManager`는 `id = 1..N`을 사용한다.  
로비 워커에 **음수 ID**를 부여한다 (`id = -1, -2, ...`).  
로그에서 `[Worker#-1]`처럼 로비 워커임을 직관적으로 구분할 수 있다.

```csharp
// 로비 워커: id = -1, -2, ..., -MaxLobbyThreadCount
_workers[i] = new TickThreadWorker(-(i + 1));

// 월드 워커: id = 1, 2, ..., MaxWorldThreadCount  (기존 유지)
_workers[i] = new TickThreadWorker(i + 1);
```

### MaxLobbyThreadCount 기본값

로비 체류 시간이 월드 체류보다 짧으므로, 로비 스레드 수를 월드 스레드 수보다 작게 설정한다.

```csharp
public static readonly int MaxLobbyThreadCount = Math.Max(2, Environment.ProcessorCount / 2);
```

CPU 코어가 8개이면 → 로비 4개, 월드 8개.

### 고려했으나 제외한 대안

| 대안 | 제외 이유 |
|------|-----------|
| `Add(ITickable, ulong sessionId)` — 호출자에 sessionId 전달 | 이미 `ITickable.SessionId`로 내부에서 알 수 있으므로 API 노출 불필요 |
| ID를 `MaxWorldThreadCount + 1`부터 시작 | 두 상수 간 결합도 생김 |
| `TickThreadWorker`에 문자열 레이블 추가 | 로비·월드 구분은 음수 ID로 충분, 변경 범위 증가 |
| 로비도 `worldId` 기반 분배 | 로비 유저는 WorldId=0으로 모두 동일 → 분배 불가 |

---

## 변경 대상 파일

| 파일 | 변경 내용 |
|------|-----------|
| `Library/ContInfo/ThreadConstInfo.cs` | `MaxLobbyThreadCount` 상수 추가 |
| `Library/World/LobbyThreadManager.cs` | 단일 워커 → `TickThreadWorker[]` 배열로 교체, 내부 `GetWorker(sessionId)` 추가 |

**변경 불필요**

| 파일 | 이유 |
|------|------|
| `Server/TcpServer.cs` | 공개 API 시그니처 유지 |
| `Server/Actors/UserObjectPoolManager.cs` | 공개 API 시그니처 유지 |
| `DummyClient/TcpDummyClient.cs` | 공개 API 시그니처 유지 |
| `DummyClient/Session/UserObjectPoolManager.cs` | 공개 API 시그니처 유지 |
| `Library/Worker/TickThreadWorker.cs` | 변경 없음 |

---

## 단계별 작업 계획

1. **`ThreadConstInfo.cs` 수정**  
   `MaxLobbyThreadCount = Math.Max(2, Environment.ProcessorCount / 2)` 상수 추가

2. **`LobbyThreadManager.cs` 리팩터링**  
   - `_worker` 단일 필드 → `_workers[]` 배열 + `_workerCount` 필드로 교체  
   - 생성자: 배열 초기화, 각 워커를 음수 id(`-(i+1)`)로 생성  
   - `Start()`: 모든 워커 순회 Start  
   - `StopAsync()`: 모든 워커 순회 StopAsync (순차 또는 병렬)  
   - `Add(ITickable)`, `Remove(ITickable)`: `GetWorker(obj.SessionId)` 경유  
   - `GetWorker(ulong sessionId)` 내부 메서드 추가: `_workers[sessionId % _workerCount]`

3. **빌드 확인**  
   `dotnet build Server/Server.sln -c Release`

4. **통합 테스트**  
   `DummyClient`로 접속·KeepAlive 교환·정상 종료 확인

---

## 최종 구현 형태 (참고)

```csharp
public class LobbyThreadManager
{
    private readonly TickThreadWorker[] _workers;
    private readonly ulong _workerCount;

    public LobbyThreadManager()
    {
        _workerCount = (ulong)ThreadConstInfo.MaxLobbyThreadCount;
        _workers = new TickThreadWorker[_workerCount];
        for (int i = 0; i < (int)_workerCount; i++)
            _workers[i] = new TickThreadWorker(-(i + 1)); // 음수 ID = 로비 워커 식별
    }

    public void Start()
    {
        foreach (var w in _workers) w.Start();
    }

    public async Task StopAsync()
    {
        foreach (var w in _workers) await w.StopAsync();
    }

    public void Add(ITickable session) => GetWorker(session.SessionId).Add(session);
    public void Remove(ITickable session) => GetWorker(session.SessionId).Remove(session);

    private TickThreadWorker GetWorker(ulong sessionId) => _workers[sessionId % _workerCount];
}
```

---

## 주의사항 / 위험 요소

- **SessionId 연속성**: `SessionIdGenerator`가 1부터 순차 증가한다면 `sessionId % N` 분배가 고르다. UUID 등 랜덤 값이어도 균등 분배된다. 확인 불필요.
- **MoveToWorld 재진입 없음**: 로비 내부에서 워커 간 세션 이동은 없다(같은 로비에 계속 있거나, 월드로 이동한다). `Remove`는 동일 워커에서 호출되므로 경쟁 없음.
- **StopAsync 순차 처리**: 현재 `WorldThreadManager`도 `foreach await` 방식이므로 일관성 유지. 필요 시 병렬(`Task.WhenAll`)로 개선 가능하나 이번 범위 외.

---

## 미결 질문

1. **`MaxLobbyThreadCount` 기본값**: `Math.Max(2, ProcessorCount / 2)` 안과 `MaxWorldThreadCount`와 동일한 값, 둘 중 어느 쪽을 선호하시나요?
2. **로비 워커 ID 표기**: 음수(`-1, -2`) 대신 다른 표기 방식(예: `1000, 1001`)을 원하시면 알려주세요.
