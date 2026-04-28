# 레이트 리미터 IP 정책 개선

날짜: 2026-04-28

## 목표

- CGNAT 환경(다수 유저가 동일 IP 공유)에서 정상 유저가 차단되지 않도록 임계값을 조정한다
- 신뢰 IP(내부 서버, 로컬호스트)는 레이트 리밋을 완전히 우회한다
- 반복 위반 IP에 한시적 밴을 적용해 재연결 공격을 차단한다
- IPv4-mapped IPv6(`::ffff:x.x.x.x`) 주소를 정규화해 동일 IP의 이중 카운팅을 막는다

---

## 현황 분석

### 현재 구현 (`Server/Acceptor/Acceptor.cs`)

```csharp
private bool IsRateLimited(Socket socket)
{
    var remoteIp = (socket.RemoteEndPoint as IPEndPoint)?.Address.ToString();
    // 슬라이딩 윈도우: 1분 내 접속 수 카운트
    if (timestamps.Count >= SessionConstInfo.MaxConnectionsPerIpPerMinute) → 차단
}
```

| 상수 | 현재 값 | 문제 |
|------|---------|------|
| `MaxConnectionsPerIpPerMinute` | 20 | CGNAT에서 같은 IP 유저 20명 이후 전부 차단 |

### 문제점 4가지

**① 임계값 20이 CGNAT에서 너무 작음**  
통신사 CGNAT에서 수십~수천 명이 같은 공인 IP를 사용한다. 20개 이후 접속이 모두 차단된다.

**② 신뢰 IP 우회 수단 없음**  
로컬호스트(`127.0.0.1`), 같은 LAN 내 게임 서버, 관리 툴 IP도 동일하게 제한된다.

**③ 반복 위반 IP에 재연결 허용**  
초과 차단되어도 즉시 재시도하면 다음 Accept 시 또 `timestamps.Count < 20`이 될 때까지 허용된다. 지속적 연결 공격에 무력화.

**④ IPv4-mapped IPv6 이중 카운팅**  
`Address.ToString()`이 `"1.2.3.4"`와 `"::ffff:1.2.3.4"`를 다른 키로 취급한다.  
같은 IPv4 주소가 두 가지 형태로 들어오면 실제 허용량이 2배가 된다.

---

## 설계 방향

### 핵심 아이디어

```
ProcessAccept(e)
  ① IPv6 정규화: NormalizeIp(address)
  ② 허용 목록 확인: IsAllowlisted(ip) → true면 즉시 통과
  ③ 밴 목록 확인: IsBanned(ip) → true면 즉시 거부 + 소켓 닫기
  ④ 슬라이딩 윈도우: IsRateLimited(ip) → 초과 시 위반 카운트 증가
  ⑤ 위반 카운트 ≥ MaxViolationsBeforeBan → 밴 등록
```

### ① IPv4-mapped IPv6 정규화

```csharp
private static string NormalizeIp(IPAddress address)
{
    if (address.IsIPv4MappedToIPv6)
        return address.MapToIPv4().ToString();
    return address.ToString();
}
```

`IsRateLimited` 진입 전 모든 주소에 적용한다.

### ② 허용 목록 (Allowlist)

`TCPAcceptor` 생성자에 `IReadOnlyCollection<string>? trustedIps` 매개변수를 추가한다.  
기본값은 로컬호스트만 포함하는 `HashSet<string> { "127.0.0.1", "::1" }`.  
`TcpServer`에서 생성 시 추가 신뢰 IP를 주입할 수 있다.

```csharp
public TCPAcceptor(int port,
                   int maxConnections = SessionConstInfo.MaxAcceptSessionCount,
                   IReadOnlyCollection<string>? trustedIps = null)
{
    _allowlistedIps = new HashSet<string>(trustedIps ?? Array.Empty<string>());
    _allowlistedIps.Add("127.0.0.1");
    _allowlistedIps.Add("::1");
    ...
}
```

### ③ 임계값 조정

`MaxConnectionsPerIpPerMinute` 상수 값을 올린다 (미결 질문 참고).

### ④ 임시 밴 메커니즘

```csharp
// 밴 정보: 만료 시각 저장
private readonly ConcurrentDictionary<string, DateTime> _bannedIps = new();

// 위반 횟수 추적
private readonly ConcurrentDictionary<string, int> _violationCounts = new();
```

**밴 등록 조건**: `IsRateLimited`가 true를 반환한 직후  
`_violationCounts[ip]++ >= SessionConstInfo.MaxViolationsBeforeBan`  
→ `_bannedIps[ip] = DateTime.UtcNow.AddMinutes(BanDurationMinutes)`

**밴 확인**: `ProcessAccept` 진입 직후, 허용 목록 다음에 확인  
만료된 밴은 `SweepExpiredIfDue` 확장 시 함께 정리.

### 추가 상수 (SessionConstInfo)

```csharp
public const int MaxViolationsBeforeBan = 5;    // N회 초과 시 임시 밴
public const int BanDurationMinutes = 10;       // 밴 지속 시간
```

### 고려했으나 제외한 대안

| 대안 | 제외 이유 |
|------|-----------|
| 서브넷(/24) 기반 제한 | CGNAT 구별 불가, 정상 ISP 블록도 묶어서 차단 위험 |
| 외부 설정 파일 로딩 | 현재 서버가 정적 상수 기반이므로 구조 과도 변경 |
| 영구 밴 | 공유 IP 특성상 다른 정상 유저에게 피해, 한시적 밴이 충분 |

---

## 변경 대상 파일

| 파일 | 변경 내용 |
|------|-----------|
| `Library/ContInfo/SessionConstInfo.cs` | `MaxConnectionsPerIpPerMinute` 값 조정, `MaxViolationsBeforeBan`, `BanDurationMinutes` 추가 |
| `Server/Acceptor/Acceptor.cs` | `NormalizeIp` 메서드 추가, 허용 목록(`_allowlistedIps`) 추가, 밴 목록(`_bannedIps`) + 위반 카운터(`_violationCounts`) 추가, `ProcessAccept` 흐름 수정, `SweepExpiredIfDue` 밴 정리 확장 |
| `Server/TcpServer.cs` | `TCPAcceptor` 생성 시 신뢰 IP 주입 옵션 전달 (선택적) |

---

## 단계별 작업 계획

1. **`SessionConstInfo.cs` 수정**  
   `MaxConnectionsPerIpPerMinute` 값 변경 + `MaxViolationsBeforeBan`, `BanDurationMinutes` 추가

2. **`Acceptor.cs` — `NormalizeIp` 메서드 추가**  
   IPv4-mapped IPv6 정규화 처리

3. **`Acceptor.cs` — 생성자에 허용 목록 추가**  
   `_allowlistedIps HashSet` 초기화, 로컬호스트 기본 포함

4. **`Acceptor.cs` — 밴/위반 필드 추가 및 로직 구현**  
   `_bannedIps`, `_violationCounts` 필드, `IsBanned`, `RegisterViolation` 메서드 추가

5. **`Acceptor.cs` — `ProcessAccept` 흐름 수정**  
   ① 정규화 → ② 허용 목록 → ③ 밴 확인 → ④ 레이트 리밋 순서로 재배치

6. **`Acceptor.cs` — `SweepExpiredIfDue` 만료 밴 정리 추가**

7. **`TcpServer.cs` — 신뢰 IP 주입** (선택적, 미결 질문 답변에 따라)

8. **빌드 확인**

---

## 주의사항 / 위험 요소

- **`_violationCounts` 무한 증가**: 위반 카운트 dict는 밴으로 이어지지 않은 IP의 항목이 쌓인다. `SweepExpiredIfDue`에서 `_connectionTimestamps`가 비어 있는 IP의 카운트도 함께 정리해야 한다.
- **밴 중 Dispose 타이밍**: 서버 종료 시 밴 dict는 GC에 맡기면 되므로 별도 Dispose 처리 불필요.
- **밴 IP의 `_connectionTimestamps` 잔류**: 밴된 IP의 슬라이딩 윈도우 항목은 SweepExpiredIfDue 주기(60초)에 정리된다. 밴 해제 후 카운트가 초기화되어 재연결 허용 — 의도된 동작.

---

## 미결 질문

1. **`MaxConnectionsPerIpPerMinute` 목표 값**: 현재 20을 얼마로 올릴까요?  
   - PC 게임(IP 분산): 50~100 수준  
   - 모바일(CGNAT 고려): 200~500 수준

2. **신뢰 IP 목록**: `TcpServer`에서 주입할 신뢰 IP 대역이 있나요?  
   없으면 기본값(로컬호스트 `127.0.0.1`, `::1`)만 유지합니다.

3. **밴 상수 기본값 동의 여부**: `MaxViolationsBeforeBan = 5`, `BanDurationMinutes = 10`이 적절한지 확인해 주세요.
