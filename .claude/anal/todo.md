# 코드 약점 분석 및 수정 TODO

> 분석일: 2026-04-21

---

## 🔴 CRITICAL — 서버 크래시 또는 메모리 고갈

- [x] **송신 큐 무제한 성장**
  - 파일: `Library/Network/SenderHandler.cs:16`
  - 문제: `_pendingSendQueue`(ConcurrentQueue)에 용량 제한 없음. 느린 클라이언트 하나로 OOM 크래시 가능
  - 수정: 최대 큐 크기 설정, 초과 시 해당 세션 강제 종료

- [x] **SocketAsyncEventArgs 미해제**
  - 파일: `Server/Acceptor/Acceptor.cs`, `Library/Network/ReceiverHandler.cs`, `Library/Network/SenderHandler.cs`
  - 문제: `IDisposable` 구현체인 `SocketAsyncEventArgs`를 풀에서 재사용만 하고 `Dispose()` 미호출 → 비관리 메모리 누수
  - 수정: 풀 해제 시 `Dispose()` 호출

- [x] **ushort 길이 필드 정수 오버플로우**
  - 파일: `Library/Network/SenderHandler.cs:66`
  - 문제: `ushort bodyLength = (ushort)body.Length;` — 65535 초과 메시지 시 값이 잘려 프로토콜 파싱 완전 붕괴
  - 수정: 최대 메시지 크기 검증 후 초과 시 예외 처리

- [x] **프로토콜 Length 상한 검증 없음 (DoS)**
  - 파일: `Library/Network/ReceiveParser.cs:45`
  - 문제: 악성 클라이언트가 헤더에 `bodySize=65535` 전송 후 100바이트만 보내면 버퍼 슬롯 영구 독점
  - 수정: `_bodySize > MaxBufferSize` 시 즉시 연결 끊기

- [x] **Fire-and-Forget 예외 무시**
  - 파일: `Library/Network/ReceiverHandler.cs:58`
  - 문제: `_ = _messageQueueWorker.EnqueueAsync(...)` — 예외 발생 시 로그도 없이 묻힘
  - 수정: `async void` 래퍼 또는 예외 캐치 후 로깅

---

## 🟠 HIGH — 고부하 시 크래시 또는 데이터 손실

- [x] **소켓 Null 레이스 컨디션**
  - 파일: `Library/Network/SenderHandler.cs:47,75`
  - 문제: `if (_socket == null)` 체크 후 다른 스레드에서 `Dispose()`로 `_socket = null` 가능 → NullReferenceException
  - 수정: 로컬 변수에 캡처 후 사용, 또는 lock 추가

- [x] **세션 재사용 타이밍 레이스** (TimerScheduleManager 내부 lock으로 안전 확인 — 실제 레이스 없음)
  - 파일: `Server/Actors/User/UserSession.cs:61`, `Library/Worker/TickThreadWorker.cs`
  - 문제: 풀 반환·재초기화 중 Tick 스레드가 이전 세션 `Tick()` 실행 중일 수 있음 → 해제된 객체 접근
  - 수정: 세션 반환 전 Tick 스레드에서 제거 완료 보장

- [x] **서버 종료 시 레이스 컨디션**
  - 파일: `Server/Actors/UserObjectPoolManager.cs:48`
  - 문제: `_stopping = true` 설정과 동시에 Acceptor 스레드의 `AcceptUser()` 호출 가능 → 일부 세션 미정리
  - 수정: `_stopping` 플래그를 `volatile` 또는 `Interlocked`로 처리, Acceptor 중단 후 세션 정리

- [x] **이벤트 핸들러 미구독 해제** (CRITICAL #2에서 수정 완료)
  - 파일: `Library/Network/ReceiverHandler.cs:27`
  - 문제: `_receiveEventArgs.Completed += OnReceiveCompleted` 등록 후 `-=` 없음 → Dispose 후에도 GC 수거 불가
  - 수정: `Dispose()`에서 `-= OnReceiveCompleted` 추가

- [x] **풀 고갈 시 서버 프로세스 사망**
  - 파일: `Library/ObjectPool/ObjectPool.cs:34`
  - 문제: 세션 풀 고갈 시 `InvalidOperationException` throw → Acceptor에서 미처리 시 서버 프로세스 종료
  - 수정: 예외 대신 `null` 반환 후 Acceptor에서 연결 거부 처리

---

## 🟡 MEDIUM — 성능 저하 또는 장기 운영 문제

- [x] **워커 스레드 수 부족**
  - 파일: `Library/ContInfo/ThreadConstInfo.cs:5`
  - 문제: `MaxUserThreadCount = 4`로 최대 10,000 세션 처리 → Tick 하나 지연 시 전체 세션 굶음
  - 수정: 하드코딩 대신 CPU 코어 수 기반 동적 설정 고려

- [x] **유저별 타이머 무제한 등록**
  - 파일: `Library/Timer/TimerScheduleManager.cs`
  - 문제: 타이머 리스트 용량 제한 없음 → 버그 있는 핸들러가 타이머 무한 등록 시 세션당 메모리 고갈
  - 수정: 세션당 최대 타이머 수 제한 추가

- [x] **소켓 비우아한 종료 (TCP RST)**
  - 파일: `Library/Network/ReceiverHandler.cs:77`
  - 문제: `SocketShutdown.Both` 즉시 호출 → FIN 대신 RST 전송, 전송 중 데이터 유실 가능
  - 수정: `SocketShutdown.Send` → 수신 완료 대기 → 소켓 닫기 순서로 변경

- [x] **연결 타임아웃 없음**
  - 파일: `Server/Acceptor/Acceptor.cs`
  - 문제: 접속 후 아무것도 하지 않는 클라이언트를 무한 유지 → 슬롯 고갈 공격 가능
  - 수정: 미활동 세션 타임아웃(예: 30초) 추가

- [x] **연결 속도 제한 없음**
  - 파일: `Server/Acceptor/Acceptor.cs`
  - 문제: 초당 수천 개 연결 요청 무제한 수락 → SYN Flood 취약
  - 수정: IP당 연결 속도 제한 또는 최대 대기 연결 수 제한

---

## 수정 우선순위 요약

| 순위 | 항목 | 위험도 |
|------|------|--------|
| 1 | 송신 큐 용량 제한 | OOM 크래시 |
| 2 | 풀 고갈 예외 처리 | 서버 프로세스 사망 |
| 3 | ushort 오버플로우 검사 | 프로토콜 전체 붕괴 |
| 4 | 프로토콜 Length 상한 검증 | DoS 벡터 |
| 5 | 소켓 Null 레이스 | 고부하 크래시 |
| 6 | 이벤트 핸들러 -= 추가 | 장기 메모리 누수 |
| 7 | 풀 반환 시 Dispose 호출 | 비관리 메모리 누수 |
| 8 | 세션 재사용 타이밍 레이스 | 고부하 크래시 |
