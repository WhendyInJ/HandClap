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
    [SerializeField] private ClashTugMinigameController clashTugMinigameController;
    [SerializeField] private CombatActorController playerController;
    [SerializeField] private CombatActorController enemyController;
    [SerializeField] private Canvas roundChoiceCanvas;
    [SerializeField] private Canvas canvas_Slot;

    [Header("다음 라운드 대기 UI")]
    [Tooltip("비워두면 이벤트만 발생합니다. 연결된 텍스트가 있으면 남은 시간을 표시합니다.")]
    [SerializeField] private TextMeshProUGUI nextRoundCountdownText;
    [SerializeField] private string countdownTextFormat = "다음 라운드까지 {0}초";

    [Header("Round Rules")]
    [SerializeField] private float roundDurationSeconds = 30f;
    [SerializeField] private int totalRounds = 3;
    [SerializeField] private float rerollAutoCloseDelay = 3f;
    [SerializeField] private float nextRoundDelayAfterSlotClose = 5f;

    [Header("Player Recovery")]
    [SerializeField, Range(0f, 1f)] private float failGaugeRecoveryBetweenRounds = 1f;

    public event Action<int, float> RoundStarted;
    public event Action<float, float> RoundTimeChanged;
    public event Action<RoundGameState> StateChanged;
    public event Action<int, PlayerBuild, float, float> BetweenRoundsChoiceRequested;
    public event Action<float, float> NextRoundIntermissionTick;

    public RoundGameState State { get; private set; } = RoundGameState.Boot;
    public int CurrentRound { get; private set; }
    public float RoundTimeRemaining { get; private set; }
    /// <summary>
    /// 라운드 간 UI 표시용 플레이어 체력 (MaxHP 상한선 기준).
    /// QTE 드레인 게이지가 아닌 영구 MaxHP 값을 사용한다.
    /// </summary>
    public float PlayerFailGaugeNormalized => battleUiController != null ? battleUiController.PlayerMaxHpNormalized : 0f;
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

        if (battleUiController != null)
            battleUiController.RecoverPlayerFailGauge(failGaugeRecoveryBetweenRounds);

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
        {
            if (roundNumber == 1)
                battleUiController.ResetPlayerFailGauge();

            battleUiController.ResetEnemyHealth();
        }

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
        Debug.Log($"Round {CurrentRound} 종료. 플레이어 선택 대기.");
        BetweenRoundsChoiceRequested?.Invoke(CurrentRound + 1, CurrentBuild, PlayerFailGaugeNormalized, 1f);
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

    void HandlePlayerQteEnded(QteEndReason endReason)
    {
        if (State != RoundGameState.RoundActive)
            return;

        if (endReason == QteEndReason.Fail)
            LoseGame();
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

        if (!active && clashTugMinigameController != null)
            clashTugMinigameController.StopMinigame(ClashMinigameResult.Interrupted);
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
    }

    void TryAutoAssignReferences()
    {
        if (slotController == null)
            slotController = FindFirstObjectByType<SlotController>();

        if (battleUiController == null)
            battleUiController = FindFirstObjectByType<BattleUiController>();

        if (clashTugMinigameController == null)
            clashTugMinigameController = FindFirstObjectByType<ClashTugMinigameController>();

        if (roundChoiceCanvas == null)
            roundChoiceCanvas = FindCanvasByName("Canvas_Choice");

        if (canvas_Slot == null)
            canvas_Slot = FindCanvasByName("Canvas_Slot");

        if (playerController != null && enemyController != null)
        {
            EnsureClashMinigameController();
            return;
        }

        if (playerController == null)
            playerController = FindPlayerController();

        if (enemyController == null)
            enemyController = FindEnemyController();

        if (playerController != null && enemyController != null)
        {
            EnsureClashMinigameController();
            return;
        }

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

        EnsureClashMinigameController();
    }

    void EnsureClashMinigameController()
    {
        if (clashTugMinigameController == null)
            return;

        if (battleUiController != null)
            clashTugMinigameController.SetBattleUiController(battleUiController);

        clashTugMinigameController.SetCombatActors(playerController, enemyController);
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

    void ApplyValidation()
    {
        roundDurationSeconds = Mathf.Max(1f, roundDurationSeconds);
        totalRounds = Mathf.Max(1, totalRounds);
        rerollAutoCloseDelay = Mathf.Max(0f, rerollAutoCloseDelay);
        nextRoundDelayAfterSlotClose = Mathf.Max(0f, nextRoundDelayAfterSlotClose);
        failGaugeRecoveryBetweenRounds = Mathf.Clamp01(failGaugeRecoveryBetweenRounds);
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
        Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < canvases.Length; i++)
        {
            if (canvases[i] != null && canvases[i].name == canvasName)
                return canvases[i];
        }

        return null;
    }
}
