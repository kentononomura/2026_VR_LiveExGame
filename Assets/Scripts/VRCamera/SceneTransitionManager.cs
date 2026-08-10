using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneTransitionManager : MonoBehaviour
{
    [SerializeField] private string resultSceneName = "VRPhotoResultTest";
    [SerializeField] private string playSceneName = "VRPhotoCameraTest";

    public void LoadResultScene()
    {
        SceneManager.LoadScene(resultSceneName);
    }

    public void LoadPlaySceneAndClear()
    {
        PhotoGalleryManager.ClearPhotos();
        SceneManager.LoadScene(playSceneName);
    }

    private void Update()
    {
        // Keyboard shortcut for easy testing in editor
        if (Input.GetKeyDown(KeyCode.Return))
        {
            LoadResultScene();
        }
    }
}
