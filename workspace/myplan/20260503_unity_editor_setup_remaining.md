# Unity Editor 씬/프리팹 셋업 + 동작 검증 (남은 작업)

날짜: 2026-05-03

## 목표

코드 작성(Phase 1~4)은 완료됐고, 남은 작업은 Unity Editor GUI에서만 가능한 항목들이다. 본 문서는 사용자가 Unity Editor에서 직접 수행해야 할 작업을 정리한다.

---

## 현황 요약

### 완료된 코드 (참고용)

```
Client/World/Assets/
├── Plugins/
│   ├── Google.Protobuf.dll
│   └── Proto/Message.cs
└── Scripts/
    ├── Network/ (ReceiveParser, TcpGameClient, MessageDispatcher, Handler×4)
    ├── Manager/ (NetworkManager, GameManager, GameSceneBootstrap)
    ├── Game/Player/ (PlayerController, PlayerStats, AutoAttack, UltimateSkill)
    ├── Game/Enemy/ (EnemyController, EnemySpawner)
    ├── Game/System/ (XpOrb, UpgradeSystem)
    └── UI/ (LoginUI, HudUI, UpgradeUI, GameOverUI)
```

### 미완료 항목 (이 계획서 범위)

1. DB scores 테이블 적용
2. Unity 프로젝트 설정 (레이어/태그)
3. Enemy / XpOrb 프리팹 작성
4. LoginScene 작성
5. GameScene 작성
6. Build Settings 등록
7. 동작 검증 (서버 실행 + Unity Play)

---

## 단계별 작업 계획

### 1단계 — DB scores 테이블 생성

PowerShell 또는 MySQL 클라이언트에서 실행:

```bash
mysql -h 172.17.121.22 -u dance -p'Crazy1!crazy' gamedb < Scripts/db/create_scores.sql
```

확인:
```sql
SHOW TABLES;
DESCRIBE scores;
```

### 2단계 — Unity 프로젝트 설정

**Unity Editor 열기**: `Client/World` 폴더를 Unity Hub로 열기.

#### 2.1 레이어 추가
- Edit → Project Settings → Tags and Layers
- User Layer 6 (또는 빈 슬롯)에 `Enemy` 추가

#### 2.2 태그 추가
- 같은 화면에서 Tag에 `Player` 추가 (이미 기본 태그에 있을 수 있음)

#### 2.3 입력 시스템 확인
- Edit → Project Settings → Player → Other Settings → Configuration → Active Input Handling
- `Input System Package (New)` 또는 `Both` 로 설정 (Keyboard.current 사용을 위해 필요)
- 변경했다면 Editor 재시작

### 3단계 — Enemy 프리팹 생성

1. Hierarchy → 우클릭 → 2D Object → Sprites → Square로 GameObject 생성
2. 이름: `Enemy`
3. Inspector에서 설정:
   - **Layer**: Enemy
   - **SpriteRenderer Color**: 빨강
   - **Add Component**: Rigidbody2D
     - Body Type: Dynamic
     - Gravity Scale: 0
     - Constraints → Freeze Rotation Z 체크
   - **Add Component**: CircleCollider2D (Radius 0.5)
   - **Add Component**: EnemyController
     - Max Hp: 20, Move Speed: 2, Contact Damage: 5, Xp Drop: 1
     - Xp Orb Prefab: (4단계 후 할당)
4. Project 창의 `Assets/Prefabs/` 에 드래그하여 프리팹화
5. Hierarchy의 원본 삭제

### 4단계 — XpOrb 프리팹 생성

1. Hierarchy → 우클릭 → 2D Object → Sprites → Circle로 GameObject 생성
2. 이름: `XpOrb`
3. Inspector에서 설정:
   - **Scale**: (0.3, 0.3, 0.3)
   - **SpriteRenderer Color**: 노랑/녹색
   - **Add Component**: XpOrb (스크립트)
4. `Assets/Prefabs/` 에 프리팹화
5. **3단계의 Enemy 프리팹** Inspector → `Xp Orb Prefab` 필드에 이 프리팹 할당

### 5단계 — LoginScene 작성

1. File → New Scene → Empty (URP)
2. File → Save As → `Assets/Scenes/LoginScene.unity`

#### 5.1 NetworkManager
- Hierarchy → 빈 GameObject 생성, 이름: `_NetworkManager`
- Add Component: NetworkManager
  - Server Host: `127.0.0.1`
  - Server Port: `9000`
  - User Id: `user_00001`
  - Password: `Test1234!`

#### 5.2 Login UI
- Hierarchy → UI → Canvas 생성 (자동으로 EventSystem도 생성됨)
- Canvas 하위에 빈 GameObject `LoginPanel` 추가
- LoginPanel 하위에:
  - **Title** (UI → Text): "로그인"
  - **UserIdField** (UI → Input Field)
  - **PasswordField** (UI → Input Field) — Content Type: Password
  - **LoginButton** (UI → Button)
  - **StatusText** (UI → Text)
- LoginPanel에 `LoginUI` 스크립트 부착
- Inspector에서 4개 필드를 모두 할당 (UserIdField, PasswordField, LoginButton, StatusText)
- Game Scene Name: `GameScene`
- World Id: `1`

### 6단계 — GameScene 작성

1. File → New Scene
2. File → Save As → `Assets/Scenes/GameScene.unity`

#### 6.1 Bootstrap & Manager
- 빈 GameObject `_Bootstrap` → Add: GameSceneBootstrap
- 빈 GameObject `_GameManager` → Add: GameManager + UpgradeSystem (PlayerStats 등 참조는 6.2 이후 할당)

#### 6.2 Player
- Hierarchy → 2D Object → Sprites → Circle, 이름: `Player`
- Tag: `Player`
- 파란색 등으로 색상 변경
- **Add Component** 순서:
  1. Rigidbody2D — Gravity 0, Freeze Rotation Z
  2. CircleCollider2D
  3. PlayerController — Move Speed: 5
  4. PlayerStats — Max Hp: 100
  5. AutoAttack — Range: 3, Cooldown: 1, Damage: 10, **Enemy Mask: Enemy** 체크
  6. UltimateSkill — Radius: 6, Cooldown: 10, Damage: 100, **Enemy Mask: Enemy** 체크

#### 6.3 Spawner
- 빈 GameObject `_Spawner` → Add: EnemySpawner
- Enemy Prefab: 3단계의 Enemy 프리팹
- Player Transform: Player 오브젝트
- Base Spawn Interval: 1, Min: 0.1, Spawn Radius: 12, Difficulty Ramp: 60

#### 6.4 _GameManager의 UpgradeSystem 필드 채우기
- Player Stats: Player의 PlayerStats
- Player Controller: Player의 PlayerController
- Auto Attack: Player의 AutoAttack
- Ultimate Skill: Player의 UltimateSkill

#### 6.5 HUD
- UI → Canvas 생성, 하위에 빈 GameObject `Hud`
  - **HpBar** (UI → Slider) — Min 0, Max 1
  - **XpBar** (UI → Slider) — Min 0, Max 1
  - **LevelText** (UI → Text) — "Lv.1"
  - **TimeText** (UI → Text) — "00:00"
  - **KillText** (UI → Text) — "Kills: 0"
  - **UltCdText** (UI → Text)
- Hud에 HudUI 스크립트 부착, 위 필드 모두 할당 + Player Stats / Ultimate Skill 참조

#### 6.6 UpgradePanel
- Canvas 하위 `UpgradePanel` (빈 GameObject, 비활성)
- 자식으로 3개 Button + 각각 Title/Description Text
- UpgradePanel에 UpgradeUI 스크립트 부착, _GameManager의 UpgradeSystem 참조 + Panel/Buttons/Texts 배열 할당

#### 6.7 GameOverPanel
- Canvas 하위 `GameOverPanel` (빈 GameObject, 비활성)
- 자식으로 ScoreText, StatusText, RestartButton
- GameOverPanel에 GameOverUI 스크립트 부착, 위 필드 할당
- Login Scene Name: `LoginScene`

### 7단계 — Build Settings 등록

- File → Build Settings
- Add Open Scenes (LoginScene 먼저, GameScene 두 번째)
- 순서: LoginScene = 0, GameScene = 1

### 8단계 — 동작 검증

#### 8.1 서버 실행
```
dotnet run --project Server/Server.csproj
```

#### 8.2 Unity Play
- LoginScene을 연 상태에서 ▶ Play
- 자동으로 ID/PW가 채워진 상태 → Login 클릭
- StatusText 변화 확인: "서버 연결 중..." → "로그인 중..." → "월드 입장 중..." → GameScene 로드
- GameScene에서:
  - WASD로 이동 확인
  - 빨간 적이 외곽에서 스폰되어 추적해 오는지 확인
  - 자동 공격으로 적 피격 → 노란 XpOrb 드롭 확인
  - XP 일정 누적 시 UpgradePanel 표시, 게임 일시정지 (Time.timeScale=0)
  - 마우스 좌클릭 → 큰 범위 폭발 데미지
  - 플레이어 HP 0 → GameOverPanel 표시 → "저장 완료" → Restart 버튼

#### 8.3 DB 검증
```sql
SELECT * FROM scores ORDER BY played_at DESC LIMIT 10;
```
방금 플레이한 결과가 row로 추가되어 있는지 확인.

---

## 주의사항 / 위험 요소

- **InputSystem 활성 핸들링**: 2.3 단계 미적용 시 `Keyboard.current` / `Mouse.current`가 null이라 입력 무반응. Player Settings 변경 후 Editor 재시작 필요.
- **Enemy Mask 미설정**: AutoAttack/UltimateSkill에 Enemy 레이어를 체크하지 않으면 적이 데미지를 받지 않음. 해결: Inspector에서 Enemy Mask 드롭다운에서 Enemy 체크.
- **Player 태그 미설정**: XpOrb가 `FindWithTag("Player")`로 플레이어를 찾으므로 태그 누락 시 자동 흡수 동작 안함.
- **Rigidbody2D Constraint**: Freeze Rotation Z를 체크하지 않으면 충돌 시 플레이어/적이 회전함.
- **카메라**: Main Camera는 기본 위치(0,0,-10) Orthographic으로 두면 됨. 별도 추적 카메라 필요시 Cinemachine 또는 단순 follow 스크립트 추가.
- **CircleCollider2D vs Trigger**: XpOrb가 픽업 거리 비교는 거리 기반이라 Trigger 설정과 무관하지만, Enemy 충돌 처리는 Collision 기반이므로 적의 CircleCollider2D는 Trigger=false 유지.
- **Time.timeScale=0 중 KeepAlive**: NetworkManager는 `Time.realtimeSinceStartup` 사용이라 일시정지 중에도 KeepAlive 정상 송신. 서버 측 10초 타임아웃 안전.

---

## 미결 질문

없음. (코드는 완료됐고 Editor 작업만 남음)

---

## 완료 기준 (Definition of Done)

- [ ] scores 테이블 DB에 생성됨
- [ ] LoginScene + GameScene 저장됨
- [ ] Enemy / XpOrb 프리팹 생성됨
- [ ] Build Settings에 두 씬 등록됨
- [ ] Unity Play로 로그인 → 게임 → 사망까지 흐름 동작
- [ ] DB scores 테이블에 플레이 결과 row 1개 이상 확인
