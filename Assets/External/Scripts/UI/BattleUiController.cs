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

    [Serializable]
    private class PlayerQteUiBinding
    {
        public GameObject root;
        public Image failGaugeFill;
        public Image recoverGaugeFill;
        public Image maxHpCapFill;
        public RectTransform track;
        public RectTransform movingBar;
        public RectTransform inputBox;
        public KeyCode moveLeftKey = KeyCode.LeftArrow;
        public KeyCode moveRightKey = KeyCode.RightArrow;
        [Min(0f)] public float movingBarSpeed = 520f;
        [Min(0f)] public float inputBoxSpeed = 520f;
        [Range(0.25f, 1.5f)] public float overlapTolerance = 1f;
        [Min(0f)] public float failDrainPerSecond = 0.35f;
        [Min(0f)] public float recoverPerSecond = 0.65f;
        [Min(0f)] public float failGaugeLossPerDamage = 0.2f;
        public bool hideWhenInactive = false;
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

    [Header("Player QTE")]
    [SerializeField] private PlayerQteUiBinding playerQte = new PlayerQteUiBinding();

    [Header("Stagger")]
    [SerializeField, Min(0f)] private float enemyStaggerDuration = 2.0f;
    [SerializeField, Min(1f)] private float staggerBonusDamageMultiplier = 1.5f;
    [SerializeField, Range(0f, 0.5f)] private float dodgeFailGaugePenalty = 0.1f;

    private float playerMaxHpNormalized = 1f;
    private float currentFailGauge;
    private float currentRecoverGauge;
    private float lastPlayerDamageTaken;
    private float qteBarDirection = 1f;
    private bool isPlayerQteActive;

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
        ApplyImmediateUiState();
    }

    void Awake()
    {
        TryAutoAssignControllers();
        TryAutoAssignCombatComponents();
        playerMaxHpNormalized = 1f;
        currentFailGauge = 1f;
        currentRecoverGauge = 0f;
        ApplyImmediateUiState();
    }

    void OnEnable()
    {
        TryAutoAssignControllers();
        TryAutoAssignCombatComponents();
        SubscribeCombatEvents();
        SubscribeHealthEvents();
        ApplyImmediateUiState();
    }

    void OnDisable()
    {
        UnsubscribeCombatEvents();
        UnsubscribeHealthEvents();
    }

    void OnValidate()
    {
        playerQte.movingBarSpeed = Mathf.Max(0f, playerQte.movingBarSpeed);
        playerQte.inputBoxSpeed = Mathf.Max(0f, playerQte.inputBoxSpeed);
        playerQte.failDrainPerSecond = Mathf.Max(0f, playerQte.failDrainPerSecond);
        playerQte.recoverPerSecond = Mathf.Max(0f, playerQte.recoverPerSecond);
        playerQte.failGaugeLossPerDamage = Mathf.Max(0f, playerQte.failGaugeLossPerDamage);
        enemyStaggerDuration = Mathf.Max(0f, enemyStaggerDuration);
        staggerBonusDamageMultiplier = Mathf.Max(1f, staggerBonusDamageMultiplier);
        dodgeFailGaugePenalty = Mathf.Clamp(dodgeFailGaugePenalty, 0f, 0.5f);

        if (!Application.isPlaying)
            ApplyImmediateUiState();
    }

    void Update()
    {
        if (!isPlayerQteActive)
            return;

        UpdateQteMovingBar();
        UpdateQteInputBox();
        UpdateQteGaugeState();
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
        UpdateQteGaugeUi();
    }

    public void RecoverPlayerFailGauge(float normalizedAmount)
    {
        if (normalizedAmount <= 0f)
            return;

        playerMaxHpNormalized = Mathf.Clamp01(playerMaxHpNormalized + normalizedAmount);
        UpdatePlayerHealthUi();
        UpdateQteGaugeUi();
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

        isPlayerQteActive = true;
        lastPlayerDamageTaken = Mathf.Max(0f, incomingDamage);
        playerMaxHpNormalized = Mathf.Clamp01(
            playerMaxHpNormalized - lastPlayerDamageTaken * playerQte.failGaugeLossPerDamage);
        currentFailGauge = playerMaxHpNormalized;
        currentRecoverGauge = 0f;
        qteBarDirection = 1f;

        ResetQtePositions();
        UpdateQteGaugeUi();
        ApplyQteVisibility();

        if (playerController != null)
            playerController.RequestStagger(0f, leanForward);

        if (currentFailGauge <= 0f)
            StopPlayerQte(QteEndReason.Fail);
    }

    public void StopPlayerQte(QteEndReason endReason)
    {
        if (!isPlayerQteActive)
            return;

        isPlayerQteActive = false;

        if (playerController != null)
            playerController.StopStagger();

        if (endReason == QteEndReason.Success)
        {
            currentFailGauge = playerMaxHpNormalized;
            UpdateQteGaugeUi();
        }

        ApplyQteVisibility();
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
            playerMaxHpNormalized - damage * playerQte.failGaugeLossPerDamage);
        currentFailGauge = playerMaxHpNormalized;
        UpdatePlayerHealthUi();
        UpdateQteGaugeUi();

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
            StartPlayerQte(dodgeFailGaugePenalty / Mathf.Max(0.01f, playerQte.failGaugeLossPerDamage));
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
        UpdateQteGaugeUi();
        ApplyQteVisibility();
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

    void UpdateQteGaugeState()
    {
        bool isRecovering = IsQteSuccessState();

        if (isRecovering)
            currentRecoverGauge = Mathf.MoveTowards(currentRecoverGauge, 1f, GetEffectiveRecoverPerSecond() * Time.deltaTime);
        else
            currentFailGauge = Mathf.MoveTowards(currentFailGauge, 0f, GetEffectiveFailDrainPerSecond() * Time.deltaTime);

        UpdateQteGaugeUi();

        if (currentRecoverGauge >= 1f)
            StopPlayerQte(QteEndReason.Success);
        else if (currentFailGauge <= 0f)
            StopPlayerQte(QteEndReason.Fail);
    }

    void UpdateQteGaugeUi()
    {
        if (playerQte.failGaugeFill != null)
            playerQte.failGaugeFill.fillAmount = currentFailGauge;

        if (playerQte.recoverGaugeFill != null)
            playerQte.recoverGaugeFill.fillAmount = currentRecoverGauge;

        if (playerQte.maxHpCapFill != null)
            playerQte.maxHpCapFill.fillAmount = playerMaxHpNormalized;

        UpdatePlayerHealthUi();
    }

    void ApplyQteVisibility()
    {
        if (playerQte.root == null || !Application.isPlaying)
            return;

        playerQte.root.SetActive(isPlayerQteActive || !playerQte.hideWhenInactive);
    }

    void ResetQtePositions()
    {
        SetAnchoredX(playerQte.movingBar, 0f);
        SetAnchoredX(playerQte.inputBox, 0f);
    }

    void UpdateQteMovingBar()
    {
        if (playerQte.track == null || playerQte.movingBar == null)
            return;

        float maxX = GetMovementLimit(playerQte.movingBar);
        float nextX = playerQte.movingBar.anchoredPosition.x + qteBarDirection * GetEffectiveMovingBarSpeed() * Time.deltaTime;

        if (nextX > maxX)
        {
            nextX = maxX;
            qteBarDirection = -1f;
        }
        else if (nextX < -maxX)
        {
            nextX = -maxX;
            qteBarDirection = 1f;
        }

        SetAnchoredX(playerQte.movingBar, nextX);
    }

    void UpdateQteInputBox()
    {
        if (playerQte.track == null || playerQte.inputBox == null)
            return;

        float input = 0f;
        if (Input.GetKey(playerQte.moveLeftKey))
            input -= 1f;
        if (Input.GetKey(playerQte.moveRightKey))
            input += 1f;

        float maxX = GetMovementLimit(playerQte.inputBox);
        float nextX = playerQte.inputBox.anchoredPosition.x + input * GetEffectiveInputBoxSpeed() * Time.deltaTime;
        SetAnchoredX(playerQte.inputBox, Mathf.Clamp(nextX, -maxX, maxX));
    }

    bool IsQteSuccessState()
    {
        if (playerQte.movingBar == null || playerQte.inputBox == null)
            return false;

        float distance = Mathf.Abs(playerQte.movingBar.anchoredPosition.x - playerQte.inputBox.anchoredPosition.x);
        float overlapRange = (GetRectWidth(playerQte.movingBar) + GetRectWidth(playerQte.inputBox))
            * 0.5f
            * GetEffectiveOverlapTolerance();

        return distance <= overlapRange;
    }

    float GetEffectiveMovingBarSpeed()
    {
        return playerQte.movingBarSpeed * GetPlayerStaggerDifficulty();
    }

    float GetEffectiveInputBoxSpeed()
    {
        return playerQte.inputBoxSpeed / GetPlayerStaggerDifficulty();
    }

    float GetEffectiveOverlapTolerance()
    {
        return Mathf.Max(0.01f, playerQte.overlapTolerance / GetPlayerStaggerDifficulty());
    }

    float GetEffectiveFailDrainPerSecond()
    {
        return playerQte.failDrainPerSecond * GetPlayerStaggerDifficulty();
    }

    float GetEffectiveRecoverPerSecond()
    {
        return playerQte.recoverPerSecond / GetPlayerStaggerDifficulty();
    }

    float GetPlayerStaggerDifficulty()
    {
        return playerStats != null
            ? Mathf.Max(0.01f, playerStats.StaggerDifficultyMultiplier)
            : 1f;
    }

    float GetMovementLimit(RectTransform target)
    {
        if (playerQte.track == null || target == null)
            return 0f;

        float trackHalfWidth = GetRectWidth(playerQte.track) * 0.5f;
        float targetHalfWidth = GetRectWidth(target) * 0.5f;
        return Mathf.Max(0f, trackHalfWidth - targetHalfWidth);
    }

    static float GetRectWidth(RectTransform rectTransform)
    {
        return rectTransform == null ? 0f : rectTransform.rect.width;
    }

    static void SetAnchoredX(RectTransform rectTransform, float value)
    {
        if (rectTransform == null)
            return;

        Vector2 anchoredPosition = rectTransform.anchoredPosition;
        anchoredPosition.x = value;
        rectTransform.anchoredPosition = anchoredPosition;
    }
}
