using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class SlideQteMinigameController : PlayerStaggerMinigameController
{
    [Header("UI")]
    [SerializeField] private GameObject root;
    [SerializeField] private Image failGaugeFill;
    [SerializeField] private Image recoverGaugeFill;
    [SerializeField] private Image maxHpCapFill;
    [SerializeField] private RectTransform track;
    [SerializeField] private RectTransform movingBar;
    [SerializeField] private RectTransform inputBox;
    [SerializeField] private bool hideWhenInactive = false;

    [Header("Input")]
    [SerializeField] private KeyCode moveLeftKey = KeyCode.LeftArrow;
    [SerializeField] private KeyCode moveRightKey = KeyCode.RightArrow;

    [Header("Rules")]
    [Min(0f)] [SerializeField] private float movingBarSpeed = 520f;
    [Min(0f)] [SerializeField] private float inputBoxSpeed = 520f;
    [Range(0.25f, 1.5f)] [SerializeField] private float overlapTolerance = 1f;
    [Min(0f)] [SerializeField] private float failDrainPerSecond = 0.35f;
    [Min(0f)] [SerializeField] private float recoverPerSecond = 0.65f;

    private float currentFailGauge;
    private float currentRecoverGauge;
    private float maxHpNormalized = 1f;
    private float difficultyMultiplier = 1f;
    private float movingBarDirection = 1f;
    private bool isActive;
    private bool isSuppressed;

    public override bool IsActive => isActive;
    public override float FailGaugeNormalized => currentFailGauge;
    public override float RecoverGaugeNormalized => currentRecoverGauge;

    void OnValidate()
    {
        movingBarSpeed = Mathf.Max(0f, movingBarSpeed);
        inputBoxSpeed = Mathf.Max(0f, inputBoxSpeed);
        failDrainPerSecond = Mathf.Max(0f, failDrainPerSecond);
        recoverPerSecond = Mathf.Max(0f, recoverPerSecond);

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

        UpdateMovingBar();
        UpdateInputBox();
        UpdateGaugeState();
    }

    public override void StartMinigame(PlayerStaggerMinigameContext context)
    {
        currentFailGauge = Mathf.Clamp01(context.failGaugeNormalized);
        currentRecoverGauge = Mathf.Clamp01(context.recoverGaugeNormalized);
        maxHpNormalized = Mathf.Clamp01(context.maxHpNormalized);
        difficultyMultiplier = Mathf.Max(0.01f, context.difficultyMultiplier);
        movingBarDirection = 1f;
        isActive = true;

        ResetPositions();
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
        bool isRecovering = IsSuccessState();

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

    void UpdateMovingBar()
    {
        if (track == null || movingBar == null)
            return;

        float maxX = GetMovementLimit(movingBar);
        float nextX = movingBar.anchoredPosition.x + movingBarDirection * GetEffectiveMovingBarSpeed() * Time.deltaTime;

        if (nextX > maxX)
        {
            nextX = maxX;
            movingBarDirection = -1f;
        }
        else if (nextX < -maxX)
        {
            nextX = -maxX;
            movingBarDirection = 1f;
        }

        SetAnchoredX(movingBar, nextX);
    }

    void UpdateInputBox()
    {
        if (track == null || inputBox == null)
            return;

        float input = 0f;
        if (Input.GetKey(moveLeftKey))
            input -= 1f;
        if (Input.GetKey(moveRightKey))
            input += 1f;

        float maxX = GetMovementLimit(inputBox);
        float nextX = inputBox.anchoredPosition.x + input * GetEffectiveInputBoxSpeed() * Time.deltaTime;
        SetAnchoredX(inputBox, Mathf.Clamp(nextX, -maxX, maxX));
    }

    bool IsSuccessState()
    {
        if (movingBar == null || inputBox == null)
            return false;

        float distance = Mathf.Abs(movingBar.anchoredPosition.x - inputBox.anchoredPosition.x);
        float overlapRange = (GetRectWidth(movingBar) + GetRectWidth(inputBox))
            * 0.5f
            * GetEffectiveOverlapTolerance();

        return distance <= overlapRange;
    }

    void ResetPositions()
    {
        SetAnchoredX(movingBar, 0f);
        SetAnchoredX(inputBox, 0f);
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

    float GetEffectiveMovingBarSpeed()
    {
        return movingBarSpeed * difficultyMultiplier;
    }

    float GetEffectiveInputBoxSpeed()
    {
        return inputBoxSpeed / difficultyMultiplier;
    }

    float GetEffectiveOverlapTolerance()
    {
        return Mathf.Max(0.01f, overlapTolerance / difficultyMultiplier);
    }

    float GetEffectiveFailDrainPerSecond()
    {
        return failDrainPerSecond * difficultyMultiplier;
    }

    float GetEffectiveRecoverPerSecond()
    {
        return recoverPerSecond / difficultyMultiplier;
    }

    float GetMovementLimit(RectTransform target)
    {
        if (track == null || target == null)
            return 0f;

        float trackHalfWidth = GetRectWidth(track) * 0.5f;
        float targetHalfWidth = GetRectWidth(target) * 0.5f;
        return Mathf.Max(0f, trackHalfWidth - targetHalfWidth);
    }

    static float GetRectWidth(RectTransform rectTransform)
    {
        return rectTransform == null ? 0f : rectTransform.rect.width;
    }

    static void SetAnchoredX(RectTransform rectTransform, float value)
    {
        if (rectTransform == null)
            return;

        Vector2 anchoredPosition = rectTransform.anchoredPosition;
        anchoredPosition.x = value;
        rectTransform.anchoredPosition = anchoredPosition;
    }
}
