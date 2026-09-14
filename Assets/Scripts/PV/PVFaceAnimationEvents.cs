using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace PVCapture
{
    /// <summary>Retains the shared clips' face events without FaceUpdate's mouse input.</summary>
    public sealed class PVFaceAnimationEvents : MonoBehaviour
    {
        private AnimatorControllerPlayable controller;
        public void Bind(AnimatorControllerPlayable value) { controller = value; }
        public void OnCallChangeFace(string stateName)
        {
            if (!controller.IsValid() || controller.GetLayerCount() < 2) return;
            int hash = Animator.StringToHash(stateName);
            if (!controller.HasState(1, hash)) return;
            controller.SetLayerWeight(1, 1);
            controller.CrossFadeInFixedTime(hash, 0, 1);
        }
    }
}
