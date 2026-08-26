using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneTransitionManager : MonoBehaviour
{
    [SerializeField] private string resultSceneName = "VRPhotoResultTest";
    [SerializeField] private string playSceneName = "TestScene";

    public void LoadResultScene()
    {
        VRScreenFader.Instance.LoadSceneWithFade(resultSceneName, 0.5f);
    }

    public void LoadPlaySceneAndClear()
    {
        VRScreenFader.Instance.LoadSceneWithFade(
            playSceneName,
            0.5f,
            PhotoGalleryManager.ClearPhotos);
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
