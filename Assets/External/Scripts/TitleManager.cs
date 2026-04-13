using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class TitleManager : MonoBehaviour
{
    [Header("Start Scene")]
    [SerializeField] private string defaultStartSceneName = "Tutorial";
    [SerializeField] private string checkedStartSceneName = "Round1";
    [SerializeField] private bool useCheckedStartScene;
    [SerializeField] private GameObject checkedMark;

    [SerializeField] private List<Button> buttons = new();

    private int focusedIndex = 0;

    void Start()
    {
        if (buttons.Count > 0)
            SelectButton(0);

        ApplyCheckedMark();
    }

    void Update()
    {
        if (buttons.Count == 0)
            return;

        if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.LeftArrow))
        {
            focusedIndex = (focusedIndex - 1 + buttons.Count) % buttons.Count;
            SelectButton(focusedIndex);
        }
        else if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.RightArrow))
        {
            focusedIndex = (focusedIndex + 1) % buttons.Count;
            SelectButton(focusedIndex);
        }
    }

    void SelectButton(int index)
    {
        if (buttons[index] != null)
            buttons[index].Select();
    }

    public void OnClickStartButton(string sceneName)
    {
        Debug.Log("Start Button Clicked");
        LoadStartScene(sceneName);
    }

    public void OnClickStartButton()
    {
        Debug.Log("Start Button Clicked");
        LoadStartScene(defaultStartSceneName);
    }

    public void SetCheckedStartScene(bool enabled)
    {
        useCheckedStartScene = enabled;
        ApplyCheckedMark();
    }

    public void ToggleCheckedStartScene()
    {
        SetCheckedStartScene(!useCheckedStartScene);
    }

    public void OnClickCheckedStartSceneButton()
    {
        ToggleCheckedStartScene();
    }

    public void OnClickExitButton()
    {
        Debug.Log("Exit Button Clicked");
        Application.Quit();
    }

    void LoadStartScene(string fallbackSceneName)
    {
        string sceneName = useCheckedStartScene
            ? checkedStartSceneName
            : fallbackSceneName;

        if (string.IsNullOrWhiteSpace(sceneName))
            sceneName = defaultStartSceneName;

        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogWarning("Start scene name is empty.", this);
            return;
        }

        SceneManager.LoadScene(sceneName);
    }

    void ApplyCheckedMark()
    {
        if (checkedMark != null)
            checkedMark.SetActive(useCheckedStartScene);
    }
}
