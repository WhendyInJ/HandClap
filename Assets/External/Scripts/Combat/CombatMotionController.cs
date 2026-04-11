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

    [Header("Dodge Motion")]
    [SerializeField] private float dodgeDistance = 1.2f;
    [SerializeField] private float dodgeHeight = 0.35f;
    [SerializeField] private float dodgeLeanAngle = 18f;
    [SerializeField] private float dodgeOutDuration = 0.14f;
    [SerializeField] private float dodgeHoldDuration = 0.08f;
    [SerializeField] private float dodgeRecoverDuration = 0.22f;

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
    private bool onCooldown;
    private bool attackClashWindowActive;
    private bool attackResolutionComplete;
    private bool attackClashed;
    private bool initialized;

    CombatActorController ownerActor;

    public bool IsPushing => isPushing;
    public bool IsDodging => isDodging;
    public bool IsBalancing => isBalancing;
    public bool IsOnCooldown => onCooldown;
    public bool CanStartMotion => !isPushing && !isDodging && !isBalancing && !onCooldown;
    public bool IsAttackClashWindowActive => isPushing && attackClashWindowActive && !attackResolutionComplete;
    public bool IsAttackResolutionComplete => attackResolutionComplete;
    public float BaseCooldown => cooldown;

    void Awake()
    {
        Initialize();
    }

    void Update()
    {
        Initialize();

        if (ownerActor != null
            && !ownerActor.RoundCombatActive
            && !ownerActor.AllowsIdleMotionWhileInactive)
            return;

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

    public bool TryPlayDodge(Action finishedCallback, float cooldownDuration)
    {
        Initialize();

        if (!CanStartMotion)
            return false;

        StartCoroutine(DoDodge(finishedCallback, cooldownDuration));
        return true;
    }

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
        attackResolutionComplete = true;
    }

    public void MarkAttackClashed()
    {
        attackClashWindowActive = false;
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

        if (ownerActor == null)
            TryGetComponent(out ownerActor);

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
        attackResolutionComplete = false;
        attackClashed = false;

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

        StartCoroutine(LerpBodyLean(
            baseBodyLocalPos, baseBodyLocalPos,
            0f, windupAngle,
            windupDuration, EaseOutCubic));

        yield return LerpPivotAndScale(
            idlePos, windupPos,
            handVisual.localScale, baseVisualLocalScale * 0.9f,
            baseRot, windupDuration, EaseOutCubic);

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

        StartCoroutine(LerpBodyLean(
            bodyPeakPos, baseBodyLocalPos,
            pushAngle, 0f,
            retractDuration, EaseInOutCubic));

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
        StartCoroutine(LerpBodyLean(
            bodyPivot != null ? bodyPivot.localPosition : baseBodyLocalPos,
            baseBodyLocalPos,
            bodyPivot != null ? GetBodyCurrentLocalAngle() : 0f,
            0f,
            retractDuration,
            EaseInOutCubic));

        yield return LerpPivotAndScale(
            handPivot.position, basePos,
            handVisual.localScale, baseVisualLocalScale,
            baseRot, retractDuration, EaseInOutCubic);

        FinishPushState(basePos, baseRot);

        yield return new WaitForSeconds(Mathf.Max(0f, cooldownDuration));
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

        finishedCallback?.Invoke();
        isDodging = false;

        yield return new WaitForSeconds(Mathf.Max(0f, cooldownDuration));
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
        Func<float, float> ease)
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
            elapsed += Time.deltaTime;
            float t = ease(Mathf.Clamp01(elapsed / duration));
            handPivot.SetPositionAndRotation(
                Vector3.Lerp(fromPos, toPos, t),
                worldRotation);
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
