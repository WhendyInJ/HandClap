using UnityEngine;
using UnityEngine.SceneManagement;

public class TitleManager : MonoBehaviour
{
    public void OnClickStartButton(string sceneName)
    {
        // Start the game
        Debug.Log("Start Button Clicked");
        // You can load the next scene or start the game logic here
        SceneManager.LoadScene(sceneName);
    }

    public void OnClickExitButton()
    {
        // Exit the game
        Debug.Log("Exit Button Clicked");
        Application.Quit();
    }
}
