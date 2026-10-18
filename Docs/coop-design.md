# 협동 모드 설계: 서버 권위 시뮬레이션 (C 방식)

작성: 2026-10-18, csharpServer 세션. unity_samkuk 세션과 협의용.

## 1. 원칙

- **서버가 전투 전체를 시뮬레이션**한다: 플레이어 이동, 적 스폰/추적/겹침/접촉 피해, 무기 발동·투사체·명중, 경험치·레벨업·선택지, 스테이지 웨이브/이벤트, 사망/클리어.
- **클라이언트는 렌더러 + 입력 장치**다: 입력(이동 벡터, 스킬 버튼, 레벨업 선택)을 보내고, 스냅샷/이벤트를 받아 보간·연출한다. 전투 중 클라이언트에 게임 규칙 코드는 남기지 않는다.
- **게임 데이터는 유니티가 원본**이다. ScriptableObject를 JSON으로 내보내고 서버가 기동 시 읽는다. 식별자는 에셋 파일명(예: `Enemy_Infantry`).
- 기존 싱글 플레이(오프라인) 코드는 유니티에 그대로 두되, 온라인 협동 전투 씬은 별도 경로로 만든다. (전환 범위는 unity_samkuk가 결정)

## 2. 틱과 전송

| 항목 | 값 |
|---|---|
| 서버 시뮬 틱 | 20 Hz (50 ms) |
| 스냅샷 전송 | 10 Hz (2틱마다) |
| 클라 입력 전송 | 20 Hz (변화 없으면 10 Hz까지 줄여도 됨) |
| 방 인원 | 최대 4 |
| 좌표 양자화 | sint32, 유닛 × 100 (cm 단위) |
| 관심 영역(AOI) | 플레이어별 반경 18 유닛 안의 적/투사체/젬만 전송 |

프레임 한계가 바디 8190바이트이므로 스냅샷은 packed 병렬 배열(ids[], xs[], ys[])로 보낸다. 적 400마리 기준 약 3 KB.
AOI 밖으로 나간 개체는 `EnemyLeave`/`EnemyEnter` 이벤트 없이, 스냅샷에서 빠지면 클라가 0.5초 뒤 숨긴다(간단화).

## 3. 메시지 (message.proto 추가분)

### 3.1 방

| 메시지 | 방향 | 내용 |
|---|---|---|
| `RoomCreateRequest { hero_id }` | C→S | 방 생성, 6자리 초대 코드 발급 |
| `RoomCreateResponse { success, error_code, room }` | S→C | |
| `RoomJoinRequest { room_code, hero_id }` | C→S | 코드로 참가 |
| `RoomJoinResponse { success, error_code, room }` | S→C | error: 1=없음 2=정원 3=진행중 |
| `RoomLeaveRequest {}` | C→S | 방 나가기 (방장이 나가면 다음 사람이 방장) |
| `RoomReadyRequest { ready }` | C→S | 준비 토글 |
| `RoomSetHeroRequest { hero_id }` | C→S | 장수 변경 |
| `RoomSetStageRequest { stage_id }` | C→S | 방장만 |
| `RoomStateNoti { room }` | S→C | 방 상태 변동 시 전원에게 |
| `RoomStartRequest {}` | C→S | 방장만. 전원 준비 필요 |
| `RoomStartResponse { success, error_code }` | S→C | |
| `BattleStartNoti { stage_id, my_slot, tick_rate, snapshot_rate, players[] }` | S→C | 전투 시작. players: slot, user_id, display_name, hero_id, entity_id |

`RoomInfo { room_code, host_slot, stage_id, members[] }`, `RoomMember { slot, user_id, display_name, hero_id, ready }`

### 3.2 전투

정확한 필드는 `Scripts/message.proto`의 "협동 전투" 절이 원본이다. 요약:

| 메시지 | 방향 | 내용 |
|---|---|---|
| `BattleInputRequest { seq, move_x, move_y, skill }` | C→S | 20 Hz. move는 길이 ≤ 1 |
| `BattleSnapshotNoti { tick, elapsed_ms, players[], enemies, gems, events[], stage }` | S→C | 10 Hz |
| `LevelUpChoiceNoti { choice_id, options[], timeout_ms }` | S→C | 레벨업한 플레이어에게만. 선택 대기 중 방 전체 일시정지(StageState.paused_by_slots), 15초 후 자동 선택 |
| `UpgradeSelectRequest { choice_id, index }` | C→S | |
| `BattleEndNoti { cleared, survive_seconds, results[] }` | S→C | 전원 사망 또는 클리어. results: slot, kills, level, score, alive, hero_id, gold |

- `PlayerState` flags: 1=사망 2=피격무적 4=이동중 8=스킬무적 16=공격버프 32=이동속도버프. facing 0=아래 1=위 2=왼쪽 3=오른쪽. `last_input_seq`는 서버가 마지막으로 반영한 입력 번호.
- `EnemyStates { ids[], xs[], ys[], flags[] }` packed 병렬 배열. flags: 1=기절 2=돌진예고 4=돌진중 8=사격예고 32=이동중. 바라보는 방향은 클라가 가장 가까운 플레이어 쪽으로 계산.
- `GemStates`는 끌려오는 중인 젬만. 바닥의 젬은 `GemSpawn` 위치에 고정.
- 궤도 무기 각도는 전송하지 않는다. 서버/클라 모두 `angle = (elapsed_ms / 1000 × rotateSpeed) mod 360 + i × 360 / count`.
- **분할 규칙**: 이벤트가 많아 바디 8190바이트를 넘으면 같은 tick의 `BattleSnapshotNoti`를 여러 개로 나눈다. 첫 메시지에 상태 전부 + 이벤트 일부, 이어지는 메시지는 tick/stage가 같고 players/enemies/gems가 비어 있으며 이벤트만 담는다. 클라는 players가 비어 있으면 상태를 덮어쓰지 않고 이벤트만 처리한다.
- KeepAlive: 서버는 어떤 메시지든 받으면 타임아웃을 미루므로 전투 중 입력만 보내도 된다.

### 3.3 이벤트 (`BattleEvent`, oneof)

| 이벤트 | 용도 |
|---|---|
| `EnemySpawn { id, enemy_id, x, y }` | 클라가 적 테이블에 등록. 재배치(너무 멀어짐)도 Despawn(reason 1) + Spawn으로 알린다 |
| `EnemyDespawn { id, reason, x, y }` | 0=사망 1=재배치/정리 |
| `Damage { target_kind, id, amount, hp_pct, x, y }` | 데미지 숫자, 피격 연출. target_kind 0=적 1=플레이어(id=slot). hp_pct 0=사망 |
| `WeaponFire { slot, weapon_id, x, y, dir_x, dir_y, index, motion }` | 무기 발동 연출. motion 0=없음 1=내려베기 2=올려베기 3=찌르기 4=휩쓸기 |
| `ProjectileSpawn { id, owner_slot, weapon_id, x, y, dir_x, dir_y, speed, lifetime_ms, enemy_id }` | 클라가 외삽. 적 투사체는 owner_slot=-1, enemy_id |
| `ProjectileDespawn { id, x, y }` | 명중/소멸 이펙트 |
| `GemSpawn { id, x, y, value }`, `GemCollect { id, slot }` | |
| `PlayerLevelUp { slot, level }`, `PlayerDied { slot }` | |
| `WaveChanged { number, name }`, `StageEvent { message, is_boss, boss_id }` | |
| `LoadoutChanged { slot, weapons[{id, level}], passives[{id, level}] }` | 보유 무기/패시브 변경 (획득·레벨업·진화). 진화하면 기본 무기가 목록에서 빠진다 |
| `AreaFx { slot, weapon_id, x, y, radius, duration_ms, kind }` | 0=불길 장판 1=화살비 예고 2=화살비 폭격 3=벼락 4=충격파 |

## 4. 서버 데이터 JSON (유니티 → `csharpServer/GameServer/Data/`)

유니티 에디터 메뉴 `Samkuk > Export Server Data`로 내보낸다. 모든 id는 에셋 파일명. 색/스프라이트 등 표시용 필드는 제외.

```
enemies.json      [{ id, displayName, role, scale, moveSpeed, maxHp, contactDamage, expReward,
                     chargeInterval, chargeWindup, chargeDuration, chargeSpeedMultiplier,
                     attackRange, fireInterval, projectileSpeed, projectileDamage, projectileLifetime, projectileSize,
                     colliderRadius }]
weapons.json      [{ id, displayName, description, type, damage, cooldown, range, count, projectileSpeed, pierce,
                     duration, size, rotateSpeed, tickInterval, knockback,
                     swingArcDegrees, comboSwings, swingDuration, trailTravel, trailScale, trailDamageRatio,
                     maxLevel, damagePerLevel, cooldownReductionPerLevel, levelsPerExtraCount }]
passives.json     [{ id, displayName, description, type, valuePerLevel, maxLevel }]
evolutions.json   [{ id, baseWeapon, requiredPassive, evolvedWeapon, requiredLevel, requiredPassiveLevel }]
heroes.json       [{ id, displayName, maxHpBonus, damageMultiplier, moveSpeedMultiplier, expMultiplier,
                     pickupRadiusMultiplier, startingWeapon, skill, weapons[] }]
stages.json       [{ id, displayName, duration,
                     waves: [{ name, startTime, spawnPerSecond, maxAlive, enemies: [{ enemy, weight }] }],
                     events: [{ time, enemy, count, isBoss, message }] }]
upgrade_catalog.json { weapons[], passives[], evolutions[], weaponLevelUpWeight, passiveWeight, newWeaponWeight }
balance.json      { playerMaxHp: 100, playerMoveSpeed, playerBodyRadius, invulnerableTime: 0.5,
                    separationStrength: 3, maxSeparationSpeed: 3, tooFarDistance: 25, playerRadius: 0.4,
                    contactPadding: 0.15, spawnRingRadius(카메라 대각선 + margin), maxGems: 300,
                    magnetRadius: 2.2, collectRadius: 0.35, gemStartSpeed: 4, gemAcceleration: 30, gemMaxSpeed: 20,
                    expBase: 7, expStep: 6, choiceCount: 3, maxWeapons: 4, healRatio: 0.3,
                    knockbackDecay: 8, maxKnockbackSpeed: 14, shotWarningSeconds: 0.4 }
```
balance.json의 값은 현재 씬/프리팹에 들어 있는 직렬화 값을 그대로 내보낸다. 유니티 익스포터는 radius(적), title(장수), skills.json, export_info.json, 카메라 값을 더 내보내며 서버는 모르는 필드를 무시한다.

익스포터는 두 가지다. 결과는 같은 스키마이며 유니티 쪽이 원본이다.
- 유니티 에디터 메뉴 `Samkuk > Export Server Data` (`Assets/Scripts/Editor/ServerDataExporter.cs`): 씬/프리팹 값까지 정확.
- `python Scripts/export_unity_data.py`: 에디터 없이 .asset YAML을 직접 읽는다. balance.json은 코드 기본값. 유니티를 못 열 때의 대안.

## 5. 단계

1. **(병행)** 유니티: 데이터 익스포터 + JSON 1차 출력. 서버: 룸 액터, 데이터 로더, 시뮬 코어(플레이어 이동, 적 스폰·추적·접촉, 화살/베기/궤도 무기, 젬·레벨업·패시브, 스테이지).
2. 프로토 확정 → `Message.cs` 재생성 → 유니티 전달. DummyClient로 4인 방 자동 전투 부하 테스트.
3. 유니티: 방 UI(만들기/코드 참가/준비/시작), 전투 씬을 스냅샷 렌더러로 구성. 실제 2~4인 플레이.
4. 2차: 나머지 무기(찌르기/불길/벼락/화살비/충격파/극), 장수 스킬, 궁병 투사체, 지형 충돌(맵 JSON), 재접속.
5. 3차: 아군 장수(AllyController), 내정(전략 지도) 연동, 결과 저장/랭킹.

1차 범위에서 **빠지는 것**: 지형 충돌(개활지), 아군 장수, 장수 스킬, 궁병 원거리 공격(접근만), 무기 6종. 이것들은 서버 시뮬 구조가 자리 잡은 뒤 하나씩 옮긴다.

## 6. 구현 현황 (2026-10-18)

서버(csharpServer) 1차 구현 완료, DummyClient `--coop`로 4인 방 한 판(60초 클리어, 레벨업·사망·결과 저장) 검증.

| 위치 | 내용 |
|---|---|
| `GameServer/Battle/Data/` | JSON 정의(`GameDefs.cs`)와 로더(`GameDataStore.cs`). 기동 시 `bin/.../Data/*.json` 읽음 |
| `GameServer/Battle/Sim/BattleWorld.cs` | 시뮬 본체: 스테이지/스폰, 적 추적·겹침·접촉·돌진·궁병 사격, 플레이어 이동·무적·재생, 투사체, 장판, 젬, 경험치·레벨업 선택지, 스냅샷 생성 |
| `GameServer/Battle/Sim/WeaponSystem.cs` | 무기 9종 발동 규칙 (Arrow/Slash/Orbit 원본 그대로, 나머지는 범위·간격 규칙만) |
| `GameServer/Battle/Sim/UpgradeGenerator.cs` | 레벨업 선택지 (유니티 UpgradeGenerator 이식) |
| `GameServer/Rooms/` | `Room`(액터, 50 ms 틱, 명령 큐), `RoomManager`(초대 코드), `RoomThreadManager` |
| `GameServer/Actors/User/Handler/Remote/RoomHandlers.cs`, `BattleHandlers.cs` | 세션 → 룸 명령 전달 |
| `DummyClient --clients N --coop [방인원] [라운드]` | 자동 협동 전투 테스트 |

1차에서 빠진 것: 장수 스킬(skill 입력은 받되 무시), 지형 충돌, 아군 장수, 메타 강화(서버 저장 연동 후), 재접속. 점수/골드 공식은 임시.
