using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;

namespace PVCapture
{
    [DefaultExecutionOrder(-9000)]
    [RequireComponent(typeof(PVAnimationController), typeof(PVStageEffectsController))]
    public sealed class PVCaptureController : MonoBehaviour
    {
        public enum PlaybackState { Preparing, Ready, Playing, Paused, Finished, Error }
        [Header("Existing shared assets (do not apply instance overrides to these assets)")]
        public GameObject musicPrefab;
        public GameObject[] activationPrefabs;
        public GameObject[] performancePrefabs;
        public GameObject[] miscellaneousPrefabs;
        public GameObject environmentTemplate;
        public float characterScale = 1.1333333f;
        [Header("Timing copied from the main performance")]
        public float musicStartEventSeconds = 2.0166667f;
        public float performanceLeadSeconds = 0.23f;
        public float activatePropsSeconds = 2.5f;
        public float endPerformanceSeconds = 240.01667f;
        public bool autoPlay = true;
        public bool showUI;
        public PlaybackState State { get; private set; } = PlaybackState.Preparing;
        public string Status { get; private set; } = "Preparing";
        public double LiveSeconds { get; private set; }
        public float MusicSeconds => Mathf.Max(0, (float)LiveSeconds - MusicOffset);
        public float MusicOffset => musicStartEventSeconds + performanceLeadSeconds;
        public AudioSource MasterAudio { get; private set; }
        public GameObject LiveRoot => liveRoot;
        private GameObject liveRoot;
        private readonly List<GameObject> props = new List<GameObject>();
        private AudioSource[] audioSources = Array.Empty<AudioSource>();
        private PVAnimationController animationClock;
        private PVStageEffectsController effects;
        private double startDsp;
        private bool propsActivated, globalsOwned;
        private float previousTimeScale;
        private bool previousListenerPause;
        private Coroutine preparation;

        private void Awake()
        {
            // TimeScale/listener pause belong to the isolated studio, never an additive gameplay scene.
            if (SceneManager.sceneCount != 1 || gameObject.scene.name != "PV_Capture")
            {
                Fail("Open PV_Capture alone, outside Play Mode, then press Play.");
                enabled = false;
                return;
            }
            previousTimeScale = Time.timeScale;
            previousListenerPause = AudioListener.pause;
            globalsOwned = true;
            animationClock = GetComponent<PVAnimationController>();
            effects = GetComponent<PVStageEffectsController>();
            Time.timeScale = 0;
            AudioListener.pause = true;
        }

        private void Start()
        {
            if (globalsOwned) preparation = StartCoroutine(Prepare(autoPlay));
        }

        private IEnumerator Prepare(bool playAfter)
        {
            State = PlaybackState.Preparing;
            Status = "Loading shared live assets";
            Time.timeScale = 0;
            AudioListener.pause = true;
            // AfterSceneLoad bootstraps have now run; remove their runtime instances, not source files.
            foreach (var menu in FindObjectsByType<VRPauseMenu>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                menu.gameObject.SetActive(false);
                Destroy(menu.gameObject);
            }
            yield return null;
            effects.Release();
            animationClock.Release();
            if (liveRoot != null)
            {
                liveRoot.SetActive(false);
                Destroy(liveRoot);
                yield return null;
            }
            props.Clear();
            LiveSeconds = 0;
            propsActivated = false;
            MasterAudio = null;
            liveRoot = new GameObject("PV Live (runtime)");
            liveRoot.SetActive(false);
            var animators = new List<Animator>();
            if (environmentTemplate != null)
            {
                var environment = Instantiate(environmentTemplate, liveRoot.transform, false);
                environment.name = "Environment";
                environment.SetActive(true);
            }
            // Synchronous Instantiate preserves Unity 6.5 AudioResource references.
            var music = Spawn(musicPrefab);
            if (music == null) { Fail("MusicPlayer prefab is missing."); yield break; }
            audioSources = music.GetComponentsInChildren<AudioSource>(true);
            foreach (var source in audioSources)
            {
                source.playOnAwake = false;
                source.ignoreListenerPause = false;
                source.pitch = 1;
                if (source.clip == null) { Fail("Missing audio resource: " + source.name); yield break; }
                source.clip.LoadAudioData();
                if (MasterAudio == null || source.name == "Main") MasterAudio = source;
            }
            if (MasterAudio == null) { Fail("MusicPlayer has no AudioSource."); yield break; }
            foreach (var prefab in activationPrefabs) if (prefab != null) props.Add(Spawn(prefab));
            foreach (var prefab in performancePrefabs)
            {
                var instance = Spawn(prefab);
                if (instance == null) continue;
                if (instance.GetComponentInChildren<UnityChan.FaceUpdate>(true) != null)
                    instance.transform.localScale *= characterScale;
                animators.AddRange(instance.GetComponentsInChildren<Animator>(true));
            }
            foreach (var prefab in miscellaneousPrefabs) Spawn(prefab);
            foreach (var input in liveRoot.GetComponentsInChildren<UnityChan.IdleChanger>(true)) input.enabled = false;
            foreach (var input in liveRoot.GetComponentsInChildren<UnityChan.FaceUpdate>(true))
            {
                input.enabled = false;
                input.gameObject.AddComponent<PVFaceAnimationEvents>();
                Destroy(input);
            }
            foreach (var input in liveRoot.GetComponentsInChildren<UnityChan.IKLookAt>(true)) input.enabled = false;
            foreach (var input in liveRoot.GetComponentsInChildren<UnityChan.DeltaLookAtAxis>(true)) input.enabled = false;
            foreach (var layout in liveRoot.GetComponentsInChildren<AudienceCircleFacing>(true)) layout.enabled = false;
            foreach (var listener in liveRoot.GetComponentsInChildren<AudioListener>(true)) listener.enabled = false;
            foreach (var camera in liveRoot.GetComponentsInChildren<Camera>(true))
            {
                camera.GetUniversalAdditionalCameraData().allowXRRendering = false;
                // Preserve only offscreen stage-screen cameras, never a second display camera.
                if (camera.targetTexture == null) camera.enabled = false;
            }
            // Let removed input/event receivers be destroyed before the shared clips can fire events.
            yield return null;
            effects.Prepare(liveRoot);
            liveRoot.SetActive(true);
            animationClock.Bind(animators);
            // Existing Start methods (LipSync target lookup, springs, Reaktion links) must run before pausing.
            yield return null;
            double deadline = Time.realtimeSinceStartupAsDouble + 30;
            while (Array.Exists(audioSources, a => a.clip.loadState == AudioDataLoadState.Loading))
            {
                if (Time.realtimeSinceStartupAsDouble > deadline) { Fail("Audio loading timed out."); yield break; }
                yield return null;
            }
            if (Array.Exists(audioSources, a => a.clip.loadState != AudioDataLoadState.Loaded))
            { Fail("An audio resource could not be loaded."); yield break; }
            foreach (var canvas in liveRoot.GetComponentsInChildren<Canvas>(true)) canvas.enabled = showUI;
            effects.UpdateAudience(0);
            effects.Suspend(liveRoot);
            State = PlaybackState.Ready;
            Status = "Ready — Play starts the full performance";
            preparation = null;
            if (playAfter) Play();
        }

        private GameObject Spawn(GameObject prefab)
        {
            if (prefab == null) return null;
            var instance = Instantiate(prefab, liveRoot.transform, false);
            instance.name = prefab.name;
            return instance;
        }

        public void Play()
        {
            if (State == PlaybackState.Paused) { Resume(); return; }
            if (State != PlaybackState.Ready) return;
            if (Time.captureDeltaTime > 0)
            {
                Status = "Disable fixed simulation / use variable-frame-rate recording before Play.";
                return;
            }
            startDsp = AudioSettings.dspTime;
            foreach (var source in audioSources)
            {
                source.Stop();
                source.timeSamples = 0;
                source.PlayScheduled(startDsp + MusicOffset);
            }
            effects.Resume();
            State = PlaybackState.Playing;
            Status = "Playing (1x, synchronized)";
            Time.timeScale = 1;
            AudioListener.pause = false;
        }

        private void Update()
        {
            if (State != PlaybackState.Playing) return;
            if (Time.captureDeltaTime > 0)
            {
                Pause();
                Status = "Paused: fixed simulation would desynchronize live effects. Disable it, then Resume.";
                return;
            }
            AdvanceLiveClock();
            if (LiveSeconds >= endPerformanceSeconds)
            {
                Pause();
                State = PlaybackState.Finished;
                Status = "Finished — Reset to record another take";
            }
        }

        private void AdvanceLiveClock()
        {
            LiveSeconds = Math.Max(LiveSeconds, AudioSettings.dspTime - startDsp);
            animationClock.AdvanceTo(LiveSeconds);
            if (!propsActivated && LiveSeconds >= activatePropsSeconds)
            {
                propsActivated = true;
                foreach (var prop in props)
                    prop.BroadcastMessage("ActivateProps", SendMessageOptions.DontRequireReceiver);
            }
            effects.UpdateAudience(MusicSeconds);
        }

        public void Pause()
        {
            if (State != PlaybackState.Playing) return;
            AudioListener.pause = true; // Also freezes DSP time and scheduled sources.
            AdvanceLiveClock();
            Time.timeScale = 0;
            effects.Suspend(liveRoot);
            State = PlaybackState.Paused;
            Status = "Paused — camera remains available";
        }

        public void Resume()
        {
            if (State != PlaybackState.Paused) return;
            if (Time.captureDeltaTime > 0)
            {
                Status = "Disable fixed simulation / use variable-frame-rate recording before Resume.";
                return;
            }
            effects.Resume();
            State = PlaybackState.Playing;
            Status = "Playing (1x, synchronized)";
            Time.timeScale = 1;
            AudioListener.pause = false;
        }

        public void ResetLive()
        {
            if (!globalsOwned || State == PlaybackState.Preparing) return;
            if (preparation != null) StopCoroutine(preparation);
            foreach (var source in audioSources) if (source != null) source.Stop();
            preparation = StartCoroutine(Prepare(false));
        }

        public void SetShowUI(bool value)
        {
            showUI = value;
            if (liveRoot != null)
                foreach (var canvas in liveRoot.GetComponentsInChildren<Canvas>(true)) canvas.enabled = value;
        }

        private void Fail(string message)
        {
            State = PlaybackState.Error;
            Status = message;
            Debug.LogError("[PV Capture] " + message, this);
        }

        private void OnApplicationFocus(bool focus)
        {
            // In Editor, this callback also fires when selecting the Inspector. The Editor-only
            // lifecycle hook handles true application focus loss without interrupting camera setup.
#if !UNITY_EDITOR
            if (!focus && State == PlaybackState.Playing) Pause();
#endif
        }

        private void OnDestroy()
        {
            if (globalsOwned)
            {
                Time.timeScale = previousTimeScale;
                AudioListener.pause = previousListenerPause;
            }
        }
    }
}
