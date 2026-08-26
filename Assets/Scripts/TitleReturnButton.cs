using UnityEngine;
using UnityEngine.SceneManagement;

public class TitleReturnButton : MonoBehaviour
{
    [Tooltip("遷移先のシーン名")]
    public string titleSceneName = "TitleScene";
    [Tooltip("フェードにかかる時間")]
    public float fadeDuration = 1.0f;

    public void OnClickReturn()
    {
        VRScreenFader.Instance.LoadSceneWithFade(
            titleSceneName,
            fadeDuration,
            PhotoGalleryManager.ClearPhotos);
    }
}
