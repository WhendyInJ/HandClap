using UnityEngine;
using System.Collections;

public class SlotController : MonoBehaviour
{
    public Reel reelElement;
    public Reel reelBody;
    public Reel reelHand;

    [SerializeField] private CombatActorStats targetStats;

    [Tooltip("마지막 릴(Hand) 정지가 끝난 뒤 Space로 다시 돌릴 수 있을 때까지 대기(초).")]
    public float inputCooldownAfterStop = 1f;
    [SerializeField] float delayBetweenReelStops = 0.35f;

    public event System.Action<PlayerBuild> BuildResolved;

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
    int pendingSlotElement;
    int pendingSlotBody;
    int pendingSlotHand;

    void Reset()
    {
        TryAutoAssignTargetStats();
    }

    void Awake()
    {
        TryAutoAssignTargetStats();
    }

    void Update()
    {
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

        ApplyPendingBuild();

        yield return new WaitForSeconds(inputCooldownAfterStop);

        phase = Phase.Idle;
    }

    void ApplyPendingBuild()
    {
        if (targetStats == null)
            TryAutoAssignTargetStats();

        if (targetStats != null)
            targetStats.ApplyBuild(pendingBuild);

        BuildResolved?.Invoke(pendingBuild);
    }

    void TryAutoAssignTargetStats()
    {
        if (targetStats != null)
            return;

        CombatActorController[] controllers = FindObjectsByType<CombatActorController>(FindObjectsSortMode.None);
        for (int i = 0; i < controllers.Length; i++)
        {
            CombatActorController controller = controllers[i];
            if (controller != null && controller.ActorSide == CombatActorSide.Player)
            {
                targetStats = controller.Stats;
                return;
            }
        }
    }
}
