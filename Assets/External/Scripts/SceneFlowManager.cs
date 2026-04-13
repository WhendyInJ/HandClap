using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 씬 전환 싱글톤. 빈 오브젝트 하나에 붙이고 DontDestroyOnLoad로 유지합니다.
/// 버튼에는 <see cref="SceneLoadButton"/>을 같이 사용해 Inspector에서 씬 이름을 지정하세요.
/// </summary>
public class SceneFlowManager : MonoBehaviour
{
    public static SceneFlowManager Instance { get; private set; }

    [Header("선택")]
    [Tooltip("비동기 로드 시 최소 한 프레임은 기다린 뒤 활성화합니다.")]
    [SerializeField] private bool useAsyncLoad = true;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>Build Settings에 등록된 씬 이름으로 이동 (버튼에서 문자열 넘기기 어려울 때는 SceneLoadButton 사용).</summary>
    public void LoadScene(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogWarning("SceneFlowManager.LoadScene: 씬 이름이 비어 있습니다.", this);
            return;
        }

        if (useAsyncLoad)
            StartCoroutine(LoadSceneAsyncRoutine(sceneName));
        else
            SceneManager.LoadScene(sceneName);
    }

    public void LoadScene(int buildIndex)
    {
        if (buildIndex < 0 || buildIndex >= SceneManager.sceneCountInBuildSettings)
        {
            Debug.LogWarning($"SceneFlowManager.LoadScene: 잘못된 build index {buildIndex}.", this);
            return;
        }

        if (useAsyncLoad)
            StartCoroutine(LoadSceneAsyncRoutine(buildIndex));
        else
            SceneManager.LoadScene(buildIndex);
    }

    public void ReloadCurrentScene()
    {
        Scene s = SceneManager.GetActiveScene();
        if (!s.IsValid())
            return;

        LoadScene(s.name);
    }

    IEnumerator LoadSceneAsyncRoutine(string sceneName)
    {
        AsyncOperation op = SceneManager.LoadSceneAsync(sceneName);
        if (op == null)
        {
            Debug.LogError($"SceneFlowManager: 씬을 불러올 수 없습니다. 이름·Build Settings 등록을 확인하세요: \"{sceneName}\"", this);
            yield break;
        }

        while (!op.isDone)
            yield return null;
    }

    IEnumerator LoadSceneAsyncRoutine(int buildIndex)
    {
        AsyncOperation op = SceneManager.LoadSceneAsync(buildIndex);
        if (op == null)
        {
            Debug.LogError($"SceneFlowManager: build index {buildIndex} 로드 실패. Build Settings를 확인하세요.", this);
            yield break;
        }

        while (!op.isDone)
            yield return null;
    }
}

/// <summary>
/// UI Button에 붙여서 OnClick에 <c>LoadTargetScene</c>만 연결하고, 아래에 씬 이름을 적습니다.
/// </summary>
[DisallowMultipleComponent]
public class SceneLoadButton : MonoBehaviour
{
    [SerializeField] private string sceneName;
    [SerializeField] private int buildIndex = -1;
    [Tooltip("체크하면 sceneName 대신 Build Index를 사용합니다.")]
    [SerializeField] private bool useBuildIndex;

    public void LoadTargetScene()
    {
        if (SceneFlowManager.Instance == null)
        {
            Debug.LogWarning("SceneLoadButton: SceneFlowManager가 씬에 없습니다. 빈 오브젝트에 SceneFlowManager를 추가하세요.", this);
            return;
        }

        if (useBuildIndex)
            SceneFlowManager.Instance.LoadScene(buildIndex);
        else
            SceneFlowManager.Instance.LoadScene(sceneName);
    }
}
