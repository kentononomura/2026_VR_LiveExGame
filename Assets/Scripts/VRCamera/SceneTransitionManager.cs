using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneTransitionManager : MonoBehaviour
{
    [SerializeField] private string resultSceneName = "VRPhotoResultTest";
    [SerializeField] private string playSceneName = "TestScene";

    public void LoadResultScene()
    {
        if (VRScreenFader.Instance != null)
        {
            VRScreenFader.Instance.FadeOut(0.5f, () =>
            {
                SceneManager.LoadScene(resultSceneName);
            });
        }
        else
        {
            SceneManager.LoadScene(resultSceneName);
        }
    }

    public void LoadPlaySceneAndClear()
    {
        if (VRScreenFader.Instance != null)
        {
            VRScreenFader.Instance.FadeOut(0.5f, () =>
            {
                PhotoGalleryManager.ClearPhotos();
                SceneManager.LoadScene(playSceneName);
            });
        }
        else
        {
            PhotoGalleryManager.ClearPhotos();
            SceneManager.LoadScene(playSceneName);
        }
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
