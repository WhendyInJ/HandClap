using System;
using UnityEngine;

[RequireComponent(typeof(CombatActorStats))]
[RequireComponent(typeof(CombatHealth))]
[RequireComponent(typeof(CombatMotionController))]
public class CombatActorController : MonoBehaviour
{
    public event Action<CombatEventData> CombatEventRaised;

    [Header("Combat Debug")]
    [SerializeField] private string actorName;
    [SerializeField] private CombatActorSide actorSide = CombatActorSide.Player;
    [SerializeField] private string actorColorHex = "#4AA3FF";
    [SerializeField] private CombatActorController opponentController;
    [SerializeField] private bool enableCombatDebugLogs = true;

    [Header("Combat Components")]
    [SerializeField] private CombatActorStats stats;
    [SerializeField] private CombatHealth health;
    [SerializeField] private CombatMotionController motion;

    private bool roundCombatActive = true;
    private bool betweenRoundIdleMotion;
    private bool hasQueuedDecision;
    private CombatState queuedDecisionState;
    private bool dodgeSucceeded;

    void Reset()
    {
        CacheComponents();
    }

    void Awake()
    {
        CacheComponents();
    }

    void Start()
    {
        EnsureDefaultPlayerInput();
    }

    void LateUpdate()
    {
        if (!hasQueuedDecision)
            return;

        ResolveQueuedDecision();
    }

    public bool TryPush()
    {
        return QueueDecision(CombatState.Attack);
    }

    public bool TryDodge()
    {
        return QueueDecision(CombatState.Dodge);
    }

    public bool TryBalanceDebug()
    {
        return roundCombatActive && Motion != null && Motion.TryPlayBalanceDebug();
    }

    public bool TryStayNeutral()
    {
        return QueueDecision(CombatState.Neutral);
    }

    public void SetPushDirection(Vector2 direction)
    {
        if (Motion != null)
            Motion.SetPushDirection(direction);
    }

    public void SetOpponent(CombatActorController opponent)
    {
        opponentController = opponent;
    }

    public void SetRoundCombatActive(bool active)
    {
        if (!active && hasQueuedDecision)
            ResolveQueuedDecision();

        roundCombatActive = active;

        if (active)
            betweenRoundIdleMotion = false;

        if (!active && hasQueuedDecision)
            hasQueuedDecision = false;
    }

    public void SetBetweenRoundIdleMotion(bool enabled)
    {
        betweenRoundIdleMotion = enabled;
    }

    public bool BlocksRoundTransition()
    {
        if (hasQueuedDecision)
            return true;

        if (Motion == null)
            return false;

        return Motion.IsPushing || Motion.IsDodging || Motion.IsBalancing;
    }

    public void ApplyBuild(PlayerBuild newBuild)
    {
        if (Stats != null)
            Stats.ApplyBuild(newBuild);
    }

    public void SetBuild(Element element, BodyType bodyType, HandSize handSize)
    {
        if (Stats != null)
            Stats.SetBuild(element, bodyType, handSize);
    }

    public void SetCombatPresentation(CombatActorSide side, string label, string colorHex)
    {
        actorSide = side;
        actorName = label;
        actorColorHex = colorHex;

        if (side == CombatActorSide.Enemy && TryGetComponent(out PlayerInputController inputController))
            inputController.SetInputEnabled(false);
    }

    public CombatState CurrentCombatState =>
        Motion != null && Motion.IsPushing ? CombatState.Attack :
        Motion != null && Motion.IsDodging ? CombatState.Dodge :
        CombatState.Neutral;

    public CombatState ResolutionCombatState => hasQueuedDecision ? queuedDecisionState : CurrentCombatState;
    public bool IsAttackClashWindowActive => Motion != null && Motion.IsAttackClashWindowActive;
    public bool IsDodgeWindowActive => Motion != null && Motion.IsDodging;
    public bool CanAttemptDecision => roundCombatActive && !hasQueuedDecision && Motion != null && Motion.CanStartMotion;

    public CombatActorSide ActorSide => actorSide;
    public bool RoundCombatActive => roundCombatActive;
    public bool AllowsIdleMotionWhileInactive => betweenRoundIdleMotion;
    public CombatActorController OpponentController => opponentController;
    public CombatActorStats Stats
    {
        get
        {
            if (stats == null)
                TryGetComponent(out stats);

            return stats;
        }
    }
    public CombatHealth Health
    {
        get
        {
            if (health == null)
                TryGetComponent(out health);

            return health;
        }
    }
    public CombatMotionController Motion
    {
        get
        {
            if (motion == null)
                TryGetComponent(out motion);

            return motion;
        }
    }
    public PlayerBuild Build => Stats != null ? Stats.Build : PlayerBuild.Default;
    public Element Element => Build.Element;
    public BodyType BodyType => Build.BodyType;
    public HandSize HandSize => Build.HandSize;
    public float AttackDamageMultiplier => Stats != null ? Stats.AttackDamageMultiplier : 1f;
    public float AttackCooldownMultiplier => Stats != null ? Stats.AttackCooldownMultiplier : 1f;
    public float ReceivedDamageMultiplier => Stats != null ? Stats.ReceivedDamageMultiplier : 1f;
    public float StaggerDifficultyMultiplier => Stats != null ? Stats.StaggerDifficultyMultiplier : 1f;

    string CombatLabel => string.IsNullOrWhiteSpace(actorName)
        ? actorSide == CombatActorSide.Player ? "\uD50C\uB808\uC774\uC5B4" : "\uC801"
        : actorName;
    string CombatColorHex => string.IsNullOrWhiteSpace(actorColorHex)
        ? actorSide == CombatActorSide.Player ? "#4AA3FF" : "#FF5C5C"
        : actorColorHex;

    void CacheComponents()
    {
        if (stats == null && !TryGetComponent(out stats))
            stats = gameObject.AddComponent<CombatActorStats>();

        if (health == null && !TryGetComponent(out health))
            health = gameObject.AddComponent<CombatHealth>();

        if (motion == null && !TryGetComponent(out motion))
            motion = gameObject.AddComponent<CombatMotionController>();
    }

    void EnsureDefaultPlayerInput()
    {
        if (actorSide != CombatActorSide.Player || TryGetComponent<EnemyController>(out _))
            return;

        if (!TryGetComponent<PlayerInputController>(out _))
            gameObject.AddComponent<PlayerInputController>();
    }

    bool QueueDecision(CombatState state)
    {
        if (!CanAttemptDecision)
            return false;

        hasQueuedDecision = true;
        queuedDecisionState = state;
        return true;
    }

    void ResolveQueuedDecision()
    {
        CombatState attemptedState = queuedDecisionState;
        hasQueuedDecision = false;

        switch (attemptedState)
        {
            case CombatState.Attack:
                Motion?.TryPlayAttack(ResolveAttackOutcome, GetAttackCooldownDuration());
                break;

            case CombatState.Dodge:
                dodgeSucceeded = false;
                Motion?.TryPlayDodge(HandleDodgeMotionFinished, GetDodgeCooldownDuration());
                break;
        }
    }

    void ResolveAttackOutcome()
    {
        if (Motion == null || Motion.IsAttackResolutionComplete)
            return;

        if (opponentController != null && TryResolveAttackClash(opponentController))
            return;

        Motion.MarkAttackResolutionComplete();

        if (opponentController != null && opponentController.IsDodgeWindowActive)
        {
            string summary = CombatRuleResolver.Resolve(CombatState.Attack, CombatState.Dodge).Summary;
            LogCombatOutcome(CombatState.Attack, CombatState.Dodge, summary);
            RaiseCombatEvent(
                CombatEventKind.AttackDodged,
                opponentController,
                CombatState.Attack,
                CombatState.Dodge,
                summary);
            opponentController.RegisterDodgeSuccess();
            return;
        }

        CombatState opponentState = opponentController != null
            ? opponentController.ResolutionCombatState
            : CombatState.Neutral;

        const string hitSummary = "\uACF5\uACA9 \uC801\uC911";
        LogCombatOutcome(CombatState.Attack, opponentState, hitSummary);
        RaiseCombatEvent(
            CombatEventKind.AttackHit,
            opponentController,
            CombatState.Attack,
            opponentState,
            hitSummary);
    }

    bool TryResolveAttackClash(CombatActorController opponent)
    {
        if (Motion == null || opponent == null || opponent.Motion == null)
            return false;

        if (!Motion.IsAttackClashWindowActive || !opponent.Motion.IsAttackClashWindowActive)
            return false;

        Motion.MarkAttackClashed();
        opponent.Motion.MarkAttackClashed();

        string clashSummary = CombatRuleResolver.Resolve(CombatState.Attack, CombatState.Attack).Summary;
        LogCombatOutcome(CombatState.Attack, CombatState.Attack, clashSummary);
        opponent.LogCombatOutcome(CombatState.Attack, CombatState.Attack, clashSummary);
        RaiseCombatEvent(
            CombatEventKind.AttackClashed,
            opponent,
            CombatState.Attack,
            CombatState.Attack,
            clashSummary);
        opponent.RaiseCombatEvent(
            CombatEventKind.AttackClashed,
            this,
            CombatState.Attack,
            CombatState.Attack,
            clashSummary);
        return true;
    }

    void RegisterDodgeSuccess()
    {
        if (Motion == null || !Motion.IsDodging || dodgeSucceeded)
            return;

        dodgeSucceeded = true;
        string summary = CombatRuleResolver.Resolve(CombatState.Dodge, CombatState.Attack).Summary;
        LogCombatOutcome(CombatState.Dodge, CombatState.Attack, summary);
        RaiseCombatEvent(
            CombatEventKind.DodgeSucceeded,
            opponentController,
            CombatState.Dodge,
            CombatState.Attack,
            summary);
    }

    void HandleDodgeMotionFinished()
    {
        if (dodgeSucceeded)
            return;

        CombatState opponentState = opponentController != null
            ? opponentController.ResolutionCombatState
            : CombatState.Neutral;
        const string failSummary = "\uD68C\uD53C \uC2E4\uD328";

        LogCombatOutcome(CombatState.Dodge, opponentState, failSummary);
        RaiseCombatEvent(
            CombatEventKind.DodgeFailed,
            opponentController,
            CombatState.Dodge,
            opponentState,
            failSummary);
    }

    void LogCombatOutcome(CombatState actorState, CombatState opponentState, string summary)
    {
        if (!enableCombatDebugLogs)
            return;

        string lane = BuildCombatLane(opponentController, actorState, opponentState);

        Debug.Log($"[\uC804\uD22C] {lane}  \uACB0\uACFC:{summary}", this);
    }

    void RaiseCombatEvent(
        CombatEventKind kind,
        CombatActorController opponent,
        CombatState actorState,
        CombatState opponentState,
        string summary)
    {
        CombatEventRaised?.Invoke(new CombatEventData(
            kind,
            this,
            opponent,
            actorState,
            opponentState,
            summary));
    }

    public string BuildCombatLane(
        CombatActorController opponent,
        CombatState actorState,
        CombatState opponentState)
    {
        CombatActorController player = actorSide == CombatActorSide.Player ? this : opponent;
        CombatActorController enemy = actorSide == CombatActorSide.Enemy ? this : opponent;

        string playerLabel = player != null
            ? player.GetColoredCombatLabel(player == this ? actorState : opponentState)
            : FormatColoredLabel(
                "\uD50C\uB808\uC774\uC5B4",
                CombatRuleResolver.ToDisplayName(CombatState.Neutral),
                "#4AA3FF");
        string enemyLabel = enemy != null
            ? enemy.GetColoredCombatLabel(enemy == this ? actorState : opponentState)
            : FormatColoredLabel(
                "\uC801",
                CombatRuleResolver.ToDisplayName(CombatState.Neutral),
                "#FF5C5C");

        string arrow = actorSide == CombatActorSide.Player ? ">" : "<";
        return $"{playerLabel} {arrow} {enemyLabel}";
    }

    public string GetColoredCombatLabel(CombatState state)
    {
        return FormatColoredLabel(CombatLabel, CombatRuleResolver.ToDisplayName(state), CombatColorHex);
    }

    float GetAttackCooldownDuration()
    {
        float baseCooldown = Motion != null ? Motion.BaseCooldown : 0f;
        return Mathf.Max(0f, baseCooldown * AttackCooldownMultiplier);
    }

    float GetDodgeCooldownDuration()
    {
        return Motion != null ? Motion.BaseCooldown : 0f;
    }

    static string FormatColoredLabel(string label, string state, string colorHex)
    {
        return $"<color={colorHex}><b>{label}[{state}]</b></color>";
    }
}
