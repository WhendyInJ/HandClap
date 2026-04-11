using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 손바닥밀치기 게임 - 플레이어 컨트롤러
/// 모든 모션은 코드로 처리 (프리미티브 전용)
///
/// [씬 설정 방법]
/// 1. 빈 GameObject "BodyPivot" 생성 (발 위치에 배치) → bodyPivot에 할당
///    - BodyPivot 자식으로 몸통 프리미티브 배치
/// 2. 빈 GameObject "HandPivot" 생성 → handPivot에 할당
///    - HandPivot 자식으로 손 Quad/Sprite 배치 → handVisual에 할
/// </summary>
public class PlayerController : MonoBehaviour
{
    public event Action<CombatEventData> CombatEventRaised;

    [Header("몸통 오브젝트")]
    [Tooltip("발 위치에 놓인 빈 GameObject. 자식으로 몸통 프리미티브를 둘 것.")]
    [SerializeField] private Transform bodyPivot;

    [Header("손 오브젝트")]
    [Tooltip("손의 이동/회전 기준점 (빈 GameObject 권장)")]
    [SerializeField] private Transform handPivot;
    [Tooltip("실제 스케일이 변하는 손 메시 오브젝트")]
    [SerializeField] private Transform handVisual;

    [Header("Idle 흔들기")]
    [SerializeField] private float idleBobAmplitude = 0.12f;
    [SerializeField] private float idleBobSpeed     = 1.2f;
    [SerializeField] private float idleSwayAngle    = 4f;

    [Header("몸통 기울기 (React 참고: transformOrigin = bottom)")]
    [Tooltip("밀칠 때 앞으로 기울어지는 최대 각도")]
    [SerializeField] private float pushLeanAngle    = 22f;
    [Tooltip("예비동작 시 뒤로 젖히는 각도")]
    [SerializeField] private float windupLeanAngle  = -8f;
    [Tooltip("기울 때 몸이 살짝 내려오는 거리 (y 하강)")]
    [SerializeField] private float leanDropY        = 0.15f;
    [Tooltip("밀치기 시 몸통이 앞으로 따라가는 거리 (손 pushDistance의 비율)")]
    [SerializeField] private float bodyFollowRatio  = 0.35f;

    [Header("밀치기 모션")]
    [SerializeField] private float pushDistance        = 2.5f;
    [SerializeField] private float pushScaleMultiplier = 1.85f;
    [SerializeField] private float pushStretchX        = 1.2f;
    [SerializeField] private float pushSquashY         = 0.75f;
    [SerializeField] private Vector2 pushDirection     = Vector2.right;

    [Header("밀치기 타이밍")]
    [SerializeField] private float windupDuration  = 0.07f;
    [SerializeField] private float windupDistance  = 0.3f;
    [SerializeField] private float pushOutDuration = 0.11f;
    [SerializeField] private float holdDuration    = 0.07f;
    [SerializeField] private float retractDuration = 0.30f;
    [SerializeField] private float cooldown        = 0.35f;

    [Header("Dodge Motion")]
    [SerializeField] private float dodgeDistance        = 1.2f;
    [SerializeField] private float dodgeHeight          = 0.35f;
    [SerializeField] private float dodgeLeanAngle       = 18f;
    [SerializeField] private float dodgeOutDuration     = 0.14f;
    [SerializeField] private float dodgeHoldDuration    = 0.08f;
    [SerializeField] private float dodgeRecoverDuration = 0.22f;

    [Header("Balance Debug Motion")]
    [SerializeField] private float balanceLeanBackAngle = 14f;
    [SerializeField] private float balanceWobbleAngle   = 9f;
    [SerializeField] private float balanceEnterDuration = 0.12f;
    [SerializeField] private float balanceWobbleTime    = 0.9f;
    [SerializeField] private float balanceRecoverTime   = 0.18f;
    [SerializeField] private float balanceWobbleCycles  = 2.5f;
    [SerializeField] private float balanceHandSwingAngle = 34f;
    [SerializeField] private float balanceHandOffsetX    = 0.32f;
    [SerializeField] private float balanceHandOffsetY    = 0.08f;
    [SerializeField] private float balanceHandFlailCycles = 3.2f;

    [Header("Debug Input")]
    [SerializeField] private bool enableDebugInput = true;
    [SerializeField] private KeyCode pushKey = KeyCode.Space;
    [SerializeField] private KeyCode dodgeKey = KeyCode.X;
    [SerializeField] private KeyCode balanceDebugKey = KeyCode.C;

    [Header("Combat Debug")]
    [SerializeField] private string actorName;
    [SerializeField] private CombatActorSide actorSide = CombatActorSide.Player;
    [SerializeField] private string actorColorHex = "#4AA3FF";
    [SerializeField] private PlayerController opponentController;
    [SerializeField] private bool enableCombatDebugLogs = true;

    // ── 내부 상태 ──────────────────────────────────────────
    private Vector3    basePivotLocalPos;
    private Vector3    baseVisualLocalScale;
    private Quaternion basePivotLocalRot;
    private Transform  handParent;

    private Vector3    baseBodyLocalPos;
    private Vector3    baseBodyLocalScale;
    private Quaternion baseBodyLocalRot;

    private float idleTimer;
    private bool  isPushing;
    private bool  isDodging;
    private bool  isBalancing;
    private bool  onCooldown;
    private bool  hasQueuedDecision;
    private CombatState queuedDecisionState;
    private bool  attackClashWindowActive;
    private bool  attackResolutionComplete;
    private bool  attackClashed;
    private bool  dodgeSucceeded;

    bool roundCombatActive = true;

    // ═══════════════════════════════════════════════════════
    void Start()
    {
        if (handPivot  == null) handPivot  = transform;
        if (handVisual == null) handVisual = handPivot;
        handParent = handPivot.parent;

        basePivotLocalPos    = handPivot.localPosition;
        baseVisualLocalScale = handVisual.localScale;
        basePivotLocalRot    = handPivot.localRotation;

        if (bodyPivot != null)
        {
            baseBodyLocalPos = bodyPivot.localPosition;
            baseBodyLocalScale = bodyPivot.localScale;
            baseBodyLocalRot = bodyPivot.localRotation;
        }
    }

    void Update()
    {
        if (roundCombatActive && !isPushing && !isDodging && !isBalancing)
            UpdateIdleMotion();

        if (!roundCombatActive || !enableDebugInput)
            return;

        if (Input.GetKeyDown(pushKey))
            TryPush();

        if (Input.GetKeyDown(dodgeKey))
            TryDodge();

        if (Input.GetKeyDown(balanceDebugKey))
            TryBalanceDebug();
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
        if (!roundCombatActive || isPushing || isDodging || isBalancing)
            return false;

        StartCoroutine(DoBalanceDebug());
        return true;
    }

    public bool TryStayNeutral()
    {
        return QueueDecision(CombatState.Neutral);
    }

    public void SetPushDirection(Vector2 direction)
    {
        if (direction.sqrMagnitude <= 0f)
            return;

        pushDirection = direction.normalized;
    }

    public void SetDebugInputEnabled(bool enabled)
    {
        enableDebugInput = enabled;
    }

    public bool RoundCombatActive => roundCombatActive;

    public void SetRoundCombatActive(bool active)
    {
        roundCombatActive = active;
        if (!active)
            hasQueuedDecision = false;
    }

    public void SetDebugKeys(KeyCode newPushKey, KeyCode newDodgeKey, KeyCode newBalanceKey)
    {
        pushKey = newPushKey;
        dodgeKey = newDodgeKey;
        balanceDebugKey = newBalanceKey;
    }

    public void SetOpponent(PlayerController opponent)
    {
        opponentController = opponent;
    }

    public CombatState CurrentCombatState =>
        isPushing ? CombatState.Attack :
        isDodging ? CombatState.Dodge :
        CombatState.Neutral;

    public CombatState ResolutionCombatState => hasQueuedDecision ? queuedDecisionState : CurrentCombatState;
    public bool IsAttackClashWindowActive => isPushing && attackClashWindowActive && !attackResolutionComplete;
    public bool IsDodgeWindowActive => isDodging;

    public bool CanAttemptDecision =>
        roundCombatActive && !hasQueuedDecision && !isPushing && !isDodging && !isBalancing && !onCooldown;

    public CombatActorSide ActorSide => actorSide;
    public PlayerController OpponentController => opponentController;

    string DisplayName => string.IsNullOrWhiteSpace(actorName) ? gameObject.name : actorName;
    string CombatLabel => string.IsNullOrWhiteSpace(actorName)
        ? actorSide == CombatActorSide.Player ? "\uD50C\uB808\uC774\uC5B4" : "\uC801"
        : actorName;
    string CombatColorHex => string.IsNullOrWhiteSpace(actorColorHex)
        ? actorSide == CombatActorSide.Player ? "#4AA3FF" : "#FF5C5C"
        : actorColorHex;

    public void SetCombatPresentation(CombatActorSide side, string label, string colorHex)
    {
        actorSide = side;
        actorName = label;
        actorColorHex = colorHex;
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
                StartCoroutine(DoPush());
                break;

            case CombatState.Dodge:
                StartCoroutine(DoDodge());
                break;
        }
    }

    void LogCombatOutcome(CombatState actorState, CombatState opponentState, string summary)
    {
        if (!enableCombatDebugLogs)
            return;

        string lane = BuildCombatLane(opponentController, actorState, opponentState);

        Debug.Log(
            $"[\uC804\uD22C] {lane}  \uACB0\uACFC:{summary}",
            this);
    }

    void RaiseCombatEvent(
        CombatEventKind kind,
        PlayerController opponent,
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
        PlayerController opponent,
        CombatState actorState,
        CombatState opponentState)
    {
        PlayerController player = actorSide == CombatActorSide.Player ? this : opponent;
        PlayerController enemy = actorSide == CombatActorSide.Enemy ? this : opponent;

        string playerLabel = player != null
            ? player.GetColoredCombatLabel(
                player == this ? actorState : opponentState)
            : FormatColoredLabel("\uD50C\uB808\uC774\uC5B4", CombatRuleResolver.ToDisplayName(CombatState.Neutral), "#4AA3FF");
        string enemyLabel = enemy != null
            ? enemy.GetColoredCombatLabel(
                enemy == this ? actorState : opponentState)
            : FormatColoredLabel("\uC801", CombatRuleResolver.ToDisplayName(CombatState.Neutral), "#FF5C5C");

        string arrow = actorSide == CombatActorSide.Player ? ">" : "<";
        return $"{playerLabel} {arrow} {enemyLabel}";
    }

    public string GetColoredCombatLabel(CombatState state)
    {
        return FormatColoredLabel(CombatLabel, CombatRuleResolver.ToDisplayName(state), CombatColorHex);
    }

    static string FormatColoredLabel(string label, string state, string colorHex)
    {
        return $"<color={colorHex}><b>{label}[{state}]</b></color>";
    }

    // ═══════════════════════════════════════════════════════
    // Idle 모션: 손 + 몸통이 살살 흔들림
    // ═══════════════════════════════════════════════════════
    void ResolveAttackOutcome()
    {
        if (attackResolutionComplete)
            return;

        if (opponentController != null && TryResolveAttackClash(opponentController))
            return;

        attackClashWindowActive = false;
        attackResolutionComplete = true;

        if (opponentController != null && opponentController.IsDodgeWindowActive)
        {
            string summary = CombatRuleResolver.Resolve(CombatState.Attack, CombatState.Dodge).Summary;
            LogCombatOutcome(
                CombatState.Attack,
                CombatState.Dodge,
                summary);
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

    bool TryResolveAttackClash(PlayerController opponent)
    {
        if (!IsAttackClashWindowActive || !opponent.IsAttackClashWindowActive)
            return false;

        attackClashWindowActive = false;
        attackResolutionComplete = true;
        attackClashed = true;

        opponent.attackClashWindowActive = false;
        opponent.attackResolutionComplete = true;
        opponent.attackClashed = true;

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
        if (!isDodging || dodgeSucceeded)
            return;

        dodgeSucceeded = true;
        string summary = CombatRuleResolver.Resolve(CombatState.Dodge, CombatState.Attack).Summary;
        LogCombatOutcome(
            CombatState.Dodge,
            CombatState.Attack,
            summary);
        RaiseCombatEvent(
            CombatEventKind.DodgeSucceeded,
            opponentController,
            CombatState.Dodge,
            CombatState.Attack,
            summary);
    }

    float GetBodyCurrentLocalAngle()
    {
        if (bodyPivot == null)
            return 0f;

        return Mathf.DeltaAngle(baseBodyLocalRot.eulerAngles.z, bodyPivot.localRotation.eulerAngles.z);
    }

    void UpdateIdleMotion()
    {
        idleTimer += Time.deltaTime;

        float bobY  = Mathf.Sin(idleTimer * idleBobSpeed * Mathf.PI * 2f) * idleBobAmplitude;
        float swayZ = Mathf.Sin(idleTimer * idleBobSpeed * Mathf.PI * 2f * 0.65f) * idleSwayAngle;

        // 손 흔들기
        handPivot.SetLocalPositionAndRotation(
            basePivotLocalPos + new Vector3(0f, bobY, 0f),
            basePivotLocalRot * Quaternion.Euler(0f, 0f, swayZ));

        // 몸통도 같은 주기로 미세하게 흔들기 (진폭은 절반)
        if (bodyPivot != null)
        {
            bodyPivot.SetLocalPositionAndRotation(
                baseBodyLocalPos + new Vector3(0f, bobY * 0.5f, 0f),
                baseBodyLocalRot * Quaternion.Euler(0f, 0f, swayZ * 0.4f));
        }
    }

    // ═══════════════════════════════════════════════════════
    // 밀치기 모션: 예비동작 → 앞으로 내밀기 → 유지 → 복귀
    // 몸통은 React의 transformOrigin:bottom 과 동일하게,
    // bodyPivot을 발 위치에 두어 상체가 앞으로 숙여지는 효과
    // ═══════════════════════════════════════════════════════
    IEnumerator DoPush()
    {
        isPushing  = true;
        onCooldown = true;
        attackClashWindowActive = true;
        attackResolutionComplete = false;
        attackClashed = false;

        Vector3 dir       = (Vector3)pushDirection.normalized;
        Vector3 pushWorldDir = GetPushWorldDirection(dir);
        Quaternion baseRot = GetBaseHandWorldRotation();
        float leanSign    = GetBodyLeanSign();
        float windupAngle = Mathf.Abs(windupLeanAngle) * -leanSign;
        float pushAngle   = Mathf.Abs(pushLeanAngle) * leanSign;
        Vector3 idlePos   = handPivot.position;
        Vector3 basePos   = GetBaseHandWorldPosition();
        Vector3 windupPos = basePos - pushWorldDir * windupDistance;
        Vector3 peakPos   = basePos + pushWorldDir * pushDistance;

        if (bodyPivot != null) bodyPivot.localRotation = baseBodyLocalRot;
        handPivot.rotation = baseRot;

        // 몸통이 앞으로 따라가는 목표 위치 (앞 방향 + 살짝 하강)
        Vector3 bodyPeakPos = baseBodyLocalPos
            + dir * (pushDistance * bodyFollowRatio)
            + new Vector3(0f, -leanDropY, 0f);

        // ── Phase 0: 예비동작 ──
        // 손: 뒤로 / 몸통: 살짝 반대로 젖히기 (무게 실리는 예비 자세)
        StartCoroutine(LerpBodyLean(
            baseBodyLocalPos, baseBodyLocalPos,
            0f, windupAngle,
            windupDuration, EaseOutCubic));

        yield return LerpPivotAndScale(
            idlePos, windupPos,
            handVisual.localScale, baseVisualLocalScale * 0.9f,
            baseRot, windupDuration, EaseOutCubic);

        // ── Phase 1: 앞으로 내밀기 + 몸통이 손과 함께 앞으로 따라감 ──
        Vector3 stretchScale = new(
            baseVisualLocalScale.x * pushScaleMultiplier * pushStretchX,
            baseVisualLocalScale.y * pushScaleMultiplier * pushSquashY,
            baseVisualLocalScale.z * pushScaleMultiplier);

        StartCoroutine(LerpBodyLean(
            baseBodyLocalPos, bodyPeakPos,
            windupAngle, pushAngle,
            pushOutDuration, EaseOutBack));

        yield return LerpPivotAndScale(
            windupPos, peakPos,
            handVisual.localScale, stretchScale,
            baseRot, pushOutDuration, EaseOutBack);

        // ── Phase 2: 유지 ──
        ResolveAttackOutcome();

        if (attackClashed)
        {
            yield return RecoverPushFromCurrentPose(basePos, baseRot);
            yield break;
        }

        Vector3 bigUniform = baseVisualLocalScale * pushScaleMultiplier;
        float elapsed = 0f;
        while (elapsed < holdDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / holdDuration);
            handPivot.rotation = baseRot;
            handVisual.localScale = Vector3.Lerp(stretchScale, bigUniform, t);
            yield return null;
        }

        // ── Phase 3: 복귀 ── 손 + 몸통 동시에 원위치
        StartCoroutine(LerpBodyLean(
            bodyPeakPos, baseBodyLocalPos,
            pushAngle, 0f,
            retractDuration, EaseInOutCubic));

        yield return LerpPivotAndScale(
            peakPos, basePos,
            handVisual.localScale, baseVisualLocalScale,
            baseRot, retractDuration, EaseInOutCubic);

        FinishPushState(basePos, baseRot);

        yield return new WaitForSeconds(cooldown);
        onCooldown = false;
    }

    IEnumerator RecoverPushFromCurrentPose(Vector3 basePos, Quaternion baseRot)
    {
        StartCoroutine(LerpBodyLean(
            bodyPivot != null ? bodyPivot.localPosition : baseBodyLocalPos, baseBodyLocalPos,
            bodyPivot != null ? GetBodyCurrentLocalAngle() : 0f, 0f,
            retractDuration, EaseInOutCubic));

        yield return LerpPivotAndScale(
            handPivot.position, basePos,
            handVisual.localScale, baseVisualLocalScale,
            baseRot, retractDuration, EaseInOutCubic);

        FinishPushState(basePos, baseRot);

        yield return new WaitForSeconds(cooldown);
        onCooldown = false;
    }

    void FinishPushState(Vector3 basePos, Quaternion baseRot)
    {
        handPivot.SetPositionAndRotation(basePos, baseRot);
        handVisual.localScale = baseVisualLocalScale;

        if (bodyPivot != null)
            bodyPivot.SetLocalPositionAndRotation(baseBodyLocalPos, baseBodyLocalRot);

        attackClashWindowActive = false;
        attackResolutionComplete = false;
        attackClashed = false;
        isPushing = false;
    }

    IEnumerator DoDodge()
    {
        isDodging  = true;
        onCooldown = true;
        dodgeSucceeded = false;

        Vector3 startPos      = transform.position;
        Vector3 dodgeTarget   = startPos + GetDodgeWorldDirection() * dodgeDistance;
        float dodgePeakAngle  = Mathf.Abs(dodgeLeanAngle) * -GetBodyLeanSign();

        handPivot.SetLocalPositionAndRotation(basePivotLocalPos, basePivotLocalRot);
        handVisual.localScale = baseVisualLocalScale;

        float elapsed = 0f;
        while (elapsed < dodgeOutDuration)
        {
            elapsed += Time.deltaTime;

            float normalizedTime = Mathf.Clamp01(elapsed / dodgeOutDuration);
            float horizontalT    = EaseOutCubic(normalizedTime);
            float arcHeight      = Mathf.Sin(normalizedTime * Mathf.PI * 0.5f) * dodgeHeight;

            transform.position = Vector3.Lerp(startPos, dodgeTarget, horizontalT)
                + Vector3.up * arcHeight;

            if (bodyPivot != null)
            {
                bodyPivot.SetLocalPositionAndRotation(
                    baseBodyLocalPos,
                    baseBodyLocalRot * Quaternion.Euler(
                        0f,
                        0f,
                        Mathf.Lerp(0f, dodgePeakAngle, EaseOutCubic(normalizedTime))));
            }

            yield return null;
        }

        transform.position = dodgeTarget + Vector3.up * dodgeHeight;
        if (bodyPivot != null)
        {
            bodyPivot.SetLocalPositionAndRotation(
                baseBodyLocalPos,
                baseBodyLocalRot * Quaternion.Euler(0f, 0f, dodgePeakAngle));
        }

        yield return new WaitForSeconds(dodgeHoldDuration);

        elapsed = 0f;
        while (elapsed < dodgeRecoverDuration)
        {
            elapsed += Time.deltaTime;

            float normalizedTime = Mathf.Clamp01(elapsed / dodgeRecoverDuration);
            float horizontalT    = EaseInOutCubic(normalizedTime);
            float verticalT      = EaseInOutCubic(normalizedTime);

            transform.position = Vector3.Lerp(
                dodgeTarget + Vector3.up * dodgeHeight,
                startPos,
                horizontalT);
            transform.position = new Vector3(
                transform.position.x,
                Mathf.Lerp(dodgeHeight, 0f, verticalT) + startPos.y,
                startPos.z);

            if (bodyPivot != null)
            {
                bodyPivot.SetLocalPositionAndRotation(
                    baseBodyLocalPos,
                    baseBodyLocalRot * Quaternion.Euler(
                        0f,
                        0f,
                        Mathf.Lerp(dodgePeakAngle, 0f, EaseInOutCubic(normalizedTime))));
            }

            yield return null;
        }

        transform.position = startPos;
        handPivot.SetLocalPositionAndRotation(basePivotLocalPos, basePivotLocalRot);
        handVisual.localScale = baseVisualLocalScale;

        if (bodyPivot != null)
            bodyPivot.SetLocalPositionAndRotation(baseBodyLocalPos, baseBodyLocalRot);

        if (!dodgeSucceeded)
        {
            CombatState opponentState = opponentController != null
                ? opponentController.ResolutionCombatState
                : CombatState.Neutral;
            const string failSummary = "\uD68C\uD53C \uC2E4\uD328";

            LogCombatOutcome(
                CombatState.Dodge,
                opponentState,
                failSummary);
            RaiseCombatEvent(
                CombatEventKind.DodgeFailed,
                opponentController,
                CombatState.Dodge,
                opponentState,
                failSummary);
        }

        isDodging = false;

        yield return new WaitForSeconds(cooldown);
        onCooldown = false;
    }

    IEnumerator DoBalanceDebug()
    {
        isBalancing = true;

        Vector3 baseHandWorldPos = GetBaseHandWorldPosition();
        Quaternion baseHandWorldRot = GetBaseHandWorldRotation();

        handPivot.SetPositionAndRotation(baseHandWorldPos, baseHandWorldRot);
        handVisual.localScale = baseVisualLocalScale;

        if (bodyPivot == null)
        {
            isBalancing = false;
            yield break;
        }

        float backwardAngle = Mathf.Abs(balanceLeanBackAngle) * -GetBodyLeanSign();
        float wobbleAngle   = Mathf.Abs(balanceWobbleAngle);

        float elapsed = 0f;
        while (elapsed < balanceEnterDuration)
        {
            elapsed += Time.deltaTime;

            float t = EaseOutCubic(Mathf.Clamp01(elapsed / balanceEnterDuration));
            bodyPivot.SetLocalPositionAndRotation(
                baseBodyLocalPos,
                baseBodyLocalRot * Quaternion.Euler(0f, 0f, Mathf.Lerp(0f, backwardAngle, t)));
            ApplyBalanceHandMotion(baseHandWorldPos, baseHandWorldRot, t * 0.35f, t);
            yield return null;
        }

        elapsed = 0f;
        while (elapsed < balanceWobbleTime)
        {
            elapsed += Time.deltaTime;

            float normalizedTime = Mathf.Clamp01(elapsed / balanceWobbleTime);
            float damping        = 1f - normalizedTime;
            float wobbleOffset   = Mathf.Sin(normalizedTime * Mathf.PI * 2f * balanceWobbleCycles)
                * wobbleAngle
                * damping;

            bodyPivot.SetLocalPositionAndRotation(
                baseBodyLocalPos,
                baseBodyLocalRot * Quaternion.Euler(0f, 0f, backwardAngle + wobbleOffset));
            ApplyBalanceHandMotion(
                baseHandWorldPos,
                baseHandWorldRot,
                normalizedTime,
                0.45f + damping * 0.55f);
            yield return null;
        }

        Vector3 recoverHandStartPos = handPivot.position;
        elapsed = 0f;
        while (elapsed < balanceRecoverTime)
        {
            elapsed += Time.deltaTime;

            float t = EaseInOutCubic(Mathf.Clamp01(elapsed / balanceRecoverTime));
            bodyPivot.SetLocalPositionAndRotation(
                baseBodyLocalPos,
                baseBodyLocalRot * Quaternion.Euler(0f, 0f, Mathf.Lerp(backwardAngle, 0f, t)));
            handPivot.SetPositionAndRotation(
                Vector3.Lerp(recoverHandStartPos, baseHandWorldPos, t),
                baseHandWorldRot);
            yield return null;
        }

        handPivot.SetPositionAndRotation(baseHandWorldPos, baseHandWorldRot);
        handVisual.localScale = baseVisualLocalScale;
        bodyPivot.SetLocalPositionAndRotation(baseBodyLocalPos, baseBodyLocalRot);
        isBalancing = false;
    }

    Vector3 GetBaseHandWorldPosition()
    {
        if (bodyPivot != null && handParent == bodyPivot && bodyPivot.parent != null)
        {
            Matrix4x4 baseBodyMatrix = Matrix4x4.TRS(
                baseBodyLocalPos,
                baseBodyLocalRot,
                baseBodyLocalScale);

            return bodyPivot.parent.localToWorldMatrix.MultiplyPoint3x4(
                baseBodyMatrix.MultiplyPoint3x4(basePivotLocalPos));
        }

        return handParent != null
            ? handParent.TransformPoint(basePivotLocalPos)
            : transform.TransformPoint(basePivotLocalPos);
    }

    Vector3 GetPushWorldDirection(Vector3 localDirection)
    {
        Vector3 worldDirection = transform.TransformDirection(localDirection);
        worldDirection.z = 0f;
        return worldDirection.sqrMagnitude > 0f ? worldDirection.normalized : Vector3.right;
    }

    Vector3 GetDodgeWorldDirection()
    {
        Vector3 backwardDirection = -GetPushWorldDirection((Vector3)pushDirection.normalized);
        backwardDirection.z = 0f;
        return backwardDirection.sqrMagnitude > 0f ? backwardDirection.normalized : Vector3.left;
    }

    Quaternion GetBaseHandWorldRotation()
    {
        if (bodyPivot != null && handParent == bodyPivot)
        {
            Quaternion bodyParentRotation = bodyPivot.parent != null
                ? bodyPivot.parent.rotation
                : Quaternion.identity;

            return bodyParentRotation * baseBodyLocalRot * basePivotLocalRot;
        }

        return handParent != null
            ? handParent.rotation * basePivotLocalRot
            : basePivotLocalRot;
    }

    void ApplyBalanceHandMotion(
        Vector3 baseWorldPos,
        Quaternion baseWorldRot,
        float normalizedTime,
        float intensity)
    {
        Vector3 armAnchor = transform.position;
        Vector3 baseArmDirection = baseWorldPos - armAnchor;
        if (baseArmDirection.sqrMagnitude < 0.0001f)
            baseArmDirection = new Vector3(1f, 0.2f, 0f);

        baseArmDirection.Normalize();

        float mainSwing = Mathf.Sin(normalizedTime * Mathf.PI * 2f * balanceHandFlailCycles);
        float liftSwing = Mathf.Sin(
            normalizedTime * Mathf.PI * 2f * (balanceHandFlailCycles * 0.5f) + 0.75f);

        float sweepAngle = mainSwing * balanceHandSwingAngle * intensity;
        float reachRadius = Vector3.Distance(armAnchor, baseWorldPos) + balanceHandOffsetX * intensity;

        Vector3 swungDirection = Quaternion.Euler(0f, 0f, sweepAngle) * baseArmDirection;
        Vector3 handPosition = armAnchor + swungDirection * reachRadius
            + Vector3.up * (liftSwing * balanceHandOffsetY * intensity);

        handPivot.SetPositionAndRotation(
            handPosition,
            baseWorldRot);
    }

    float GetBodyLeanSign()
    {
        Vector2 dir = pushDirection.sqrMagnitude > 0f ? pushDirection.normalized : Vector2.right;

        if (Mathf.Abs(dir.x) > 0.001f)
            return -Mathf.Sign(dir.x);

        return Mathf.Sign(pushLeanAngle) == 0f ? -1f : Mathf.Sign(pushLeanAngle);
    }

    // ── 몸통 기울기 보간 (위치 + Z회전) ──────────────────
    // bodyPivot이 발 위치에 있으면 React의 transformOrigin:bottom 과 동일한 효과
    IEnumerator LerpBodyLean(
        Vector3 fromPos, Vector3 toPos,
        float fromAngle, float toAngle,
        float duration, System.Func<float, float> ease)
    {
        if (bodyPivot == null) yield break;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t     = ease(Mathf.Clamp01(elapsed / duration));
            float angle = Mathf.Lerp(fromAngle, toAngle, t);
            bodyPivot.SetLocalPositionAndRotation(
                Vector3.Lerp(fromPos, toPos, t),
                baseBodyLocalRot * Quaternion.Euler(0f, 0f, angle));
            yield return null;
        }
    }

    // ── 손 위치 + 스케일 보간 ────────────────────────────
    IEnumerator LerpPivotAndScale(
        Vector3 fromPos,   Vector3 toPos,
        Vector3 fromScale, Vector3 toScale,
        Quaternion worldRotation,
        float duration, System.Func<float, float> ease)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = ease(Mathf.Clamp01(elapsed / duration));
            handPivot.SetPositionAndRotation(
                Vector3.Lerp(fromPos, toPos, t),
                worldRotation);
            handVisual.localScale   = Vector3.Lerp(fromScale, toScale, t);
            yield return null;
        }
    }

    // ═══════════════════════════════════════════════════════
    // Easing 함수 (React framer-motion 커브와 동일 계열)
    // ═══════════════════════════════════════════════════════
    static float EaseOutCubic(float t) =>
        1f - Mathf.Pow(1f - t, 3f);

    // EaseOutBack: 살짝 오버슈트 → 앞으로 확 내밀리는 타격감
    static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }

    static float EaseInOutCubic(float t) =>
        t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
}
