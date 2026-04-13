using UnityEngine;
using UnityEngine.SceneManagement;

public class InGameMenuManager : MonoBehaviour
{
    [Header("Game Over")]
    [SerializeField] private CombatActorController playerController;
    [SerializeField] private CombatHealth playerHealth;
    [SerializeField] private GameObject gameOverPanel;
    [SerializeField] private bool hideGameOverPanelOnAwake = true;
    [SerializeField] private bool pauseOnGameOver;

    private CombatHealth subscribedPlayerHealth;
    private bool gameOverShown;

    void Reset()
    {
        TryAutoAssignPlayerHealth();
    }

    void Awake()
    {
        TryAutoAssignPlayerHealth();

        if (hideGameOverPanelOnAwake && gameOverPanel != null)
            gameOverPanel.SetActive(false);
    }

    void OnEnable()
    {
        TryAutoAssignPlayerHealth();
        SubscribePlayerHealth();
    }

    void OnDisable()
    {
        UnsubscribePlayerHealth();
    }

    public void OnClickRestart()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void OnClickTitle(string sceneName)
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(sceneName);
    }

    public void ShowGameOverPanel()
    {
        if (gameOverShown)
            return;

        gameOverShown = true;

        if (gameOverPanel != null)
            gameOverPanel.SetActive(true);

        if (pauseOnGameOver)
            Time.timeScale = 0f;
    }

    public void HideGameOverPanel()
    {
        gameOverShown = false;

        if (gameOverPanel != null)
            gameOverPanel.SetActive(false);

        if (pauseOnGameOver)
            Time.timeScale = 1f;
    }

    void SubscribePlayerHealth()
    {
        if (playerHealth == null || subscribedPlayerHealth == playerHealth)
            return;

        UnsubscribePlayerHealth();
        subscribedPlayerHealth = playerHealth;
        subscribedPlayerHealth.Died += HandlePlayerDied;
    }

    void UnsubscribePlayerHealth()
    {
        if (subscribedPlayerHealth == null)
            return;

        subscribedPlayerHealth.Died -= HandlePlayerDied;
        subscribedPlayerHealth = null;
    }

    void HandlePlayerDied(CombatHealth defeatedHealth)
    {
        if (defeatedHealth != playerHealth)
            return;

        ShowGameOverPanel();
    }

    void TryAutoAssignPlayerHealth()
    {
        if (playerHealth != null)
            return;

        if (playerController == null)
            playerController = FindPlayerController();

        if (playerController != null)
            playerHealth = playerController.Health;
    }

    CombatActorController FindPlayerController()
    {
        CombatActorController[] controllers = FindObjectsByType<CombatActorController>(FindObjectsSortMode.None);
        for (int i = 0; i < controllers.Length; i++)
        {
            CombatActorController controller = controllers[i];
            if (controller != null && controller.ActorSide == CombatActorSide.Player)
                return controller;
        }

        return null;
    }
}
