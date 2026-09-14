using UnityEditor;
using UnityEngine;

namespace PVCapture.Editor
{
    [InitializeOnLoad]
    public static class PVCaptureEditorLifecycle
    {
        static PVCaptureEditorLifecycle()
        {
            EditorApplication.focusChanged += focused =>
            {
                if (focused || !EditorApplication.isPlaying) return;
                Object.FindAnyObjectByType<PVCaptureController>()?.Pause();
            };
        }
    }
}
