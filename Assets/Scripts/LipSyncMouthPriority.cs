using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 表情アニメーション中の口形状だけを抑制し、曲のMTH_A/I/U/E/O Lipsyncを優先します。
/// Animatorと通常のLipSyncControllerより後に処理します。
/// </summary>
[DefaultExecutionOrder(10000)]
public sealed class LipSyncMouthPriority : MonoBehaviour
{
    private struct MouthBlendShape
    {
        public SkinnedMeshRenderer Renderer;
        public int Index;
    }

    private readonly List<MouthBlendShape> expressionMouthShapes =
        new List<MouthBlendShape>();
    private float prioritizeUntil;
    private bool initialized;

    public void PrioritizeFor(float duration)
    {
        if (!initialized)
        {
            CacheExpressionMouthShapes();
        }

        prioritizeUntil = Mathf.Max(prioritizeUntil, Time.time + Mathf.Max(0f, duration));
    }

    private void LateUpdate()
    {
        if (Time.time >= prioritizeUntil)
        {
            return;
        }

        foreach (MouthBlendShape shape in expressionMouthShapes)
        {
            if (shape.Renderer != null)
            {
                shape.Renderer.SetBlendShapeWeight(shape.Index, 0f);
            }
        }
    }

    private void CacheExpressionMouthShapes()
    {
        expressionMouthShapes.Clear();

        SkinnedMeshRenderer[] renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (SkinnedMeshRenderer renderer in renderers)
        {
            Mesh mesh = renderer.sharedMesh;
            if (mesh == null) continue;

            for (int index = 0; index < mesh.blendShapeCount; index++)
            {
                string blendShapeName = mesh.GetBlendShapeName(index);
                if (IsExpressionMouthShape(blendShapeName))
                {
                    expressionMouthShapes.Add(new MouthBlendShape
                    {
                        Renderer = renderer,
                        Index = index
                    });
                }
            }
        }

        initialized = true;
    }

    private static bool IsExpressionMouthShape(string blendShapeName)
    {
        if (string.IsNullOrEmpty(blendShapeName) || !blendShapeName.Contains("MTH_"))
        {
            return false;
        }

        // These five shapes belong to song Lipsync and must never be suppressed.
        return !blendShapeName.EndsWith("MTH_A") &&
               !blendShapeName.EndsWith("MTH_I") &&
               !blendShapeName.EndsWith("MTH_U") &&
               !blendShapeName.EndsWith("MTH_E") &&
               !blendShapeName.EndsWith("MTH_O");
    }
}
