using UnityEngine;

[DisallowMultipleComponent]
public class CombatHandContactFeedbackController : MonoBehaviour
{

    [Header("Combat Actors")]
    [SerializeField] private CombatActorController playerController;
    [SerializeField] private CombatActorController enemyController;

    [Header("Palm Contact Points")]
    [SerializeField] private Transform playerPalmContactPoint;
    [SerializeField] private Transform enemyPalmContactPoint;

    [Header("Contact Detection")]
    [SerializeField, Min(0f)] private float contactRadius = 0.2f;
    [SerializeField, Min(1f)] private float resetDistanceMultiplier = 1.4f;
    [SerializeField] private bool requireAtLeastOneHandExtending = true;
    [Tooltip("true이면 양쪽 모두 손을 뻗고 있을 때(팜 클래시)만 이펙트 발동. " +
             "false이면 한쪽만 뻗어도 발동(단방향 접촉 허용).")]
    [SerializeField] private bool requirePalmClash = true;
    [SerializeField, Min(0f)] private float contactCooldown = 0.08f;

    [Header("Debug")]
    [SerializeField] private bool logContactState;

    private bool contactActive;
    private float nextContactTime;

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

    void Update()
    {
        TryAutoAssignActors();

        Transform playerPalm = GetPalmContactPoint(playerController, playerPalmContactPoint);
        Transform enemyPalm = GetPalmContactPoint(enemyController, enemyPalmContactPoint);
        if (playerPalm == null || enemyPalm == null)
            return;

        bool playerHandActive = IsHandExtending(playerController);
        bool enemyHandActive = IsHandExtending(enemyController);
        bool anyHandActive = playerHandActive || enemyHandActive;

        if (requireAtLeastOneHandExtending && !anyHandActive)
        {
            ResetContact();
            return;
        }

        float distance = Vector3.Distance(playerPalm.position, enemyPalm.position);
        float resetDistance = contactRadius * resetDistanceMultiplier;

        // 리셋 조건이 충족되면 즉시 return — 같은 프레임에 재발화 방지
        if (!anyHandActive || distance > resetDistance)
        {
            ResetContact();
            return;
        }

        if (contactActive || Time.time < nextContactTime || distance > contactRadius)
            return;

        bool isPalmClash = playerHandActive && enemyHandActive;

        // requirePalmClash가 켜져 있으면 양쪽 모두 뻗고 있을 때만 발동
        if (requirePalmClash && !isPalmClash)
            return;

        PlayContact(isPalmClash, playerPalm, enemyPalm);
    }

    void PlayContact(bool isPalmClash, Transform playerPalmContact, Transform enemyPalmContact)
    {
        contactActive = true;
        nextContactTime = Time.time + contactCooldown;

        if (logContactState)
        {
            Debug.Log(
                $"[HandContact] {(isPalmClash ? "Palm Clash" : "One-sided Contact")} " +
                $"Player:{GetHandPhase(playerController)} Enemy:{GetHandPhase(enemyController)}",
                this);
        }
    }

    void ResetContact()
    {
        contactActive = false;
    }

    bool IsHandExtending(CombatActorController actor)
    {
        return actor != null && actor.IsHandContactActive;
    }

    CombatHandExtensionPhase GetHandPhase(CombatActorController actor)
    {
        return actor != null ? actor.HandExtensionPhase : CombatHandExtensionPhase.None;
    }

    Transform GetPalmContactPoint(CombatActorController actor, Transform overridePoint)
    {
        if (overridePoint != null)
            return overridePoint;

        return actor != null ? actor.PalmContactPoint : null;
    }

    /// <summary>
    /// AttackClashed / AttackMeetingWin / AttackMeetingLoss 이벤트 수신 시
    /// 거리 무관하게 contact effect를 강제 발동한다.
    /// handMeetingRadius(게임 로직)가 contactRadius(비주얼)보다 커서
    /// 히트스탑으로 손이 얼어버리는 경우에도 이펙트가 빠지지 않도록 보장.
    /// </summary>
    void HandleCombatContactEvent(CombatEventData eventData)
    {
        if (eventData.Kind != CombatEventKind.AttackClashed
            && eventData.Kind != CombatEventKind.AttackMeetingWin
            && eventData.Kind != CombatEventKind.AttackMeetingLoss)
            return;

        // 같은 공격 사이클에서 이미 발동됐으면 무시 (Win·Loss 이중 발화 방지)
        if (contactActive || Time.time < nextContactTime)
            return;

        Transform playerPalm = GetPalmContactPoint(playerController, playerPalmContactPoint);
        Transform enemyPalm  = GetPalmContactPoint(enemyController, enemyPalmContactPoint);
        if (playerPalm == null || enemyPalm == null)
            return;

        bool playerHandActive = IsHandExtending(playerController);
        bool enemyHandActive  = IsHandExtending(enemyController);
        bool isPalmClash      = playerHandActive && enemyHandActive;

        PlayContact(isPalmClash, playerPalm, enemyPalm);
    }

    void SubscribeCombatEvents()
    {
        if (playerController != null)
        {
            playerController.CombatEventRaised -= HandleCombatContactEvent;
            playerController.CombatEventRaised += HandleCombatContactEvent;
        }

        if (enemyController != null && enemyController != playerController)
        {
            enemyController.CombatEventRaised -= HandleCombatContactEvent;
            enemyController.CombatEventRaised += HandleCombatContactEvent;
        }
    }

    void UnsubscribeCombatEvents()
    {
        if (playerController != null)
            playerController.CombatEventRaised -= HandleCombatContactEvent;

        if (enemyController != null)
            enemyController.CombatEventRaised -= HandleCombatContactEvent;
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

        // 컨트롤러가 이번에 새로 할당됐다면 이벤트 구독도 갱신
        if (playerController != null || enemyController != null)
            SubscribeCombatEvents();
    }
}
