using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class VRScreenFader : MonoBehaviour
{
    private static VRScreenFader _instance;
    public static VRScreenFader Instance
    {
        get
        {
            if (_instance == null)
            {
                var obj = new GameObject("VRScreenFaderPersistent");
                DontDestroyOnLoad(obj);
                _instance = obj.AddComponent<VRScreenFader>();
            }
            return _instance;
        }
        private set { _instance = value; }
    }

    private Canvas fadeCanvas;
    private Image fadeImage;
    private bool isFading = false;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(this); // スクリプト単体を破棄（親オブジェクトは壊さない）
            return;
        }

        // 自分がApp Configなどの他のオブジェクトにアタッチされている場合、
        // フェード専用の独立したオブジェクトを生成してそこに機能を移譲する。
        // （これによりApp Config全体がDontDestroyOnLoadになってしまうバグを防ぐ）
        if (gameObject.name != "VRScreenFaderPersistent")
        {
            GameObject persistentObj = new GameObject("VRScreenFaderPersistent");
            DontDestroyOnLoad(persistentObj);
            
            // 新しいオブジェクトに自身と同じコンポーネントを追加（追加時に新しいAwakeが走る）
            persistentObj.AddComponent<VRScreenFader>();
            
            // 元のオブジェクトからこのスクリプトだけを削除
            Destroy(this);
            return;
        }

        Instance = this;
        CreateFadeCanvas();
        
        // シーンがロードされたら自動的にフェードインする
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        // シーンロード時に自動で画面を明るくする
        if (gameObject.activeInHierarchy)
        {
            FadeIn(1.0f, null);
        }
    }

    private void CreateFadeCanvas()
    {
        // Create a persistent Canvas for the screen fade effect
        GameObject canvasObj = new GameObject("VRFadeCanvas");
        canvasObj.transform.SetParent(transform);
        
        fadeCanvas = canvasObj.AddComponent<Canvas>();
        fadeCanvas.renderMode = RenderMode.ScreenSpaceCamera; // VR対応: OverlayではなくCameraを使う
        fadeCanvas.worldCamera = Camera.main;
        fadeCanvas.planeDistance = 0.1f; // カメラの10cm前に配置
        fadeCanvas.sortingOrder = 9999;

        canvasObj.AddComponent<CanvasScaler>();
        canvasObj.AddComponent<GraphicRaycaster>();

        // Create the fullscreen black image
        GameObject imageObj = new GameObject("FadeImage");
        imageObj.transform.SetParent(canvasObj.transform, false);

        fadeImage = imageObj.AddComponent<Image>();
        fadeImage.color = new Color(0, 0, 0, 0); // Start fully transparent

        // Stretch the image to fill the screen
        RectTransform rect = fadeImage.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.sizeDelta = Vector2.zero;
        rect.anchoredPosition = Vector2.zero;
    }

    private void Update()
    {
        // シーン遷移などでメインカメラが変わった場合に自動追従する
        if (fadeCanvas != null && fadeCanvas.worldCamera == null)
        {
            fadeCanvas.worldCamera = Camera.main;
        }
    }

    public void FadeOut(float duration, System.Action onComplete)
    {
        if (isFading) return;
        StartCoroutine(FadeRoutine(0f, 1f, duration, onComplete));
    }

    public void FadeIn(float duration, System.Action onComplete)
    {
        if (isFading) return;
        StartCoroutine(FadeRoutine(1f, 0f, duration, onComplete));
    }

    private IEnumerator FadeRoutine(float startAlpha, float endAlpha, float duration, System.Action onComplete)
    {
        isFading = true;
        
        // Ensure Canvas is active
        if (fadeCanvas != null) fadeCanvas.gameObject.SetActive(true);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Lerp(startAlpha, endAlpha, elapsed / duration);
            if (fadeImage != null)
            {
                fadeImage.color = new Color(0, 0, 0, alpha);
            }
            yield return null;
        }

        if (fadeImage != null)
        {
            fadeImage.color = new Color(0, 0, 0, endAlpha);
        }

        // If fully faded out, keep canvas active. If fully faded in, deactivate to save drawcalls.
        if (endAlpha <= 0f && fadeCanvas != null)
        {
            fadeCanvas.gameObject.SetActive(false);
        }

        isFading = false;
        onComplete?.Invoke();
    }
}
