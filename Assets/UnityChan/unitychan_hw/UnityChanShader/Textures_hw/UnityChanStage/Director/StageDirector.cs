using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using Unity.XR.CoreUtils;

public class StageDirector : MonoBehaviour, ISceneLoadReady
{
    // Control options.
    public bool ignoreFastForward = true;

    // Prefabs.
    public GameObject musicPlayerPrefab;
    public GameObject mainCameraRigPrefab;
    public GameObject[] prefabsNeedsActivation;
    public GameObject[] prefabsOnTimeline;
    public GameObject[] miscPrefabs;

    // Camera points.
    public Transform[] cameraPoints;

    // Exposed to animator.
    public float overlayIntensity = 1.0f;

    public float lookatIntensity = 0.83f;

    [Header("Performance Synchronization")]
    [Tooltip("ダンスとLipSyncを音楽のDSP時計へ同期します。再生時刻の強制変更は行いません。")]
    public bool synchronizePerformanceToMusic = true;

    [Tooltip("全AudioSourceを同じDSP時刻に予約再生するための準備時間です。")]
    [Range(0.02f, 0.25f)] public float scheduledStartLeadTime = 0.08f;

    [Tooltip("ダンス・LipSync・ステージ演出を音楽より何秒先行させるか。現在の調整値は0.2秒です。")]
    [Range(0.0f, 1.0f)] public float performanceLeadTime = 0.2f;

    [Tooltip("音楽を基準にAnimator速度を緩やかに補正します。時刻の強制変更は行いません。")]
    [Range(0.0f, 0.05f)] public float maximumPlaybackSpeedCorrection = 0.02f;

    [Tooltip("音楽とアニメーションの誤差に対する速度補正の強さです。")]
    [Range(0.05f, 1.0f)] public float playbackSpeedCorrectionGain = 0.35f;

    // Objects to be controlled.
    GameObject musicPlayer;
    CameraSwitcher mainCameraSwitcher;
    ScreenOverlay[] screenOverlays;
    GameObject[] objectsNeedsActivation;
    GameObject[] objectsOnTimeline;

    readonly List<AudioSource> performanceAudioSources = new List<AudioSource>();
    readonly List<Animator> performanceAnimators = new List<Animator>();
    Animator directorAnimator;
    AudioSource masterAudioSource;
    double scheduledMusicDspTime;
    double scheduledAnimatorPauseDspTime;
    double previousSyncDspTime;
    double animationElapsedTime;
    bool musicStartHandled;
    bool waitingForAnimatorPause;
    bool waitingForScheduledMusicStart;
    bool performanceIsRunning;
    bool initializationComplete;
    string initializationStatus = "StageDirector初期化待機中";

    public bool IsSceneLoadReady => initializationComplete;
    public string SceneLoadStatus => initializationStatus;

    void Awake()
    {
        directorAnimator = GetComponent<Animator>();
        if (directorAnimator != null) directorAnimator.speed = 0f;

        screenOverlays = new ScreenOverlay[0];
        objectsNeedsActivation = new GameObject[prefabsNeedsActivation != null ? prefabsNeedsActivation.Length : 0];
        objectsOnTimeline = new GameObject[prefabsOnTimeline != null ? prefabsOnTimeline.Length : 0];

        StartCoroutine(InitializePerformanceRoutine());
    }

    private IEnumerator InitializePerformanceRoutine()
    {
        initializationStatus = "音楽プレイヤーを準備中";
        if (musicPlayerPrefab != null)
        {
            musicPlayer = Instantiate(musicPlayerPrefab);
            FreezeAnimators(musicPlayer);
        }
        yield return null;

        initializationStatus = "XRリグとシーン装備を準備中";
        GameObject cameraRig = FindOrCreateCameraRig();
        if (cameraRig != null)
        {
            EnsureTestScenePhoneCamera(cameraRig);
            mainCameraSwitcher = cameraRig.GetComponentInChildren<CameraSwitcher>(true);
            screenOverlays = cameraRig.GetComponentsInChildren<ScreenOverlay>(true);
        }
        yield return null;

        initializationStatus = "ステージ小物を準備中";
        if (prefabsNeedsActivation != null)
        {
            for (int i = 0; i < prefabsNeedsActivation.Length; i++)
            {
                if (prefabsNeedsActivation[i] != null)
                {
                    objectsNeedsActivation[i] = Instantiate(prefabsNeedsActivation[i]);
                }
                yield return null;
            }
        }

        initializationStatus = "ダンスとLipSyncを準備中";
        if (prefabsOnTimeline != null)
        {
            for (int i = 0; i < prefabsOnTimeline.Length; i++)
            {
                if (prefabsOnTimeline[i] != null)
                {
                    objectsOnTimeline[i] = Instantiate(prefabsOnTimeline[i]);
                    FreezeAnimators(objectsOnTimeline[i]);
                }
                yield return null;
            }
        }

        initializationStatus = "ステージ演出を準備中";
        if (miscPrefabs != null)
        {
            foreach (GameObject prefab in miscPrefabs)
            {
                if (prefab != null)
                {
                    Instantiate(prefab);
                }
                yield return null;
            }
        }

        CachePerformanceAnimators();

        initializationStatus = "音源データを準備中";
        if (musicPlayer != null)
        {
            performanceAudioSources.Clear();
            performanceAudioSources.AddRange(musicPlayer.GetComponentsInChildren<AudioSource>(true));
            foreach (AudioSource source in performanceAudioSources)
            {
                if (source != null && source.clip != null && source.clip.loadState == AudioDataLoadState.Unloaded)
                {
                    source.clip.LoadAudioData();
                }
            }

            bool audioIsLoading;
            do
            {
                audioIsLoading = false;
                foreach (AudioSource source in performanceAudioSources)
                {
                    if (source != null && source.clip != null && source.clip.loadState == AudioDataLoadState.Loading)
                    {
                        audioIsLoading = true;
                        break;
                    }
                }
                if (audioIsLoading) yield return null;
            }
            while (audioIsLoading);
        }

        // 初回Renderer・Animator更新を黒画面中に済ませる。
        initializationStatus = "初回描画を安定化中";
        yield return null;
        yield return null;

        // Director・ダンス・LipSyncを同じフレームの0秒地点から開始する。
        if (directorAnimator != null) directorAnimator.speed = 1f;
        SetPerformanceAnimatorSpeed(1f);
        initializationComplete = true;
        initializationStatus = "準備完了";
        Debug.Log("[StageDirector] 分割初期化が完了しました。パフォーマンスを同一フレームから開始します。");
    }

    private GameObject FindOrCreateCameraRig()
    {
        XROrigin existingOrigin = FindAnyObjectByType<XROrigin>(FindObjectsInactive.Include);
        if (existingOrigin != null)
        {
            Debug.Log($"[StageDirector] 永続XRリグ '{existingOrigin.gameObject.name}' を再利用します。");
            return existingOrigin.gameObject;
        }

        if (mainCameraRigPrefab == null) return null;

        GameObject createdRig = Instantiate(mainCameraRigPrefab);
        if (createdRig.GetComponent<XROriginPersistence>() == null)
        {
            createdRig.AddComponent<XROriginPersistence>();
        }
        Debug.Log("[StageDirector] XRリグが存在しないためフォールバック生成しました。");
        return createdRig;
    }

    private void EnsureTestScenePhoneCamera(GameObject targetRig)
    {
        if (targetRig == null || targetRig.GetComponentInChildren<VRPhoneCamera>(true) != null ||
            mainCameraRigPrefab == null)
        {
            return;
        }

        VRPhoneCamera sourcePhone = mainCameraRigPrefab.GetComponentInChildren<VRPhoneCamera>(true);
        if (sourcePhone == null) return;

        string parentPath = GetRelativeTransformPath(mainCameraRigPrefab.transform, sourcePhone.transform.parent);
        Transform targetParent = string.IsNullOrEmpty(parentPath)
            ? targetRig.transform
            : targetRig.transform.Find(parentPath);

        if (targetParent == null)
        {
            Debug.LogWarning($"[StageDirector] 永続XRリグにスマホの親 '{parentPath}' が見つかりません。");
            return;
        }

        GameObject phone = Instantiate(sourcePhone.gameObject, targetParent, false);
        phone.name = sourcePhone.gameObject.name;
        phone.SetActive(true);
        Debug.Log("[StageDirector] TestScene用の右手スマホカメラだけを永続XRリグへ追加しました。");
    }

    private static string GetRelativeTransformPath(Transform root, Transform target)
    {
        if (root == null || target == null || target == root) return string.Empty;

        var parts = new List<string>();
        Transform current = target;
        while (current != null && current != root)
        {
            parts.Insert(0, current.name);
            current = current.parent;
        }
        return current == root ? string.Join("/", parts.ToArray()) : string.Empty;
    }

    private static void FreezeAnimators(GameObject root)
    {
        if (root == null) return;
        foreach (Animator animator in root.GetComponentsInChildren<Animator>(true))
        {
            animator.speed = 0f;
        }
    }

    void Update()
    {
        PauseAnimatorsBeforeScheduledMusicStart();
        ResumeAnimatorsAtScheduledMusicStart();
        KeepPerformanceLockedToAudioClock();

        foreach (var so in screenOverlays)
        {
            so.intensity = overlayIntensity;
            so.enabled = overlayIntensity > 0.01f;
        }

        KeyEvent();
    }

    void KeyEvent()
    {

        if(Input.GetKey(KeyCode.Escape))
        {
        #if UNITY_EDITOR
                    UnityEditor.EditorApplication.isPlaying = false;
        #else
            Application.Quit();
        #endif
        }else if(Input.GetKeyDown(KeyCode.L) && objectsOnTimeline != null && objectsOnTimeline.Length > 0 && objectsOnTimeline[0] != null)
        {
            UnityChan.IKLookAt ikla;
            ikla = objectsOnTimeline[0].GetComponent<UnityChan.IKLookAt>();
            if(ikla != null)
            {
                if (ikla.clampWeight == 1)
                    ikla.clampWeight = lookatIntensity;
                else
                    ikla.clampWeight = 1.0f;
            }
        }

    }

    public void StartMusic()
    {
        if (musicStartHandled)
        {
            Debug.LogWarning("[PerformanceSync] 重複したStartMusicイベントを無視しました。");
            return;
        }
        musicStartHandled = true;

        performanceAudioSources.Clear();
        performanceAudioSources.AddRange(
            musicPlayer.GetComponentsInChildren<AudioSource>(true));
        masterAudioSource = FindMasterAudioSource();

        if (performanceAudioSources.Count == 0)
        {
            Debug.LogError("[PerformanceSync] MusicPlayerにAudioSourceがありません。");
            return;
        }

        if (!synchronizePerformanceToMusic)
        {
            foreach (AudioSource source in performanceAudioSources)
            {
                if (source != null) source.Play();
            }
            return;
        }

        CachePerformanceAnimators();
        float schedulingLead = Mathf.Max(0.02f, scheduledStartLeadTime);
        float performanceAdvance = Mathf.Max(0f, performanceLeadTime);
        scheduledMusicDspTime = AudioSettings.dspTime + schedulingLead + performanceAdvance;
        scheduledAnimatorPauseDspTime = scheduledMusicDspTime - schedulingLead;

        foreach (AudioSource source in performanceAudioSources)
        {
            if (source == null || source.clip == null) continue;

            source.Stop();
            source.timeSamples = 0;
            source.PlayScheduled(scheduledMusicDspTime);
        }

        // 元から含まれる約2秒のプリロールにInspector指定分を加える。
        // 指定時間だけ通常再生した後、DSP予約再生までの短い区間だけ停止する。
        waitingForAnimatorPause = performanceAdvance > 0f;
        if (!waitingForAnimatorPause)
        {
            PauseSynchronizedAnimators();
        }
        waitingForScheduledMusicStart = true;

        Debug.Log(
            $"[PerformanceSync] 音楽をDSP時刻 {scheduledMusicDspTime:F3} に予約。" +
            $" PerformanceLead={performanceAdvance:F3}秒 Animators={performanceAnimators.Count}");
    }

    void PauseAnimatorsBeforeScheduledMusicStart()
    {
        if (!waitingForAnimatorPause || AudioSettings.dspTime < scheduledAnimatorPauseDspTime)
        {
            return;
        }

        waitingForAnimatorPause = false;
        PauseSynchronizedAnimators();
    }

    void PauseSynchronizedAnimators()
    {
        if (directorAnimator != null) directorAnimator.speed = 0f;
        SetPerformanceAnimatorSpeed(0f);
    }

    void CachePerformanceAnimators()
    {
        performanceAnimators.Clear();

        if (objectsOnTimeline == null) return;
        foreach (GameObject timelineObject in objectsOnTimeline)
        {
            if (timelineObject == null) continue;

            foreach (Animator animator in timelineObject.GetComponentsInChildren<Animator>(true))
            {
                if (animator != null && !performanceAnimators.Contains(animator))
                {
                    performanceAnimators.Add(animator);
                }
            }
        }
    }

    void ResumeAnimatorsAtScheduledMusicStart()
    {
        if (!waitingForScheduledMusicStart || AudioSettings.dspTime < scheduledMusicDspTime)
        {
            return;
        }

        // 時刻の強制シークやTransform操作は行わない。全Animatorを先頭から一度だけ再開する。
        if (directorAnimator != null) directorAnimator.speed = 1f;
        SetPerformanceAnimatorSpeed(1f);
        waitingForScheduledMusicStart = false;
        performanceIsRunning = true;
        previousSyncDspTime = AudioSettings.dspTime;
        animationElapsedTime = 0.0;
        Debug.Log("[PerformanceSync] 音楽・ダンス・LipSyncを同時に開始しました。");
    }

    void KeepPerformanceLockedToAudioClock()
    {
        if (!performanceIsRunning || masterAudioSource == null || masterAudioSource.clip == null)
        {
            return;
        }

        double now = AudioSettings.dspTime;
        double dspDelta = now - previousSyncDspTime;
        previousSyncDspTime = now;

        if (dspDelta < 0.0 || dspDelta > 0.5)
        {
            // Editor停止や一時停止からの復帰時に大きな補正を入れない。
            animationElapsedTime = GetMasterAudioTime();
            SetPerformanceAnimatorSpeed(1f);
            return;
        }

        float audioTime = GetMasterAudioTime();
        double clockError = audioTime - animationElapsedTime;
        float correction = Mathf.Clamp(
            (float)clockError * playbackSpeedCorrectionGain,
            -maximumPlaybackSpeedCorrection,
            maximumPlaybackSpeedCorrection);
        float playbackSpeed = 1f + correction;

        SetPerformanceAnimatorSpeed(playbackSpeed);
        animationElapsedTime += dspDelta * playbackSpeed;
    }

    float GetMasterAudioTime()
    {
        if (masterAudioSource == null || masterAudioSource.clip == null)
        {
            return 0f;
        }

        int frequency = masterAudioSource.clip.frequency;
        return frequency > 0
            ? (float)masterAudioSource.timeSamples / frequency
            : masterAudioSource.time;
    }

    AudioSource FindMasterAudioSource()
    {
        AudioSource fallback = null;
        foreach (AudioSource source in performanceAudioSources)
        {
            if (source == null || source.clip == null) continue;
            if (fallback == null) fallback = source;

            // MusicPlayerでは可聴音源のGameObject名がMain。スペクトラム解析用の無音音源を避ける。
            if (source.gameObject.name == "Main" || source.volume > 0f)
            {
                return source;
            }
        }

        return fallback;
    }

    void SetPerformanceAnimatorSpeed(float speed)
    {
        foreach (Animator animator in performanceAnimators)
        {
            if (animator != null) animator.speed = speed;
        }
    }

    public void ActivateProps()
    {
        foreach (var o in objectsNeedsActivation) o.BroadcastMessage("ActivateProps");
    }

    public void SwitchCamera(int index)
    {
        if (mainCameraSwitcher)
            mainCameraSwitcher.ChangePosition(cameraPoints[index], true);
    }

    public void StartAutoCameraChange()
    {
        if (mainCameraSwitcher)
            mainCameraSwitcher.StartAutoChange();
    }

    public void StopAutoCameraChange()
    {
        if (mainCameraSwitcher)
            mainCameraSwitcher.StopAutoChange();
    }

    public void FastForward(float second)
    {
        if (!ignoreFastForward)
        {
            FastForwardAnimator(GetComponent<Animator>(), second, 0);
            foreach (var go in objectsOnTimeline)
                foreach (var animator in go.GetComponentsInChildren<Animator>())
                    FastForwardAnimator(animator, second, 0.5f);
        }
    }

    void FastForwardAnimator(Animator animator, float second, float crossfade)
    {
        for (var layer = 0; layer < animator.layerCount; layer++)
        {
            var info = animator.GetCurrentAnimatorStateInfo(layer);
            if (crossfade > 0.0f)
                animator.CrossFade(info.fullPathHash, crossfade / info.length, layer, info.normalizedTime + second / info.length);
            else
                animator.Play(info.fullPathHash, layer, info.normalizedTime + second / info.length);
        }
    }

    public void EndPerformance()
    {
        VRScreenFader.Instance.LoadSceneWithFade("VRPhotoResultTest", 1.0f);
    }
}
