using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

public class StageDirector : MonoBehaviour
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
    double previousSyncDspTime;
    double animationElapsedTime;
    bool musicStartHandled;
    bool waitingForScheduledMusicStart;
    bool performanceIsRunning;

    void Awake()
    {
        // Instantiate the prefabs.
        musicPlayer = (GameObject)Instantiate(musicPlayerPrefab);

        var cameraRig = (GameObject)Instantiate(mainCameraRigPrefab);
        mainCameraSwitcher = cameraRig.GetComponentInChildren<CameraSwitcher>();
        screenOverlays = cameraRig.GetComponentsInChildren<ScreenOverlay>();

        objectsNeedsActivation = new GameObject[prefabsNeedsActivation.Length];
        for (var i = 0; i < prefabsNeedsActivation.Length; i++)
            objectsNeedsActivation[i] = (GameObject)Instantiate(prefabsNeedsActivation[i]);

        objectsOnTimeline = new GameObject[prefabsOnTimeline.Length];
        for (var i = 0; i < prefabsOnTimeline.Length; i++)
            objectsOnTimeline[i] = (GameObject)Instantiate(prefabsOnTimeline[i]);

        foreach (var p in miscPrefabs) Instantiate(p);

        directorAnimator = GetComponent<Animator>();
        CachePerformanceAnimators();
    }

    void Update()
    {
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
        }else if(Input.GetKeyDown(KeyCode.L))
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
        scheduledMusicDspTime = AudioSettings.dspTime +
                                Mathf.Max(0.02f, scheduledStartLeadTime);

        foreach (AudioSource source in performanceAudioSources)
        {
            if (source == null || source.clip == null) continue;

            source.Stop();
            source.timeSamples = 0;
            source.PlayScheduled(scheduledMusicDspTime);
        }

        // ダンス素材とLipSync素材には、音楽開始イベントまで約2秒のプリロールが含まれる。
        // そのプリロールは維持し、DSP予約再生までの短い待ち時間だけ全体を停止する。
        if (directorAnimator != null) directorAnimator.speed = 0f;
        SetPerformanceAnimatorSpeed(0f);
        waitingForScheduledMusicStart = true;

        Debug.Log(
            $"[PerformanceSync] 音楽をDSP時刻 {scheduledMusicDspTime:F3} に予約。" +
            $" Animators={performanceAnimators.Count}");
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
        if (VRScreenFader.Instance != null)
        {
            VRScreenFader.Instance.FadeOut(1.0f, () => SceneManager.LoadSceneAsync("VRPhotoResultTest"));
        }
        else
        {
            SceneManager.LoadSceneAsync("VRPhotoResultTest");
        }
    }
}
