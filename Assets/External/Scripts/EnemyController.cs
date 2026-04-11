using UnityEngine;

public enum EnemyActionChoice
{
    Idle,
    Attack,
    Dodge,
}

[RequireComponent(typeof(PlayerController))]
public class EnemyController : MonoBehaviour
{
    [Header("Enemy Setup")]
    [SerializeField] private PlayerController targetController;
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

    private PlayerController motionController;
    private float nextDecisionDelay;

    void Reset()
    {
        ApplySetup();
    }

    void Awake()
    {
        ApplySetup();
    }

    void OnEnable()
    {
        ResetDecisionDelay();
    }

    void OnValidate()
    {
        ApplySetup();
        decisionIntervalMin = Mathf.Max(0.1f, decisionIntervalMin);
        decisionIntervalMax = Mathf.Max(decisionIntervalMin, decisionIntervalMax);
        attackWeight = Mathf.Max(0f, attackWeight);
        idleWeight = Mathf.Max(0f, idleWeight);
        dodgeWeight = Mathf.Max(0f, dodgeWeight);
    }

    void Update()
    {
        if (!enableAi || motionController == null || !motionController.RoundCombatActive)
            return;

        if (!motionController.CanAttemptDecision)
            return;

        nextDecisionDelay -= Time.deltaTime;
        if (nextDecisionDelay > 0f)
            return;

        ExecuteAiDecision();
        ResetDecisionDelay();
    }

    public bool TryPush()
    {
        return motionController != null && motionController.TryPush();
    }

    public bool TryDodge()
    {
        return motionController != null && motionController.TryDodge();
    }

    public bool TryBalanceDebug()
    {
        return motionController != null && motionController.TryBalanceDebug();
    }

    void ApplySetup()
    {
        if (!TryGetComponent(out motionController))
            return;

        motionController.SetPushDirection(enemyPushDirection);
        motionController.SetDebugInputEnabled(enableDebugInput);
        motionController.SetDebugKeys(pushKey, dodgeKey, balanceKey);
        motionController.SetCombatPresentation(CombatActorSide.Enemy, "\uC801", "#FF5C5C");
        motionController.SetOpponent(targetController);

        if (targetController != null)
        {
            targetController.SetCombatPresentation(CombatActorSide.Player, "\uD50C\uB808\uC774\uC5B4", "#4AA3FF");

            if (targetController.OpponentController == null)
                targetController.SetOpponent(motionController);
        }
    }

    void ExecuteAiDecision()
    {
        EnemyActionChoice action = PickNextAction();

        if (enableAiDebugLogs)
        {
            string lane = motionController.BuildCombatLane(
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
                motionController.TryPush();
                break;

            case EnemyActionChoice.Dodge:
                motionController.TryDodge();
                break;

            default:
                motionController.TryStayNeutral();
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
