using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class Round3EndSceneLoader : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RoundGameManager roundGameManager;
    [SerializeField] private BattleUiController battleUiController;

    [Header("Scene")]
    [FormerlySerializedAs("endSceneName")]
    [SerializeField] private string targetSceneName = "End";
    [SerializeField] private bool requireActiveSceneName;
    [FormerlySerializedAs("round3SceneName")]
    [SerializeField] private string requiredActiveSceneName = "Round3";
    [SerializeField, Min(0f)] private float loadDelay = 0f;
    [SerializeField] private bool useUnscaledDelay = true;

    [Header("Win Presentation")]
    [SerializeField] private bool playWinPresentation = true;
    [SerializeField] private GameObject winPrefab;
    [SerializeField] private Transform winPresentationParent;
    [SerializeField] private Transform winPresentationSpawnPoint;
    [SerializeField, Min(0f)] private float winDisplayDuration = 0.8f;
    [SerializeField, Min(0f)] private float winPopInDuration = 0.14f;
    [SerializeField, Min(0f)] private float winFadeOutDuration = 0.18f;
    [SerializeField, Min(0f)] private float winStartScaleMultiplier = 0.8f;
    [SerializeField, Min(0f)] private float winPeakScaleMultiplier = 1.12f;
    [SerializeField] private bool destroyWinPrefabAfterDisplay = true;

    [Header("Fade Transition")]
    [SerializeField] private bool useFadeTransition = true;
    [SerializeField, Min(0f)] private float sceneFadeOutDuration = 0.6f;
    [SerializeField, Min(0f)] private float sceneFadeInDuration = 0.6f;

    private bool loadRequested;
    private Coroutine loadRoutine;

    void Reset()
    {
        TryAutoAssignReferences();
    }

    void Awake()
    {
        TryAutoAssignReferences();
    }

    void OnEnable()
    {
        TryAutoAssignReferences();
        SubscribeEvents();
        TryLoadTargetSceneFromCurrentState();
    }

    void OnDisable()
    {
        UnsubscribeEvents();

        if (loadRoutine != null)
        {
            StopCoroutine(loadRoutine);
            loadRoutine = null;
        }
    }

    void OnValidate()
    {
        loadDelay = Mathf.Max(0f, loadDelay);
        winDisplayDuration = Mathf.Max(0f, winDisplayDuration);
        winPopInDuration = Mathf.Max(0f, winPopInDuration);
        winFadeOutDuration = Mathf.Max(0f, winFadeOutDuration);
        winStartScaleMultiplier = Mathf.Max(0f, winStartScaleMultiplier);
        winPeakScaleMultiplier = Mathf.Max(0f, winPeakScaleMultiplier);
        sceneFadeOutDuration = Mathf.Max(0f, sceneFadeOutDuration);
        sceneFadeInDuration = Mathf.Max(0f, sceneFadeInDuration);
    }

    void HandleEnemyDefeated()
    {
        RequestLoadTargetScene();
    }

    void HandleRoundStateChanged(RoundGameState state)
    {
        if (state == RoundGameState.Victory)
            RequestLoadTargetScene();
    }

    void TryLoadTargetSceneFromCurrentState()
    {
        if (roundGameManager != null && roundGameManager.State == RoundGameState.Victory)
            RequestLoadTargetScene();
    }

    void RequestLoadTargetScene()
    {
        if (loadRequested || !CanLoadTargetScene())
            return;

        loadRequested = true;
        loadRoutine = StartCoroutine(RunWinThenLoadScene());
    }

    IEnumerator RunWinThenLoadScene()
    {
        if (loadDelay <= 0f)
        {
            yield return null;
        }
        else
        {
            yield return WaitTransitionSeconds(loadDelay);
        }

        if (playWinPresentation && winPrefab != null)
            yield return ShowWinPresentationPrefab();

        LoadTargetScene();
    }

    IEnumerator ShowWinPresentationPrefab()
    {
        GameObject instance = InstantiateWinPresentationPrefab();
        if (instance == null)
            yield break;

        yield return AnimateWinPresentationInstance(instance);

        if (destroyWinPrefabAfterDisplay && instance != null)
            Destroy(instance);
    }

    GameObject InstantiateWinPresentationPrefab()
    {
        Transform parent = winPresentationParent;

        if (parent != null)
        {
            GameObject instance = Instantiate(winPrefab, parent);

            if (winPresentationSpawnPoint != null)
            {
                instance.transform.SetPositionAndRotation(
                    winPresentationSpawnPoint.position,
                    winPresentationSpawnPoint.rotation);
            }
            else if (instance.transform is RectTransform rectTransform)
            {
                rectTransform.anchoredPosition = Vector2.zero;
            }
            else
            {
                instance.transform.localPosition = Vector3.zero;
            }

            return instance;
        }

        Vector3 position = winPresentationSpawnPoint != null
            ? winPresentationSpawnPoint.position
            : Vector3.zero;
        Quaternion rotation = winPresentationSpawnPoint != null
            ? winPresentationSpawnPoint.rotation
            : winPrefab.transform.rotation;

        return Instantiate(winPrefab, position, rotation);
    }

    IEnumerator AnimateWinPresentationInstance(GameObject instance)
    {
        SpriteRenderer[] spriteRenderers = instance.GetComponentsInChildren<SpriteRenderer>(true);
        Graphic[] graphics = instance.GetComponentsInChildren<Graphic>(true);
        Color[] spriteColors = CaptureSpriteColors(spriteRenderers);
        Color[] graphicColors = CaptureGraphicColors(graphics);
        Vector3 baseScale = instance.transform.localScale;
        Vector3 startScale = baseScale * winStartScaleMultiplier;
        Vector3 peakScale = baseScale * winPeakScaleMultiplier;
        Vector3 exitScale = baseScale * Mathf.Max(1f, winPeakScaleMultiplier);

        float popDuration = Mathf.Min(winPopInDuration, winDisplayDuration);
        float fadeDuration = Mathf.Min(winFadeOutDuration, Mathf.Max(0f, winDisplayDuration - popDuration));
        float holdDuration = Mathf.Max(0f, winDisplayDuration - popDuration - fadeDuration);

        instance.transform.localScale = startScale;
        SetAlpha(spriteRenderers, graphics, spriteColors, graphicColors, 0f);

        float elapsed = 0f;
        while (elapsed < popDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, popDuration));
            instance.transform.localScale = Vector3.LerpUnclamped(startScale, peakScale, EaseOutBack(t));
            SetAlpha(spriteRenderers, graphics, spriteColors, graphicColors, t);
            yield return null;
        }

        instance.transform.localScale = peakScale;
        SetAlpha(spriteRenderers, graphics, spriteColors, graphicColors, 1f);

        elapsed = 0f;
        float settleDuration = Mathf.Min(0.12f, holdDuration);
        while (elapsed < settleDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, settleDuration));
            instance.transform.localScale = Vector3.LerpUnclamped(peakScale, baseScale, EaseOutCubic(t));
            yield return null;
        }

        instance.transform.localScale = baseScale;
        yield return WaitTransitionSeconds(Mathf.Max(0f, holdDuration - settleDuration));

        elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, fadeDuration));
            instance.transform.localScale = Vector3.LerpUnclamped(baseScale, exitScale, EaseOutCubic(t));
            SetAlpha(spriteRenderers, graphics, spriteColors, graphicColors, 1f - t);
            yield return null;
        }
    }

    IEnumerator WaitTransitionSeconds(float duration)
    {
        float elapsed = 0f;
        duration = Mathf.Max(0f, duration);

        while (elapsed < duration)
        {
            elapsed += useUnscaledDelay ? Time.unscaledDeltaTime : Time.deltaTime;
            yield return null;
        }
    }

    void LoadTargetScene()
    {
        if (string.IsNullOrWhiteSpace(targetSceneName))
        {
            Debug.LogWarning("Target scene name is empty.", this);
            return;
        }

        Time.timeScale = 1f;

        if (useFadeTransition)
        {
            if (TutorialSceneFadeOverlayRunner.IsTransitioning)
                return;

            TutorialSceneFadeOverlayRunner.Begin(
                targetSceneName,
                sceneFadeOutDuration,
                sceneFadeInDuration);
            return;
        }

        SceneManager.LoadScene(targetSceneName);
    }

    bool CanLoadTargetScene()
    {
        if (!requireActiveSceneName)
            return true;

        return SceneManager.GetActiveScene().name == requiredActiveSceneName;
    }

    void SubscribeEvents()
    {
        if (battleUiController != null)
        {
            battleUiController.EnemyDefeated -= HandleEnemyDefeated;
            battleUiController.EnemyDefeated += HandleEnemyDefeated;
        }

        if (roundGameManager != null)
        {
            roundGameManager.StateChanged -= HandleRoundStateChanged;
            roundGameManager.StateChanged += HandleRoundStateChanged;
        }
    }

    void UnsubscribeEvents()
    {
        if (battleUiController != null)
            battleUiController.EnemyDefeated -= HandleEnemyDefeated;

        if (roundGameManager != null)
            roundGameManager.StateChanged -= HandleRoundStateChanged;
    }

    void TryAutoAssignReferences()
    {
        if (roundGameManager == null)
            roundGameManager = FindFirstObjectByType<RoundGameManager>();

        if (battleUiController == null)
            battleUiController = FindFirstObjectByType<BattleUiController>();
    }

    static Color[] CaptureSpriteColors(SpriteRenderer[] spriteRenderers)
    {
        Color[] colors = new Color[spriteRenderers.Length];
        for (int i = 0; i < spriteRenderers.Length; i++)
            colors[i] = spriteRenderers[i] != null ? spriteRenderers[i].color : Color.white;

        return colors;
    }

    static Color[] CaptureGraphicColors(Graphic[] graphics)
    {
        Color[] colors = new Color[graphics.Length];
        for (int i = 0; i < graphics.Length; i++)
            colors[i] = graphics[i] != null ? graphics[i].color : Color.white;

        return colors;
    }

    static void SetAlpha(
        SpriteRenderer[] spriteRenderers,
        Graphic[] graphics,
        Color[] spriteColors,
        Color[] graphicColors,
        float normalizedAlpha)
    {
        normalizedAlpha = Mathf.Clamp01(normalizedAlpha);

        for (int i = 0; i < spriteRenderers.Length; i++)
        {
            if (spriteRenderers[i] == null)
                continue;

            Color color = spriteColors[i];
            color.a *= normalizedAlpha;
            spriteRenderers[i].color = color;
        }

        for (int i = 0; i < graphics.Length; i++)
        {
            if (graphics[i] == null)
                continue;

            Color color = graphicColors[i];
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
