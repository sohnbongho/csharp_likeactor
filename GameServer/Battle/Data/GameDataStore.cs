using Library.Logger;
using System.Text.Json;

namespace GameServer.Battle.Data;

// 기동 시 GameServer/Data/*.json을 읽어 보관한다. 읽기 전용이라 모든 룸 스레드가 공유한다.
public sealed class GameDataStore
{
    private static readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
    };

    public IReadOnlyDictionary<string, EnemyDef> Enemies { get; }
    public IReadOnlyDictionary<string, WeaponDef> Weapons { get; }
    public IReadOnlyDictionary<string, PassiveDef> Passives { get; }
    public IReadOnlyDictionary<string, EvolutionDef> Evolutions { get; }
    public IReadOnlyDictionary<string, HeroDef> Heroes { get; }
    public IReadOnlyDictionary<string, StageDef> Stages { get; }
    public UpgradeCatalogDef Catalog { get; }
    public BalanceDef Balance { get; }

    // 방 생성 시 스테이지를 지정하지 않으면 쓰는 기본 스테이지 (stages.json의 첫 항목)
    public string DefaultStageId { get; }
    public string DefaultHeroId { get; }

    private GameDataStore(
        Dictionary<string, EnemyDef> enemies, Dictionary<string, WeaponDef> weapons,
        Dictionary<string, PassiveDef> passives, Dictionary<string, EvolutionDef> evolutions,
        Dictionary<string, HeroDef> heroes, Dictionary<string, StageDef> stages,
        UpgradeCatalogDef catalog, BalanceDef balance, string defaultStageId, string defaultHeroId)
    {
        Enemies = enemies; Weapons = weapons; Passives = passives; Evolutions = evolutions;
        Heroes = heroes; Stages = stages; Catalog = catalog; Balance = balance;
        DefaultStageId = defaultStageId; DefaultHeroId = defaultHeroId;
    }

    public static GameDataStore Load(string dir)
    {
        var enemies = LoadList<EnemyDef>(Path.Combine(dir, "enemies.json")).ToDictionary(e => e.Id);
        var weapons = LoadList<WeaponDef>(Path.Combine(dir, "weapons.json")).ToDictionary(w => w.Id);
        var passives = LoadList<PassiveDef>(Path.Combine(dir, "passives.json")).ToDictionary(p => p.Id);
        var evolutions = LoadList<EvolutionDef>(Path.Combine(dir, "evolutions.json")).ToDictionary(e => e.Id);
        var heroes = LoadList<HeroDef>(Path.Combine(dir, "heroes.json")).ToDictionary(h => h.Id);
        var stageList = LoadList<StageDef>(Path.Combine(dir, "stages.json"));
        var stages = stageList.ToDictionary(s => s.Id);
        var catalog = LoadObject<UpgradeCatalogDef>(Path.Combine(dir, "upgrade_catalog.json")) ?? new UpgradeCatalogDef();
        var balance = LoadObject<BalanceDef>(Path.Combine(dir, "balance.json")) ?? new BalanceDef();

        if (stageList.Count == 0)
            throw new InvalidOperationException($"stages.json에 스테이지가 없습니다: {dir}");
        if (heroes.Count == 0)
            throw new InvalidOperationException($"heroes.json에 장수가 없습니다: {dir}");

        // 참조 무결성 검사: 끊긴 참조는 경고만 남기고 기동은 계속한다.
        foreach (var s in stageList)
        {
            foreach (var w in s.Waves)
                w.Enemies.RemoveAll(e => !enemies.ContainsKey(e.Enemy) && Warn($"스테이지 {s.Id} 웨이브 '{w.Name}'의 적 '{e.Enemy}' 없음"));
            s.Events.RemoveAll(e => !enemies.ContainsKey(e.Enemy) && Warn($"스테이지 {s.Id} 이벤트의 적 '{e.Enemy}' 없음"));
        }
        foreach (var h in heroes.Values)
        {
            if (h.StartingWeapon != "" && !weapons.ContainsKey(h.StartingWeapon))
                Warn($"장수 {h.Id}의 시작 무기 '{h.StartingWeapon}' 없음");
            h.Weapons.RemoveAll(w => !weapons.ContainsKey(w) && Warn($"장수 {h.Id}의 무기 '{w}' 없음"));
        }
        catalog.Weapons.RemoveAll(w => !weapons.ContainsKey(w) && Warn($"카탈로그 무기 '{w}' 없음"));
        catalog.Passives.RemoveAll(p => !passives.ContainsKey(p) && Warn($"카탈로그 패시브 '{p}' 없음"));
        catalog.Evolutions.RemoveAll(e => !evolutions.ContainsKey(e) && Warn($"카탈로그 진화 '{e}' 없음"));

        var store = new GameDataStore(enemies, weapons, passives, evolutions, heroes, stages, catalog, balance,
            stageList[0].Id, heroes.Keys.First());

        _logger.Info(() => $"게임 데이터 로드: 적 {enemies.Count}, 무기 {weapons.Count}, 패시브 {passives.Count}, 진화 {evolutions.Count}, 장수 {heroes.Count}, 스테이지 {stages.Count} ({dir})");
        return store;
    }

    private static bool Warn(string message)
    {
        _logger.Warn(() => $"[게임 데이터] {message}");
        return true;
    }

    private static List<T> LoadList<T>(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"게임 데이터 파일이 없습니다. Scripts/export_unity_data.py로 내보내세요: {path}");
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<List<T>>(stream, _jsonOptions) ?? new List<T>();
    }

    private static T? LoadObject<T>(string path) where T : class
    {
        if (!File.Exists(path))
            return null;
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(stream, _jsonOptions);
    }
}
