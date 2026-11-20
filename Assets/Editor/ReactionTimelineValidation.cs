using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ReactionTimelineLab;

public static class ReactionTimelineValidation
{
    [MenuItem("Tools/Reaction Timeline/Open Test Scene")]
    public static void Open()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            EditorSceneManager.OpenScene("Assets/Scenes/ReactionTimeline_Test.unity");
    }

    [MenuItem("Tools/Reaction Timeline/Validate Sampling")]
    public static void Validate()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/UnityChan/Prefabs/unitychan_hw.prefab");
        var dance = Clip("Assets/UnityChan/Animations/C86unitychan_003_NOT01_Final.fbx");
        var reaction = Clip("Assets/UnityChan/Animations/unitychan_WIN00.fbx");
        var holder = new GameObject("Temporary reaction validation");
        holder.SetActive(false);
        var a = UnityEngine.Object.Instantiate(prefab, holder.transform);
        var b = UnityEngine.Object.Instantiate(prefab, holder.transform);
        foreach (var script in holder.GetComponentsInChildren<MonoBehaviour>(true)) script.enabled = false;
        var aa = a.GetComponent<Animator>();
        var ab = b.GetComponent<Animator>();
        var mask = new AvatarMask();
        for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
            mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, true);
        holder.SetActive(true);
        try
        {
            using (var baseline = new ReactionTimelinePose(aa, dance))
            using (var test = new ReactionTimelinePose(ab, dance))
            {
                test.SetReaction(reaction, mask);
                foreach (double t in new[] { 20.0, 20.15, 21.0, 22.0, 25.0, 10.0, 60.0 })
                {
                    float weight = ReactionTimelinePose.Weight(t - 20, 3, 0.3f, 0.7f);
                    baseline.Sample(t, 2.246667, 0, 0);
                    test.Sample(t, 2.246667, Math.Max(0, t - 20), weight);
                    Require(Math.Abs(test.DanceTime - baseline.DanceTime) < 0.000001, "Dance time diverged");
                    if (weight == 0)
                        for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
                        {
                            var ba = aa.GetBoneTransform((HumanBodyBones)i);
                            var bb = ab.GetBoneTransform((HumanBodyBones)i);
                            if (!ba || !bb) continue;
                            Require(Quaternion.Angle(ba.localRotation, bb.localRotation) < 0.1f, "Return pose differs: " + ba.name);
                        }
                }
                Require(ReactionTimelinePose.Weight(1, 3, 0.3f, 0.7f) == 1, "Full override never reached");
                Require(ReactionTimelinePose.Weight(3, 3, 0.3f, 0.7f) == 0, "Reaction did not end");
                var upperMask = AssetDatabase.LoadAssetAtPath<AvatarMask>("Assets/VRCameraAssets/UpperBodyMask.mask");
                test.SetReaction(reaction, upperMask);
                baseline.Sample(30, 2.246667, 0, 0);
                test.Sample(30, 2.246667, 0.5, 1);
                foreach (var bone in new[] { HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg,
                    HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg })
                    Require(Quaternion.Angle(aa.GetBoneTransform(bone).localRotation,
                        ab.GetBoneTransform(bone).localRotation) < 0.1f, "Upper mask changed leg: " + bone);
            }
            Debug.Log("[ReactionTimelineValidation] PASS: absolute dance sampling, full override envelope, return pose, backward seek and upper-body leg isolation.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(holder);
            UnityEngine.Object.DestroyImmediate(mask);
        }
    }

    private static AnimationClip Clip(string path)
    {
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
            if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__")) return clip;
        throw new InvalidOperationException("Missing clip: " + path);
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
