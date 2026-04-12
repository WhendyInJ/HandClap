using System.Collections;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class HitEffectSpawnAnimator : MonoBehaviour
{
    private SpriteRenderer[] spriteRenderers;
    private Graphic[] graphics;
    private Color[] spriteBaseColors;
    private Color[] graphicBaseColors;
    private Coroutine animationRoutine;

    public void Play(
        float startScaleMultiplier,
        float shrinkScaleMultiplier,
        float fadeInDuration,
        float settleDuration)
    {
        CacheRenderers();

        if (animationRoutine != null)
            StopCoroutine(animationRoutine);

        animationRoutine = StartCoroutine(Animate(
            Mathf.Max(0f, startScaleMultiplier),
            Mathf.Max(0f, shrinkScaleMultiplier),
            Mathf.Max(0f, fadeInDuration),
            Mathf.Max(0f, settleDuration)));
    }

    IEnumerator Animate(
        float startScaleMultiplier,
        float shrinkScaleMultiplier,
        float fadeInDuration,
        float settleDuration)
    {
        Vector3 baseScale = transform.localScale;
        Vector3 startScale = baseScale * startScaleMultiplier;
        Vector3 shrinkScale = baseScale * shrinkScaleMultiplier;

        transform.localScale = startScale;
        SetAlpha(0f);

        float elapsed = 0f;
        while (elapsed < fadeInDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, fadeInDuration));
            float eased = EaseOutCubic(t);
            transform.localScale = Vector3.LerpUnclamped(startScale, shrinkScale, eased);
            SetAlpha(t);
            yield return null;
        }

        transform.localScale = shrinkScale;
        SetAlpha(1f);

        elapsed = 0f;
        while (elapsed < settleDuration)
        {
            elapsed += Time.deltaTime;
            float t = EaseOutBack(Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, settleDuration)));
            transform.localScale = Vector3.LerpUnclamped(shrinkScale, baseScale, t);
            yield return null;
        }

        transform.localScale = baseScale;
        SetAlpha(1f);
        animationRoutine = null;
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

    static float EaseOutBack(float t)
    {
        t = Mathf.Clamp01(t);
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }
}
