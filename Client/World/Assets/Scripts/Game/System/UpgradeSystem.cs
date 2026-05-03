using System;
using System.Collections.Generic;
using Game.Player;
using UnityEngine;

namespace Game.Systems
{
    public enum UpgradeType
    {
        DamageUp,
        RangeUp,
        AttackSpeedUp,
        MoveSpeedUp,
        MaxHpUp,
        UltimateCooldownDown,
        UltimateDamageUp
    }

    [Serializable]
    public class UpgradeOption
    {
        public UpgradeType Type;
        public string Title;
        public string Description;
    }

    public class UpgradeSystem : MonoBehaviour
    {
        [SerializeField] private PlayerStats playerStats;
        [SerializeField] private PlayerController playerController;
        [SerializeField] private AutoAttack autoAttack;
        [SerializeField] private UltimateSkill ultimateSkill;

        private static readonly UpgradeOption[] _allOptions = new[]
        {
            new UpgradeOption { Type = UpgradeType.DamageUp,             Title = "공격력 +5",    Description = "자동 공격 데미지가 5 증가" },
            new UpgradeOption { Type = UpgradeType.RangeUp,              Title = "사거리 +0.5",  Description = "자동 공격 범위가 0.5 증가" },
            new UpgradeOption { Type = UpgradeType.AttackSpeedUp,        Title = "공격속도 +",   Description = "자동 공격 쿨다운 15% 감소" },
            new UpgradeOption { Type = UpgradeType.MoveSpeedUp,          Title = "이동속도 +1",  Description = "이동 속도가 1 증가" },
            new UpgradeOption { Type = UpgradeType.MaxHpUp,              Title = "최대 HP +20",  Description = "최대 HP가 20 증가하고 회복" },
            new UpgradeOption { Type = UpgradeType.UltimateCooldownDown, Title = "궁극기 쿨감",   Description = "궁극기 쿨다운 1초 감소" },
            new UpgradeOption { Type = UpgradeType.UltimateDamageUp,     Title = "궁극기 강화",   Description = "궁극기 데미지 50 증가" },
        };

        public event Action<UpgradeOption[]> OnUpgradeOffered;
        public event Action OnUpgradeApplied;

        private UpgradeOption[] _currentChoices;

        private void OnEnable()
        {
            if (playerStats != null) playerStats.OnLevelUp += HandleLevelUp;
        }

        private void OnDisable()
        {
            if (playerStats != null) playerStats.OnLevelUp -= HandleLevelUp;
        }

        private void HandleLevelUp(int newLevel)
        {
            _currentChoices = PickRandom(3);
            Time.timeScale = 0f;
            OnUpgradeOffered?.Invoke(_currentChoices);
        }

        public void Choose(int index)
        {
            if (_currentChoices == null || index < 0 || index >= _currentChoices.Length) return;
            Apply(_currentChoices[index].Type);
            _currentChoices = null;
            Time.timeScale = 1f;
            OnUpgradeApplied?.Invoke();
        }

        private void Apply(UpgradeType type)
        {
            switch (type)
            {
                case UpgradeType.DamageUp:
                    if (autoAttack != null) autoAttack.Damage += 5;
                    break;
                case UpgradeType.RangeUp:
                    if (autoAttack != null) autoAttack.Range += 0.5f;
                    break;
                case UpgradeType.AttackSpeedUp:
                    if (autoAttack != null) autoAttack.Cooldown *= 0.85f;
                    break;
                case UpgradeType.MoveSpeedUp:
                    if (playerController != null) playerController.MoveSpeed += 1f;
                    break;
                case UpgradeType.MaxHpUp:
                    if (playerStats != null) playerStats.IncreaseMaxHp(20);
                    break;
                case UpgradeType.UltimateCooldownDown:
                    if (ultimateSkill != null) ultimateSkill.Cooldown -= 1f;
                    break;
                case UpgradeType.UltimateDamageUp:
                    if (ultimateSkill != null) ultimateSkill.Damage += 50;
                    break;
            }
        }

        private UpgradeOption[] PickRandom(int count)
        {
            var pool = new List<UpgradeOption>(_allOptions);
            var picked = new UpgradeOption[Mathf.Min(count, pool.Count)];
            for (int i = 0; i < picked.Length; i++)
            {
                int idx = UnityEngine.Random.Range(0, pool.Count);
                picked[i] = pool[idx];
                pool.RemoveAt(idx);
            }
            return picked;
        }
    }
}
