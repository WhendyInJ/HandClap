using System;
using System.Collections;
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

    [Header("Attack Meeting")]
    [Tooltip("두 손의 뻗은 비율 차이가 이 값 이하이면 중간지점 충돌(AttackClashed)로 판정. 예: 0.15 = ±15%")]
    [SerializeField, Range(0f, 0.5f)] private float midpointClashTolerance = 0.15f;
    [Tooltip("공격 만남을 감지하는 손바닥 간 최대 거리(월드 단위)")]
    [SerializeField, Min(0f)] private float handMeetingRadius = 0.5f;

    [Header("Combat Components")]
    [SerializeField] private CombatActorStats stats;
    [SerializeField] private CombatHealth health;
    [SerializeField] private CombatMotionController motion;

    private bool roundCombatActive = true;
    private bool betweenRoundIdleMotion;
    private bool hasQueuedDecision;
    private CombatState queuedDecisionState;
    private bool dodgeSucceeded;
    private bool feintWasPunished;
    private bool feintBeatDodge;
    private bool feintBeatAttack;
    private bool feintAttackCounterResolved;
    private bool hasInitialAttackMeetingFrame;
    private Vector3 initialAttackMeetingMidpoint;
    private Vector3 initialAttackMeetingPlayerToEnemyAxis = Vector3.right;
    private float initialAttackMeetingHalfDistance = 1f;

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
        CaptureInitialAttackMeetingFrame(false);
    }

    void LateUpdate()
    {
        if (hasQueuedDecision)
            ResolveQueuedDecision();

        TryEvaluateHandMeeting();
    }

    public bool TryPush()
    {
        if (TryFakeAttack())
            return true;

        return QueueDecision(CombatState.Attack);
    }

    public bool TryFakeAttack()
    {
        if (!roundCombatActive || Motion == null || !Motion.TryPlayFakeAttack(ResolveFeintOutcome))
            return false;

        hasQueuedDecision = false;
        feintWasPunished = false;
        feintBeatDodge = IsOpponentDodgeCommitted();
        feintBeatAttack = IsOpponentAttackCommitted();
        feintAttackCounterResolved = false;

        CombatState opponentState = opponentController != null
            ? opponentController.ResolutionCombatState
            : CombatState.Neutral;
        const string summary = "페이크 공격";

        LogCombatOutcome(CombatState.Attack, opponentState, summary);
        RaiseCombatEvent(
            CombatEventKind.AttackFeinted,
            opponentController,
            CombatState.Attack,
            opponentState,
            summary);
        return true;
    }

    /// <summary>
    /// 팔을 뻗었다가 회수하는 페인트 모션을 재생한다 (AI 선공 페이크 전용).
    /// DoPush 취소 방식이 아닌 독립 모션이므로 뻗기/회수가 명확하게 보인다.
    /// </summary>
    public bool TryFeintAttack()
    {
        if (!roundCombatActive || Motion == null || !Motion.TryPlayFeintAttack(GetAttackCooldownDuration(), ResolveFeintOutcome))
            return false;

        feintWasPunished = false;
        feintBeatDodge = IsOpponentDodgeCommitted();
        feintBeatAttack = IsOpponentAttackCommitted();
        feintAttackCounterResolved = false;

        CombatState opponentState = opponentController != null
            ? opponentController.ResolutionCombatState
            : CombatState.Neutral;
        const string summary = "페인트 공격";

        LogCombatOutcome(CombatState.Attack, opponentState, summary);
        RaiseCombatEvent(
            CombatEventKind.AttackFeinted,
            opponentController,
            CombatState.Attack,
            opponentState,
            summary);
        return true;
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

    /// <summary>
    /// 즉시 휘청거림 시작. CanStartMotion이 아니면 false 반환.
    /// duration &lt;= 0 이면 StopStagger() 호출 시까지 무한 유지.
    /// </summary>
    public bool TryPlayStagger(float duration, bool leanForward = false, Action finishedCallback = null)
        => Motion != null && Motion.TryPlayStagger(duration, leanForward, finishedCallback);

    public bool TryPlayHitReact(bool leanForward = false)
        => Motion != null && Motion.TryPlayHitReact(leanForward);

    /// <summary>
    /// 현재 모션이 끝나는 즉시 휘청거림 시작 (페이크 응징 등 모션 중 요청 시 사용).
    /// </summary>
    public void RequestStagger(float duration, bool leanForward = false, Action finishedCallback = null)
    {
        if (Motion == null)
            return;

        if (Motion.TryPlayStagger(duration, leanForward, finishedCallback))
            return;

        StartCoroutine(WaitAndStagger(duration, leanForward, finishedCallback));
    }

    /// <summary>무한 스태거 종료. 복귀 애니메이션은 자동 재생.</summary>
    public void StopStagger() => Motion?.StopStagger();

    public void SetPushDirection(Vector2 direction)
    {
        if (Motion != null)
            Motion.SetPushDirection(direction);
    }

    public void SetOpponent(CombatActorController opponent)
    {
        opponentController = opponent;
        CaptureInitialAttackMeetingFrame(true);
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

    public bool IsStaggered => Motion != null && Motion.IsStaggered;

    public CombatState CurrentCombatState =>
        Motion != null && Motion.IsPushing && !Motion.IsAttackFeinted ? CombatState.Attack :
        Motion != null && Motion.IsDodging ? CombatState.Dodge :
        CombatState.Neutral;

    public CombatState ResolutionCombatState => hasQueuedDecision ? queuedDecisionState : CurrentCombatState;
    public bool IsAttackClashWindowActive => Motion != null && Motion.IsAttackClashWindowActive;
    public bool CanFakeAttack => roundCombatActive && Motion != null && Motion.CanFakeAttack;
    bool IsAttackCommitted => hasQueuedDecision && queuedDecisionState == CombatState.Attack || CurrentCombatState == CombatState.Attack;
    public bool IsDodgeWindowActive => Motion != null && Motion.IsDodging;
    bool IsDodgeCommitted => hasQueuedDecision && queuedDecisionState == CombatState.Dodge || IsDodgeWindowActive;
    public bool IsHandContactActive => Motion != null && Motion.IsHandContactActive;
    public float HandExtensionNormalized => Motion != null ? Motion.HandExtensionNormalized : 0f;
    public CombatHandExtensionPhase HandExtensionPhase => Motion != null
        ? Motion.HandExtensionPhase
        : CombatHandExtensionPhase.None;
    public Transform PalmContactPoint => Motion != null ? Motion.PalmContactPoint : transform;
    public Vector3 PalmContactWorldPosition => Motion != null ? Motion.PalmContactWorldPosition : transform.position;
    /// <summary>
    /// 현재 페인트(페이크/페인트 모션) 중인지 여부.
    /// 플레이어 페이크 취소(PlayFakeAttackRecovery)와 AI 페인트 모션(DoFeintAttack) 모두 해당.
    /// </summary>
    public bool IsFeinting => Motion != null && Motion.IsPushing && Motion.IsAttackFeinted;
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

    IEnumerator WaitAndStagger(float duration, bool leanForward, Action finishedCallback)
    {
        // 현재 모션(push/dodge/balance)이 끝날 때까지 대기 (cooldown은 무시)
        yield return new WaitUntil(() =>
            Motion == null ||
            !roundCombatActive ||
            (!Motion.IsPushing && !Motion.IsDodging && !Motion.IsBalancing && !Motion.IsStaggered));

        if (roundCombatActive && Motion != null)
            Motion.TryPlayStagger(duration, leanForward, finishedCallback);
    }

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

    void CaptureInitialAttackMeetingFrame(bool force)
    {
        if (!force && hasInitialAttackMeetingFrame)
            return;

        if (opponentController == null)
            return;

        CombatActorController player = GetPlayerActor(opponentController);
        CombatActorController enemy = GetEnemyActor(opponentController);

        Vector3 playerPosition = player != null ? player.transform.position : transform.position;
        Vector3 enemyPosition = enemy != null ? enemy.transform.position : opponentController.transform.position;
        Vector3 playerToEnemy = enemyPosition - playerPosition;
        playerToEnemy.z = 0f;

        float distance = playerToEnemy.magnitude;
        if (distance <= 0.0001f)
        {
            playerToEnemy = Vector3.right;
            distance = 2f;
        }

        Vector3 midpoint = (playerPosition + enemyPosition) * 0.5f;
        Vector3 axis = playerToEnemy.normalized;
        float halfDistance = Mathf.Max(0.0001f, distance * 0.5f);

        ApplyInitialAttackMeetingFrame(midpoint, axis, halfDistance);

        if (opponentController != null)
            opponentController.ApplyInitialAttackMeetingFrame(midpoint, axis, halfDistance);
    }

    void ApplyInitialAttackMeetingFrame(Vector3 midpoint, Vector3 playerToEnemyAxis, float halfDistance)
    {
        initialAttackMeetingMidpoint = midpoint;
        initialAttackMeetingPlayerToEnemyAxis = playerToEnemyAxis.sqrMagnitude > 0.0001f
            ? playerToEnemyAxis.normalized
            : Vector3.right;
        initialAttackMeetingHalfDistance = Mathf.Max(0.0001f, halfDistance);
        hasInitialAttackMeetingFrame = true;
    }

    void EnsureInitialAttackMeetingFrame(CombatActorController opponent)
    {
        if (opponentController == null && opponent != null)
            opponentController = opponent;

        CaptureInitialAttackMeetingFrame(false);
    }

    bool QueueDecision(CombatState state)
    {
        if (!CanAttemptDecision)
            return false;

        hasQueuedDecision = true;
        queuedDecisionState = state;

        if (state == CombatState.Dodge)
            NotifyOpponentFeintOfDodge();
        else if (state == CombatState.Attack)
            NotifyOpponentFeintOfAttack();

        return true;
    }

    void ResolveQueuedDecision()
    {
        CombatState attemptedState = queuedDecisionState;
        hasQueuedDecision = false;

        switch (attemptedState)
        {
            case CombatState.Attack:
                if (Motion != null && Motion.TryPlayAttack(ResolveAttackOutcome, GetAttackCooldownDuration()))
                    NotifyOpponentFeintOfAttack();
                break;

            case CombatState.Dodge:
                dodgeSucceeded = false;
                if (Motion != null && Motion.TryPlayDodge(HandleDodgeMotionFinished, GetDodgeCooldownDuration()))
                    NotifyOpponentFeintOfDodge();
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

        if (opponentController != null && opponentController.TryResolveFeintCounteredAttack(this))
            return;

        CombatState opponentState = opponentController != null
            ? opponentController.ResolutionCombatState
            : CombatState.Neutral;

        const string hitSummary = "공격 적중";
        LogCombatOutcome(CombatState.Attack, opponentState, hitSummary);
        RaiseCombatEvent(
            CombatEventKind.AttackHit,
            opponentController,
            CombatState.Attack,
            opponentState,
            hitSummary);
    }

    /// <summary>
    /// LateUpdate에서 매 프레임 호출. Player 측만 평가해 이중 발화를 막는다.
    /// 두 손바닥이 handMeetingRadius 안에 들어오면 뻗기 비율로 충돌 유형을 판정한다.
    /// </summary>
    void TryEvaluateHandMeeting()
    {
        if (actorSide != CombatActorSide.Player)
            return;

        if (!roundCombatActive || Motion == null || opponentController == null)
            return;

        if (!Motion.IsAttackClashWindowActive || !opponentController.Motion.IsAttackClashWindowActive)
            return;

        float dist = Vector3.Distance(PalmContactWorldPosition, opponentController.PalmContactWorldPosition);
        if (dist > handMeetingRadius)
            return;

        ResolveAttackMeetingByExtension(opponentController);
    }

    bool TryResolveAttackClash(CombatActorController opponent)
    {
        if (Motion == null || opponent == null || opponent.Motion == null)
            return false;

        if (!Motion.IsAttackClashWindowActive || !opponent.Motion.IsAttackClashWindowActive)
            return false;

        ResolveAttackMeetingByExtension(opponent);
        return true;
    }

    /// <summary>
    /// 두 액터의 HandExtensionNormalized 비율을 비교해 중간지점 충돌 또는 우세/열세를 판정한다.
    /// midpointClashTolerance 이내 차이이면 AttackClashed, 초과이면 AttackMeetingWin/Loss.
    /// </summary>
    void ResolveAttackMeetingByExtension(CombatActorController opponent)
    {
        if (TryResolveAttackMeetingByInitialMidpoint(opponent))
            return;

        Motion.MarkAttackClashed();
        opponent.Motion.MarkAttackClashed();

        float myExt = HandExtensionNormalized;
        float opExt = opponent.HandExtensionNormalized;
        float extensionDiff = myExt - opExt;  // 양수 = 내가 더 멀리 뻗음

        if (Mathf.Abs(extensionDiff) <= midpointClashTolerance)
        {
            // 중간지점 충돌 — 쌍방 동등
            string clashSummary = CombatRuleResolver.Resolve(CombatState.Attack, CombatState.Attack).Summary;
            LogCombatOutcome(CombatState.Attack, CombatState.Attack, clashSummary);
            opponent.LogCombatOutcome(CombatState.Attack, CombatState.Attack, clashSummary);
            RaiseCombatEvent(CombatEventKind.AttackClashed, opponent,
                CombatState.Attack, CombatState.Attack, clashSummary);
            opponent.RaiseCombatEvent(CombatEventKind.AttackClashed, this,
                CombatState.Attack, CombatState.Attack, clashSummary);
            return;
        }

        // 우세/열세 판정
        float maxRange = 1f - midpointClashTolerance;
        float advantageRatio = maxRange > 0f
            ? Mathf.Clamp01((Mathf.Abs(extensionDiff) - midpointClashTolerance) / maxRange)
            : 1f;

        CombatActorController winner = extensionDiff > 0f ? this : opponent;
        CombatActorController loser  = extensionDiff > 0f ? opponent : this;

        string winSummary  = $"공격 만남 우세 ({advantageRatio * 100f:F0}%)";
        string lossSummary = $"공격 만남 열세 ({advantageRatio * 100f:F0}%)";

        winner.LogCombatOutcome(CombatState.Attack, CombatState.Attack, winSummary);
        loser.LogCombatOutcome(CombatState.Attack, CombatState.Attack, lossSummary);

        winner.RaiseCombatEvent(CombatEventKind.AttackMeetingWin, loser,
            CombatState.Attack, CombatState.Attack, winSummary, advantageRatio);
        loser.RaiseCombatEvent(CombatEventKind.AttackMeetingLoss, winner,
            CombatState.Attack, CombatState.Attack, lossSummary, advantageRatio);
    }

    bool TryResolveAttackMeetingByInitialMidpoint(CombatActorController opponent)
    {
        if (Motion == null || opponent == null || opponent.Motion == null)
            return false;

        Motion.MarkAttackClashed();
        opponent.Motion.MarkAttackClashed();
        EnsureInitialAttackMeetingFrame(opponent);

        Vector3 contactPoint = (PalmContactWorldPosition + opponent.PalmContactWorldPosition) * 0.5f;
        float signedDistanceFromCenter = Vector3.Dot(
            contactPoint - initialAttackMeetingMidpoint,
            initialAttackMeetingPlayerToEnemyAxis);
        float signedOffsetNormalized = initialAttackMeetingHalfDistance > 0f
            ? signedDistanceFromCenter / initialAttackMeetingHalfDistance
            : 0f;
        float absOffsetNormalized = Mathf.Abs(signedOffsetNormalized);
        string positionSummary = BuildAttackMeetingPositionSummary(signedOffsetNormalized, contactPoint);

        if (absOffsetNormalized <= midpointClashTolerance)
        {
            string clashSummary = $"{CombatRuleResolver.Resolve(CombatState.Attack, CombatState.Attack).Summary} / {positionSummary}";
            LogCombatOutcome(CombatState.Attack, CombatState.Attack, clashSummary);
            opponent.LogCombatOutcome(CombatState.Attack, CombatState.Attack, clashSummary);
            RaiseCombatEvent(CombatEventKind.AttackClashed, opponent,
                CombatState.Attack, CombatState.Attack, clashSummary);
            opponent.RaiseCombatEvent(CombatEventKind.AttackClashed, this,
                CombatState.Attack, CombatState.Attack, clashSummary);
            return true;
        }

        float maxRange = 1f - midpointClashTolerance;
        float advantageRatio = maxRange > 0f
            ? Mathf.Clamp01((absOffsetNormalized - midpointClashTolerance) / maxRange)
            : 1f;

        CombatActorController player = GetPlayerActor(opponent);
        CombatActorController enemy = GetEnemyActor(opponent);
        CombatActorController winner = signedOffsetNormalized > 0f ? player : enemy;
        CombatActorController loser = signedOffsetNormalized > 0f ? enemy : player;

        if (winner == null || loser == null)
        {
            winner = signedOffsetNormalized > 0f ? this : opponent;
            loser = signedOffsetNormalized > 0f ? opponent : this;
        }

        string winSummary = $"공격 만남 우세 ({advantageRatio * 100f:F0}%) / {positionSummary}";
        string lossSummary = $"공격 만남 열세 ({advantageRatio * 100f:F0}%) / {positionSummary}";

        winner.LogCombatOutcome(CombatState.Attack, CombatState.Attack, winSummary);
        loser.LogCombatOutcome(CombatState.Attack, CombatState.Attack, lossSummary);

        winner.RaiseCombatEvent(CombatEventKind.AttackMeetingWin, loser,
            CombatState.Attack, CombatState.Attack, winSummary, advantageRatio);
        loser.RaiseCombatEvent(CombatEventKind.AttackMeetingLoss, winner,
            CombatState.Attack, CombatState.Attack, lossSummary, advantageRatio);
        return true;
    }

    CombatActorController GetPlayerActor(CombatActorController opponent)
    {
        if (actorSide == CombatActorSide.Player)
            return this;

        if (opponent != null && opponent.actorSide == CombatActorSide.Player)
            return opponent;

        return null;
    }

    CombatActorController GetEnemyActor(CombatActorController opponent)
    {
        if (actorSide == CombatActorSide.Enemy)
            return this;

        if (opponent != null && opponent.actorSide == CombatActorSide.Enemy)
            return opponent;

        return null;
    }

    string BuildAttackMeetingPositionSummary(float signedOffsetNormalized, Vector3 contactPoint)
    {
        float percent = Mathf.Abs(signedOffsetNormalized) * 100f;
        string side = Mathf.Abs(signedOffsetNormalized) <= 0.001f
            ? "중앙"
            : signedOffsetNormalized > 0f ? "적쪽" : "플레이어쪽";

        return $"중앙 기준 {side} {percent:F0}% (충돌점 {contactPoint.x:F2}, {contactPoint.y:F2})";
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
        string summary,
        float advantageRatio = 0f)
    {
        CombatEventRaised?.Invoke(new CombatEventData(
            kind,
            this,
            opponent,
            actorState,
            opponentState,
            summary,
            advantageRatio));
    }

    void ResolveFeintOutcome()
    {
        if (feintWasPunished || !roundCombatActive)
            return;

        CombatState opponentState = opponentController != null
            ? opponentController.ResolutionCombatState
            : CombatState.Neutral;

        if (feintBeatDodge || IsOpponentDodgeCommitted())
        {
            LogCombatOutcome(CombatState.Attack, CombatState.Dodge, "Fake beat dodge");
            return;
        }

        if (feintAttackCounterResolved)
            return;

        if (feintBeatAttack || IsOpponentAttackCommitted())
        {
            TryResolveFeintCounteredAttack(opponentController);
            return;
        }

        LogCombatOutcome(CombatState.Attack, opponentState, "Fake no effect");
    }

    void MarkFeintPunished()
    {
        feintWasPunished = true;
    }

    void NotifyOpponentFeintOfDodge()
    {
        if (opponentController != null && opponentController.IsFeinting)
            opponentController.MarkFeintBeatDodge(this);
    }

    void NotifyOpponentFeintOfAttack()
    {
        if (opponentController != null && opponentController.IsFeinting)
            opponentController.MarkFeintBeatAttack(this);
    }

    bool IsOpponentAttackCommitted()
    {
        return opponentController != null && opponentController.IsAttackCommitted;
    }

    bool IsOpponentDodgeCommitted()
    {
        return opponentController != null && opponentController.IsDodgeCommitted;
    }

    void MarkFeintBeatAttack(CombatActorController attackingOpponent)
    {
        if (!IsFeinting || attackingOpponent != opponentController)
            return;

        feintBeatAttack = true;
    }

    bool TryResolveFeintCounteredAttack(CombatActorController attackingOpponent)
    {
        if (!IsFeinting || attackingOpponent != opponentController || feintAttackCounterResolved)
            return false;

        feintBeatAttack = true;
        feintAttackCounterResolved = true;

        const string summary = "Fake beat attack";
        LogCombatOutcome(CombatState.Attack, CombatState.Attack, summary);
        RaiseCombatEvent(
            CombatEventKind.FeintCountered,
            attackingOpponent,
            CombatState.Attack,
            CombatState.Attack,
            summary);
        return true;
    }

    void MarkFeintBeatDodge(CombatActorController dodgingOpponent)
    {
        if (!IsFeinting || dodgingOpponent != opponentController)
            return;

        feintBeatDodge = true;
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
