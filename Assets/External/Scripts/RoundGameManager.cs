using System;
using System.Collections;
using TMPro;
using UnityEngine;

public enum RoundGameState
{
    Boot,
    RoundActive,
    WaitingRoundChoice,
    WaitingRerollResolution,
    Victory,
    Defeat,
}

public enum BetweenRoundChoice
{
    Heal,
    Reroll,
}

public class RoundGameManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private SlotController slotController;
    [SerializeField] private BattleUiController battleUiController;
    [SerializeField] private PlayerController playerController;
    [SerializeField] private PlayerController enemyController;
    [SerializeField] private Canvas roundChoiceCanvas;
    [SerializeField] private Canvas canvas_Slot;

    [Header("다음 라운드 대기 UI (선택/슬롯 종료 후 ~ 라운드 시작 전)")]
    [Tooltip("비우면 이벤트만 발생. 연결 시 남은 초(올림)를 표시합니다.")]
    [SerializeField] private TextMeshProUGUI nextRoundCountdownText;
    [SerializeField] private string countdownTextFormat = "다음 라운드까지 {0}초";

    [Header("Round Rules")]
    [SerializeField] private float roundDurationSeconds = 30f;
    [SerializeField] private int totalRounds = 3;
    [SerializeField] private float rerollAutoCloseDelay = 3f;
    [SerializeField] private float nextRoundDelayAfterSlotClose = 5f;

    [Header("Player Health")]
    [SerializeField] private float playerMaxHealth = 5f;
    [SerializeField] private float playerStartHealth = 5f;
    [SerializeField] private float playerDamagePerEnemyHit = 1f;
    [SerializeField] private float playerDamageOnQteFail = 1f;
    [SerializeField] private float healAmountBetweenRounds = 2f;

    public event Action<int, float> RoundStarted;
    public event Action<float, float> RoundTimeChanged;
    public event Action<float, float> PlayerHealthChanged;
    public event Action<RoundGameState> StateChanged;
    public event Action<int, PlayerBuild, float, float> BetweenRoundsChoiceRequested;

    /// <summary>슬롯/선택 후 다음 라운드 진입 대기 중 매 프레임. remaining은 초 단위, duration은 해당 대기 총 길이.</summary>
    public event Action<float, float> NextRoundIntermissionTick;

    public RoundGameState State { get; private set; } = RoundGameState.Boot;
    public int CurrentRound { get; private set; }
    public float RoundTimeRemaining { get; private set; }
    public float PlayerCurrentHealth { get; private set; }
    public bool HasCurrentBuild { get; private set; }
    public PlayerBuild CurrentBuild { get; private set; }

    Coroutine rerollFlowRoutine;

    void Reset()
    {
        TryAutoAssignReferences();
    }

    void Awake()
    {
        TryAutoAssignReferences();
        ApplyValidation();
        PlayerCurrentHealth = Mathf.Clamp(playerStartHealth, 0f, playerMaxHealth);

        if (slotController != null && slotController.HasResolvedBuild)
        {
            CurrentBuild = slotController.CurrentBuild;
            HasCurrentBuild = true;
        }

        ApplyRoundCombatActive(false);
    }

    void Start()
    {
        SetChoiceCanvasActive(false);
        SetSlotCanvasActive(false);

        if (slotController != null)
            slotController.SetKeyboardInputEnabled(false);

        NotifyPlayerHealthChanged();
        StartRound(1);
    }

    void OnEnable()
    {
        SubscribeEvents();
    }

    void OnDisable()
    {
        if (rerollFlowRoutine != null)
        {
            StopCoroutine(rerollFlowRoutine);
            rerollFlowRoutine = null;
        }

        ClearIntermissionCountdownDisplay();
        UnsubscribeEvents();
    }

    void OnValidate()
    {
        ApplyValidation();
    }

    void Update()
    {
        if (State != RoundGameState.RoundActive)
            return;

        RoundTimeRemaining = Mathf.Max(0f, RoundTimeRemaining - Time.deltaTime);
        RoundTimeChanged?.Invoke(RoundTimeRemaining, roundDurationSeconds);

        if (RoundTimeRemaining <= 0f)
            HandleRoundTimeout();
    }

    public void ChooseHeal()
    {
        if (State != RoundGameState.WaitingRoundChoice)
            return;

        if (rerollFlowRoutine != null)
            return;

        SetChoiceCanvasActive(false);
        HealPlayer(healAmountBetweenRounds);
        rerollFlowRoutine = StartCoroutine(HealThenIntermissionNextRound());
    }

    public void ChooseReroll()
    {
        if (State != RoundGameState.WaitingRoundChoice)
            return;

        SetState(RoundGameState.WaitingRerollResolution);
        SetChoiceCanvasActive(false);
        SetSlotCanvasActive(true);

        if (slotController == null)
        {
            if (rerollFlowRoutine != null)
                StopCoroutine(rerollFlowRoutine);

            rerollFlowRoutine = StartCoroutine(StartNextRoundAfterDelay(nextRoundDelayAfterSlotClose));
            return;
        }

        slotController.SetKeyboardInputEnabled(true);
    }

    public void StartRound(int roundNumber)
    {
        if (roundNumber <= 0)
            roundNumber = 1;

        CurrentRound = roundNumber;
        Debug.Log($"=== Round {CurrentRound} 시작 ===");
        RoundTimeRemaining = roundDurationSeconds;
        SetChoiceCanvasActive(false);
        SetSlotCanvasActive(false);

        if (slotController != null)
            slotController.SetKeyboardInputEnabled(false);

        if (battleUiController != null)
            battleUiController.ResetEnemyHealth();

        SetState(RoundGameState.RoundActive);
        ClearIntermissionCountdownDisplay();
        RoundStarted?.Invoke(CurrentRound, roundDurationSeconds);
        RoundTimeChanged?.Invoke(RoundTimeRemaining, roundDurationSeconds);
    }

    void HandleRoundTimeout()
    {
        if (State != RoundGameState.RoundActive)
            return;

        if (CurrentRound >= totalRounds)
        {
            LoseGame();
            return;
        }

        SetState(RoundGameState.WaitingRoundChoice);
        SetChoiceCanvasActive(true);
        Debug.Log($"Round {CurrentRound} 종료. 플레이어 선택 대기... (다음 라운드에서 체력 회복 또는 빌드 재뽑기)");
        BetweenRoundsChoiceRequested?.Invoke(CurrentRound + 1, CurrentBuild, PlayerCurrentHealth, playerMaxHealth);
    }

    void HandleEnemyDefeated()
    {
        if (State != RoundGameState.RoundActive)
            return;

        WinGame();
    }

    void HandleBuildResolved(PlayerBuild build)
    {
        CurrentBuild = build;
        HasCurrentBuild = true;

        if (State == RoundGameState.WaitingRerollResolution)
        {
            if (slotController != null)
                slotController.SetKeyboardInputEnabled(false);

            if (rerollFlowRoutine != null)
                StopCoroutine(rerollFlowRoutine);

            rerollFlowRoutine = StartCoroutine(CompleteRerollFlow());
        }
    }

    void HandleCombatEvent(CombatEventData eventData)
    {
        if (State != RoundGameState.RoundActive)
            return;

        if (eventData.Kind != CombatEventKind.AttackHit)
            return;

        if (eventData.Actor == enemyController && eventData.Opponent == playerController)
            ApplyPlayerDamage(playerDamagePerEnemyHit);
    }

    void HandlePlayerQteEnded(QteEndReason endReason)
    {
        if (State != RoundGameState.RoundActive)
            return;

        if (endReason == QteEndReason.Fail)
            ApplyPlayerDamage(playerDamageOnQteFail);
    }

    void ApplyPlayerDamage(float damage)
    {
        if (damage <= 0f)
            return;

        PlayerCurrentHealth = Mathf.Clamp(PlayerCurrentHealth - damage, 0f, playerMaxHealth);
        NotifyPlayerHealthChanged();

        if (PlayerCurrentHealth <= 0f)
            LoseGame();
    }

    void HealPlayer(float amount)
    {
        if (amount <= 0f)
            return;

        PlayerCurrentHealth = Mathf.Clamp(PlayerCurrentHealth + amount, 0f, playerMaxHealth);
        NotifyPlayerHealthChanged();
    }

    void AdvanceToNextRound()
    {
        int nextRound = CurrentRound + 1;

        if (nextRound > totalRounds)
        {
            LoseGame();
            return;
        }

        StartRound(nextRound);
    }

    void WinGame()
    {
        Debug.Log("플레이어 승리!");
        SetState(RoundGameState.Victory);
    }

    void LoseGame()
    {
        Debug.Log("플레이어 패배...");
        SetState(RoundGameState.Defeat);
    }

    void SetState(RoundGameState newState)
    {
        if (State == newState)
            return;

        State = newState;
        ApplyRoundCombatActive(newState == RoundGameState.RoundActive);
        StateChanged?.Invoke(State);
    }

    void ApplyRoundCombatActive(bool active)
    {
        if (playerController != null)
            playerController.SetRoundCombatActive(active);

        if (enemyController != null)
            enemyController.SetRoundCombatActive(active);

        if (!active && battleUiController != null)
            battleUiController.StopPlayerQte(QteEndReason.Success);
    }

    void NotifyPlayerHealthChanged()
    {
        PlayerHealthChanged?.Invoke(PlayerCurrentHealth, playerMaxHealth);
    }

    void SubscribeEvents()
    {
        if (slotController != null)
            slotController.BuildResolved += HandleBuildResolved;

        if (battleUiController != null)
        {
            battleUiController.EnemyDefeated += HandleEnemyDefeated;
            battleUiController.PlayerQteEnded += HandlePlayerQteEnded;
        }

        if (playerController != null)
            playerController.CombatEventRaised += HandleCombatEvent;

        if (enemyController != null && enemyController != playerController)
            enemyController.CombatEventRaised += HandleCombatEvent;
    }

    void UnsubscribeEvents()
    {
        if (slotController != null)
            slotController.BuildResolved -= HandleBuildResolved;

        if (battleUiController != null)
        {
            battleUiController.EnemyDefeated -= HandleEnemyDefeated;
            battleUiController.PlayerQteEnded -= HandlePlayerQteEnded;
        }

        if (playerController != null)
            playerController.CombatEventRaised -= HandleCombatEvent;

        if (enemyController != null && enemyController != playerController)
            enemyController.CombatEventRaised -= HandleCombatEvent;
    }

    void TryAutoAssignReferences()
    {
        if (slotController == null)
            slotController = FindObjectOfType<SlotController>();

        if (battleUiController == null)
            battleUiController = FindObjectOfType<BattleUiController>();

        if (roundChoiceCanvas == null)
            roundChoiceCanvas = FindCanvasByName("Canvas_Choice");

        if (canvas_Slot == null)
            canvas_Slot = FindCanvasByName("Canvas_Slot");

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
    }

    void ApplyValidation()
    {
        roundDurationSeconds = Mathf.Max(1f, roundDurationSeconds);
        totalRounds = Mathf.Max(1, totalRounds);
        rerollAutoCloseDelay = Mathf.Max(0f, rerollAutoCloseDelay);
        nextRoundDelayAfterSlotClose = Mathf.Max(0f, nextRoundDelayAfterSlotClose);
        playerMaxHealth = Mathf.Max(1f, playerMaxHealth);
        playerStartHealth = Mathf.Clamp(playerStartHealth, 0f, playerMaxHealth);
        playerDamagePerEnemyHit = Mathf.Max(0f, playerDamagePerEnemyHit);
        playerDamageOnQteFail = Mathf.Max(0f, playerDamageOnQteFail);
        healAmountBetweenRounds = Mathf.Max(0f, healAmountBetweenRounds);
    }

    IEnumerator HealThenIntermissionNextRound()
    {
        yield return RunIntermissionCountdown(nextRoundDelayAfterSlotClose);
        rerollFlowRoutine = null;
        AdvanceToNextRound();
    }

    IEnumerator CompleteRerollFlow()
    {
        float elapsed = 0f;

        while (elapsed < rerollAutoCloseDelay)
        {
            if (Input.GetKeyDown(KeyCode.Space))
                break;

            elapsed += Time.deltaTime;
            yield return null;
        }

        SetSlotCanvasActive(false);

        if (slotController != null)
            slotController.SetKeyboardInputEnabled(false);

        yield return RunIntermissionCountdown(nextRoundDelayAfterSlotClose);
        rerollFlowRoutine = null;
        AdvanceToNextRound();
    }

    IEnumerator StartNextRoundAfterDelay(float delay)
    {
        SetSlotCanvasActive(false);

        if (slotController != null)
            slotController.SetKeyboardInputEnabled(false);

        yield return RunIntermissionCountdown(delay);
        rerollFlowRoutine = null;
        AdvanceToNextRound();
    }

    IEnumerator RunIntermissionCountdown(float duration)
    {
        if (duration <= 0f)
            yield break;

        float remaining = duration;
        while (remaining > 0f)
        {
            PushIntermissionCountdown(remaining, duration);
            yield return null;
            remaining -= Time.deltaTime;
        }

        PushIntermissionCountdown(0f, duration);
    }

    void PushIntermissionCountdown(float remainingSeconds, float duration)
    {
        NextRoundIntermissionTick?.Invoke(remainingSeconds, duration);

        if (nextRoundCountdownText == null)
            return;

        int sec = Mathf.Max(0, Mathf.CeilToInt(remainingSeconds));
        nextRoundCountdownText.text = string.Format(countdownTextFormat, sec);
    }

    void ClearIntermissionCountdownDisplay()
    {
        NextRoundIntermissionTick?.Invoke(0f, 0f);

        if (nextRoundCountdownText != null)
            nextRoundCountdownText.text = string.Empty;
    }

    void SetChoiceCanvasActive(bool isActive)
    {
        if (roundChoiceCanvas != null)
            roundChoiceCanvas.gameObject.SetActive(isActive);
    }

    void SetSlotCanvasActive(bool isActive)
    {
        if (canvas_Slot != null)
            canvas_Slot.gameObject.SetActive(isActive);
    }

    static Canvas FindCanvasByName(string canvasName)
    {
        Canvas[] canvases = FindObjectsOfType<Canvas>(true);
        for (int i = 0; i < canvases.Length; i++)
        {
            if (canvases[i] != null && canvases[i].name == canvasName)
                return canvases[i];
        }

        return null;
    }
}
