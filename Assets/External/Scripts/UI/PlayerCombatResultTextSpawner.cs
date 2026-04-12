using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerCombatResultTextSpawner : MonoBehaviour
{
    [Header("Combat Actors")]
    [SerializeField] private CombatActorController playerController;
    [SerializeField] private CombatActorController enemyController;

    [Header("Spawn")]
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private GameObject textPrefab;
    [SerializeField] private Transform parentOverride;
    [SerializeField] private bool parentToSpawnPoint;
    [SerializeField] private bool useSpawnPointRotation = true;
    [SerializeField, Min(0f)] private float destroyAfterSeconds = 1.5f;

    [Header("Messages")]
    [SerializeField] private string feintFailedMessage = "헛짓함!";
    [SerializeField] private string feintPunishedMessage = "응징당함!";
    [SerializeField] private string dodgeFailedMessage = "헛짓함!";
    [SerializeField] private string attackDodgedMessage = "빗나감!";

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
        if (!TryGetPlayerResultMessage(eventData, out string message))
            return;

        SpawnMessage(message);
    }

    bool TryGetPlayerResultMessage(CombatEventData eventData, out string message)
    {
        message = null;

        switch (eventData.Kind)
        {
            case CombatEventKind.FeintFailed:
                if (eventData.Actor == playerController)
                    message = feintFailedMessage;
                break;

            case CombatEventKind.FeintPunished:
                if (eventData.Opponent == playerController)
                    message = feintPunishedMessage;
                break;

            case CombatEventKind.DodgeFailed:
                if (eventData.Actor == playerController)
                    message = dodgeFailedMessage;
                break;

            case CombatEventKind.AttackDodged:
                if (eventData.Actor == playerController)
                    message = attackDodgedMessage;
                break;
        }

        return !string.IsNullOrEmpty(message);
    }

    void SpawnMessage(string message)
    {
        if (textPrefab == null || spawnPoint == null)
            return;

        Transform parent = parentOverride != null
            ? parentOverride
            : parentToSpawnPoint ? spawnPoint : null;

        Quaternion rotation = useSpawnPointRotation
            ? spawnPoint.rotation
            : textPrefab.transform.rotation;

        GameObject instance = Instantiate(textPrefab, spawnPoint.position, rotation, parent);

        if (parent == spawnPoint)
            instance.transform.localPosition = Vector3.zero;

        TMP_Text text = instance.GetComponent<TMP_Text>();
        if (text == null)
            text = instance.GetComponentInChildren<TMP_Text>();

        if (text != null)
            text.text = message;

        if (destroyAfterSeconds > 0f)
            Destroy(instance, destroyAfterSeconds);
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
