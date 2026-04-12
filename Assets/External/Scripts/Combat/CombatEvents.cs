using UnityEngine;

public enum CombatEventKind
{
    AttackHit,
    AttackClashed,

    /// <summary>
    /// 공격 만남에서 더 멀리 뻗은 쪽(우세한 쪽)에게 발생.
    /// AdvantageRatio(0~1)로 우위 강도를 나타낸다.
    /// </summary>
    AttackMeetingWin,

    /// <summary>
    /// 공격 만남에서 덜 뻗은 쪽(열세한 쪽)에게 발생.
    /// AdvantageRatio(0~1)로 상대의 우위 강도를 나타낸다.
    /// </summary>
    AttackMeetingLoss,

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
        string summary,
        float advantageRatio = 0f)
    {
        Kind = kind;
        Actor = actor;
        Opponent = opponent;
        ActorState = actorState;
        OpponentState = opponentState;
        Summary = summary;
        AdvantageRatio = Mathf.Clamp01(advantageRatio);
    }

    public CombatEventKind Kind { get; }
    public CombatActorController Actor { get; }
    public CombatActorController Opponent { get; }
    public CombatState ActorState { get; }
    public CombatState OpponentState { get; }
    public string Summary { get; }

    /// <summary>
    /// 공격 만남 우위 강도 (0=동등, 1=최대 우세).
    /// AttackMeetingWin / AttackMeetingLoss 이외에는 0.
    /// </summary>
    public float AdvantageRatio { get; }
}
