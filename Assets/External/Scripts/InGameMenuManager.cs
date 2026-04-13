using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class InGameMenuManager : MonoBehaviour
{
    [Header("Game Over")]
    [SerializeField] private RoundGameManager roundGameManager;
    [SerializeField] private CombatActorController playerController;
    [SerializeField] private CombatHealth playerHealth;
    [SerializeField] private BattleUiController battleUiController;
    [SerializeField] private GameObject gameOverPanel;
    [SerializeField] private bool hideGameOverPanelOnAwake = true;
    [SerializeField] private bool pauseOnGameOver;

    [Header("Game Over Buttons")]
    [SerializeField] private List<Button> menuButtons = new();

    private int focusedMenuIndex = 0;

    private CombatHealth subscribedPlayerHealth;
    private BattleUiController subscribedBattleUiController;
    private RoundGameManager subscribedRoundGameManager;
    private bool gameOverShown;

    void Reset()
    {
        TryAutoAssignRoundGameManager();
        TryAutoAssignPlayerHealth();
        TryAutoAssignBattleUiController();
    }

    void Awake()
    {
        TryAutoAssignRoundGameManager();
        TryAutoAssignPlayerHealth();
        TryAutoAssignBattleUiController();

        if (hideGameOverPanelOnAwake && gameOverPanel != null)
            gameOverPanel.SetActive(false);
    }

    void Update()
    {
        if (gameOverPanel == null || !gameOverPanel.activeSelf)
            return;

        if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.LeftArrow))
        {
            focusedMenuIndex = (focusedMenuIndex - 1 + menuButtons.Count) % menuButtons.Count;
            SelectMenuButton(focusedMenuIndex);
        }
        else if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.RightArrow))
        {
            focusedMenuIndex = (focusedMenuIndex + 1) % menuButtons.Count;
            SelectMenuButton(focusedMenuIndex);
        }
    }

    void SelectMenuButton(int index)
    {
        if (menuButtons[index] != null)
            menuButtons[index].Select();
    }

    void OnEnable()
    {
        TryAutoAssignRoundGameManager();
        TryAutoAssignPlayerHealth();
        TryAutoAssignBattleUiController();
        SubscribeRoundGameManager();
        SubscribePlayerHealth();
        SubscribeBattleUiController();
        ShowGameOverIfRoundIsDefeat();
        ShowGameOverIfPlayerHealthIsEmpty();
    }

    void OnDisable()
    {
        UnsubscribeRoundGameManager();
        UnsubscribePlayerHealth();
        UnsubscribeBattleUiController();
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

        focusedMenuIndex = 0;
        SelectMenuButton(0);

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
        subscribedPlayerHealth.HealthChanged += HandlePlayerHealthChanged;
        subscribedPlayerHealth.Died += HandlePlayerDied;
    }

    void UnsubscribePlayerHealth()
    {
        if (subscribedPlayerHealth == null)
            return;

        subscribedPlayerHealth.HealthChanged -= HandlePlayerHealthChanged;
        subscribedPlayerHealth.Died -= HandlePlayerDied;
        subscribedPlayerHealth = null;
    }

    void SubscribeBattleUiController()
    {
        if (battleUiController == null || subscribedBattleUiController == battleUiController)
            return;

        UnsubscribeBattleUiController();
        subscribedBattleUiController = battleUiController;
        subscribedBattleUiController.PlayerQteEnded += HandlePlayerQteEnded;
    }

    void UnsubscribeBattleUiController()
    {
        if (subscribedBattleUiController == null)
            return;

        subscribedBattleUiController.PlayerQteEnded -= HandlePlayerQteEnded;
        subscribedBattleUiController = null;
    }

    void SubscribeRoundGameManager()
    {
        if (roundGameManager == null || subscribedRoundGameManager == roundGameManager)
            return;

        UnsubscribeRoundGameManager();
        subscribedRoundGameManager = roundGameManager;
        subscribedRoundGameManager.StateChanged += HandleRoundGameStateChanged;
    }

    void UnsubscribeRoundGameManager()
    {
        if (subscribedRoundGameManager == null)
            return;

        subscribedRoundGameManager.StateChanged -= HandleRoundGameStateChanged;
        subscribedRoundGameManager = null;
    }

    void HandleRoundGameStateChanged(RoundGameState state)
    {
        if (state == RoundGameState.Defeat)
            ShowGameOverPanel();
    }

    void HandlePlayerHealthChanged(CombatHealth changedHealth)
    {
        if (changedHealth != playerHealth)
            return;

        ShowGameOverIfPlayerHealthIsEmpty();
    }

    void HandlePlayerDied(CombatHealth defeatedHealth)
    {
        if (defeatedHealth != playerHealth)
            return;

        ShowGameOverPanel();
    }

    void HandlePlayerQteEnded(QteEndReason endReason)
    {
        if (endReason != QteEndReason.Fail)
            return;

        ShowGameOverIfPlayerHealthIsEmpty();
    }

    void ShowGameOverIfPlayerHealthIsEmpty()
    {
        if (playerHealth == null)
            return;

        playerHealth.EnsureInitialized();

        if (playerHealth.CurrentHealth <= 0f)
            ShowGameOverPanel();
    }

    void ShowGameOverIfRoundIsDefeat()
    {
        if (roundGameManager != null && roundGameManager.State == RoundGameState.Defeat)
            ShowGameOverPanel();
    }

    void TryAutoAssignRoundGameManager()
    {
        if (roundGameManager == null)
            roundGameManager = FindFirstObjectByType<RoundGameManager>();
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

    void TryAutoAssignBattleUiController()
    {
        if (battleUiController == null)
            battleUiController = FindFirstObjectByType<BattleUiController>();
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
