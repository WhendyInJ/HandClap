using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

public enum RoundGameState
{
    Boot,
    TutorialCoach,
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

[Serializable]
public class RoundTutorialCoachStep
{
    [TextArea(4, 18)]
    public string coachText;

    [Tooltip("이 단계에서 코치 텍스트를 보여 줄 시간(초). 0이면 아래 목표 달성형 단계가 아니면 바로 다음 단계.")]
    [Min(0f)]
    public float displaySeconds = 5f;

    [Tooltip("이 단계에서 목표로 보여 줄 플레이어 일반 공격 횟수. 0이면 진행률을 표시하지 않습니다.")]
    [Min(0)]
    public int requiredPlayerBasicAttacks;

    [Tooltip("이 단계에서 목표로 보여 줄 플레이어 회피 성공 횟수. 0이면 진행률을 표시하지 않습니다.")]
    [Min(0)]
    public int requiredPlayerDodgeSuccesses;

    [Tooltip("이 단계에서 목표로 보여 줄 '적 페이크를 아무 입력 없이 넘기기' 성공 횟수. 0이면 진행률을 표시하지 않습니다.")]
    [Min(0)]
    public int requiredIgnoredEnemyFeints;

    [Tooltip("이 단계에서 목표로 보여 줄 강제 공격 경합 횟수. 0이면 진행률을 표시하지 않습니다.")]
    [Min(0)]
    public int requiredForcedClashes;

    [Tooltip("이 단계 동안 적 AI를 멈추고 튜토리얼 전용 동작만 사용합니다.")]
    public bool pauseEnemyAi = true;

    [Tooltip("플레이어가 일반 공격을 시작하면 적도 바로 일반 공격을 시도합니다.")]
    public bool enemyReactsToPlayerBasicAttack;

    [Tooltip("0보다 크면 이 간격(초)마다 적이 일반 공격을 시도합니다.")]
    [Min(0f)]
    public float enemyAutoAttackInterval;

    [Tooltip("자동 공격이 있으면 단계 시작 직후 첫 공격을 바로 시도합니다.")]
    public bool enemyAutoAttackStartsImmediately;

    [Tooltip("자동 행동에서 적이 공격과 페이크를 섞어 사용합니다.")]
    public bool enemyAutoMixAttackAndFeint;

    [Tooltip("자동 행동을 섞어 쓸 때 페이크 확률(0~1). 예: 0.3 = 30%")]
    [Range(0f, 1f)]
    public float enemyAutoFeintChance = 0.5f;

    [Tooltip("자동 행동 대신 플레이어와 적이 같은 타이밍에 공격해서 경합 미니게임으로 강제 진입합니다.")]
    public bool forceSimultaneousAttackClash;

    [Tooltip("시간 제한 대신 목표를 채울 때까지 계속 진행합니다. 회피 튜토리얼용.")]
    public bool waitUntilRequirementsCompleted;

    public UnityEvent onStepStarted;
    public UnityEvent onStepFinished;
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
    private string statusTutorialCoachFormat = "코치 {0}/{1}<br>이 단계 {2:00}초";
    private string statusTutorialCoachUntimedFormat = "코치 {0}/{1}<br>제한시간 없음";

    [Header("Round Rules")]
    [SerializeField] private float roundDurationSeconds = 30f;
    [SerializeField] private int totalRounds = 3;
    [Tooltip("라운드 종료 후 선택 UI 전 소강 시간(초). RoundSettling 구간.")]
    [SerializeField] private float roundSettlingSeconds = 1.5f;
    [SerializeField] private float rerollAutoCloseDelay = 3f;
    [SerializeField] private float nextRoundDelayAfterSlotClose = 5f;

    [Header("Player Recovery")]
    [SerializeField, Range(0f, 1f)] private float failGaugeRecoveryBetweenRounds = 1f;

    [Header("라운드 1 전 · 코치 튜토리얼")]
    [Tooltip("켜면 Start 시 아래 단계를 먼저 진행한 뒤 라운드 1을 시작합니다.")]
    [SerializeField] private bool runCoachTutorialBeforeFirstRound;

    [Tooltip("스승 대사용 TMP. 비우면 nextRoundCountdownText에 단계 요약만 씁니다.")]
    [SerializeField] private TextMeshProUGUI coachTutorialText;

    [Tooltip("튜토리얼 중에만 켤 패널 루트(선택).")]
    [SerializeField] private GameObject coachTutorialRoot;

    [SerializeField] private RoundTutorialCoachStep[] coachTutorialSteps = Array.Empty<RoundTutorialCoachStep>();

    [Tooltip("튜토리얼 동안 전투 입력(공격·회피 등)을 허용할지.")]
    [SerializeField] private bool coachTutorialAllowCombat = true;

    [Tooltip("튜토리얼 중 적 격파·QTE 실패로 승/패 처리하지 않음.")]
    [SerializeField] private bool suppressCombatResolutionDuringCoachTutorial = true;

    [Tooltip("단계 남은 시간에 Unscaled Time 사용.")]
    [SerializeField] private bool coachTutorialUseUnscaledTime;

    [Tooltip("코치 TMP 아래에 남은 초를 덧붙임.")]
    [SerializeField] private bool appendSecondsToCoachText = true;

    public event Action<int, float> RoundStarted;
    public event Action<float, float> RoundTimeChanged;
    public event Action<RoundGameState> StateChanged;
    public event Action<int, PlayerBuild, float, float> BetweenRoundsChoiceRequested;
    public event Action<float, float> NextRoundIntermissionTick;
    public event Action<int, int, float> TutorialCoachStepTick;

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
    public bool IsCoachTutorialDamageSuppressed => State == RoundGameState.TutorialCoach;

    Coroutine rerollFlowRoutine;
    Coroutine roundSettleRoutine;
    Coroutine tutorialCoachRoutine;

    EnemyController enemyAiController;
    float settlingRemainingDisplay;
    int tutorialCoachStepIndex;
    float tutorialCoachStepRemaining;
    int tutorialCoachPlayerBasicAttackCount;
    int tutorialCoachPlayerDodgeSuccessCount;
    int tutorialCoachIgnoredEnemyFeintCount;
    int tutorialCoachForcedClashCount;
    float tutorialCoachEnemyAutoAttackTimer;
    bool tutorialForcedClashAwaitingResult;
    Coroutine tutorialForcedClashRoutine;

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

        if (runCoachTutorialBeforeFirstRound && coachTutorialSteps != null && coachTutorialSteps.Length > 0)
            tutorialCoachRoutine = StartCoroutine(RunCoachTutorialThenRoundOne());
        else
            StartRound(1);
    }

    void OnEnable()
    {
        SubscribeEvents();
    }

    void OnDisable()
    {
        StopTutorialCoachRoutine();

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
        if (suppressCombatResolutionDuringCoachTutorial && State == RoundGameState.TutorialCoach)
            return;

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
        if (suppressCombatResolutionDuringCoachTutorial && State == RoundGameState.TutorialCoach)
            return;

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
        bool roundCombatActive =
            newState == RoundGameState.RoundActive
            || (newState == RoundGameState.TutorialCoach && coachTutorialAllowCombat);

        ApplyRoundCombatActive(roundCombatActive);
        ApplyBetweenRoundIdleMotion(newState == RoundGameState.RoundSettling);

        if (newState == RoundGameState.Victory || newState == RoundGameState.Defeat)
            ClearNextRoundStatusDisplay();
        else
        {
            if (newState == RoundGameState.RoundActive)
                settlingRemainingDisplay = 0f;

            if (newState == RoundGameState.RoundActive
                || newState == RoundGameState.TutorialCoach
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

        if (playerController != null)
        {
            playerController.BasicAttackStarted += HandlePlayerBasicAttackStarted;
            playerController.CombatEventRaised += HandlePlayerCombatEventRaised;
        }

        if (enemyController != null && enemyController != playerController)
            enemyController.CombatEventRaised += HandleEnemyCombatEventRaised;

        if (battleUiController != null)
        {
            battleUiController.EnemyDefeated += HandleEnemyDefeated;
            battleUiController.PlayerQteEnded += HandlePlayerQteEnded;
        }

        if (clashTugMinigameController != null)
            clashTugMinigameController.MinigameEnded += HandleClashMinigameEnded;
    }

    void UnsubscribeEvents()
    {
        if (slotController != null)
            slotController.BuildResolved -= HandleBuildResolved;

        if (playerController != null)
        {
            playerController.BasicAttackStarted -= HandlePlayerBasicAttackStarted;
            playerController.CombatEventRaised -= HandlePlayerCombatEventRaised;
        }

        if (enemyController != null && enemyController != playerController)
            enemyController.CombatEventRaised -= HandleEnemyCombatEventRaised;

        if (battleUiController != null)
        {
            battleUiController.EnemyDefeated -= HandleEnemyDefeated;
            battleUiController.PlayerQteEnded -= HandlePlayerQteEnded;
        }

        if (clashTugMinigameController != null)
            clashTugMinigameController.MinigameEnded -= HandleClashMinigameEnded;
    }

    void HandlePlayerBasicAttackStarted()
    {
        if (State != RoundGameState.TutorialCoach)
            return;

        RoundTutorialCoachStep step = GetCurrentCoachTutorialStep();
        if (step == null)
            return;

        if (!step.enemyReactsToPlayerBasicAttack && !step.forceSimultaneousAttackClash)
            tutorialCoachPlayerBasicAttackCount++;

        if (step.forceSimultaneousAttackClash)
            TryTriggerTutorialForcedClash();
        else if (step.enemyReactsToPlayerBasicAttack)
            TryTriggerTutorialEnemyAction(step);

        ApplyCoachTutorialStepVisual(step);
        RefreshNextRoundStatusText();
    }

    void HandlePlayerCombatEventRaised(CombatEventData eventData)
    {
        if (State != RoundGameState.TutorialCoach)
            return;

        RoundTutorialCoachStep step = GetCurrentCoachTutorialStep();
        if (step == null)
            return;

        if (eventData.Kind == CombatEventKind.DodgeSucceeded)
        {
            if (eventData.Actor != playerController || eventData.Opponent != enemyController)
                return;

            tutorialCoachPlayerDodgeSuccessCount++;
            ApplyCoachTutorialStepVisual(step);
            RefreshNextRoundStatusText();
            return;
        }

        if (!step.enemyReactsToPlayerBasicAttack
            || eventData.Actor != playerController
            || eventData.Opponent != enemyController)
            return;

        switch (eventData.Kind)
        {
            case CombatEventKind.AttackHit:
                if (enemyController != null && enemyController.IsStaggered)
                    return;

                tutorialCoachPlayerBasicAttackCount++;
                ApplyCoachTutorialStepVisual(step);
                RefreshNextRoundStatusText();
                break;

            case CombatEventKind.AttackMeetingWin:
                tutorialCoachPlayerBasicAttackCount++;
                ApplyCoachTutorialStepVisual(step);
                RefreshNextRoundStatusText();
                break;
        }
    }

    void HandleEnemyCombatEventRaised(CombatEventData eventData)
    {
        if (State != RoundGameState.TutorialCoach || eventData.Kind != CombatEventKind.FeintFailed)
            return;

        if (eventData.Actor != enemyController || eventData.Opponent != playerController)
            return;

        RoundTutorialCoachStep step = GetCurrentCoachTutorialStep();
        if (step == null)
            return;

        tutorialCoachIgnoredEnemyFeintCount++;
        ApplyCoachTutorialStepVisual(step);
        RefreshNextRoundStatusText();
    }

    void HandleClashMinigameEnded(ClashMinigameResult result, float gaugeNormalized)
    {
        if (!tutorialForcedClashAwaitingResult)
            return;

        tutorialForcedClashAwaitingResult = false;

        if (State != RoundGameState.TutorialCoach || result == ClashMinigameResult.Interrupted)
            return;

        RoundTutorialCoachStep step = GetCurrentCoachTutorialStep();
        if (step == null || !step.forceSimultaneousAttackClash)
            return;

        tutorialCoachForcedClashCount++;
        ApplyCoachTutorialStepVisual(step);
        RefreshNextRoundStatusText();
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
            EnsureEnemyAiController();
            EnsureClashMinigameController();
            return;
        }

        if (playerController == null)
            playerController = FindPlayerController();

        if (enemyController == null)
            enemyController = FindEnemyController();

        if (playerController != null && enemyController != null)
        {
            EnsureEnemyAiController();
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

        EnsureEnemyAiController();
        EnsureClashMinigameController();
    }

    void EnsureEnemyAiController()
    {
        if (enemyAiController != null)
            return;

        if (enemyController != null)
            enemyAiController = enemyController.GetComponent<EnemyController>();

        if (enemyAiController == null)
            enemyAiController = FindFirstObjectByType<EnemyController>();
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

        if (coachTutorialSteps == null)
            return;

        for (int i = 0; i < coachTutorialSteps.Length; i++)
        {
            if (coachTutorialSteps[i] == null)
                continue;

            coachTutorialSteps[i].displaySeconds = Mathf.Max(0f, coachTutorialSteps[i].displaySeconds);
            coachTutorialSteps[i].requiredPlayerBasicAttacks = Mathf.Max(0, coachTutorialSteps[i].requiredPlayerBasicAttacks);
            coachTutorialSteps[i].requiredPlayerDodgeSuccesses = Mathf.Max(0, coachTutorialSteps[i].requiredPlayerDodgeSuccesses);
            coachTutorialSteps[i].requiredIgnoredEnemyFeints = Mathf.Max(0, coachTutorialSteps[i].requiredIgnoredEnemyFeints);
            coachTutorialSteps[i].requiredForcedClashes = Mathf.Max(0, coachTutorialSteps[i].requiredForcedClashes);
            coachTutorialSteps[i].enemyAutoAttackInterval = Mathf.Max(0f, coachTutorialSteps[i].enemyAutoAttackInterval);
            coachTutorialSteps[i].enemyAutoFeintChance = Mathf.Clamp01(coachTutorialSteps[i].enemyAutoFeintChance);
        }
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

            case RoundGameState.TutorialCoach:
                int totalCoach = coachTutorialSteps != null ? coachTutorialSteps.Length : 0;
                RoundTutorialCoachStep currentStep = GetCurrentCoachTutorialStep();
                if (currentStep != null && currentStep.waitUntilRequirementsCompleted && StepHasAnyRequirements(currentStep))
                {
                    nextRoundCountdownText.text = string.Format(
                        statusTutorialCoachUntimedFormat,
                        tutorialCoachStepIndex + 1,
                        totalCoach);
                    break;
                }

                int coachSec = Mathf.Max(0, Mathf.CeilToInt(tutorialCoachStepRemaining));
                nextRoundCountdownText.text = string.Format(
                    statusTutorialCoachFormat,
                    tutorialCoachStepIndex + 1,
                    totalCoach,
                    coachSec);
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

    IEnumerator RunCoachTutorialThenRoundOne()
    {
        TryAutoAssignReferences();
        CurrentRound = 0;
        SetCoachTutorialRootActive(true);
        SetState(RoundGameState.TutorialCoach);

        for (int i = 0; i < coachTutorialSteps.Length; i++)
        {
            tutorialCoachStepIndex = i;
            RoundTutorialCoachStep step = coachTutorialSteps[i];
            if (step == null)
                continue;

            BeginCoachTutorialStep(step);
            step.onStepStarted?.Invoke();

            float duration = Mathf.Max(0f, step.displaySeconds);
            bool useUntimedRequirements = step.waitUntilRequirementsCompleted && StepHasAnyRequirements(step);
            tutorialCoachStepRemaining = duration;

            while (State == RoundGameState.TutorialCoach)
            {
                if (useUntimedRequirements)
                {
                    if (IsCoachTutorialStepComplete(step))
                        break;
                }
                else if (tutorialCoachStepRemaining <= 0f)
                {
                    break;
                }

                TickCoachTutorialStep(step);
                ApplyCoachTutorialStepVisual(step);
                TutorialCoachStepTick?.Invoke(i, coachTutorialSteps.Length, tutorialCoachStepRemaining);
                RefreshNextRoundStatusText();
                yield return null;

                if (!useUntimedRequirements)
                {
                    tutorialCoachStepRemaining -= coachTutorialUseUnscaledTime
                        ? Time.unscaledDeltaTime
                        : Time.deltaTime;
                }
            }

            ApplyCoachTutorialStepVisual(step);
            step.onStepFinished?.Invoke();
            EndCoachTutorialStep();
        }

        tutorialCoachRoutine = null;
        EndCoachTutorialStep();
        ClearCoachTutorialVisuals();
        SetCoachTutorialRootActive(false);
        StartRound(1);
    }

    void BeginCoachTutorialStep(RoundTutorialCoachStep step)
    {
        tutorialCoachPlayerBasicAttackCount = 0;
        tutorialCoachPlayerDodgeSuccessCount = 0;
        tutorialCoachIgnoredEnemyFeintCount = 0;
        tutorialCoachForcedClashCount = 0;
        tutorialForcedClashAwaitingResult = false;
        tutorialCoachEnemyAutoAttackTimer = step.enemyAutoAttackStartsImmediately
            ? 0f
            : Mathf.Max(0f, step.enemyAutoAttackInterval);

        TryAutoAssignReferences();
        if (enemyAiController != null)
            enemyAiController.SetManualAiPaused(step.pauseEnemyAi);
    }

    void TickCoachTutorialStep(RoundTutorialCoachStep step)
    {
        if (step == null || step.forceSimultaneousAttackClash || step.enemyAutoAttackInterval <= 0f)
            return;

        tutorialCoachEnemyAutoAttackTimer -= coachTutorialUseUnscaledTime
            ? Time.unscaledDeltaTime
            : Time.deltaTime;

        while (tutorialCoachEnemyAutoAttackTimer <= 0f)
        {
            bool started = TryTriggerTutorialEnemyAction(step);

            if (started)
                tutorialCoachEnemyAutoAttackTimer += step.enemyAutoAttackInterval;
            else
                tutorialCoachEnemyAutoAttackTimer = 0.05f;
        }
    }

    void EndCoachTutorialStep()
    {
        tutorialCoachPlayerBasicAttackCount = 0;
        tutorialCoachPlayerDodgeSuccessCount = 0;
        tutorialCoachIgnoredEnemyFeintCount = 0;
        tutorialCoachForcedClashCount = 0;
        tutorialCoachEnemyAutoAttackTimer = 0f;
        tutorialForcedClashAwaitingResult = false;

        if (tutorialForcedClashRoutine != null)
        {
            StopCoroutine(tutorialForcedClashRoutine);
            tutorialForcedClashRoutine = null;
        }

        if (enemyAiController != null)
            enemyAiController.SetManualAiPaused(false);
    }

    RoundTutorialCoachStep GetCurrentCoachTutorialStep()
    {
        if (coachTutorialSteps == null
            || tutorialCoachStepIndex < 0
            || tutorialCoachStepIndex >= coachTutorialSteps.Length)
            return null;

        return coachTutorialSteps[tutorialCoachStepIndex];
    }

    static bool StepHasAnyRequirements(RoundTutorialCoachStep step)
    {
        return step != null
            && (step.requiredPlayerBasicAttacks > 0
                || step.requiredPlayerDodgeSuccesses > 0
                || step.requiredIgnoredEnemyFeints > 0
                || step.requiredForcedClashes > 0);
    }

    bool IsCoachTutorialStepComplete(RoundTutorialCoachStep step)
    {
        if (step == null)
            return true;

        bool basicAttackComplete = step.requiredPlayerBasicAttacks <= 0
            || tutorialCoachPlayerBasicAttackCount >= step.requiredPlayerBasicAttacks;
        bool dodgeComplete = step.requiredPlayerDodgeSuccesses <= 0
            || tutorialCoachPlayerDodgeSuccessCount >= step.requiredPlayerDodgeSuccesses;
        bool ignoredFeintComplete = step.requiredIgnoredEnemyFeints <= 0
            || tutorialCoachIgnoredEnemyFeintCount >= step.requiredIgnoredEnemyFeints;
        bool forcedClashComplete = step.requiredForcedClashes <= 0
            || tutorialCoachForcedClashCount >= step.requiredForcedClashes;

        return basicAttackComplete && dodgeComplete && ignoredFeintComplete && forcedClashComplete;
    }

    bool TryTriggerTutorialEnemyAction(RoundTutorialCoachStep step)
    {
        TryAutoAssignReferences();

        if (State != RoundGameState.TutorialCoach)
            return false;

        if (step != null && step.enemyAutoMixAttackAndFeint && enemyAiController != null)
        {
            return UnityEngine.Random.value < step.enemyAutoFeintChance
                ? enemyAiController.TryTelegraphedFeintAttack()
                : enemyAiController.TryTelegraphedBasicAttack();
        }

        if (enemyAiController != null)
            return enemyAiController.TryTelegraphedBasicAttack();

        return enemyController != null && enemyController.TryPush(false);
    }

    bool TryTriggerTutorialForcedClash()
    {
        TryAutoAssignReferences();

        if (State != RoundGameState.TutorialCoach
            || tutorialForcedClashAwaitingResult
            || tutorialForcedClashRoutine != null
            || clashTugMinigameController == null
            || clashTugMinigameController.IsActive
            || playerController == null
            || enemyController == null)
            return false;

        if ((battleUiController != null && battleUiController.IsPlayerQteActive)
            || playerController.IsStaggered
            || enemyController.IsStaggered
            || !enemyController.CanAttemptDecision)
            return false;

        bool enemyStarted = enemyAiController != null
            ? enemyAiController.TryBasicAttackOnly()
            : enemyController.TryPush(false);

        if (!enemyStarted)
            return false;

        tutorialForcedClashRoutine = StartCoroutine(BeginTutorialForcedClashNextFrame());
        return true;
    }

    IEnumerator BeginTutorialForcedClashNextFrame()
    {
        yield return null;
        tutorialForcedClashRoutine = null;

        if (State != RoundGameState.TutorialCoach
            || clashTugMinigameController == null
            || clashTugMinigameController.IsActive
            || (battleUiController != null && battleUiController.IsPlayerQteActive))
            yield break;

        tutorialForcedClashAwaitingResult = true;
        clashTugMinigameController.StartMinigame();
    }

    void ApplyCoachTutorialStepVisual(RoundTutorialCoachStep step)
    {
        if (step == null || coachTutorialText == null)
            return;

        string body = step.coachText ?? string.Empty;
        if (step.requiredPlayerBasicAttacks > 0)
        {
            int clampedCount = Mathf.Min(tutorialCoachPlayerBasicAttackCount, step.requiredPlayerBasicAttacks);
            body += $"\n\n<size=90%><color=#D8D8D8>일반 공격 {clampedCount}/{step.requiredPlayerBasicAttacks}</color></size>";
        }

        if (step.requiredPlayerDodgeSuccesses > 0)
        {
            int clampedCount = Mathf.Min(tutorialCoachPlayerDodgeSuccessCount, step.requiredPlayerDodgeSuccesses);
            body += $"\n\n<size=90%><color=#D8D8D8>회피 성공 {clampedCount}/{step.requiredPlayerDodgeSuccesses}</color></size>";
        }

        if (step.requiredIgnoredEnemyFeints > 0)
        {
            int clampedCount = Mathf.Min(tutorialCoachIgnoredEnemyFeintCount, step.requiredIgnoredEnemyFeints);
            body += $"\n\n<size=90%><color=#D8D8D8>페이크 무시 {clampedCount}/{step.requiredIgnoredEnemyFeints}</color></size>";
        }

        if (step.requiredForcedClashes > 0)
        {
            int clampedCount = Mathf.Min(tutorialCoachForcedClashCount, step.requiredForcedClashes);
            body += $"\n\n<size=90%><color=#D8D8D8>공격 경합 {clampedCount}/{step.requiredForcedClashes}</color></size>";
        }

        if (appendSecondsToCoachText && tutorialCoachStepRemaining > 0f)
        {
            body +=
                $"\n\n<size=85%><color=#AAAAAA>({Mathf.CeilToInt(tutorialCoachStepRemaining)}초)</color></size>";
        }

        coachTutorialText.text = body;
    }

    void ClearCoachTutorialVisuals()
    {
        if (coachTutorialText != null)
            coachTutorialText.text = string.Empty;

        tutorialCoachStepRemaining = 0f;
    }

    void SetCoachTutorialRootActive(bool active)
    {
        if (coachTutorialRoot != null)
            coachTutorialRoot.SetActive(active);
    }

    void StopTutorialCoachRoutine()
    {
        if (tutorialCoachRoutine == null)
            return;

        StopCoroutine(tutorialCoachRoutine);
        tutorialCoachRoutine = null;
        EndCoachTutorialStep();
        ClearCoachTutorialVisuals();
        SetCoachTutorialRootActive(false);

        if (State == RoundGameState.TutorialCoach)
            SetState(RoundGameState.Boot);
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
