using UnityEngine;
using UnityEngine.XR;

/// <summary>シーンに配置して使用する左手ペンライトの微弱振動。</summary>
public sealed class PenlightSwingHaptics : MonoBehaviour
{
    [SerializeField] private PenlightGaugeController leftPenlight;
    [Range(0f, 1f)] [SerializeField] private float amplitude = 0.08f;
    [Min(0.01f)] [SerializeField] private float pulseDuration = 0.06f;
    [Min(0.01f)] [SerializeField] private float pulseInterval = 0.05f;
    [Tooltip("振動を開始する手の移動速度 (m/s)。")]
    [Min(0.01f)] [SerializeField] private float minimumSpeed = 0.15f;
    [Tooltip("振動を開始する回転速度 (度/秒)。手首だけの振りにも反応します。")]
    [Min(1f)] [SerializeField] private float minimumAngularSpeed = 45f;

    private InputDevice device;
    private Vector3 previousPosition;
    private Quaternion previousRotation;
    private bool hasPreviousPose;
    private bool ownsPulse;
    private float nextPulseTime;
    private float nextSearchTime;

    private void LateUpdate()
    {
        if (leftPenlight == null && Time.unscaledTime >= nextSearchTime)
        {
            nextSearchTime = Time.unscaledTime + 1f;
            foreach (var gauge in FindObjectsByType<PenlightGaugeController>())
            {
                var saber = gauge.GetComponent<Saber>();
                if (saber != null && saber.handType == Saber.HandType.Left)
                {
                    leftPenlight = gauge;
                    break;
                }
            }
        }

        if (leftPenlight == null || !leftPenlight.isActiveAndEnabled ||
            leftPenlight.saber == null || !leftPenlight.saber.isActiveAndEnabled ||
            leftPenlight.saber.handType != Saber.HandType.Left ||
            Time.timeScale <= 0f || !Application.isFocused)
        {
            ResetFeedback();
            return;
        }

        if (!device.isValid)
        {
            device = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            hasPreviousPose = false;
        }

        if (!device.isValid ||
            !device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) || !tracked ||
            !device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 position) ||
            !device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rotation))
        {
            ResetFeedback();
            return;
        }

        // XRのトラッキング空間で比較し、リグの移動や回転を振りとして扱わない。
        float distance = Vector3.Distance(position, previousPosition);
        float angle = Quaternion.Angle(rotation, previousRotation);
        float dt = Time.unscaledDeltaTime;
        bool swinging = hasPreviousPose && dt > 0f && dt <= 0.1f &&
            distance < 0.5f && angle < 90f &&
            (distance / dt >= Mathf.Max(0.01f, minimumSpeed) ||
             angle / dt >= Mathf.Max(1f, minimumAngularSpeed));
        previousPosition = position;
        previousRotation = rotation;
        hasPreviousPose = true;

        // Gauge.Update の後に実行し、同フレームのレベルアップも優先する。
        if (leftPenlight.IsLevelHapticActive)
        {
            ownsPulse = false;
            return;
        }

        if (!swinging || amplitude <= 0f)
        {
            StopPulse();
            return;
        }

        if (Time.unscaledTime < nextPulseTime) return;
        nextPulseTime = Time.unscaledTime + Mathf.Max(0.01f, pulseInterval);
        if (device.TryGetHapticCapabilities(out HapticCapabilities capabilities) &&
            capabilities.supportsImpulse && capabilities.numChannels > 0)
        {
            ownsPulse = device.SendHapticImpulse(0, Mathf.Clamp01(amplitude),
                Mathf.Max(0.01f, pulseDuration));
        }
    }

    private void StopPulse()
    {
        if (ownsPulse && device.isValid &&
            (leftPenlight == null || !leftPenlight.IsLevelHapticActive))
            device.StopHaptics();
        ownsPulse = false;
        nextPulseTime = 0f;
    }

    private void ResetFeedback()
    {
        StopPulse();
        hasPreviousPose = false;
    }

    private void OnDisable() => ResetFeedback();
    private void OnApplicationFocus(bool focused)
    {
        if (!focused) ResetFeedback();
    }
    private void OnApplicationPause(bool paused)
    {
        if (paused) ResetFeedback();
    }
}
