using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(CombatActorController))]
public class PlayerInputController : MonoBehaviour
{
    [SerializeField] private CombatActorController actorController;
    [SerializeField] private bool inputEnabled = true;
    [SerializeField] private KeyCode pushKey = KeyCode.Space;
    [SerializeField] private KeyCode dodgeKey = KeyCode.X;
    [SerializeField] private KeyCode balanceDebugKey = KeyCode.C;

    void Reset()
    {
        CacheController();
    }

    void Awake()
    {
        CacheController();
    }

    void Update()
    {
        if (!inputEnabled || actorController == null)
            return;

        if (Input.GetKeyDown(pushKey))
            actorController.TryPush();

        if (Input.GetKeyDown(dodgeKey))
            actorController.TryDodge();

        if (Input.GetKeyDown(balanceDebugKey))
            actorController.TryBalanceDebug();
    }

    public void SetInputEnabled(bool enabled)
    {
        inputEnabled = enabled;
    }

    public void SetKeys(KeyCode newPushKey, KeyCode newDodgeKey, KeyCode newBalanceKey)
    {
        pushKey = newPushKey;
        dodgeKey = newDodgeKey;
        balanceDebugKey = newBalanceKey;
    }

    void CacheController()
    {
        if (actorController == null)
            TryGetComponent(out actorController);
    }
}
