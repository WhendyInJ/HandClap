using System;
using UnityEngine;
using UnityEngine.UI;

public enum QteEndReason
{
    Success,
    Fail,
}

public class BattleUiController : MonoBehaviour
{
    [Serializable]
    private class HealthUiBinding
    {
        public Image fillImage;
        public Image maxHpCapFill;
    }

    [Header("Combat Targets")]
    [SerializeField] private CombatActorController playerController;
    [SerializeField] private CombatActorController enemyController;
    [SerializeField] private CombatActorStats playerStats;
    [SerializeField] private CombatActorStats enemyStats;
    [SerializeField] private CombatHealth enemyHealth;

    [Header("Health UI")]
    [SerializeField] private HealthUiBinding playerHealthUi = new HealthUiBinding();
    [SerializeField] private HealthUiBinding enemyHealthUi = new HealthUiBinding();

    [Header("Player Stagger Minigames")]
    [SerializeField] private PlayerStaggerMinigameType playerStaggerMinigameType = PlayerStaggerMinigameType.GaugeHold;
    [SerializeField] private SlideQteMinigameController slideQteMinigame;
    [SerializeField] private GaugeHoldMinigameController gaugeHoldMinigame;

    [Header("Stagger")]
    [SerializeField, Min(0f)] private float enemyStaggerDuration = 2.0f;
    [SerializeField, Min(1f)] private float staggerBonusDamageMultiplier = 1.5f;
    [SerializeField, Min(0f)] private float failGaugeLossPerDamage = 0.2f;
    [SerializeField, Range(0f, 0.5f)] private float dodgeFailGaugePenalty = 0.1f;

    private float playerMaxHpNormalized = 1f;
    private float currentFailGauge;
    private float currentRecoverGauge;
    private float lastPlayerDamageTaken;
    private bool isPlayerQteActive;
    private PlayerStaggerMinigameController activeStaggerMinigame;

    public event Action<QteEndReason> PlayerQteEnded;
    public event Action EnemyDefeated;

    public bool IsPlayerQteActive => isPlayerQteActive;
    public float PlayerHealthNormalized => isPlayerQteActive ? currentFailGauge : playerMaxHpNormalized;
    public float PlayerMaxHpNormalized => playerMaxHpNormalized;
    public float EnemyHealthNormalized => enemyHealth != null ? enemyHealth.Normalized : 0f;
    public float FailGaugeNormalized => currentFailGauge;
    public float RecoverGaugeNormalized => currentRecoverGauge;
    public float LastPlayerDamageTaken => lastPlayerDamageTaken;
    public CombatActorController PlayerController => playerController;
    public CombatActorController EnemyController => enemyController;
    public CombatActorStats PlayerStats => playerStats;
    public CombatActorStats EnemyStats => enemyStats;

    void Reset()
    {
        TryAutoAssignControllers();
        TryAutoAssignCombatComponents();
        TryAutoAssignMinigames();
        ApplyImmediateUiState();
    }

    void Awake()
    {
        TryAutoAssignControllers();
        TryAutoAssignCombatComponents();
        TryAutoAssignMinigames();
        playerMaxHpNormalized = 1f;
        currentFailGauge = 1f;
        currentRecoverGauge = 0f;
        ApplyImmediateUiState();
    }

    void OnEnable()
    {
        TryAutoAssignControllers();
        TryAutoAssignCombatComponents();
        TryAutoAssignMinigames();
        SubscribeCombatEvents();
        SubscribeHealthEvents();
        SubscribeMinigameEvents();
        ApplyImmediateUiState();
    }

    void OnDisable()
    {
        UnsubscribeMinigameEvents();
        UnsubscribeCombatEvents();
        UnsubscribeHealthEvents();
    }

    void OnValidate()
    {
        enemyStaggerDuration = Mathf.Max(0f, enemyStaggerDuration);
        staggerBonusDamageMultiplier = Mathf.Max(1f, staggerBonusDamageMultiplier);
        failGaugeLossPerDamage = Mathf.Max(0f, failGaugeLossPerDamage);
        dodgeFailGaugePenalty = Mathf.Clamp(dodgeFailGaugePenalty, 0f, 0.5f);

        if (!Application.isPlaying)
            ApplyImmediateUiState();
    }

    public void ResetEnemyHealth()
    {
        if (enemyHealth != null)
            enemyHealth.ResetHealth();

        UpdateEnemyHealthUi();
    }

    public void ApplyEnemyDamage(float damage)
    {
        if (damage <= 0f || enemyHealth == null)
            return;

        enemyHealth.ApplyDamage(damage);
        UpdateEnemyHealthUi();
    }

    public void ResetPlayerFailGauge()
    {
        playerMaxHpNormalized = 1f;
        currentFailGauge = 1f;
        currentRecoverGauge = 0f;
        UpdatePlayerHealthUi();
        ApplyMinigameIdleStates();
    }

    public void RecoverPlayerFailGauge(float normalizedAmount)
    {
        if (normalizedAmount <= 0f)
            return;

        playerMaxHpNormalized = Mathf.Clamp01(playerMaxHpNormalized + normalizedAmount);
        UpdatePlayerHealthUi();
        ApplyMinigameIdleStates();
    }

    public void StartPlayerQte()
    {
        StartPlayerQte(0f, leanForward: false);
    }

    public void StartPlayerQte(float incomingDamage)
    {
        StartPlayerQte(incomingDamage, leanForward: false);
    }

    public void StartPlayerQte(float incomingDamage, bool leanForward)
    {
        if (isPlayerQteActive)
            return;

        PlayerStaggerMinigameController minigame = GetSelectedStaggerMinigame();
        if (minigame == null)
        {
            Debug.LogWarning("Player stagger minigame is not assigned.");
            return;
        }

        ApplyMinigameAvailability(minigame);
        isPlayerQteActive = true;
        activeStaggerMinigame = minigame;
        lastPlayerDamageTaken = Mathf.Max(0f, incomingDamage);
        playerMaxHpNormalized = Mathf.Clamp01(
            playerMaxHpNormalized - lastPlayerDamageTaken * failGaugeLossPerDamage);
        currentFailGauge = playerMaxHpNormalized;
        currentRecoverGauge = 0f;
        UpdatePlayerHealthUi();

        if (playerController != null)
            playerController.RequestStagger(0f, leanForward);

        minigame.StartMinigame(CreateMinigameContext());

        if (currentFailGauge <= 0f)
            StopPlayerQte(QteEndReason.Fail);
    }

    public void StopPlayerQte(QteEndReason endReason)
    {
        CompletePlayerQte(endReason, stopActiveMinigame: true);
    }

    void CompletePlayerQte(QteEndReason endReason, bool stopActiveMinigame)
    {
        if (!isPlayerQteActive)
            return;

        PlayerStaggerMinigameController minigame = activeStaggerMinigame;
        isPlayerQteActive = false;
        activeStaggerMinigame = null;

        if (playerController != null)
            playerController.StopStagger();

        if (endReason == QteEndReason.Success)
            currentFailGauge = playerMaxHpNormalized;

        if (stopActiveMinigame && minigame != null && minigame.IsActive)
            minigame.StopMinigame(endReason);

        ApplyMinigameIdleStates();
        UpdatePlayerHealthUi();
        PlayerQteEnded?.Invoke(endReason);
    }

    void HandleCombatEvent(CombatEventData eventData)
    {
        switch (eventData.Kind)
        {
            case CombatEventKind.AttackHit:
                HandleAttackHit(eventData);
                break;
            case CombatEventKind.AttackDodged:
                HandleAttackDodged(eventData);
                break;
            case CombatEventKind.DodgeFailed:
                HandleDodgeFailed(eventData);
                break;
            case CombatEventKind.FeintPunished:
                HandleFeintPunished(eventData);
                break;
        }
    }

    void HandleAttackHit(CombatEventData eventData)
    {
        if (eventData.Actor == playerController && eventData.Opponent == enemyController)
        {
            float finalDamage = playerStats != null
                ? playerStats.CalculateDamageToEnemy(enemyStats)
                : 0f;

            if (enemyController != null && enemyController.IsStaggered)
                finalDamage *= staggerBonusDamageMultiplier;

            if (enemyHealth != null)
                enemyHealth.ApplyDamage(finalDamage);

            UpdateEnemyHealthUi();
            return;
        }

        if (eventData.Actor == enemyController && eventData.Opponent == playerController)
        {
            float finalDamage = enemyStats != null
                ? enemyStats.CalculateDamageToPlayer(playerStats)
                : 0f;

            ApplyPlayerDamage(finalDamage);
        }
    }

    public void ApplyPlayerDamage(float damage)
    {
        if (damage <= 0f)
            return;

        playerMaxHpNormalized = Mathf.Clamp01(
            playerMaxHpNormalized - damage * failGaugeLossPerDamage);
        currentFailGauge = playerMaxHpNormalized;
        UpdatePlayerHealthUi();
        ApplyMinigameIdleStates();

        if (playerMaxHpNormalized <= 0f)
            PlayerQteEnded?.Invoke(QteEndReason.Fail);
    }

    void HandleAttackDodged(CombatEventData eventData)
    {
        if (eventData.Actor == playerController)
        {
            StartPlayerQte(0f, leanForward: true);
            return;
        }

        if (eventData.Actor == enemyController && enemyController != null)
            enemyController.RequestStagger(enemyStaggerDuration, leanForward: true);
    }

    void HandleDodgeFailed(CombatEventData eventData)
    {
        if (eventData.Actor == playerController)
        {
            StartPlayerQte(dodgeFailGaugePenalty / Mathf.Max(0.01f, failGaugeLossPerDamage));
            return;
        }

        if (eventData.Actor == enemyController && enemyController != null)
            enemyController.RequestStagger(enemyStaggerDuration);
    }

    void HandleFeintPunished(CombatEventData eventData)
    {
        if (eventData.Opponent == playerController)
        {
            float finalDamage = enemyStats != null
                ? enemyStats.CalculateDamageToPlayer(playerStats)
                : 0f;

            StartPlayerQte(finalDamage);
            return;
        }

        if (eventData.Opponent == enemyController)
        {
            float finalDamage = playerStats != null
                ? playerStats.CalculateDamageToEnemy(enemyStats)
                : 0f;

            if (enemyHealth != null)
                enemyHealth.ApplyDamage(finalDamage);

            UpdateEnemyHealthUi();

            if (enemyController != null)
                enemyController.RequestStagger(enemyStaggerDuration);
        }
    }

    void SubscribeCombatEvents()
    {
        if (playerController != null)
            playerController.CombatEventRaised += HandleCombatEvent;

        if (enemyController != null && enemyController != playerController)
            enemyController.CombatEventRaised += HandleCombatEvent;
    }

    void UnsubscribeCombatEvents()
    {
        if (playerController != null)
            playerController.CombatEventRaised -= HandleCombatEvent;

        if (enemyController != null && enemyController != playerController)
            enemyController.CombatEventRaised -= HandleCombatEvent;
    }

    void SubscribeHealthEvents()
    {
        if (enemyHealth != null)
        {
            enemyHealth.HealthChanged += HandleHealthChanged;
            enemyHealth.Died += HandleEnemyDied;
        }
    }

    void UnsubscribeHealthEvents()
    {
        if (enemyHealth != null)
        {
            enemyHealth.HealthChanged -= HandleHealthChanged;
            enemyHealth.Died -= HandleEnemyDied;
        }
    }

    void HandleHealthChanged(CombatHealth changedHealth)
    {
        if (changedHealth == enemyHealth)
            UpdateEnemyHealthUi();
    }

    void HandleEnemyDied(CombatHealth defeatedHealth)
    {
        if (defeatedHealth == enemyHealth)
            EnemyDefeated?.Invoke();
    }

    void TryAutoAssignControllers()
    {
        if (playerController != null && enemyController != null)
            return;

        if (playerController == null)
            playerController = FindPlayerController();

        if (enemyController == null)
            enemyController = FindEnemyController();

        if (playerController != null && enemyController != null)
            return;

        CombatActorController[] controllers = FindObjectsByType<CombatActorController>(FindObjectsSortMode.None);
        for (int i = 0; i < controllers.Length; i++)
        {
            CombatActorController controller = controllers[i];
            if (controller == null)
                continue;

            if (playerController == null && controller.ActorSide == CombatActorSide.Player)
                playerController = controller;
            else if (enemyController == null && controller.ActorSide == CombatActorSide.Enemy)
                enemyController = controller;
        }

        if (playerController != null && enemyController == null)
            enemyController = playerController.OpponentController;

        if (enemyController != null && playerController == null)
            playerController = enemyController.OpponentController;
    }

    CombatActorController FindPlayerController()
    {
        PlayerController[] players = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
        for (int i = 0; i < players.Length; i++)
        {
            PlayerController player = players[i];
            if (player != null && !player.TryGetComponent<EnemyController>(out _))
                return player;
        }

        return null;
    }

    CombatActorController FindEnemyController()
    {
        EnemyController[] enemies = FindObjectsByType<EnemyController>(FindObjectsSortMode.None);
        for (int i = 0; i < enemies.Length; i++)
        {
            EnemyController enemy = enemies[i];
            if (enemy != null && enemy.ActorController != null)
                return enemy.ActorController;
        }

        return null;
    }

    void TryAutoAssignCombatComponents()
    {
        if (playerController != null && playerStats == null)
            playerStats = playerController.Stats;

        if (enemyController != null)
        {
            if (enemyStats == null)
                enemyStats = enemyController.Stats;

            if (enemyHealth == null)
                enemyHealth = enemyController.Health;
        }

        if (enemyHealth != null)
            enemyHealth.EnsureInitialized();
    }

    void TryAutoAssignMinigames()
    {
        if (slideQteMinigame == null)
            slideQteMinigame = GetComponentInChildren<SlideQteMinigameController>(true);

        if (slideQteMinigame == null)
            slideQteMinigame = FindFirstObjectByType<SlideQteMinigameController>();

        if (gaugeHoldMinigame == null)
            gaugeHoldMinigame = GetComponentInChildren<GaugeHoldMinigameController>(true);

        if (gaugeHoldMinigame == null)
            gaugeHoldMinigame = FindFirstObjectByType<GaugeHoldMinigameController>();
    }

    void ApplyImmediateUiState()
    {
        if (!Application.isPlaying)
        {
            playerMaxHpNormalized = 1f;
            currentFailGauge = 1f;
            currentRecoverGauge = 0f;
            isPlayerQteActive = false;
        }

        UpdatePlayerHealthUi();
        UpdateEnemyHealthUi();
        ApplyMinigameIdleStates();
    }

    void UpdatePlayerHealthUi()
    {
        if (playerHealthUi.fillImage != null)
            playerHealthUi.fillImage.fillAmount = PlayerHealthNormalized;

        if (playerHealthUi.maxHpCapFill != null)
            playerHealthUi.maxHpCapFill.fillAmount = playerMaxHpNormalized;
    }

    void UpdateEnemyHealthUi()
    {
        if (enemyHealthUi.fillImage == null)
            return;

        enemyHealthUi.fillImage.fillAmount = EnemyHealthNormalized;
    }

    void SubscribeMinigameEvents()
    {
        SubscribeMinigameEvents(slideQteMinigame);
        SubscribeMinigameEvents(gaugeHoldMinigame);
    }

    void SubscribeMinigameEvents(PlayerStaggerMinigameController minigame)
    {
        if (minigame == null)
            return;

        minigame.MinigameEnded -= HandleStaggerMinigameEnded;
        minigame.GaugeChanged -= HandleStaggerMinigameGaugeChanged;
        minigame.MinigameEnded += HandleStaggerMinigameEnded;
        minigame.GaugeChanged += HandleStaggerMinigameGaugeChanged;
    }

    void UnsubscribeMinigameEvents()
    {
        UnsubscribeMinigameEvents(slideQteMinigame);
        UnsubscribeMinigameEvents(gaugeHoldMinigame);
    }

    void UnsubscribeMinigameEvents(PlayerStaggerMinigameController minigame)
    {
        if (minigame == null)
            return;

        minigame.MinigameEnded -= HandleStaggerMinigameEnded;
        minigame.GaugeChanged -= HandleStaggerMinigameGaugeChanged;
    }

    void HandleStaggerMinigameEnded(QteEndReason endReason)
    {
        CompletePlayerQte(endReason, stopActiveMinigame: false);
    }

    void HandleStaggerMinigameGaugeChanged(float failGaugeNormalized, float recoverGaugeNormalized)
    {
        currentFailGauge = Mathf.Clamp01(failGaugeNormalized);
        currentRecoverGauge = Mathf.Clamp01(recoverGaugeNormalized);
        UpdatePlayerHealthUi();
    }

    PlayerStaggerMinigameContext CreateMinigameContext()
    {
        return new PlayerStaggerMinigameContext
        {
            failGaugeNormalized = currentFailGauge,
            recoverGaugeNormalized = currentRecoverGauge,
            maxHpNormalized = playerMaxHpNormalized,
            difficultyMultiplier = GetPlayerStaggerDifficulty(),
        };
    }

    PlayerStaggerMinigameController GetSelectedStaggerMinigame()
    {
        switch (playerStaggerMinigameType)
        {
            case PlayerStaggerMinigameType.SlideQte:
                return slideQteMinigame != null
                    ? (PlayerStaggerMinigameController)slideQteMinigame
                    : gaugeHoldMinigame;

            case PlayerStaggerMinigameType.GaugeHold:
                return gaugeHoldMinigame != null
                    ? (PlayerStaggerMinigameController)gaugeHoldMinigame
                    : slideQteMinigame;

            default:
                return gaugeHoldMinigame != null
                    ? (PlayerStaggerMinigameController)gaugeHoldMinigame
                    : slideQteMinigame;
        }
    }

    float GetPlayerStaggerDifficulty()
    {
        return playerStats != null
            ? Mathf.Max(0.01f, playerStats.StaggerDifficultyMultiplier)
            : 1f;
    }

    void ApplyMinigameIdleStates()
    {
        PlayerStaggerMinigameController selectedMinigame = GetSelectedStaggerMinigame();
        ApplyMinigameAvailability(selectedMinigame);

        float failGauge = isPlayerQteActive ? currentFailGauge : playerMaxHpNormalized;

        if (slideQteMinigame != null)
            slideQteMinigame.ApplyIdleState(failGauge, currentRecoverGauge, playerMaxHpNormalized);

        if (gaugeHoldMinigame != null)
            gaugeHoldMinigame.ApplyIdleState(failGauge, currentRecoverGauge, playerMaxHpNormalized);
    }

    void ApplyMinigameAvailability(PlayerStaggerMinigameController selectedMinigame)
    {
        if (slideQteMinigame != null)
            slideQteMinigame.SetSuppressed(slideQteMinigame != selectedMinigame);

        if (gaugeHoldMinigame != null)
            gaugeHoldMinigame.SetSuppressed(gaugeHoldMinigame != selectedMinigame);
    }
}
