using System.Collections;
using UnityEngine;

// ─── AI State Enums ───────────────────────────────────────────────────────────

public enum EnemyAIState
{
    Idle,
    Reacting,
    Acting,
}

/// <summary>플레이어 공격 감지 시 AI 반응 선택지</summary>
public enum EnemyReactionChoice
{
    CounterAttack, // 받아치기
    StayNeutral,   // 중립 유지
    Dodge,         // 회피
}

/// <summary>AI가 먼저 행동할 때 선택지</summary>
public enum EnemyActingChoice
{
    Attack,     // 공격
    FakeAttack, // 페이크
}

// ─── EnemyController ──────────────────────────────────────────────────────────

public class EnemyController : MonoBehaviour
{
    // ── Setup ──────────────────────────────────────────────────────────────────

    [Header("Enemy Setup")]
    [SerializeField] private CombatActorController targetController;
    [SerializeField] private Vector2 enemyPushDirection = Vector2.left;

    [Header("Debug Input")]
    [SerializeField] private bool enableDebugInput;
    [SerializeField] private KeyCode pushKey    = KeyCode.Keypad1;
    [SerializeField] private KeyCode dodgeKey   = KeyCode.Keypad2;
    [SerializeField] private KeyCode balanceKey = KeyCode.Keypad3;

    // ── AI Settings ────────────────────────────────────────────────────────────

    [Header("AI")]
    [SerializeField] private bool enableAi = true;
    [SerializeField] private bool enableAiDebugLogs = true;

    [Header("AI Pause")]
    [SerializeField] private bool pauseAiDuringMinigames = true;
    [SerializeField] private BattleUiController battleUiController;
    [SerializeField] private ClashTugMinigameController clashTugMinigameController;

    [Header("Action Cooldown (Idle → Acting)")]
    [Tooltip("대기 상태에서 AI가 먼저 행동을 취하기까지의 쿨타임 범위(초)")]
    [SerializeField] private float actionCooldownMin = 1.2f;
    [SerializeField] private float actionCooldownMax = 2.5f;

    [Header("Recovery Delay (state ends → back to Idle)")]
    [Tooltip("반응/행동이 끝난 뒤 대기 상태로 돌아가기까지의 짧은 대기 시간(초)")]
    [SerializeField] private float recoveryDelay = 0.25f;

    [Header("Reaction Weights (플레이어 공격 감지 → 반응 선택)")]
    [SerializeField, Min(0f)] private float reactCounterAttackWeight = 0.4f;
    [SerializeField, Min(0f)] private float reactStayNeutralWeight   = 0.3f;
    [SerializeField, Min(0f)] private float reactDodgeWeight         = 0.3f;

    [Header("Acting Weights (AI 선공 → 행동 선택)")]
    [SerializeField, Min(0f)] private float actAttackWeight     = 0.6f;
    [SerializeField, Min(0f)] private float actFakeAttackWeight = 0.4f;
    // 페인트 모션 타이밍은 CombatMotionController의 "Feint Attack" 헤더에서 조절

    [Header("Attack Telegraph")]
    [SerializeField] private Vector2 attackTelegraphDelayRange = new Vector2(0.5f, 1.0f);
    [SerializeField, Range(0f, 1f)] private float attackTelegraphDamageMultiplier = 0.5f;
    [SerializeField, HideInInspector] private SpriteFillController attackTelegraphFill;
    [SerializeField] private SpriteFillController[] attackTelegraphFills;

    // ── Runtime State ──────────────────────────────────────────────────────────

    private CombatActorController actorController;
    private EnemyAIState currentAiState = EnemyAIState.Idle;
    private Coroutine aiStateRoutine;
    private float actionCooldownTimer;
    private bool playerWasAttacking;
    private bool wasRoundCombatActive;
    private bool isAttackTelegraphActive;

    // ── Public API ─────────────────────────────────────────────────────────────

    public CombatActorController ActorController
    {
        get
        {
            EnsureActorController(true);
            return actorController;
        }
    }

    public CombatActorController TargetController => targetController;
    public EnemyAIState CurrentAIState => currentAiState;
    public bool IsAttackTelegraphActive => isAttackTelegraphActive;
    public float IncomingDamageMultiplier => isAttackTelegraphActive ? attackTelegraphDamageMultiplier : 1f;

    public bool TryPush()         => actorController != null && actorController.TryPush();
    public bool TryDodge()        => actorController != null && actorController.TryDodge();
    public bool TryBalanceDebug() => actorController != null && actorController.TryBalanceDebug();

    // ── Unity Lifecycle ────────────────────────────────────────────────────────

    void Reset()
    {
        EnsureActorController(true);
        TryAutoAssignAiPauseSources();
        ApplySetup();
    }

    void Awake()
    {
        EnsureActorController(true);
        TryAutoAssignAiPauseSources();
        ApplySetup();
        SetAttackTelegraphFill(0f);
    }

    void OnEnable()
    {
        TryAutoAssignAiPauseSources();
        ResetActionCooldown();
        wasRoundCombatActive = actorController != null && actorController.RoundCombatActive;
    }

    void OnDisable()
    {
        if (aiStateRoutine != null)
        {
            StopCoroutine(aiStateRoutine);
            aiStateRoutine = null;
        }

        SetAttackTelegraphActive(false);
        SetAttackTelegraphFill(0f);
    }

    void OnValidate()
    {
        EnsureActorController(false);
        ApplySetup();
        actionCooldownMin = Mathf.Max(0.1f, actionCooldownMin);
        actionCooldownMax = Mathf.Max(actionCooldownMin, actionCooldownMax);
        recoveryDelay     = Mathf.Max(0f, recoveryDelay);
        attackTelegraphDelayRange.x = Mathf.Max(0f, attackTelegraphDelayRange.x);
        attackTelegraphDelayRange.y = Mathf.Max(attackTelegraphDelayRange.x, attackTelegraphDelayRange.y);
        attackTelegraphDamageMultiplier = Mathf.Clamp01(attackTelegraphDamageMultiplier);
    }

    void Update()
    {
        HandleDebugInput();

        bool isRoundCombatActive = actorController != null && actorController.RoundCombatActive;
        if (isRoundCombatActive && !wasRoundCombatActive)
            HandleCombatResumed();

        wasRoundCombatActive = isRoundCombatActive;

        if (!enableAi || actorController == null || !isRoundCombatActive)
            return;

        if (IsAiPausedByMinigame())
        {
            playerWasAttacking = IsPlayerAttacking();
            return;
        }

        // 쿨타임은 비-Idle 상태에서도 계속 진행
        // → 반응이 끝나고 Idle로 돌아왔을 때 바로 행동 상태로 진입 가능
        TickActionCooldown();

        if (currentAiState == EnemyAIState.Idle)
            UpdateIdleState();
    }

    // ── Idle State ─────────────────────────────────────────────────────────────

    void UpdateIdleState()
    {
        // 플레이어 공격 시작(상승 에지) 감지
        bool playerIsAttacking  = IsPlayerAttacking();
        bool playerJustAttacked = !playerWasAttacking && playerIsAttacking;
        playerWasAttacking = playerIsAttacking;

        // 우선순위 1: 플레이어 공격 감지 → 반응
        if (playerJustAttacked && actorController.CanAttemptDecision)
        {
            EnterReacting();
            return;
        }

        // 우선순위 2: 행동 쿨타임 만료 → AI 선공
        if (actionCooldownTimer <= 0f && actorController.CanAttemptDecision)
        {
            EnterActing();
        }
    }

    // ── State: Reacting ────────────────────────────────────────────────────────

    void EnterReacting()
    {
        SetState(EnemyAIState.Reacting);
        StartAiStateRoutine(RunReacting());
    }

    IEnumerator RunReacting()
    {
        EnemyReactionChoice reaction = RollReaction();
        LogAI("반응", ReactionLabel(reaction), ReactionToCombatState(reaction));
        yield return ExecuteReaction(reaction);

        yield return WaitForMotionEnd();
        yield return WaitWhileAiPaused();

        if (!IsAiActive())
        {
            AbortAiStateRoutine();
            yield break;
        }

        yield return WaitForSecondsWithAiPause(recoveryDelay);
        ReturnToIdle();
        aiStateRoutine = null;
    }

    IEnumerator ExecuteReaction(EnemyReactionChoice reaction)
    {
        switch (reaction)
        {
            case EnemyReactionChoice.CounterAttack:
                actorController.TryPush();
                break;
            case EnemyReactionChoice.Dodge:
                actorController.TryDodge();
                break;
            case EnemyReactionChoice.StayNeutral:
                actorController.TryStayNeutral();
                break;
        }

        yield break;
    }

    // ── State: Acting ──────────────────────────────────────────────────────────

    void EnterActing()
    {
        SetState(EnemyAIState.Acting);
        ResetActionCooldown();
        StartAiStateRoutine(RunActing());
    }

    IEnumerator RunActing()
    {
        yield return WaitWhileAiPaused();

        if (!IsAiActive())
        {
            AbortAiStateRoutine();
            yield break;
        }

        LogAI("행동 예고", "Telegraph", CombatState.Attack);
        yield return RunAttackTelegraph();
        yield return WaitWhileAiPaused();

        if (!IsAiActive())
        {
            AbortAiStateRoutine();
            yield break;
        }

        EnemyActingChoice action = RollAction();
        LogAI("행동", ActionLabel(action), ActionToCombatState(action));

        ExecuteActing(action);

        yield return WaitForMotionEnd();
        yield return WaitWhileAiPaused();

        if (!IsAiActive())
        {
            AbortAiStateRoutine();
            yield break;
        }

        yield return WaitForSecondsWithAiPause(recoveryDelay);
        ReturnToIdle();
        aiStateRoutine = null;
    }

    void ExecuteActing(EnemyActingChoice action)
    {
        if (action == EnemyActingChoice.FakeAttack)
        {
            actorController.TryFeintAttack(); // 뻗기→회수 독립 페인트 모션
            return;
        }

        actorController.TryPush();
    }

    IEnumerator RunAttackTelegraph()
    {
        float duration = Random.Range(attackTelegraphDelayRange.x, attackTelegraphDelayRange.y);
        if (duration <= 0f)
        {
            SetAttackTelegraphFill(0f);
            yield break;
        }

        SetAttackTelegraphActive(true);
        SetAttackTelegraphFill(0f);

        float elapsed = 0f;
        while (elapsed < duration && IsAiActive())
        {
            if (IsAiPausedByMinigame())
            {
                yield return WaitWhileAiPaused();
                continue;
            }

            elapsed += Time.deltaTime;
            SetAttackTelegraphFill(Mathf.Clamp01(elapsed / duration));
            yield return null;
        }

        SetAttackTelegraphActive(false);
        SetAttackTelegraphFill(0f);
    }

    void SetAttackTelegraphActive(bool active)
    {
        isAttackTelegraphActive = active;
    }

    void SetAttackTelegraphFill(float fill)
    {
        if (attackTelegraphFill != null)
            attackTelegraphFill.SetFill(fill);

        if (attackTelegraphFills == null)
            return;

        for (int i = 0; i < attackTelegraphFills.Length; i++)
        {
            if (attackTelegraphFills[i] != null)
                attackTelegraphFills[i].SetFill(fill);
        }
    }

    // ── Shared Helpers ─────────────────────────────────────────────────────────

    /// <summary>현재 모션이 완전히 끝나 다음 행동이 가능해질 때까지 대기</summary>
    IEnumerator WaitForMotionEnd()
    {
        yield return new WaitUntil(() =>
            actorController == null ||
            !actorController.RoundCombatActive ||
            (!IsAiPausedByMinigame() && actorController.CanAttemptDecision));
    }

    IEnumerator WaitWhileAiPaused()
    {
        while (IsAiActive() && IsAiPausedByMinigame())
            yield return null;
    }

    IEnumerator WaitForSecondsWithAiPause(float duration)
    {
        float elapsed = 0f;
        duration = Mathf.Max(0f, duration);

        while (elapsed < duration && IsAiActive())
        {
            if (!IsAiPausedByMinigame())
                elapsed += Time.deltaTime;

            yield return null;
        }
    }

    void ReturnToIdle()
    {
        SetState(EnemyAIState.Idle);
    }

    void HandleCombatResumed()
    {
        if (currentAiState != EnemyAIState.Idle)
            ReturnToIdle();

        playerWasAttacking = IsPlayerAttacking();
        ResetActionCooldown();
    }

    void StartAiStateRoutine(IEnumerator routine)
    {
        if (aiStateRoutine != null)
        {
            StopCoroutine(aiStateRoutine);
            SetAttackTelegraphActive(false);
            SetAttackTelegraphFill(0f);
        }

        aiStateRoutine = StartCoroutine(routine);
    }

    void AbortAiStateRoutine()
    {
        SetAttackTelegraphActive(false);
        SetAttackTelegraphFill(0f);
        ReturnToIdle();
        aiStateRoutine = null;
    }

    void SetState(EnemyAIState newState)
    {
        if (currentAiState == newState)
            return;

        if (enableAiDebugLogs)
            Debug.Log($"[AI] 상태: {currentAiState} → {newState}", this);

        currentAiState = newState;
    }

    void TickActionCooldown()
    {
        if (actionCooldownTimer > 0f)
            actionCooldownTimer -= Time.deltaTime;
    }

    bool IsPlayerAttacking() =>
        targetController != null &&
        targetController.CurrentCombatState == CombatState.Attack;

    bool IsAiPausedByMinigame()
    {
        if (!pauseAiDuringMinigames)
            return false;

        TryAutoAssignAiPauseSources();

        return (battleUiController != null && battleUiController.IsPlayerQteActive)
            || (clashTugMinigameController != null && clashTugMinigameController.IsActive);
    }

    bool IsAiActive() =>
        enableAi && actorController != null && actorController.RoundCombatActive;

    void ResetActionCooldown() =>
        actionCooldownTimer = Random.Range(actionCooldownMin, actionCooldownMax);

    void TryAutoAssignAiPauseSources()
    {
        if (battleUiController == null)
            battleUiController = FindFirstObjectByType<BattleUiController>();

        if (clashTugMinigameController == null)
            clashTugMinigameController = FindFirstObjectByType<ClashTugMinigameController>();
    }

    // ── Probability Rolls ──────────────────────────────────────────────────────

    EnemyReactionChoice RollReaction()
    {
        float total = reactCounterAttackWeight + reactStayNeutralWeight + reactDodgeWeight;
        if (total <= 0f)
            return EnemyReactionChoice.StayNeutral;

        float roll = Random.value * total;

        if (roll < reactCounterAttackWeight) return EnemyReactionChoice.CounterAttack;
        roll -= reactCounterAttackWeight;
        if (roll < reactStayNeutralWeight)   return EnemyReactionChoice.StayNeutral;
        return EnemyReactionChoice.Dodge;
    }

    EnemyActingChoice RollAction()
    {
        float total = actAttackWeight + actFakeAttackWeight;
        if (total <= 0f)
            return EnemyActingChoice.Attack;

        return Random.value * total < actAttackWeight
            ? EnemyActingChoice.Attack
            : EnemyActingChoice.FakeAttack;
    }

    // ── Debug Logging ──────────────────────────────────────────────────────────

    void LogAI(string phase, string choiceLabel, CombatState actorState)
    {
        if (!enableAiDebugLogs || actorController == null)
            return;

        CombatState opponentState = targetController != null
            ? targetController.ResolutionCombatState
            : CombatState.Neutral;

        string lane = actorController.BuildCombatLane(targetController, actorState, opponentState);
        Debug.Log($"[AI] {lane}  {phase}:{choiceLabel}", this);
    }

    static string ReactionLabel(EnemyReactionChoice r) => r switch
    {
        EnemyReactionChoice.CounterAttack => "받아치기",
        EnemyReactionChoice.Dodge         => "회피",
        _                                 => "중립 유지",
    };

    static string ActionLabel(EnemyActingChoice a) => a switch
    {
        EnemyActingChoice.FakeAttack => "페이크",
        _                            => "공격",
    };

    static CombatState ReactionToCombatState(EnemyReactionChoice r) => r switch
    {
        EnemyReactionChoice.CounterAttack => CombatState.Attack,
        EnemyReactionChoice.Dodge         => CombatState.Dodge,
        _                                 => CombatState.Neutral,
    };

    static CombatState ActionToCombatState(EnemyActingChoice a) =>
        CombatState.Attack; // Attack과 FakeAttack 모두 공격 모션으로 시작

    // ── Debug Input ────────────────────────────────────────────────────────────

    void HandleDebugInput()
    {
        if (!enableDebugInput || actorController == null)
            return;

        if (Input.GetKeyDown(pushKey))    actorController.TryPush();
        if (Input.GetKeyDown(dodgeKey))   actorController.TryDodge();
        if (Input.GetKeyDown(balanceKey)) actorController.TryBalanceDebug();
    }

    // ── Setup Infrastructure ───────────────────────────────────────────────────

    void ApplySetup()
    {
        EnsureActorController(false);

        if (actorController == null)
            return;

        if (targetController == null)
            targetController = FindPlayerTarget();

        actorController.SetPushDirection(enemyPushDirection);
        actorController.SetCombatPresentation(CombatActorSide.Enemy, "적", "#FF5C5C");
        actorController.SetOpponent(targetController);

        if (targetController != null)
        {
            targetController.SetCombatPresentation(CombatActorSide.Player, "플레이어", "#4AA3FF");

            if (targetController.OpponentController == null)
                targetController.SetOpponent(actorController);
        }
    }

    void EnsureActorController(bool addIfMissing)
    {
        if (actorController != null || TryGetComponent(out actorController) || !addIfMissing)
            return;

        actorController = gameObject.AddComponent<CombatActorController>();
    }

    CombatActorController FindPlayerTarget()
    {
        CombatActorController[] actors =
            FindObjectsByType<CombatActorController>(FindObjectsSortMode.None);

        for (int i = 0; i < actors.Length; i++)
        {
            CombatActorController actor = actors[i];
            if (actor != null
                && actor != actorController
                && actor.ActorSide == CombatActorSide.Player
                && !actor.TryGetComponent<EnemyController>(out _))
                return actor;
        }

        return null;
    }
}
