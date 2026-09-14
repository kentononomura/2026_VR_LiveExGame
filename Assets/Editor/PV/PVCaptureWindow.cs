using UnityEditor;
using UnityEngine;

namespace PVCapture.Editor
{
    public sealed class PVCaptureWindow : EditorWindow
    {
        private Vector2 scroll;
        [MenuItem("Tools/PV Capture/Open Controls")]
        public static void Open()
        {
            // A utility window remains reachable even when Game view uses Play Maximized.
            foreach (var existing in Resources.FindObjectsOfTypeAll<PVCaptureWindow>()) existing.Close();
            var window = CreateInstance<PVCaptureWindow>();
            window.titleContent = new GUIContent("PV Capture");
            window.minSize = new Vector2(530, 630);
            window.ShowUtility();
        }
        private void OnInspectorUpdate() { Repaint(); }
        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            var control = Object.FindAnyObjectByType<PVCaptureController>();
            var free = Object.FindAnyObjectByType<PVFreeCamera>();
            if (control == null || free == null)
            {
                EditorGUILayout.HelpBox("Open PV_Capture alone, then enter Play Mode. This window is never included in the recorded image.", MessageType.Info);
                if (GUILayout.Button("Open PV_Capture")) PVCaptureSceneBuilder.Open();
                EditorGUILayout.EndScrollView();
                return;
            }
            DrawPlayback(control);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Free Camera", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Right-click Game view to capture mouse. WASD: move, Q/E: down/up, Shift: fast, wheel: speed, ESC: release. Camera works while paused.", MessageType.None);
            Camera camera = free.CaptureCamera;
            EditorGUI.BeginChangeCheck();
            float fov = EditorGUILayout.Slider("FOV", camera.fieldOfView, 10, 100);
            float speed = EditorGUILayout.Slider("Move speed", free.moveSpeed, 0.02f, 20);
            float rotation = EditorGUILayout.Slider("Rotation sensitivity", free.rotationSpeed, 0.01f, 1);
            float smoothing = EditorGUILayout.Slider("Smoothing", free.smoothing, 0, 30);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObjects(new Object[] { free, camera }, "PV camera settings");
                camera.fieldOfView = fov; free.moveSpeed = speed; free.rotationSpeed = rotation; free.smoothing = smoothing;
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Persistent camera presets", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Save writes only the PV preset asset, including during Play Mode. Recall restores position, rotation and FOV. Initial angles are starting points; adjust to your shot.", MessageType.None);
            var presets = free.presets;
            if (presets != null)
            {
                for (int i = 0; i < presets.views.Length; i++)
                {
                    var view = presets.views[i];
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label((i + 1).ToString(), GUILayout.Width(20));
                    string label = EditorGUILayout.TextField(view?.label ?? "Empty");
                    if (view != null && label != view.label)
                    {
                        Undo.RecordObject(presets, "Rename PV preset"); view.label = label;
                        EditorUtility.SetDirty(presets); AssetDatabase.SaveAssetIfDirty(presets);
                    }
                    if (GUILayout.Button("Save", GUILayout.Width(48))) SavePreset(free, i);
                    using (new EditorGUI.DisabledScope(view == null || !view.saved))
                        if (GUILayout.Button("Recall", GUILayout.Width(52)))
                        {
                            Undo.RecordObjects(new Object[] { free.transform, camera }, "Recall PV preset");
                            free.Recall(i);
                        }
                    EditorGUILayout.EndHorizontal();
                }
            }
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("Recorder Window: Movie / Game View, 1920×1080. Frame Rate > Playback MUST be Variable; Max FPS: 30 or 60. Turning off Cap FPS while Playback is Constant does not help. If the live stopped: Stop Recording, select Variable, Reset, Play, then start a new recording. No independent seek or slow playback is available.", MessageType.Info);
            EditorGUILayout.EndScrollView();
        }

        public static void SavePreset(PVFreeCamera free, int index)
        {
            Undo.RecordObject(free.presets, "Save PV camera preset");
            free.presets.Save(index, free.CaptureCamera);
            EditorUtility.SetDirty(free.presets);
            AssetDatabase.SaveAssetIfDirty(free.presets);
        }

        internal static void DrawPlayback(PVCaptureController control)
        {
            EditorGUILayout.LabelField("Synchronized live playback", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(EditorApplication.isPlaying ? control.Status : "Enter Play Mode to load the live assets.");
            if (EditorApplication.isPlaying && Time.captureDeltaTime > 0)
                EditorGUILayout.HelpBox("Recorder is forcing fixed simulation time. Stop Recording and change Frame Rate > Playback from Constant to Variable, then Reset and Play. Cap FPS is a separate setting.", MessageType.Warning);
            EditorGUILayout.LabelField($"Live: {control.LiveSeconds:F2}s   Music: {control.MusicSeconds:F2}s");
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying || control.State != PVCaptureController.PlaybackState.Ready))
                if (GUILayout.Button("Play")) control.Play();
            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying || control.State != PVCaptureController.PlaybackState.Playing))
                if (GUILayout.Button("Pause")) control.Pause();
            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying || control.State != PVCaptureController.PlaybackState.Paused))
                if (GUILayout.Button("Resume")) control.Resume();
            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying || control.State == PVCaptureController.PlaybackState.Preparing))
                if (GUILayout.Button("Reset")) control.ResetLive();
            EditorGUILayout.EndHorizontal();
            bool show = EditorGUILayout.Toggle("Show capture UI", control.showUI);
            if (show != control.showUI) control.SetShowUI(show);
        }
    }

    [CustomEditor(typeof(PVCaptureController))]
    public sealed class PVCaptureControllerInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            PVCaptureWindow.DrawPlayback((PVCaptureController)target);
            if (GUILayout.Button("Open PV Capture controls / presets")) PVCaptureWindow.Open();
            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying)) DrawDefaultInspector();
        }
    }
}
