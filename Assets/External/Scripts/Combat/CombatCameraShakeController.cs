using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class CombatCameraShakeController : MonoBehaviour
{
    [Header("Combat Actors")]
    [SerializeField] private CombatActorController playerController;
    [SerializeField] private CombatActorController enemyController;

    [Header("Shake Target")]
    [SerializeField] private Transform shakeTarget;

    [Header("Attack Meeting Shake")]
    [SerializeField] private bool shakeOnAttackClash = true;
    [SerializeField] private bool shakeOnAttackMeetingWin = true;
    [SerializeField, Min(0f)] private float shakeDuration = 0.16f;
    [SerializeField, Min(0f)] private float shakeStrength = 0.16f;
    [SerializeField, Min(1f)] private float shakeFrequency = 45f;
    [SerializeField, Min(0f)] private float duplicateEventCooldown = 0.05f;

    private Coroutine shakeRoutine;
    private Vector3 baseLocalPosition;
    private bool hasBaseLocalPosition;
    private float nextShakeTime;

    void Reset()
    {
        shakeTarget = transform;
        TryAutoAssignActors();
    }

    void Awake()
    {
        if (shakeTarget == null)
            shakeTarget = transform;

        CacheBasePosition();
        TryAutoAssignActors();
    }

    void OnEnable()
    {
        if (shakeTarget == null)
            shakeTarget = transform;

        CacheBasePosition();
        TryAutoAssignActors();
        SubscribeCombatEvents();
    }

    void OnDisable()
    {
        UnsubscribeCombatEvents();

        if (shakeRoutine != null)
        {
            StopCoroutine(shakeRoutine);
            shakeRoutine = null;
        }

        RestoreBasePosition();
    }

    void HandleCombatEvent(CombatEventData eventData)
    {
        if (!ShouldShakeForEvent(eventData))
            return;

        PlayShake();
    }

    bool ShouldShakeForEvent(CombatEventData eventData)
    {
        if (Time.time < nextShakeTime)
            return false;

        switch (eventData.Kind)
        {
            case CombatEventKind.AttackClashed:
                if (!shakeOnAttackClash)
                    return false;

                return playerController == null || eventData.Actor == playerController;

            case CombatEventKind.AttackMeetingWin:
                return shakeOnAttackMeetingWin;

            default:
                return false;
        }
    }

    public void PlayShake()
    {
        PlayShake(shakeDuration, shakeStrength, shakeFrequency);
    }

    public void PlayShake(float duration, float strength, float frequency)
    {
        if (shakeTarget == null || duration <= 0f || strength <= 0f)
            return;

        nextShakeTime = Time.time + duplicateEventCooldown;

        if (shakeRoutine != null)
            StopCoroutine(shakeRoutine);

        shakeRoutine = StartCoroutine(ShakeRoutine(duration, strength, Mathf.Max(1f, frequency)));
    }

    IEnumerator ShakeRoutine(float duration, float strength, float frequency)
    {
        CacheBasePosition();

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float normalizedTime = Mathf.Clamp01(elapsed / duration);
            float damping = 1f - normalizedTime;
            float sampleTime = elapsed * frequency;

            Vector2 noise = new(
                Mathf.PerlinNoise(sampleTime, 0.37f) * 2f - 1f,
                Mathf.PerlinNoise(0.73f, sampleTime) * 2f - 1f);

            if (noise.sqrMagnitude > 1f)
                noise.Normalize();

            shakeTarget.localPosition = baseLocalPosition
                + new Vector3(noise.x, noise.y, 0f) * (strength * damping);

            yield return null;
        }

        RestoreBasePosition();
        shakeRoutine = null;
    }

    void CacheBasePosition()
    {
        if (shakeTarget == null)
            return;

        if (hasBaseLocalPosition && shakeRoutine != null)
            return;

        baseLocalPosition = shakeTarget.localPosition;
        hasBaseLocalPosition = true;
    }

    void RestoreBasePosition()
    {
        if (shakeTarget != null && hasBaseLocalPosition)
            shakeTarget.localPosition = baseLocalPosition;
    }

    void SubscribeCombatEvents()
    {
        if (playerController != null)
        {
            playerController.CombatEventRaised -= HandleCombatEvent;
            playerController.CombatEventRaised += HandleCombatEvent;
        }

        if (enemyController != null && enemyController != playerController)
        {
            enemyController.CombatEventRaised -= HandleCombatEvent;
            enemyController.CombatEventRaised += HandleCombatEvent;
        }
    }

    void UnsubscribeCombatEvents()
    {
        if (playerController != null)
            playerController.CombatEventRaised -= HandleCombatEvent;

        if (enemyController != null && enemyController != playerController)
            enemyController.CombatEventRaised -= HandleCombatEvent;
    }

    void TryAutoAssignActors()
    {
        if (playerController != null && enemyController != null)
            return;

        BattleUiController battleUi = FindFirstObjectByType<BattleUiController>();
        if (battleUi != null)
        {
            if (playerController == null)
                playerController = battleUi.PlayerController;

            if (enemyController == null)
                enemyController = battleUi.EnemyController;
        }

        if (playerController != null && enemyController != null)
            return;

        CombatActorController[] controllers = FindObjectsByType<CombatActorController>(FindObjectsSortMode.None);
        for (int i = 0; i < controllers.Length; i++)
        {
            CombatActorController controller = controllers[i];
            if (controller == null)
                continue;

            if (playerController == null && controller.ActorSide == CombatActorSide.Player)
                playerController = controller;
            else if (enemyController == null && controller.ActorSide == CombatActorSide.Enemy)
                enemyController = controller;
        }
    }
}
