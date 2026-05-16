# Server 분리: LoginServer / GameServer / SocketLibrary

날짜: 2026-05-16

## 목표

- 현재 단일 `Server` 프로젝트를 **LoginServer**, **GameServer**, **SocketLibrary** 세 프로젝트로 분리한다.
- 클라이언트는 로그인 전용 서버(LoginServer)에서 인증 후 게임 서버(GameServer)에 별도 연결한다.
- 공통 네트워크/인프라 코드는 SocketLibrary에 격리한다.

---

## 현황 분석

### 현재 프로젝트 구조

```
Server.sln
├── Library/          ← 공유 프레임워크 (네트워크, DB, MessageQueue 등)
│   ├── Acceptor 없음 ← Acceptor는 Server/에 있음
│   └── World/        ← 로비·월드 스레드 매니저 (게임 전용 코드가 Library에 혼재)
├── Server/           ← 로그인 + 게임 로직이 한 곳에
│   ├── Acceptor/     ← TCP 수락 (범용, SocketLibrary로 이동 가능)
│   ├── Actors/User/Handler/Remote/
│   │   ├── LoginRequestHandler.cs      ← LoginServer 전용
│   │   ├── EnterWorldRequestHandler.cs ← GameServer 전용
│   │   ├── GameOverReportHandler.cs    ← GameServer 전용
│   │   ├── MoveRequestHandler.cs       ← GameServer 전용
│   │   └── KeepAliveRequestHandler.cs  ← 양쪽 공통
│   ├── Actors/User/DbRequest/Sql/
│   │   ├── LoginSqlRequest.cs          ← LoginServer 전용
│   │   ├── LogoutSqlRequest.cs         ← LoginServer 전용
│   │   └── SaveScoreSqlRequest.cs      ← GameServer 전용
│   ├── AdminApi/     ← UserObjectPoolManager에 강하게 결합
│   └── TcpServer.cs  ← Init/Start/Stop 통합
└── DummyClient/      ← 단일 서버 주소에 연결
```

### 현재 메시지 플로우

```
클라이언트 → Server (로그인) → Server (게임, 같은 TCP 연결)
```

### 핵심 문제

| 문제 | 설명 |
|------|------|
| 단일 TCP 연결에 Login + Game 혼재 | 분리 시 클라이언트가 두 번 연결해야 함 |
| Library/World/ | 게임 전용 코드가 공유 라이브러리에 있음 |
| UserSession이 모든 역할 수행 | LoginServer/GameServer 각각 특화 필요 |
| AdminApi가 UserObjectPoolManager 의존 | 서버 분리 시 결합 해소 필요 |

---

## 설계 방향

### 핵심 아이디어: 토큰 기반 2-hop 인증

```
클라이언트
  1. LoginServer 연결 → LoginRequest
  2. LoginServer → Redis에 auth_token(UUID) SET (TTL 30s)
              → LoginResponse { success, auth_token, game_server_ip, game_server_port }
  3. 클라이언트가 LoginServer 연결 종료
  4. GameServer 연결 → GameConnectRequest { auth_token }
  5. GameServer → Redis에서 token 조회 → 검증 → GameConnectResponse
  6. 이후 게임 메시지 처리
```

### 최종 프로젝트 구조

```
Server.sln
├── SocketLibrary/    ← Library 이름 변경 + Server/Acceptor 흡수, World 제거
├── LoginServer/      ← 로그인 전용 서버 (Server에서 분리)
├── GameServer/       ← 게임 전용 서버 (Server에서 분리)
└── DummyClient/      ← 2-hop 연결 방식으로 업데이트
```

### SocketLibrary 포함 범위

현재 `Library/`에서 `World/`만 제거하고 `Server/Acceptor/`를 추가:

| 포함 | 이유 |
|------|------|
| Network/, MessageQueue/, ObjectPool/, Timer/, Worker/ | 양쪽 서버 공통 |
| Db/ (Sql, Cache, Broadcast) | 양쪽 서버가 DB 사용 |
| Logger/, Security/, ContInfo/, DTO/, Model/ | 공통 인프라 |
| **Acceptor/** (Server에서 이동) | 범용 TCP 수락, 서버마다 필요 |
| AdminApi/Dtos.cs | LoginServer에서 참조 |

`World/` (LobbyThreadManager, WorldThreadManager) → **GameServer로 이동**

### 대안 검토

| 대안 | 제외 이유 |
|------|-----------|
| Library 유지 + Server만 분리 | "SocketLibrary"라는 이름이 명시된 요구사항 |
| AdminServer 별도 분리 | 현재 요구사항에 없음, 복잡도 증가 |
| 단일 연결로 서버 간 프록시 | 내부 아키텍처가 더 복잡해짐 |
| 공유 메모리로 서버 간 통신 | 별도 프로세스가 전제인 분리와 상충 |

---

## 변경 대상 파일

### SocketLibrary (Library 이름 변경 + 조정)

| 파일 | 변경 내용 |
|------|-----------|
| `Library/Library.csproj` | 파일명 → `SocketLibrary.csproj`, Assembly명 변경 |
| `Library/World/` | 삭제 → GameServer로 이동 |
| `Server/Acceptor/Acceptor.cs` | `SocketLibrary/Acceptor/`로 이동, namespace 변경 |
| `Server/Acceptor/SocketAsyncEventArgsPool.cs` | 동일 |

### LoginServer (신규 프로젝트)

| 파일 | 변경 내용 |
|------|-----------|
| `LoginServer/LoginServer.csproj` | 신규 생성 |
| `LoginServer/Program.cs` | Server/Program.cs 기반, LoginServer용으로 수정 |
| `LoginServer/TcpLoginServer.cs` | TcpServer.cs → World 관련 제거 |
| `LoginServer/Actors/User/UserSession.cs` | 로그인 전용 (WorldId, MoveToWorld 제거, token 발급 추가) |
| `LoginServer/Actors/UserObjectPoolManager.cs` | World 관련 제거 |
| `LoginServer/Actors/User/Handler/Remote/LoginRequestHandler.cs` | 이동 |
| `LoginServer/Actors/User/Handler/Remote/KeepAliveRequestHandler.cs` | 이동 |
| `LoginServer/Actors/User/Handler/Inner/LoginResultHandler.cs` | token 발급 + Redis SET 로직 추가 |
| `LoginServer/Actors/User/Handler/Inner/DbErrorHandler.cs` | 이동 |
| `LoginServer/Actors/User/DbRequest/Sql/LoginSqlRequest.cs` | 이동 |
| `LoginServer/Actors/User/DbRequest/Sql/LogoutSqlRequest.cs` | 이동 |
| `LoginServer/Actors/User/DbRequest/Cache/GetUserCacheRequest.cs` | 이동 |
| `LoginServer/Actors/User/Model/UserAccountData.cs` | 이동 |
| `LoginServer/Model/Message/LoginResultMessage.cs` | 이동 |
| `LoginServer/Model/Message/InnerServerMessage.cs` | 이동 |
| `LoginServer/Model/Message/UserDisconnectMessage.cs` | 이동 |
| `LoginServer/AdminApi/` | 이동 (관리 API는 LoginServer에 유지) |
| `LoginServer/appsettings.json` | 포트 9000, GameServer 주소 포함 |

### GameServer (신규 프로젝트)

| 파일 | 변경 내용 |
|------|-----------|
| `GameServer/GameServer.csproj` | 신규 생성 |
| `GameServer/Program.cs` | 신규 |
| `GameServer/TcpGameServer.cs` | TcpServer.cs 기반, LobbyThreadManager/WorldThreadManager 포함 |
| `GameServer/World/` | Library/World/에서 이동 |
| `GameServer/Actors/User/UserSession.cs` | 게임 전용 (로그인 대신 token 검증 흐름, game 상태 유지) |
| `GameServer/Actors/UserObjectPoolManager.cs` | World 포함 버전 유지 |
| `GameServer/Actors/User/Handler/Remote/GameConnectRequestHandler.cs` | **신규**: token → Redis 검증 |
| `GameServer/Actors/User/Handler/Remote/EnterWorldRequestHandler.cs` | 이동 |
| `GameServer/Actors/User/Handler/Remote/GameOverReportHandler.cs` | 이동 |
| `GameServer/Actors/User/Handler/Remote/MoveRequestHandler.cs` | 이동 |
| `GameServer/Actors/User/Handler/Remote/KeepAliveRequestHandler.cs` | 이동 |
| `GameServer/Actors/User/Handler/Inner/DbErrorHandler.cs` | 이동 |
| `GameServer/Actors/User/DbRequest/Sql/SaveScoreSqlRequest.cs` | 이동 |
| `GameServer/Actors/User/Model/UserAccountData.cs` | 이동 (LoginServer와 동일 구조) |
| `GameServer/Model/Message/` | InnerServerMessage 등 필요 파일 이동 |
| `GameServer/appsettings.json` | 포트 9001 (LoginServer는 9000) |

### Scripts/message.proto (변경)

| 변경 | 내용 |
|------|------|
| `LoginResponse` 확장 | `auth_token`, `game_server_address`, `game_server_port` 필드 추가 |
| `GameConnectRequest` 추가 | `auth_token` 필드 |
| `GameConnectResponse` 추가 | `success`, `error_code` 필드 |
| `MessageWrapper.payload` | `GameConnectRequest`, `GameConnectResponse` 케이스 추가 |

### Server.sln (변경)

| 변경 | 내용 |
|------|------|
| `Library` 프로젝트 참조 제거 | `SocketLibrary`로 교체 |
| `Server` 프로젝트 제거 | LoginServer, GameServer로 교체 |
| `LoginServer` 추가 | |
| `GameServer` 추가 | |
| `SocketLibrary` 추가 | |

### DummyClient (변경)

| 파일 | 변경 내용 |
|------|-----------|
| `DummyClient/Program.cs` | LoginServer IP/Port + GameServer IP/Port 설정 읽기 |
| `DummyClient/TcpDummyClient.cs` | 1) LoginServer 연결 → 로그인 → token 수신 → 2) GameServer 연결 → GameConnectRequest |
| `DummyClient/Session/UserSession.cs` | 2-hop 상태 머신 추가 |
| `DummyClient/Session/Handler/Remote/LoginResponseHandler.cs` | token 저장 후 GameServer 연결 트리거 |
| `DummyClient/appsettings.json` | LoginServer, GameServer 주소 분리 |

---

## 단계별 작업 계획

### 1단계: Proto 수정 및 재생성
- `Scripts/message.proto` 수정 (LoginResponse 확장, GameConnectRequest/Response 추가)
- `Scripts/build.bat` 실행 → `Library/DTO/Message.cs` 재생성

### 2단계: SocketLibrary 생성
- `Library/` 폴더를 `SocketLibrary/`로 복사/이름 변경
- `Library/World/` 파일 삭제
- `Server/Acceptor/`를 `SocketLibrary/Acceptor/`로 이동
- namespace `Server.Acceptor` → `SocketLibrary.Acceptor` 변경
- `Library.csproj` → `SocketLibrary.csproj`, Assembly명 변경
- 빌드 확인

### 3단계: GameServer 프로젝트 생성
- `GameServer/` 디렉토리 및 `GameServer.csproj` 생성
- `Library/World/` → `GameServer/World/` 이동, namespace 변경
- Game 전용 핸들러·세션·모델 이동 (namespace 일괄 변경)
- `TcpGameServer.cs` 작성 (World 스레드 포함)
- `GameConnectRequestHandler.cs` 신규 작성 (Redis token 검증)
- GameServer의 `UserSession.cs` 특화 (token 검증 흐름, `IsBlockedBeforeAuth`에서 `GameConnectRequest` 허용)
- `appsettings.json` 작성 (포트 9001)
- 빌드 확인

### 4단계: LoginServer 프로젝트 생성
- `LoginServer/` 디렉토리 및 `LoginServer.csproj` 생성
- Login 전용 핸들러·세션·모델 이동, namespace 변경
- `TcpLoginServer.cs` 작성 (WorldThreadManager 제거)
- `LoginResultHandler.cs` 수정: 성공 시 `Guid.NewGuid()` 토큰 → Redis SET(TTL 30s) → LoginResponse에 token + GameServer 주소 포함
- LoginServer의 `UserSession.cs` 특화 (WorldId, MoveToWorld 제거)
- `AdminApi/` 이동
- `appsettings.json` 작성 (포트 9000, GameServer 주소 설정 포함)
- 빌드 확인

### 5단계: Server.sln 업데이트
- 기존 `Library`, `Server` 프로젝트 참조 제거
- `SocketLibrary`, `LoginServer`, `GameServer`, `DummyClient` 추가
- 솔루션 빌드 전체 확인

### 6단계: DummyClient 업데이트
- `appsettings.json`: LoginServer/GameServer 주소 분리
- `TcpDummyClient.cs`: 2-hop 연결 구현
- `LoginResponseHandler.cs`: token 저장 후 GameServer 연결 시작
- GameServer 연결 후 `GameConnectRequest` 전송 핸들러 추가
- 통합 테스트

### 7단계: 구 Server/Library 정리
- `Server/` 프로젝트 삭제 (또는 sln에서만 제거)
- 원본 `Library/` 폴더 삭제 (SocketLibrary로 대체됨)
- `.gitignore` 및 `CLAUDE.md` 업데이트

---

## 주의사항 / 위험 요소

| 항목 | 설명 |
|------|------|
| **UserAccountData 중복** | LoginServer와 GameServer 양쪽에 동일 구조체. SocketLibrary로 올리는 게 깔끔하나, 게임 전용 필드(WorldId, X, Y)가 추가될 경우 GameServer에서 상속/확장 필요 |
| **AdminApi 결합도** | AdminApi의 `SessionsController`, `StatsController`가 `UserObjectPoolManager`에 직접 의존. GameServer로 이동 시 LoginServer 세션 수 조회 불가 |
| **Library/World namespace** | `Library.World` → `GameServer.World`로 변경 시 네임스페이스 충돌 없음. 단, SocketLibrary에서 World를 참조하는 코드가 없는지 확인 필요 (`Library/World/`는 순수 스레드 매니저라 의존성 없음) |
| **Redis 의존성 증가** | 현재 Redis는 캐시·브로드캐스트 용도. 토큰 저장 추가 시 Redis 장애 = 로그인 불가. Redis HA 구성 확인 필요 |
| **DummyClient 복잡도** | 2-hop 연결 시 UserSession 상태 머신이 복잡해짐. 연결 실패/타임아웃 처리 추가 필요 |
| **KeepAliveRequestHandler 중복** | LoginServer와 GameServer 양쪽에 동일한 핸들러가 존재. 내용이 동일하면 SocketLibrary 이동 고려 (단, `IRemoteMessageHandlerAsync` 리플렉션 스캔 범위 확인) |
| **포트 충돌** | LoginServer 9000, GameServer 9001. 현재 `appsettings.json`과 `SessionConstInfo.ServerPort` 값 조율 필요 |
| **Acceptor namespace 변경 파급** | `Server.Acceptor` → `SocketLibrary.Acceptor`. Server 내 참조는 모두 사라지므로 DummyClient에 Acceptor 참조가 없는지 확인 (없음) |

---

## 미결 질문

1. **AdminApi 귀속**: LoginServer(인증 관리)와 GameServer(세션·점수 관리)에 각각 올릴지, 하나의 서버에만 둘지?
   - 현재 AdminApi 컨트롤러 분류: Auth → LoginServer / Health·Notice → 양쪽 / Sessions·Scores·Stats → GameServer

2. **DummyClient의 GameServer 주소**: LoginResponse에서 동적으로 받을지(`game_server_address`, `game_server_port`), 아니면 설정 파일에 고정할지?
   - 동적 수신이 스케일아웃에 유리하나, 테스트 복잡도 증가

3. **Redis 토큰 TTL**: 클라이언트가 LoginServer 응답을 받고 GameServer에 연결하는 시간 고려 시 30초가 적당한지? (로딩 화면 포함)

4. **UserAccountData 위치**: SocketLibrary에 올려 공유할지, 각 서버에 별도 정의할지?
   - SocketLibrary에 두면 게임 전용 필드 추가 시 GameServer에서 서브클래스 사용 필요

5. **기존 Server 프로젝트**: 분리 작업 중 fallback용으로 sln에 남겨둘지, 즉시 제거할지?

6. **SocketLibrary 의존성 범위**: `Db/`(MySQL, Redis 패키지)를 SocketLibrary에 포함할지, 각 서버가 직접 참조할지?
   - 포함 시 SocketLibrary가 무거워지나, 중복 코드 없음
