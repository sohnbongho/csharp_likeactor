using GameServer.Battle.Data;
using Messages;
using System.Numerics;

namespace GameServer.Battle.Sim;

// 한 판의 전투 전체를 시뮬레이션한다. 유니티 EnemyManager/EnemySpawner/StageController/
// PlayerHealth/PlayerExperience/ExpGemManager/Weapon*의 규칙을 서버로 옮긴 것.
// 룸 스레드에서만 호출된다. 네트워크는 모르며, 룸이 Step 후 BuildSnapshot으로 상태를 꺼내 보낸다.
public sealed class BattleWorld
{
    public const int TickRate = 20;
    public const float TickSeconds = 1f / TickRate;
    public const int SnapshotRate = 10;
    public const int ChoiceTimeoutMs = 15000;
    public const float AoiRadius = 18f;          // 플레이어별 관심 영역 반지름
    public const float RainDelaySeconds = 0.6f;  // 화살비 예고 시간

    private readonly GameDataStore _data;
    private readonly BalanceDef _b;
    private readonly StageDef _stage;
    private readonly Random _rng;
    private readonly SpatialGrid _grid;

    public readonly List<PlayerEntity> Players = new();
    public readonly List<EnemyEntity> Enemies = new();
    public readonly List<ProjectileEntity> Projectiles = new();
    public readonly List<GemEntity> Gems = new();
    public readonly List<ZoneEntity> Zones = new();
    public readonly List<DelayedStrike> Strikes = new();

    // 마지막 스냅샷 이후 쌓인 이벤트. 룸이 스냅샷을 보낸 뒤 ClearEvents로 비운다.
    public readonly List<BattleEvent> Events = new();

    private readonly Stack<EnemyEntity> _enemyPool = new();
    private int _nextEnemyId = 1;
    private int _nextProjectileId = 1;
    private int _nextGemId = 1;
    private int _nextChoiceId = 1;

    public int Tick { get; private set; }
    public float Elapsed { get; private set; }
    public bool Ended { get; private set; }
    public bool Cleared { get; private set; }
    public bool Paused => _pendingChoices.Count > 0;

    // 스테이지 진행
    private readonly List<WaveDef> _waves;
    private readonly List<StageEventDef> _stageEvents;
    private int _waveIndex = -1;
    private int _nextEvent;
    private WaveDef? _wave;
    private float _spawnAccumulator;
    private EnemyEntity? _boss;

    // 레벨업 선택 대기 (slot → 선택지)
    private sealed class PendingChoice
    {
        public int ChoiceId;
        public List<UpgradeOption> Options = new();
        public float RemainingSeconds;
    }
    private readonly Dictionary<int, PendingChoice> _pendingChoices = new();
    private readonly int[] _queuedLevelUps = new int[4];

    // 레벨업 선택지를 해당 플레이어에게 보내야 할 때 (룸이 연결)
    public Action<int, LevelUpChoiceNoti>? OnLevelUpChoice;

    // 작업용 버퍼
    private readonly List<EnemyEntity> _hits = new(64);
    private readonly EnemyEntity?[] _nearestBuffer = new EnemyEntity?[8];

    public BattleWorld(GameDataStore data, StageDef stage, int seed)
    {
        _data = data;
        _b = data.Balance;
        _stage = stage;
        _rng = new Random(seed);
        _grid = new SpatialGrid(_b.CellSize);

        _waves = stage.Waves.OrderBy(w => w.StartTime).ToList();
        _stageEvents = stage.Events.OrderBy(e => e.Time).ToList();
    }

    public GameDataStore Data => _data;
    public BalanceDef Balance => _b;
    public Random Rng => _rng;
    public SpatialGrid Grid => _grid;

    // ───────────────────────── 준비 ─────────────────────────

    public PlayerEntity AddPlayer(int slot, HeroDef hero)
    {
        var p = new PlayerEntity { Slot = slot, Hero = hero };
        // 시작 위치: 원점 주위 작은 원 (서로 겹치지 않게)
        float a = slot * MathF.PI * 0.5f;
        p.Pos = new Vector2(MathF.Cos(a), MathF.Sin(a)) * 0.8f;
        RecalculateStats(p);
        p.Hp = p.MaxHp(_b);
        Players.Add(p);
        return p;
    }

    public void Start()
    {
        foreach (var p in Players)
        {
            if (p.Hero.StartingWeapon != "" && _data.Weapons.TryGetValue(p.Hero.StartingWeapon, out var def))
                AddWeapon(p, def);
        }
        UpdateWave();
    }

    public PlayerEntity? GetPlayer(int slot)
    {
        foreach (var p in Players)
            if (p.Slot == slot) return p;
        return null;
    }

    public void SetInput(int slot, int seq, float moveX, float moveY, bool skill)
    {
        var p = GetPlayer(slot);
        if (p == null) return;
        p.LastInputSeq = seq;
        var v = new Vector2(moveX, moveY);
        if (!float.IsFinite(v.X) || !float.IsFinite(v.Y)) v = Vector2.Zero;
        float len = v.Length();
        if (len > 1f) v /= len;
        p.MoveInput = v;
        if (skill) p.SkillPressed = true;
    }

    // 접속이 끊긴 플레이어: 적의 표적에서 빠지고 입력이 멈춘다. 선택 대기 중이었다면 자동 선택.
    public void DisconnectPlayer(int slot)
    {
        var p = GetPlayer(slot);
        if (p == null) return;
        p.Connected = false;
        p.MoveInput = Vector2.Zero;
        if (_pendingChoices.TryGetValue(slot, out var pending))
            ResolveChoice(p, pending, 0);
    }

    // ───────────────────────── 틱 ─────────────────────────

    public void Step()
    {
        if (Ended) return;
        Tick++;

        if (Paused)
        {
            // 레벨업 선택 대기: 게임 시간은 멈추고 선택 제한 시간만 흐른다
            StepChoiceTimeouts(TickSeconds);
            return;
        }

        float dt = TickSeconds;
        Elapsed += dt;

        StepStage(dt);
        StepPlayers(dt);
        StepEnemies(dt);
        StepProjectiles(dt);
        StepWeapons(dt);
        StepZones(dt);
        StepStrikes(dt);
        StepGems(dt);
        CheckEnd();
    }

    public void ClearEvents() => Events.Clear();

    // ───────────────────────── 스테이지 ─────────────────────────

    private void StepStage(float dt)
    {
        UpdateWave();
        while (_nextEvent < _stageEvents.Count && _stageEvents[_nextEvent].Time <= Elapsed)
            FireStageEvent(_stageEvents[_nextEvent++]);

        if (_wave != null && _wave.Enemies.Count > 0 && AnyTargetablePlayer())
        {
            _spawnAccumulator += _wave.SpawnPerSecond * dt;
            while (_spawnAccumulator >= 1f)
            {
                _spawnAccumulator -= 1f;
                if (Enemies.Count >= _wave.MaxAlive)
                {
                    _spawnAccumulator = 0f;
                    break;
                }
                var def = PickWaveEnemy(_wave);
                if (def != null) SpawnEnemy(def, RandomRingPosition());
            }
        }

        if (Elapsed >= _stage.Duration)
        {
            Ended = true;
            Cleared = true;
        }
    }

    private void UpdateWave()
    {
        int index = -1;
        for (int i = 0; i < _waves.Count; i++)
        {
            if (_waves[i].StartTime <= Elapsed) index = i;
            else break;
        }
        if (index < 0 || index == _waveIndex) return;

        _waveIndex = index;
        _wave = _waves[index];
        Events.Add(new BattleEvent { WaveChanged = new WaveChangedEvent { Number = index + 1, Name = _wave.Name } });
    }

    private void FireStageEvent(StageEventDef ev)
    {
        int bossId = 0;
        if (_data.Enemies.TryGetValue(ev.Enemy, out var def))
        {
            for (int i = 0; i < Math.Max(1, ev.Count); i++)
            {
                var e = SpawnEnemy(def, RandomRingPosition());
                if (ev.IsBoss)
                {
                    _boss = e;
                    bossId = e.Id;
                }
            }
        }
        Events.Add(new BattleEvent { StageEvent = new StageEventEvent { Message = ev.Message, IsBoss = ev.IsBoss, BossId = bossId } });
    }

    private EnemyDef? PickWaveEnemy(WaveDef wave)
    {
        float total = 0f;
        foreach (var e in wave.Enemies) total += MathF.Max(0f, e.Weight);
        if (total <= 0f) return null;

        float roll = (float)_rng.NextDouble() * total;
        foreach (var e in wave.Enemies)
        {
            roll -= MathF.Max(0f, e.Weight);
            if (roll <= 0f && _data.Enemies.TryGetValue(e.Enemy, out var def)) return def;
        }
        return _data.Enemies.TryGetValue(wave.Enemies[^1].Enemy, out var last) ? last : null;
    }

    // 살아있는 플레이어 한 명을 골라 그 주위 스폰 링 위의 임의 위치
    private Vector2 RandomRingPosition()
    {
        var center = RandomTargetablePlayer()?.Pos ?? Vector2.Zero;
        float angle = (float)_rng.NextDouble() * MathF.PI * 2f;
        return center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * _b.SpawnRingRadius;
    }

    private PlayerEntity? RandomTargetablePlayer()
    {
        int count = 0;
        foreach (var p in Players) if (p.IsTargetable) count++;
        if (count == 0) return null;
        int pick = _rng.Next(count);
        foreach (var p in Players)
        {
            if (!p.IsTargetable) continue;
            if (pick-- == 0) return p;
        }
        return null;
    }

    private bool AnyTargetablePlayer()
    {
        foreach (var p in Players) if (p.IsTargetable) return true;
        return false;
    }

    // ───────────────────────── 적 ─────────────────────────

    public EnemyEntity SpawnEnemy(EnemyDef def, Vector2 pos)
    {
        var e = _enemyPool.Count > 0 ? _enemyPool.Pop() : new EnemyEntity();
        e.Reset(_nextEnemyId++, def, pos, _rng.NextDouble() < 0.5 ? 1 : -1);
        e.ListIndex = Enemies.Count;
        Enemies.Add(e);
        _grid.MarkDirty();
        _grid.NoteRadius(e.Radius);
        Events.Add(new BattleEvent { EnemySpawn = new EnemySpawnEvent { Id = e.Id, EnemyId = def.Id, X = Q(pos.X), Y = Q(pos.Y) } });
        return e;
    }

    private void DespawnEnemy(EnemyEntity e, int reason)
    {
        int idx = e.ListIndex;
        if (idx < 0) return;
        int last = Enemies.Count - 1;
        var moved = Enemies[last];
        Enemies[idx] = moved;
        moved.ListIndex = idx;
        Enemies.RemoveAt(last);
        e.ListIndex = -1;
        e.Alive = false;
        _grid.MarkDirty();
        if (ReferenceEquals(_boss, e)) _boss = null;
        Events.Add(new BattleEvent { EnemyDespawn = new EnemyDespawnEvent { Id = e.Id, Reason = reason, X = Q(e.Pos.X), Y = Q(e.Pos.Y) } });
        _enemyPool.Push(e);
    }

    private void StepEnemies(float dt)
    {
        int n = Enemies.Count;
        if (n == 0) return;

        _grid.Build(Enemies);

        float tooFarSqr = _b.TooFarDistance * _b.TooFarDistance;

        for (int i = 0; i < n && i < Enemies.Count; i++)
        {
            EnemyEntity e = Enemies[i];
            Vector2 p = _grid.CachedPosition(i);
            float r = e.Radius;

            // 가장 가까운 (살아있는) 플레이어가 표적
            PlayerEntity? target = null;
            float distSqr = float.MaxValue;
            foreach (var cand in Players)
            {
                if (!cand.IsTargetable) continue;
                float ds = (cand.Pos - p).LengthSquared();
                if (ds < distSqr) { distSqr = ds; target = cand; }
            }
            if (target == null) return; // 표적이 없으면 CheckEnd가 끝낸다

            if (distSqr > tooFarSqr)
            {
                // 너무 멀어진 적은 스폰 링으로 재배치 (유니티 EnemyTooFar → Teleport)
                var np = RandomRingPosition();
                Events.Add(new BattleEvent { EnemyDespawn = new EnemyDespawnEvent { Id = e.Id, Reason = 1, X = Q(e.Pos.X), Y = Q(e.Pos.Y) } });
                e.Pos = np;
                e.KnockVelocity = Vector2.Zero;
                Events.Add(new BattleEvent { EnemySpawn = new EnemySpawnEvent { Id = e.Id, EnemyId = e.Def.Id, X = Q(np.X), Y = Q(np.Y) } });
                continue;
            }

            Vector2 toTarget = target.Pos - p;
            float stop = _b.PlayerRadius + r;
            bool stunned = TickStun(e, dt);

            if (!stunned)
            {
                float contact = stop + _b.ContactPadding;
                if (distSqr <= contact * contact)
                    TryContactDamage(target, e.Def.ContactDamage);
            }

            Vector2 desired = Vector2.Zero;
            Vector2 dirToTarget = distSqr > 1e-6f ? toTarget / MathF.Sqrt(distSqr) : new Vector2(1f, 0f);

            if (stunned)
                desired = Vector2.Zero;
            else if (e.Def.ChargeInterval > 0f && TickCharge(e, dt, dirToTarget, out Vector2 chargeVelocity))
                desired = chargeVelocity;
            else if (e.Def.AttackRange > 0f)
                desired = MoveAsArcher(e, dirToTarget, distSqr, dt);
            else if (distSqr > stop * stop)
                desired = dirToTarget * e.Def.MoveSpeed;

            Vector2 push = _grid.ComputePush(i, p, r);
            Vector2 sep = ClampMagnitude(push * _b.SeparationStrength, _b.MaxSeparationSpeed);
            Vector2 velocity = desired + sep + TickKnockback(e, dt);

            e.Pos = p + velocity * dt;
            e.Moving = desired.LengthSquared() > 0.0025f;
            if (desired.X > 0.05f) e.FaceLeft = false;
            else if (desired.X < -0.05f) e.FaceLeft = true;
        }
    }

    private Vector2 MoveAsArcher(EnemyEntity e, Vector2 dirToTarget, float distSqr, float dt)
    {
        float range = e.Def.AttackRange;
        float dist = MathF.Sqrt(distSqr);

        if (TickShoot(e, dt, dist <= range * 1.15f))
            SpawnEnemyProjectile(e, dirToTarget);

        if (dist > range) return dirToTarget * e.Def.MoveSpeed;
        if (dist < range * 0.65f) return -dirToTarget * (e.Def.MoveSpeed * 0.7f);
        return Vector2.Zero;
    }

    private bool TickShoot(EnemyEntity e, float dt, bool canShoot)
    {
        float interval = MathF.Max(0.1f, e.Def.FireInterval);
        float warnAt = MathF.Max(0f, interval - _b.ShotWarningSeconds);
        e.ShootClock += dt;

        if (!canShoot)
        {
            e.ShootClock = MathF.Min(e.ShootClock, warnAt);
            e.Telegraphing = false;
            return false;
        }

        if (e.ShootClock >= interval)
        {
            e.ShootClock = 0f;
            e.Telegraphing = false;
            return true;
        }

        e.Telegraphing = e.ShootClock >= warnAt;
        return false;
    }

    private static bool TickStun(EnemyEntity e, float dt)
    {
        if (e.StunTimer <= 0f) return false;
        e.StunTimer -= dt;
        return e.StunTimer > 0f;
    }

    private static bool TickCharge(EnemyEntity e, float dt, Vector2 dirToPlayer, out Vector2 velocity)
    {
        velocity = Vector2.Zero;
        switch (e.Phase)
        {
            case ChargePhase.Chase:
                e.ChargeClock += dt;
                if (e.ChargeClock >= e.Def.ChargeInterval)
                {
                    e.Phase = ChargePhase.Windup;
                    e.PhaseTimer = e.Def.ChargeWindup;
                    e.ChargeDir = dirToPlayer;
                    return true;
                }
                return false;

            case ChargePhase.Windup:
                e.ChargeDir = dirToPlayer;
                e.PhaseTimer -= dt;
                if (e.PhaseTimer <= 0f)
                {
                    e.Phase = ChargePhase.Charge;
                    e.PhaseTimer = e.Def.ChargeDuration;
                }
                return true;

            default:
                e.PhaseTimer -= dt;
                velocity = e.ChargeDir * (e.Def.MoveSpeed * e.Def.ChargeSpeedMultiplier);
                if (e.PhaseTimer <= 0f)
                {
                    e.Phase = ChargePhase.Chase;
                    e.ChargeClock = 0f;
                }
                return true;
        }
    }

    private Vector2 TickKnockback(EnemyEntity e, float dt)
    {
        if (e.KnockVelocity == Vector2.Zero) return Vector2.Zero;
        Vector2 current = e.KnockVelocity;
        e.KnockVelocity *= MathF.Exp(-_b.KnockbackDecay * dt);
        if (e.KnockVelocity.LengthSquared() < 0.01f) e.KnockVelocity = Vector2.Zero;
        return current;
    }

    public void Knockback(EnemyEntity e, Vector2 velocity)
    {
        if (!e.Alive) return;
        e.KnockVelocity = ClampMagnitude(e.KnockVelocity + velocity, _b.MaxKnockbackSpeed);
    }

    public void Stun(EnemyEntity e, float seconds)
    {
        if (!e.Alive || seconds <= 0f) return;
        e.StunTimer = MathF.Max(e.StunTimer, seconds);
        e.ShootClock = 0f;
        e.Telegraphing = false;
        e.Phase = ChargePhase.Chase;
        e.ChargeClock = 0f;
    }

    // 적에게 피해. 죽으면 경험치 젬을 떨어뜨리고 제거한다. attacker는 킬 집계용.
    public void DamageEnemy(EnemyEntity e, float amount, PlayerEntity? attacker)
    {
        if (!e.Alive || amount <= 0f) return;

        e.Hp -= amount;
        int hpPct = e.Hp > 0f ? Math.Max(1, (int)MathF.Round(e.Hp / e.Def.MaxHp * 100f)) : 0;
        Events.Add(new BattleEvent { Damage = new DamageEvent { TargetKind = 0, Id = e.Id, Amount = (int)MathF.Round(amount), HpPct = hpPct, X = Q(e.Pos.X), Y = Q(e.Pos.Y) } });

        if (e.Hp <= 0f)
        {
            e.Alive = false;
            if (attacker != null) attacker.Kills++;
            SpawnGem(e.Pos, e.Def.ExpReward, attacker);
            DespawnEnemy(e, 0);
        }
    }

    // ───────────────────────── 플레이어 ─────────────────────────

    private void StepPlayers(float dt)
    {
        foreach (var p in Players)
        {
            if (p.IsDead || !p.Connected)
            {
                p.Moving = false;
                continue;
            }

            float speed = _b.PlayerMoveSpeed * p.MoveSpeedMultiplier;
            Vector2 velocity = p.MoveInput * speed;
            p.Pos += velocity * dt;
            p.Moving = velocity.LengthSquared() > 0.0001f;
            if (p.Moving) p.Facing = PickDirection(p.MoveInput, p.Facing);
            p.SkillPressed = false; // 장수 스킬은 2차 범위

            if (p.SkillInvulnTimer > 0f) p.SkillInvulnTimer -= dt;
            if (p.InvulnTimer > 0f) p.InvulnTimer -= dt;

            if (p.RegenPerSecond > 0f)
            {
                float max = p.MaxHp(_b);
                if (p.Hp < max)
                {
                    p.RegenAccumulator += p.RegenPerSecond * dt;
                    if (p.RegenAccumulator >= _b.RegenTickSeconds * p.RegenPerSecond)
                    {
                        HealPlayer(p, p.RegenAccumulator);
                        p.RegenAccumulator = 0f;
                    }
                }
            }
        }
    }

    private static Facing PickDirection(Vector2 dir, Facing current)
    {
        if (dir.LengthSquared() < 1e-6f) return current;
        if (MathF.Abs(dir.X) > MathF.Abs(dir.Y))
            return dir.X < 0f ? Facing.Left : Facing.Right;
        return dir.Y > 0f ? Facing.Up : Facing.Down;
    }

    public void HealPlayer(PlayerEntity p, float amount)
    {
        if (p.IsDead || amount <= 0f) return;
        p.Hp = MathF.Min(p.MaxHp(_b), p.Hp + amount);
    }

    // 접촉 피해: 피격 무적 중이면 무시, 적용되면 무적 시간이 시작된다
    public bool TryContactDamage(PlayerEntity p, float amount)
    {
        if (!p.CanTakeContactDamage) return false;
        p.InvulnTimer = _b.InvulnerableTime;
        DamagePlayer(p, amount);
        return true;
    }

    public void DamagePlayer(PlayerEntity p, float amount)
    {
        if (p.IsDead || amount <= 0f || p.SkillInvulnTimer > 0f) return;

        p.Hp = MathF.Max(0f, p.Hp - amount);
        float max = p.MaxHp(_b);
        int hpPct = p.Hp > 0f ? Math.Max(1, (int)MathF.Round(p.Hp / max * 100f)) : 0;
        Events.Add(new BattleEvent { Damage = new DamageEvent { TargetKind = 1, Id = p.Slot, Amount = (int)MathF.Round(amount), HpPct = hpPct, X = Q(p.Pos.X), Y = Q(p.Pos.Y) } });

        if (p.Hp <= 0f)
        {
            p.IsDead = true;
            p.Moving = false;
            Events.Add(new BattleEvent { PlayerDied = new PlayerDiedEvent { Slot = p.Slot } });
        }
    }

    // PlayerStats.Recalculate 이식. 메타 강화는 서버 저장 연동 전까지 0.
    public void RecalculateStats(PlayerEntity p)
    {
        float damage = 0f, cooldown = 0f, speed = 0f, pickup = 0f, exp = 0f, maxHp = 0f, regen = 0f;
        foreach (var kv in p.PassiveLevels)
        {
            if (!_data.Passives.TryGetValue(kv.Key, out var def)) continue;
            float v = def.ValuePerLevel * kv.Value;
            switch (def.Type)
            {
                case PassiveType.Damage: damage += v; break;
                case PassiveType.Cooldown: cooldown += v; break;
                case PassiveType.MoveSpeed: speed += v; break;
                case PassiveType.PickupRadius: pickup += v; break;
                case PassiveType.ExpGain: exp += v; break;
                case PassiveType.MaxHp: maxHp += v; break;
                case PassiveType.Regen: regen += v; break;
            }
        }

        var h = p.Hero;
        float prevBonus = p.MaxHpBonus;
        p.DamageMultiplier = (1f + damage) * h.DamageMultiplier;
        p.CooldownMultiplier = MathF.Max(0.3f, 1f - cooldown);
        p.MoveSpeedMultiplier = (1f + speed) * h.MoveSpeedMultiplier;
        p.PickupRadiusMultiplier = (1f + pickup) * h.PickupRadiusMultiplier;
        p.ExpMultiplier = (1f + exp) * h.ExpMultiplier;
        p.MaxHpBonus = maxHp + h.MaxHpBonus;
        p.RegenPerSecond = regen;

        // 최대 체력이 늘어난 만큼 회복 (PlayerHealth.OnStatsChanged)
        float delta = p.MaxHpBonus - prevBonus;
        if (delta > 0f && !p.IsDead) p.Hp += delta;
        p.Hp = MathF.Min(p.Hp, p.MaxHp(_b));
    }

    public bool AddPassive(PlayerEntity p, PassiveDef passive)
    {
        int lv = p.GetPassiveLevel(passive.Id);
        if (lv >= passive.MaxLevel) return false;
        p.PassiveLevels[passive.Id] = lv + 1;
        RecalculateStats(p);
        EmitLoadout(p);
        return true;
    }

    public WeaponInstance AddWeapon(PlayerEntity p, WeaponDef def)
    {
        var existing = p.FindWeapon(def.Id);
        if (existing != null)
        {
            LevelUpWeapon(p, existing);
            return existing;
        }

        var w = new WeaponInstance(def);
        WeaponSystem.OnInitialized(this, p, w);
        p.Weapons.Add(w);
        EmitLoadout(p);
        return w;
    }

    public bool LevelUpWeapon(PlayerEntity p, WeaponInstance w)
    {
        if (w.IsMaxLevel) return false;
        w.Level++;
        EmitLoadout(p);
        return true;
    }

    public WeaponInstance? EvolveWeapon(PlayerEntity p, WeaponDef from, WeaponDef to)
    {
        var old = p.FindWeapon(from.Id);
        if (old == null || p.FindWeapon(to.Id) != null) return null;

        p.Weapons.Remove(old);
        p.EvolvedBases.Add(from.Id);
        return AddWeapon(p, to);
    }

    // 보유 무기/패시브 전체를 이벤트로 알린다 (획득·레벨업·진화 때마다)
    public void EmitLoadout(PlayerEntity p)
    {
        var ev = new LoadoutChangedEvent { Slot = p.Slot };
        foreach (var w in p.Weapons)
            ev.Weapons.Add(new LoadoutEntry { Id = w.Def.Id, Level = w.Level });
        foreach (var kv in p.PassiveLevels)
            ev.Passives.Add(new LoadoutEntry { Id = kv.Key, Level = kv.Value });
        Events.Add(new BattleEvent { LoadoutChanged = ev });
    }

    public void AddAreaFx(PlayerEntity owner, WeaponInstance w, Vector2 pos, float radius, float durationSeconds, int kind)
    {
        Events.Add(new BattleEvent
        {
            AreaFx = new AreaFxEvent
            {
                Slot = owner.Slot, WeaponId = w.Def.Id, X = Q(pos.X), Y = Q(pos.Y),
                Radius = Q(radius), DurationMs = (int)(durationSeconds * 1000f), Kind = kind,
            }
        });
    }

    // ───────────────────────── 경험치 / 레벨업 ─────────────────────────

    public int ExpRequiredFor(int level) => _b.ExpBase + (level - 1) * _b.ExpStep;

    public void AddExp(PlayerEntity p, float amount)
    {
        if (amount <= 0f || p.IsDead) return;
        int gained = Math.Max(1, (int)MathF.Round(amount * p.ExpMultiplier));
        p.Exp += gained;

        while (p.Exp >= ExpRequiredFor(p.Level))
        {
            p.Exp -= ExpRequiredFor(p.Level);
            p.Level++;
            Events.Add(new BattleEvent { PlayerLevelUp = new PlayerLevelUpEvent { Slot = p.Slot, Level = p.Level } });
            _queuedLevelUps[p.Slot]++;
        }
        TryOfferChoice(p);
    }

    private void TryOfferChoice(PlayerEntity p)
    {
        if (!p.Connected || p.IsDead) { _queuedLevelUps[p.Slot] = 0; return; }
        if (_queuedLevelUps[p.Slot] <= 0 || _pendingChoices.ContainsKey(p.Slot)) return;

        _queuedLevelUps[p.Slot]--;
        var options = UpgradeGenerator.Generate(this, p);
        if (options.Count == 0) return;

        var pending = new PendingChoice
        {
            ChoiceId = _nextChoiceId++,
            Options = options,
            RemainingSeconds = ChoiceTimeoutMs / 1000f,
        };
        _pendingChoices[p.Slot] = pending;

        var noti = new LevelUpChoiceNoti { ChoiceId = pending.ChoiceId, TimeoutMs = ChoiceTimeoutMs };
        for (int i = 0; i < options.Count; i++)
        {
            var o = options[i];
            noti.Options.Add(new UpgradeChoice
            {
                Index = i,
                Kind = (int)o.Kind,
                Title = o.Title,
                Description = o.Description,
                Id = o.Id,
                Level = o.Level,
                EvolvedWeaponId = o.EvolvedWeaponId,
            });
        }
        OnLevelUpChoice?.Invoke(p.Slot, noti);
    }

    public bool SelectUpgrade(int slot, int choiceId, int index)
    {
        var p = GetPlayer(slot);
        if (p == null) return false;
        if (!_pendingChoices.TryGetValue(slot, out var pending) || pending.ChoiceId != choiceId) return false;
        if (index < 0 || index >= pending.Options.Count) return false;
        ResolveChoice(p, pending, index);
        return true;
    }

    private void ResolveChoice(PlayerEntity p, PendingChoice pending, int index)
    {
        _pendingChoices.Remove(p.Slot);
        pending.Options[index].Apply();
        TryOfferChoice(p);
    }

    private void StepChoiceTimeouts(float dt)
    {
        // 열거 중 수정 방지를 위해 복사
        foreach (var kv in _pendingChoices.ToArray())
        {
            kv.Value.RemainingSeconds -= dt;
            if (kv.Value.RemainingSeconds > 0f) continue;
            var p = GetPlayer(kv.Key);
            if (p != null) ResolveChoice(p, kv.Value, 0);
            else _pendingChoices.Remove(kv.Key);
        }
    }

    // ───────────────────────── 투사체 ─────────────────────────

    public void SpawnProjectile(PlayerEntity owner, WeaponInstance w, Vector2 pos, Vector2 dir, float damage)
    {
        var def = w.Def;
        var p = new ProjectileEntity
        {
            Id = _nextProjectileId++,
            OwnerSlot = owner.Slot,
            SourceId = def.Id,
            Pos = pos,
            Dir = dir,
            Speed = def.ProjectileSpeed,
            Damage = damage,
            Radius = def.Size,
            LifeLeft = def.Duration,
            PierceLeft = Math.Max(1, def.Pierce),
            Knockback = def.Knockback,
        };
        Projectiles.Add(p);
        Events.Add(new BattleEvent
        {
            ProjectileSpawn = new ProjectileSpawnEvent
            {
                Id = p.Id, OwnerSlot = owner.Slot, WeaponId = def.Id,
                X = Q(pos.X), Y = Q(pos.Y), DirX = QDir(dir.X), DirY = QDir(dir.Y),
                Speed = Q(p.Speed), LifetimeMs = (int)(p.LifeLeft * 1000f),
            }
        });
    }

    private void SpawnEnemyProjectile(EnemyEntity e, Vector2 dir)
    {
        var def = e.Def;
        var p = new ProjectileEntity
        {
            Id = _nextProjectileId++,
            OwnerSlot = -1,
            SourceId = def.Id,
            Pos = e.Pos,
            Dir = dir,
            Speed = def.ProjectileSpeed,
            Damage = def.ProjectileDamage,
            Radius = def.ProjectileSize,
            LifeLeft = def.ProjectileLifetime,
            PierceLeft = 1,
        };
        Projectiles.Add(p);
        Events.Add(new BattleEvent
        {
            ProjectileSpawn = new ProjectileSpawnEvent
            {
                Id = p.Id, OwnerSlot = -1, EnemyId = def.Id,
                X = Q(p.Pos.X), Y = Q(p.Pos.Y), DirX = QDir(dir.X), DirY = QDir(dir.Y),
                Speed = Q(p.Speed), LifetimeMs = (int)(p.LifeLeft * 1000f),
            }
        });
    }

    private void StepProjectiles(float dt)
    {
        for (int i = Projectiles.Count - 1; i >= 0; i--)
        {
            var p = Projectiles[i];
            p.Pos += p.Dir * (p.Speed * dt);
            p.LifeLeft -= dt;

            bool remove = p.LifeLeft <= 0f;
            if (!remove)
            {
                if (p.OwnerSlot >= 0)
                {
                    var owner = GetPlayer(p.OwnerSlot);
                    _hits.Clear();
                    _grid.OverlapCircle(p.Pos, p.Radius, _hits);
                    foreach (var e in _hits)
                    {
                        if (!p.AlreadyHit.Add(e.Id)) continue;
                        if (p.Knockback > 0f)
                        {
                            Vector2 away = e.Pos - p.Pos;
                            if (away.LengthSquared() < 1e-6f) away = p.Dir;
                            Knockback(e, Vector2.Normalize(away) * p.Knockback);
                        }
                        DamageEnemy(e, p.Damage, owner);
                        if (--p.PierceLeft <= 0) { remove = true; break; }
                    }
                }
                else
                {
                    float reach = p.Radius + _b.EnemyProjectilePlayerRadius;
                    foreach (var pl in Players)
                    {
                        if (!pl.IsTargetable) continue;
                        if ((pl.Pos - p.Pos).LengthSquared() > reach * reach) continue;
                        TryContactDamage(pl, p.Damage);
                        remove = true;
                        break;
                    }
                }
            }

            if (remove)
            {
                Events.Add(new BattleEvent { ProjectileDespawn = new ProjectileDespawnEvent { Id = p.Id, X = Q(p.Pos.X), Y = Q(p.Pos.Y) } });
                int last = Projectiles.Count - 1;
                Projectiles[i] = Projectiles[last];
                Projectiles.RemoveAt(last);
            }
        }
    }

    // ───────────────────────── 무기 / 장판 / 지연 공격 ─────────────────────────

    private void StepWeapons(float dt)
    {
        foreach (var p in Players)
        {
            if (p.IsDead || !p.Connected) continue;
            foreach (var w in p.Weapons)
                WeaponSystem.Step(this, p, w, dt);
        }
    }

    public void AddZone(PlayerEntity owner, WeaponInstance w, Vector2 pos, float radius, float duration)
    {
        Zones.Add(new ZoneEntity { OwnerSlot = owner.Slot, Weapon = w, Pos = pos, Radius = radius, Remaining = duration });
    }

    public void AddDelayedStrike(PlayerEntity owner, WeaponInstance w, Vector2 pos, float radius)
    {
        Strikes.Add(new DelayedStrike { OwnerSlot = owner.Slot, Weapon = w, Pos = pos, Radius = radius, Remaining = RainDelaySeconds });
    }

    private void StepZones(float dt)
    {
        for (int i = Zones.Count - 1; i >= 0; i--)
        {
            var z = Zones[i];
            z.Remaining -= dt;
            z.TickTimer += dt;
            var owner = GetPlayer(z.OwnerSlot);
            if (owner != null && z.TickTimer >= z.Weapon.Def.TickInterval)
            {
                z.TickTimer = 0f;
                StrikeCircle(owner, z.Weapon, z.Pos, z.Radius, z.Pos, z.Weapon.Damage(owner), 0f);
            }
            if (z.Remaining <= 0f)
            {
                Zones[i] = Zones[^1];
                Zones.RemoveAt(Zones.Count - 1);
            }
        }
    }

    private void StepStrikes(float dt)
    {
        for (int i = Strikes.Count - 1; i >= 0; i--)
        {
            var s = Strikes[i];
            s.Remaining -= dt;
            if (s.Remaining > 0f) continue;
            var owner = GetPlayer(s.OwnerSlot);
            if (owner != null)
            {
                AddAreaFx(owner, s.Weapon, s.Pos, s.Radius, 0.3f, 2);
                StrikeCircle(owner, s.Weapon, s.Pos, s.Radius, s.Pos, s.Weapon.Damage(owner), 1f);
            }
            Strikes[i] = Strikes[^1];
            Strikes.RemoveAt(Strikes.Count - 1);
        }
    }

    // 원 안의 적 전부에게 피해 + knockOrigin 반대쪽으로 넉백
    public int StrikeCircle(PlayerEntity owner, WeaponInstance w, Vector2 center, float radius, Vector2 knockOrigin, float damage, float knockScale)
    {
        _hits.Clear();
        _grid.OverlapCircle(center, radius, _hits);
        int count = _hits.Count;
        for (int i = 0; i < count; i++)
        {
            var e = _hits[i];
            Knock(w, e, knockOrigin, knockScale);
            DamageEnemy(e, damage, owner);
        }
        return count;
    }

    // origin에서 dir 방향 길이 length, 반폭 halfWidth의 직선 위 적 전부에게 피해 + 넉백
    public int StrikeLine(PlayerEntity owner, WeaponInstance w, Vector2 origin, Vector2 dir, float length, float halfWidth, float damage)
    {
        _hits.Clear();
        _grid.OverlapCircle(origin + dir * (length * 0.5f), length * 0.5f + halfWidth, _hits);
        int hit = 0;
        int count = _hits.Count;
        for (int i = 0; i < count; i++)
        {
            var e = _hits[i];
            Vector2 rel = e.Pos - origin;
            float along = Vector2.Dot(rel, dir);
            float side = MathF.Abs(rel.X * dir.Y - rel.Y * dir.X);

            if (along < -e.Radius || along > length + e.Radius) continue;
            if (side > halfWidth + e.Radius) continue;

            Knock(w, e, origin, 1f);
            DamageEnemy(e, damage, owner);
            hit++;
        }
        return hit;
    }

    private void Knock(WeaponInstance w, EnemyEntity e, Vector2 origin, float scale)
    {
        if (w.Def.Knockback <= 0f || scale <= 0f || !e.Alive) return;
        Vector2 away = e.Pos - origin;
        if (away.LengthSquared() < 1e-6f) away = new Vector2(1f, 0f);
        Knockback(e, Vector2.Normalize(away) * (w.Def.Knockback * scale));
    }

    public int FindNearest(Vector2 from, float range, int max)
    {
        int n = Math.Min(max, _nearestBuffer.Length);
        Array.Clear(_nearestBuffer);
        var slice = n == _nearestBuffer.Length ? _nearestBuffer : new EnemyEntity?[n];
        int found = _grid.FindNearest(from, range, slice);
        if (!ReferenceEquals(slice, _nearestBuffer)) Array.Copy(slice, _nearestBuffer, n);
        return found;
    }

    public EnemyEntity? NearestResult(int i) => _nearestBuffer[i];

    // ───────────────────────── 젬 ─────────────────────────

    private void SpawnGem(Vector2 pos, int value, PlayerEntity? killer)
    {
        if (value <= 0) return;
        if (Gems.Count >= _b.MaxGems)
        {
            // 한도 초과: 처치한 플레이어(없으면 가장 가까운 플레이어)에게 즉시 지급
            var receiver = killer != null && killer.IsTargetable ? killer : NearestTargetablePlayer(pos, float.MaxValue);
            if (receiver != null) AddExp(receiver, value);
            return;
        }
        var g = new GemEntity { Id = _nextGemId++, Pos = pos, Value = value };
        Gems.Add(g);
        Events.Add(new BattleEvent { GemSpawn = new GemSpawnEvent { Id = g.Id, X = Q(pos.X), Y = Q(pos.Y), Value = value } });
    }

    private PlayerEntity? NearestTargetablePlayer(Vector2 from, float maxDist)
    {
        PlayerEntity? best = null;
        float bestSqr = maxDist * maxDist;
        foreach (var p in Players)
        {
            if (!p.IsTargetable) continue;
            float d = (p.Pos - from).LengthSquared();
            if (d <= bestSqr) { bestSqr = d; best = p; }
        }
        return best;
    }

    private void StepGems(float dt)
    {
        for (int i = Gems.Count - 1; i >= 0; i--)
        {
            var g = Gems[i];
            PlayerEntity? target = g.Attracted ? GetPlayer(g.TargetSlot) : null;
            if (target != null && !target.IsTargetable) { target = null; g.Attracted = false; }

            if (!g.Attracted)
            {
                // 획득 범위는 플레이어마다 다르므로 각자 검사
                foreach (var p in Players)
                {
                    if (!p.IsTargetable) continue;
                    float radius = _b.MagnetRadius * p.PickupRadiusMultiplier;
                    if ((p.Pos - g.Pos).LengthSquared() > radius * radius) continue;
                    target = p;
                    g.Attracted = true;
                    g.TargetSlot = p.Slot;
                    g.Speed = _b.GemStartSpeed;
                    break;
                }
                if (target == null) continue;
            }

            Vector2 toTarget = target!.Pos - g.Pos;
            float dist = toTarget.Length();
            g.Speed = MathF.Min(_b.GemMaxSpeed, g.Speed + _b.GemAcceleration * dt);
            float step = g.Speed * dt;

            if (dist <= _b.CollectRadius || step >= dist)
            {
                AddExp(target, g.Value);
                Events.Add(new BattleEvent { GemCollect = new GemCollectEvent { Id = g.Id, Slot = target.Slot } });
                Gems[i] = Gems[^1];
                Gems.RemoveAt(Gems.Count - 1);
                continue;
            }

            g.Pos += toTarget / dist * step;
        }
    }

    // ───────────────────────── 종료 ─────────────────────────

    private void CheckEnd()
    {
        if (Ended) return;
        if (!AnyTargetablePlayer())
        {
            Ended = true;
            Cleared = false;
        }
    }

    public int ScoreOf(PlayerEntity p) => p.Kills * 10 + (p.Level - 1) * 100 + (int)Elapsed * 5;

    // ───────────────────────── 스냅샷 ─────────────────────────

    // viewer 기준 관심 영역 안의 적/젬만 담는다. Events는 공용이므로 같은 객체를 모든 수신자 메시지에 넣는다.
    public BattleSnapshotNoti BuildSnapshot(PlayerEntity viewer)
    {
        var snap = new BattleSnapshotNoti
        {
            Tick = Tick,
            ElapsedMs = (int)(Elapsed * 1000f),
            Enemies = new EnemyStates(),
            Gems = new GemStates(),
            Stage = new StageState
            {
                WaveNumber = _waveIndex + 1,
                WaveName = _wave?.Name ?? "",
                RemainingMs = (int)(MathF.Max(0f, _stage.Duration - Elapsed) * 1000f),
                BossId = _boss != null && _boss.Alive ? _boss.Id : 0,
                BossHpPct = _boss != null && _boss.Alive ? (int)MathF.Round(_boss.Hp / _boss.Def.MaxHp * 100f) : 0,
                EnemyCount = Enemies.Count,
                Paused = Paused,
            },
        };

        foreach (var p in Players)
        {
            snap.Players.Add(new PlayerState
            {
                Slot = p.Slot,
                X = Q(p.Pos.X), Y = Q(p.Pos.Y),
                Hp = (int)MathF.Ceiling(p.Hp), MaxHp = (int)MathF.Round(p.MaxHp(_b)),
                Level = p.Level, Exp = p.Exp, ExpToNext = ExpRequiredFor(p.Level),
                Facing = (int)p.Facing, Flags = p.Flags, Kills = p.Kills,
                SkillCooldownMs = 0, LastInputSeq = p.LastInputSeq,
            });
        }
        foreach (var slot in _pendingChoices.Keys)
            snap.Stage.PausedBySlots.Add(slot);

        float aoiSqr = AoiRadius * AoiRadius;
        Vector2 center = viewer.Pos;
        var es = snap.Enemies;
        foreach (var e in Enemies)
        {
            if ((e.Pos - center).LengthSquared() > aoiSqr) continue;
            es.Ids.Add(e.Id);
            es.Xs.Add(Q(e.Pos.X));
            es.Ys.Add(Q(e.Pos.Y));
            es.Flags.Add(e.Flags);
        }

        var gs = snap.Gems;
        foreach (var g in Gems)
        {
            if (!g.Attracted) continue;
            if ((g.Pos - center).LengthSquared() > aoiSqr) continue;
            gs.Ids.Add(g.Id);
            gs.Xs.Add(Q(g.Pos.X));
            gs.Ys.Add(Q(g.Pos.Y));
        }

        snap.Events.AddRange(Events);
        return snap;
    }

    // ───────────────────────── 유틸 ─────────────────────────

    public static int Q(float v) => (int)MathF.Round(v * 100f);
    public static int QDir(float v) => (int)MathF.Round(v * 1000f);

    public static Vector2 ClampMagnitude(Vector2 v, float max)
    {
        float sqr = v.LengthSquared();
        if (sqr <= max * max) return v;
        return v / MathF.Sqrt(sqr) * max;
    }

    public static Vector2 DirectionOf(float angleDegrees)
    {
        float rad = angleDegrees * (MathF.PI / 180f);
        return new Vector2(MathF.Cos(rad), MathF.Sin(rad));
    }
}
