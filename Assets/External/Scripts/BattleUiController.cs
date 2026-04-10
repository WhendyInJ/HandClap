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
    private class EnemyHealthUiBinding
    {
        public Image fillImage;
        [Min(1f)] public float maxHealth = 5f;
        [Min(0f)] public float damagePerHit = 1f;
    }

    [Serializable]
    private class PlayerQteUiBinding
    {
        public GameObject root;
        public Image failGaugeFill;
        public Image recoverGaugeFill;
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
        public bool hideWhenInactive = true;
    }

    [Header("Combat Targets")]
    [SerializeField] private PlayerController playerController;
    [SerializeField] private PlayerController enemyController;

    [Header("Enemy Health")]
    [SerializeField] private EnemyHealthUiBinding enemyHealth = new EnemyHealthUiBinding();

    [Header("Player QTE")]
    [SerializeField] private PlayerQteUiBinding playerQte = new PlayerQteUiBinding();

    private float currentEnemyHealth;
    private float currentFailGauge;
    private float currentRecoverGauge;
    private float qteBarDirection = 1f;
    private bool isPlayerQteActive;

    public event Action<QteEndReason> PlayerQteEnded;

    public bool IsPlayerQteActive => isPlayerQteActive;
    public float EnemyHealthNormalized => Normalize(currentEnemyHealth, enemyHealth.maxHealth);
    public float FailGaugeNormalized => currentFailGauge;
    public float RecoverGaugeNormalized => currentRecoverGauge;

    void Reset()
    {
        TryAutoAssignControllers();
        ApplyImmediateUiState();
    }

    void Awake()
    {
        currentEnemyHealth = Mathf.Max(0f, enemyHealth.maxHealth);
        currentFailGauge = 1f;
        currentRecoverGauge = 0f;
        ApplyImmediateUiState();
    }

    void OnEnable()
    {
        SubscribeCombatEvents();
        ApplyQteVisibility();
    }

    void OnDisable()
    {
        UnsubscribeCombatEvents();
    }

    void OnValidate()
    {
        enemyHealth.maxHealth = Mathf.Max(1f, enemyHealth.maxHealth);
        enemyHealth.damagePerHit = Mathf.Max(0f, enemyHealth.damagePerHit);
        playerQte.movingBarSpeed = Mathf.Max(0f, playerQte.movingBarSpeed);
        playerQte.inputBoxSpeed = Mathf.Max(0f, playerQte.inputBoxSpeed);
        playerQte.failDrainPerSecond = Mathf.Max(0f, playerQte.failDrainPerSecond);
        playerQte.recoverPerSecond = Mathf.Max(0f, playerQte.recoverPerSecond);

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
        currentEnemyHealth = enemyHealth.maxHealth;
        UpdateEnemyHealthUi();
    }

    public void ApplyEnemyDamage(float damage)
    {
        if (damage <= 0f)
            return;

        currentEnemyHealth = Mathf.Clamp(currentEnemyHealth - damage, 0f, enemyHealth.maxHealth);
        UpdateEnemyHealthUi();
    }

    public void StartPlayerQte()
    {
        if (isPlayerQteActive)
            return;

        isPlayerQteActive = true;
        currentFailGauge = 1f;
        currentRecoverGauge = 0f;
        qteBarDirection = 1f;

        ResetQtePositions();
        UpdateQteGaugeUi();
        ApplyQteVisibility();
    }

    public void StopPlayerQte(QteEndReason endReason)
    {
        if (!isPlayerQteActive)
            return;

        isPlayerQteActive = false;
        ApplyQteVisibility();
        PlayerQteEnded?.Invoke(endReason);
    }

    void HandleCombatEvent(CombatEventData eventData)
    {
        if (eventData.Kind != CombatEventKind.AttackHit)
            return;

        if (eventData.Actor == playerController && eventData.Opponent == enemyController)
        {
            ApplyEnemyDamage(enemyHealth.damagePerHit);
            return;
        }

        if (eventData.Actor == enemyController && eventData.Opponent == playerController)
            StartPlayerQte();
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

    void TryAutoAssignControllers()
    {
        if (playerController != null && enemyController != null)
            return;

        PlayerController[] controllers = FindObjectsOfType<PlayerController>();
        for (int i = 0; i < controllers.Length; i++)
        {
            PlayerController controller = controllers[i];

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

    void ApplyImmediateUiState()
    {
        if (!Application.isPlaying)
        {
            currentEnemyHealth = enemyHealth.maxHealth;
            currentFailGauge = 1f;
            currentRecoverGauge = 0f;
            isPlayerQteActive = false;
        }

        UpdateEnemyHealthUi();
        UpdateQteGaugeUi();
        ApplyQteVisibility();
    }

    void UpdateEnemyHealthUi()
    {
        if (enemyHealth.fillImage == null)
            return;

        enemyHealth.fillImage.fillAmount = EnemyHealthNormalized;
    }

    void UpdateQteGaugeState()
    {
        bool isRecovering = IsQteSuccessState();

        if (isRecovering)
            currentRecoverGauge = Mathf.MoveTowards(currentRecoverGauge, 1f, playerQte.recoverPerSecond * Time.deltaTime);
        else
            currentFailGauge = Mathf.MoveTowards(currentFailGauge, 0f, playerQte.failDrainPerSecond * Time.deltaTime);

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
        float nextX = playerQte.movingBar.anchoredPosition.x + qteBarDirection * playerQte.movingBarSpeed * Time.deltaTime;

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
        float nextX = playerQte.inputBox.anchoredPosition.x + input * playerQte.inputBoxSpeed * Time.deltaTime;
        SetAnchoredX(playerQte.inputBox, Mathf.Clamp(nextX, -maxX, maxX));
    }

    bool IsQteSuccessState()
    {
        if (playerQte.movingBar == null || playerQte.inputBox == null)
            return false;

        float distance = Mathf.Abs(playerQte.movingBar.anchoredPosition.x - playerQte.inputBox.anchoredPosition.x);
        float overlapRange = (GetRectWidth(playerQte.movingBar) + GetRectWidth(playerQte.inputBox))
            * 0.5f
            * playerQte.overlapTolerance;

        return distance <= overlapRange;
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

    static float Normalize(float current, float max)
    {
        return max <= 0f ? 0f : Mathf.Clamp01(current / max);
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
