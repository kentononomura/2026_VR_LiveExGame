using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using ReactionTimelineLab;

/// <summary>Compare authored Generic bone rotations with Humanoid retargeting on the actual game prefab.</summary>
public static class ReactionAvatarAudit
{
    private const string Folder = "Assets/UnityChan/Animations/Reaction";
    private const string Prefab = "Assets/UnityChan/Prefabs/unitychan_hw.prefab";
    private const string Request = "Temp/ReactionAvatarAudit.request";
    private static readonly HumanBodyBones[] Arms = {
        HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
        HumanBodyBones.RightShoulder, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand };
    private static readonly HumanBodyBones[] Legs = {
        HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
        HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot };

    [InitializeOnLoadMethod]
    private static void Schedule() { EditorApplication.delayCall += RequestedRun; }
    private static void RequestedRun()
    {
        if (!File.Exists(Request) || EditorApplication.isPlayingOrWillChangePlaymode) return;
        string mode = File.ReadAllText(Request).Trim();
        File.Delete(Request);
        Run(Path.GetFileName(mode));
    }

    [MenuItem("Tools/Reaction Timeline/Audit Refined Avatars")]
    public static void Audit() { Run("audit"); }

    private static void Run(string label)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        var report = new StringBuilder("Reaction Avatar Audit: " + label + "\n");
        report.AppendLine("Angles in degrees. Local rotations include Humanoid twist redistribution; segment direction and hand orientation are compared in the chest frame.");
        string temp = "Assets/Editor/ReactionAudit_" + Guid.NewGuid().ToString("N");
        AssetDatabase.CreateFolder("Assets/Editor", Path.GetFileName(temp));
        try
        {
            foreach (string path in Directory.GetFiles(Folder, "*.fbx")) AuditClip(path.Replace('\\', '/'), temp, report);
        }
        finally
        {
            AssetDatabase.DeleteAsset(temp);
            Directory.CreateDirectory("Temp/ReactionAvatarAudit");
            File.WriteAllText("Temp/ReactionAvatarAudit/" + label + ".txt", report.ToString());
        }
        Debug.Log("[ReactionAvatarAudit]\n" + report);
    }

    private static void AuditClip(string path, string temp, StringBuilder report)
    {
        string rawPath = temp + "/" + Path.GetFileName(path);
        if (!AssetDatabase.CopyAsset(path, rawPath)) throw new IOException("Could not copy diagnostic FBX.");
        var rawImporter = (ModelImporter)AssetImporter.GetAtPath(rawPath);
        rawImporter.animationType = ModelImporterAnimationType.Generic;
        rawImporter.SaveAndReimport();
        var rawClip = Clip(rawPath);
        var humanClip = Clip(path);
        var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().Single();
        report.AppendLine($"\n{humanClip.name}: {humanClip.length:F3}s; validAvatar={avatar.isValid}; human={humanClip.isHumanMotion}");
        var holder = new GameObject("Reaction audit (temporary)");
        holder.SetActive(false);
        var source = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(rawPath), holder.transform);
        var solo = Spawn(holder);
        var authoredView = Spawn(holder);
        var mixed = Spawn(holder);
        var baseline = Spawn(holder);
        foreach (var script in holder.GetComponentsInChildren<MonoBehaviour>(true)) script.enabled = false;
        foreach (var animator in holder.GetComponentsInChildren<Animator>(true))
        { animator.runtimeAnimatorController = null; animator.applyRootMotion = false; animator.fireEvents = false; }
        var sourceBones = source.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
        var mask = new AvatarMask();
        for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++) mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, true);
        var upper = AssetDatabase.LoadAssetAtPath<AvatarMask>("Assets/VRCameraAssets/UpperBodyMask.mask");
        holder.SetActive(true);
        var graph = PlayableGraph.Create("Standalone reaction audit");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        var playable = AnimationClipPlayable.Create(graph, humanClip);
        playable.SetSpeed(0);
        playable.SetApplyFootIK(false);
        AnimationPlayableOutput.Create(graph, "Standalone", solo).SetSourcePlayable(playable);
        graph.Play();
        var dance = Clip("Assets/UnityChan/Animations/C86unitychan_003_NOT01_Final.fbx");
        try
        {
            using (var mix = new ReactionTimelinePose(mixed, dance))
            using (var basePose = new ReactionTimelinePose(baseline, dance))
            {
                mix.SetReaction(humanClip, mask);
                float maxAuthored = 0, maxMixer = 0, maxLeg = 0, maxReturn = 0, maxSegment = 0, maxHand = 0;
                foreach (float fraction in Enumerable.Range(0, 61).Select(i => i / 60f))
                {
                    double time = humanClip.length * fraction;
                    rawClip.SampleAnimation(source, (float)time);
                    playable.SetTime(time);
                    graph.Evaluate(0);
                    float error = 0;
                    var details = new StringBuilder();
                    foreach (var bone in Arms)
                    {
                        var target = solo.GetBoneTransform(bone);
                        if (!target || !sourceBones.TryGetValue(target.name, out var raw)) continue;
                        float angle = Quaternion.Angle(target.localRotation, raw.localRotation);
                        error = Mathf.Max(error, angle);
                        details.Append($" {bone}={angle:F1}");
                    }
                    maxAuthored = Mathf.Max(maxAuthored, error);
                    float maxDirection = 0;
                    var soloBones = solo.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
                    foreach (string side in new[] { "Left", "Right" })
                    foreach (var pair in new[] { new[] { "Arm", "ForeArm" }, new[] { "ForeArm", "Hand" } })
                    {
                        string from = "Character1_" + side + pair[0], to = "Character1_" + side + pair[1];
                        Vector3 rawDirection = sourceBones["Character1_Spine2"].InverseTransformDirection(sourceBones[to].position - sourceBones[from].position);
                        Vector3 humanDirection = soloBones["Character1_Spine2"].InverseTransformDirection(soloBones[to].position - soloBones[from].position);
                        maxDirection = Mathf.Max(maxDirection, Vector3.Angle(rawDirection, humanDirection));
                    }
                    maxSegment = Mathf.Max(maxSegment, maxDirection);
                    foreach (string side in new[] { "Left", "Right" })
                    {
                        string hand = "Character1_" + side + "Hand";
                        var rawRotation = Quaternion.Inverse(sourceBones["Character1_Spine2"].rotation) * sourceBones[hand].rotation;
                        var humanRotation = Quaternion.Inverse(soloBones["Character1_Spine2"].rotation) * soloBones[hand].rotation;
                        maxHand = Mathf.Max(maxHand, Quaternion.Angle(rawRotation, humanRotation));
                    }
                    mix.Sample(20 + time, 2.246667, time, 1);
                    maxMixer = Mathf.Max(maxMixer, Difference(solo, mixed, Arms));
                    report.AppendLine($"  t={time:F3}: source-vs-solo max={error:F2}; segment-direction={maxDirection:F2};" + details);
                    if (fraction == 0.5f || fraction == 0.75f)
                    {
                        authoredView.enabled = false;
                        foreach (var target in authoredView.GetComponentsInChildren<Transform>(true))
                            if (sourceBones.TryGetValue(target.name, out var raw)) target.localRotation = raw.localRotation;
                        RenderComparison(authoredView, solo, holder, $"{Path.GetFileNameWithoutExtension(path)}_{fraction:F2}");
                    }
                }
                // Compare upper-body layer at full weight with the same reaction alone.
                mix.SetReaction(humanClip, upper);
                float maxUpper = 0;
                foreach (double danceTime in new[] { 10.0, 30.0, 60.0 })
                foreach (float fraction in new[] { 0.25f, 0.5f, 0.75f })
                {
                    double time = humanClip.length * fraction;
                    playable.SetTime(time); graph.Evaluate(0);
                    basePose.Sample(danceTime, 2.246667, 0, 0);
                    mix.Sample(danceTime, 2.246667, time, 1);
                    maxLeg = Mathf.Max(maxLeg, Difference(baseline, mixed, Legs));
                    maxUpper = Mathf.Max(maxUpper, Difference(solo, mixed, Arms));
                    foreach (float weight in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
                    {
                        mix.Sample(danceTime, 2.246667, time, weight);
                        foreach (var bone in Arms)
                        {
                            var q = mixed.GetBoneTransform(bone).localRotation;
                            if (new[] { q.x, q.y, q.z, q.w }.Any(v => float.IsNaN(v) || float.IsInfinity(v))) throw new Exception("Non-finite blended rotation");
                        }
                    }
                    mix.Sample(danceTime, 2.246667, time, 0);
                    maxReturn = Mathf.Max(maxReturn, Difference(baseline, mixed, Arms));
                }
                report.AppendLine($"  SUMMARY segment-direction={maxSegment:F3}, hand-orientation={maxHand:F3}, authored-vs-solo={maxAuthored:F3}, full-layer-vs-solo={maxMixer:F3}, upper-arms-vs-solo={maxUpper:F3}, upper-leg-drift={maxLeg:F3}, return-error={maxReturn:F3}");
                if (maxSegment > 5f || maxHand > 5f || maxMixer > 0.2f || maxUpper > 0.2f || maxLeg > 0.2f || maxReturn > 0.2f)
                    throw new Exception("Reference pose or layer sampling regression; see audit report.");
            }
        }
        finally
        {
            graph.Destroy();
            UnityEngine.Object.DestroyImmediate(holder);
            UnityEngine.Object.DestroyImmediate(mask);
        }
    }

    private static Animator Spawn(GameObject holder)
    {
        var obj = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab), holder.transform);
        obj.transform.localPosition = Vector3.zero;
        obj.transform.localRotation = Quaternion.identity;
        var animator = obj.GetComponent<Animator>();
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        return animator;
    }

    private static void RenderComparison(Animator authored, Animator human, GameObject holder, string name)
    {
        var renderers = holder.GetComponentsInChildren<Renderer>(true);
        foreach (var renderer in renderers) renderer.enabled = false;
        foreach (var renderer in authored.GetComponentsInChildren<Renderer>(true)) renderer.enabled = true;
        foreach (var renderer in human.GetComponentsInChildren<Renderer>(true)) renderer.enabled = true;
        authored.transform.position = new Vector3(0.75f, 0, 0);
        human.transform.position = new Vector3(-0.75f, 0, 0);
        var cameraObject = new GameObject("Audit camera", typeof(Camera));
        var lightObject = new GameObject("Audit light", typeof(Light));
        var camera = cameraObject.GetComponent<Camera>();
        var light = lightObject.GetComponent<Light>();
        light.type = LightType.Directional; light.intensity = 1.2f;
        light.transform.rotation = Quaternion.Euler(35, -30, 0);
        camera.transform.position = new Vector3(0, 1.2f, 4.8f);
        camera.transform.LookAt(new Vector3(0, 1f, 0));
        camera.fieldOfView = 30; camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.08f, 0.1f, 0.15f);
        var rt = new RenderTexture(1200, 900, 24);
        var tex = new Texture2D(1200, 900, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        try
        {
            camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, 1200, 900), 0, 0); tex.Apply();
            Directory.CreateDirectory("Temp/ReactionAvatarAudit/Images");
            File.WriteAllBytes("Temp/ReactionAvatarAudit/Images/" + name + ".png", tex.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = null; RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(tex);
            UnityEngine.Object.DestroyImmediate(cameraObject); UnityEngine.Object.DestroyImmediate(lightObject);
            authored.transform.position = Vector3.zero; human.transform.position = Vector3.zero;
        }
    }

    private static float Difference(Animator a, Animator b, HumanBodyBones[] bones)
    {
        float result = 0;
        foreach (var bone in bones)
            result = Mathf.Max(result, Quaternion.Angle(a.GetBoneTransform(bone).localRotation, b.GetBoneTransform(bone).localRotation));
        return result;
    }

    private static AnimationClip Clip(string path) => AssetDatabase.LoadAllAssetsAtPath(path)
        .OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
}
