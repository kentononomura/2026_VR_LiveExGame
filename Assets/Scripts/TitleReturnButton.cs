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
        // タイトルに戻る際に、これまでに撮影した写真をすべてリセット（メモリ解放）する
        PhotoGalleryManager.ClearPhotos();

        // VRScreenFaderが存在する場合はフェードアウトして遷移
        if (VRScreenFader.Instance != null)
        {
            VRScreenFader.Instance.FadeOut(fadeDuration, () =>
            {
                SceneManager.LoadSceneAsync(titleSceneName);
            });
        }
        else
        {
            // フェーダーが無い場合は即座に遷移
            SceneManager.LoadSceneAsync(titleSceneName);
        }
    }
}
