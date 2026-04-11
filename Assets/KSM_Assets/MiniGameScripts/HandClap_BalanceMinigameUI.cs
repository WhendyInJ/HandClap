using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 손바닥 밀치기용 균형 미니게임 UI를 실제 화면에 반영하는 스크립트.
///
/// 이번 수정의 핵심:
/// 1. SafeZone 시작 폭을 더 이상 현재 RectTransform 의 보이는 폭에서 추정하지 않는다.
/// 2. SafeZone 시작 폭은 Inspector 의 safeZoneBaseWidthInPixels 값을 절대 기준으로 사용한다.
/// 3. 그 폭을 바탕으로 컨트롤러의 시작 SafeZone 비율도 같이 동기화한다.
/// 4. 이후 실제 SafeZone 표시 위치와 폭은 controller.SafeZoneStart01 / End01 로 그린다.
/// 5. 즉, "보이는 SafeZone 크기"와 "실제 판정 SafeZone 크기"를 같은 기준으로 맞춘다.
/// </summary>
public class HandClap_BalanceMinigameUI : MonoBehaviour
{
    [Header("컨트롤러 참조")]

    /// <summary>
    /// 실제 미니게임 로직을 들고 있는 컨트롤러 참조.
    /// </summary>
    [SerializeField] private HandClap_BalanceMinigameController controller = null;

    [Header("전체 루트")]

    /// <summary>
    /// 미니게임 UI 중 실제로 보였다/숨겨질 비주얼 루트.
    /// </summary>
    [SerializeField] private GameObject uiRoot = null;

    /// <summary>
    /// 미니게임이 진행 중이 아닐 때 UI를 자동으로 숨길지 여부.
    /// </summary>
    [SerializeField] private bool hideWhenNotPlaying = true;

    [Header("공용 이동 기준점 - BalanceRoot 기준")]

    /// <summary>
    /// SafeZone / BalanceBar 가 공용으로 사용할 시작 기준점.
    /// BalanceRoot 아래의 왼쪽 기준점을 연결한다.
    /// </summary>
    [SerializeField] private RectTransform balanceLineStartPoint = null;

    /// <summary>
    /// SafeZone / BalanceBar 가 공용으로 사용할 끝 기준점.
    /// BalanceRoot 아래의 오른쪽 기준점을 연결한다.
    /// </summary>
    [SerializeField] private RectTransform balanceLineEndPoint = null;

    [Header("실제 표시 대상")]

    /// <summary>
    /// 연두색 SafeZone RectTransform.
    /// </summary>
    [SerializeField] private RectTransform safeZoneRect = null;

    /// <summary>
    /// 노란 BalanceBar RectTransform.
    /// </summary>
    [SerializeField] private RectTransform balanceLineRect = null;

    [Header("SafeZone 기준 폭 설정")]

    /// <summary>
    /// SafeZone 의 시작 최대 폭을 픽셀 기준으로 직접 지정한다.
    ///
    /// 중요:
    /// - 이 값이 "내가 설정한 SafeZone 시작 크기"의 진짜 기준이다.
    /// - stretch, world corners, 자동 계산에 영향받지 않는다.
    /// - 예: 180 으로 넣으면 시작 시 SafeZone 이 정확히 180px 폭이 되도록 동작한다.
    /// </summary>
    [SerializeField, Min(1f)] private float safeZoneBaseWidthInPixels = 180f;

    [Header("상단 타이머 바 참조")]

    /// <summary>
    /// 상단 얇은 타이머 Fill Image.
    /// </summary>
    [SerializeField] private Image timerFillImage = null;

    [Header("중앙 복구 바 참조")]

    /// <summary>
    /// 중앙 복구 게이지 Fill Image.
    /// </summary>
    [SerializeField] private Image recoveryFillImage = null;

    /// <summary>
    /// 중앙 복구 게이지 수치를 0~100 형태로 보여줄 텍스트.
    /// </summary>
    [SerializeField] private TMP_Text recoveryPercentText = null;

    [Header("보조 텍스트")]

    /// <summary>
    /// 현재 상태를 보여주는 텍스트.
    /// </summary>
    [SerializeField] private TMP_Text stateText = null;

    /// <summary>
    /// 노란 BalanceBar 가 SafeZone 안에 있을 때 출력할 텍스트.
    /// </summary>
    [SerializeField] private string stableText = "균형 유지";

    /// <summary>
    /// 노란 BalanceBar 가 SafeZone 밖에 있을 때 출력할 텍스트.
    /// </summary>
    [SerializeField] private string dangerText = "위험";

    [Header("타이머 색상 설정")]

    /// <summary>
    /// 시간이 충분할 때의 타이머 색상.
    /// </summary>
    [SerializeField] private Color timerFullTimeColor = new Color(0.2f, 0.9f, 0.2f, 1f);

    /// <summary>
    /// 시간이 거의 없을 때의 타이머 색상.
    /// </summary>
    [SerializeField] private Color timerLowTimeColor = new Color(0.95f, 0.15f, 0.15f, 1f);

    [Header("복구 바 색상 설정")]

    /// <summary>
    /// 복구량이 0에 가까울 때의 색상.
    /// </summary>
    [SerializeField] private Color recoveryEmptyColor = new Color(0.95f, 0.2f, 0.2f, 1f);

    /// <summary>
    /// 복구량이 100에 가까울 때의 색상.
    /// </summary>
    [SerializeField] private Color recoveryFullColor = new Color(0.2f, 0.95f, 0.2f, 1f);

    /// <summary>
    /// UI가 활성화된 마지막 상태를 저장한다.
    /// </summary>
    private bool lastVisibleState = true;

    /// <summary>
    /// TimerFill 의 디자인 최대 폭.
    /// </summary>
    private float cachedTimerFillMaxWidth = -1f;

    /// <summary>
    /// RecoveryFill 의 디자인 최대 폭.
    /// </summary>
    private float cachedRecoveryFillMaxWidth = -1f;

    /// <summary>
    /// TimerFill 의 초기 왼쪽 시작 X 좌표.
    /// </summary>
    private float cachedTimerFillLeftX = 0f;

    /// <summary>
    /// TimerFill 의 초기 로컬 Y 좌표.
    /// </summary>
    private float cachedTimerFillLocalY = 0f;

    /// <summary>
    /// TimerFill 의 초기 로컬 Z 좌표.
    /// </summary>
    private float cachedTimerFillLocalZ = 0f;

    /// <summary>
    /// TimerFill 의 초기 높이.
    /// </summary>
    private float cachedTimerFillHeight = 0f;

    /// <summary>
    /// TimerFill 의 초기 Pivot X 값.
    /// </summary>
    private float cachedTimerFillPivotX = 0f;

    /// <summary>
    /// RecoveryFill 의 초기 왼쪽 시작 X 좌표.
    /// </summary>
    private float cachedRecoveryFillLeftX = 0f;

    /// <summary>
    /// RecoveryFill 의 초기 로컬 Y 좌표.
    /// </summary>
    private float cachedRecoveryFillLocalY = 0f;

    /// <summary>
    /// RecoveryFill 의 초기 로컬 Z 좌표.
    /// </summary>
    private float cachedRecoveryFillLocalZ = 0f;

    /// <summary>
    /// RecoveryFill 의 초기 높이.
    /// </summary>
    private float cachedRecoveryFillHeight = 0f;

    /// <summary>
    /// RecoveryFill 의 초기 Pivot X 값.
    /// </summary>
    private float cachedRecoveryFillPivotX = 0f;

    /// <summary>
    /// SafeZone 기본 중심 X 좌표.
    /// 미니게임 시작 전 기본 표시 상태 복원용이다.
    /// </summary>
    private float cachedSafeZoneCenterX = 0f;

    /// <summary>
    /// SafeZone 기본 중심 Y 좌표.
    /// </summary>
    private float cachedSafeZoneCenterY = 0f;

    /// <summary>
    /// SafeZone 기본 로컬 Z 좌표.
    /// </summary>
    private float cachedSafeZoneLocalZ = 0f;

    /// <summary>
    /// SafeZone 높이.
    /// </summary>
    private float cachedSafeZoneHeight = 0f;

    /// <summary>
    /// BalanceBar 의 초기 로컬 Y 좌표.
    /// </summary>
    private float cachedBalanceBarLocalY = 0f;

    /// <summary>
    /// BalanceBar 의 초기 로컬 Z 좌표.
    /// </summary>
    private float cachedBalanceBarLocalZ = 0f;

    /// <summary>
    /// RectTransform 의 현재 화면 모양을 로컬 기준으로 읽기 위한 코너 버퍼.
    /// </summary>
    private readonly Vector3[] cachedWorldCorners = new Vector3[4];

    /// <summary>
    /// 시작 전에 디자인 데이터를 캐싱하고 기본 상태를 적용한다.
    /// </summary>
    private void Awake()
    {
        CacheDesignData();
        SyncControllerInitialSafeZoneSizeFromVisualBase();
        ResetVisualsToDefault();
    }

    /// <summary>
    /// 활성화될 때도 다시 한 번 동기화한다.
    /// </summary>
    private void OnEnable()
    {
        CacheDesignData();
        SyncControllerInitialSafeZoneSizeFromVisualBase();
        RefreshRootVisibility(force: true);
        ResetVisualsToDefault();
    }

    /// <summary>
    /// 시작 시 현재 컨트롤러 상태에 맞춰 UI를 정리한다.
    /// </summary>
    private void Start()
    {
        CacheDesignData();
        SyncControllerInitialSafeZoneSizeFromVisualBase();
        RefreshRootVisibility(force: true);
        RefreshUIImmediately();
    }

    /// <summary>
    /// 화면 크기나 RectTransform 크기가 바뀌면 다시 기준을 잡는다.
    /// </summary>
    private void OnRectTransformDimensionsChange()
    {
        if (!isActiveAndEnabled)
        {
            return;
        }

        CacheDesignData();
        SyncControllerInitialSafeZoneSizeFromVisualBase();
        RefreshUIImmediately();
    }

    /// <summary>
    /// 매 프레임 컨트롤러 수치를 읽어서 UI에 반영한다.
    /// </summary>
    private void Update()
    {
        RefreshRootVisibility(force: false);

        if (controller == null)
        {
            return;
        }

        if (!controller.IsPlaying && hideWhenNotPlaying)
        {
            return;
        }

        RefreshUIImmediately();
    }

    /// <summary>
    /// Inspector 에서 현재 배치된 디자인 값을 캐싱한다.
    /// </summary>
    private void CacheDesignData()
    {
        CacheHorizontalFillData(
            timerFillImage,
            ref cachedTimerFillMaxWidth,
            ref cachedTimerFillLeftX,
            ref cachedTimerFillLocalY,
            ref cachedTimerFillLocalZ,
            ref cachedTimerFillHeight,
            ref cachedTimerFillPivotX);

        CacheHorizontalFillData(
            recoveryFillImage,
            ref cachedRecoveryFillMaxWidth,
            ref cachedRecoveryFillLeftX,
            ref cachedRecoveryFillLocalY,
            ref cachedRecoveryFillLocalZ,
            ref cachedRecoveryFillHeight,
            ref cachedRecoveryFillPivotX);

        CacheSafeZoneData();
        CacheBalanceBarData();
    }

    /// <summary>
    /// 가로 길이가 변하는 Fill UI 의 현재 디자인 값을 캐싱한다.
    /// </summary>
    private void CacheHorizontalFillData(
        Image targetImage,
        ref float cachedMaxWidth,
        ref float cachedLeftX,
        ref float cachedLocalY,
        ref float cachedLocalZ,
        ref float cachedHeight,
        ref float cachedPivotX)
    {
        if (targetImage == null)
        {
            return;
        }

        RectTransform targetRect = targetImage.rectTransform;

        float currentWidth = Mathf.Max(Mathf.Abs(targetRect.rect.width), Mathf.Abs(targetRect.sizeDelta.x));
        float currentHeight = Mathf.Max(Mathf.Abs(targetRect.rect.height), Mathf.Abs(targetRect.sizeDelta.y));

        if (currentWidth <= 0.01f)
        {
            return;
        }

        cachedMaxWidth = currentWidth;
        cachedHeight = currentHeight;
        cachedPivotX = targetRect.pivot.x;
        cachedLocalY = targetRect.localPosition.y;
        cachedLocalZ = targetRect.localPosition.z;
        cachedLeftX = targetRect.localPosition.x - (currentWidth * cachedPivotX);
    }

    /// <summary>
    /// SafeZone 의 현재 배치 상태를 읽어서
    /// Y, Z, 높이, 기본 중심 위치를 캐싱한다.
    ///
    /// 중요:
    /// - 여기서는 SafeZone "폭"을 기준으로 삼지 않는다.
    /// - 시작 폭은 오직 safeZoneBaseWidthInPixels 를 진짜 기준으로 사용한다.
    /// </summary>
    private void CacheSafeZoneData()
    {
        if (safeZoneRect == null)
        {
            return;
        }

        RectTransform parentRect = safeZoneRect.parent as RectTransform;

        if (parentRect != null)
        {
            safeZoneRect.GetWorldCorners(cachedWorldCorners);

            float minX = 0f;
            float maxX = 0f;
            float minY = 0f;
            float maxY = 0f;
            bool initialized = false;

            for (int index = 0; index < cachedWorldCorners.Length; index++)
            {
                Vector3 localPoint = parentRect.InverseTransformPoint(cachedWorldCorners[index]);

                if (!initialized)
                {
                    minX = maxX = localPoint.x;
                    minY = maxY = localPoint.y;
                    initialized = true;
                    continue;
                }

                minX = Mathf.Min(minX, localPoint.x);
                maxX = Mathf.Max(maxX, localPoint.x);
                minY = Mathf.Min(minY, localPoint.y);
                maxY = Mathf.Max(maxY, localPoint.y);
            }

            cachedSafeZoneCenterX = (minX + maxX) * 0.5f;
            cachedSafeZoneCenterY = (minY + maxY) * 0.5f;
            cachedSafeZoneLocalZ = safeZoneRect.localPosition.z;
            cachedSafeZoneHeight = Mathf.Max(1f, maxY - minY);
        }
        else
        {
            cachedSafeZoneCenterX = safeZoneRect.localPosition.x;
            cachedSafeZoneCenterY = safeZoneRect.localPosition.y;
            cachedSafeZoneLocalZ = safeZoneRect.localPosition.z;
            cachedSafeZoneHeight = Mathf.Max(1f, Mathf.Abs(safeZoneRect.rect.height), Mathf.Abs(safeZoneRect.sizeDelta.y));
        }

        safeZoneRect.anchorMin = new Vector2(0.5f, 0.5f);
        safeZoneRect.anchorMax = new Vector2(0.5f, 0.5f);
        safeZoneRect.pivot = new Vector2(0.5f, 0.5f);
    }

    /// <summary>
    /// BalanceBar 의 현재 디자인 값을 캐싱한다.
    /// </summary>
    private void CacheBalanceBarData()
    {
        if (balanceLineRect == null)
        {
            return;
        }

        cachedBalanceBarLocalY = balanceLineRect.localPosition.y;
        cachedBalanceBarLocalZ = balanceLineRect.localPosition.z;
    }

    /// <summary>
    /// UI에서 정한 SafeZone 시작 픽셀 폭을
    /// 컨트롤러가 사용하는 시작 SafeZone 비율로 변환해서 전달한다.
    ///
    /// 예:
    /// - 트랙 길이 600px
    /// - safeZoneBaseWidthInPixels 180
    /// => 시작 SafeZone 비율은 0.3
    /// </summary>
    private void SyncControllerInitialSafeZoneSizeFromVisualBase()
    {
        if (controller == null || safeZoneRect == null)
        {
            return;
        }

        RectTransform targetParent = safeZoneRect.parent as RectTransform;

        if (!TryGetMarkerRangeInParentSpace(
            balanceLineStartPoint,
            balanceLineEndPoint,
            targetParent,
            out float startX,
            out float endX))
        {
            return;
        }

        float totalTrackWidth = Mathf.Max(1f, endX - startX);
        float designSize01 = Mathf.Clamp01(safeZoneBaseWidthInPixels / totalTrackWidth);

        controller.SetInitialSafeZoneSize01FromUI(designSize01);
    }

    /// <summary>
    /// 미니게임 시작 전 기본 표시 상태를 잡는다.
    /// </summary>
    private void ResetVisualsToDefault()
    {
        if (timerFillImage != null)
        {
            SetHorizontalFillByCachedDesign(
                timerFillImage.rectTransform,
                1f,
                cachedTimerFillMaxWidth,
                cachedTimerFillLeftX,
                cachedTimerFillLocalY,
                cachedTimerFillLocalZ,
                cachedTimerFillHeight,
                cachedTimerFillPivotX);

            timerFillImage.color = timerFullTimeColor;
        }

        if (recoveryFillImage != null)
        {
            SetHorizontalFillByCachedDesign(
                recoveryFillImage.rectTransform,
                0f,
                cachedRecoveryFillMaxWidth,
                cachedRecoveryFillLeftX,
                cachedRecoveryFillLocalY,
                cachedRecoveryFillLocalZ,
                cachedRecoveryFillHeight,
                cachedRecoveryFillPivotX);

            recoveryFillImage.color = recoveryEmptyColor;
        }

        if (safeZoneRect != null)
        {
            safeZoneRect.anchorMin = new Vector2(0.5f, 0.5f);
            safeZoneRect.anchorMax = new Vector2(0.5f, 0.5f);
            safeZoneRect.pivot = new Vector2(0.5f, 0.5f);
            safeZoneRect.localPosition = new Vector3(
                cachedSafeZoneCenterX,
                cachedSafeZoneCenterY,
                cachedSafeZoneLocalZ);

            safeZoneRect.sizeDelta = new Vector2(
                safeZoneBaseWidthInPixels,
                cachedSafeZoneHeight);
        }

        if (recoveryPercentText != null)
        {
            recoveryPercentText.text = "0";
        }

        if (stateText != null)
        {
            stateText.text = stableText;
        }
    }

    /// <summary>
    /// 현재 컨트롤러 진행 상태에 따라 UI 루트의 활성/비활성 여부를 갱신한다.
    /// </summary>
    private void RefreshRootVisibility(bool force)
    {
        if (uiRoot == null)
        {
            return;
        }

        bool visible = true;

        if (controller == null)
        {
            visible = !hideWhenNotPlaying;
        }
        else if (hideWhenNotPlaying)
        {
            visible = controller.IsPlaying;
        }

        if (!force && visible == lastVisibleState)
        {
            return;
        }

        uiRoot.SetActive(visible);
        lastVisibleState = visible;
    }

    /// <summary>
    /// 컨트롤러의 현재 값을 읽어서 모든 UI를 즉시 갱신한다.
    /// </summary>
    private void RefreshUIImmediately()
    {
        if (controller == null)
        {
            ResetVisualsToDefault();
            return;
        }

        RefreshTimerBar();
        RefreshSafeZone();
        RefreshBalanceBar();
        RefreshRecoveryGauge();
        RefreshStateText();
    }

    /// <summary>
    /// 상단 타이머 바를 갱신한다.
    /// </summary>
    private void RefreshTimerBar()
    {
        if (timerFillImage == null)
        {
            return;
        }

        float remaining01 = controller.RemainingTime01;

        SetHorizontalFillByCachedDesign(
            timerFillImage.rectTransform,
            remaining01,
            cachedTimerFillMaxWidth,
            cachedTimerFillLeftX,
            cachedTimerFillLocalY,
            cachedTimerFillLocalZ,
            cachedTimerFillHeight,
            cachedTimerFillPivotX);

        timerFillImage.color = Color.Lerp(timerLowTimeColor, timerFullTimeColor, remaining01);
    }

    /// <summary>
    /// SafeZone 의 시각 표현을 갱신한다.
    ///
    /// 중요:
    /// - 시작 폭은 더 이상 현재 Rect 의 보이는 폭으로 계산하지 않는다.
    /// - 컨트롤러가 내보낸 SafeZoneStart01 / End01 를
    ///   marker 범위에 직접 매핑해서 위치와 폭을 결정한다.
    /// - 따라서 좌우 대칭과 실제 SafeZone 판정 크기가 일치한다.
    /// </summary>
    private void RefreshSafeZone()
    {
        if (safeZoneRect == null || controller == null)
        {
            return;
        }

        RectTransform targetParent = safeZoneRect.parent as RectTransform;

        if (!TryGetMarkerRangeInParentSpace(
            balanceLineStartPoint,
            balanceLineEndPoint,
            targetParent,
            out float startX,
            out float endX))
        {
            return;
        }

        float safeZoneStartX = Mathf.Lerp(startX, endX, controller.SafeZoneStart01);
        float safeZoneEndX = Mathf.Lerp(startX, endX, controller.SafeZoneEnd01);

        float safeZoneWidth = Mathf.Max(0f, safeZoneEndX - safeZoneStartX);
        float safeZoneCenterX = (safeZoneStartX + safeZoneEndX) * 0.5f;

        safeZoneRect.anchorMin = new Vector2(0.5f, 0.5f);
        safeZoneRect.anchorMax = new Vector2(0.5f, 0.5f);
        safeZoneRect.pivot = new Vector2(0.5f, 0.5f);

        safeZoneRect.localPosition = new Vector3(
            safeZoneCenterX,
            cachedSafeZoneCenterY,
            cachedSafeZoneLocalZ);

        Vector2 currentSize = safeZoneRect.sizeDelta;
        currentSize.x = safeZoneWidth;
        currentSize.y = cachedSafeZoneHeight;
        safeZoneRect.sizeDelta = currentSize;
    }

    /// <summary>
    /// BalanceBar 의 좌우 위치를 갱신한다.
    /// </summary>
    private void RefreshBalanceBar()
    {
        if (balanceLineRect == null || controller == null)
        {
            return;
        }

        RectTransform targetParent = balanceLineRect.parent as RectTransform;

        if (!TryGetMarkerRangeInParentSpace(
            balanceLineStartPoint,
            balanceLineEndPoint,
            targetParent,
            out float startX,
            out float endX))
        {
            return;
        }

        float pivotPositionX = Mathf.Lerp(startX, endX, controller.BalanceLine01);

        balanceLineRect.localPosition = new Vector3(
            pivotPositionX,
            cachedBalanceBarLocalY,
            cachedBalanceBarLocalZ);
    }

    /// <summary>
    /// 중앙 복구 게이지와 숫자 텍스트를 갱신한다.
    /// </summary>
    private void RefreshRecoveryGauge()
    {
        if (controller == null)
        {
            return;
        }

        float recoveryProgress01 = controller.RecoveryProgress01;

        if (recoveryFillImage != null)
        {
            SetHorizontalFillByCachedDesign(
                recoveryFillImage.rectTransform,
                recoveryProgress01,
                cachedRecoveryFillMaxWidth,
                cachedRecoveryFillLeftX,
                cachedRecoveryFillLocalY,
                cachedRecoveryFillLocalZ,
                cachedRecoveryFillHeight,
                cachedRecoveryFillPivotX);

            recoveryFillImage.color = Color.Lerp(recoveryEmptyColor, recoveryFullColor, recoveryProgress01);
        }

        if (recoveryPercentText != null)
        {
            int currentRecoveryValue = Mathf.RoundToInt(controller.RecoveryValue);
            recoveryPercentText.text = currentRecoveryValue.ToString();
        }
    }

    /// <summary>
    /// 현재 안정 상태에 따라 보조 텍스트를 갱신한다.
    /// </summary>
    private void RefreshStateText()
    {
        if (stateText == null || controller == null)
        {
            return;
        }

        stateText.text = controller.IsInsideSafeZone ? stableText : dangerText;
    }

    /// <summary>
    /// Fill UI 의 현재 디자인 기준값을 유지한 채,
    /// 가로 길이만 0~1 비율로 변경한다.
    /// </summary>
    private void SetHorizontalFillByCachedDesign(
        RectTransform targetRect,
        float normalizedWidth01,
        float storedMaxWidth,
        float storedLeftX,
        float storedLocalY,
        float storedLocalZ,
        float storedHeight,
        float storedPivotX)
    {
        if (targetRect == null)
        {
            return;
        }

        float targetWidth = Mathf.Max(0f, storedMaxWidth) * Mathf.Clamp01(normalizedWidth01);
        float pivotPositionX = storedLeftX + (targetWidth * storedPivotX);

        targetRect.localPosition = new Vector3(
            pivotPositionX,
            storedLocalY,
            storedLocalZ);

        Vector2 currentSize = targetRect.sizeDelta;
        currentSize.x = targetWidth;
        currentSize.y = storedHeight;
        targetRect.sizeDelta = currentSize;
    }

    /// <summary>
    /// 두 marker 의 위치를 targetParent 기준 로컬 X 좌표로 변환해서,
    /// 좌우 순서대로 정렬된 범위를 반환한다.
    /// </summary>
    private bool TryGetMarkerRangeInParentSpace(
        RectTransform startMarker,
        RectTransform endMarker,
        RectTransform targetParent,
        out float minX,
        out float maxX)
    {
        minX = 0f;
        maxX = 0f;

        if (startMarker == null || endMarker == null || targetParent == null)
        {
            return false;
        }

        Vector3 startWorldPosition = startMarker.position;
        Vector3 endWorldPosition = endMarker.position;

        Vector3 startLocalPosition = targetParent.InverseTransformPoint(startWorldPosition);
        Vector3 endLocalPosition = targetParent.InverseTransformPoint(endWorldPosition);

        minX = Mathf.Min(startLocalPosition.x, endLocalPosition.x);
        maxX = Mathf.Max(startLocalPosition.x, endLocalPosition.x);
        return true;
    }
}