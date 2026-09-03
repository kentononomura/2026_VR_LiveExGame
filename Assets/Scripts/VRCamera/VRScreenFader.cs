using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
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
    private bool isSceneTransitioning;
    private bool managedSceneLoadInProgress;

    public bool IsSceneTransitioning => isSceneTransitioning;

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

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // 一元管理された遷移中は、ロードコルーチン自身が準備完了後にフェードインする。
        // ここでStopAllCoroutinesするとロード処理まで停止するため、管理外ロード時だけ従来処理を行う。
        if (!managedSceneLoadInProgress)
        {
            StopAllCoroutines();
            isFading = false;
        }
        
        if (Camera.main != null)
        {
            UnityEngine.Debug.Log($"[VRScreenFader] シーンロード直後のカメラ座標 - Scene: {scene.name}, Position: {Camera.main.transform.position}, LocalPosition: {Camera.main.transform.localPosition}");
        }

        // Oculus Link/Meta Quest特有のシーン遷移時トラッキングフリーズ（画面が張り付く、床にめり込む）対策：
        // シーンロード直後にトラッキングポーズをリセットし、カメラの再取得を強制する
        StartCoroutine(ResetTrackingRoutine());
        
        if (!managedSceneLoadInProgress && gameObject.activeInHierarchy)
        {
            if (fadeCanvas != null) fadeCanvas.gameObject.SetActive(true);
            if (fadeImage != null) fadeImage.color = new Color(0f, 0f, 0f, 1f);
            StartCoroutine(FadeInWhenSceneReady(scene, 1.0f));
        }
    }

    /// <summary>
    /// 黒フェード中に非同期ロードとシーン初期化を完了し、安定フレーム後にフェードインします。
    /// </summary>
    public void LoadSceneWithFade(
        string sceneName,
        float fadeDuration = 1.0f,
        System.Action beforeLoad = null)
    {
        if (isSceneTransitioning || string.IsNullOrEmpty(sceneName)) return;
        StartCoroutine(LoadSceneRoutine(sceneName, Mathf.Max(0.01f, fadeDuration), beforeLoad));
    }

    private IEnumerator LoadSceneRoutine(string sceneName, float fadeDuration, System.Action beforeLoad)
    {
        isSceneTransitioning = true;
        managedSceneLoadInProgress = true;
        float transitionStartedAt = Time.realtimeSinceStartup;
        Debug.Log($"[SceneLoader] '{sceneName}' への遷移を開始します。");

        yield return StartCoroutine(FadeRoutine(0f, 1f, fadeDuration, null));

        // FadeRoutine の完了直後にロードを始めると、最終的な真っ黒のフレームが
        // HMDへ提出される前にScene activationがメインスレッドを占有する場合がある。
        // 描画完了まで待ち、XR compositorが再投影できる黒フレームを確実に渡す。
        yield return new WaitForEndOfFrame();
        beforeLoad?.Invoke();

        ThreadPriority previousPriority = Application.backgroundLoadingPriority;
        Application.backgroundLoadingPriority = ThreadPriority.Low;

        AsyncOperation loadOperation = SceneManager.LoadSceneAsync(sceneName);
        if (loadOperation == null)
        {
            Debug.LogError($"[VRScreenFader] シーン '{sceneName}' のロードを開始できませんでした。");
            Application.backgroundLoadingPriority = previousPriority;
            managedSceneLoadInProgress = false;
            isSceneTransitioning = false;
            yield return StartCoroutine(FadeRoutine(1f, 0f, fadeDuration, null));
            yield break;
        }

        // バックグラウンドロードが通常フレームより優先されないよう明示する。
        loadOperation.priority = -1;

        // 読み込みを90%まで進め、黒画面が確実に描画された状態でActivationを行う。
        loadOperation.allowSceneActivation = false;
        while (loadOperation.progress < 0.9f)
        {
            yield return null;
        }

        // Activation直前にも完成した黒フレームを提出する。重いAwake/OnEnableが
        // あっても、ユーザーには直前の安定した黒画面が再投影される。
        yield return new WaitForEndOfFrame();
        loadOperation.allowSceneActivation = true;
        while (!loadOperation.isDone)
        {
            yield return null;
        }

        Debug.Log($"[SceneLoader] '{sceneName}' のActivation完了: {Time.realtimeSinceStartup - transitionStartedAt:F2}秒");

        Application.backgroundLoadingPriority = previousPriority;

        Scene loadedScene = SceneManager.GetSceneByName(sceneName);
        yield return StartCoroutine(WaitForSceneReady(loadedScene));

        // XR Compositorへ安定した黒フレームを数回提出してから表示を戻す。
        yield return StartCoroutine(WaitForStableFrames());

        yield return StartCoroutine(FadeRoutine(1f, 0f, fadeDuration, null));
        managedSceneLoadInProgress = false;
        isSceneTransitioning = false;
        Debug.Log($"[SceneLoader] '{sceneName}' の準備・フェードイン完了: {Time.realtimeSinceStartup - transitionStartedAt:F2}秒");
    }

    private IEnumerator FadeInWhenSceneReady(Scene scene, float duration)
    {
        yield return StartCoroutine(WaitForSceneReady(scene));
        yield return StartCoroutine(WaitForStableFrames());
        yield return StartCoroutine(FadeRoutine(1f, 0f, duration, null));
    }

    private IEnumerator WaitForStableFrames()
    {
        const int requiredStableFrames = 12;
        const float maximumStableFrameTime = 0.022f;
        float timeoutAt = Time.realtimeSinceStartup + 10f;
        int stableFrames = 0;

        while (stableFrames < requiredStableFrames && Time.realtimeSinceStartup < timeoutAt)
        {
            yield return null;
            stableFrames = Time.unscaledDeltaTime <= maximumStableFrameTime
                ? stableFrames + 1
                : 0;
        }
    }

    private IEnumerator WaitForSceneReady(Scene scene)
    {
        // Startと最初のCoroutineが走るまで1フレーム待ってからReady providerを収集する。
        yield return null;

        var providers = new List<ISceneLoadReady>();
        MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include);
        foreach (MonoBehaviour behaviour in behaviours)
        {
            if (behaviour != null && behaviour.gameObject.scene == scene && behaviour is ISceneLoadReady provider)
            {
                providers.Add(provider);
            }
        }

        float nextWarningAt = Time.realtimeSinceStartup + 30f;
        while (providers.Exists(provider => provider != null && !provider.IsSceneLoadReady))
        {
            if (Time.realtimeSinceStartup >= nextWarningAt)
            {
                foreach (ISceneLoadReady provider in providers)
                {
                    if (provider != null && !provider.IsSceneLoadReady)
                    {
                        Debug.LogWarning($"[VRScreenFader] シーン準備を継続して待機しています: {provider.SceneLoadStatus}");
                    }
                }
                // 未完了のまま表示へ進めず、以後は30秒ごとに状態だけを記録する。
                nextWarningAt = Time.realtimeSinceStartup + 30f;
            }
            yield return null;
        }
    }

    private IEnumerator ResetTrackingRoutine()
    {
        // XR Rigが完全に初期化されるのを数フレーム待つ
        yield return null;
        yield return null;

        // カメラの最終座標をログ出力（デバッグ用）
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            UnityEngine.Debug.Log($"[VRScreenFader] シーンロード後のカメラ座標 - Position: {mainCam.transform.position}, LocalPosition: {mainCam.transform.localPosition}, Rotation: {mainCam.transform.rotation.eulerAngles}");
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
        if (fadeCanvas != null)
        {
            Camera currentMain = Camera.main;
            // worldCameraが未設定、または非アクティブになった古いカメラを指している、あるいは現在のCamera.mainと異なる場合は更新する
            if (fadeCanvas.worldCamera != currentMain || (fadeCanvas.worldCamera != null && !fadeCanvas.worldCamera.gameObject.activeInHierarchy))
            {
                fadeCanvas.worldCamera = currentMain;
                if (currentMain != null)
                {
                    fadeCanvas.planeDistance = 0.1f; // カメラの目の前に正確に配置
                }
            }
        }
    }

    public void FadeOut(float duration, System.Action onComplete)
    {
        if (isFading) return;
        StartCoroutine(FadeRoutine(0f, 1f, duration, onComplete));
    }

    public void FadeIn(float duration, System.Action onComplete)
    {
        // シーン遷移中のフェードインはLoadSceneRoutineだけが行う。
        // 新しいシーンのStartから呼ばれても、準備完了前に黒画面を開かない。
        if (managedSceneLoadInProgress || isFading) return;
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
            // ポーズ中やTime.timeScale変更中でも、シーン遷移のフェードは停止させない。
            elapsed += Time.unscaledDeltaTime;
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
