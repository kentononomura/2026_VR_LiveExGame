using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace ReactionTimelineLab
{
    public sealed class ReactionTimelineLaboratory : MonoBehaviour
    {
        public GameObject characterPrefab;
        public AnimationClip danceClip;
        public AudioClip song;
        public AnimationClip[] reactions;
        public AvatarMask upperBodyMask;
        [Tooltip("Dance clip time at song time zero. Existing live: 2.016667 + 0.23 seconds.")]
        public float danceOffset = 2.246667f;
        [Min(0)] public float blendIn = 0.3f;
        [Min(0)] public float blendOut = 0.7f;
        [Min(0.1f)] public float reactionSpeed = 1;
        public bool fullBody = true;
        public bool quantizeToBeat;
        [Min(1)] public float bpm = 120;
        public float beatOffset;

        public double SongTime { get; private set; }
        public float ReactionWeight { get; private set; }
        public double DanceTime => testPose == null ? 0 : testPose.DanceTime;
        public bool Playing { get; private set; }
        private ReactionTimelinePose referencePose, testPose;
        private AvatarMask fullMask;
        private AudioSource audioSource;
        private double startDsp, startSong, reactionStart = double.NaN, reactionDuration;
        private float activeSpeed, activeIn, activeOut;
        private bool activeFullBody;
        private int selected;
        private bool ready;
        private string status = "Loading audio...";
        private double Length => song == null ? 0 : song.length;

        private IEnumerator Start()
        {
            if (SceneManager.sceneCount != 1 || gameObject.scene.name != "ReactionTimeline_Test")
            { status = "Open ReactionTimeline_Test alone before Play."; yield break; }
            if (!characterPrefab || !danceClip || !song || !upperBodyMask || reactions == null ||
                reactions.Length == 0 || Array.Exists(reactions, c => c == null))
            { status = "Missing laboratory asset references."; Debug.LogError(status, this); yield break; }
            // The game's global bootstrap also creates a menu in test scenes. Remove only its runtime instance.
            foreach (var menu in FindObjectsByType<VRPauseMenu>(FindObjectsInactive.Include))
            { menu.gameObject.SetActive(false); Destroy(menu.gameObject); }
            fullMask = new AvatarMask();
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
                fullMask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, true);
            // Camera faces -Z, so positive world X appears on screen left.
            referencePose = CreateCharacter("Dance reference", new Vector3(1.15f, 0, 0));
            testPose = CreateCharacter("Reaction comparison", new Vector3(-1.15f, 0, 0));
            CreateStage();
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.clip = song;
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0;
            audioSource.volume = 0.3f;
            song.LoadAudioData();
            double deadline = Time.realtimeSinceStartupAsDouble + 30;
            while (song.loadState == AudioDataLoadState.Loading && Time.realtimeSinceStartupAsDouble < deadline)
                yield return null;
            if (song.loadState != AudioDataLoadState.Loaded)
            { status = "Audio could not be loaded."; yield break; }
            ready = true;
            status = "Ready. Left: dance only / Right: reaction. Press Play below.";
            Sample();
        }

        private ReactionTimelinePose CreateCharacter(string label, Vector3 position)
        {
            var holder = new GameObject(label);
            holder.transform.SetParent(transform, false);
            holder.SetActive(false);
            var instance = Instantiate(characterPrefab, holder.transform);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            // No controller, gaze, lip-sync or input scripts may compete with the sampled pose.
            foreach (var script in instance.GetComponentsInChildren<MonoBehaviour>(true)) script.enabled = false;
            foreach (var source in instance.GetComponentsInChildren<AudioSource>(true)) source.enabled = false;
            var animator = instance.GetComponentInChildren<Animator>(true);
            holder.transform.position = position;
            holder.SetActive(true);
            return new ReactionTimelinePose(animator, danceClip);
        }

        private void CreateStage()
        {
            var cameraObject = new GameObject("Laboratory Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.transform.SetParent(transform);
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>();
            camera.transform.position = new Vector3(0, 1.4f, 5.5f);
            camera.transform.LookAt(new Vector3(0, 0.9f, 0));
            camera.fieldOfView = 36;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.1f, 0.15f);
            camera.GetUniversalAdditionalCameraData().allowXRRendering = false;
            var light = new GameObject("Laboratory Light", typeof(Light));
            light.transform.SetParent(transform);
            light.transform.rotation = Quaternion.Euler(45, -30, 0);
            light.GetComponent<Light>().type = LightType.Directional;
            light.GetComponent<Light>().intensity = 1.2f;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Ground reference";
            floor.transform.SetParent(transform);
            floor.transform.localScale = Vector3.one * 0.6f;
        }

        private void Update()
        {
            if (!ready) return;
            if (Playing)
            {
                SongTime = Math.Min(Length, startSong + Math.Max(0, AudioSettings.dspTime - startDsp));
                if (SongTime >= Length) { Playing = false; audioSource.Stop(); }
            }
            Sample();
        }

        private void Sample()
        {
            if (testPose == null) return;
            double elapsed = double.IsNaN(reactionStart) ? -1 : SongTime - reactionStart;
            ReactionWeight = ReactionTimelinePose.Weight(elapsed, reactionDuration, activeIn, activeOut);
            referencePose.Sample(SongTime, danceOffset, 0, 0);
            testPose.Sample(SongTime, danceOffset,
                Math.Min(Math.Max(0, elapsed * activeSpeed), Math.Max(0, reactionDuration * activeSpeed - 0.0001)),
                ReactionWeight);
        }

        public void Play()
        {
            if (!ready || Playing) return;
            if (SongTime >= Length) Seek(0);
            startSong = SongTime;
            startDsp = AudioSettings.dspTime + 0.1;
            audioSource.timeSamples = Math.Min(song.samples - 1, (int)(SongTime * song.frequency));
            audioSource.PlayScheduled(startDsp);
            Playing = true;
        }

        public void Pause()
        {
            if (!ready) return;
            if (Playing) SongTime = Math.Min(Length, startSong + Math.Max(0, AudioSettings.dspTime - startDsp));
            Playing = false;
            audioSource.Stop();
            Sample();
        }

        public void Seek(double seconds)
        {
            if (!ready) return;
            bool resume = Playing;
            Pause();
            SongTime = Math.Max(0, Math.Min(Length, seconds));
            Sample();
            if (resume && SongTime < Length) Play();
        }

        public void TriggerReaction()
        {
            if (!ready) return;
            // Preserve the active pose on repeated commands: no abrupt replacement during a reaction.
            if (!double.IsNaN(reactionStart) && SongTime < reactionStart + reactionDuration)
            { status = "Reaction already pending/playing. Clear or wait before another command."; return; }
            activeSpeed = Mathf.Max(0.1f, reactionSpeed);
            activeIn = Mathf.Max(0, blendIn);
            activeOut = Mathf.Max(0, blendOut);
            activeFullBody = fullBody;
            reactionDuration = reactions[selected].length / activeSpeed;
            reactionStart = SongTime;
            if (quantizeToBeat)
            {
                double beat = 60 / Math.Max(1, bpm);
                reactionStart = Math.Max(SongTime, Math.Ceiling((SongTime - beatOffset) / beat) * beat + beatOffset);
            }
            testPose.SetReaction(reactions[selected], activeFullBody ? fullMask : upperBodyMask);
            status = $"Reaction at {reactionStart:F2}s for {reactionDuration:F2}s. Settings are captured at trigger.";
            Sample();
        }

        private void OnApplicationPause(bool paused) { if (paused) Pause(); }
        private void OnDisable() { if (ready) Pause(); }
        private void OnDestroy()
        {
            referencePose?.Dispose();
            testPose?.Dispose();
            if (fullMask) Destroy(fullMask);
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(12, 12, Mathf.Min(650, Screen.width - 24), 350), GUI.skin.box);
            GUILayout.Label("REACTION TIMELINE LAB  |  Left: dance / Right: reaction");
            GUILayout.Label(status);
            if (ready)
            {
                GUILayout.Label($"Song {SongTime:F2}s   Dance {DanceTime:F2}s   Reaction {ReactionWeight:P0}");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(Playing ? "Pause" : "Play")) { if (Playing) Pause(); else Play(); }
                if (GUILayout.Button("-1 frame")) { Pause(); Seek(SongTime - 1.0 / 60); }
                if (GUILayout.Button("+1 frame")) { Pause(); Seek(SongTime + 1.0 / 60); }
                if (GUILayout.Button("Reset")) { Pause(); reactionStart = double.NaN; Seek(0); }
                GUILayout.EndHorizontal();
                float seek = GUILayout.HorizontalSlider((float)SongTime, 0, (float)Length);
                if (Math.Abs(seek - SongTime) > 0.025) Seek(seek);
                if (!double.IsNaN(reactionStart))
                {
                    float from = (float)Math.Max(0, reactionStart - 1);
                    float to = (float)Math.Min(Length, reactionStart + reactionDuration + 1);
                    GUILayout.Label($"Transition zoom: {from:F2} - {to:F2}s (drag to pause and inspect)");
                    float local = GUILayout.HorizontalSlider(Mathf.Clamp((float)SongTime, from, to), from, to);
                    if (Math.Abs(local - Mathf.Clamp((float)SongTime, from, to)) > 0.002)
                    { Pause(); Seek(local); }
                }
                GUILayout.BeginHorizontal();
                for (int i = 0; i < reactions.Length; i++)
                    if (GUILayout.Toggle(selected == i, reactions[i].name, GUI.skin.button)) selected = i;
                GUILayout.EndHorizontal();
                fullBody = GUILayout.Toggle(fullBody, "Full body (off = upper body mask)");
                blendIn = Slider("Blend in", blendIn, 0.01f, 1.5f);
                blendOut = Slider("Blend out", blendOut, 0.01f, 1.5f);
                reactionSpeed = Slider("Reaction speed", reactionSpeed, 0.25f, 2);
                GUILayout.BeginHorizontal();
                quantizeToBeat = GUILayout.Toggle(quantizeToBeat, "Next beat (BPM / offset: Inspector)");
                if (GUILayout.Button("Trigger")) TriggerReaction();
                if (GUILayout.Button("Replay transition") && !double.IsNaN(reactionStart))
                { Seek(Math.Max(0, reactionStart - 1)); Play(); }
                if (GUILayout.Button("Clear")) { reactionStart = double.NaN; Sample(); }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndArea();
        }

        private static float Slider(string label, float value, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: {value:F2}", GUILayout.Width(145));
            value = GUILayout.HorizontalSlider(value, min, max);
            GUILayout.EndHorizontal();
            return value;
        }
    }
}
