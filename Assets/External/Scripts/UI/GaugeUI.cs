using UnityEngine;

/// <summary>
/// UI 계기판 침. 기본은 90°(쉼) → -90°(끝까지). invertSweep 켜면 -90° → 90°.
/// 스페이스를 누르는 동안 속도가 0에서 최대까지 가속되고,
/// 떼는 순간 속도가 0으로 리셋된 뒤, 내려올 때도 다시 0에서 가속합니다.
/// </summary>
[DisallowMultipleComponent]
public class GaugeUI : MonoBehaviour
{
    [SerializeField] RectTransform needle;
    [Tooltip("키를 누르지 않았을 때 Z 각도(도).")]
    [SerializeField] private float angleAtRest = 90f;
    [Tooltip("키를 끝까지 눌렀을 때 Z 각도(도).")]
    [SerializeField] private float angleAtFull = -90f;
    [SerializeField] private KeyCode holdKey = KeyCode.Space;
    [Tooltip("체크 시 침 범위를 반대로: needleT 0 = angleAtFull, 1 = angleAtRest (기본 각도면 -90°에서 90°로).")]
    [SerializeField] private bool invertSweep;

    [Header("속도 (needleT 0→1 / 초)")]
    [Tooltip("끝까지 밟았을 때 이 속도에 도달하면 더 이상 안 빨라짐.")]
    [Min(0.01f)] [SerializeField] private float maxSpeed = 1.35f;
    [Tooltip("누르고 있을 때 초당 속도 증가량. 클수록 빨리 최대 속도까지 도달.")]
    [Min(0.01f)] [SerializeField] private float accelerationWhilePressed = 2.8f;
    [Tooltip("뗀 뒤 아래로 갈 때 가속(절댓값). 올라갈 때와 같거나 다르게 줄 수 있음.")]
    [Min(0.01f)] [SerializeField] private float accelerationWhileReleased = 2.8f;
    [Tooltip("스페이스를 막 누른 프레임에 속도 0으로(다시 채우기).")]
    [SerializeField] private bool resetSpeedOnPressDown = true;
    [Tooltip("스페이스를 뗀 프레임에 속도 0으로(내려올 때도 처음부터 가속).")]
    [SerializeField] private bool resetSpeedOnRelease = true;

    float needleT;
    float needleSpeed;

    void Reset()
    {
        if (needle == null)
        {
            Transform child = transform.Find("Needle");
            if (child != null)
                needle = child as RectTransform;
        }
    }

    void Update()
    {
        if (needle == null)
            return;

        float dt = Time.deltaTime;

        if (Input.GetKey(holdKey))
        {
            if (resetSpeedOnPressDown && Input.GetKeyDown(holdKey))
                needleSpeed = 0f;

            if (needleT < 1f)
            {
                needleSpeed = Mathf.MoveTowards(needleSpeed, maxSpeed, accelerationWhilePressed * dt);
                needleT = Mathf.Clamp01(needleT + needleSpeed * dt);
            }
            else
            {
                needleT = 1f;
                needleSpeed = Mathf.Min(needleSpeed, maxSpeed);
            }
        }
        else
        {
            if (resetSpeedOnRelease && Input.GetKeyUp(holdKey))
                needleSpeed = 0f;

            if (needleT > 0f)
            {
                needleSpeed = Mathf.MoveTowards(needleSpeed, -maxSpeed, accelerationWhileReleased * dt);
                needleT = Mathf.Clamp01(needleT + needleSpeed * dt);
            }

            if (needleT <= 0f)
            {
                needleT = 0f;
                needleSpeed = 0f;
            }
        }

        ApplyNeedleRotation();
    }

    public float NormalizedNeedle => needleT;

    public float GetAngleForNormalized(float normalized)
    {
        normalized = Mathf.Clamp01(normalized);

        return invertSweep
            ? Mathf.Lerp(angleAtFull, angleAtRest, normalized)
            : Mathf.Lerp(angleAtRest, angleAtFull, normalized);
    }

    public void SetNeedleImmediate(float normalized)
    {
        needleT = Mathf.Clamp01(normalized);
        needleSpeed = 0f;
        ApplyNeedleRotation();
    }

    void ApplyNeedleRotation()
    {
        if (needle == null)
            return;

        float z = GetAngleForNormalized(needleT);
        needle.localRotation = Quaternion.Euler(0f, 0f, z);
    }
}
