using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class StoneBossGimmickController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private EnemyController enemyController;
    [SerializeField] private CombatActorController enemyActor;
    [SerializeField] private CombatActorController playerActor;
    [SerializeField] private BattleUiController battleUiController;
    [SerializeField] private CombatCameraShakeController cameraShakeController;
    [SerializeField] private Transform jumpTarget;
    [SerializeField] private Transform playerHeadTarget;

    [Header("Stone Gimmick")]
    [SerializeField] private bool enableStoneGimmick = true;
    [SerializeField] private bool autoStartOnCooldown = true;
    [SerializeField, Min(0f)] private float autoStartCooldownMin = 5f;
    [SerializeField, Min(0f)] private float autoStartCooldownMax = 8f;
    [SerializeField] private bool waitUntilEnemyReady = true;
    [SerializeField] private bool startOnlyWhenEnemyIdle = true;
    [SerializeField] private bool reserveEnemyAiWhenCooldownReady = true;
    [SerializeField] private bool preemptEnemyActionWhenReady = true;
    [SerializeField] private bool ignoreEnemyActionCooldown = true;

    [Header("Jump")]
    [SerializeField, Min(0f)] private float jumpHeight = 1.5f;
    [SerializeField, Min(0f)] private float jumpUpDuration = 0.25f;
    [SerializeField, Min(0f)] private float airHangDuration = 0.1f;
    [SerializeField, Min(0f)] private float landingDuration = 0.18f;

    [Header("Landing Shake")]
    [SerializeField, Min(0f)] private float landingShakeDuration = 0.3f;
    [SerializeField, Min(0f)] private float landingShakeStrength = 0.35f;
    [SerializeField, Min(1f)] private float landingShakeFrequency = 55f;

    [Header("Falling Stone")]
    [SerializeField] private GameObject stonePrefab;
    [SerializeField] private Transform stoneParentOverride;
    [SerializeField, Min(0f)] private float stoneSpawnAfterLandingDelay = 0.05f;
    [SerializeField, Min(0f)] private float stoneSpawnHeight = 4f;
    [SerializeField, Min(0f)] private float stoneFallDuration = 0.65f;
    [SerializeField, Min(0f)] private float stoneImpactRadius = 0.75f;
    [SerializeField, Min(0f)] private float stoneDamage = 1f;
    [SerializeField, Min(0f)] private float stoneDestroyDelay = 0.4f;
    [SerializeField] private Vector2 stoneRandomOffsetRange = new(0.2f, 0f);
    [SerializeField] private bool dodgeDuringFallAvoidsStone = true;
    [SerializeField] private bool leavingImpactRadiusAvoidsStone = true;

    [Header("Debug")]
    [SerializeField] private bool logStoneGimmick = true;

    private Coroutine gimmickRoutine;
    private float cooldownTimer;
    private bool enemyAiPausedByGimmick;
    private bool hasJumpBasePosition;
    private Vector3 jumpBasePosition;
    private bool playerDodgedDuringStoneFall;

    void Reset()
    {
        TryAutoAssignReferences();
        jumpTarget = transform;
    }

    void Awake()
    {
        TryAutoAssignReferences();
    }

    void OnEnable()
    {
        TryAutoAssignReferences();
        ResetCooldownTimer();
    }

    void OnDisable()
    {
        if (gimmickRoutine != null)
        {
            StopCoroutine(gimmickRoutine);
            gimmickRoutine = null;
        }

        RestoreJumpTargetPosition();
        SetEnemyAiPaused(false);
    }

    void OnValidate()
    {
        autoStartCooldownMin = Mathf.Max(0f, autoStartCooldownMin);
        autoStartCooldownMax = Mathf.Max(autoStartCooldownMin, autoStartCooldownMax);
        jumpHeight = Mathf.Max(0f, jumpHeight);
        jumpUpDuration = Mathf.Max(0f, jumpUpDuration);
        airHangDuration = Mathf.Max(0f, airHangDuration);
        landingDuration = Mathf.Max(0f, landingDuration);
        landingShakeDuration = Mathf.Max(0f, landingShakeDuration);
        landingShakeStrength = Mathf.Max(0f, landingShakeStrength);
        landingShakeFrequency = Mathf.Max(1f, landingShakeFrequency);
        stoneSpawnAfterLandingDelay = Mathf.Max(0f, stoneSpawnAfterLandingDelay);
        stoneSpawnHeight = Mathf.Max(0f, stoneSpawnHeight);
        stoneFallDuration = Mathf.Max(0f, stoneFallDuration);
        stoneImpactRadius = Mathf.Max(0f, stoneImpactRadius);
        stoneDamage = Mathf.Max(0f, stoneDamage);
        stoneDestroyDelay = Mathf.Max(0f, stoneDestroyDelay);
        stoneRandomOffsetRange.x = Mathf.Max(0f, stoneRandomOffsetRange.x);
        stoneRandomOffsetRange.y = Mathf.Max(0f, stoneRandomOffsetRange.y);
    }

    void Update()
    {
        if (!autoStartOnCooldown || gimmickRoutine != null || !CanContinueGimmick())
            return;

        if (battleUiController != null && battleUiController.IsPlayerQteActive)
            return;

        cooldownTimer -= Time.deltaTime;
        if (cooldownTimer > 0f)
            return;

        if (reserveEnemyAiWhenCooldownReady && ShouldReserveEnemyAiForGimmick())
            SetEnemyAiPaused(true);

        TryStartGimmick();
    }

    public bool TryStartGimmick()
    {
        if (gimmickRoutine != null || !CanStartGimmick())
            return false;

        gimmickRoutine = StartCoroutine(RunStoneGimmick());
        return true;
    }

    [ContextMenu("Start Stone Gimmick")]
    void StartStoneGimmickContext()
    {
        TryStartGimmick();
    }

    IEnumerator RunStoneGimmick()
    {
        LogStoneGimmick("Stone gimmick started.");
        SetEnemyAiPaused(true);

        while (CanContinueGimmick()
            && waitUntilEnemyReady
            && !IsEnemyFreeForGimmick())
        {
            yield return null;
        }

        if (CanContinueGimmick())
            yield return RunJumpAndLanding();

        if (CanContinueGimmick())
        {
            PlayLandingShake();

            if (stoneSpawnAfterLandingDelay > 0f)
                yield return WaitForSecondsWhileActive(stoneSpawnAfterLandingDelay);
        }

        if (CanContinueGimmick())
            yield return DropStoneOnPlayer();

        RestoreJumpTargetPosition();
        SetEnemyAiPaused(false);
        ResetCooldownTimer();
        gimmickRoutine = null;
        LogStoneGimmick("Stone gimmick ended.");
    }

    IEnumerator RunJumpAndLanding()
    {
        Transform target = GetJumpTarget();
        if (target == null)
            yield break;

        jumpBasePosition = target.position;
        hasJumpBasePosition = true;

        Vector3 topPosition = jumpBasePosition + Vector3.up * jumpHeight;
        yield return MoveJumpTarget(target, jumpBasePosition, topPosition, jumpUpDuration, easeIn: false);
        yield return WaitForSecondsWhileActive(airHangDuration);
        yield return MoveJumpTarget(target, topPosition, jumpBasePosition, landingDuration, easeIn: true);

        target.position = jumpBasePosition;
    }

    IEnumerator MoveJumpTarget(Transform target, Vector3 from, Vector3 to, float duration, bool easeIn)
    {
        if (target == null)
            yield break;

        if (duration <= 0f)
        {
            target.position = to;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration && CanContinueGimmick())
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float easedT = easeIn ? t * t : 1f - (1f - t) * (1f - t);
            target.position = Vector3.LerpUnclamped(from, to, easedT);
            yield return null;
        }

        if (target != null)
            target.position = to;
    }

    IEnumerator DropStoneOnPlayer()
    {
        if (stonePrefab == null || playerActor == null)
            yield break;

        Vector3 impactPoint = GetStoneImpactPoint();
        Vector3 spawnPoint = impactPoint + Vector3.up * stoneSpawnHeight;
        Quaternion spawnRotation = stonePrefab.transform.rotation;

        GameObject stoneInstance = Instantiate(stonePrefab, spawnPoint, spawnRotation, stoneParentOverride);
        playerDodgedDuringStoneFall = false;

        yield return MoveStoneToImpact(stoneInstance, spawnPoint, impactPoint);
        ResolveStoneImpact(impactPoint);

        if (stoneInstance != null)
        {
            if (stoneDestroyDelay > 0f)
                Destroy(stoneInstance, stoneDestroyDelay);
            else
                Destroy(stoneInstance);
        }
    }

    IEnumerator MoveStoneToImpact(GameObject stoneInstance, Vector3 from, Vector3 to)
    {
        if (stoneInstance == null)
            yield break;

        if (stoneFallDuration <= 0f)
        {
            stoneInstance.transform.position = to;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < stoneFallDuration && CanContinueGimmick() && stoneInstance != null)
        {
            elapsed += Time.deltaTime;
            if (TryMarkPlayerDodgingStone())
                playerDodgedDuringStoneFall = true;

            float t = Mathf.Clamp01(elapsed / stoneFallDuration);
            float easedT = t * t;
            stoneInstance.transform.position = Vector3.LerpUnclamped(from, to, easedT);
            yield return null;
        }

        if (stoneInstance != null)
            stoneInstance.transform.position = to;
    }

    void ResolveStoneImpact(Vector3 impactPoint)
    {
        if (playerActor == null)
            return;

        bool dodged = IsPlayerDodgingStone();
        bool outsideImpactRadius = leavingImpactRadiusAvoidsStone
            && Vector2.Distance(GetCurrentPlayerTargetPosition(), impactPoint) > stoneImpactRadius;

        if (dodged || outsideImpactRadius)
        {
            if (dodged)
                playerActor.SuppressCurrentDodgeFailure();

            LogStoneGimmick("Stone impact avoided.");
            return;
        }

        if (battleUiController != null)
        {
            battleUiController.ApplyPlayerDamage(stoneDamage);
        }
        else if (playerActor.Health != null)
        {
            playerActor.Health.ApplyDamage(stoneDamage);
        }

        LogStoneGimmick($"Stone impact hit player. Damage: {stoneDamage:F2}");
    }

    Vector3 GetStoneImpactPoint()
    {
        Vector3 targetPosition = GetCurrentPlayerTargetPosition();
        Vector2 randomOffset = new(
            Random.Range(-stoneRandomOffsetRange.x, stoneRandomOffsetRange.x),
            Random.Range(-stoneRandomOffsetRange.y, stoneRandomOffsetRange.y));

        return targetPosition + new Vector3(randomOffset.x, randomOffset.y, 0f);
    }

    bool IsPlayerDodgingStone()
    {
        return playerDodgedDuringStoneFall || TryMarkPlayerDodgingStone();
    }

    bool TryMarkPlayerDodgingStone()
    {
        if (!dodgeDuringFallAvoidsStone || playerActor == null)
            return false;

        bool isDodgingStone = playerActor.IsDodgeWindowActive
            || playerActor.ResolutionCombatState == CombatState.Dodge;

        if (!isDodgingStone)
            return false;

        playerActor.SuppressCurrentDodgeFailure();
        return true;
    }

    Vector3 GetCurrentPlayerTargetPosition()
    {
        if (playerHeadTarget != null)
            return playerHeadTarget.position;

        return playerActor != null ? playerActor.transform.position : transform.position;
    }

    void PlayLandingShake()
    {
        if (cameraShakeController == null)
            cameraShakeController = FindFirstObjectByType<CombatCameraShakeController>();

        if (cameraShakeController != null)
            cameraShakeController.PlayShake(
                landingShakeDuration,
                landingShakeStrength,
                landingShakeFrequency);
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

    bool CanStartGimmick()
    {
        if (!CanContinueGimmick())
            return false;

        if (!IsEnemyStartWindowOpen())
            return false;

        if (battleUiController != null && battleUiController.IsPlayerQteActive)
            return false;

        return true;
    }

    bool IsEnemyStartWindowOpen()
    {
        if (startOnlyWhenEnemyIdle
            && enemyController != null
            && (enemyController.CurrentAIState != EnemyAIState.Idle || enemyController.IsAttackTelegraphActive))
        {
            if (!CanPreemptEnemyActionForGimmick())
                return false;
        }

        if (waitUntilEnemyReady && !IsEnemyFreeForGimmick())
            return false;

        return true;
    }

    bool ShouldReserveEnemyAiForGimmick()
    {
        if (enemyController == null)
            return false;

        if (!startOnlyWhenEnemyIdle)
            return true;

        if (enemyController.CurrentAIState == EnemyAIState.Idle && !enemyController.IsAttackTelegraphActive)
            return true;

        return CanPreemptEnemyActionForGimmick();
    }

    bool CanPreemptEnemyActionForGimmick()
    {
        return preemptEnemyActionWhenReady
            && enemyController != null
            && IsEnemyFreeForGimmick();
    }

    bool IsEnemyFreeForGimmick()
    {
        if (IsEnemyStaggered())
            return false;

        if (!waitUntilEnemyReady)
            return true;

        if (enemyActor == null || !enemyActor.RoundCombatActive)
            return false;

        if (!ignoreEnemyActionCooldown)
            return enemyActor.CanAttemptDecision;

        CombatMotionController motion = enemyActor.Motion;
        return motion != null
            && !motion.IsPushing
            && !motion.IsDodging
            && !motion.IsBalancing
            && !motion.IsStaggered;
    }

    bool IsEnemyStaggered()
    {
        return enemyActor != null && enemyActor.IsStaggered;
    }

    bool CanContinueGimmick()
    {
        return enableStoneGimmick
            && enemyActor != null
            && enemyActor.RoundCombatActive
            && enemyActor.Health != null
            && enemyActor.Health.IsAlive
            && !enemyActor.IsStaggered;
    }

    Transform GetJumpTarget()
    {
        if (jumpTarget != null)
            return jumpTarget;

        if (enemyActor != null)
            return enemyActor.transform;

        return transform;
    }

    void RestoreJumpTargetPosition()
    {
        if (!hasJumpBasePosition)
            return;

        Transform target = GetJumpTarget();
        if (target != null)
            target.position = jumpBasePosition;

        hasJumpBasePosition = false;
    }

    void SetEnemyAiPaused(bool paused)
    {
        if (enemyController == null || enemyAiPausedByGimmick == paused)
            return;

        enemyAiPausedByGimmick = paused;
        enemyController.SetManualAiPaused(paused);

        if (paused && enemyActor != null)
            enemyActor.ClearQueuedDecision();
    }

    void ResetCooldownTimer()
    {
        cooldownTimer = Random.Range(autoStartCooldownMin, autoStartCooldownMax);
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

        if (jumpTarget == null)
            jumpTarget = enemyActor != null ? enemyActor.transform : transform;
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

    void LogStoneGimmick(string message)
    {
        if (logStoneGimmick)
            Debug.Log($"[StoneBoss] {message}", this);
    }
}
