using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace PVCapture.Editor
{
    public static class PVCaptureSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/PV_Capture.unity";
        public const string PresetPath = "Assets/Scripts/PV/PVCameraPresets.asset";

        [MenuItem("Tools/PV Capture/Create Capture Scene")]
        public static void Create()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before creating the studio.");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
                throw new InvalidOperationException("PV_Capture already exists; it will not be overwritten.");
            var previous = SceneManager.GetActiveScene();
            // Copy only the scene layout/settings. All meshes, materials, controllers and prefab assets stay shared.
            if (!AssetDatabase.CopyAsset("Assets/Scenes/TestScene.unity", ScenePath))
                throw new InvalidOperationException("Could not create the capture scene.");
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                var roots = scene.GetRootGameObjects();
                var source = roots.SelectMany(r => r.GetComponentsInChildren<StageDirector>(true)).Single();
                var controlObject = new GameObject("PV Capture Controls");
                var control = controlObject.AddComponent<PVCaptureController>();
                control.musicPrefab = source.musicPlayerPrefab;
                control.activationPrefabs = source.prefabsNeedsActivation;
                control.performancePrefabs = source.prefabsOnTimeline;
                control.miscellaneousPrefabs = source.miscPrefabs;
                control.characterScale = source.unityChanScaleMultiplier;
                control.performanceLeadSeconds = source.performanceLeadTime;
                var directorAnimator = source.GetComponent<Animator>();
                var clip = directorAnimator.runtimeAnimatorController.animationClips.First(c => c.name == "StageDirector");
                var events = AnimationUtility.GetAnimationEvents(clip);
                control.musicStartEventSeconds = events.First(e => e.functionName == "StartMusic").time;
                control.activatePropsSeconds = events.First(e => e.functionName == "ActivateProps").time;
                control.endPerformanceSeconds = events.First(e => e.functionName == "EndPerformance").time;
                var template = new GameObject("PV Environment Template (inactive)");
                template.SetActive(false);
                control.environmentTemplate = template;
                foreach (var root in roots)
                {
                    if (root.name == "Audience Layout" || root.name == "Directional Light" || root.name == "StageReflectionProbe")
                        root.transform.SetParent(template.transform, true);
                    else UnityEngine.Object.DestroyImmediate(root);
                }
                foreach (var layout in template.GetComponentsInChildren<AudienceCircleFacing>(true)) layout.enabled = false;
                var cameraObject = new GameObject("PV Free Camera", typeof(Camera), typeof(AudioListener), typeof(PVFreeCamera));
                var camera = cameraObject.GetComponent<Camera>();
                cameraObject.tag = "MainCamera";
                camera.transform.position = new Vector3(0, 2.2f, 8);
                camera.transform.LookAt(new Vector3(0, 1.1f, 0));
                camera.fieldOfView = 50;
                camera.nearClipPlane = 0.03f;
                camera.farClipPlane = 500;
                camera.GetUniversalAdditionalCameraData().allowXRRendering = false;
                var presets = AssetDatabase.LoadAssetAtPath<PVCameraPreset>(PresetPath);
                if (presets == null)
                {
                    presets = ScriptableObject.CreateInstance<PVCameraPreset>();
                    Vector3[] positions = {
                        new Vector3(0, 2.2f, 8), new Vector3(0, 1.35f, 3), new Vector3(0, 1.7f, 1.25f),
                        new Vector3(0.8f, 0.35f, 2.5f), new Vector3(5, 1.8f, 1), new Vector3(-5, 1.8f, 1),
                        new Vector3(3, 3.5f, 5), new Vector3(-3, 2.5f, 5), new Vector3(0, 5, 8), new Vector3(0, 1.8f, 12) };
                    string[] labels = { "Stage wide", "Front", "Face close-up", "Low angle", "Stage right", "Stage left", "High diagonal", "Left diagonal", "Overhead wide", "Audience wide" };
                    for (int i = 0; i < positions.Length; i++)
                    {
                        Vector3 target = new Vector3(0, i == 2 ? 1.65f : 1.1f, 0);
                        presets.views[i] = new PVCameraPreset.View { label = labels[i], saved = true, position = positions[i],
                            rotation = Quaternion.LookRotation(target - positions[i]), fieldOfView = i == 2 ? 40 : 50 };
                    }
                    AssetDatabase.CreateAsset(presets, PresetPath);
                }
                cameraObject.GetComponent<PVFreeCamera>().presets = presets;
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssetIfDirty(presets);
                Debug.Log("[PV Capture] Created " + ScenePath + ". Source assets were not changed.");
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [MenuItem("Tools/PV Capture/Open Capture Scene")]
        public static void Open()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            PVCaptureWindow.Open();
        }
    }
}
