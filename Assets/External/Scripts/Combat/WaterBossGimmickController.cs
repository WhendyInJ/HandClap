using System.Collections;
using UnityEngine;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
public class WaterBossGimmickController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private EnemyController enemyController;
    [SerializeField] private CombatActorController enemyActor;
    [SerializeField] private CombatActorController playerActor;
    [SerializeField] private CombatHealth enemyHealth;

    [Header("Water Heal Gimmick")]
    [SerializeField] private bool enableWaterGimmick = true;
    [SerializeField, Range(0f, 1f)] private float triggerHealthNormalized = 0.5f;
    [SerializeField, Min(1)] private int healAttemptCount = 3;
    [SerializeField, Range(0f, 1f)] private float healAmountNormalized = 0.1f;
    [SerializeField, Min(0f)] private float healTelegraphFillDuration = 1f;
    [FormerlySerializedAs("attackInterruptWindowDuration")]
    [SerializeField, Min(0f)] private float healTelegraphHoldDuration = 0.5f;
    [SerializeField, Min(0f)] private float delayBetweenHealAttempts = 0.15f;
    [SerializeField] private SpriteFillColorMode telegraphColorMode = SpriteFillColorMode.Gimmick;

    [Header("Heal Effect")]
    [SerializeField] private GameObject healEffectPrefab;
    [SerializeField] private Transform healEffectSpawnPoint;
    [SerializeField] private Transform healEffectParentOverride;
    [SerializeField, Min(0f)] private float healEffectRandomCircleRadius = 0.25f;
    [SerializeField] private bool useHealEffectSpawnPointRotation = true;
    [SerializeField, Min(0f)] private float healEffectBobAmplitude = 0.12f;
    [SerializeField, Min(0f)] private float healEffectBobSpeed = 1.5f;
    [SerializeField] private float healEffectYRotationSpeed = 180f;

    [Header("Cancel Face")]
    [SerializeField] private SpriteRenderer faceRenderer;
    [SerializeField] private Sprite interruptedHealFaceSprite;
    [SerializeField, Min(0f)] private float interruptedFaceDuration = 0.35f;

    [Header("Debug")]
    [SerializeField] private bool logWaterGimmick = true;

    private Coroutine gimmickRoutine;
    private bool wasAtOrBelowThreshold;
    private bool interruptWindowActive;
    private bool interruptRequested;
    private Coroutine faceReactionRoutine;
    private Sprite faceSpriteBeforeReaction;
    private bool faceReactionActive;

    void Reset()
    {
        TryAutoAssignReferences();
    }

    void Awake()
    {
        TryAutoAssignReferences();
    }

    void OnEnable()
    {
        TryAutoAssignReferences();
        SubscribeEvents();
        wasAtOrBelowThreshold = IsAtOrBelowTriggerHealth();
    }

    void OnDisable()
    {
        UnsubscribeEvents();

        if (gimmickRoutine != null)
        {
            StopCoroutine(gimmickRoutine);
            gimmickRoutine = null;
        }

        RestoreInterruptedFace();

        interruptWindowActive = false;
        interruptRequested = false;

        if (enemyController != null)
        {
            enemyController.SetGimmickIncomingDamageSuppressed(false);
            enemyController.SetGimmickTelegraphActive(false, 0f, telegraphColorMode);
            enemyController.SetManualAiPaused(false);
        }
    }

    void OnValidate()
    {
        triggerHealthNormalized = Mathf.Clamp01(triggerHealthNormalized);
        healAttemptCount = Mathf.Max(1, healAttemptCount);
        healAmountNormalized = Mathf.Clamp01(healAmountNormalized);
        healTelegraphFillDuration = Mathf.Max(0f, healTelegraphFillDuration);
        healTelegraphHoldDuration = Mathf.Max(0f, healTelegraphHoldDuration);
        delayBetweenHealAttempts = Mathf.Max(0f, delayBetweenHealAttempts);
        healEffectRandomCircleRadius = Mathf.Max(0f, healEffectRandomCircleRadius);
        healEffectBobAmplitude = Mathf.Max(0f, healEffectBobAmplitude);
        healEffectBobSpeed = Mathf.Max(0f, healEffectBobSpeed);
        interruptedFaceDuration = Mathf.Max(0f, interruptedFaceDuration);
    }

    void HandleEnemyHealthChanged(CombatHealth changedHealth)
    {
        if (!enableWaterGimmick || changedHealth != enemyHealth || enemyHealth == null)
            return;

        bool isAtOrBelowThreshold = IsAtOrBelowTriggerHealth();
        if (gimmickRoutine == null && !wasAtOrBelowThreshold && isAtOrBelowThreshold)
            gimmickRoutine = StartCoroutine(RunWaterHealGimmick());

        if (gimmickRoutine == null)
            wasAtOrBelowThreshold = isAtOrBelowThreshold;
    }

    void HandlePlayerCombatEvent(CombatEventData eventData)
    {
        if (!interruptWindowActive
            || eventData.Actor != playerActor
            || eventData.Opponent != enemyActor)
            return;

        if (eventData.Kind == CombatEventKind.AttackHit || eventData.Kind == CombatEventKind.AttackMeetingWin)
            interruptRequested = true;
    }

    IEnumerator RunWaterHealGimmick()
    {
        LogWaterGimmick("Water gimmick started.");

        if (enemyController != null)
            enemyController.SetManualAiPaused(true);

        while (CanContinueGimmick() && enemyActor != null && !enemyActor.CanAttemptDecision)
            yield return null;

        for (int attemptIndex = 0; attemptIndex < healAttemptCount; attemptIndex++)
        {
            if (!CanContinueGimmick())
                break;

            GameObject healEffectInstance = SpawnHealEffect();
            yield return RunHealTelegraph();

            if (!CanContinueGimmick())
            {
                DestroyHealEffect(healEffectInstance);
                break;
            }

            interruptRequested = false;
            interruptWindowActive = true;
            SetIncomingDamageSuppressed(true);
            yield return WaitForInterruptWindow();
            interruptWindowActive = false;

            if (!CanContinueGimmick())
            {
                SetIncomingDamageSuppressed(false);
                DestroyHealEffect(healEffectInstance);
                break;
            }

            if (interruptRequested)
            {
                LogWaterGimmick($"Water heal {attemptIndex + 1}/{healAttemptCount} interrupted.");
                PlayInterruptedFaceReaction();
                yield return WaitForPlayerAttackToFinish();
            }
            else if (enemyHealth != null)
            {
                float healed = enemyHealth.Heal(enemyHealth.MaxHealth * healAmountNormalized);
                LogWaterGimmick($"Water heal {attemptIndex + 1}/{healAttemptCount}: {healed:F2}");
            }

            SetIncomingDamageSuppressed(false);
            DestroyHealEffect(healEffectInstance);

            if (delayBetweenHealAttempts > 0f && attemptIndex < healAttemptCount - 1)
                yield return new WaitForSeconds(delayBetweenHealAttempts);
        }

        interruptWindowActive = false;
        interruptRequested = false;
        SetIncomingDamageSuppressed(false);

        if (enemyController != null)
        {
            enemyController.SetGimmickTelegraphActive(false, 0f, telegraphColorMode);
            enemyController.SetManualAiPaused(false);
        }

        gimmickRoutine = null;
        wasAtOrBelowThreshold = IsAtOrBelowTriggerHealth();
        LogWaterGimmick("Water gimmick ended.");
    }

    IEnumerator RunHealTelegraph()
    {
        if (enemyController == null)
            yield break;

        enemyController.SetGimmickTelegraphActive(true, 0f, telegraphColorMode);

        if (healTelegraphFillDuration <= 0f)
        {
            enemyController.SetGimmickTelegraphFill(1f);
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < healTelegraphFillDuration && CanContinueGimmick())
        {
            elapsed += Time.deltaTime;
            enemyController.SetGimmickTelegraphFill(Mathf.Clamp01(elapsed / healTelegraphFillDuration));
            yield return null;
        }

        enemyController.SetGimmickTelegraphFill(1f);
    }

    IEnumerator WaitForInterruptWindow()
    {
        float elapsed = 0f;
        while (elapsed < healTelegraphHoldDuration && CanContinueGimmick() && !interruptRequested)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    IEnumerator WaitForPlayerAttackToFinish()
    {
        while (CanContinueGimmick()
            && playerActor != null
            && (playerActor.ResolutionCombatState == CombatState.Attack
                || playerActor.CurrentCombatState == CombatState.Attack))
        {
            yield return null;
        }
    }

    GameObject SpawnHealEffect()
    {
        if (healEffectPrefab == null)
            return null;

        Transform spawnPoint = healEffectSpawnPoint != null
            ? healEffectSpawnPoint
            : enemyActor != null ? enemyActor.transform : transform;
        if (spawnPoint == null)
            return null;

        Vector2 randomOffset = Random.insideUnitCircle * healEffectRandomCircleRadius;
        Vector3 spawnPosition = spawnPoint.position + new Vector3(randomOffset.x, randomOffset.y, 0f);
        Quaternion spawnRotation = useHealEffectSpawnPointRotation
            ? spawnPoint.rotation
            : healEffectPrefab.transform.rotation;

        GameObject instance = Instantiate(healEffectPrefab, spawnPosition, spawnRotation, healEffectParentOverride);
        WaterBossHealEffectAnimator animator = instance.GetComponent<WaterBossHealEffectAnimator>();
        if (animator == null)
            animator = instance.AddComponent<WaterBossHealEffectAnimator>();

        animator.Configure(
            healEffectBobAmplitude,
            healEffectBobSpeed,
            healEffectYRotationSpeed);
        return instance;
    }

    void DestroyHealEffect(GameObject healEffectInstance)
    {
        if (healEffectInstance != null)
            Destroy(healEffectInstance);
    }

    void SetIncomingDamageSuppressed(bool suppressed)
    {
        if (enemyController != null)
            enemyController.SetGimmickIncomingDamageSuppressed(suppressed);
    }

    void PlayInterruptedFaceReaction()
    {
        if (faceRenderer == null || interruptedHealFaceSprite == null)
            return;

        if (faceReactionRoutine != null)
        {
            StopCoroutine(faceReactionRoutine);
            faceReactionRoutine = null;
        }

        if (!faceReactionActive)
        {
            faceSpriteBeforeReaction = faceRenderer.sprite;
            faceReactionActive = true;
        }

        faceRenderer.sprite = interruptedHealFaceSprite;
        faceReactionRoutine = StartCoroutine(RestoreInterruptedFaceAfterDelay());
    }

    IEnumerator RestoreInterruptedFaceAfterDelay()
    {
        float elapsed = 0f;
        while (elapsed < interruptedFaceDuration)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        faceReactionRoutine = null;
        RestoreInterruptedFace();
    }

    void RestoreInterruptedFace()
    {
        if (faceReactionRoutine != null)
        {
            StopCoroutine(faceReactionRoutine);
            faceReactionRoutine = null;
        }

        if (faceRenderer != null && faceReactionActive)
            faceRenderer.sprite = faceSpriteBeforeReaction;

        faceReactionActive = false;
        faceSpriteBeforeReaction = null;
    }

    bool CanContinueGimmick()
    {
        return enableWaterGimmick
            && enemyHealth != null
            && enemyHealth.IsAlive
            && enemyActor != null
            && enemyActor.RoundCombatActive;
    }

    bool IsAtOrBelowTriggerHealth()
    {
        return enemyHealth != null && enemyHealth.Normalized <= triggerHealthNormalized;
    }

    void SubscribeEvents()
    {
        if (enemyHealth != null)
        {
            enemyHealth.HealthChanged -= HandleEnemyHealthChanged;
            enemyHealth.HealthChanged += HandleEnemyHealthChanged;
        }

        if (playerActor != null)
        {
            playerActor.CombatEventRaised -= HandlePlayerCombatEvent;
            playerActor.CombatEventRaised += HandlePlayerCombatEvent;
        }
    }

    void UnsubscribeEvents()
    {
        if (enemyHealth != null)
            enemyHealth.HealthChanged -= HandleEnemyHealthChanged;

        if (playerActor != null)
        {
            playerActor.CombatEventRaised -= HandlePlayerCombatEvent;
        }
    }

    void TryAutoAssignReferences()
    {
        if (enemyController == null)
            TryGetComponent(out enemyController);

        if (enemyActor == null)
        {
            if (enemyController != null)
                enemyActor = enemyController.ActorController;
            else
                TryGetComponent(out enemyActor);
        }

        if (enemyHealth == null && enemyActor != null)
            enemyHealth = enemyActor.Health;

        if (playerActor == null && enemyController != null)
            playerActor = enemyController.TargetController;

        if (playerActor == null)
            playerActor = FindPlayerActor();

        if (faceRenderer == null)
            faceRenderer = FindFaceRenderer();
    }

    CombatActorController FindPlayerActor()
    {
        CombatActorController[] actors = FindObjectsByType<CombatActorController>(FindObjectsSortMode.None);
        for (int i = 0; i < actors.Length; i++)
        {
            CombatActorController actor = actors[i];
            if (actor != null && actor.ActorSide == CombatActorSide.Player)
                return actor;
        }

        return null;
    }

    SpriteRenderer FindFaceRenderer()
    {
        SpriteRenderer[] spriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < spriteRenderers.Length; i++)
        {
            SpriteRenderer spriteRenderer = spriteRenderers[i];
            if (spriteRenderer != null && spriteRenderer.name == "Face")
                return spriteRenderer;
        }

        return null;
    }

    void LogWaterGimmick(string message)
    {
        if (logWaterGimmick)
            Debug.Log($"[WaterBoss] {message}", this);
    }
}
