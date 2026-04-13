using System;
using System.Collections;
using TMPro;
using UnityEngine;

public enum RoundGameState
{
    Boot,
    RoundActive,
    RoundSettling,
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

    [Header("공통 상태 텍스트 (nextRoundCountdownText)")]
    [Tooltip("라운드 진행·소강·다음 라운드 대기 등을 한 곳에 표시합니다. 비우면 이벤트만 씁니다.")]
    [SerializeField] private TextMeshProUGUI nextRoundCountdownText;
    private string statusRoundActiveFormat = "라운드 {0}/{1}<br>전투 종료까지 {2:00}초";
    private string statusSettlingFormat = "라운드 {0}/{1}<br>선택 화면까지 {2:00}초";
    private string statusWaitingChoiceFormat = "라운드 {0}/{1} 종료<br>힐 또는 리롤을 선택하세요";
    private string statusIntermissionFormat = "다음 라운드 {0}/{1}<br>전투 시작까지 {2:00}초";
    private string statusRerollFormat = "라운드 {0}/{1}<br>슬롯에서 빌드를 선택하세요";

    [Header("Round Rules")]
    [SerializeField] private float roundDurationSeconds = 30f;
    [SerializeField] private int totalRounds = 3;
    [Tooltip("라운드 종료 후 선택 UI 전 소강 시간(초). RoundSettling 구간.")]
    [SerializeField] private float roundSettlingSeconds = 1.5f;
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
    Coroutine roundSettleRoutine;

    float settlingRemainingDisplay;

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
        ApplyBetweenRoundIdleMotion(false);
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

        StopRoundSettleRoutine();
        ClearNextRoundStatusDisplay();
        UnsubscribeEvents();
    }

    void OnValidate()
    {
        ApplyValidation();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.E))
        {
            TryAutoAssignReferences();
            if (State == RoundGameState.RoundActive && playerController != null)
            {
                Debug.Log(
                    $"[빌드 디버그] 라운드 {CurrentRound} — Element={playerController.Element}, " +
                    $"BodyType={playerController.BodyType}, HandSize={playerController.HandSize}");
            }
        }

        if (State != RoundGameState.RoundActive)
            return;

        RoundTimeRemaining = Mathf.Max(0f, RoundTimeRemaining - Time.deltaTime);
        RoundTimeChanged?.Invoke(RoundTimeRemaining, roundDurationSeconds);

        if (RoundTimeRemaining <= 0f && !AnyActorBlocksRoundTimeout())
            HandleRoundTimeout();

        RefreshNextRoundStatusText();
    }

    bool AnyActorBlocksRoundTimeout()
    {
        TryAutoAssignReferences();

        if (playerController != null && playerController.BlocksRoundTransition())
            return true;

        if (enemyController != null && enemyController.BlocksRoundTransition())
            return true;

        return false;
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

        StopRoundSettleRoutine();

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

        TryAutoAssignReferences();
        ApplyCurrentBuildToPlayer();

        SetState(RoundGameState.RoundActive);
        RoundStarted?.Invoke(CurrentRound, roundDurationSeconds);
        RoundTimeChanged?.Invoke(RoundTimeRemaining, roundDurationSeconds);
        RefreshNextRoundStatusText();
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

        TryAutoAssignReferences();
        StopRoundSettleRoutine();
        settlingRemainingDisplay = Mathf.Max(0f, roundSettlingSeconds);
        SetState(RoundGameState.RoundSettling);
        RefreshNextRoundStatusText();
        SetChoiceCanvasActive(false);
        Debug.Log($"Round {CurrentRound} 종료. 소강 후 선택 UI.");
        roundSettleRoutine = StartCoroutine(RoundSettleThenWaitingChoice());
    }

    IEnumerator RoundSettleThenWaitingChoice()
    {
        if (roundSettlingSeconds <= 0f)
        {
            roundSettleRoutine = null;

            if (State != RoundGameState.RoundSettling)
                yield break;

            SetState(RoundGameState.WaitingRoundChoice);
            SetChoiceCanvasActive(true);
            Debug.Log($"Round {CurrentRound} 선택 대기.");
            BetweenRoundsChoiceRequested?.Invoke(CurrentRound + 1, CurrentBuild, PlayerFailGaugeNormalized, 1f);
            yield break;
        }

        settlingRemainingDisplay = roundSettlingSeconds;

        while (settlingRemainingDisplay > 0f)
        {
            RefreshNextRoundStatusText();
            yield return null;
            settlingRemainingDisplay -= Time.deltaTime;
        }

        settlingRemainingDisplay = 0f;
        RefreshNextRoundStatusText();

        roundSettleRoutine = null;

        if (State != RoundGameState.RoundSettling)
            yield break;

        SetState(RoundGameState.WaitingRoundChoice);
        SetChoiceCanvasActive(true);
        Debug.Log($"Round {CurrentRound} 선택 대기.");
        BetweenRoundsChoiceRequested?.Invoke(CurrentRound + 1, CurrentBuild, PlayerFailGaugeNormalized, 1f);
    }

    void StopRoundSettleRoutine()
    {
        if (roundSettleRoutine == null)
            return;

        StopCoroutine(roundSettleRoutine);
        roundSettleRoutine = null;
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
        TryAutoAssignReferences();
        ApplyCurrentBuildToPlayer();

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
        ApplyBetweenRoundIdleMotion(newState == RoundGameState.RoundSettling);

        if (newState == RoundGameState.Victory || newState == RoundGameState.Defeat)
            ClearNextRoundStatusDisplay();
        else
        {
            if (newState == RoundGameState.RoundActive)
                settlingRemainingDisplay = 0f;

            if (newState == RoundGameState.RoundActive
                || newState == RoundGameState.WaitingRoundChoice
                || newState == RoundGameState.WaitingRerollResolution)
                RefreshNextRoundStatusText();
        }

        StateChanged?.Invoke(State);
    }

    void ApplyBetweenRoundIdleMotion(bool enabled)
    {
        if (playerController != null)
            playerController.SetBetweenRoundIdleMotion(enabled);

        if (enemyController != null)
            enemyController.SetBetweenRoundIdleMotion(enabled);
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

    void ApplyCurrentBuildToPlayer()
    {
        if (!HasCurrentBuild || playerController == null)
            return;

        playerController.ApplyBuild(CurrentBuild);
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
        roundSettlingSeconds = Mathf.Max(0f, roundSettlingSeconds);
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
        int nextRound = Mathf.Min(CurrentRound + 1, totalRounds);
        nextRoundCountdownText.text = string.Format(statusIntermissionFormat, nextRound, totalRounds, sec);
    }

    void RefreshNextRoundStatusText()
    {
        if (nextRoundCountdownText == null)
            return;

        switch (State)
        {
            case RoundGameState.RoundActive:
                int battleSec = Mathf.Max(0, Mathf.CeilToInt(RoundTimeRemaining));
                nextRoundCountdownText.text = string.Format(
                    statusRoundActiveFormat,
                    CurrentRound,
                    totalRounds,
                    battleSec);
                break;

            case RoundGameState.RoundSettling:
                int settleSec = Mathf.Max(0, Mathf.CeilToInt(settlingRemainingDisplay));
                nextRoundCountdownText.text = string.Format(
                    statusSettlingFormat,
                    CurrentRound,
                    totalRounds,
                    settleSec);
                break;

            case RoundGameState.WaitingRoundChoice:
                if (rerollFlowRoutine != null)
                    return;

                nextRoundCountdownText.text = string.Format(
                    statusWaitingChoiceFormat,
                    CurrentRound,
                    totalRounds);
                break;

            case RoundGameState.WaitingRerollResolution:
                if (rerollFlowRoutine != null)
                    return;

                nextRoundCountdownText.text = string.Format(statusRerollFormat, CurrentRound, totalRounds);
                break;

            default:
                break;
        }
    }

    void ClearNextRoundStatusDisplay()
    {
        NextRoundIntermissionTick?.Invoke(0f, 0f);
        settlingRemainingDisplay = 0f;

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
