using UnityEngine;

[DisallowMultipleComponent]
public class CrowdBounceCheer : MonoBehaviour
{
    [Header("Targets")]
    [SerializeField] private Transform body;
    [SerializeField] private Transform[] hands;

    [Header("Body Bounce")]
    [SerializeField, Min(0f)] private float bodyBounceHeight = 0.18f;
    [SerializeField, Min(0f)] private float bodySquashAmount = 0.06f;

    [Header("Hand Bounce")]
    [SerializeField, Min(0f)] private float handBounceHeight = 0.28f;
    [SerializeField] private float handSideOffset = 0.04f;
    [SerializeField] private float handRotationAngle = 12f;

    [Header("Timing")]
    [SerializeField, Min(0f)] private float bounceSpeedMin = 1.4f;
    [SerializeField, Min(0f)] private float bounceSpeedMax = 2.4f;
    [SerializeField, Min(0f)] private float speedChangeIntervalMin = 0.8f;
    [SerializeField, Min(0f)] private float speedChangeIntervalMax = 1.8f;
    [SerializeField, Min(0f)] private float speedChangeSmoothness = 3f;
    [SerializeField] private bool randomizeStartPhase = true;

    [Header("Variation")]
    [SerializeField, Range(0f, 1f)] private float handPhaseOffset = 0.15f;
    [SerializeField, Range(0f, 1f)] private float randomIntensity = 0.15f;

    private Vector3 bodyBaseLocalPosition;
    private Vector3 bodyBaseLocalScale;
    private Vector3[] handBaseLocalPositions;
    private Quaternion[] handBaseLocalRotations;

    private float phase;
    private float currentSpeed;
    private float targetSpeed;
    private float speedChangeTimer;
    private float intensityMultiplier = 1f;

    void Reset()
    {
        body = transform;
    }

    void Awake()
    {
        CacheBasePose();
        InitializeMotion();
    }

    void OnEnable()
    {
        CacheBasePose();
        InitializeMotion();
    }

    void OnValidate()
    {
        bodyBounceHeight = Mathf.Max(0f, bodyBounceHeight);
        bodySquashAmount = Mathf.Max(0f, bodySquashAmount);
        handBounceHeight = Mathf.Max(0f, handBounceHeight);
        bounceSpeedMin = Mathf.Max(0f, bounceSpeedMin);
        bounceSpeedMax = Mathf.Max(bounceSpeedMin, bounceSpeedMax);
        speedChangeIntervalMin = Mathf.Max(0f, speedChangeIntervalMin);
        speedChangeIntervalMax = Mathf.Max(speedChangeIntervalMin, speedChangeIntervalMax);
        speedChangeSmoothness = Mathf.Max(0f, speedChangeSmoothness);
    }

    void Update()
    {
        UpdateSpeed();
        phase = Mathf.Repeat(phase + currentSpeed * Time.deltaTime, 1f);

        float bodyBounce = GetBounce01(phase);
        ApplyBody(bodyBounce);
        ApplyHands(bodyBounce);
    }

    void CacheBasePose()
    {
        if (body == null)
            body = transform;

        if (body != null)
        {
            bodyBaseLocalPosition = body.localPosition;
            bodyBaseLocalScale = body.localScale;
        }

        int handCount = hands != null ? hands.Length : 0;
        handBaseLocalPositions = new Vector3[handCount];
        handBaseLocalRotations = new Quaternion[handCount];

        for (int i = 0; i < handCount; i++)
        {
            if (hands[i] == null)
                continue;

            handBaseLocalPositions[i] = hands[i].localPosition;
            handBaseLocalRotations[i] = hands[i].localRotation;
        }
    }

    void InitializeMotion()
    {
        phase = randomizeStartPhase ? Random.value : 0f;
        intensityMultiplier = Random.Range(1f - randomIntensity, 1f + randomIntensity);
        PickNewSpeed(immediate: true);
    }

    void UpdateSpeed()
    {
        speedChangeTimer -= Time.deltaTime;
        if (speedChangeTimer <= 0f)
            PickNewSpeed(immediate: false);

        if (speedChangeSmoothness <= 0f)
        {
            currentSpeed = targetSpeed;
            return;
        }

        currentSpeed = Mathf.Lerp(
            currentSpeed,
            targetSpeed,
            1f - Mathf.Exp(-speedChangeSmoothness * Time.deltaTime));
    }

    void PickNewSpeed(bool immediate)
    {
        targetSpeed = Random.Range(bounceSpeedMin, bounceSpeedMax);
        speedChangeTimer = Random.Range(speedChangeIntervalMin, speedChangeIntervalMax);

        if (immediate)
            currentSpeed = targetSpeed;
    }

    void ApplyBody(float bounce)
    {
        if (body == null)
            return;

        float height = bodyBounceHeight * intensityMultiplier;
        body.localPosition = bodyBaseLocalPosition + Vector3.up * (bounce * height);

        float squash = bodySquashAmount * (1f - bounce) * intensityMultiplier;
        body.localScale = new Vector3(
            bodyBaseLocalScale.x * (1f + squash),
            bodyBaseLocalScale.y * Mathf.Max(0f, 1f - squash),
            bodyBaseLocalScale.z);
    }

    void ApplyHands(float bodyBounce)
    {
        if (hands == null)
            return;

        for (int i = 0; i < hands.Length; i++)
        {
            Transform hand = hands[i];
            if (hand == null)
                continue;

            float handPhase = Mathf.Repeat(phase + handPhaseOffset + i * 0.08f, 1f);
            float handBounce = Mathf.Max(bodyBounce, GetBounce01(handPhase));
            float side = i % 2 == 0 ? -1f : 1f;

            Vector3 offset = new(
                side * handSideOffset * Mathf.Sin(handPhase * Mathf.PI * 2f),
                handBounce * handBounceHeight * intensityMultiplier,
                0f);

            hand.localPosition = handBaseLocalPositions[i] + offset;
            hand.localRotation = handBaseLocalRotations[i]
                * Quaternion.Euler(0f, 0f, side * handRotationAngle * handBounce);
        }
    }

    static float GetBounce01(float normalizedPhase)
    {
        float wave = Mathf.Sin(normalizedPhase * Mathf.PI);
        return wave * wave;
    }
}
