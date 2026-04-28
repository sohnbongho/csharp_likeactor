# Channel 배압(Backpressure) 적용

날짜: 2026-04-28

## 목표

- 메시지 채널을 `Unbounded → Bounded`로 교체해 채널 무한 증가(OOM) 위험을 제거한다
- 채널이 가득 찼을 때 해당 세션을 강제 종료해 폭주 클라이언트를 빠르게 차단한다
- `ReceiverHandler`에서 `EnqueueMessageAsync` 실패(false) 시 envelope을 풀로 반환해 메모리 누수를 막는다

---

## 현황 분석

### 문제 코드 — `UserSession` (Server & DummyClient 동일)

```csharp
_messageChannel = Channel.CreateUnbounded<IMessageQueue>(
    new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false });
```

`Unbounded`이므로 `TryWrite`가 항상 `true`를 반환한다.  
네트워크 수신 속도 > 틱 처리 속도 상황이 지속되면 채널 크기가 무제한 증가한다.

### 문제 코드 — `ReceiverHandler.OnReceiveCompleted`

```csharp
foreach (var msg in messages)
{
    await _receiver.EnqueueMessageAsync(RemoteReceiveMessage.Rent(msg));
    // ↑ 반환값 무시 — 실패해도 다음 메시지로 진행
}
```

`EnqueueMessageAsync`가 `false`를 반환해도 아무 처리 없이 계속 수신한다.  
결과적으로 이미 렌트된 `RemoteReceiveMessage` envelope이 풀로 반환되지 않아 누수가 발생한다.

### 처리 속도 상한

```
MaxMessagesPerTick = 50 메시지 / 100ms = 초당 500 메시지/세션
```

정상 게임 트래픽은 수 메시지/초 수준이므로, 채널 용량을 1,000으로 잡으면  
**2초분 버스트**를 허용하면서도 폭주는 차단한다.

---

## 설계 방향

### 핵심 아이디어

1. `CreateUnbounded` → `CreateBounded(MaxMessageChannelCapacity)` + `FullMode = DropWrite`  
   → 가득 찼을 때 `TryWrite`가 즉시 `false` 반환
2. `ReceiverHandler`에서 `false` 수신 시 → envelope 풀 반환 → `Disconnected()` 호출  
   → 폭주 세션 즉시 강제 종료

### BoundedChannelFullMode 선택 근거

| 모드 | 동작 | 선택 이유 |
|------|------|-----------|
| `DropWrite` | TryWrite false 반환, 메시지 미기록 | 호출자가 즉시 감지해 세션 차단 가능 |
| `Wait` | 채널에 공간이 생길 때까지 async 대기 | 폭주 세션이 워커를 점령, 다른 세션 지연 |
| `DropOldest` | 오래된 메시지를 버리고 새 메시지 기록 | 이미 처리 대기 중인 메시지를 무단 폐기 |

`DropWrite` + 세션 종료가 게임 서버에 가장 적합하다.

### 고려했으나 제외한 대안

- **별도 메시지 수신 레이트 리미터 추가**: `MaxConnectionsPerIpPerMinute`와 유사한 per-session 메시지 카운터 방식. 구현이 복잡하고 채널 OOM 자체를 막지는 못해 제외.
- **채널 용량을 `MaxMessagesPerTick`과 동일하게 설정(50)**: 단 1틱 분량만 허용해 정상 트래픽 스파이크도 차단할 수 있어 제외.

---

## 변경 대상 파일

| 파일 | 변경 내용 |
|------|-----------|
| `Library/ContInfo/SessionConstInfo.cs` | `MaxMessageChannelCapacity = 1000` 상수 추가 |
| `Library/Network/ReceiverHandler.cs` | `EnqueueMessageAsync` 반환값 검사 + 실패 시 envelope 반환 + Disconnected 호출 |
| `Server/Actors/User/UserSession.cs` | `CreateUnbounded` → `CreateBounded(MaxMessageChannelCapacity, DropWrite)` |
| `DummyClient/Session/UserSession.cs` | 동일 — `CreateUnbounded` → `CreateBounded(MaxMessageChannelCapacity, DropWrite)` |

---

## 단계별 작업 계획

1. **`SessionConstInfo.cs` 수정**  
   `MaxMessageChannelCapacity = 1000` 상수 추가

2. **`Server/Actors/User/UserSession.cs` 수정**  
   `Channel.CreateUnbounded` → `Channel.CreateBounded` 교체

3. **`DummyClient/Session/UserSession.cs` 수정**  
   동일 변경

4. **`Library/Network/ReceiverHandler.cs` 수정**  
   `EnqueueMessageAsync` 반환값 검사 로직 추가

5. **빌드 확인**  
   `dotnet build Server/Server.sln -c Release`

---

## 최종 구현 형태 (참고)

### UserSession (Server & DummyClient 동일)

```csharp
_messageChannel = Channel.CreateBounded<IMessageQueue>(
    new BoundedChannelOptions(SessionConstInfo.MaxMessageChannelCapacity)
    {
        SingleReader = true,
        AllowSynchronousContinuations = false,
        FullMode = BoundedChannelFullMode.DropWrite
    });
```

### ReceiverHandler.OnReceiveCompleted

```csharp
var messages = _parser.Parse(e.BytesTransferred);
foreach (var msg in messages)
{
    var envelope = RemoteReceiveMessage.Rent(msg);
    if (!await _receiver.EnqueueMessageAsync(envelope))
    {
        RemoteReceiveMessage.Return(envelope);
        Disconnected();
        return;
    }
}

StartReceive();
```

---

## 주의사항 / 위험 요소

- **DummyClient는 풀 반환 없이 Disconnect**: DummyClient `UserSession`은 반환 대상 envelope이 없고, Dispose 시 채널을 비우지 않는다. Bounded로 변경해도 DummyClient에서 채널이 가득 찰 일은 없지만, 코드 일관성 유지 차원에서 변경한다.
- **`Disconnected()` 중복 호출 가능성**: `ReceiverHandler.Disconnected()`는 이미 null 체크 후 `_receiver.Disconnect()`를 호출하며, `UserSession.Dispose()`는 `Interlocked.CompareExchange`로 1회만 실행된다. 중복 호출 안전.
- **채널 용량 1,000의 적합성**: `MaxMessagesPerTick(50) × 20틱(2초)` 기준. 핸들러 처리 시간이 느려 틱이 100ms를 크게 초과하는 경우 실제 버퍼는 더 짧아질 수 있다.

---

## 미결 질문

1. **`MaxMessageChannelCapacity` 값**: 1,000(2초분 버스트)이 적절한지, 더 크게(예: 2,000) 잡기를 원하는지 확인 부탁드립니다.
