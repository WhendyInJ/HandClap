using UnityEngine;

[DisallowMultipleComponent]
public class WaterBossHealEffectAnimator : MonoBehaviour
{
    [SerializeField, Min(0f)] private float bobAmplitude = 0.12f;
    [SerializeField, Min(0f)] private float bobSpeed = 1.5f;
    [SerializeField] private float yRotationSpeed = 180f;

    private Vector3 baseLocalPosition;
    private Quaternion baseLocalRotation;
    private float timer;

    void Awake()
    {
        CacheBaseTransform();
    }

    void OnEnable()
    {
        CacheBaseTransform();
        timer = 0f;
    }

    void Update()
    {
        timer += Time.deltaTime;

        float bobOffset = Mathf.Sin(timer * bobSpeed * Mathf.PI * 2f) * bobAmplitude;
        float yRotation = GetSteppedYRotation();

        transform.localPosition = baseLocalPosition + Vector3.up * bobOffset;
        transform.localRotation = baseLocalRotation * Quaternion.Euler(0f, yRotation, 0f);
    }

    public void Configure(float newBobAmplitude, float newBobSpeed, float newYRotationSpeed)
    {
        bobAmplitude = Mathf.Max(0f, newBobAmplitude);
        bobSpeed = Mathf.Max(0f, newBobSpeed);
        yRotationSpeed = newYRotationSpeed;
    }

    void CacheBaseTransform()
    {
        baseLocalPosition = transform.localPosition;
        baseLocalRotation = transform.localRotation;
    }

    float GetSteppedYRotation()
    {
        float degreesPerStep = 180f;
        float speed = Mathf.Abs(yRotationSpeed);
        if (speed <= 0.0001f)
            return 0f;

        float stepDuration = degreesPerStep / speed;
        float step = Mathf.Floor(timer / stepDuration);
        return step * degreesPerStep * Mathf.Sign(yRotationSpeed);
    }
}
