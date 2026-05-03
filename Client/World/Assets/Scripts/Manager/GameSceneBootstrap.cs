using UnityEngine;

namespace Game.Manager
{
    public class GameSceneBootstrap : MonoBehaviour
    {
        private void Start()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.StartGame();
                Debug.Log($"[Bootstrap] StartGame 호출 → State={GameManager.Instance.State}");
            }
            else
            {
                Debug.LogError("[Bootstrap] GameManager.Instance == null — _GameManager에 GameManager 컴포넌트가 있는지 확인");
            }
        }
    }
}
