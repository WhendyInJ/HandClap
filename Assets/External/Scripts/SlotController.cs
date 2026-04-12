using UnityEngine;
using System;
using System.Collections;
using Random = UnityEngine.Random;

public class SlotController : MonoBehaviour
{
    public Reel reelElement;
    public Reel reelBody;
    public Reel reelHand;

    [Tooltip("마지막 릴(Hand) 정지가 끝난 뒤 Space로 다시 돌릴 수 있을 때까지 대기(초).")]
    public float inputCooldownAfterStop = 1f;
    [SerializeField] float delayBetweenReelStops = 0.35f;

    enum Phase
    {
        Idle,
        /// <summary>릴이 돌아가는 중 — Space 한 번 더 누르면 결과 확정.</summary>
        AwaitingCommit,
        /// <summary>릴 순차 정지 연출 중 — Space 무시.</summary>
        Resolving
    }

    Phase phase = Phase.Idle;

    PlayerBuild pendingBuild;
    PlayerBuild currentBuild;
    int pendingSlotElement;
    int pendingSlotBody;
    int pendingSlotHand;
    bool hasResolvedBuild;
    bool keyboardInputEnabled = true;

    public event Action<PlayerBuild> BuildResolved;

    public bool HasResolvedBuild => hasResolvedBuild;
    public bool IsBusy => phase != Phase.Idle;
    public PlayerBuild CurrentBuild => currentBuild;

    void Update()
    {
        if (!keyboardInputEnabled)
            return;

        if (!Input.GetKeyDown(KeyCode.Space))
            return;

        switch (phase)
        {
            case Phase.Idle:
                BeginSpinPhase();
                break;
            case Phase.AwaitingCommit:
                phase = Phase.Resolving;
                StartCoroutine(ResolveSpinCoroutine());
                break;
            default:
                break;
        }
    }

    public void SetKeyboardInputEnabled(bool isEnabled)
    {
        keyboardInputEnabled = isEnabled;
    }

    void BeginSpinPhase()
    {
        phase = Phase.AwaitingCommit;

        pendingBuild = new PlayerBuild
        {
            Element = (Element)Random.Range(0, SlotReelLayout.VariantCount),
            BodyType = (BodyType)Random.Range(0, SlotReelLayout.VariantCount),
            HandSize = (HandSize)Random.Range(0, SlotReelLayout.VariantCount)
        };

        pendingSlotElement = reelElement.GetRandomIndexForSlotType(pendingBuild.Element);
        pendingSlotBody = reelBody.GetRandomIndexForSlotType(pendingBuild.BodyType);
        pendingSlotHand = reelHand.GetRandomIndexForSlotType(pendingBuild.HandSize);

        reelElement.StartSpin();
        reelBody.StartSpin();
        reelHand.StartSpin();

        Debug.Log("🎰 슬롯 시작 — Space를 다시 눌러 릴을 멈춥니다.");
    }

    public bool TryBeginManualReroll()
    {
        if (phase != Phase.Idle)
            return false;

        BeginSpinPhase();
        return true;
    }

    public void AutoReroll(float previewDuration = 0.9f)
    {
        if (phase != Phase.Idle)
            return;

        StartCoroutine(AutoRerollRoutine(previewDuration));
    }

    IEnumerator ResolveSpinCoroutine()
    {
        yield return StartCoroutine(reelElement.CoStop(pendingSlotElement));

        yield return new WaitForSeconds(delayBetweenReelStops);
        yield return StartCoroutine(reelBody.CoStop(pendingSlotBody));

        yield return new WaitForSeconds(delayBetweenReelStops);
        yield return StartCoroutine(reelHand.CoStop(pendingSlotHand));

        Debug.Log(
            $"결과 → Element: {pendingBuild.Element} (릴인덱스 {pendingSlotElement}), " +
            $"Body: {pendingBuild.BodyType} ({pendingSlotBody}), Hand: {pendingBuild.HandSize} ({pendingSlotHand})");

        currentBuild = pendingBuild;
        hasResolvedBuild = true;
        BuildResolved?.Invoke(currentBuild);

        yield return new WaitForSeconds(inputCooldownAfterStop);

        phase = Phase.Idle;
    }

    IEnumerator AutoRerollRoutine(float previewDuration)
    {
        BeginSpinPhase();
        yield return new WaitForSeconds(Mathf.Max(0f, previewDuration));

        if (phase != Phase.AwaitingCommit)
            yield break;

        phase = Phase.Resolving;
        yield return StartCoroutine(ResolveSpinCoroutine());
    }
}