using System.Collections;
using UnityEngine;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
public class ElectricBossGimmickController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private EnemyController enemyController;
    [SerializeField] private CombatActorController enemyActor;
    [SerializeField] private CombatActorController playerActor;
    [SerializeField] private BattleUiController battleUiController;
    [SerializeField] private CombatCameraShakeController cameraShakeController;

    [Header("Gauge")]
    [SerializeField] private GameObject[] gaugeSlots;
    [SerializeField, Min(1)] private int requiredGaugeSlots = 4;

    [Header("Overcharge")]
    [FormerlySerializedAs("enemySuccessfulAttacksToArm")]
    [FormerlySerializedAs("dodgeSuccessesAfterFullGaugeToArm")]
    [SerializeField, Min(1)] private int playerDodgeSuccessesAfterFullGaugeToArm = 3;

    [Header("Gimmick Cast")]
    [SerializeField] private bool enableElectricGimmick = true;
    [SerializeField] private bool waitUntilEnemyReadyToCast = true;
    [SerializeField] private bool pauseEnemyAiDuringGimmick = true;
    [SerializeField, Min(0f)] private float damageToPlayer = 1f;
    [SerializeField, Min(0f)] private float castStartDelay = 0f;

    [Header("Camera Shake")]
    [SerializeField, Min(0f)] private float shakeDuration = 0.45f;
    [SerializeField, Min(0f)] private float shakeStrength = 0.35f;
    [SerializeField, Min(1f)] private float shakeFrequency = 60f;

    [Header("Wave Effect")]
    [SerializeField] private GameObject waveEffectPrefab;
    [SerializeField] private Transform waveEffectSpawnPoint;
    [SerializeField] private Transform waveEffectParentOverride;
    [SerializeField, Min(0f)] private float waveEffectRandomCircleRadius = 0f;
    [SerializeField] private bool useWaveEffectSpawnPointRotation = true;
    [SerializeField, Min(0f)] private float waveEffectDuration = 0.7f;
    [SerializeField, Min(0f)] private float waveStartScaleMultiplier = 0.2f;
    [SerializeField, Min(0f)] private float waveEndScaleMultiplier = 3f;
    [SerializeField] private bool fadeOutWaveEffect = true;
    [SerializeField] private bool destroyWaveEffectWhenFinished = true;

    [Header("Debug")]
    [SerializeField] private bool logElectricGimmick = true;

    private int currentGaugeSlots;
    private int playerDodgeSuccessCountAfterFullGauge;
    private bool gimmickArmed;
    private bool isCastingGimmick;
    private bool enemyAiPausedByGimmick;
    private Coroutine gimmickRoutine;

    void Reset()
    {
        TryAutoAssignReferences();
    }

    void Awake()
    {
        TryAutoAssignReferences();
        ApplyGaugeView();
    }

    void OnEnable()
    {
        TryAutoAssignReferences();
        SubscribeEvents();
        ApplyGaugeView();
    }

    void OnDisable()
    {
        UnsubscribeEvents();

        if (gimmickRoutine != null)
        {
            StopCoroutine(gimmickRoutine);
            gimmickRoutine = null;
        }

        isCastingGimmick = false;
        SetEnemyAiPaused(false);
    }

    void OnValidate()
    {
        requiredGaugeSlots = Mathf.Max(1, requiredGaugeSlots);
        playerDodgeSuccessesAfterFullGaugeToArm = Mathf.Max(1, playerDodgeSuccessesAfterFullGaugeToArm);
        damageToPlayer = Mathf.Max(0f, damageToPlayer);
        castStartDelay = Mathf.Max(0f, castStartDelay);
        shakeDuration = Mathf.Max(0f, shakeDuration);
        shakeStrength = Mathf.Max(0f, shakeStrength);
        shakeFrequency = Mathf.Max(1f, shakeFrequency);
        waveEffectRandomCircleRadius = Mathf.Max(0f, waveEffectRandomCircleRadius);
        waveEffectDuration = Mathf.Max(0f, waveEffectDuration);
        waveStartScaleMultiplier = Mathf.Max(0f, waveStartScaleMultiplier);
        waveEndScaleMultiplier = Mathf.Max(0f, waveEndScaleMultiplier);

        if (!Application.isPlaying)
            ApplyGaugeView();
    }

    public void ResetElectricGauge()
    {
        currentGaugeSlots = 0;
        playerDodgeSuccessCountAfterFullGauge = 0;
        gimmickArmed = false;
        ApplyGaugeView();
    }

    void HandlePlayerCombatEvent(CombatEventData eventData)
    {
        if (!enableElectricGimmick || playerActor == null)
            return;

        if (eventData.Actor != playerActor || eventData.Opponent != enemyActor)
            return;

        if (eventData.Kind == CombatEventKind.DodgeSucceeded)
            HandlePlayerDodgeSucceeded();
    }

    void HandlePlayerDodgeSucceeded()
    {
        if (isCastingGimmick || gimmickArmed)
            return;

        if (IsGaugeFull())
        {
            HandlePlayerDodgeSucceededAfterFullGauge();
            return;
        }

        SetGaugeSlots(currentGaugeSlots + 1);
        LogElectricGimmick($"Player dodge success. Gauge charged: {currentGaugeSlots}/{GetFullGaugeSlotCount()}");
    }

    void HandlePlayerDodgeSucceededAfterFullGauge()
    {
        if (!IsGaugeFull() || gimmickArmed || isCastingGimmick)
            return;

        playerDodgeSuccessCountAfterFullGauge++;
        LogElectricGimmick(
            $"Player dodge success after full gauge: {playerDodgeSuccessCountAfterFullGauge}/{playerDodgeSuccessesAfterFullGaugeToArm}");

        if (playerDodgeSuccessCountAfterFullGauge < playerDodgeSuccessesAfterFullGaugeToArm)
            return;

        ArmGimmick();
    }

    void ArmGimmick()
    {
        if (gimmickArmed)
            return;

        gimmickArmed = true;
        LogElectricGimmick("Electric gimmick armed.");

        if (gimmickRoutine == null)
            gimmickRoutine = StartCoroutine(RunGimmickWhenReady());
    }

    IEnumerator RunGimmickWhenReady()
    {
        if (pauseEnemyAiDuringGimmick)
            SetEnemyAiPaused(true);

        while (CanContinueGimmick() && waitUntilEnemyReadyToCast && !IsCastWindowOpen())
            yield return null;

        if (!CanContinueGimmick())
        {
            FinishGimmickRoutine(resetGauge: false, applyDamage: false);
            yield break;
        }

        isCastingGimmick = true;

        if (castStartDelay > 0f)
        {
            yield return WaitForSecondsWhileActive(castStartDelay);
            if (!CanContinueGimmick())
            {
                FinishGimmickRoutine(resetGauge: false, applyDamage: false);
                yield break;
            }
        }

        PlayCameraShake();
        SpawnWaveEffect();

        if (waveEffectDuration > 0f)
        {
            yield return WaitForSecondsWhileActive(waveEffectDuration);
            if (!CanContinueGimmick())
            {
                FinishGimmickRoutine(resetGauge: true, applyDamage: false);
                yield break;
            }
        }

        FinishGimmickRoutine(resetGauge: true, applyDamage: true);
    }

    void FinishGimmickRoutine(bool resetGauge, bool applyDamage)
    {
        if (resetGauge)
            ResetElectricGauge();

        if (applyDamage)
            ApplyPlayerDamage();

        isCastingGimmick = false;
        gimmickArmed = false;
        gimmickRoutine = null;
        SetEnemyAiPaused(false);
        LogElectricGimmick("Electric gimmick ended.");
    }

    void CancelWaitingGimmick()
    {
        if (gimmickRoutine == null || isCastingGimmick)
            return;

        StopCoroutine(gimmickRoutine);
        gimmickRoutine = null;
        gimmickArmed = false;
        SetEnemyAiPaused(false);
        LogElectricGimmick("Electric gimmick canceled before cast.");
    }

    bool IsCastWindowOpen()
    {
        if (battleUiController != null && battleUiController.IsPlayerQteActive)
            return false;

        if (enemyController != null
            && (enemyController.CurrentAIState != EnemyAIState.Idle || enemyController.IsAttackTelegraphActive))
        {
            return false;
        }

        if (enemyActor != null && !enemyActor.CanAttemptDecision)
            return false;

        return true;
    }

    bool CanContinueGimmick()
    {
        return enableElectricGimmick
            && enemyActor != null
            && enemyActor.RoundCombatActive
            && enemyActor.Health != null
            && enemyActor.Health.IsAlive
            && playerActor != null
            && playerActor.Health != null
            && playerActor.Health.IsAlive;
    }

    void PlayCameraShake()
    {
        if (cameraShakeController == null)
            cameraShakeController = FindFirstObjectByType<CombatCameraShakeController>();

        if (cameraShakeController != null)
            cameraShakeController.PlayShake(shakeDuration, shakeStrength, shakeFrequency);
    }

    void SpawnWaveEffect()
    {
        if (waveEffectPrefab == null)
            return;

        Transform spawnPoint = waveEffectSpawnPoint != null
            ? waveEffectSpawnPoint
            : enemyActor != null ? enemyActor.transform : transform;

        Vector2 randomOffset = Random.insideUnitCircle * waveEffectRandomCircleRadius;
        Vector3 spawnPosition = spawnPoint.position + new Vector3(randomOffset.x, randomOffset.y, 0f);
        Quaternion spawnRotation = useWaveEffectSpawnPointRotation
            ? spawnPoint.rotation
            : waveEffectPrefab.transform.rotation;

        GameObject instance = Instantiate(
            waveEffectPrefab,
            spawnPosition,
            spawnRotation,
            waveEffectParentOverride);

        ElectricBossWaveEffectAnimator animator = instance.GetComponent<ElectricBossWaveEffectAnimator>();
        if (animator == null)
            animator = instance.AddComponent<ElectricBossWaveEffectAnimator>();

        animator.Play(
            waveEffectDuration,
            waveStartScaleMultiplier,
            waveEndScaleMultiplier,
            fadeOutWaveEffect,
            destroyWaveEffectWhenFinished);
    }

    void ApplyPlayerDamage()
    {
        if (damageToPlayer <= 0f)
            return;

        if (battleUiController != null)
        {
            battleUiController.ApplyPlayerDamage(damageToPlayer);
            return;
        }

        if (playerActor != null && playerActor.Health != null)
            playerActor.Health.ApplyDamage(damageToPlayer);
    }

    IEnumerator WaitForSecondsWhileActive(float duration)
    {
        float elapsed = 0f;
        duration = Mathf.Max(0f, duration);

        while (elapsed < duration && CanContinueGimmick())
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    void SetGaugeSlots(int value)
    {
        currentGaugeSlots = Mathf.Clamp(value, 0, GetFullGaugeSlotCount());
        ApplyGaugeView();
    }

    bool IsGaugeFull()
    {
        return currentGaugeSlots >= GetFullGaugeSlotCount();
    }

    int GetFullGaugeSlotCount()
    {
        int slotLimit = gaugeSlots != null && gaugeSlots.Length > 0
            ? gaugeSlots.Length
            : requiredGaugeSlots;

        return Mathf.Max(1, Mathf.Min(requiredGaugeSlots, slotLimit));
    }

    void ApplyGaugeView()
    {
        if (gaugeSlots == null)
            return;

        for (int i = 0; i < gaugeSlots.Length; i++)
        {
            if (gaugeSlots[i] != null)
                gaugeSlots[i].SetActive(i < currentGaugeSlots);
        }
    }

    void SetEnemyAiPaused(bool paused)
    {
        if (!pauseEnemyAiDuringGimmick || enemyController == null || enemyAiPausedByGimmick == paused)
            return;

        enemyAiPausedByGimmick = paused;
        enemyController.SetManualAiPaused(paused);

        if (paused && enemyActor != null)
            enemyActor.ClearQueuedDecision();
    }

    void SubscribeEvents()
    {
        if (playerActor != null)
        {
            playerActor.CombatEventRaised -= HandlePlayerCombatEvent;
            playerActor.CombatEventRaised += HandlePlayerCombatEvent;
        }
    }

    void UnsubscribeEvents()
    {
        if (playerActor != null)
            playerActor.CombatEventRaised -= HandlePlayerCombatEvent;
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

        if (playerActor == null && enemyController != null)
            playerActor = enemyController.TargetController;

        if (playerActor == null)
            playerActor = FindPlayerActor();

        if (battleUiController == null)
            battleUiController = FindFirstObjectByType<BattleUiController>();

        if (cameraShakeController == null)
            cameraShakeController = FindFirstObjectByType<CombatCameraShakeController>();
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

    void LogElectricGimmick(string message)
    {
        if (logElectricGimmick)
            Debug.Log($"[ElectricBoss] {message}", this);
    }
}
