public enum CombatEventKind
{
    AttackHit,
    AttackClashed,
    AttackDodged,
    AttackFeinted,
    DodgeSucceeded,
    DodgeFailed,

    /// <summary>
    /// 상대가 페인트(페이크/페인트 모션) 중일 때 공격이 적중.
    /// 페인트 동작을 읽고 반격에 성공한 경우에 발생한다.
    /// </summary>
    FeintPunished,

    /// <summary>
    /// Feint ended without the opponent dodging or punishing it.
    /// The actor who used the feint should stagger.
    /// </summary>
    FeintFailed,
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
