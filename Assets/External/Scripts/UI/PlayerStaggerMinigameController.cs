using System;
using UnityEngine;

public enum PlayerStaggerMinigameType
{
    SlideQte,
    GaugeHold,
}

public struct PlayerStaggerMinigameContext
{
    public float failGaugeNormalized;
    public float recoverGaugeNormalized;
    public float maxHpNormalized;
    public float difficultyMultiplier;
}

public abstract class PlayerStaggerMinigameController : MonoBehaviour
{
    public event Action<QteEndReason> MinigameEnded;
    public event Action<float, float> GaugeChanged;

    public abstract bool IsActive { get; }
    public abstract float FailGaugeNormalized { get; }
    public abstract float RecoverGaugeNormalized { get; }

    public abstract void StartMinigame(PlayerStaggerMinigameContext context);
    public abstract void StopMinigame(QteEndReason endReason);

    public virtual void SetSuppressed(bool suppressed)
    {
    }

    public virtual void ApplyIdleState(float failGaugeNormalized, float recoverGaugeNormalized, float maxHpNormalized)
    {
    }

    protected void RaiseGaugeChanged(float failGaugeNormalized, float recoverGaugeNormalized)
    {
        GaugeChanged?.Invoke(Mathf.Clamp01(failGaugeNormalized), Mathf.Clamp01(recoverGaugeNormalized));
    }

    protected void RaiseMinigameEnded(QteEndReason endReason)
    {
        MinigameEnded?.Invoke(endReason);
    }
}
