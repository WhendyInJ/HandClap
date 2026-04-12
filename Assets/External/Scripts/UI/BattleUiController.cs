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
    [SerializeField, Min(0f)] private float gaugeHoldTimerDamageRatio = 1f;

    [Header("Stagger")]
    [SerializeField, Min(0f)] private float enemyStaggerDuration = 2.0f;
    [SerializeField, Min(1f)] private float staggerBonusDamageMultiplier = 1.5f;
    [SerializeField, Range(0f, 0.5f)] private float dodgeFailGaugePenalty = 0.1f;

    private float currentFailGauge;
    private float currentRecoverGauge;
    private float lastPlayerDamageTaken;
    private float qteStartFailGauge;
    private float qteLowestFailGauge;
    private bool isPlayerQteActive;
    private bool suppressPlayerDeathEvent;
    private PlayerStaggerMinigameController activeStaggerMinigame;

    public event Action<QteEndReason> PlayerQteEnded;
    public event Action EnemyDefeated;

    public bool IsPlayerQteActive => isPlayerQteActive;
    public float PlayerHealthNormalized => GetPlayerHealthNormalized();
    public float PlayerMaxHpNormalized => GetPlayerHealthNormalized();
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
        currentFailGauge = GetPlayerHealthNormalized();
        qteStartFailGauge = currentFailGauge;
        qteLowestFailGauge = currentFailGauge;
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
        dodgeFailGaugePenalty = Mathf.Clamp(dodgeFailGaugePenalty, 0f, 0.5f);
        gaugeHoldTimerDamageRatio = Mathf.Max(0f, gaugeHoldTimerDamageRatio);

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

        enemyHealth.ApplyDamage(damage * GetEnemyIncomingDamageMultiplier());
        UpdateEnemyHealthUi();
    }

    public void ResetPlayerFailGauge()
    {
        if (playerController != null && playerController.Health != null)
            playerController.Health.ResetHealth();

        currentFailGauge = GetPlayerHealthNormalized();
        qteStartFailGauge = currentFailGauge;
        qteLowestFailGauge = currentFailGauge;
        currentRecoverGauge = 0f;
        UpdatePlayerHealthUi();
        ApplyMinigameIdleStates();
    }

    public void RecoverPlayerFailGauge(float normalizedAmount)
    {
        if (normalizedAmount <= 0f)
            return;

        if (playerController != null && playerController.Health != null)
            playerController.Health.Heal(playerController.Health.MaxHealth * normalizedAmount);

        currentFailGauge = GetPlayerHealthNormalized();
        UpdatePlayerHealthUi();
        ApplyMinigameIdleStates();
    }

    public void StartPlayerQte()
    {
        StartPlayerQte(0f, leanForward: false, PlayerStaggerGaugeStartLayout.Default);
    }

    public void StartPlayerQte(float incomingDamage)
    {
        StartPlayerQte(incomingDamage, leanForward: false, PlayerStaggerGaugeStartLayout.Default);
    }

    public void StartPlayerQte(float incomingDamage, bool leanForward)
    {
        StartPlayerQte(incomingDamage, leanForward, PlayerStaggerGaugeStartLayout.Default);
    }

    public void StartPlayerQte(
        float incomingDamage,
        bool leanForward,
        PlayerStaggerGaugeStartLayout gaugeStartLayout)
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
        ApplyPlayerCombatHealthDamage(lastPlayerDamageTaken);
        currentFailGauge = GetPlayerHealthNormalized();
        qteStartFailGauge = currentFailGauge;
        qteLowestFailGauge = currentFailGauge;
        currentRecoverGauge = 0f;
        UpdatePlayerHealthUi();

        if (playerController != null)
            playerController.RequestStagger(0f, leanForward);

        minigame.StartMinigame(CreateMinigameContext(gaugeStartLayout));

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
        float timerDamage = GetGaugeHoldTimerDamage(minigame);
        isPlayerQteActive = false;
        activeStaggerMinigame = null;

        if (playerController != null)
            playerController.StopStagger();

        if (endReason == QteEndReason.Success)
            currentFailGauge = GetPlayerHealthNormalized();

        if (timerDamage > 0f)
        {
            suppressPlayerDeathEvent = true;
            ApplyPlayerCombatHealthDamage(timerDamage);
            suppressPlayerDeathEvent = false;
        }

        currentFailGauge = GetPlayerHealthNormalized();
        QteEndReason finalEndReason = GetPlayerHealthNormalized() <= 0f
            ? QteEndReason.Fail
            : endReason;

        if (stopActiveMinigame && minigame != null && minigame.IsActive)
            minigame.StopMinigame(finalEndReason);

        currentFailGauge = GetPlayerHealthNormalized();
        ApplyMinigameIdleStates();
        UpdatePlayerHealthUi();
        PlayerQteEnded?.Invoke(finalEndReason);
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
            case CombatEventKind.FeintFailed:
                HandleFeintFailed(eventData);
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

            ApplyEnemyDamage(finalDamage);
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

        ApplyPlayerCombatHealthDamage(damage);
        currentFailGauge = GetPlayerHealthNormalized();
        UpdatePlayerHealthUi();
        ApplyMinigameIdleStates();

        if (GetPlayerHealthNormalized() <= 0f)
            PlayerQteEnded?.Invoke(QteEndReason.Fail);
    }

    void HandleAttackDodged(CombatEventData eventData)
    {
        if (eventData.Actor == playerController)
        {
            StartPlayerQte(
                0f,
                leanForward: true,
                PlayerStaggerGaugeStartLayout.TargetLeftNeedleRight);
            return;
        }

        if (eventData.Actor == enemyController && enemyController != null)
            enemyController.RequestStagger(enemyStaggerDuration, leanForward: true);
    }

    void HandleDodgeFailed(CombatEventData eventData)
    {
        if (eventData.Actor == playerController)
        {
            StartPlayerQte(
                GetPlayerDamageForNormalizedHealthLoss(dodgeFailGaugePenalty),
                leanForward: false,
                PlayerStaggerGaugeStartLayout.TargetRightNeedleLeft);
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

            StartPlayerQte(
                finalDamage,
                leanForward: false,
                PlayerStaggerGaugeStartLayout.TargetRightNeedleLeft);
            return;
        }

        if (eventData.Opponent == enemyController)
        {
            float finalDamage = playerStats != null
                ? playerStats.CalculateDamageToEnemy(enemyStats)
                : 0f;

            ApplyEnemyDamage(finalDamage);

            if (enemyController != null)
                enemyController.RequestStagger(enemyStaggerDuration);
        }
    }

    void HandleFeintFailed(CombatEventData eventData)
    {
        if (eventData.Actor == playerController)
        {
            StartPlayerQte(
                0f,
                leanForward: false,
                PlayerStaggerGaugeStartLayout.TargetRightNeedleLeft);
            return;
        }

        if (eventData.Actor == enemyController && enemyController != null)
            enemyController.RequestStagger(enemyStaggerDuration);
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
        CombatHealth playerHealth = GetPlayerHealth();
        if (playerHealth != null)
        {
            playerHealth.HealthChanged += HandleHealthChanged;
            playerHealth.Died += HandlePlayerDied;
        }

        if (enemyHealth != null)
        {
            enemyHealth.HealthChanged += HandleHealthChanged;
            enemyHealth.Died += HandleEnemyDied;
        }
    }

    void UnsubscribeHealthEvents()
    {
        CombatHealth playerHealth = GetPlayerHealth();
        if (playerHealth != null)
        {
            playerHealth.HealthChanged -= HandleHealthChanged;
            playerHealth.Died -= HandlePlayerDied;
        }

        if (enemyHealth != null)
        {
            enemyHealth.HealthChanged -= HandleHealthChanged;
            enemyHealth.Died -= HandleEnemyDied;
        }
    }

    void HandleHealthChanged(CombatHealth changedHealth)
    {
        if (changedHealth == GetPlayerHealth())
        {
            if (!isPlayerQteActive)
                currentFailGauge = GetPlayerHealthNormalized();

            UpdatePlayerHealthUi();
            ApplyMinigameIdleStates();
        }
        else if (changedHealth == enemyHealth)
        {
            UpdateEnemyHealthUi();
        }
    }

    void HandlePlayerDied(CombatHealth defeatedHealth)
    {
        if (defeatedHealth == GetPlayerHealth() && !suppressPlayerDeathEvent)
            PlayerQteEnded?.Invoke(QteEndReason.Fail);
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

        if (playerController != null && playerController.Health != null)
            playerController.Health.EnsureInitialized();

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
            currentFailGauge = GetPlayerHealthNormalized();
            currentRecoverGauge = 0f;
            isPlayerQteActive = false;
        }
        else if (!isPlayerQteActive)
        {
            currentFailGauge = GetPlayerHealthNormalized();
        }

        UpdatePlayerHealthUi();
        UpdateEnemyHealthUi();
        ApplyMinigameIdleStates();
    }

    void UpdatePlayerHealthUi()
    {
        float displayedHealthNormalized = GetDisplayedPlayerHealthNormalized();
        ApplyPlayerHealthFillDisplay(displayedHealthNormalized);

        if (playerHealthUi.fillImage != null)
            playerHealthUi.fillImage.fillAmount = displayedHealthNormalized;

        if (playerHealthUi.maxHpCapFill != null)
            playerHealthUi.maxHpCapFill.fillAmount = PlayerMaxHpNormalized;
    }

    float GetDisplayedPlayerHealthNormalized()
    {
        return isPlayerQteActive ? currentFailGauge : GetPlayerHealthNormalized();
    }

    void ApplyPlayerHealthFillDisplay(float displayedHealthNormalized)
    {
        CombatHealth playerHealth = GetPlayerHealth();
        if (playerHealth == null)
            return;

        if (isPlayerQteActive)
            playerHealth.SetFillOverride(displayedHealthNormalized);
        else
            playerHealth.ClearFillOverride();
    }

    float GetPlayerHealthNormalized()
    {
        CombatHealth playerHealth = GetPlayerHealth();
        if (playerHealth != null)
            return playerHealth.Normalized;

        return 1f;
    }

    CombatHealth GetPlayerHealth()
    {
        return playerController != null ? playerController.Health : null;
    }

    float GetGaugeHoldTimerDamage(PlayerStaggerMinigameController minigame)
    {
        if (minigame == null || minigame != gaugeHoldMinigame || gaugeHoldTimerDamageRatio <= 0f)
            return 0f;

        float normalizedLoss = Mathf.Max(0f, qteStartFailGauge - qteLowestFailGauge);
        return GetPlayerDamageForNormalizedHealthLoss(normalizedLoss) * gaugeHoldTimerDamageRatio;
    }

    float GetPlayerDamageForNormalizedHealthLoss(float normalizedLoss)
    {
        CombatHealth playerHealth = GetPlayerHealth();
        float maxHealth = playerHealth != null ? playerHealth.MaxHealth : 1f;
        return Mathf.Clamp01(normalizedLoss) * maxHealth;
    }

    void ApplyPlayerCombatHealthDamage(float damage)
    {
        CombatHealth playerHealth = GetPlayerHealth();
        if (damage <= 0f || playerHealth == null)
            return;

        playerHealth.ApplyDamage(damage);
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
        if (isPlayerQteActive)
            qteLowestFailGauge = Mathf.Min(qteLowestFailGauge, currentFailGauge);

        currentRecoverGauge = Mathf.Clamp01(recoverGaugeNormalized);
        UpdatePlayerHealthUi();
    }

    float GetEnemyIncomingDamageMultiplier()
    {
        if (enemyController != null && enemyController.TryGetComponent(out EnemyController enemy))
            return enemy.IncomingDamageMultiplier;

        return 1f;
    }

    PlayerStaggerMinigameContext CreateMinigameContext(PlayerStaggerGaugeStartLayout gaugeStartLayout)
    {
        return new PlayerStaggerMinigameContext
        {
            failGaugeNormalized = currentFailGauge,
            recoverGaugeNormalized = currentRecoverGauge,
            maxHpNormalized = GetPlayerHealthNormalized(),
            difficultyMultiplier = GetPlayerStaggerDifficulty(),
            gaugeStartLayout = gaugeStartLayout,
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

        float playerHealthNormalized = GetPlayerHealthNormalized();
        float failGauge = isPlayerQteActive ? currentFailGauge : playerHealthNormalized;

        if (slideQteMinigame != null)
            slideQteMinigame.ApplyIdleState(failGauge, currentRecoverGauge, playerHealthNormalized);

        if (gaugeHoldMinigame != null)
            gaugeHoldMinigame.ApplyIdleState(failGauge, currentRecoverGauge, playerHealthNormalized);
    }

    void ApplyMinigameAvailability(PlayerStaggerMinigameController selectedMinigame)
    {
        if (slideQteMinigame != null)
            slideQteMinigame.SetSuppressed(slideQteMinigame != selectedMinigame);

        if (gaugeHoldMinigame != null)
            gaugeHoldMinigame.SetSuppressed(gaugeHoldMinigame != selectedMinigame);
    }
}
