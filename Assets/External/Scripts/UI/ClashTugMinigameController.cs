using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public enum ClashMinigameResult
{
    PlayerWin,
    EnemyWin,
    Draw,
    Interrupted,
}

[DisallowMultipleComponent]
public class ClashTugMinigameController : MonoBehaviour
{
    [Serializable]
    private class UiBinding
    {
        [Header("Scene UI")]
        public GameObject root;
        public RectTransform track;
        public RectTransform marker;
        public Image playerFill;
        public Image enemyFill;

        [Header("Child Names")]
        public string trackName = "Track";
        public string markerName = "Marker";
        public string playerFillName = "PlayerFill";
        public string enemyFillName = "EnemyFill";
        public bool hideWhenInactive = true;
    }

    [Header("References")]
    [SerializeField] private BattleUiController battleUiController;
    [SerializeField] private CombatActorController playerController;
    [SerializeField] private CombatActorController enemyController;
    [SerializeField] private CombatActorStats playerStats;
    [SerializeField] private CombatActorStats enemyStats;

    [Header("UI")]
    [SerializeField] private UiBinding ui = new UiBinding();

    [Header("Rules")]
    [SerializeField] private KeyCode mashKey = KeyCode.Space;
    [SerializeField, Min(0.1f)] private float durationSeconds = 2.5f;
    [SerializeField, Range(0f, 1f)] private float playerPushPerPress = 0.03f;
    [SerializeField, Range(0f, 1f)] private float enemyPushPerSecond = 0.08f;
    [SerializeField, Range(0f, 1f)] private float maxPushFromStart = 0.3f;
    [SerializeField, Range(0f, 1f)] private float winThreshold = 0.5f;
    [SerializeField, Min(0f)] private float resumeDelayAfterMinigame = 0.2f;

    private float gaugeNormalized = 0.5f;
    private float startGaugeNormalized = 0.5f;
    private float maxGaugeNormalized = 0.8f;
    private float timeRemaining;
    private bool isActive;
    private bool restorePlayerCombatAfterClash;
    private bool restoreEnemyCombatAfterClash;
    private Coroutine restoreCombatRoutine;

    public event Action<ClashMinigameResult, float> MinigameEnded;

    public bool IsActive => isActive;
    public float GaugeNormalized => gaugeNormalized;
    public float StartGaugeNormalized => startGaugeNormalized;
    public float MaxGaugeNormalized => maxGaugeNormalized;
    public float TimeRemaining => timeRemaining;

    void Reset()
    {
        TryAutoAssignReferences();
        BindUiChildrenIfNeeded();
    }

    void Awake()
    {
        TryAutoAssignReferences();
        ApplyValidation();
        BindUiChildrenIfNeeded();
        ApplyVisibility();
        UpdateUi();
    }

    void OnEnable()
    {
        TryAutoAssignReferences();
        SubscribeCombatEvents();
        BindUiChildrenIfNeeded();
        ApplyVisibility();
    }

    void OnDisable()
    {
        UnsubscribeCombatEvents();
        CancelPendingCombatRestore();
        EndMinigame(ClashMinigameResult.Interrupted, applyResult: false, restoreCombat: false);
    }

    void OnValidate()
    {
        ApplyValidation();

        if (!Application.isPlaying)
            BindUiChildrenIfNeeded();
    }

    void Update()
    {
        if (!isActive)
            return;

        UpdateMinigame();
    }

    public void SetBattleUiController(BattleUiController controller)
    {
        battleUiController = controller;
    }

    public void SetCombatActors(CombatActorController player, CombatActorController enemy)
    {
        bool resubscribe = isActiveAndEnabled;
        if (resubscribe)
            UnsubscribeCombatEvents();

        playerController = player;
        enemyController = enemy;
        playerStats = playerController != null ? playerController.Stats : playerStats;
        enemyStats = enemyController != null ? enemyController.Stats : enemyStats;

        if (resubscribe)
            SubscribeCombatEvents();
    }

    public void StartMinigame()
    {
        if (isActive)
            return;

        CancelPendingCombatRestore();
        BindUiChildrenIfNeeded();
        RefreshStats();

        startGaugeNormalized = CalculateStartGauge();
        maxGaugeNormalized = Mathf.Clamp01(startGaugeNormalized + maxPushFromStart);
        gaugeNormalized = startGaugeNormalized;
        timeRemaining = durationSeconds;
        isActive = true;

        restorePlayerCombatAfterClash = playerController != null && playerController.RoundCombatActive;
        restoreEnemyCombatAfterClash = enemyController != null && enemyController.RoundCombatActive;

        if (playerController != null)
            playerController.SetRoundCombatActive(false);

        if (enemyController != null)
            enemyController.SetRoundCombatActive(false);

        UpdateUi();
        ApplyVisibility();
    }

    public void StopMinigame(ClashMinigameResult result)
    {
        if (result == ClashMinigameResult.Interrupted)
            CancelPendingCombatRestore();

        EndMinigame(result, applyResult: false, restoreCombat: result != ClashMinigameResult.Interrupted);
    }

    void HandleCombatEvent(CombatEventData eventData)
    {
        if (eventData.Kind != CombatEventKind.AttackClashed)
            return;

        if (!IsEventBetweenPlayerAndEnemy(eventData))
            return;

        StartMinigame();
    }

    void UpdateMinigame()
    {
        if (Input.GetKeyDown(mashKey))
            gaugeNormalized = Mathf.Min(maxGaugeNormalized, gaugeNormalized + playerPushPerPress);

        if (enemyPushPerSecond > 0f)
            gaugeNormalized = Mathf.Max(0f, gaugeNormalized - enemyPushPerSecond * Time.deltaTime);

        timeRemaining = Mathf.Max(0f, timeRemaining - Time.deltaTime);
        UpdateUi();

        if (timeRemaining <= 0f)
            CompleteMinigame(EvaluateResult());
    }

    void CompleteMinigame(ClashMinigameResult result)
    {
        EndMinigame(result, applyResult: true, restoreCombat: true);
    }

    void EndMinigame(ClashMinigameResult result, bool applyResult, bool restoreCombat)
    {
        if (!isActive)
            return;

        isActive = false;
        ApplyVisibility();

        if (restoreCombat)
            ScheduleRestoreCombatAfterDelay();

        if (applyResult)
            ApplyClashDamage();

        MinigameEnded?.Invoke(result, gaugeNormalized);
    }

    void ScheduleRestoreCombatAfterDelay()
    {
        CancelPendingCombatRestore();
        restoreCombatRoutine = StartCoroutine(RestoreCombatAfterDelay());
    }

    IEnumerator RestoreCombatAfterDelay()
    {
        if (resumeDelayAfterMinigame > 0f)
            yield return new WaitForSeconds(resumeDelayAfterMinigame);
        else
            yield return null;

        restoreCombatRoutine = null;
        RestoreCombatAfterClash();
    }

    void CancelPendingCombatRestore()
    {
        if (restoreCombatRoutine == null)
            return;

        StopCoroutine(restoreCombatRoutine);
        restoreCombatRoutine = null;
    }

    void RestoreCombatAfterClash()
    {
        if (playerController != null)
            playerController.SetRoundCombatActive(restorePlayerCombatAfterClash);

        if (enemyController != null)
            enemyController.SetRoundCombatActive(restoreEnemyCombatAfterClash);
    }

    void ApplyClashDamage()
    {
        float playerAttackDamage = playerStats != null
            ? playerStats.CalculateDamageToEnemy(enemyStats)
            : 0f;

        float enemyAttackDamage = enemyStats != null
            ? enemyStats.CalculateDamageToPlayer(playerStats)
            : 0f;

        float totalDamage = Mathf.Max(0f, playerAttackDamage + enemyAttackDamage);
        if (totalDamage <= 0f)
            return;

        float enemyDamageRatio = Mathf.Clamp01(gaugeNormalized);
        float playerDamageRatio = 1f - enemyDamageRatio;

        if (battleUiController != null)
        {
            battleUiController.ApplyEnemyDamage(totalDamage * enemyDamageRatio);
            battleUiController.ApplyPlayerDamage(totalDamage * playerDamageRatio);
            return;
        }

        if (enemyController != null && enemyController.Health != null)
            enemyController.Health.ApplyDamage(totalDamage * enemyDamageRatio * GetEnemyIncomingDamageMultiplier());
    }

    float GetEnemyIncomingDamageMultiplier()
    {
        if (enemyController != null && enemyController.TryGetComponent(out EnemyController enemy))
            return enemy.IncomingDamageMultiplier;

        return 1f;
    }

    ClashMinigameResult EvaluateResult()
    {
        const float drawTolerance = 0.001f;
        if (Mathf.Abs(gaugeNormalized - winThreshold) <= drawTolerance)
            return ClashMinigameResult.Draw;

        return gaugeNormalized > winThreshold
            ? ClashMinigameResult.PlayerWin
            : ClashMinigameResult.EnemyWin;
    }

    float CalculateStartGauge()
    {
        Element playerElement = playerStats != null ? playerStats.Element : PlayerBuild.Default.Element;
        Element enemyElement = enemyStats != null ? enemyStats.Element : PlayerBuild.Default.Element;

        if (playerElement == enemyElement)
            return 0.5f;

        if (SlotStatRules.HasElementAdvantage(playerElement, enemyElement))
            return 0.7f;

        return SlotStatRules.HasElementAdvantage(enemyElement, playerElement)
            ? 0.3f
            : 0.5f;
    }

    void BindUiChildrenIfNeeded()
    {
        if (ui.root == null)
            return;

        Transform root = ui.root.transform;

        if (ui.track == null)
            ui.track = FindChildComponent<RectTransform>(root, ui.trackName);

        if (ui.marker == null)
            ui.marker = FindChildComponent<RectTransform>(root, ui.markerName);

        if (ui.playerFill == null)
            ui.playerFill = FindChildComponent<Image>(root, ui.playerFillName);

        if (ui.enemyFill == null)
            ui.enemyFill = FindChildComponent<Image>(root, ui.enemyFillName);
    }

    void UpdateUi()
    {
        float normalized = Mathf.Clamp01(gaugeNormalized);

        if (ui.playerFill != null)
            ui.playerFill.fillAmount = normalized;

        if (ui.enemyFill != null)
            ui.enemyFill.fillAmount = 1f - normalized;

        if (ui.track != null && ui.marker != null)
        {
            float maxX = GetMovementLimit(ui.track, ui.marker);
            SetAnchoredX(ui.marker, Mathf.Lerp(-maxX, maxX, normalized));
        }
    }

    void ApplyVisibility()
    {
        if (ui.root == null || !Application.isPlaying)
            return;

        ui.root.SetActive(isActive || !ui.hideWhenInactive);
    }

    void TryAutoAssignReferences()
    {
        if (battleUiController == null)
            battleUiController = FindFirstObjectByType<BattleUiController>();

        if (battleUiController != null)
        {
            if (playerController == null)
                playerController = battleUiController.PlayerController;

            if (enemyController == null)
                enemyController = battleUiController.EnemyController;

            if (playerStats == null)
                playerStats = battleUiController.PlayerStats;

            if (enemyStats == null)
                enemyStats = battleUiController.EnemyStats;
        }

        if (playerController == null)
            playerController = FindPlayerController();

        if (enemyController == null)
            enemyController = FindEnemyController();

        RefreshStats();
    }

    void RefreshStats()
    {
        if (playerController != null && playerStats == null)
            playerStats = playerController.Stats;

        if (enemyController != null && enemyStats == null)
            enemyStats = enemyController.Stats;
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

    bool IsEventBetweenPlayerAndEnemy(CombatEventData eventData)
    {
        bool hasPlayer = eventData.Actor == playerController || eventData.Opponent == playerController;
        bool hasEnemy = eventData.Actor == enemyController || eventData.Opponent == enemyController;
        return hasPlayer && hasEnemy;
    }

    void ApplyValidation()
    {
        durationSeconds = Mathf.Max(0.1f, durationSeconds);
        playerPushPerPress = Mathf.Clamp01(playerPushPerPress);
        enemyPushPerSecond = Mathf.Clamp01(enemyPushPerSecond);
        maxPushFromStart = Mathf.Clamp01(maxPushFromStart);
        winThreshold = Mathf.Clamp01(winThreshold);
        resumeDelayAfterMinigame = Mathf.Max(0f, resumeDelayAfterMinigame);
    }

    static T FindChildComponent<T>(Transform root, string childName) where T : Component
    {
        if (root == null || string.IsNullOrWhiteSpace(childName))
            return null;

        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] != null && children[i].name == childName)
                return children[i].GetComponent<T>();
        }

        return null;
    }

    static float GetMovementLimit(RectTransform track, RectTransform target)
    {
        if (track == null || target == null)
            return 0f;

        float trackHalfWidth = GetRectWidth(track) * 0.5f;
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
