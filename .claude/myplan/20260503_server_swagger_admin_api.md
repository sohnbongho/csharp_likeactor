# 서버에 Swagger (관리/모니터링 HTTP API) 추가

날짜: 2026-05-03

## 목표

- TCP 게임 서버 프로세스 안에 ASP.NET Core 기반 HTTP 관리 API를 추가
- Swagger UI로 API 명세를 시각화하고 브라우저에서 직접 호출 가능
- 기존 TCP 게임 서비스(포트 9000)는 그대로 유지, 별도 포트(9001 예정)에 HTTP 호스트

---

## 현황 분석

### 현재 서버 특성

- **타입**: Console App (`OutputType=Exe`), `.NET 8`, `Microsoft.NET.Sdk`
- **프로토콜**: 자체 정의 TCP (2-byte 헤더 + protobuf), HTTP 엔드포인트 없음
- **모니터링**: `TcpServer.MonitorAsync`가 10초마다 콘솔 로그로 동접/CPU/메모리/패킷 출력만 함
- **DB 접근**: SqlWorkerManager로 비동기 큐 처리

### 노출할 가치가 있는 운영 정보

| 항목 | 출처 |
|------|------|
| 활성 동접 수 | `UserObjectPoolManager.ActiveSessionCount` |
| 패킷 통계 | `Library.Network.PacketStats.Snapshot()` |
| CPU/메모리 | `Process.GetCurrentProcess()` |
| 활성 세션 목록 | `UserObjectPoolManager._activeSessions` (현재 private) |
| 세션 강제 종료 | `UserSession.Disconnect()` |
| 최근 스코어 조회 | DB `scores` 테이블 |

### Swagger의 본질

Swagger(OpenAPI)는 HTTP/REST API용 명세 도구. TCP/Protobuf 메시지에는 직접 적용 불가. 따라서 Swagger를 추가한다는 것은 사실상 **HTTP 관리 API를 새로 만들고 거기에 Swagger UI를 얹는다**는 의미로 해석함.

---

## 설계 방향

### 핵심 아이디어

기존 TCP 호스트를 유지하면서 같은 프로세스에 ASP.NET Core Minimal Host를 추가 기동.

```
┌────────────────────────────────────────────────────┐
│              Server.exe (같은 프로세스)             │
│                                                     │
│  ┌─────────────────────┐  ┌─────────────────────┐ │
│  │  TCPAcceptor :9000  │  │  AspNet Host :9001  │ │
│  │  (게임 클라이언트)    │  │  (Swagger UI / API) │ │
│  └─────────────────────┘  └─────────────────────┘ │
│           │                          │             │
│           └────────┬─────────────────┘             │
│                    ▼                               │
│      UserObjectPoolManager (공유)                   │
│      SqlWorkerManager (공유)                        │
└────────────────────────────────────────────────────┘
```

### 선택한 접근법

- **같은 프로세스 + 별도 포트 9001**
  - 활성 세션, 패킷 통계 등을 별도 IPC 없이 메모리 직접 참조
  - `WebApplication.CreateBuilder()`로 ASP.NET Core 미니 호스트 구성
  - `Microsoft.AspNetCore.OpenApi`(.NET 8 내장) + `Swashbuckle.AspNetCore`로 Swagger UI 제공
- **DI 컨테이너 활용**: 컨트롤러가 `UserObjectPoolManager`, `SqlWorkerManager`를 의존성 주입으로 받음
- **인증**: 1차 구현은 단순 API Key (HTTP Header `X-Admin-Key`) 미들웨어. 추후 JWT 등으로 확장 가능

### 제외한 대안

- **별도 프로세스로 관리 서버 분리**: 동접/세션 정보를 IPC(파이프/Redis)로 공유해야 해서 복잡. 단일 프로세스가 단순.
- **gRPC + grpc-gateway**: 기존 protobuf와 통합되긴 하지만 학습 비용 크고 Swagger UI 통합도 별도 작업 필요.
- **Server.csproj를 `Microsoft.NET.Sdk.Web`으로 변경**: 콘솔 App의 메인 진입점이 충돌할 수 있음. `FrameworkReference`만 추가하는 방식이 안전.

---

## 변경 대상 파일

| 파일 | 변경 내용 |
|------|-----------|
| `Server/Server.csproj` | `FrameworkReference Microsoft.AspNetCore.App` 추가, `Swashbuckle.AspNetCore` 패키지 추가 |
| `Server/appsettings.json` | `AdminApi` 섹션 추가 (port, apiKey, enabled) |
| `Server/Program.cs` | DbConfig 로드 후 AdminApiHost.StartAsync() 호출, 종료 시 정지 |
| `Server/TcpServer.cs` | 외부에서 모니터링 정보를 조회할 수 있도록 public getter 추가 (또는 ServerStats 객체 도출) |
| `Server/Actors/UserObjectPoolManager.cs` | `IEnumerable<SessionInfo> EnumerateSessions()` 등 조회 API 추가 |
| `Server/AdminApi/AdminApiHost.cs` | 신규: WebApplication 빌드/Run/Stop 캡슐화 |
| `Server/AdminApi/AdminApiKeyMiddleware.cs` | 신규: `X-Admin-Key` 헤더 검증 |
| `Server/AdminApi/Controllers/StatsController.cs` | 신규: GET `/api/stats` |
| `Server/AdminApi/Controllers/SessionsController.cs` | 신규: GET `/api/sessions`, POST `/api/sessions/{id}/disconnect` |
| `Server/AdminApi/Controllers/ScoresController.cs` | 신규: GET `/api/scores` |
| `Server/AdminApi/Controllers/HealthController.cs` | 신규: GET `/api/health` |
| `Server/AdminApi/Models/*.cs` | 응답 DTO들 (StatsDto, SessionDto, ScoreDto 등) |

---

## 단계별 작업 계획

### 1단계 — 의존성 추가
`Server.csproj`에 다음 추가:
```
<FrameworkReference Include="Microsoft.AspNetCore.App" />
<PackageReference Include="Swashbuckle.AspNetCore" Version="6.7.3" />
```

### 2단계 — 설정 추가
`appsettings.json`:
```json
"AdminApi": {
  "Enabled": true,
  "Port": 9001,
  "ApiKey": "change-me"
}
```

### 3단계 — 서버 상태 노출 통로 정비
- `UserObjectPoolManager`에 `IEnumerable<SessionSnapshot> EnumerateSessions()` 추가
  - SessionSnapshot: SessionId, UserId, WorldId, RemoteIp, ConnectedAt 등
- 강제 종료용 `bool TryDisconnect(ulong sessionId)` 추가
- TcpServer에 `IServerStats` 인터페이스로 동접/패킷/CPU 정보 노출

### 4단계 — AdminApi 호스트 구현
- `AdminApiHost.cs`: `WebApplication.CreateBuilder` 기반
- 컨트롤러 디스커버리, Swagger 엔드포인트(`/swagger`), API Key 미들웨어 등록
- DI 등록: `UserObjectPoolManager`, `SqlWorkerManager`, `IServerStats`를 싱글톤으로 등록

### 5단계 — API Key 미들웨어
- `/swagger`, `/swagger/v1/swagger.json`, `/api/health`는 보호 면제 (헬스체크는 외부 모니터링용)
- 그 외 `/api/*`는 `X-Admin-Key` 헤더 검증, 불일치 시 401

### 6단계 — 컨트롤러 4종 구현

#### StatsController
- `GET /api/stats` → `{ activeSessions, packetsRecvTotal, packetsSentTotal, cpuPercent, memoryMb, uptimeSeconds }`

#### SessionsController
- `GET /api/sessions` → 활성 세션 목록
- `POST /api/sessions/{sessionId}/disconnect` → 강제 종료

#### ScoresController
- `GET /api/scores?accountId={id}&limit=50` → 특정 계정의 최근 스코어
- `GET /api/scores/top?limit=10` → 전체 상위 스코어
- (Dapper로 직접 DB 쿼리, SqlWorker 큐는 우회 — 읽기 전용)

#### HealthController
- `GET /api/health` → `{ status: "ok", db: "ok"|"down" }`

### 7단계 — Program.cs 통합
- DbConfig 로드 후 `var adminHost = await AdminApiHost.StartAsync(...)`
- 기존 `server.Start()` (블로킹) 유지, 종료 시 `adminHost.StopAsync()` 호출

### 8단계 — 빌드 검증 + 동작 확인
- `dotnet build Server/Server.sln -c Release`
- 서버 실행 후 브라우저로 `http://localhost:9001/swagger` 접속하여 UI 확인
- API 호출 테스트 (X-Admin-Key 헤더 포함)

---

## 주의사항 / 위험 요소

- **`UserObjectPoolManager` 내부 자료구조 노출 위험**: 현재 `_activeSessions`(ConcurrentDictionary)에서 enumerate 하면 동시성 안전하지만, 강제 종료는 세션의 Tick 스레드에서 일어나야 안전. AdminApi → Tick 스레드로 디스패치 큐를 통해 호출해야 race 없음. 1차 버전은 `Disconnect()`가 이미 thread-safe(`Interlocked.CompareExchange`)이므로 직접 호출 가능.
- **AspNet 호스트 SDK 충돌**: `Microsoft.NET.Sdk` + `FrameworkReference Microsoft.AspNetCore.App`로도 충분하지만 `dotnet publish` 시 일부 옵션 차이 발생 가능. 필요 시 SDK를 Web으로 바꿔야 할 수 있음.
- **컨트롤러 디스커버리**: Minimal API보다 `AddControllers`+`MapControllers`가 Swashbuckle과 잘 맞음. 단, 컨트롤러 어셈블리 명시적으로 등록 필요.
- **API Key 노출 위험**: appsettings.json에 평문 보관 시 git에 커밋되면 위험. `appsettings.local.json`에 분리하거나 환경변수 사용 권장.
- **Swagger UI 외부 노출**: 운영 환경에서 `/swagger`가 인증 없이 열리면 API 구조가 노출됨. 1차는 개발 편의 위해 노출, 운영 배포 시 인증 추가 또는 비활성화.
- **DB 직접 쿼리**: ScoresController가 SqlWorker 큐를 우회하면 DB 부하가 게임 트래픽과 분리되지 않음. 단순 모니터링 용도로만 사용하고 빈도 제한 권장.
- **DI 충돌**: `UserObjectPoolManager`는 현재 `new`로 생성. AdminApiHost에 인스턴스를 그대로 전달하는 방식(컨테이너에 `AddSingleton(instance)`)으로 처리.

---

## 미결 질문

1. **노출 범위**: 모니터링만(읽기 전용)인가요? 아니면 세션 강제 종료, 공지 송신 등 쓰기 작업도 포함할까요?

2. **인증 방식**: 단순 API Key (헤더)로 충분한가요? 아니면 JWT/Basic Auth가 필요한가요? 사내망 전용이라 인증 생략도 가능합니까?

3. **포트**: 9001 사용해도 될까요?

4. **Swagger UI 노출**: 운영 환경에서도 `/swagger` 열어둘까요, 개발 환경에서만 활성화할까요?

5. **공지 기능**: appsettings의 Redis BroadcastChannel을 통해 모든 클라이언트에 공지 메시지를 보내는 API도 함께 만들까요?
