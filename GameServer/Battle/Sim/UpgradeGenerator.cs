using GameServer.Battle.Data;

namespace GameServer.Battle.Sim;

public enum UpgradeKind { NewWeapon = 0, WeaponLevelUp = 1, Passive = 2, Heal = 3, Evolve = 4 }

public sealed class UpgradeOption
{
    public UpgradeKind Kind;
    public string Title = "";
    public string Description = "";
    public string Id = "";              // 무기 id / 패시브 id / 진화면 기본 무기 id
    public string EvolvedWeaponId = ""; // 진화 결과 무기
    public int Level;                   // 선택 후 레벨
    public Action Apply = () => { };
}

// 유니티 UpgradeGenerator 이식. 보유 상태를 보고 레벨업 선택지를 가중치 무작위로 만든다.
public static class UpgradeGenerator
{
    private struct Weighted
    {
        public UpgradeOption Option;
        public float Weight;
    }

    public static List<UpgradeOption> Generate(BattleWorld world, PlayerEntity p)
    {
        var data = world.Data;
        var catalog = data.Catalog;
        var b = world.Balance;
        int count = b.ChoiceCount;

        var evolutions = BuildEvolutionOptions(world, p);
        var candidates = new List<Weighted>();

        // 보유 무기 강화
        foreach (var w in p.Weapons)
        {
            if (w.IsMaxLevel) continue;
            var weapon = w;
            candidates.Add(new Weighted
            {
                Weight = catalog.WeaponLevelUpWeight,
                Option = new UpgradeOption
                {
                    Kind = UpgradeKind.WeaponLevelUp,
                    Id = weapon.Def.Id,
                    Level = weapon.Level + 1,
                    Title = $"{weapon.Def.DisplayName} Lv.{weapon.Level + 1}",
                    Description = weapon.NextLevelDescription(),
                    Apply = () => world.LevelUpWeapon(p, weapon),
                }
            });
        }

        // 새 무기 (장수의 무기 목록 안에서만. 목록이 비면 공용 무기 전부)
        if (p.Weapons.Count < b.MaxWeapons)
        {
            foreach (var id in catalog.Weapons)
            {
                if (!data.Weapons.TryGetValue(id, out var def)) continue;
                if (p.FindWeapon(id) != null || p.EvolvedBases.Contains(id)) continue;
                if (p.Hero.Weapons.Count > 0 && !p.Hero.Weapons.Contains(id)) continue;
                var wd = def;
                candidates.Add(new Weighted
                {
                    Weight = catalog.NewWeaponWeight,
                    Option = new UpgradeOption
                    {
                        Kind = UpgradeKind.NewWeapon,
                        Id = wd.Id,
                        Level = 1,
                        Title = $"새 무기: {wd.DisplayName}",
                        Description = wd.Description,
                        Apply = () => world.AddWeapon(p, wd),
                    }
                });
            }
        }

        // 패시브
        foreach (var id in catalog.Passives)
        {
            if (!data.Passives.TryGetValue(id, out var passive)) continue;
            int next = p.GetPassiveLevel(id) + 1;
            if (next > passive.MaxLevel) continue;
            var pd = passive;
            candidates.Add(new Weighted
            {
                Weight = catalog.PassiveWeight,
                Option = new UpgradeOption
                {
                    Kind = UpgradeKind.Passive,
                    Id = pd.Id,
                    Level = next,
                    Title = $"{pd.DisplayName} Lv.{next}",
                    Description = pd.Description,
                    Apply = () => world.AddPassive(p, pd),
                }
            });
        }

        var result = new List<UpgradeOption>(count);
        for (int i = 0; i < evolutions.Count && result.Count < count; i++) result.Add(evolutions[i]);
        DrawWeighted(world.Rng, candidates, count - result.Count, result);

        if (result.Count < count)
        {
            result.Add(new UpgradeOption
            {
                Kind = UpgradeKind.Heal,
                Title = "체력 회복",
                Description = $"최대 체력의 {b.HealRatio * 100f:0}% 회복",
                Apply = () => world.HealPlayer(p, p.MaxHp(b) * b.HealRatio),
            });
        }

        return result;
    }

    public static bool CanEvolve(BattleWorld world, EvolutionDef evo, PlayerEntity p)
    {
        if (!evo.IsValid) return false;
        if (p.FindWeapon(evo.EvolvedWeapon) != null) return false;
        if (!world.Data.Weapons.ContainsKey(evo.EvolvedWeapon)) return false;

        var w = p.FindWeapon(evo.BaseWeapon);
        if (w == null) return false;

        int needed = Math.Min(evo.RequiredLevel, w.Def.MaxLevel);
        return w.Level >= needed && p.GetPassiveLevel(evo.RequiredPassive) >= evo.RequiredPassiveLevel;
    }

    private static List<UpgradeOption> BuildEvolutionOptions(BattleWorld world, PlayerEntity p)
    {
        var data = world.Data;
        var options = new List<UpgradeOption>();
        foreach (var id in data.Catalog.Evolutions)
        {
            if (!data.Evolutions.TryGetValue(id, out var evo)) continue;
            if (!CanEvolve(world, evo, p)) continue;

            var from = data.Weapons[evo.BaseWeapon];
            var to = data.Weapons[evo.EvolvedWeapon];
            var passiveName = data.Passives.TryGetValue(evo.RequiredPassive, out var pd) ? pd.DisplayName : evo.RequiredPassive;
            options.Add(new UpgradeOption
            {
                Kind = UpgradeKind.Evolve,
                Id = from.Id,
                EvolvedWeaponId = to.Id,
                Level = 1,
                Title = $"진화: {from.DisplayName} → {to.DisplayName}",
                Description = $"{passiveName}의 힘으로 무기가 진화한다.\n{to.Description}",
                Apply = () => world.EvolveWeapon(p, from, to),
            });
        }
        return options;
    }

    private static void DrawWeighted(Random rng, List<Weighted> pool, int n, List<UpgradeOption> result)
    {
        for (int drawn = 0; drawn < n && pool.Count > 0; drawn++)
        {
            float total = 0f;
            for (int i = 0; i < pool.Count; i++) total += MathF.Max(0f, pool[i].Weight);
            if (total <= 0f) return;

            float roll = (float)rng.NextDouble() * total;
            int chosen = pool.Count - 1;
            float acc = 0f;
            for (int i = 0; i < pool.Count; i++)
            {
                acc += MathF.Max(0f, pool[i].Weight);
                if (roll <= acc && pool[i].Weight > 0f) { chosen = i; break; }
            }

            result.Add(pool[chosen].Option);
            pool.RemoveAt(chosen);
        }
    }
}
