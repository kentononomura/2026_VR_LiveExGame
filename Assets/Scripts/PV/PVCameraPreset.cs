using System;
using UnityEngine;

namespace PVCapture
{
    [CreateAssetMenu(menuName = "PV Capture/Camera Presets")]
    public sealed class PVCameraPreset : ScriptableObject
    {
        [Serializable]
        public sealed class View
        {
            public string label;
            public bool saved;
            public Vector3 position;
            public Quaternion rotation = Quaternion.identity;
            [Range(10, 100)] public float fieldOfView = 50;
        }

        public View[] views = new View[10];

        public void Save(int index, Camera camera)
        {
            if (camera == null || index < 0 || index >= views.Length) return;
            var view = views[index] ?? (views[index] = new View());
            if (string.IsNullOrEmpty(view.label)) view.label = "Preset " + (index + 1);
            view.saved = true;
            view.position = camera.transform.position;
            view.rotation = camera.transform.rotation;
            view.fieldOfView = camera.fieldOfView;
        }
    }
}
