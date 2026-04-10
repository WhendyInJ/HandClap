using System;
using UnityEngine;

/// <summary>
/// 손바닥 밀치기 게임용 균형 미니게임의 실제 로직을 담당하는 컨트롤러.
///
/// 현재 구조:
/// 1. 노란 BalanceLine 은 자동으로 움직이는 추적 대상이다.
/// 2. 플레이어는 SafeZone(연두색 구간)을 움직인다.
/// 3. BalanceLine 을 SafeZone 안에 오래 유지할수록 중앙 복구 게이지가 찬다.
/// 4. 공격을 맞을 때마다 SafeZone 길이가 줄어든다.
/// 5. 제한 시간 안에 중앙 복구 게이지를 100까지 채우면 성공한다.
///
/// 이번 수정의 핵심:
/// - UI가 정한 SafeZone 시작 폭을 논리 SafeZone 시작 비율로 동기화할 수 있다.
/// - UI가 정한 BalanceLine 실제 반폭을 논리 edge padding 으로 동기화할 수 있다.
/// - 따라서 로직 판정과 실제 화면상의 비주얼 이동 범위를 최대한 일치시킨다.
/// </summary>
public class HandClap_BalanceMinigameController : MonoBehaviour
{
    [Header("프로토타입 시작 옵션")]

    /// <summary>
    /// 게임 시작 시 자동으로 미니게임을 시작할지 여부.
    /// 테스트용 옵션이다.
    /// </summary>
    [SerializeField] private bool playOnStart = false;

    /// <summary>
    /// 미니게임이 이미 진행 중일 때 다시 시작 요청이 오면 재시작을 허용할지 여부.
    /// </summary>
    [SerializeField] private bool allowRestartWhilePlaying = false;

    /// <summary>
    /// Time.timeScale 영향을 무시하고 진행할지 여부.
    /// </summary>
    [SerializeField] private bool useUnscaledTime = false;

    [Header("SafeZone 크기 설정")]

    /// <summary>
    /// 미니게임 시작 시 SafeZone 의 초기 길이 비율.
    /// 0~1 범위이며 1이면 전체 바 길이와 같다.
    /// UI에서 시작 폭을 동기화하면 그 값이 우선 사용될 수 있다.
    /// </summary>
    [SerializeField, Range(0.05f, 1f)] private float initialSafeZoneSize01 = 0.42f;

    /// <summary>
    /// SafeZone 이 줄어들 수 있는 최소 길이 비율.
    /// 공격을 많이 맞아도 이 값 아래로는 내려가지 않는다.
    /// </summary>
    [SerializeField, Range(0.05f, 1f)] private float minSafeZoneSize01 = 0.12f;

    /// <summary>
    /// SafeZone 이 시작할 때 놓일 초기 중심 위치.
    /// 0은 맨 왼쪽, 0.5는 중앙, 1은 맨 오른쪽이다.
    /// </summary>
    [SerializeField, Range(0f, 1f)] private float initialSafeZoneCenter01 = 0.5f;

    /// <summary>
    /// 공격 1회 피격 시 SafeZone 길이가 줄어드는 기본 비율.
    /// </summary>
    [SerializeField, Min(0f)] private float safeZoneShrinkPerHit01 = 0.06f;

    [Header("SafeZone 이동 설정 - 플레이어 조작")]

    /// <summary>
    /// 플레이어가 SafeZone 을 조작할 때 사용하는 입력 키.
    /// 현재 요구사항 기준으로 스페이스바를 사용한다.
    /// 스페이스를 누르면 오른쪽으로 밀리고, 떼면 왼쪽으로 흐른다.
    /// </summary>
    [SerializeField] private KeyCode controlKey = KeyCode.Space;

    /// <summary>
    /// 스페이스를 누르고 있을 때 SafeZone 이 오른쪽으로 가속되는 세기.
    /// </summary>
    [SerializeField, Min(0f)] private float safeZonePressAcceleration = 3.6f;

    /// <summary>
    /// 스페이스를 누르지 않을 때 SafeZone 이 왼쪽으로 가속되는 세기.
    /// </summary>
    [SerializeField, Min(0f)] private float safeZoneReleaseAcceleration = 2.9f;

    /// <summary>
    /// SafeZone 의 최대 이동 속도.
    /// </summary>
    [SerializeField, Min(0.1f)] private float safeZoneMaxSpeed = 1.2f;

    /// <summary>
    /// SafeZone 속도가 점차 줄어드는 감쇠 계수.
    /// 너무 높으면 답답하고, 너무 낮으면 미끄럽다.
    /// </summary>
    [SerializeField, Range(0f, 30f)] private float safeZoneVelocityDamping = 7.5f;

    /// <summary>
    /// SafeZone 이 양끝 경계에 닿았을 때 속도를 얼마나 줄일지 정하는 계수.
    /// </summary>
    [SerializeField, Range(0f, 1f)] private float safeZoneEdgeDamping = 0.15f;

    [Header("BalanceLine 자동 이동 설정")]

    /// <summary>
    /// BalanceLine 이 새 목표 위치를 다시 정하는 시간 간격.
    /// 값이 클수록 덜 요리조리 움직인다.
    /// </summary>
    [SerializeField, Min(0.1f)] private float balanceLineTargetChangeInterval = 1.45f;

    /// <summary>
    /// BalanceLine 이 한 번에 새 목표로 삼을 수 있는 최대 이동 폭.
    /// 값이 작을수록 덜 급격하게 방향을 바꾼다.
    /// </summary>
    [SerializeField, Range(0.01f, 1f)] private float balanceLineMaxTargetShift01 = 0.09f;

    /// <summary>
    /// BalanceLine 을 화면 중앙 쪽으로 조금 끌어당기는 정도.
    /// 값이 높을수록 가장자리로 덜 치우친다.
    /// </summary>
    [SerializeField, Range(0f, 1f)] private float balanceLineCenterBias = 0.45f;

    /// <summary>
    /// BalanceLine 이 목표 위치로 이동할 때 사용하는 SmoothDamp 시간.
    /// 값이 클수록 더 천천히, 부드럽게 이동한다.
    /// </summary>
    [SerializeField, Min(0.01f)] private float balanceLineSmoothTime = 0.55f;

    /// <summary>
    /// BalanceLine 의 최대 이동 속도.
    /// 너무 빠르다는 피드백을 반영해 낮춘 값이다.
    /// </summary>
    [SerializeField, Min(0.01f)] private float balanceLineMaxSpeed = 0.11f;

    /// <summary>
    /// BalanceLine 이 너무 가장자리 끝에 붙지 않게 하기 위한 패딩 비율.
    /// UI에서 실제 라인 반폭 기준 padding 을 전달하면 그 값과 더 큰 쪽을 사용한다.
    /// </summary>
    [SerializeField, Range(0f, 0.2f)] private float balanceLineEdgePadding01 = 0.03f;

    [Header("상단 얇은 타이머 설정")]

    /// <summary>
    /// 미니게임 제한 시간.
    /// 이 시간이 0이 되기 전에 중앙 복구 게이지를 목표치까지 채워야 한다.
    /// </summary>
    [SerializeField, Min(0.5f)] private float challengeDuration = 8f;

    [Header("중앙 복구 게이지 설정")]

    /// <summary>
    /// BalanceLine 이 SafeZone 안에 있을 때 중앙 복구 게이지가 초당 차오르는 속도.
    /// </summary>
    [SerializeField, Min(0f)] private float recoveryFillPerSecond = 14f;

    /// <summary>
    /// BalanceLine 이 SafeZone 밖에 있을 때 중앙 복구 게이지가 초당 감소하는 속도.
    /// </summary>
    [SerializeField, Min(0f)] private float recoveryDecayPerSecond = 6f;

    /// <summary>
    /// 중앙 복구 게이지 목표값.
    /// 기본적으로 100으로 둔다.
    /// </summary>
    [SerializeField, Min(1f)] private float recoveryGoal = 100f;

    [Header("디버그 옵션")]

    /// <summary>
    /// 상태 변화 시 디버그 로그를 출력할지 여부.
    /// </summary>
    [SerializeField] private bool verboseLog = false;

    /// <summary>
    /// UI가 정한 시작 SafeZone 크기 비율 오버라이드.
    /// 0 이하이면 Inspector 기본값을 사용한다.
    /// </summary>
    private float runtimeInitialSafeZoneSize01Override = -1f;

    /// <summary>
    /// UI가 정한 BalanceLine 반폭 기반 padding 오버라이드.
    /// 0 이하이면 Inspector 기본값만 사용한다.
    /// </summary>
    private float runtimeBalanceLineEdgePadding01Override = -1f;

    /// <summary>
    /// 미니게임 시작 시 외부에 알려주는 이벤트.
    /// </summary>
    public event Action OnBalanceMinigameStarted;

    /// <summary>
    /// 미니게임 성공 시 외부에 알려주는 이벤트.
    /// </summary>
    public event Action OnBalanceMinigameSucceeded;

    /// <summary>
    /// 미니게임 실패 시 외부에 알려주는 이벤트.
    /// </summary>
    public event Action OnBalanceMinigameFailed;

    /// <summary>
    /// 미니게임 종료 시 성공 여부를 함께 알려주는 이벤트.
    /// true면 성공, false면 실패.
    /// </summary>
    public event Action<bool> OnBalanceMinigameFinished;

    /// <summary>
    /// 현재 BalanceLine 이 SafeZone 안에 있는지 상태가 바뀔 때 호출되는 이벤트.
    /// true면 안전 구간 안, false면 바깥.
    /// </summary>
    public event Action<bool> OnBalanceStabilityChanged;

    /// <summary>
    /// 현재 미니게임이 진행 중인지 여부.
    /// </summary>
    private bool isPlaying = false;

    /// <summary>
    /// 현재 SafeZone 길이 비율.
    /// 공격을 맞을수록 줄어든다.
    /// </summary>
    private float currentSafeZoneSize01 = 0f;

    /// <summary>
    /// 현재 SafeZone 이동 진행도.
    /// 0이면 완전 왼쪽, 1이면 완전 오른쪽이다.
    /// 실제 SafeZone 시작 위치는 currentSafeZoneSize01 을 반영해서 계산한다.
    /// </summary>
    private float currentSafeZoneTravel01 = 0f;

    /// <summary>
    /// SafeZone 의 현재 속도.
    /// </summary>
    private float safeZoneVelocity = 0f;

    /// <summary>
    /// 노란 BalanceLine 의 현재 위치 비율.
    /// 자동으로 움직이는 대상이다.
    /// 이 값은 "트랙 전체 기준 정규화 위치"다.
    /// </summary>
    private float balanceLine01 = 0.5f;

    /// <summary>
    /// SmoothDamp 에서 사용하는 BalanceLine 내부 속도 변수.
    /// </summary>
    private float balanceLineVelocity = 0f;

    /// <summary>
    /// BalanceLine 이 현재 향하고 있는 목표 위치 비율.
    /// </summary>
    private float balanceLineTarget01 = 0.5f;

    /// <summary>
    /// 새 목표 위치를 다시 정하기까지 남은 시간.
    /// </summary>
    private float balanceLineTargetChangeTimer = 0f;

    /// <summary>
    /// 미니게임 남은 시간.
    /// </summary>
    private float remainingTime = 0f;

    /// <summary>
    /// 중앙 복구 게이지의 현재 값.
    /// 0에서 시작해서 recoveryGoal 에 도달하면 성공 처리한다.
    /// </summary>
    private float recoveryValue = 0f;

    /// <summary>
    /// 이전 프레임 기준으로 BalanceLine 이 SafeZone 안에 있었는지 기록한다.
    /// </summary>
    private bool wasInsideSafeZoneLastFrame = false;

    /// <summary>
    /// SafeZone 이 실제로 사용할 수 있는 이동 가능한 전체 비율 길이.
    /// SafeZone 이 작아질수록 커지고, 커질수록 줄어든다.
    /// </summary>
    private float SafeZoneAvailableTravel01 => Mathf.Max(0f, 1f - currentSafeZoneSize01);

    /// <summary>
    /// 현재 사용할 시작 SafeZone 크기 비율.
    /// UI 오버라이드가 있으면 그 값을 우선 사용한다.
    /// </summary>
    private float EffectiveInitialSafeZoneSize01
    {
        get
        {
            float resolvedValue = runtimeInitialSafeZoneSize01Override > 0f
                ? runtimeInitialSafeZoneSize01Override
                : initialSafeZoneSize01;

            return Mathf.Clamp(resolvedValue, minSafeZoneSize01, 1f);
        }
    }

    /// <summary>
    /// 실제로 사용할 BalanceLine 가장자리 패딩 비율.
    /// Inspector 기본값과 UI 오버라이드 중 더 큰 값을 사용한다.
    /// </summary>
    private float EffectiveBalanceLineEdgePadding01
    {
        get
        {
            float overrideValue = runtimeBalanceLineEdgePadding01Override > 0f
                ? runtimeBalanceLineEdgePadding01Override
                : 0f;

            return Mathf.Clamp(Mathf.Max(balanceLineEdgePadding01, overrideValue), 0f, 0.49f);
        }
    }

    /// <summary>
    /// 외부에서 현재 미니게임 진행 여부를 읽기 위한 프로퍼티.
    /// </summary>
    public bool IsPlaying => isPlaying;

    /// <summary>
    /// 외부에서 현재 SafeZone 길이를 읽기 위한 프로퍼티.
    /// </summary>
    public float SafeZoneSize01 => currentSafeZoneSize01;

    /// <summary>
    /// 외부에서 현재 SafeZone 시작 위치를 읽기 위한 프로퍼티.
    /// 현재 크기를 반영해서 이동 진행도를 실제 시작 좌표로 변환한다.
    /// </summary>
    public float SafeZoneStart01 => currentSafeZoneTravel01 * SafeZoneAvailableTravel01;

    /// <summary>
    /// 외부에서 현재 SafeZone 끝 위치를 읽기 위한 프로퍼티.
    /// </summary>
    public float SafeZoneEnd01 => Mathf.Clamp01(SafeZoneStart01 + currentSafeZoneSize01);

    /// <summary>
    /// 외부에서 현재 SafeZone 중심 위치를 읽기 위한 프로퍼티.
    /// </summary>
    public float SafeZoneCenter01 => SafeZoneStart01 + (currentSafeZoneSize01 * 0.5f);

    /// <summary>
    /// 외부에서 현재 BalanceLine 위치를 읽기 위한 프로퍼티.
    /// </summary>
    public float BalanceLine01 => balanceLine01;

    /// <summary>
    /// 외부에서 상단 타이머 진행률을 0~1로 읽기 위한 프로퍼티.
    /// 1이면 풀타임, 0이면 시간 종료다.
    /// </summary>
    public float RemainingTime01
    {
        get
        {
            if (challengeDuration <= 0f)
            {
                return 0f;
            }

            return Mathf.Clamp01(remainingTime / challengeDuration);
        }
    }

    /// <summary>
    /// 외부에서 중앙 복구 게이지 진행률을 0~1로 읽기 위한 프로퍼티.
    /// </summary>
    public float RecoveryProgress01
    {
        get
        {
            if (recoveryGoal <= 0f)
            {
                return 0f;
            }

            return Mathf.Clamp01(recoveryValue / recoveryGoal);
        }
    }

    /// <summary>
    /// 외부에서 중앙 복구 게이지 현재 값을 직접 읽기 위한 프로퍼티.
    /// </summary>
    public float RecoveryValue => recoveryValue;

    /// <summary>
    /// 외부에서 현재 BalanceLine 이 SafeZone 안에 있는지 읽기 위한 프로퍼티.
    /// 현재 판정은 BalanceLine 중심점 기준이다.
    /// </summary>
    public bool IsInsideSafeZone
    {
        get
        {
            return balanceLine01 >= SafeZoneStart01 && balanceLine01 <= SafeZoneEnd01;
        }
    }

    /// <summary>
    /// UI에서 계산한 시작 SafeZone 크기 비율을 컨트롤러에 전달한다.
    /// </summary>
    /// <param name="size01">트랙 전체 길이 대비 SafeZone 시작 비율.</param>
    public void SetInitialSafeZoneSize01FromUI(float size01)
    {
        runtimeInitialSafeZoneSize01Override = Mathf.Clamp(size01, minSafeZoneSize01, 1f);

        if (!isPlaying)
        {
            currentSafeZoneSize01 = EffectiveInitialSafeZoneSize01;
            currentSafeZoneTravel01 = GetTravel01FromCenter(initialSafeZoneCenter01, currentSafeZoneSize01);
        }

        if (verboseLog)
        {
            Debug.Log($"[HandClap_BalanceMinigameController] UI 기준 SafeZone 시작 비율 등록: {runtimeInitialSafeZoneSize01Override:0.000}");
        }
    }

    /// <summary>
    /// UI에서 계산한 BalanceLine 반폭 비율을 컨트롤러에 전달한다.
    /// 이 값은 로직상 가장자리 패딩으로 사용되어, 판정과 비주얼 범위를 맞추는 데 사용된다.
    /// </summary>
    /// <param name="halfWidth01">트랙 전체 길이 대비 BalanceLine 반폭 비율.</param>
    public void SetBalanceLineHalfWidth01FromUI(float halfWidth01)
    {
        runtimeBalanceLineEdgePadding01Override = Mathf.Clamp(halfWidth01, 0f, 0.49f);

        if (verboseLog)
        {
            Debug.Log($"[HandClap_BalanceMinigameController] UI 기준 BalanceLine 반폭 패딩 등록: {runtimeBalanceLineEdgePadding01Override:0.000}");
        }
    }

    /// <summary>
    /// 시작 시 런타임 값을 초기화하고, 테스트 옵션에 따라 미니게임을 자동 시작한다.
    /// </summary>
    private void Start()
    {
        ResetRuntimeState();

        if (playOnStart)
        {
            StartBalanceMinigame();
        }
    }

    /// <summary>
    /// 매 프레임 미니게임 로직을 갱신한다.
    /// </summary>
    private void Update()
    {
        if (!isPlaying)
        {
            return;
        }

        float deltaTime = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

        if (deltaTime <= 0f)
        {
            return;
        }

        UpdateSafeZoneMovement(deltaTime);
        UpdateBalanceLineMovement(deltaTime);
        UpdateRecoveryGauge(deltaTime);
        UpdateTimer(deltaTime);
        UpdateStabilityEventState();

        if (recoveryValue >= recoveryGoal)
        {
            FinishAsSuccess();
            return;
        }

        if (remainingTime <= 0f)
        {
            FinishAsFail();
        }
    }

    /// <summary>
    /// 외부에서 휘청 미니게임을 시작할 때 호출하는 함수.
    /// 플레이어 휘청 이벤트가 발생하면 이 함수만 호출하면 된다.
    /// </summary>
    public void StartBalanceMinigame()
    {
        if (isPlaying && !allowRestartWhilePlaying)
        {
            if (verboseLog)
            {
                Debug.Log("[HandClap_BalanceMinigameController] 이미 진행 중이라 시작 요청을 무시했습니다.");
            }

            return;
        }

        isPlaying = true;

        currentSafeZoneSize01 = EffectiveInitialSafeZoneSize01;
        currentSafeZoneTravel01 = GetTravel01FromCenter(initialSafeZoneCenter01, currentSafeZoneSize01);
        safeZoneVelocity = 0f;

        balanceLine01 = 0.5f;
        balanceLineVelocity = 0f;
        balanceLineTarget01 = 0.5f;
        balanceLineTargetChangeTimer = balanceLineTargetChangeInterval;

        remainingTime = challengeDuration;
        recoveryValue = 0f;

        wasInsideSafeZoneLastFrame = IsInsideSafeZone;

        if (verboseLog)
        {
            Debug.Log($"[HandClap_BalanceMinigameController] 균형 미니게임 시작 - SafeZone: {currentSafeZoneSize01:0.000}, LinePadding: {EffectiveBalanceLineEdgePadding01:0.000}");
        }

        OnBalanceMinigameStarted?.Invoke();
        OnBalanceStabilityChanged?.Invoke(wasInsideSafeZoneLastFrame);
    }

    /// <summary>
    /// 외부에서 플레이어가 공격을 맞았을 때 호출하는 기본 함수.
    /// SafeZone 을 기본 감소량만큼 줄인다.
    /// </summary>
    public void ApplyHit()
    {
        ApplyHit(safeZoneShrinkPerHit01);
    }

    /// <summary>
    /// 외부에서 플레이어가 공격을 맞았을 때 호출하는 확장 함수.
    /// 공격 종류마다 다른 감소량을 넣고 싶을 때 사용한다.
    /// </summary>
    /// <param name="customShrinkAmount01">줄일 SafeZone 비율.</param>
    public void ApplyHit(float customShrinkAmount01)
    {
        if (!isPlaying)
        {
            return;
        }

        if (customShrinkAmount01 <= 0f)
        {
            return;
        }

        currentSafeZoneSize01 = Mathf.Max(minSafeZoneSize01, currentSafeZoneSize01 - customShrinkAmount01);
        currentSafeZoneTravel01 = Mathf.Clamp01(currentSafeZoneTravel01);

        if (verboseLog)
        {
            Debug.Log($"[HandClap_BalanceMinigameController] 피격 발생 - SafeZone 감소, 현재 크기: {currentSafeZoneSize01:0.000}");
        }
    }

    /// <summary>
    /// 외부 체력 시스템과 연결할 때 사용할 함수.
    /// 체력 1이면 시작 SafeZone 크기, 체력 0이면 최소 SafeZone 크기를 사용한다.
    /// </summary>
    /// <param name="health01">0~1 범위 체력 비율.</param>
    public void SetExternalHealth01(float health01)
    {
        float clampedHealth01 = Mathf.Clamp01(health01);

        float mappedSafeZoneSize = Mathf.Lerp(minSafeZoneSize01, EffectiveInitialSafeZoneSize01, clampedHealth01);
        currentSafeZoneSize01 = Mathf.Clamp(mappedSafeZoneSize, minSafeZoneSize01, 1f);
        currentSafeZoneTravel01 = Mathf.Clamp01(currentSafeZoneTravel01);

        if (verboseLog)
        {
            Debug.Log($"[HandClap_BalanceMinigameController] 외부 체력 연동 - 체력 비율: {clampedHealth01:0.00}, SafeZone: {currentSafeZoneSize01:0.000}");
        }
    }

    /// <summary>
    /// 외부에서 현재 진행 중인 미니게임을 강제로 종료하고 싶을 때 사용하는 함수.
    /// 상태 전환, 씬 전환, 사망 처리 시 필요할 수 있다.
    /// </summary>
    public void ForceStopWithoutResult()
    {
        if (!isPlaying)
        {
            return;
        }

        isPlaying = false;

        if (verboseLog)
        {
            Debug.Log("[HandClap_BalanceMinigameController] 결과 처리 없이 강제 종료");
        }
    }

    /// <summary>
    /// 미니게임 시작 전/후 기본 런타임 상태를 초기화한다.
    /// </summary>
    private void ResetRuntimeState()
    {
        isPlaying = false;

        currentSafeZoneSize01 = EffectiveInitialSafeZoneSize01;
        currentSafeZoneTravel01 = GetTravel01FromCenter(initialSafeZoneCenter01, currentSafeZoneSize01);
        safeZoneVelocity = 0f;

        balanceLine01 = 0.5f;
        balanceLineVelocity = 0f;
        balanceLineTarget01 = 0.5f;
        balanceLineTargetChangeTimer = balanceLineTargetChangeInterval;

        remainingTime = challengeDuration;
        recoveryValue = 0f;
        wasInsideSafeZoneLastFrame = false;
    }

    /// <summary>
    /// 플레이어 입력을 받아 SafeZone 을 이동시킨다.
    /// 이동 진행도(0~1)를 직접 움직인다.
    /// </summary>
    /// <param name="deltaTime">프레임 시간.</param>
    private void UpdateSafeZoneMovement(float deltaTime)
    {
        float inputAcceleration = Input.GetKey(controlKey) ? safeZonePressAcceleration : -safeZoneReleaseAcceleration;

        safeZoneVelocity += inputAcceleration * deltaTime;
        safeZoneVelocity = Mathf.Clamp(safeZoneVelocity, -safeZoneMaxSpeed, safeZoneMaxSpeed);

        float dampingT = 1f - Mathf.Exp(-safeZoneVelocityDamping * deltaTime);
        safeZoneVelocity = Mathf.Lerp(safeZoneVelocity, 0f, dampingT);

        currentSafeZoneTravel01 += safeZoneVelocity * deltaTime;

        float clampedTravel = Mathf.Clamp01(currentSafeZoneTravel01);

        if (!Mathf.Approximately(clampedTravel, currentSafeZoneTravel01))
        {
            currentSafeZoneTravel01 = clampedTravel;
            safeZoneVelocity *= safeZoneEdgeDamping;
        }
        else
        {
            currentSafeZoneTravel01 = clampedTravel;
        }
    }

    /// <summary>
    /// BalanceLine 을 부드럽게 이동시킨다.
    /// 일정 시간마다 새 목표 위치를 정하고 SmoothDamp 로 천천히 이동한다.
    /// </summary>
    /// <param name="deltaTime">프레임 시간.</param>
    private void UpdateBalanceLineMovement(float deltaTime)
    {
        balanceLineTargetChangeTimer -= deltaTime;

        if (balanceLineTargetChangeTimer <= 0f)
        {
            ChooseNextBalanceLineTarget();
            balanceLineTargetChangeTimer = balanceLineTargetChangeInterval;
        }

        balanceLine01 = Mathf.SmoothDamp(
            balanceLine01,
            balanceLineTarget01,
            ref balanceLineVelocity,
            balanceLineSmoothTime,
            balanceLineMaxSpeed,
            deltaTime
        );

        float edgePadding = EffectiveBalanceLineEdgePadding01;
        balanceLine01 = Mathf.Clamp(balanceLine01, edgePadding, 1f - edgePadding);
    }

    /// <summary>
    /// BalanceLine 의 다음 목표 위치를 정한다.
    /// UI와 맞춘 실제 edge padding 범위를 고려한다.
    /// </summary>
    private void ChooseNextBalanceLineTarget()
    {
        float randomShift = UnityEngine.Random.Range(-balanceLineMaxTargetShift01, balanceLineMaxTargetShift01);
        float centerPull = (0.5f - balanceLine01) * balanceLineCenterBias;

        float nextTarget = balanceLine01 + randomShift + centerPull;
        float edgePadding = EffectiveBalanceLineEdgePadding01;

        balanceLineTarget01 = Mathf.Clamp(nextTarget, edgePadding, 1f - edgePadding);
    }

    /// <summary>
    /// BalanceLine 이 SafeZone 안에 있는지 여부에 따라 중앙 복구 게이지를 증가 또는 감소시킨다.
    /// </summary>
    /// <param name="deltaTime">프레임 시간.</param>
    private void UpdateRecoveryGauge(float deltaTime)
    {
        if (IsInsideSafeZone)
        {
            recoveryValue += recoveryFillPerSecond * deltaTime;
        }
        else
        {
            recoveryValue -= recoveryDecayPerSecond * deltaTime;
        }

        recoveryValue = Mathf.Clamp(recoveryValue, 0f, recoveryGoal);
    }

    /// <summary>
    /// 상단 타이머를 감소시킨다.
    /// </summary>
    /// <param name="deltaTime">프레임 시간.</param>
    private void UpdateTimer(float deltaTime)
    {
        remainingTime -= deltaTime;
        remainingTime = Mathf.Max(0f, remainingTime);
    }

    /// <summary>
    /// BalanceLine 이 SafeZone 안/밖 상태가 바뀌었는지 검사하고
    /// 상태가 바뀌었을 때만 이벤트를 발행한다.
    /// </summary>
    private void UpdateStabilityEventState()
    {
        bool currentInsideSafeZone = IsInsideSafeZone;

        if (currentInsideSafeZone == wasInsideSafeZoneLastFrame)
        {
            return;
        }

        wasInsideSafeZoneLastFrame = currentInsideSafeZone;
        OnBalanceStabilityChanged?.Invoke(currentInsideSafeZone);
    }

    /// <summary>
    /// SafeZone 초기 중심 위치와 현재 크기를 받아, 이를 0~1 이동 진행도로 변환한다.
    /// </summary>
    /// <param name="center01">SafeZone 중심 위치 비율.</param>
    /// <param name="size01">SafeZone 크기 비율.</param>
    /// <returns>0~1 이동 진행도.</returns>
    private float GetTravel01FromCenter(float center01, float size01)
    {
        float start01 = Mathf.Clamp01(center01 - (size01 * 0.5f));
        float availableTravel01 = Mathf.Max(0f, 1f - size01);

        if (availableTravel01 <= 0.0001f)
        {
            return 0f;
        }

        return Mathf.Clamp01(start01 / availableTravel01);
    }

    /// <summary>
    /// 미니게임 성공 처리.
    /// </summary>
    private void FinishAsSuccess()
    {
        if (!isPlaying)
        {
            return;
        }

        isPlaying = false;
        recoveryValue = recoveryGoal;

        if (verboseLog)
        {
            Debug.Log("[HandClap_BalanceMinigameController] 균형 회복 성공");
        }

        OnBalanceMinigameSucceeded?.Invoke();
        OnBalanceMinigameFinished?.Invoke(true);
    }

    /// <summary>
    /// 미니게임 실패 처리.
    /// </summary>
    private void FinishAsFail()
    {
        if (!isPlaying)
        {
            return;
        }

        isPlaying = false;
        remainingTime = 0f;

        if (verboseLog)
        {
            Debug.Log("[HandClap_BalanceMinigameController] 균형 회복 실패");
        }

        OnBalanceMinigameFailed?.Invoke();
        OnBalanceMinigameFinished?.Invoke(false);
    }
}