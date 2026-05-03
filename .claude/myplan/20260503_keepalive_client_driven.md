# KeepAlive 클라이언트 주도 방식으로 전환

날짜: 2026-05-03

## 목표

- 클라이언트가 3초마다 `KeepAliveRequest`를 서버로 전송
- 서버는 10초 동안 `KeepAliveRequest`를 받지 못하면 해당 세션 강제 종료
- 서버가 응답으로 보내던 `KeepAliveNoti` 제거

---

## 현황 분석

### 현재 흐름 (서버 주도 핑퐁)

```
Client → Server: KeepAliveRequest
Server → Client: KeepAliveNoti       ← KeepAliveRequestHandler에서 즉시 응답
Client → Server: KeepAliveRequest    ← KeepAliveNotiHandler에서 즉시 재전송
(무한 반복)
```

첫 KeepAlive는 `LoginResponseHandler`가 로그인 성공 직후 `Send(KeepAliveRequest)` 호출로 시작.

### 관련 파일

| 파일 | 현재 역할 |
|------|-----------|
| `Server/Actors/User/Handler/Remote/KeepAliveRequestHandler.cs` | Request 수신 → Noti 즉시 응답 |
| `DummyClient/Session/Handler/Remote/KeepAliveNotiHandler.cs` | Noti 수신 → Request 즉시 재전송 |
| `DummyClient/Session/Handler/Remote/LoginResponseHandler.cs` | 로그인 성공 시 최초 KeepAlive 트리거 |
| `Server/Actors/User/UserSession.cs` | TimerScheduleManager 보유, TickAsync 100ms 주기 |
| `DummyClient/Session/UserSession.cs` | TickAsync 100ms 주기, TimerScheduleManager 없음 |
| `Library/ContInfo/SessionConstInfo.cs` | 상수 정의 위치 |

### 현재 문제점

- 핑퐁 구조라 클라이언트 응답 지연이 곧 서버 발신 지연으로 전파됨
- 서버 측 비활성 세션 감지 수단이 없음 (클라이언트가 패킷을 끊어도 소켓 오류 전까지 세션 유지)

---

## 설계 방향

### 핵심 아이디어

- **클라이언트**: `TickAsync()`(100ms 주기)에서 마지막 전송 시각을 비교해 3초 경과 시 `KeepAliveRequest` 전송
- **서버**: `KeepAliveRequestHandler`에서 수신 시각만 갱신. `TickAsync()` 끝에서 인증된 세션에 한해 10초 초과 여부 확인 후 Disconnect

### 선택한 접근법

`TickAsync()` 내 타임스탬프 비교 방식.

- `TimerScheduleManager`는 일회성 혹은 지연 콜백용이며, 타이머 취소 기능이 없어 슬라이딩 데드라인 구현이 번거로움
- `Stopwatch.GetTimestamp()`를 `long` 필드로 보관하고 Tick마다 비교하면 코드가 단순하고 GC 부담 없음
- 서버의 쓰기(`UpdateKeepAlive`)와 읽기(타임아웃 체크) 모두 동일한 `TickThreadWorker` 스레드에서 실행되므로 락 불필요

### 제외한 대안

- **TimerScheduleManager 활용**: 취소 API 없어 슬라이딩 타임아웃 구현 시 만료된 타이머가 중복 Disconnect 호출 위험
- **별도 WatchdogThread**: 스레드 추가 비용 및 세션 상태 공유를 위한 lock 필요 — 오버엔지니어링

---

## 변경 대상 파일

| 파일 | 변경 내용 |
|------|-----------|
| `Library/ContInfo/SessionConstInfo.cs` | `KeepAliveIntervalSeconds = 3`, `KeepAliveTimeoutSeconds = 10` 상수 추가 |
| `Server/Actors/User/UserSession.cs` | `_lastKeepAliveReceivedAt` 필드 추가, `UpdateKeepAlive()` 메서드 추가, `TickAsync()` 끝에 타임아웃 체크 추가, `OnAuthenticated()` / `Reinitialize()` 에서 초기화 |
| `Server/Actors/User/Handler/Remote/KeepAliveRequestHandler.cs` | Noti 응답 제거, `session.UpdateKeepAlive()` 호출로 교체 |
| `DummyClient/Session/UserSession.cs` | `_isAuthenticated` 플래그, `_lastKeepAliveSentAt` 필드, `OnAuthenticated()` 메서드 추가, `TickAsync()`에서 3초 경과 시 전송 |
| `DummyClient/Session/Handler/Remote/LoginResponseHandler.cs` | 즉시 Send(KeepAliveRequest) 제거 → `session.OnAuthenticated()` 호출로 교체 |
| `DummyClient/Session/Handler/Remote/KeepAliveNotiHandler.cs` | 삭제 (서버가 Noti를 더 이상 전송하지 않음) |

---

## 단계별 작업 계획

### 1단계: 상수 추가
`SessionConstInfo.cs`에 아래 두 상수 추가:
```
KeepAliveIntervalSeconds = 3    // 클라이언트 전송 주기
KeepAliveTimeoutSeconds  = 10   // 서버 타임아웃
```

### 2단계: 서버 UserSession 수정
- `_lastKeepAliveReceivedAt` (`long`, `Stopwatch.GetTimestamp()`) 필드 추가
- `UpdateKeepAlive()` 메서드 추가: `_lastKeepAliveReceivedAt = Stopwatch.GetTimestamp()`
- `OnAuthenticated()` 내부에서 `_lastKeepAliveReceivedAt` 초기화
- `Reinitialize()`에서 `_lastKeepAliveReceivedAt = 0` 초기화
- `TickAsync()` 끝(채널 소진 후)에 아래 로직 추가:
  ```
  if (IsAuthenticated)
      elapsed = (now - _lastKeepAliveReceivedAt) / Stopwatch.Frequency
      if elapsed > KeepAliveTimeoutSeconds → 로그 + Disconnect()
  ```

### 3단계: 서버 KeepAliveRequestHandler 수정
- `receiver.Send(KeepAliveNoti)` 제거
- `if (receiver is not UserSession session) return` 추가
- `session.UpdateKeepAlive()` 호출

### 4단계: 클라이언트 UserSession 수정
- `_isAuthenticated` (`bool`) 필드 추가
- `_lastKeepAliveSentAt` (`long`) 필드 추가
- `OnAuthenticated()` 메서드 추가:
  ```
  _isAuthenticated = true;
  _lastKeepAliveSentAt = Stopwatch.GetTimestamp();
  ```
- `TickAsync()` 내 채널 소진 후 아래 로직 추가:
  ```
  if (_isAuthenticated)
      elapsed = (now - _lastKeepAliveSentAt) / Stopwatch.Frequency
      if elapsed >= KeepAliveIntervalSeconds
          _lastKeepAliveSentAt = now
          Send(KeepAliveRequest)
  ```
- `Dispose()` 에서 `_isAuthenticated = false` 초기화

### 5단계: 클라이언트 LoginResponseHandler 수정
- `receiver.Send(KeepAliveRequest)` 즉시 전송 제거
- `session.OnAuthenticated()` 호출로 교체 (이후 TickAsync에서 타이머로 첫 전송)

### 6단계: 클라이언트 KeepAliveNotiHandler.cs 삭제

### 7단계: 빌드 검증
```
dotnet build Server/Server.sln -c Release
```

---

## 주의사항 / 위험 요소

- **최초 KeepAlive 전송 타이밍**: `OnAuthenticated()` 호출 시 `_lastKeepAliveSentAt`를 현재 시각으로 초기화하므로, 첫 전송은 로그인 성공 후 3초 뒤. 즉시 보내고 싶다면 `0`으로 초기화하면 첫 Tick에 바로 전송됨 — 요구사항에 따라 결정 필요.
- **서버 타임아웃 시작 시점**: `OnAuthenticated()` 시 초기화하므로, 로그인 직후부터 10초 카운트 시작. 클라이언트가 3초 뒤 첫 패킷을 보내므로 정상 흐름에서는 문제없음.
- **KeepAliveNoti proto 잔존**: `message.proto`에서 `KeepAliveNoti`를 제거하면 clean하지만 protoc 재실행이 필요. 이번 작업 범위에서는 proto 수정 없이 메시지만 미사용으로 남김.
- **DummyClient Dispose 시 `_isAuthenticated` 초기화**: DummyClient의 UserSession은 풀에서 재사용되지 않지만, 방어적으로 Dispose에서 초기화하는 것이 안전.

---

## 미결 질문

1. **첫 KeepAlive 전송 타이밍**: 로그인 성공 직후 즉시 보낼까요, 아니면 3초 후에 첫 전송을 할까요?
