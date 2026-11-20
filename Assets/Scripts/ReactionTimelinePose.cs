using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace ReactionTimelineLab
{
    // Absolute sampling: even at reaction weight 1 the dance uses the song's current time.
    public sealed class ReactionTimelinePose : IDisposable
    {
        private PlayableGraph graph;
        private AnimationClipPlayable dance, reaction;
        private AnimationLayerMixerPlayable mixer;
        private readonly AnimationClip danceClip;
        public double DanceTime => dance.GetTime();

        public ReactionTimelinePose(Animator animator, AnimationClip clip)
        {
            danceClip = clip;
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            animator.fireEvents = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            graph = PlayableGraph.Create("Reaction timeline laboratory");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            dance = AnimationClipPlayable.Create(graph, clip);
            dance.SetSpeed(0);
            dance.SetApplyFootIK(false);
            mixer = AnimationLayerMixerPlayable.Create(graph, 2);
            graph.Connect(dance, 0, mixer, 0);
            mixer.SetInputWeight(0, 1);
            AnimationPlayableOutput.Create(graph, "Pose", animator).SetSourcePlayable(mixer);
            graph.Play();
        }

        public void SetReaction(AnimationClip clip, AvatarMask mask)
        {
            if (reaction.IsValid())
            {
                graph.Disconnect(mixer, 1);
                graph.DestroyPlayable(reaction);
            }
            reaction = AnimationClipPlayable.Create(graph, clip);
            reaction.SetSpeed(0);
            reaction.SetApplyFootIK(false);
            graph.Connect(reaction, 0, mixer, 1);
            mixer.SetLayerMaskFromAvatarMask(1, mask);
            mixer.SetInputWeight(1, 0);
        }

        public void Sample(double songSeconds, double danceOffset, double reactionSeconds, float weight)
        {
            double t = Math.Max(0, Math.Min(danceClip.length - 0.0001, songSeconds + danceOffset));
            dance.SetTime(t);
            if (reaction.IsValid())
            {
                reaction.SetTime(Math.Max(0, reactionSeconds));
            }
            mixer.SetInputWeight(1, Mathf.Clamp01(weight));
            graph.Evaluate(0);
        }

        public static float Weight(double elapsed, double duration, float blendIn, float blendOut)
        {
            if (elapsed <= 0 || elapsed >= duration || duration <= 0) return 0;
            // Scale overlapping fades to fit short clips; avoid sudden truncation.
            double scale = Math.Min(1, duration / Math.Max(0.0001, blendIn + blendOut));
            double a = blendIn * scale, b = blendOut * scale;
            float enter = a > 0 ? Mathf.Clamp01((float)(elapsed / a)) : 1;
            float leave = b > 0 ? Mathf.Clamp01((float)((duration - elapsed) / b)) : 1;
            float x = Mathf.Min(enter, leave);
            return x * x * x * (x * (x * 6 - 15) + 10);
        }

        public void Dispose() { if (graph.IsValid()) graph.Destroy(); }
    }
}
