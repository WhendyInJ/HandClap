using System;
using UnityEngine;

[DisallowMultipleComponent]
public class CombatHealth : MonoBehaviour
{
    [SerializeField, Min(1f)] private float maxHealth = 5f;
    [SerializeField] private bool resetOnAwake = true;
    [SerializeField, Min(0f)] private float currentHealth;

    private bool initialized;

    public event Action<CombatHealth> HealthChanged;
    public event Action<CombatHealth> Died;

    public float MaxHealth => maxHealth;
    public float CurrentHealth
    {
        get
        {
            EnsureInitialized();
            return currentHealth;
        }
    }
    public float Normalized => maxHealth <= 0f ? 0f : Mathf.Clamp01(CurrentHealth / maxHealth);
    public bool IsAlive => CurrentHealth > 0f;

    void Awake()
    {
        EnsureInitialized();
    }

    void OnValidate()
    {
        maxHealth = Mathf.Max(1f, maxHealth);
        currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
    }

    public void EnsureInitialized()
    {
        if (initialized)
            return;

        currentHealth = resetOnAwake || currentHealth <= 0f
            ? maxHealth
            : Mathf.Clamp(currentHealth, 0f, maxHealth);
        initialized = true;
    }

    public void ResetHealth()
    {
        EnsureInitialized();
        currentHealth = maxHealth;
        HealthChanged?.Invoke(this);
    }

    public void SetMaxHealth(float value, bool refillHealth)
    {
        EnsureInitialized();
        maxHealth = Mathf.Max(1f, value);

        currentHealth = refillHealth
            ? maxHealth
            : Mathf.Clamp(currentHealth, 0f, maxHealth);

        HealthChanged?.Invoke(this);
    }

    public float ApplyDamage(float damage)
    {
        EnsureInitialized();

        if (damage <= 0f || currentHealth <= 0f)
            return 0f;

        float previousHealth = currentHealth;
        currentHealth = Mathf.Clamp(currentHealth - damage, 0f, maxHealth);
        float appliedDamage = previousHealth - currentHealth;

        if (appliedDamage <= 0f)
            return 0f;

        HealthChanged?.Invoke(this);

        if (currentHealth <= 0f)
            Died?.Invoke(this);

        return appliedDamage;
    }

    public float Heal(float amount)
    {
        EnsureInitialized();

        if (amount <= 0f || currentHealth >= maxHealth)
            return 0f;

        float previousHealth = currentHealth;
        currentHealth = Mathf.Clamp(currentHealth + amount, 0f, maxHealth);
        float healedAmount = currentHealth - previousHealth;

        if (healedAmount > 0f)
            HealthChanged?.Invoke(this);

        return healedAmount;
    }
}
