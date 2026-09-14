using UnityEngine;
using UnityEngine.InputSystem;

namespace PVCapture
{
    [RequireComponent(typeof(Camera))]
    public sealed class PVFreeCamera : MonoBehaviour
    {
        [Min(0.01f)] public float moveSpeed = 2;
        [Min(1)] public float fastMultiplier = 4;
        [Min(0.001f)] public float rotationSpeed = 0.12f;
        [Min(0)] public float smoothing = 12;
        public PVCameraPreset presets;
        public Camera CaptureCamera => GetComponent<Camera>();
        private Vector3 velocity;
        private Quaternion desiredRotation;
        private float yaw, pitch;

        private void OnEnable() { ResetMotion(); }
        public void ResetMotion()
        {
            velocity = Vector3.zero;
            desiredRotation = transform.rotation;
            yaw = transform.eulerAngles.y;
            pitch = Mathf.DeltaAngle(0, transform.eulerAngles.x);
        }

        public void Recall(int index)
        {
            if (presets == null || index < 0 || index >= presets.views.Length) return;
            var view = presets.views[index];
            if (view == null || !view.saved) return;
            transform.SetPositionAndRotation(view.position, view.rotation);
            CaptureCamera.fieldOfView = view.fieldOfView;
            ResetMotion();
        }

        private void Update()
        {
            // This camera keeps moving while the live performance is paused.
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (!Application.isFocused || keyboard == null || mouse == null) return;
            if (keyboard.escapeKey.wasPressedThisFrame) ReleaseCursor();
            else if (mouse.rightButton.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                ResetMotion();
                return; // Do not treat the click-to-focus mouse travel as a camera rotation.
            }
            if (Cursor.lockState != CursorLockMode.Locked) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            float wheel = mouse.scroll.ReadValue().y;
            if (wheel != 0) moveSpeed = Mathf.Clamp(moveSpeed * Mathf.Pow(1.2f, Mathf.Sign(wheel)), 0.02f, 100);
            Vector2 delta = mouse.delta.ReadValue() * rotationSpeed;
            yaw += delta.x;
            pitch = Mathf.Clamp(pitch - delta.y, -89, 89);
            desiredRotation = Quaternion.Euler(pitch, yaw, 0);
            float blend = smoothing <= 0 ? 1 : 1 - Mathf.Exp(-smoothing * dt);
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, blend);
            Vector3 input = new Vector3(
                (keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0),
                (keyboard.eKey.isPressed ? 1 : 0) - (keyboard.qKey.isPressed ? 1 : 0),
                (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0));
            Vector3 target = transform.right * input.x + Vector3.up * input.y + transform.forward * input.z;
            target = Vector3.ClampMagnitude(target, 1) * moveSpeed;
            if (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed) target *= fastMultiplier;
            velocity = Vector3.Lerp(velocity, target, blend);
            transform.position += velocity * dt;
        }

        private void OnApplicationFocus(bool focus) { if (!focus) ReleaseCursor(); }
        private void OnDisable() { ReleaseCursor(); }
        private void ReleaseCursor()
        {
            velocity = Vector3.zero;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
