using System.Collections.Generic;
using UnityEngine;

namespace PVCapture
{
    [DefaultExecutionOrder(32000)]
    public sealed class PVStageEffectsController : MonoBehaviour
    {
        private sealed class Audience
        {
            public AudienceCircleFacing layout;
            public Material material;
            public ParticleSystemRenderer renderer;
            public Material original;
        }
        private readonly List<Audience> audience = new List<Audience>();
        private readonly List<Behaviour> suspended = new List<Behaviour>();
        private Transform[] heldTransforms;
        private Vector3[] heldPositions, heldScales;
        private Quaternion[] heldRotations;
        private SkinnedMeshRenderer[] heldMeshes;
        private float[][] heldBlendShapes;

        public void Prepare(GameObject root)
        {
            foreach (var spawner in root.GetComponentsInChildren<Reaktion.Spawner>(true))
                spawner.parent = root.transform; // Runtime lasers also belong to this resettable performance.
            foreach (var particles in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = particles.main;
                main.useUnscaledTime = false;
            }
            foreach (var layout in root.GetComponentsInChildren<AudienceCircleFacing>(true))
            {
                layout.enabled = false;
                var renderer = layout.GetComponent<ParticleSystemRenderer>();
                if (renderer == null || renderer.sharedMaterial == null) continue;
                Material original = renderer.sharedMaterial;
                Material material = new Material(original);
                var shader = Resources.Load<Shader>("TestSceneAudience/MixedAudience");
                if (shader != null) material.shader = shader;
                material.EnableKeyword("_TIMEUPDATEMODE_MANUAL");
                material.SetFloat("_TimeUpdateMode", 1);
                renderer.sharedMaterial = material;
                audience.Add(new Audience { layout = layout, renderer = renderer, material = material, original = original });
            }
        }

        public void UpdateAudience(float musicSeconds)
        {
            foreach (var item in audience)
            {
                item.layout.RefreshFacing();
                var mat = item.material;
                mat.SetFloat("_ManualTime", musicSeconds * mat.GetFloat("_Speed") * item.layout.animationSpeedMultiplier);
                mat.SetFloat("_AudienceSeconds", musicSeconds);
                mat.SetFloat("_MotionInterval", item.layout.motionInterval);
                mat.SetFloat("_MotionTransition", item.layout.motionTransition);
                mat.SetFloat("_Blend1", Mathf.Clamp01(musicSeconds / 0.3f));
                for (int i = 2; i <= 4; i++) mat.SetFloat("_Blend" + i, 0);
            }
        }

        public void Suspend(GameObject root)
        {
            if (suspended.Count > 0 || root == null) return;
            // Some legacy scripts change transforms/blend shapes even with deltaTime == 0.
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (!behaviour.enabled) continue;
                suspended.Add(behaviour);
                behaviour.enabled = false;
            }
            // Animator can reapply its last base pose even with a manual graph and zero timeScale.
            // Retain the visible secondary animation / face pose until the whole live resumes.
            heldTransforms = root.GetComponentsInChildren<Transform>(true);
            heldPositions = new Vector3[heldTransforms.Length];
            heldScales = new Vector3[heldTransforms.Length];
            heldRotations = new Quaternion[heldTransforms.Length];
            for (int i = 0; i < heldTransforms.Length; i++)
            {
                heldPositions[i] = heldTransforms[i].localPosition;
                heldRotations[i] = heldTransforms[i].localRotation;
                heldScales[i] = heldTransforms[i].localScale;
            }
            heldMeshes = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            heldBlendShapes = new float[heldMeshes.Length][];
            for (int i = 0; i < heldMeshes.Length; i++)
            {
                int count = heldMeshes[i].sharedMesh != null ? heldMeshes[i].sharedMesh.blendShapeCount : 0;
                heldBlendShapes[i] = new float[count];
                for (int j = 0; j < count; j++) heldBlendShapes[i][j] = heldMeshes[i].GetBlendShapeWeight(j);
            }
        }

        private void LateUpdate()
        {
            if (heldTransforms == null) return;
            for (int i = 0; i < heldTransforms.Length; i++)
            {
                if (heldTransforms[i] == null) continue;
                heldTransforms[i].SetLocalPositionAndRotation(heldPositions[i], heldRotations[i]);
                heldTransforms[i].localScale = heldScales[i];
            }
            for (int i = 0; i < heldMeshes.Length; i++)
                if (heldMeshes[i] != null)
                    for (int j = 0; j < heldBlendShapes[i].Length; j++) heldMeshes[i].SetBlendShapeWeight(j, heldBlendShapes[i][j]);
        }

        public void Resume()
        {
            heldTransforms = null;
            foreach (var behaviour in suspended) if (behaviour != null) behaviour.enabled = true;
            suspended.Clear();
        }

        public void Release()
        {
            heldTransforms = null;
            suspended.Clear();
            foreach (var item in audience)
            {
                if (item.renderer != null) item.renderer.sharedMaterial = item.original;
                if (item.material != null) Destroy(item.material);
            }
            audience.Clear();
        }
        private void OnDestroy() { Release(); }
    }
}
