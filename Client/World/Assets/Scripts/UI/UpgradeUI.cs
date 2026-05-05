using Game.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    public class UpgradeUI : MonoBehaviour
    {
        [SerializeField] private UpgradeSystem upgradeSystem;
        [SerializeField] private GameObject panel;
        [SerializeField] private Button[] choiceButtons = new Button[3];
        [SerializeField] private Text[] choiceTitles = new Text[3];
        [SerializeField] private Text[] choiceDescriptions = new Text[3];

        private void Start()
        {
            if (upgradeSystem == null)
            {
                upgradeSystem = FindFirstObjectByType<UpgradeSystem>();
                if (upgradeSystem == null)
                    Debug.LogWarning("[UpgradeUI] UpgradeSystem을 찾지 못했습니다. 씬에 UpgradeSystem이 있는지 확인하세요.");
            }

            if (upgradeSystem != null)
                upgradeSystem.OnUpgradeOffered += Show;

            for (int i = 0; i < choiceButtons.Length; i++)
            {
                int idx = i;
                if (choiceButtons[i] != null)
                    choiceButtons[i].onClick.AddListener(() => OnChoiceClicked(idx));
            }

            if (panel != null) panel.SetActive(false);
        }

        private void OnDestroy()
        {
            if (upgradeSystem != null)
                upgradeSystem.OnUpgradeOffered -= Show;
        }

        private void Show(UpgradeOption[] options)
        {
            if (panel != null) panel.SetActive(true);

            for (int i = 0; i < choiceButtons.Length; i++)
            {
                if (i < options.Length)
                {
                    if (choiceButtons[i] != null) choiceButtons[i].gameObject.SetActive(true);
                    if (choiceTitles[i] != null) choiceTitles[i].text = options[i].Title;
                    if (choiceDescriptions[i] != null) choiceDescriptions[i].text = options[i].Description;
                }
                else if (choiceButtons[i] != null)
                {
                    choiceButtons[i].gameObject.SetActive(false);
                }
            }
        }

        private void OnChoiceClicked(int index)
        {
            if (upgradeSystem != null)
                upgradeSystem.Choose(index);
            else
                Time.timeScale = 1f; // upgradeSystem 없어도 반드시 복구

            if (panel != null) panel.SetActive(false);
        }
    }
}
