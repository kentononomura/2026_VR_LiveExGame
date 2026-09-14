using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PVCapture.Editor
{
    /// <summary>Opt-in integration check; output stays in Temp/PVCapture. Does not save gameplay scenes.</summary>
    [InitializeOnLoad]
    public static class PVCaptureValidation
    {
        private const string Folder = "Temp/PVCapture";
        private static int step;
        private static double deadline, frozenTime;
        private static int frozenSample;
        private static GameObject firstRoot;
        private static Vector3 cameraPosition;
        private static Transform[] bones;
        private static Quaternion[] boneRotations;
        private static ParticleSystem[] particles;
        private static float[] particleTimes;
        private static bool running;

        static PVCaptureValidation()
        {
            EditorApplication.update += Update;
            Application.logMessageReceived += (message, stack, type) =>
            {
                if (!EditorApplication.isPlaying || UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "PV_Capture") return;
                if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
                Directory.CreateDirectory(Folder);
                File.AppendAllText(Folder + "/runtime-errors.txt", message + "\n" + stack + "\n");
            };
        }

        [MenuItem("Tools/PV Capture/Validate Playback (Play Mode)")]
        public static void Begin()
        {
            var control = UnityEngine.Object.FindAnyObjectByType<PVCaptureController>();
            if (!EditorApplication.isPlaying || control == null)
                throw new InvalidOperationException("Run PV_Capture in Play Mode first.");
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Folder + "/validation.txt", "PV integration validation\n");
            running = true; fullRun = false; step = -1; deadline = EditorApplication.timeSinceStartup + 60;
        }

        private static void Update()
        {
            // Local opt-in batch entry points for repeatable Editor validation, not arbitrary code execution.
            string request = Folder + "/request.txt";
            if (File.Exists(request) && !EditorApplication.isCompiling && !EditorApplication.isUpdating)
            {
                string command;
                try { command = File.ReadAllText(request).Trim(); File.Delete(request); }
                catch (IOException) { return; } // A request may still be completing an atomic handoff.
                try
                {
                    if (command == "build") PVCaptureSceneBuilder.Create();
                    else if (command == "play")
                    {
                        if (!EditorApplication.isPlaying && !Enumerable.Range(0, UnityEngine.SceneManagement.SceneManager.sceneCount)
                            .Any(i => UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty))
                        {
                            EditorSceneManager.OpenScene(PVCaptureSceneBuilder.ScenePath);
                            EditorApplication.isPlaying = true;
                        }
                        else throw new InvalidOperationException("Play request requires stopped, clean scenes; unsaved work is preserved.");
                    }
                    else if (command == "validate") Begin();
                    else if (command == "stop") EditorApplication.isPlaying = false;
                    else if (command == "capture") Capture();
                    else if (command == "inspect") Inspect();
                    else if (command == "full-run")
                    {
                        Begin();
                        fullRun = true;
                    }
                    else if (command == "controls") PVCaptureWindow.Open();
                    else if (command == "presets") CheckPresets();
                    File.WriteAllText(Folder + "/response.txt", command + " OK");
                }
                catch (Exception exception) { File.WriteAllText(Folder + "/response.txt", exception.ToString()); }
            }
            if (!running) return;
            try { Tick(); }
            catch (Exception exception)
            {
                running = false;
                File.AppendAllText(Folder + "/validation.txt", "FAIL: " + exception + "\n");
                UnityEngine.Object.FindAnyObjectByType<PVCaptureController>()?.Pause();
            }
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying) { running = false; return; }
            var control = UnityEngine.Object.FindAnyObjectByType<PVCaptureController>();
            if (control == null) return;
            double now = EditorApplication.timeSinceStartup;
            if (now > deadline) throw new Exception("Timed out in step " + step + ": " + control.Status);
            var camera = UnityEngine.Object.FindAnyObjectByType<PVFreeCamera>();
            if (control.State == PVCaptureController.PlaybackState.Error) throw new Exception(control.Status);
            if (step == -1 && control.State != PVCaptureController.PlaybackState.Preparing)
            {
                control.ResetLive(); step = 0;
            }
            else if (step == 0 && control.State == PVCaptureController.PlaybackState.Ready)
            {
                control.Play(); step = 1;
            }
            else if (step == 1 && control.LiveSeconds > 0.4)
            {
                control.Pause(); frozenTime = control.LiveSeconds; step = 2; deadline = now + 20;
                File.AppendAllText(Folder + "/validation.txt", "Paused during audio preroll\n");
            }
            else if (step == 2 && now > deadline - 18)
            {
                Require(Math.Abs(control.LiveSeconds - frozenTime) < 0.0001, "Preroll clock moved during Pause");
                control.Resume(); step = 3; deadline = now + 30;
            }
            else if (step == 3 && control.MusicSeconds > 5)
            {
                Require(UnityEngine.Object.FindObjectsByType<AudioListener>().Count(a => a.enabled) == 1, "Expected exactly one audio listener");
                Require(UnityEngine.Object.FindAnyObjectByType<StageDirector>() == null, "Gameplay director must be absent");
                Require(UnityEngine.Object.FindAnyObjectByType<VRPauseMenu>() == null, "VR menu must be absent");
                Require(UnityEngine.Object.FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>() == null, "XR rig must be absent");
                Require(control.GetComponent<PVAnimationController>().ControllerCount >= 2, "Dance and LipSync must both be bound");
                CheckAudio(control);
                control.Pause();
                frozenTime = control.LiveSeconds; frozenSample = control.MasterAudio.timeSamples;
                bones = control.LiveRoot.GetComponentsInChildren<Transform>();
                boneRotations = bones.Select(b => b.localRotation).ToArray();
                particles = control.LiveRoot.GetComponentsInChildren<ParticleSystem>();
                particleTimes = particles.Select(p => p.time).ToArray();
                // Reproduce Recorder restoring the pre-recording timeScale after PV Pause.
                // Particle simulation must remain frozen independently of the global clock.
                Time.timeScale = 1;
                cameraPosition = camera.transform.position;
                camera.transform.position += Vector3.right * 0.1f;
                step = 4; deadline = now + 20;
            }
            else if (step == 4 && now > deadline - 18)
            {
                Require(Math.Abs(control.LiveSeconds - frozenTime) < 0.0001, "Live clock moved during Pause");
                Require(control.MasterAudio.timeSamples == frozenSample, "Music moved during Pause");
                for (int i = 0; i < bones.Length; i++)
                    if (bones[i] != null) Require(Quaternion.Angle(bones[i].localRotation, boneRotations[i]) < 0.05f, "Transform moved during Pause: " + bones[i].name);
                for (int i = 0; i < particles.Length; i++)
                    if (particles[i] != null) Require(Mathf.Abs(particles[i].time - particleTimes[i]) < 0.001f, "Particle simulation moved during Pause");
                Require(camera.transform.position != cameraPosition, "Camera must be independent of paused performance");
                camera.transform.position = cameraPosition;
                File.AppendAllText(Folder + "/validation.txt", "PASS: music, bones, particles, clock frozen even after Recorder-style timeScale restoration; camera independent\n");
                Capture(); control.Resume(); step = 5; deadline = now + 20;
            }
            else if (step == 5 && control.LiveSeconds > frozenTime + 3)
            {
                CheckAudio(control);
                firstRoot = control.LiveRoot;
                cameraPosition = camera.transform.position;
                control.ResetLive(); step = 6; deadline = now + 45;
            }
            else if (step == 6 && control.State == PVCaptureController.PlaybackState.Ready)
            {
                Require(control.LiveSeconds == 0, "Reset did not restore live clock");
                Require(!ReferenceEquals(control.LiveRoot, firstRoot), "Reset must rebuild the full performance");
                Require(cameraPosition == camera.transform.position, "Reset changed the camera");
                Require(!control.MasterAudio.isPlaying, "Reset should wait for Play");
                File.AppendAllText(Folder + "/validation.txt", "PASS: full Reset rebuilds live assets and preserves camera\n");
                control.Play(); step = 7; deadline = now + 30;
            }
            else if (step == 7 && control.MusicSeconds > 4)
            {
                CheckAudio(control);
                File.AppendAllText(Folder + "/validation.txt", "PASS: synchronized replay after Reset\n");
                if (fullRun) { step = 8; deadline = now + 270; nextSample = 30; }
                else { control.Pause(); running = false; File.AppendAllText(Folder + "/validation.txt", "ALL CHECKS PASSED\n"); }
                Inspect();
            }
            else if (step == 8)
            {
                if (control.MusicSeconds >= nextSample && control.State == PVCaptureController.PlaybackState.Playing)
                {
                    CheckAudio(control); nextSample += 30;
                    File.AppendAllText(Folder + "/validation.txt", "Full-run music time: " + control.MusicSeconds.ToString("F2") + "s\n");
                }
                if (control.State == PVCaptureController.PlaybackState.Finished)
                {
                    Require(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "PV_Capture", "Performance changed scene");
                    File.AppendAllText(Folder + "/validation.txt", "PASS: full performance ended in studio without scene transition\nALL CHECKS PASSED\n");
                    fullRun = false; running = false; Capture();
                }
            }
        }

        private static bool fullRun;
        private static float nextSample;

        private static void CheckPresets()
        {
            var free = UnityEngine.Object.FindAnyObjectByType<PVFreeCamera>();
            Require(free != null && free.presets != null, "Preset target missing");
            var presets = free.presets;
            string backup = EditorJsonUtility.ToJson(presets);
            Vector3 position = free.transform.position;
            Quaternion rotation = free.transform.rotation;
            float fov = free.CaptureCamera.fieldOfView;
            try
            {
                free.transform.SetPositionAndRotation(new Vector3(3.21f, 2.34f, 7.65f), Quaternion.Euler(12, 171, 0));
                free.CaptureCamera.fieldOfView = 43;
                PVCaptureWindow.SavePreset(free, 9);
                string savedText = File.ReadAllText(AssetDatabase.GetAssetPath(presets));
                Require(savedText.Contains("3.21") && savedText.Contains("43"), "Preset did not persist to disk");
                free.transform.position = Vector3.zero; free.CaptureCamera.fieldOfView = 60;
                free.Recall(9);
                Require(Vector3.Distance(free.transform.position, new Vector3(3.21f, 2.34f, 7.65f)) < 0.0001f, "Preset position mismatch");
                Require(Quaternion.Angle(free.transform.rotation, Quaternion.Euler(12, 171, 0)) < 0.01f, "Preset rotation mismatch");
                Require(Mathf.Abs(free.CaptureCamera.fieldOfView - 43) < 0.001f, "Preset FOV mismatch");
                File.WriteAllText(Folder + "/presets.txt", "PASS: disk save and position/rotation/FOV recall; original preset data restored\n");
            }
            finally
            {
                EditorJsonUtility.FromJsonOverwrite(backup, presets);
                EditorUtility.SetDirty(presets); AssetDatabase.SaveAssetIfDirty(presets);
                free.transform.SetPositionAndRotation(position, rotation); free.CaptureCamera.fieldOfView = fov; free.ResetMotion();
            }
        }

        private static void CheckAudio(PVCaptureController control)
        {
            double maxError = 0;
            foreach (var source in control.LiveRoot.GetComponentsInChildren<AudioSource>())
            {
                if (source.clip == null) continue;
                double error = Math.Abs((double)source.timeSamples / source.clip.frequency - control.MusicSeconds);
                maxError = Math.Max(maxError, error);
                Require(error < 0.12, "Audio clock differs by " + error + "s: " + source.name);
            }
            Require(Math.Abs(control.GetComponent<PVAnimationController>().EvaluatedSeconds - control.LiveSeconds) < 0.001, "Performance clocks differ");
            File.AppendAllText(Folder + "/validation.txt", "PASS: shared performance clock; max audio sample error " + maxError.ToString("F4") + "s\n");
        }
        private static void Require(bool success, string message) { if (!success) throw new Exception(message); }

        private static void Inspect()
        {
            var control = UnityEngine.Object.FindAnyObjectByType<PVCaptureController>();
            string info = control == null ? "No PV Capture controller" : control.Status + "\nLive=" + control.LiveSeconds + " Music=" + control.MusicSeconds;
            var free = UnityEngine.Object.FindAnyObjectByType<PVFreeCamera>();
            info += "\nFocus=" + Application.isFocused + " EditorFocus=" + EditorApplication.isFocused
                + " Window=" + EditorWindow.focusedWindow?.GetType().Name
                + " Cursor=" + Cursor.lockState + " TimeScale=" + Time.timeScale
                + " Input=" + UnityEngine.InputSystem.InputSystem.settings.updateMode
                + " Keyboard=" + (UnityEngine.InputSystem.Keyboard.current != null)
                + " Mouse=" + (UnityEngine.InputSystem.Mouse.current != null);
            if (free != null) info += "\nCamera=" + free.transform.position.ToString("F5") + " Rotation=" + free.transform.eulerAngles.ToString("F3") + " Enabled=" + free.enabled;
            File.WriteAllText(Folder + "/inspection.txt", info + "\nPlaying=" + EditorApplication.isPlaying + "\n");
        }

        private static void Capture()
        {
            var free = UnityEngine.Object.FindAnyObjectByType<PVFreeCamera>();
            if (free == null) return;
            Camera camera = free.CaptureCamera;
            var previous = camera.targetTexture;
            var active = RenderTexture.active;
            var texture = new RenderTexture(1920, 1080, 24);
            var readback = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = texture; camera.Render(); RenderTexture.active = texture;
                readback.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); readback.Apply();
                File.WriteAllBytes(Folder + "/capture.png", readback.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previous; RenderTexture.active = active;
                UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(readback);
            }
        }
    }
}
