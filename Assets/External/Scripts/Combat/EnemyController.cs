using UnityEngine;

public enum EnemyActionChoice
{
    Idle,
    Attack,
    Dodge,
}

public class EnemyController : MonoBehaviour
{
    [Header("Enemy Setup")]
    [SerializeField] private CombatActorController targetController;
    [SerializeField] private Vector2 enemyPushDirection = Vector2.left;
    [SerializeField] private bool enableDebugInput;
    [SerializeField] private KeyCode pushKey = KeyCode.Keypad1;
    [SerializeField] private KeyCode dodgeKey = KeyCode.Keypad2;
    [SerializeField] private KeyCode balanceKey = KeyCode.Keypad3;

    [Header("Simple AI")]
    [SerializeField] private bool enableAi = true;
    [SerializeField] private float decisionIntervalMin = 1.2f;
    [SerializeField] private float decisionIntervalMax = 2f;
    [SerializeField] private float attackWeight = 0.4f;
    [SerializeField] private float idleWeight = 0.3f;
    [SerializeField] private float dodgeWeight = 0.3f;
    [SerializeField] private bool enableAiDebugLogs = true;

    private CombatActorController actorController;
    private float nextDecisionDelay;

    public CombatActorController ActorController
    {
        get
        {
            EnsureActorController(true);
            return actorController;
        }
    }

    public CombatActorController TargetController => targetController;

    void Reset()
    {
        EnsureActorController(true);
        ApplySetup();
    }

    void Awake()
    {
        EnsureActorController(true);
        ApplySetup();
    }

    void OnEnable()
    {
        ResetDecisionDelay();
    }

    void OnValidate()
    {
        EnsureActorController(false);
        ApplySetup();
        decisionIntervalMin = Mathf.Max(0.1f, decisionIntervalMin);
        decisionIntervalMax = Mathf.Max(decisionIntervalMin, decisionIntervalMax);
        attackWeight = Mathf.Max(0f, attackWeight);
        idleWeight = Mathf.Max(0f, idleWeight);
        dodgeWeight = Mathf.Max(0f, dodgeWeight);
    }

    void Update()
    {
        UpdateDebugInput();

        if (!enableAi || actorController == null || !actorController.CanAttemptDecision)
            return;

        nextDecisionDelay -= Time.deltaTime;
        if (nextDecisionDelay > 0f)
            return;

        ExecuteAiDecision();
        ResetDecisionDelay();
    }

    public bool TryPush()
    {
        return actorController != null && actorController.TryPush();
    }

    public bool TryDodge()
    {
        return actorController != null && actorController.TryDodge();
    }

    public bool TryBalanceDebug()
    {
        return actorController != null && actorController.TryBalanceDebug();
    }

    void ApplySetup()
    {
        EnsureActorController(false);

        if (actorController == null)
            return;

        if (targetController == null)
            targetController = FindPlayerTarget();

        actorController.SetPushDirection(enemyPushDirection);
        actorController.SetCombatPresentation(CombatActorSide.Enemy, "\uC801", "#FF5C5C");
        actorController.SetOpponent(targetController);

        if (targetController != null)
        {
            targetController.SetCombatPresentation(CombatActorSide.Player, "\uD50C\uB808\uC774\uC5B4", "#4AA3FF");

            if (targetController.OpponentController == null)
                targetController.SetOpponent(actorController);
        }
    }

    void EnsureActorController(bool addIfMissing)
    {
        if (actorController != null || TryGetComponent(out actorController) || !addIfMissing)
            return;

        actorController = gameObject.AddComponent<CombatActorController>();
    }

    CombatActorController FindPlayerTarget()
    {
        CombatActorController[] actors = FindObjectsByType<CombatActorController>(FindObjectsSortMode.None);
        for (int i = 0; i < actors.Length; i++)
        {
            CombatActorController actor = actors[i];
            if (actor != null
                && actor != actorController
                && actor.ActorSide == CombatActorSide.Player
                && !actor.TryGetComponent<EnemyController>(out _))
                return actor;
        }

        return null;
    }

    void UpdateDebugInput()
    {
        if (!enableDebugInput || actorController == null)
            return;

        if (Input.GetKeyDown(pushKey))
            actorController.TryPush();

        if (Input.GetKeyDown(dodgeKey))
            actorController.TryDodge();

        if (Input.GetKeyDown(balanceKey))
            actorController.TryBalanceDebug();
    }

    void ExecuteAiDecision()
    {
        EnemyActionChoice action = PickNextAction();

        if (enableAiDebugLogs)
        {
            string lane = actorController.BuildCombatLane(
                targetController,
                ToCombatState(action),
                targetController != null ? targetController.ResolutionCombatState : CombatState.Neutral);

            Debug.Log(
                $"[AI] {lane}  \uC120\uD0DD:{ToDisplayName(action)}",
                this);
        }

        switch (action)
        {
            case EnemyActionChoice.Attack:
                actorController.TryPush();
                break;

            case EnemyActionChoice.Dodge:
                actorController.TryDodge();
                break;

            default:
                actorController.TryStayNeutral();
                break;
        }
    }

    EnemyActionChoice PickNextAction()
    {
        float totalWeight = attackWeight + idleWeight + dodgeWeight;
        if (totalWeight <= 0f)
            return EnemyActionChoice.Idle;

        float roll = Random.value * totalWeight;

        if (roll < attackWeight)
            return EnemyActionChoice.Attack;

        roll -= attackWeight;
        if (roll < idleWeight)
            return EnemyActionChoice.Idle;

        return EnemyActionChoice.Dodge;
    }

    void ResetDecisionDelay()
    {
        nextDecisionDelay = Random.Range(decisionIntervalMin, decisionIntervalMax);
    }

    static string ToDisplayName(EnemyActionChoice action)
    {
        return action switch
        {
            EnemyActionChoice.Attack => "\uACF5\uACA9",
            EnemyActionChoice.Dodge => "\uD68C\uD53C",
            _ => "\uAC00\uB9CC\uD788",
        };
    }

    static CombatState ToCombatState(EnemyActionChoice action)
    {
        return action switch
        {
            EnemyActionChoice.Attack => CombatState.Attack,
            EnemyActionChoice.Dodge => CombatState.Dodge,
            _ => CombatState.Neutral,
        };
    }
}
