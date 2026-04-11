public enum CombatState
{
    Neutral,
    Attack,
    Dodge,
}

public enum CombatActorSide
{
    Player,
    Enemy,
}

public enum CombatOutcome
{
    None,
    AttackClash,
    AttackHitsNeutral,
    AttackDodged,
    NeutralHitByAttack,
    DodgeSuccess,
    DodgeFail,
}

public readonly struct CombatResolution
{
    public CombatResolution(
        CombatState actorState,
        CombatState opponentState,
        CombatOutcome outcome,
        string summary)
    {
        ActorState = actorState;
        OpponentState = opponentState;
        Outcome = outcome;
        Summary = summary;
    }

    public CombatState ActorState { get; }
    public CombatState OpponentState { get; }
    public CombatOutcome Outcome { get; }
    public string Summary { get; }
}

public static class CombatRuleResolver
{
    public static CombatResolution Resolve(CombatState actorState, CombatState opponentState)
    {
        return actorState switch
        {
            CombatState.Attack => ResolveAttack(opponentState),
            CombatState.Dodge => ResolveDodge(opponentState),
            _ => ResolveNeutral(opponentState),
        };
    }

    public static string ToDisplayName(CombatState state)
    {
        return state switch
        {
            CombatState.Attack => "\uACF5\uACA9",
            CombatState.Dodge => "\uD68C\uD53C",
            _ => "\uC911\uB9BD",
        };
    }

    static CombatResolution ResolveAttack(CombatState opponentState)
    {
        return opponentState switch
        {
            CombatState.Attack => CreateResolution(
                CombatState.Attack,
                opponentState,
                CombatOutcome.AttackClash,
                "\uACF5\uACA9 \uCDA9\uB3CC"),
            CombatState.Dodge => CreateResolution(
                CombatState.Attack,
                opponentState,
                CombatOutcome.AttackDodged,
                "\uD68C\uD53C\uB2F9\uD568"),
            _ => CreateResolution(
                CombatState.Attack,
                opponentState,
                CombatOutcome.AttackHitsNeutral,
                "\uACF5\uACA9 \uC801\uC911"),
        };
    }

    static CombatResolution ResolveNeutral(CombatState opponentState)
    {
        return opponentState == CombatState.Attack
            ? CreateResolution(
                CombatState.Neutral,
                opponentState,
                CombatOutcome.NeutralHitByAttack,
                "\uACF5\uACA9 \uBC1B\uC74C")
            : CreateResolution(
                CombatState.Neutral,
                opponentState,
                CombatOutcome.None,
                "\uC911\uB9BD \uC720\uC9C0");
    }

    static CombatResolution ResolveDodge(CombatState opponentState)
    {
        return opponentState == CombatState.Attack
            ? CreateResolution(
                CombatState.Dodge,
                opponentState,
                CombatOutcome.DodgeSuccess,
                "\uD68C\uD53C \uC131\uACF5")
            : CreateResolution(
                CombatState.Dodge,
                opponentState,
                CombatOutcome.DodgeFail,
                "\uD68C\uD53C \uC2E4\uD328");
    }

    static CombatResolution CreateResolution(
        CombatState actorState,
        CombatState opponentState,
        CombatOutcome outcome,
        string summary)
    {
        return new CombatResolution(actorState, opponentState, outcome, summary);
    }
}
