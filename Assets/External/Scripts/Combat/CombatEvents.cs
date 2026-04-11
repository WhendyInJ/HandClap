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
        PlayerController actor,
        PlayerController opponent,
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
    public PlayerController Actor { get; }
    public PlayerController Opponent { get; }
    public CombatState ActorState { get; }
    public CombatState OpponentState { get; }
    public string Summary { get; }
}
