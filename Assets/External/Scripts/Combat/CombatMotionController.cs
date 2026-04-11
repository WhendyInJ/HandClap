using System;
using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class CombatMotionController : MonoBehaviour
{
    [Header("Body Object")]
    [SerializeField] private Transform bodyPivot;

    [Header("Hand Object")]
    [SerializeField] private Transform handPivot;
    [SerializeField] private Transform handVisual;

    [Header("Idle Motion")]
    [SerializeField] private float idleBobAmplitude = 0.12f;
    [SerializeField] private float idleBobSpeed = 1.2f;
    [SerializeField] private float idleSwayAngle = 4f;

    [Header("Body Lean")]
    [SerializeField] private float pushLeanAngle = 22f;
    [SerializeField] private float windupLeanAngle = -8f;
    [SerializeField] private float leanDropY = 0.15f;
    [SerializeField] private float bodyFollowRatio = 0.35f;

    [Header("Push Motion")]
    [SerializeField] private float pushDistance = 2.5f;
    [SerializeField] private float pushScaleMultiplier = 1.85f;
    [SerializeField] private float pushStretchX = 1.2f;
    [SerializeField] private float pushSquashY = 0.75f;
    [SerializeField] private Vector2 pushDirection = Vector2.right;

    [Header("Push Timing")]
    [SerializeField] private float windupDuration = 0.07f;
    [SerializeField] private float windupDistance = 0.3f;
    [SerializeField] private float pushOutDuration = 0.11f;
    [SerializeField] private float holdDuration = 0.07f;
    [SerializeField] private float retractDuration = 0.30f;
    [SerializeField] private float cooldown = 0.35f;

    [Header("Fake Attack")]
    [SerializeField] private float fakePullBackDistance = 0.35f;
    [SerializeField] private float fakePullBackDuration = 0.06f;
    [SerializeField] private float fakeRecoverDuration = 0.14f;
    [SerializeField, Range(0f, 1f)] private float fakeCooldownMultiplier = 0.5f;
    [SerializeField] private float fakeBodyLeanAngle = 10f;

    [Header("Feint Attack (팔 뻗었다 회수하는 독립 페인트 모션)")]
    [Tooltip("팔이 뻗는 거리. pushDistance보다 짧게 설정하면 '살짝 내밀다 회수' 느낌이 납니다.")]
    [SerializeField] private float feintPushDistance = 1.4f;
    [Tooltip("팔을 뻗는 시간(초). 짧을수록 빠르게 내밀어집니다.")]
    [SerializeField] private float feintExtendDuration = 0.12f;
    [Tooltip("뻗을 때 몸통 앞쪽 기울기 각도(도).")]
    [SerializeField] private float feintBodyLeanAngle = 10f;
    [Tooltip("팔을 회수하는 전체 시간(초). 스냅백 + 복귀 구간을 합산한 총 시간.")]
    [SerializeField] private float feintRetractDuration = 0.28f;
    [Tooltip("회수 시 뒤쪽으로 기우는 반동 각도(도). 값이 클수록 크게 뒤로 기웁니다.")]
    [SerializeField] private float feintRecoilAngle = 14f;
    [Tooltip("전체 회수 시간에서 스냅백(반동) 구간이 차지하는 비율 (0~1).\n" +
             "예: 0.35이면 앞 35%는 빠른 반동, 뒤 65%는 몸통 복귀입니다.")]
    [SerializeField, Range(0f, 1f)] private float feintRecoilPhaseRatio = 0.35f;

    [Header("Dodge Motion")]
    [SerializeField] private float dodgeDistance = 1.2f;
    [SerializeField] private float dodgeHeight = 0.35f;
    [SerializeField] private float dodgeLeanAngle = 18f;
    [SerializeField] private float dodgeOutDuration = 0.14f;
    [SerializeField] private float dodgeHoldDuration = 0.08f;
    [SerializeField] private float dodgeRecoverDuration = 0.22f;

    [Header("Stagger Motion")]
    [Tooltip("충격 직후 뒤로 기우는 시간(초)")]
    [SerializeField] private float staggerEnterDuration = 0.08f;
    [Tooltip("충격 각도(도). 뒤로 기우는 최대 각도.")]
    [SerializeField] private float staggerImpactAngle = 20f;
    [Tooltip("흔들림 진폭(도)")]
    [SerializeField] private float staggerWobbleAngle = 8f;
    [Tooltip("초당 흔들림 사이클 수")]
    [SerializeField] private float staggerWobbleCycles = 2.5f;
    [Tooltip("중립으로 복귀하는 시간(초)")]
    [SerializeField] private float staggerRecoverDuration = 0.22f;

    [Header("Balance Debug Motion")]
    [SerializeField] private float balanceLeanBackAngle = 14f;
    [SerializeField] private float balanceWobbleAngle = 9f;
    [SerializeField] private float balanceEnterDuration = 0.12f;
    [SerializeField] private float balanceWobbleTime = 0.9f;
    [SerializeField] private float balanceRecoverTime = 0.18f;
    [SerializeField] private float balanceWobbleCycles = 2.5f;
    [SerializeField] private float balanceHandSwingAngle = 34f;
    [SerializeField] private float balanceHandOffsetX = 0.32f;
    [SerializeField] private float balanceHandOffsetY = 0.08f;
    [SerializeField] private float balanceHandFlailCycles = 3.2f;

    private Vector3 basePivotLocalPos;
    private Vector3 baseVisualLocalScale;
    private Quaternion basePivotLocalRot;
    private Transform handParent;

    private Vector3 baseBodyLocalPos;
    private Vector3 baseBodyLocalScale;
    private Quaternion baseBodyLocalRot;

    private float idleTimer;
    private bool isPushing;
    private bool isDodging;
    private bool isBalancing;
    private bool isStaggered;
    private bool staggerStopped;
    private bool onCooldown;
    private bool attackClashWindowActive;
    private bool attackFakeWindowActive;
    private bool attackFakeRequested;
    private bool attackResolutionComplete;
    private bool attackClashed;
    private bool attackFeinted;
    private bool initialized;
    private Coroutine bodyLeanRoutine;

    public bool IsPushing => isPushing;
    public bool IsDodging => isDodging;
    public bool IsBalancing => isBalancing;
    public bool IsOnCooldown => onCooldown;
    public bool CanStartMotion => !isPushing && !isDodging && !isBalancing && !isStaggered && !onCooldown;
    public bool IsStaggered => isStaggered;
    public bool IsAttackClashWindowActive => isPushing && attackClashWindowActive && !attackResolutionComplete;
    public bool IsAttackFakeWindowActive => isPushing && attackFakeWindowActive && !attackResolutionComplete;
    public bool CanFakeAttack => IsAttackFakeWindowActive && !attackFakeRequested && !attackClashed;
    public bool IsAttackResolutionComplete => attackResolutionComplete;
    public bool IsAttackFeinted => attackFeinted;
    public float BaseCooldown => cooldown;

    void Awake()
    {
        Initialize();
    }

    void Update()
    {
        Initialize();

        if (!isPushing && !isDodging && !isBalancing)
            UpdateIdleMotion();
    }

    void OnValidate()
    {
        idleBobAmplitude = Mathf.Max(0f, idleBobAmplitude);
        idleBobSpeed = Mathf.Max(0f, idleBobSpeed);
        windupDuration = Mathf.Max(0f, windupDuration);
        windupDistance = Mathf.Max(0f, windupDistance);
        pushOutDuration = Mathf.Max(0f, pushOutDuration);
        holdDuration = Mathf.Max(0f, holdDuration);
        retractDuration = Mathf.Max(0f, retractDuration);
        cooldown = Mathf.Max(0f, cooldown);
        fakePullBackDistance = Mathf.Max(0f, fakePullBackDistance);
        fakePullBackDuration = Mathf.Max(0f, fakePullBackDuration);
        fakeRecoverDuration = Mathf.Max(0f, fakeRecoverDuration);
        fakeCooldownMultiplier = Mathf.Clamp01(fakeCooldownMultiplier);
        fakeBodyLeanAngle = Mathf.Max(0f, fakeBodyLeanAngle);
        feintPushDistance = Mathf.Max(0f, feintPushDistance);
        feintExtendDuration = Mathf.Max(0.01f, feintExtendDuration);
        feintRetractDuration = Mathf.Max(0.01f, feintRetractDuration);
        feintBodyLeanAngle = Mathf.Max(0f, feintBodyLeanAngle);
        feintRecoilAngle = Mathf.Max(0f, feintRecoilAngle);
        feintRecoilPhaseRatio = Mathf.Clamp01(feintRecoilPhaseRatio);
        staggerEnterDuration = Mathf.Max(0f, staggerEnterDuration);
        staggerImpactAngle = Mathf.Max(0f, staggerImpactAngle);
        staggerWobbleAngle = Mathf.Max(0f, staggerWobbleAngle);
        staggerWobbleCycles = Mathf.Max(0f, staggerWobbleCycles);
        staggerRecoverDuration = Mathf.Max(0f, staggerRecoverDuration);
        dodgeDistance = Mathf.Max(0f, dodgeDistance);
        dodgeHeight = Mathf.Max(0f, dodgeHeight);
        dodgeOutDuration = Mathf.Max(0f, dodgeOutDuration);
        dodgeHoldDuration = Mathf.Max(0f, dodgeHoldDuration);
        dodgeRecoverDuration = Mathf.Max(0f, dodgeRecoverDuration);
        balanceEnterDuration = Mathf.Max(0f, balanceEnterDuration);
        balanceWobbleTime = Mathf.Max(0f, balanceWobbleTime);
        balanceRecoverTime = Mathf.Max(0f, balanceRecoverTime);
    }

    public void SetPushDirection(Vector2 direction)
    {
        if (direction.sqrMagnitude <= 0f)
            return;

        pushDirection = direction.normalized;
    }

    public bool TryPlayAttack(Action impactCallback, float cooldownDuration)
    {
        Initialize();

        if (!CanStartMotion)
            return false;

        StartCoroutine(DoPush(impactCallback, cooldownDuration));
        return true;
    }

    public bool TryPlayFakeAttack()
    {
        Initialize();

        if (!CanFakeAttack)
            return false;

        attackFakeRequested = true;
        attackFakeWindowActive = false;
        attackClashWindowActive = false;
        attackResolutionComplete = true;
        attackFeinted = true;
        return true;
    }

    /// <summary>
    /// 팔을 feintPushDistance만큼 뻗었다가 회수하는 독립 페인트 모션.
    /// 기존 DoPush 취소 방식과 달리 뻗기→회수를 명확하게 재생한다.
    /// </summary>
    public bool TryPlayFeintAttack(float cooldownDuration)
    {
        Initialize();

        if (!CanStartMotion)
            return false;

        StartCoroutine(DoFeintAttack(cooldownDuration));
        return true;
    }

    public bool TryPlayDodge(Action finishedCallback, float cooldownDuration)
    {
        Initialize();

        if (!CanStartMotion)
            return false;

        StartCoroutine(DoDodge(finishedCallback, cooldownDuration));
        return true;
    }

    /// <summary>
    /// 휘청거림 모션 시작.
    /// duration &gt; 0 이면 해당 시간(초) 후 자동 종료.
    /// duration &lt;= 0 이면 StopStagger() 호출 시까지 무한 유지 (플레이어 QTE 연동용).
    /// leanForward = true 이면 공격 방향으로 앞으로 기움 (공격이 회피당한 경우).
    /// isPushing/isDodging/isBalancing 중에는 시작 불가. onCooldown은 무시(취소).
    /// </summary>
    public bool TryPlayStagger(float duration, bool leanForward = false, Action finishedCallback = null)
    {
        Initialize();

        if (isPushing || isDodging || isBalancing || isStaggered)
            return false;

        onCooldown = false; // 남은 쿨다운 취소, 스태거가 우선
        StartCoroutine(DoStagger(duration, leanForward, finishedCallback));
        return true;
    }

    /// <summary>무한 스태거를 외부에서 종료. 복귀 애니메이션은 자동 재생.</summary>
    public void StopStagger() => staggerStopped = true;

    public bool TryPlayBalanceDebug()
    {
        Initialize();

        if (!CanStartMotion)
            return false;

        StartCoroutine(DoBalanceDebug());
        return true;
    }

    public void MarkAttackResolutionComplete()
    {
        attackClashWindowActive = false;
        attackFakeWindowActive = false;
        attackResolutionComplete = true;
    }

    public void MarkAttackClashed()
    {
        attackClashWindowActive = false;
        attackFakeWindowActive = false;
        attackResolutionComplete = true;
        attackClashed = true;
    }

    void Initialize()
    {
        if (initialized)
            return;

        if (handPivot == null)
            handPivot = transform;

        if (handVisual == null)
            handVisual = handPivot;

        handParent = handPivot.parent;

        basePivotLocalPos = handPivot.localPosition;
        baseVisualLocalScale = handVisual.localScale;
        basePivotLocalRot = handPivot.localRotation;

        if (bodyPivot != null)
        {
            baseBodyLocalPos = bodyPivot.localPosition;
            baseBodyLocalScale = bodyPivot.localScale;
            baseBodyLocalRot = bodyPivot.localRotation;
        }

        initialized = true;
    }

    void UpdateIdleMotion()
    {
        idleTimer += Time.deltaTime;

        float bobY = Mathf.Sin(idleTimer * idleBobSpeed * Mathf.PI * 2f) * idleBobAmplitude;
        float swayZ = Mathf.Sin(idleTimer * idleBobSpeed * Mathf.PI * 2f * 0.65f) * idleSwayAngle;

        handPivot.SetLocalPositionAndRotation(
            basePivotLocalPos + new Vector3(0f, bobY, 0f),
            basePivotLocalRot * Quaternion.Euler(0f, 0f, swayZ));

        if (bodyPivot != null)
        {
            bodyPivot.SetLocalPositionAndRotation(
                baseBodyLocalPos + new Vector3(0f, bobY * 0.5f, 0f),
                baseBodyLocalRot * Quaternion.Euler(0f, 0f, swayZ * 0.4f));
        }
    }

    IEnumerator DoPush(Action impactCallback, float cooldownDuration)
    {
        isPushing = true;
        onCooldown = true;
        attackClashWindowActive = true;
        attackFakeWindowActive = true;
        attackFakeRequested = false;
        attackResolutionComplete = false;
        attackClashed = false;
        attackFeinted = false;

        Vector3 dir = (Vector3)pushDirection.normalized;
        Vector3 pushWorldDir = GetPushWorldDirection(dir);
        Quaternion baseRot = GetBaseHandWorldRotation();
        float leanSign = GetBodyLeanSign();
        float windupAngle = Mathf.Abs(windupLeanAngle) * -leanSign;
        float pushAngle = Mathf.Abs(pushLeanAngle) * leanSign;
        Vector3 idlePos = handPivot.position;
        Vector3 basePos = GetBaseHandWorldPosition();
        Vector3 windupPos = basePos - pushWorldDir * windupDistance;
        Vector3 peakPos = basePos + pushWorldDir * pushDistance;

        if (bodyPivot != null)
            bodyPivot.localRotation = baseBodyLocalRot;

        handPivot.rotation = baseRot;

        Vector3 bodyPeakPos = baseBodyLocalPos
            + dir * (pushDistance * bodyFollowRatio)
            + new Vector3(0f, -leanDropY, 0f);

        StartBodyLean(
            baseBodyLocalPos, baseBodyLocalPos,
            0f, windupAngle,
            windupDuration, EaseOutCubic);

        yield return LerpPivotAndScale(
            idlePos, windupPos,
            handVisual.localScale, baseVisualLocalScale * 0.9f,
            baseRot, windupDuration, EaseOutCubic,
            IsAttackFakeRequested);

        if (attackFakeRequested)
        {
            yield return PlayFakeAttackRecovery(basePos, baseRot, pushWorldDir, leanSign, cooldownDuration);
            yield break;
        }

        Vector3 stretchScale = new(
            baseVisualLocalScale.x * pushScaleMultiplier * pushStretchX,
            baseVisualLocalScale.y * pushScaleMultiplier * pushSquashY,
            baseVisualLocalScale.z * pushScaleMultiplier);

        StartBodyLean(
            baseBodyLocalPos, bodyPeakPos,
            windupAngle, pushAngle,
            pushOutDuration, EaseOutBack);

        yield return LerpPivotAndScale(
            windupPos, peakPos,
            handVisual.localScale, stretchScale,
            baseRot, pushOutDuration, EaseOutBack,
            IsAttackFakeRequested);

        if (attackFakeRequested)
        {
            yield return PlayFakeAttackRecovery(basePos, baseRot, pushWorldDir, leanSign, cooldownDuration);
            yield break;
        }

        attackFakeWindowActive = false;
        impactCallback?.Invoke();

        if (attackClashed)
        {
            yield return RecoverPushFromCurrentPose(basePos, baseRot, cooldownDuration);
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

        StartBodyLean(
            bodyPeakPos, baseBodyLocalPos,
            pushAngle, 0f,
            retractDuration, EaseInOutCubic);

        yield return LerpPivotAndScale(
            peakPos, basePos,
            handVisual.localScale, baseVisualLocalScale,
            baseRot, retractDuration, EaseInOutCubic);

        FinishPushState(basePos, baseRot);

        yield return new WaitForSeconds(Mathf.Max(0f, cooldownDuration));
        onCooldown = false;
    }

    IEnumerator RecoverPushFromCurrentPose(Vector3 basePos, Quaternion baseRot, float cooldownDuration)
    {
        StartBodyLean(
            bodyPivot != null ? bodyPivot.localPosition : baseBodyLocalPos,
            baseBodyLocalPos,
            bodyPivot != null ? GetBodyCurrentLocalAngle() : 0f,
            0f,
            retractDuration,
            EaseInOutCubic);

        yield return LerpPivotAndScale(
            handPivot.position, basePos,
            handVisual.localScale, baseVisualLocalScale,
            baseRot, retractDuration, EaseInOutCubic);

        FinishPushState(basePos, baseRot);

        yield return new WaitForSeconds(Mathf.Max(0f, cooldownDuration));
        onCooldown = false;
    }

    IEnumerator PlayFakeAttackRecovery(
        Vector3 basePos,
        Quaternion baseRot,
        Vector3 pushWorldDir,
        float leanSign,
        float cooldownDuration)
    {
        attackFakeWindowActive = false;
        attackClashWindowActive = false;
        attackResolutionComplete = true;
        attackFeinted = true;

        float fakeLeanAngle = Mathf.Abs(fakeBodyLeanAngle) * -leanSign;
        Vector3 currentPos = handPivot.position;
        Vector3 fakePullPos = basePos - pushWorldDir * fakePullBackDistance;
        Vector3 fakePullScale = baseVisualLocalScale * 0.82f;
        Quaternion fakePullRot = baseRot * Quaternion.Euler(0f, 0f, fakeLeanAngle);

        StartBodyLean(
            bodyPivot != null ? bodyPivot.localPosition : baseBodyLocalPos,
            baseBodyLocalPos,
            bodyPivot != null ? GetBodyCurrentLocalAngle() : 0f,
            fakeLeanAngle,
            fakePullBackDuration,
            EaseOutCubic);

        yield return LerpPivotScaleAndRotation(
            currentPos, fakePullPos,
            handVisual.localScale, fakePullScale,
            handPivot.rotation, fakePullRot,
            fakePullBackDuration,
            EaseOutCubic);

        StartBodyLean(
            bodyPivot != null ? bodyPivot.localPosition : baseBodyLocalPos,
            baseBodyLocalPos,
            bodyPivot != null ? GetBodyCurrentLocalAngle() : 0f,
            0f,
            fakeRecoverDuration,
            EaseInOutCubic);

        yield return LerpPivotScaleAndRotation(
            handPivot.position, basePos,
            handVisual.localScale, baseVisualLocalScale,
            handPivot.rotation, baseRot,
            fakeRecoverDuration,
            EaseInOutCubic);

        FinishPushState(basePos, baseRot);

        float cooldownAfterFake = Mathf.Max(0f, cooldownDuration * fakeCooldownMultiplier);
        yield return new WaitForSeconds(cooldownAfterFake);
        onCooldown = false;
    }

    IEnumerator DoFeintAttack(float cooldownDuration)
    {
        isPushing = true;
        onCooldown = true;
        attackResolutionComplete = true;
        attackFeinted = true;

        Vector3 basePos    = GetBaseHandWorldPosition();
        Quaternion baseRot = GetBaseHandWorldRotation();
        Vector3 pushDir    = GetPushWorldDirection((Vector3)pushDirection.normalized);
        Vector3 extendPos  = basePos + pushDir * feintPushDistance;

        float leanSign    = GetBodyLeanSign();
        float forwardLean = Mathf.Abs(feintBodyLeanAngle) * leanSign;   // 앞쪽 기울기
        float recoilLean  = Mathf.Abs(feintRecoilAngle)   * -leanSign;  // 뒤쪽 반동 기울기

        float snapDuration    = feintRetractDuration * feintRecoilPhaseRatio;
        float recoverDuration = feintRetractDuration * (1f - feintRecoilPhaseRatio);

        // ── 단계 1: 뻗기 ───────────────────────────────
        // 팔이 앞으로 나가고 몸통이 앞으로 기운다
        StartBodyLean(baseBodyLocalPos, baseBodyLocalPos,
            0f, forwardLean, feintExtendDuration, EaseOutCubic);
        yield return LerpPivotAndScale(
            handPivot.position, extendPos,
            handVisual.localScale, baseVisualLocalScale * 1.15f,
            baseRot, feintExtendDuration, EaseOutCubic);

        // ── 단계 2: 스냅백 ─────────────────────────────
        // 팔이 빠르게 돌아오고 몸통이 뒤로 반동하며 기운다
        StartBodyLean(baseBodyLocalPos, baseBodyLocalPos,
            forwardLean, recoilLean, snapDuration, EaseOutCubic);
        yield return LerpPivotAndScale(
            handPivot.position, basePos,
            handVisual.localScale, baseVisualLocalScale,
            baseRot, snapDuration, EaseOutCubic);

        // ── 단계 3: 복귀 ───────────────────────────────
        // 팔은 이미 basePos에 있고, 몸통만 천천히 중립으로 돌아온다
        StartBodyLean(baseBodyLocalPos, baseBodyLocalPos,
            recoilLean, 0f, recoverDuration, EaseInOutCubic);
        yield return new WaitForSeconds(recoverDuration);

        FinishPushState(basePos, baseRot);

        float cooldownAfterFeint = Mathf.Max(0f, cooldownDuration * fakeCooldownMultiplier);
        yield return new WaitForSeconds(cooldownAfterFeint);
        onCooldown = false;
    }

    void FinishPushState(Vector3 basePos, Quaternion baseRot)
    {
        handPivot.SetPositionAndRotation(basePos, baseRot);
        handVisual.localScale = baseVisualLocalScale;

        if (bodyPivot != null)
            bodyPivot.SetLocalPositionAndRotation(baseBodyLocalPos, baseBodyLocalRot);

        attackClashWindowActive = false;
        attackFakeWindowActive = false;
        attackFakeRequested = false;
        attackResolutionComplete = false;
        attackClashed = false;
        attackFeinted = false;
        isPushing = false;
    }

    IEnumerator DoDodge(Action finishedCallback, float cooldownDuration)
    {
        isDodging = true;
        onCooldown = true;

        Vector3 startPos = transform.position;
        Vector3 dodgeTarget = startPos + GetDodgeWorldDirection() * dodgeDistance;
        float dodgePeakAngle = Mathf.Abs(dodgeLeanAngle) * -GetBodyLeanSign();

        handPivot.SetLocalPositionAndRotation(basePivotLocalPos, basePivotLocalRot);
        handVisual.localScale = baseVisualLocalScale;

        float elapsed = 0f;
        while (elapsed < dodgeOutDuration)
        {
            elapsed += Time.deltaTime;

            float normalizedTime = Mathf.Clamp01(elapsed / dodgeOutDuration);
            float horizontalT = EaseOutCubic(normalizedTime);
            float arcHeight = Mathf.Sin(normalizedTime * Mathf.PI * 0.5f) * dodgeHeight;

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
            float horizontalT = EaseInOutCubic(normalizedTime);
            float verticalT = EaseInOutCubic(normalizedTime);

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

        // isDodging을 먼저 false로 설정해야 DodgeFailed 이벤트에서 즉시 stagger를 시작할 수 있다
        isDodging = false;
        finishedCallback?.Invoke();

        yield return new WaitForSeconds(Mathf.Max(0f, cooldownDuration));
        onCooldown = false;
    }

    IEnumerator DoStagger(float duration, bool leanForward, Action finishedCallback)
    {
        isStaggered = true;
        staggerStopped = false;

        Vector3 baseHandWorldPos = GetBaseHandWorldPosition();
        Quaternion baseHandWorldRot = GetBaseHandWorldRotation();

        if (bodyPivot == null)
        {
            isStaggered = false;
            finishedCallback?.Invoke();
            yield break;
        }

        float leanSign = GetBodyLeanSign();
        // leanForward: 공격 방향(앞) / 기본: 피격 방향(뒤)
        float impactAngle = Mathf.Abs(staggerImpactAngle) * (leanForward ? leanSign : -leanSign);

        // ── 단계 1: 충격 진입 (빠르게 뒤로 기울기) ─────────────────────
        float elapsed = 0f;
        while (elapsed < staggerEnterDuration)
        {
            elapsed += Time.deltaTime;
            float t = EaseOutCubic(Mathf.Clamp01(elapsed / staggerEnterDuration));
            bodyPivot.SetLocalPositionAndRotation(
                baseBodyLocalPos,
                baseBodyLocalRot * Quaternion.Euler(0f, 0f, Mathf.Lerp(0f, impactAngle, t)));
            yield return null;
        }

        // ── 단계 2: 휘청거림 루프 ────────────────────────────────────────
        // duration <= 0 이면 StopStagger() 호출 시까지 무한 유지
        // duration > 0 이면 남은 시간(enter/recover 제외) 동안만 유지
        float wobbleDuration = duration > 0f
            ? Mathf.Max(0f, duration - staggerEnterDuration - staggerRecoverDuration)
            : float.PositiveInfinity;

        elapsed = 0f;
        while (!staggerStopped && elapsed < wobbleDuration)
        {
            elapsed += Time.deltaTime;
            float wobble = Mathf.Sin(elapsed * staggerWobbleCycles * Mathf.PI * 2f) * staggerWobbleAngle;
            bodyPivot.SetLocalPositionAndRotation(
                baseBodyLocalPos,
                baseBodyLocalRot * Quaternion.Euler(0f, 0f, impactAngle + wobble));
            yield return null;
        }

        // ── 단계 3: 중립 복귀 ────────────────────────────────────────────
        float currentAngle = GetBodyCurrentLocalAngle();
        elapsed = 0f;
        while (elapsed < staggerRecoverDuration)
        {
            elapsed += Time.deltaTime;
            float t = EaseInOutCubic(Mathf.Clamp01(elapsed / staggerRecoverDuration));
            bodyPivot.SetLocalPositionAndRotation(
                baseBodyLocalPos,
                baseBodyLocalRot * Quaternion.Euler(0f, 0f, Mathf.Lerp(currentAngle, 0f, t)));
            yield return null;
        }

        handPivot.SetPositionAndRotation(baseHandWorldPos, baseHandWorldRot);
        bodyPivot.SetLocalPositionAndRotation(baseBodyLocalPos, baseBodyLocalRot);
        isStaggered = false;
        staggerStopped = false;
        finishedCallback?.Invoke();
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
        float wobbleAngle = Mathf.Abs(balanceWobbleAngle);

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
            float damping = 1f - normalizedTime;
            float wobbleOffset = Mathf.Sin(normalizedTime * Mathf.PI * 2f * balanceWobbleCycles)
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

    float GetBodyCurrentLocalAngle()
    {
        if (bodyPivot == null)
            return 0f;

        return Mathf.DeltaAngle(baseBodyLocalRot.eulerAngles.z, bodyPivot.localRotation.eulerAngles.z);
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

    void StartBodyLean(
        Vector3 fromPos,
        Vector3 toPos,
        float fromAngle,
        float toAngle,
        float duration,
        Func<float, float> ease)
    {
        if (bodyLeanRoutine != null)
            StopCoroutine(bodyLeanRoutine);

        bodyLeanRoutine = StartCoroutine(LerpBodyLean(
            fromPos,
            toPos,
            fromAngle,
            toAngle,
            duration,
            ease));
    }

    bool IsAttackFakeRequested()
    {
        return attackFakeRequested;
    }

    IEnumerator LerpBodyLean(
        Vector3 fromPos,
        Vector3 toPos,
        float fromAngle,
        float toAngle,
        float duration,
        Func<float, float> ease)
    {
        if (bodyPivot == null)
            yield break;

        if (duration <= 0f)
        {
            bodyPivot.SetLocalPositionAndRotation(
                toPos,
                baseBodyLocalRot * Quaternion.Euler(0f, 0f, toAngle));
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = ease(Mathf.Clamp01(elapsed / duration));
            float angle = Mathf.Lerp(fromAngle, toAngle, t);
            bodyPivot.SetLocalPositionAndRotation(
                Vector3.Lerp(fromPos, toPos, t),
                baseBodyLocalRot * Quaternion.Euler(0f, 0f, angle));
            yield return null;
        }
    }

    IEnumerator LerpPivotAndScale(
        Vector3 fromPos,
        Vector3 toPos,
        Vector3 fromScale,
        Vector3 toScale,
        Quaternion worldRotation,
        float duration,
        Func<float, float> ease,
        Func<bool> shouldStop = null)
    {
        if (duration <= 0f)
        {
            handPivot.SetPositionAndRotation(toPos, worldRotation);
            handVisual.localScale = toScale;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (shouldStop != null && shouldStop())
                yield break;

            elapsed += Time.deltaTime;
            float t = ease(Mathf.Clamp01(elapsed / duration));
            handPivot.SetPositionAndRotation(
                Vector3.Lerp(fromPos, toPos, t),
                worldRotation);
            handVisual.localScale = Vector3.Lerp(fromScale, toScale, t);
            yield return null;
        }
    }

    IEnumerator LerpPivotScaleAndRotation(
        Vector3 fromPos,
        Vector3 toPos,
        Vector3 fromScale,
        Vector3 toScale,
        Quaternion fromRotation,
        Quaternion toRotation,
        float duration,
        Func<float, float> ease)
    {
        if (duration <= 0f)
        {
            handPivot.SetPositionAndRotation(toPos, toRotation);
            handVisual.localScale = toScale;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = ease(Mathf.Clamp01(elapsed / duration));
            handPivot.SetPositionAndRotation(
                Vector3.Lerp(fromPos, toPos, t),
                Quaternion.Slerp(fromRotation, toRotation, t));
            handVisual.localScale = Vector3.Lerp(fromScale, toScale, t);
            yield return null;
        }
    }

    static float EaseOutCubic(float t) =>
        1f - Mathf.Pow(1f - t, 3f);

    static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }

    static float EaseInOutCubic(float t) =>
        t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
}
