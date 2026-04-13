using UnityEngine;

[DisallowMultipleComponent]
public class CombatHitFeedbackController : MonoBehaviour
{
    [Header("Combat Actors")]
    [SerializeField] private CombatActorController playerController;
    [SerializeField] private CombatActorController enemyController;

    [Header("Hit Spawn Points")]
    [SerializeField] private Transform playerHitSpawnPoint;
    [SerializeField] private Transform enemyHitSpawnPoint;

    [Header("Hit Prefab")]
    [SerializeField] private GameObject hitPrefab;
    [SerializeField] private Transform parentOverride;
    [SerializeField, Min(0f)] private float randomCircleRadius = 0.25f;
    [SerializeField] private bool useSpawnPointRotation = true;
    [SerializeField, Min(0f)] private float destroyAfterSeconds = 1.5f;

    [Header("Hit Effect Animation")]
    [SerializeField] private bool animateHitPrefabOnSpawn = true;
    [SerializeField, Min(0f)] private float effectStartScaleMultiplier = 1.35f;
    [SerializeField, Min(0f)] private float effectShrinkScaleMultiplier = 0.82f;
    [SerializeField, Min(0f)] private float effectFadeInDuration = 0.045f;
    [SerializeField, Min(0f)] private float effectSettleDuration = 0.12f;

    [Header("Hit React")]
    [SerializeField] private bool playHitReact = true;
    [SerializeField] private bool skipHitReactWhenHitWillStagger = true;

    void Reset()
    {
        TryAutoAssignActors();
    }

    void Awake()
    {
        TryAutoAssignActors();
    }

    void OnEnable()
    {
        TryAutoAssignActors();
        SubscribeCombatEvents();
    }

    void OnDisable()
    {
        UnsubscribeCombatEvents();
    }

    void HandleCombatEvent(CombatEventData eventData)
    {
        if (!TryGetHitTarget(eventData, out CombatActorController hitTarget, out bool hitWillStagger))
            return;

        SpawnHitPrefab(hitTarget);

        PlayHitReact(hitTarget, hitWillStagger);
    }

    bool TryGetHitTarget(
        CombatEventData eventData,
        out CombatActorController hitTarget,
        out bool hitWillStagger)
    {
        hitTarget = null;
        hitWillStagger = false;

        switch (eventData.Kind)
        {
            case CombatEventKind.AttackHit:
            case CombatEventKind.AttackMeetingWin:
                hitTarget = eventData.Opponent;
                break;

            case CombatEventKind.FeintPunished:
                hitTarget = eventData.Opponent;
                hitWillStagger = true;
                break;
        }

        return hitTarget != null;
    }

    void SpawnHitPrefab(CombatActorController hitTarget)
    {
        if (hitPrefab == null)
            return;

        Transform spawnPoint = GetSpawnPoint(hitTarget);
        if (spawnPoint == null)
            return;

        Vector2 randomOffset = Random.insideUnitCircle * randomCircleRadius;
        Vector3 spawnPosition = spawnPoint.position + new Vector3(randomOffset.x, randomOffset.y, 0f);
        Quaternion spawnRotation = useSpawnPointRotation
            ? spawnPoint.rotation
            : hitPrefab.transform.rotation;

        GameObject instance = Instantiate(hitPrefab, spawnPosition, spawnRotation, parentOverride);
        PlayHitEffectSpawnAnimation(instance);

        if (destroyAfterSeconds > 0f)
            Destroy(instance, destroyAfterSeconds);
    }

    void PlayHitReact(CombatActorController hitTarget, bool hitWillStagger)
    {
        if (!playHitReact || hitTarget == null || hitTarget.IsStaggered)
            return;

        if (hitWillStagger && skipHitReactWhenHitWillStagger)
            return;

        hitTarget.TryPlayHitReact();
    }

    void PlayHitEffectSpawnAnimation(GameObject instance)
    {
        if (!animateHitPrefabOnSpawn || instance == null)
            return;

        HitEffectSpawnAnimator animator = instance.GetComponent<HitEffectSpawnAnimator>();
        if (animator == null)
            animator = instance.AddComponent<HitEffectSpawnAnimator>();

        animator.Play(
            effectStartScaleMultiplier,
            effectShrinkScaleMultiplier,
            effectFadeInDuration,
            effectSettleDuration);
    }

    Transform GetSpawnPoint(CombatActorController hitTarget)
    {
        if (hitTarget == playerController)
            return playerHitSpawnPoint != null ? playerHitSpawnPoint : hitTarget.transform;

        if (hitTarget == enemyController)
            return enemyHitSpawnPoint != null ? enemyHitSpawnPoint : hitTarget.transform;

        return hitTarget != null ? hitTarget.transform : null;
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
