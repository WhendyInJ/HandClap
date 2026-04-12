using System;
using UnityEngine;

[DisallowMultipleComponent]
public class CombatActorStats : MonoBehaviour
{
    [Header("Slot Build")]
    [Tooltip("Element / BodyType / HandSize. 리롤 슬롯 결과는 RoundGameManager가 ApplyBuild로 플레이어에 반영합니다.")]
    [SerializeField] private PlayerBuild build = PlayerBuild.Default;

    [Header("Attack")]
    [SerializeField, Min(0f)] private float baseAttackDamage = 1f;

    public event Action<CombatActorStats> StatsChanged;
    public event Action<PlayerBuild> BuildChanged;

    public PlayerBuild Build => build;
    public Element Element => build.Element;
    public BodyType BodyType => build.BodyType;
    public HandSize HandSize => build.HandSize;
    public float BaseAttackDamage => baseAttackDamage;

    public float AttackDamageMultiplier => SlotStatRules.GetHandDamageMultiplier(build.HandSize);
    public float AttackCooldownMultiplier => SlotStatRules.GetAttackCooldownMultiplier(build.HandSize);
    public float ReceivedDamageMultiplier => SlotStatRules.GetReceivedDamageMultiplier(build.BodyType);
    public float StaggerDifficultyMultiplier => SlotStatRules.GetStaggerDifficultyMultiplier(build.BodyType);

    void OnValidate()
    {
        baseAttackDamage = Mathf.Max(0f, baseAttackDamage);
    }

    public void ApplyBuild(PlayerBuild newBuild)
    {
        build = newBuild;
        BuildChanged?.Invoke(build);
        StatsChanged?.Invoke(this);
    }

    public void SetBuild(Element element, BodyType bodyType, HandSize handSize)
    {
        ApplyBuild(new PlayerBuild
        {
            Element = element,
            BodyType = bodyType,
            HandSize = handSize
        });
    }

    public void SetBaseAttackDamage(float damage)
    {
        baseAttackDamage = Mathf.Max(0f, damage);
        StatsChanged?.Invoke(this);
    }

    public float CalculateDamageToEnemy(CombatActorStats enemyStats)
    {
        return enemyStats == null
            ? 0f
            : SlotStatRules.CalculateEnemyReceivedDamage(
                baseAttackDamage,
                build,
                enemyStats.Build);
    }

    public float CalculateDamageToPlayer(CombatActorStats playerStats)
    {
        return playerStats == null
            ? 0f
            : SlotStatRules.CalculatePlayerReceivedDamage(
                baseAttackDamage,
                build,
                playerStats.Build);
    }
}
