using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace PVCapture
{
    /// <summary>All performance controllers advance by the same DSP-clock delta. No independent seek API.</summary>
    public sealed class PVAnimationController : MonoBehaviour
    {
        private readonly List<PlayableGraph> graphs = new List<PlayableGraph>();
        public double EvaluatedSeconds { get; private set; }
        public int ControllerCount => graphs.Count;

        public void Bind(IEnumerable<Animator> animators)
        {
            Release();
            foreach (var animator in animators)
            {
                if (animator == null || animator.runtimeAnimatorController == null) continue;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var graph = PlayableGraph.Create("PV " + animator.name);
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var controller = AnimatorControllerPlayable.Create(graph, animator.runtimeAnimatorController);
                var faceEvents = animator.GetComponent<PVFaceAnimationEvents>();
                if (faceEvents != null) faceEvents.Bind(controller);
                var output = AnimationPlayableOutput.Create(graph, "Performance", animator);
                output.SetSourcePlayable(controller);
                graph.Play();
                graph.Evaluate(0);
                graphs.Add(graph);
            }
        }

        public void AdvanceTo(double seconds)
        {
            double remaining = System.Math.Max(0, seconds - EvaluatedSeconds);
            // Small increments retain controller transitions/events even after an Editor stall.
            while (remaining > 0.0000001)
            {
                float step = (float)System.Math.Min(remaining, 1.0 / 60.0);
                foreach (var graph in graphs) if (graph.IsValid()) graph.Evaluate(step);
                remaining -= step;
            }
            EvaluatedSeconds = seconds;
        }

        public void Release()
        {
            foreach (var graph in graphs) if (graph.IsValid()) graph.Destroy();
            graphs.Clear();
            EvaluatedSeconds = 0;
        }
        private void OnDestroy() { Release(); }
    }
}
