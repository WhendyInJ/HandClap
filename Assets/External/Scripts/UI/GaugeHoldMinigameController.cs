using System;
using UnityEngine;
using UnityEngine.UI;

public enum GaugeTargetSpawnMode
{
    Fixed,
    Random,
    LeftOnly,
    RightOnly,
    AlternatingSides,
}

enum GaugeVisualSide
{
    None,
    Left,
    Right,
}

[Serializable]
public struct GaugeTargetRange
{
    [Range(0f, 1f)] public float min;
    [Range(0f, 1f)] public float max;

    public float Center => (min + max) * 0.5f;
    public float Size => Mathf.Max(0f, max - min);

    public bool Contains(float normalized)
    {
        return normalized >= min && normalized <= max;
    }
}

[DisallowMultipleComponent]
public class GaugeHoldMinigameController : PlayerStaggerMinigameController
{
    [Header("UI")]
    [SerializeField] private GameObject root;
    [SerializeField] private GaugeUI gauge;
    [SerializeField] private RectTransform targetRoot;
    [SerializeField] private RectTransform targetPrefab;
    [SerializeField] private Image failGaugeFill;
    [SerializeField] private Image recoverGaugeFill;
    [SerializeField] private Image maxHpCapFill;
    [SerializeField] private bool hideWhenInactive = true;

    [Header("Rules")]
    [Min(0f)] [SerializeField] private float failDrainPerSecond = 0.35f;
    [Min(0f)] [SerializeField] private float recoverPerSecond = 0.65f;
    [Range(0.02f, 0.5f)] [SerializeField] private float targetWidth = 0.16f;
    [Range(0f, 1f)] [SerializeField] private float fixedTargetCenter = 0.5f;
    [SerializeField] private GaugeTargetSpawnMode spawnMode = GaugeTargetSpawnMode.Random;
    [SerializeField] private bool resetNeedleOnStart = true;
    [Range(0f, 1f)] [SerializeField] private float initialNeedleNormalized = 0f;

    [Header("Target View")]
    [Min(0f)] [SerializeField] private float targetRadius = 120f;
    [Tooltip("타겟 배치에 적용할 반지름 오프셋입니다. 음수는 안쪽, 양수는 바깥쪽으로 이동합니다.")]
    [SerializeField] private float targetRadiusOffset = 0f;
    [Min(1f)] [SerializeField] private float minTargetViewWidth = 24f;
    [Tooltip("Needle 이미지가 Z 회전 0도일 때 가리키는 UI 로컬 방향입니다.")]
    [SerializeField] private Vector2 directionAtZeroDegrees = Vector2.up;

    [Header("Editor Debug")]
    [SerializeField] private bool drawEditorDebug = true;
    [SerializeField] private bool drawDebugOnlyWhenSelected = true;
    [SerializeField] private GaugeTargetRange editorPreviewTarget = new GaugeTargetRange { min = 0.42f, max = 0.58f };
    [SerializeField] private Color debugSweepColor = new Color(1f, 1f, 1f, 0.35f);
    [SerializeField] private Color debugTargetColor = new Color(0.2f, 1f, 0.25f, 0.8f);
    [SerializeField] private Color debugNeedleColor = new Color(1f, 0.2f, 0.2f, 0.9f);

    private GaugeTargetRange currentTarget;
    private RectTransform currentTargetView;
    private float currentFailGauge;
    private float currentRecoverGauge;
    private float maxHpNormalized = 1f;
    private float difficultyMultiplier = 1f;
    private bool isActive;
    private bool isSuppressed;
    private bool nextTargetOnRight;

    public override bool IsActive => isActive;
    public override float FailGaugeNormalized => currentFailGauge;
    public override float RecoverGaugeNormalized => currentRecoverGauge;
    public GaugeTargetRange CurrentTarget => currentTarget;

    void Reset()
    {
        if (gauge == null)
            gauge = GetComponentInChildren<GaugeUI>(true);
    }

    void OnValidate()
    {
        failDrainPerSecond = Mathf.Max(0f, failDrainPerSecond);
        recoverPerSecond = Mathf.Max(0f, recoverPerSecond);
        targetWidth = Mathf.Clamp(targetWidth, 0.02f, 0.5f);
        fixedTargetCenter = Mathf.Clamp01(fixedTargetCenter);
        initialNeedleNormalized = Mathf.Clamp01(initialNeedleNormalized);
        targetRadius = Mathf.Max(0f, targetRadius);
        minTargetViewWidth = Mathf.Max(1f, minTargetViewWidth);
        if (directionAtZeroDegrees.sqrMagnitude <= 0.0001f)
            directionAtZeroDegrees = Vector2.up;
        else
            directionAtZeroDegrees.Normalize();
        editorPreviewTarget.min = Mathf.Clamp01(editorPreviewTarget.min);
        editorPreviewTarget.max = Mathf.Clamp01(editorPreviewTarget.max);

        if (editorPreviewTarget.max < editorPreviewTarget.min)
            editorPreviewTarget.max = editorPreviewTarget.min;

        if (!Application.isPlaying)
            UpdateUi();
    }

    void OnDisable()
    {
        isActive = false;
        ApplyVisibility();
    }

    void Update()
    {
        if (!isActive)
            return;

        UpdateGaugeState();
    }

    public override void StartMinigame(PlayerStaggerMinigameContext context)
    {
        currentFailGauge = Mathf.Clamp01(context.failGaugeNormalized);
        currentRecoverGauge = Mathf.Clamp01(context.recoverGaugeNormalized);
        maxHpNormalized = Mathf.Clamp01(context.maxHpNormalized);
        difficultyMultiplier = Mathf.Max(0.01f, context.difficultyMultiplier);
        isActive = true;

        if (gauge != null && (resetNeedleOnStart || context.gaugeStartLayout != PlayerStaggerGaugeStartLayout.Default))
            gauge.SetNeedleImmediate(GetInitialNeedleNormalized(context.gaugeStartLayout));

        SpawnTarget(CreateTargetRange(context.gaugeStartLayout));
        UpdateUi();
        ApplyVisibility();
        RaiseGaugeChanged(currentFailGauge, currentRecoverGauge);

        if (currentFailGauge <= 0f)
            StopMinigame(QteEndReason.Fail);
    }

    public override void StopMinigame(QteEndReason endReason)
    {
        if (!isActive)
            return;

        isActive = false;

        if (endReason == QteEndReason.Success)
            currentFailGauge = maxHpNormalized;

        UpdateUi();
        ApplyVisibility();
        RaiseGaugeChanged(currentFailGauge, currentRecoverGauge);
        RaiseMinigameEnded(endReason);
    }

    public override void SetSuppressed(bool suppressed)
    {
        isSuppressed = suppressed;
        ApplyVisibility();
    }

    public override void ApplyIdleState(float failGaugeNormalized, float recoverGaugeNormalized, float maxHpNormalized)
    {
        if (isActive)
            return;

        currentFailGauge = Mathf.Clamp01(failGaugeNormalized);
        currentRecoverGauge = Mathf.Clamp01(recoverGaugeNormalized);
        this.maxHpNormalized = Mathf.Clamp01(maxHpNormalized);
        UpdateUi();
        ApplyVisibility();
    }

    void UpdateGaugeState()
    {
        bool isRecovering = gauge != null && currentTarget.Contains(gauge.NormalizedNeedle);

        if (isRecovering)
            currentRecoverGauge = Mathf.MoveTowards(currentRecoverGauge, 1f, GetEffectiveRecoverPerSecond() * Time.deltaTime);
        else
            currentFailGauge = Mathf.MoveTowards(currentFailGauge, 0f, GetEffectiveFailDrainPerSecond() * Time.deltaTime);

        UpdateUi();
        RaiseGaugeChanged(currentFailGauge, currentRecoverGauge);

        if (currentRecoverGauge >= 1f)
            StopMinigame(QteEndReason.Success);
        else if (currentFailGauge <= 0f)
            StopMinigame(QteEndReason.Fail);
    }

    GaugeTargetRange CreateTargetRange(PlayerStaggerGaugeStartLayout gaugeStartLayout)
    {
        float width = GetEffectiveTargetWidth();
        float halfWidth = width * 0.5f;
        float center = GetTargetCenter(halfWidth, GetTargetSideOverride(gaugeStartLayout));

        return new GaugeTargetRange
        {
            min = Mathf.Clamp01(center - halfWidth),
            max = Mathf.Clamp01(center + halfWidth),
        };
    }

    float GetTargetCenter(float halfWidth, GaugeVisualSide sideOverride)
    {
        float minCenter = halfWidth;
        float maxCenter = 1f - halfWidth;

        if (sideOverride != GaugeVisualSide.None)
            return GetRandomCenterOnVisualSide(halfWidth, sideOverride);

        switch (spawnMode)
        {
            case GaugeTargetSpawnMode.Fixed:
                return Mathf.Clamp(fixedTargetCenter, minCenter, maxCenter);

            case GaugeTargetSpawnMode.LeftOnly:
                return GetRandomCenterOnVisualSide(halfWidth, GaugeVisualSide.Left);

            case GaugeTargetSpawnMode.RightOnly:
                return GetRandomCenterOnVisualSide(halfWidth, GaugeVisualSide.Right);

            case GaugeTargetSpawnMode.AlternatingSides:
                nextTargetOnRight = !nextTargetOnRight;
                return nextTargetOnRight
                    ? GetRandomCenterOnVisualSide(halfWidth, GaugeVisualSide.Right)
                    : GetRandomCenterOnVisualSide(halfWidth, GaugeVisualSide.Left);

            default:
                return UnityEngine.Random.Range(minCenter, maxCenter);
        }
    }

    float GetRandomCenterOnVisualSide(float halfWidth, GaugeVisualSide side)
    {
        bool lowNormalizedSideIsLeft = IsLowNormalizedSideVisualLeft(halfWidth);
        bool useLowNormalizedSide = side == GaugeVisualSide.Left
            ? lowNormalizedSideIsLeft
            : !lowNormalizedSideIsLeft;

        return GetRandomCenterOnNormalizedSide(halfWidth, useLowNormalizedSide);
    }

    float GetRandomCenterOnNormalizedSide(float halfWidth, bool useLowNormalizedSide)
    {
        float minCenter = halfWidth;
        float maxCenter = 1f - halfWidth;

        if (useLowNormalizedSide)
            return UnityEngine.Random.Range(minCenter, Mathf.Max(minCenter, Mathf.Min(maxCenter, 0.35f)));

        return UnityEngine.Random.Range(Mathf.Min(maxCenter, Mathf.Max(minCenter, 0.65f)), maxCenter);
    }

    bool IsLowNormalizedSideVisualLeft(float halfWidth)
    {
        if (gauge == null)
            return true;

        float lowNormalized = halfWidth;
        float highNormalized = 1f - halfWidth;
        float lowX = GetLocalDirectionForNormalized(lowNormalized).x;
        float highX = GetLocalDirectionForNormalized(highNormalized).x;
        return lowX <= highX;
    }

    GaugeVisualSide GetTargetSideOverride(PlayerStaggerGaugeStartLayout gaugeStartLayout)
    {
        switch (gaugeStartLayout)
        {
            case PlayerStaggerGaugeStartLayout.TargetLeftNeedleRight:
                return GaugeVisualSide.Left;

            case PlayerStaggerGaugeStartLayout.TargetRightNeedleLeft:
                return GaugeVisualSide.Right;

            default:
                return GaugeVisualSide.None;
        }
    }

    float GetInitialNeedleNormalized(PlayerStaggerGaugeStartLayout gaugeStartLayout)
    {
        switch (gaugeStartLayout)
        {
            case PlayerStaggerGaugeStartLayout.TargetLeftNeedleRight:
                return GetNeedleNormalizedForVisualSide(GaugeVisualSide.Right);

            case PlayerStaggerGaugeStartLayout.TargetRightNeedleLeft:
                return GetNeedleNormalizedForVisualSide(GaugeVisualSide.Left);

            default:
                return initialNeedleNormalized;
        }
    }

    float GetNeedleNormalizedForVisualSide(GaugeVisualSide side)
    {
        if (side == GaugeVisualSide.None || gauge == null)
            return initialNeedleNormalized;

        float zeroX = GetLocalDirectionForNormalized(0f).x;
        float oneX = GetLocalDirectionForNormalized(1f).x;
        bool zeroIsLeft = zeroX <= oneX;

        return side == GaugeVisualSide.Left
            ? (zeroIsLeft ? 0f : 1f)
            : (zeroIsLeft ? 1f : 0f);
    }

    void SpawnTarget(GaugeTargetRange range)
    {
        currentTarget = range;

        if (currentTargetView != null)
            Destroy(currentTargetView.gameObject);

        if (targetPrefab == null || targetRoot == null || gauge == null)
            return;

        currentTargetView = Instantiate(targetPrefab, targetRoot);
        ConfigureTargetRect(currentTargetView, targetRoot.pivot);

        float centerAngle = gauge.GetAngleForNormalized(range.Center);
        float targetViewRadius = GetTargetViewRadius();
        currentTargetView.anchoredPosition = AngleToPosition(centerAngle, targetViewRadius);
        currentTargetView.localRotation = Quaternion.Euler(0f, 0f, centerAngle);

        float minAngle = gauge.GetAngleForNormalized(range.min);
        float maxAngle = gauge.GetAngleForNormalized(range.max);
        float arcWidth = Mathf.Abs(Mathf.DeltaAngle(minAngle, maxAngle)) * Mathf.Deg2Rad * targetViewRadius;
        Vector2 size = currentTargetView.sizeDelta;
        size.x = Mathf.Max(minTargetViewWidth, arcWidth);
        currentTargetView.sizeDelta = size;
    }

    static void ConfigureTargetRect(RectTransform target, Vector2 parentPivot)
    {
        if (target == null)
            return;

        target.anchorMin = parentPivot;
        target.anchorMax = parentPivot;
        target.pivot = new Vector2(0.5f, 0.5f);
        target.localScale = Vector3.one;
        target.anchoredPosition3D = Vector3.zero;
    }

    void UpdateUi()
    {
        if (failGaugeFill != null)
            failGaugeFill.fillAmount = currentFailGauge;

        if (recoverGaugeFill != null)
            recoverGaugeFill.fillAmount = currentRecoverGauge;

        if (maxHpCapFill != null)
            maxHpCapFill.fillAmount = maxHpNormalized;
    }

    void ApplyVisibility()
    {
        if (root == null || !Application.isPlaying)
            return;

        root.SetActive(!isSuppressed && (isActive || !hideWhenInactive));
    }

    float GetEffectiveTargetWidth()
    {
        return Mathf.Clamp(targetWidth / difficultyMultiplier, 0.02f, 1f);
    }

    float GetEffectiveFailDrainPerSecond()
    {
        return failDrainPerSecond * difficultyMultiplier;
    }

    float GetEffectiveRecoverPerSecond()
    {
        return recoverPerSecond / difficultyMultiplier;
    }

    float GetTargetViewRadius()
    {
        return Mathf.Max(0f, targetRadius + targetRadiusOffset);
    }

    void OnDrawGizmos()
    {
        if (!drawEditorDebug || drawDebugOnlyWhenSelected)
            return;

        DrawDebugGizmos();
    }

    void OnDrawGizmosSelected()
    {
        if (!drawEditorDebug)
            return;

        DrawDebugGizmos();
    }

    void DrawDebugGizmos()
    {
        if (gauge == null || targetRoot == null)
            return;

        GaugeTargetRange debugTarget = Application.isPlaying && isActive
            ? currentTarget
            : editorPreviewTarget;

        DrawGaugeRay(gauge.GetAngleForNormalized(0f), debugSweepColor);
        DrawGaugeRay(gauge.GetAngleForNormalized(1f), debugSweepColor);
        DrawGaugeArc(0f, 1f, debugSweepColor, 32);

        DrawGaugeArc(debugTarget.min, debugTarget.max, debugTargetColor, 12);
        DrawGaugeRay(gauge.GetAngleForNormalized(debugTarget.min), debugTargetColor);
        DrawGaugeRay(gauge.GetAngleForNormalized(debugTarget.max), debugTargetColor);
        DrawGaugePoint(debugTarget.Center, debugTargetColor, 0.025f);

        float needleNormalized = Application.isPlaying && gauge != null
            ? gauge.NormalizedNeedle
            : initialNeedleNormalized;
        DrawGaugeRay(gauge.GetAngleForNormalized(needleNormalized), debugNeedleColor);
        DrawGaugePoint(needleNormalized, debugNeedleColor, 0.02f);
    }

    void DrawGaugeRay(float angle, Color color)
    {
        Vector3 center = targetRoot.position;
        Vector3 point = GetWorldPointOnGauge(angle);

        Gizmos.color = color;
        Gizmos.DrawLine(center, point);
    }

    void DrawGaugeArc(float minNormalized, float maxNormalized, Color color, int segments)
    {
        if (segments <= 0)
            return;

        Gizmos.color = color;
        Vector3 previous = GetWorldPointForNormalized(minNormalized);
        for (int i = 1; i <= segments; i++)
        {
            float t = Mathf.Lerp(minNormalized, maxNormalized, i / (float)segments);
            Vector3 next = GetWorldPointForNormalized(t);
            Gizmos.DrawLine(previous, next);
            previous = next;
        }
    }

    void DrawGaugePoint(float normalized, Color color, float radiusScale)
    {
        Gizmos.color = color;
        Gizmos.DrawSphere(GetWorldPointForNormalized(normalized), GetWorldDebugRadius(radiusScale));
    }

    Vector3 GetWorldPointForNormalized(float normalized)
    {
        return GetWorldPointOnGauge(gauge.GetAngleForNormalized(Mathf.Clamp01(normalized)));
    }

    Vector2 GetLocalDirectionForNormalized(float normalized)
    {
        float angle = gauge != null ? gauge.GetAngleForNormalized(Mathf.Clamp01(normalized)) : 0f;
        return AngleToPosition(angle, 1f);
    }

    Vector3 GetWorldPointOnGauge(float angle)
    {
        Vector2 localPoint = AngleToPosition(angle, GetTargetViewRadius());
        return targetRoot.TransformPoint(localPoint);
    }

    float GetWorldDebugRadius(float radiusScale)
    {
        Vector3 center = targetRoot.position;
        Vector3 edge = targetRoot.TransformPoint(AngleToPosition(0f, GetTargetViewRadius()));
        return Mathf.Max(0.01f, Vector3.Distance(center, edge) * radiusScale);
    }

    Vector2 AngleToPosition(float angle, float radius)
    {
        Vector2 baseDirection = directionAtZeroDegrees.sqrMagnitude > 0.0001f
            ? directionAtZeroDegrees.normalized
            : Vector2.up;

        return Quaternion.Euler(0f, 0f, angle) * baseDirection * radius;
    }
}
