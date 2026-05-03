using UnityEngine;

namespace Game.Manager
{
    public class GameSceneBootstrap : MonoBehaviour
    {
        private void Start()
        {
            if (GameManager.Instance != null)
                GameManager.Instance.StartGame();
        }
    }
}
