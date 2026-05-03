using Game.Manager;
using Game.Player;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    public class HudUI : MonoBehaviour
    {
        [SerializeField] private PlayerStats playerStats;
        [SerializeField] private UltimateSkill ultimateSkill;
        [SerializeField] private Slider hpBar;
        [SerializeField] private Slider xpBar;
        [SerializeField] private Text levelText;
        [SerializeField] private Text timeText;
        [SerializeField] private Text killText;
        [SerializeField] private Text ultimateCooldownText;

        private void Start()
        {
            if (playerStats != null)
            {
                playerStats.OnHpChanged += OnHpChanged;
                playerStats.OnXpChanged += OnXpChanged;
                playerStats.OnLevelUp += OnLevelUp;
                OnHpChanged(playerStats.CurrentHp, playerStats.MaxHp);
                OnXpChanged(playerStats.Xp, playerStats.XpToNextLevel);
                OnLevelUp(playerStats.Level);
            }

            if (GameManager.Instance != null)
                GameManager.Instance.OnKillCountChanged += OnKillCountChanged;
        }

        private void OnDestroy()
        {
            if (playerStats != null)
            {
                playerStats.OnHpChanged -= OnHpChanged;
                playerStats.OnXpChanged -= OnXpChanged;
                playerStats.OnLevelUp -= OnLevelUp;
            }
            if (GameManager.Instance != null)
                GameManager.Instance.OnKillCountChanged -= OnKillCountChanged;
        }

        private void Update()
        {
            if (timeText != null && GameManager.Instance != null)
            {
                int total = Mathf.FloorToInt(GameManager.Instance.ElapsedSeconds);
                timeText.text = $"{total / 60:D2}:{total % 60:D2}";
            }

            if (ultimateCooldownText != null && ultimateSkill != null)
            {
                var cd = ultimateSkill.RemainingCooldown;
                ultimateCooldownText.text = cd <= 0f ? "궁극기 READY" : $"궁극기 {cd:F1}s";
            }
        }

        private void OnHpChanged(int current, int max)
        {
            if (hpBar != null) hpBar.value = max > 0 ? (float)current / max : 0f;
        }

        private void OnXpChanged(int current, int next)
        {
            if (xpBar != null) xpBar.value = next > 0 ? (float)current / next : 0f;
        }

        private void OnLevelUp(int level)
        {
            if (levelText != null) levelText.text = $"Lv.{level}";
        }

        private void OnKillCountChanged(int kills)
        {
            if (killText != null) killText.text = $"Kills: {kills}";
        }
    }
}
