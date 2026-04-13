using UnityEngine;

[DisallowMultipleComponent]
public class LaserZRotationSway : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform targetTransform;
    [SerializeField] private bool useLocalRotation = true;
    [SerializeField] private bool captureBaseRotationOnEnable = true;

    [Header("Swing Angle")]
    [SerializeField] private float leftAngleOffset = -12f;
    [SerializeField] private float rightAngleOffset = 12f;

    [Header("Changing Speed")]
    [Tooltip("Swing speed in left-right cycles per second.")]
    [SerializeField, Min(0f)] private float speedMin = 0.35f;
    [SerializeField, Min(0f)] private float speedMax = 1.4f;
    [Tooltip("How often a new target speed is picked.")]
    [SerializeField, Min(0f)] private float speedChangeIntervalMin = 0.25f;
    [SerializeField, Min(0f)] private float speedChangeIntervalMax = 0.9f;
    [Tooltip("Higher values make speed changes snap faster.")]
    [SerializeField, Min(0f)] private float speedChangeSmoothness = 5f;

    [Header("Start")]
    [SerializeField] private bool randomizeStartPhase = true;
    [SerializeField, Range(0f, 1f)] private float startPhase;

    private float baseZRotation;
    private float phase;
    private float currentSpeed;
    private float targetSpeed;
    private float speedChangeTimer;

    void Reset()
    {
        targetTransform = transform;
    }

    void Awake()
    {
        EnsureTarget();
        CaptureBaseRotation();
        PickNewSpeed(immediate: true);
        phase = randomizeStartPhase ? Random.value : startPhase;
    }

    void OnEnable()
    {
        EnsureTarget();

        if (captureBaseRotationOnEnable)
            CaptureBaseRotation();

        PickNewSpeed(immediate: true);
        phase = randomizeStartPhase ? Random.value : startPhase;
    }

    void OnValidate()
    {
        speedMin = Mathf.Max(0f, speedMin);
        speedMax = Mathf.Max(speedMin, speedMax);
        speedChangeIntervalMin = Mathf.Max(0f, speedChangeIntervalMin);
        speedChangeIntervalMax = Mathf.Max(speedChangeIntervalMin, speedChangeIntervalMax);
        speedChangeSmoothness = Mathf.Max(0f, speedChangeSmoothness);
    }

    void Update()
    {
        EnsureTarget();
        if (targetTransform == null)
            return;

        UpdateSpeed();
        phase = Mathf.Repeat(phase + currentSpeed * Time.deltaTime, 1f);

        float wave = Mathf.Sin(phase * Mathf.PI * 2f) * 0.5f + 0.5f;
        float zOffset = Mathf.Lerp(leftAngleOffset, rightAngleOffset, wave);
        ApplyZRotation(baseZRotation + zOffset);
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
        targetSpeed = Random.Range(speedMin, speedMax);
        speedChangeTimer = Random.Range(speedChangeIntervalMin, speedChangeIntervalMax);

        if (immediate)
            currentSpeed = targetSpeed;
    }

    void CaptureBaseRotation()
    {
        if (targetTransform == null)
            return;

        baseZRotation = useLocalRotation
            ? targetTransform.localEulerAngles.z
            : targetTransform.eulerAngles.z;
    }

    void ApplyZRotation(float zRotation)
    {
        if (useLocalRotation)
        {
            Vector3 euler = targetTransform.localEulerAngles;
            euler.z = zRotation;
            targetTransform.localEulerAngles = euler;
            return;
        }

        Vector3 worldEuler = targetTransform.eulerAngles;
        worldEuler.z = zRotation;
        targetTransform.eulerAngles = worldEuler;
    }

    void EnsureTarget()
    {
        if (targetTransform == null)
            targetTransform = transform;
    }
}
