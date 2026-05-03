using System;
using Game.Manager;
using UnityEngine;

namespace Game.Player
{
    public class PlayerStats : MonoBehaviour
    {
        [SerializeField] private int maxHp = 100;
        [SerializeField] private int currentHp = 100;

        public int MaxHp => maxHp;
        public int CurrentHp => currentHp;
        public int Level { get; private set; } = 1;
        public int Xp { get; private set; }
        public int XpToNextLevel { get; private set; } = 5;

        public event Action<int, int> OnHpChanged;
        public event Action<int, int> OnXpChanged;
        public event Action<int> OnLevelUp;
        public event Action OnDied;

        public void TakeDamage(int amount)
        {
            if (currentHp <= 0) return;
            currentHp = Mathf.Max(0, currentHp - amount);
            OnHpChanged?.Invoke(currentHp, maxHp);
            if (currentHp == 0)
            {
                OnDied?.Invoke();
                GameManager.Instance?.GameOver();
            }
        }

        public void Heal(int amount)
        {
            currentHp = Mathf.Min(maxHp, currentHp + amount);
            OnHpChanged?.Invoke(currentHp, maxHp);
        }

        public void IncreaseMaxHp(int amount)
        {
            maxHp += amount;
            currentHp += amount;
            OnHpChanged?.Invoke(currentHp, maxHp);
        }

        public void AddXp(int amount)
        {
            Xp += amount;
            while (Xp >= XpToNextLevel)
            {
                Xp -= XpToNextLevel;
                Level++;
                XpToNextLevel = Mathf.RoundToInt(XpToNextLevel * 1.4f);
                OnLevelUp?.Invoke(Level);
            }
            OnXpChanged?.Invoke(Xp, XpToNextLevel);
        }
    }
}
