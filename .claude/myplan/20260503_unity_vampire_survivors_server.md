# Unity 뱀서류 게임 + 서버 연동

날짜: 2026-05-03

## 목표

- Unity 6 (Client/World)에서 Vampire Survivors 스타일 2D 게임 제작
- 기존 서버의 Login / EnterWorld 인프라를 그대로 활용
- 게임 플레이 결과(스코어, 생존 시간)를 서버 DB에 저장

---

## 현황 분석

### 서버 인프라 (재활용 가능)

| 항목 | 상태 |
|------|------|
| Login (LoginRequest/Response) | 완성 — SHA256 해시 + PBKDF2 DB 인증 |
| EnterWorld (EnterWorldRequest/Response) | 완성 — WorldId로 WorldThreadManager 라우팅 |
| KeepAlive (3초 주기, 10초 타임아웃) | 완성 |
| WorldThread (WorldId % N) | 준비됨 — 같은 WorldId 유저는 같은 스레드 |
| accounts 테이블, SqlWorkerManager | 완성 |

### Unity 프로젝트

- **버전**: Unity 6000.4.5f1
- **설치된 패키지**: URP, 2D Sprite/Tilemap/Animation, Input System (New)
- **현재 콘텐츠**: SampleScene 1개만 존재 — 완전 빈 상태

### 프로토콜 현황 (message.proto)

현재 정의된 메시지 중 이번에 사용할 것:
- `ConnectedResponse`, `LoginRequest/Response`, `EnterWorldRequest/Response`, `KeepAliveRequest`

**추가 필요한 메시지**:
- `GameOverReport` (C→S): 스코어, 생존 시간, 킬 수
- `GameOverResponse` (S→C): 저장 완료 확인

---

## 설계 방향

### 게임 방식 (1인 싱글플레이, 서버 연동)

```
로그인 씬 → 게임 씬 (플레이) → 게임오버 → 스코어 서버 전송
```

멀티플레이는 WorldThread가 이미 준비되어 있으나, 첫 단계는 싱글플레이어로 범위를 제한.

### 네트워크 레이어 전략

DummyClient의 패턴(ReceiverHandler + SenderHandler + ReceiveParser)을 Unity용으로 재구현.  
Unity는 `System.Net.Sockets`를 지원하므로 기존 서버 Library 코드와 동일한 2-byte 헤더 + protobuf 구조 사용.

**Protobuf Unity 적용**: Google.Protobuf를 NuGet에서 다운로드 → Unity 패키지로 변환, `Message.cs`(기존 Scripts/에서 생성된 것)를 Assets에 복사.

### Vampire Survivors 핵심 루프

```
스폰된 적 → 플레이어 자동 공격 + 궁극기(마우스 클릭) → XP 드롭 → 레벨업 → 무기 강화
```

- **플레이어**: WASD 이동, 일반 공격 완전 자동 (범위 원형), 궁극기 마우스 클릭 시 발동 (쿨다운)
- **적**: 플레이어를 향해 직진, 접촉 시 피해
- **XP / 레벨**: XP 오브 수집 → 레벨업 → 업그레이드 3선택지
- **종료 조건**: HP 0 → GameOver → 스코어 서버 전송
- **멀티플레이**: 추후 코-op 추가 예정 (서버 WorldThread 이미 준비됨)

---

## 변경 대상 파일

### Proto / 서버

| 파일 | 변경 내용 |
|------|-----------|
| `Scripts/message.proto` | `GameOverReport`, `GameOverResponse` 메시지 추가 |
| `Scripts/build.bat` 실행 | Message.cs 재생성 후 Library/DTO/ 복사 |
| `Server/Actors/User/Handler/Remote/GameOverReportHandler.cs` | 신규: 스코어 수신 → DB 저장 |
| `Server/Actors/User/DbRequest/Sql/SaveScoreSqlRequest.cs` | 신규: scores 테이블에 INSERT |
| `Scripts/db/create_scores.sql` | 신규: scores 테이블 DDL |

### Unity (Client/World/Assets/)

| 파일 | 변경 내용 |
|------|-----------|
| `Plugins/Google.Protobuf/` | NuGet에서 가져온 Protobuf DLL 배치 |
| `Plugins/Proto/Message.cs` | 서버와 공유하는 proto 생성 파일 복사 |
| `Scripts/Network/TcpGameClient.cs` | TCP 연결, 2-byte 헤더 수신/송신 |
| `Scripts/Network/ReceiveParser.cs` | 서버와 동일한 상태 머신 파서 (Unity 이식) |
| `Scripts/Network/MessageDispatcher.cs` | PayloadCase → 핸들러 딕셔너리 라우팅 |
| `Scripts/Network/Handler/ConnectedResponseHandler.cs` | 수신 후 자동 LoginRequest 전송 |
| `Scripts/Network/Handler/LoginResponseHandler.cs` | 성공 시 게임씬 로드 |
| `Scripts/Network/Handler/EnterWorldResponseHandler.cs` | 성공 시 게임 시작 허가 |
| `Scripts/Network/Handler/GameOverResponseHandler.cs` | 저장 완료 확인 후 결과 화면 |
| `Scripts/Manager/NetworkManager.cs` | MonoBehaviour 싱글톤, Update에서 수신 처리 |
| `Scripts/Manager/GameManager.cs` | 게임 상태(로그인/플레이/종료) 관리 |
| `Scripts/Game/Player/PlayerController.cs` | WASD 이동 (New Input System) |
| `Scripts/Game/Player/PlayerStats.cs` | HP, XP, 레벨 데이터 |
| `Scripts/Game/Player/AutoAttack.cs` | 일정 주기 범위 자동 공격 |
| `Scripts/Game/Player/UltimateSkill.cs` | 마우스 클릭 발동, 쿨다운 관리 |
| `Scripts/Game/Enemy/EnemyController.cs` | 플레이어 추적, HP, 피격 처리 |
| `Scripts/Game/Enemy/EnemySpawner.cs` | 시간 경과에 따라 적 수/속도 증가 |
| `Scripts/Game/System/XpSystem.cs` | XP 오브, 레벨업 임계값 |
| `Scripts/Game/System/UpgradeSystem.cs` | 레벨업 시 3선택지 UI 표시 |
| `Scripts/UI/LoginUI.cs` | ID/PW 입력, 로그인 버튼 |
| `Scripts/UI/HudUI.cs` | HP바, XP바, 타이머, 킬 수 |
| `Scripts/UI/GameOverUI.cs` | 결과 표시, 재시작 버튼 |
| `Scenes/LoginScene.unity` | 신규 씬 |
| `Scenes/GameScene.unity` | 신규 씬 |

---

## 단계별 작업 계획

### Phase 1 — 프로토/서버 확장 (서버 코드)

1. `message.proto`에 `GameOverReport` / `GameOverResponse` 추가
2. `build.bat` 실행 → Message.cs 재생성
3. `create_scores.sql` 작성 (account_id, score, kill_count, survive_seconds, played_at)
4. `SaveScoreSqlRequest.cs` 구현 (IsCritical=false)
5. `GameOverReportHandler.cs` 구현
6. `dotnet build` 검증

### Phase 2 — Unity 네트워크 레이어

1. Google.Protobuf DLL을 Assets/Plugins/에 배치
2. Message.cs를 Assets/Plugins/Proto/에 복사
3. `ReceiveParser.cs` 이식 (2-byte 헤더 상태 머신)
4. `TcpGameClient.cs` 구현 (비동기 Connect, Send, 수신 스레드)
5. `MessageDispatcher.cs` 구현 (딕셔너리 기반 핸들러 등록)
6. `NetworkManager.cs` MonoBehaviour 싱글톤 구현
7. 각 응답 핸들러 4종 구현
8. Unity에서 서버 연결 → LoginRequest → LoginResponse 흐름 테스트

### Phase 3 — 게임플레이 구현

1. `GameManager.cs` — 씬 전환, 게임 상태 머신
2. `PlayerController.cs` — New Input System WASD 이동
3. `PlayerStats.cs` — HP/XP/레벨
4. `AutoAttack.cs` — OverlapCircle 범위 자동 공격, 쿨다운
5. `UltimateSkill.cs` — 마우스 좌클릭 발동, 범위 폭발형, 쿨다운 UI 연동
6. `EnemyController.cs` — NavMesh 없이 직선 추적 (Rigidbody2D)
7. `EnemySpawner.cs` — 카메라 외곽 랜덤 스폰, 시간에 따라 밀도 증가
8. `XpSystem.cs` — XP 오브 물리, 자동 흡수 범위
9. `UpgradeSystem.cs` — 레벨업 시 Time.timeScale=0, 선택지 3개

### Phase 4 — UI + 서버 연동

1. `LoginScene` UI: ID/PW 입력 → 로그인 버튼
2. `HudUI.cs`: HP바, XP바, 경과 시간, 킬 수
3. `GameOverUI.cs`: 스코어 표시 → GameOverReport 전송 → 응답 후 재시작
4. `EnterWorldRequest` 전송 (로그인 성공 후 GameScene 진입 시)
5. 전체 흐름 통합 테스트

---

## 주의사항 / 위험 요소

- **Protobuf in Unity**: `Google.Protobuf`는 Unity에서 직접 NuGet 지원이 없음. `net8.0` 빌드 결과물 DLL을 수동으로 Assets/Plugins/에 배치해야 함. `UNSAFE` 코드 허용 설정 필요할 수 있음.
- **소켓 스레드 vs Unity 메인 스레드**: TCP 수신은 별도 스레드에서 동작 → `NetworkManager.Update()`에서 ConcurrentQueue를 드레인해 메인 스레드로 안전하게 전달해야 함.
- **KeepAlive 3초 주기**: Unity에서도 서버 타임아웃(10초) 안에 전송해야 함. `NetworkManager.Update()`에서 타이머 관리.
- **SHA256 클라이언트 해시**: 서버와 동일한 `SHA256(plaintext)` 연산을 Unity에서 `System.Security.Cryptography`로 구현해야 함.
- **씬 빌드 순서**: LoginScene(0), GameScene(1) 순서로 Build Settings에 등록 필요.

---

## 확정 사항 (2026-05-03)

| 항목 | 결정 |
|------|------|
| 멀티플레이 | 1단계 싱글만, 추후 코-op 추가 |
| 공격 방식 | 일반 공격 완전 자동 + 궁극기 마우스 클릭 |
| Protobuf DLL | net8.0 빌드 결과물 직접 복사 |
| 스코어 저장 | 런 별 전체 이력 (scores 테이블에 매 게임 INSERT) |
