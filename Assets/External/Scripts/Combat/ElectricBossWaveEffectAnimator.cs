using System.Collections;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class ElectricBossWaveEffectAnimator : MonoBehaviour
{
    private SpriteRenderer[] spriteRenderers;
    private Graphic[] graphics;
    private Color[] spriteBaseColors;
    private Color[] graphicBaseColors;
    private Coroutine animationRoutine;

    public void Play(
        float duration,
        float startScaleMultiplier,
        float endScaleMultiplier,
        bool fadeOut,
        bool destroyWhenFinished)
    {
        CacheRenderers();

        if (animationRoutine != null)
            StopCoroutine(animationRoutine);

        animationRoutine = StartCoroutine(Animate(
            Mathf.Max(0f, duration),
            Mathf.Max(0f, startScaleMultiplier),
            Mathf.Max(0f, endScaleMultiplier),
            fadeOut,
            destroyWhenFinished));
    }

    IEnumerator Animate(
        float duration,
        float startScaleMultiplier,
        float endScaleMultiplier,
        bool fadeOut,
        bool destroyWhenFinished)
    {
        Vector3 baseScale = transform.localScale;
        Vector3 startScale = baseScale * startScaleMultiplier;
        Vector3 endScale = baseScale * endScaleMultiplier;

        if (duration <= 0f)
        {
            transform.localScale = endScale;
            SetAlpha(fadeOut ? 0f : 1f);
            Finish(destroyWhenFinished);
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float easedT = EaseOutCubic(t);

            transform.localScale = Vector3.LerpUnclamped(startScale, endScale, easedT);
            SetAlpha(fadeOut ? 1f - t : 1f);
            yield return null;
        }

        transform.localScale = endScale;
        SetAlpha(fadeOut ? 0f : 1f);
        Finish(destroyWhenFinished);
    }

    void Finish(bool destroyWhenFinished)
    {
        animationRoutine = null;

        if (destroyWhenFinished)
            Destroy(gameObject);
    }

    void CacheRenderers()
    {
        spriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        graphics = GetComponentsInChildren<Graphic>(true);

        spriteBaseColors = new Color[spriteRenderers.Length];
        for (int i = 0; i < spriteRenderers.Length; i++)
            spriteBaseColors[i] = spriteRenderers[i].color;

        graphicBaseColors = new Color[graphics.Length];
        for (int i = 0; i < graphics.Length; i++)
            graphicBaseColors[i] = graphics[i].color;
    }

    void SetAlpha(float normalizedAlpha)
    {
        normalizedAlpha = Mathf.Clamp01(normalizedAlpha);

        for (int i = 0; i < spriteRenderers.Length; i++)
        {
            if (spriteRenderers[i] == null)
                continue;

            Color color = spriteBaseColors[i];
            color.a *= normalizedAlpha;
            spriteRenderers[i].color = color;
        }

        for (int i = 0; i < graphics.Length; i++)
        {
            if (graphics[i] == null)
                continue;

            Color color = graphicBaseColors[i];
            color.a *= normalizedAlpha;
            graphics[i].color = color;
        }
    }

    static float EaseOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        return 1f - Mathf.Pow(1f - t, 3f);
    }
}
