using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

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

        if (loadDelay <= 0f)
        {
            LoadTargetScene();
            return;
        }

        loadRoutine = StartCoroutine(LoadTargetSceneAfterDelay());
    }

    IEnumerator LoadTargetSceneAfterDelay()
    {
        float elapsed = 0f;
        while (elapsed < loadDelay)
        {
            elapsed += useUnscaledDelay ? Time.unscaledDeltaTime : Time.deltaTime;
            yield return null;
        }

        LoadTargetScene();
    }

    void LoadTargetScene()
    {
        if (string.IsNullOrWhiteSpace(targetSceneName))
        {
            Debug.LogWarning("Target scene name is empty.", this);
            return;
        }

        Time.timeScale = 1f;
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
}
