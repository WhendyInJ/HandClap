public enum CombatEventKind
{
    AttackHit,
    AttackClashed,
    AttackDodged,
    DodgeSucceeded,
    DodgeFailed,
}

public readonly struct CombatEventData
{
    public CombatEventData(
        CombatEventKind kind,
        CombatActorController actor,
        CombatActorController opponent,
        CombatState actorState,
        CombatState opponentState,
        string summary)
    {
        Kind = kind;
        Actor = actor;
        Opponent = opponent;
        ActorState = actorState;
        OpponentState = opponentState;
        Summary = summary;
    }

    public CombatEventKind Kind { get; }
    public CombatActorController Actor { get; }
    public CombatActorController Opponent { get; }
    public CombatState ActorState { get; }
    public CombatState OpponentState { get; }
    public string Summary { get; }
}
