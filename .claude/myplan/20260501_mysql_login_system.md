# MySQL 기반 로그인 시스템

날짜: 2026-05-01

## 목표

- 클라이언트가 userId / password를 전송하면 MySQL 검증 후 로그인 결과를 반환하는 완전한 인증 흐름 구현
- 기존 Actor 모델(SqlWorker 채널 → InnerMessage → 핸들러)에 자연스럽게 통합
- 비밀번호는 서버에서 PBKDF2-SHA256으로 검증 (외부 패키지 불필요)

---

## 현황 분석

### 이미 존재하는 인프라

| 컴포넌트 | 파일 | 상태 |
|---------|------|------|
| SQL 워커 채널 | `Library/Db/Sql/SqlWorker.cs` | 완성 |
| SQL 요청 인터페이스 | `Library/Db/Sql/ISqlRequest.cs` | 완성 |
| DB 에러 핸들러 | `Server/.../Inner/DbErrorHandler.cs` | TODO (응답 미구현) |
| LoginSqlRequest | `Server/.../DbRequest/Sql/LoginSqlRequest.cs` | 뼈대만 있음 |
| LogoutSqlRequest | `Server/.../DbRequest/Sql/LogoutSqlRequest.cs` | 뼈대만 있음 |

### 현재 proto 메시지

`message.proto`에 `LoginRequest` / `LoginResponse`가 없음. `ConnectedResponse`, `KeepAlive*`, `EnterWorld*`만 존재.

### UserSession 상태

- 로그인 여부나 계정 정보를 저장하는 필드가 없음
- `WorldId`만 존재 (0 = 로비)

### 문제점 / 개선 여지

- `LoginSqlRequest.ExecuteAsync` 가 `await Task.CompletedTask` 로 비어 있음
- `DbErrorHandler` 가 클라이언트에 에러를 돌려보내지 않음
- 인증 전 모든 메시지를 받아들이는 구조 (인증 게이팅 없음)

---

## 설계 방향

### 핵심 흐름

```
클라이언트
  → LoginRequest (userId, password)
  → LoginRequestHandler (Remote)          // 입력 검증, 중복 로그인 체크
  → LoginSqlRequest (SqlWorkerManager)    // DB 조회 + 비밀번호 검증
  → LoginResultMessage (InnerMessage)     // 세션 채널로 결과 전달
  → LoginResultHandler (Inner)            // AccountData 저장 or 에러
  → LoginResponse (클라이언트)
```

### 비밀번호 해싱 전략

**PBKDF2-SHA256 (System.Security.Cryptography, 외부 패키지 없음)**

- 솔트: `RandomNumberGenerator.GetBytes(32)` → Base64 저장
- 반복: 100,000 회
- 출력: 32 바이트 → Base64 저장
- 검증: `CryptographicOperations.FixedTimeEquals` (타이밍 공격 방지)

**BCrypt 제외 이유**: NuGet 의존성 추가 필요, 이 프로젝트는 외부 패키지 최소화 방침

### 인증 게이팅

- `UserSession.IsAuthenticated` 프로퍼티 추가
- 미인증 세션이 Login/KeepAlive 외의 메시지 전송 시 무시(또는 연결 끊기)
- `MessageQueueDispatcher` 대신 각 핸들러에서 개별 체크 (기존 패턴 유지, Dispatcher 수정 최소화)

### 중복 로그인

- 현재는 Redis 기반 중복 체크 없이 단순 처리: **나중에 로그인한 세션이 우선**
- Redis를 이용한 중복 로그인 강제 종료는 별도 설계로 분리 (미결 질문 참조)

---

## DB 스키마

```sql
-- Scripts/db/create_accounts.sql
CREATE TABLE IF NOT EXISTS accounts (
    account_id  BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
    user_id     VARCHAR(50)  NOT NULL UNIQUE,
    password_hash VARCHAR(88) NOT NULL,   -- Base64(32 bytes)
    salt          VARCHAR(88) NOT NULL,   -- Base64(32 bytes)
    status      TINYINT UNSIGNED NOT NULL DEFAULT 0,  -- 0=active, 1=banned
    created_at  DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    last_login_at DATETIME NULL,
    INDEX idx_user_id (user_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
```

---

## 변경 대상 파일

| 파일 | 변경 내용 |
|------|-----------|
| `Scripts/message.proto` | `LoginRequest`, `LoginResponse`, `ErrorResponse` 메시지 추가 |
| `Library/DTO/Message.cs` | proto 재생성 (`Scripts/build.bat`) |
| `Server/Actors/User/UserSession.cs` | `AccountData` 프로퍼티, `IsAuthenticated` 추가 |
| `Server/Actors/User/DbRequest/Sql/LoginSqlRequest.cs` | 실제 쿼리 + PBKDF2 검증 구현 |
| `Server/Actors/User/DbRequest/Sql/LogoutSqlRequest.cs` | `last_login_at` 업데이트 쿼리 구현 |
| `Server/Actors/User/Handler/Inner/DbErrorHandler.cs` | `ErrorResponse` 전송 추가 |
| **신규** `Scripts/db/create_accounts.sql` | DB 스키마 파일 |
| **신규** `Server/Actors/User/Model/UserAccountData.cs` | 로그인 후 보관할 계정 데이터 |
| **신규** `Server/Model/Message/LoginResultMessage.cs` | Inner 메시지 (DB 결과 → 세션) |
| **신규** `Server/Actors/User/Handler/Remote/LoginRequestHandler.cs` | Remote 핸들러 |
| **신규** `Server/Actors/User/Handler/Inner/LoginResultHandler.cs` | Inner 핸들러 |

---

## 단계별 작업 계획

### 1단계 — DB 스키마 & Proto

1. `Scripts/db/create_accounts.sql` 작성
2. `Scripts/message.proto` 에 아래 추가:
   ```protobuf
   message LoginRequest {
       string user_id = 1;
       string password = 2;
   }
   message LoginResponse {
       bool success = 1;
       int32 error_code = 2;
       // 0=성공, 1=인증실패, 2=밴, 3=서버오류
   }
   message ErrorResponse {
       int32 error_code = 1;
   }
   ```
   `MessageWrapper.oneof payload` 에도 `login_request = 20`, `login_response = 21`, `error_response = 22` 추가
3. `Scripts/build.bat` 실행 → `Library/DTO/Message.cs` 갱신

### 2단계 — 모델 추가

4. `Server/Actors/User/Model/UserAccountData.cs` 생성:
   ```csharp
   public class UserAccountData
   {
       public ulong AccountId { get; init; }
       public string UserId { get; init; } = string.Empty;
   }
   ```

5. `Server/Model/Message/LoginResultMessage.cs` 생성:
   ```csharp
   public enum LoginErrorCode { Success = 0, InvalidCredentials = 1, Banned = 2, ServerError = 3 }

   public class LoginResultMessage : IInnerServerMessage
   {
       public bool Success { get; init; }
       public LoginErrorCode ErrorCode { get; init; }
       public ulong AccountId { get; init; }
       public string UserId { get; init; } = string.Empty;
   }
   ```

### 3단계 — UserSession 수정

6. `UserSession.cs` 에 추가:
   ```csharp
   public UserAccountData? AccountData { get; private set; }
   public bool IsAuthenticated => AccountData != null;
   internal void SetAccountData(UserAccountData data) => AccountData = data;
   ```
   `Reinitialize()` 에 `AccountData = null;` 추가

### 4단계 — SQL 요청 구현

7. `LoginSqlRequest.ExecuteAsync` 구현:
   - `SELECT account_id, user_id, password_hash, salt, status FROM accounts WHERE user_id = @UserId`
   - 행 없음 / 비밀번호 불일치 → `LoginErrorCode.InvalidCredentials`
   - `status = 1` (banned) → `LoginErrorCode.Banned`
   - 성공 시 `UPDATE accounts SET last_login_at = UTC_TIMESTAMP() WHERE account_id = @AccountId`
   - 결과를 `LoginResultMessage`로 세션 채널에 enqueue

8. `LogoutSqlRequest.ExecuteAsync` 구현:
   - `UPDATE accounts SET last_login_at = UTC_TIMESTAMP() WHERE account_id = @AccountId`

### 5단계 — 핸들러 구현

9. `LoginRequestHandler.cs` (Remote):
   ```
   [RemoteMessageHandlerAsyncAttribute(MessageWrapper.PayloadOneofCase.LoginRequest)]
   ```
   - 이미 `IsAuthenticated` 이면 → `LoginResponse { success=false, error_code=1 }` 반환
   - userId / password 비어있으면 → 응답 후 리턴
   - `SqlWorkerManager.Enqueue(new LoginSqlRequest(session, userId, password))`

10. `LoginResultHandler.cs` (Inner):
    ```
    [InnerMessageHandlerAsync(typeof(LoginResultMessage))]
    ```
    - 성공: `session.SetAccountData(...)`, `Send(LoginResponse { success=true })`
    - 실패: `Send(LoginResponse { success=false, error_code=... })`

11. `DbErrorHandler.cs` 수정:
    - `receiver.Send(new MessageWrapper { ErrorResponse = new ErrorResponse { ErrorCode = 3 } })`

---

## 주의사항 / 위험 요소

| 위험 | 내용 | 대응 |
|------|------|------|
| 평문 비밀번호 전송 | TLS 없으면 네트워크에서 노출 | 현재 서버는 TLS 미지원. 향후 TLS 추가 필요. 문서화 |
| PBKDF2 CPU 부하 | 100,000회 반복은 SqlWorker 스레드 점유 | SqlWorker 1개 스레드에서 ~50ms 내외. 기본 8 workers면 허용 범위. 필요 시 반복수 조정 |
| build.bat 실행 필요 | proto 수정 후 수동 실행 | 계획서에 명시. 자동화 미포함 |
| `DropWrite` 채널 | SqlWorkerManager 채널이 꽉 차면 LoginSqlRequest 드롭 | 클라이언트는 응답 없음(타임아웃). 용량 조정 또는 재시도 안내 필요 |
| 중복 로그인 | 같은 userId로 두 세션 동시 로그인 가능 | 현재 설계에서는 허용. Redis 기반 중복 차단은 별도 작업 |
| Dapper 미사용 | MySqlConnector raw 사용 시 쿼리 매핑 수작업 | 기존 LoginSqlRequest 패턴 그대로 유지. Dapper 없이 `MySqlCommand` 사용 |

---

## 미결 질문

1. **회원가입(Register) 기능**도 이번 설계에 포함할지? (현재 설계는 로그인만)
2. **중복 로그인 방지**: 기존 세션 강제 종료 vs 새 로그인 거부 vs 허용 중 어느 쪽?
3. **비밀번호 클라이언트 해싱**: 클라이언트(DummyClient)에서 SHA256 먼저 해서 보낼지, 서버에서 전담할지?
4. **brute force 방지**: 로그인 시도 횟수 제한이 필요한지? (현재 IP 레이트리미터는 연결 횟수만 제한)
5. **인증 게이팅 강도**: 미인증 세션이 Login 외 메시지 전송 시 연결을 끊을지, 무시만 할지?
6. **Dapper 추가**: `MySqlCommand` 직접 사용 대신 Dapper 패키지 도입 여부?
