using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Quest実機でInput SystemのTrackedPoseDriverがTracking Stateを取得できない場合にも、
/// XRNodeからHMDの位置・回転を直接反映するバックアップドライバー。
/// </summary>
[DefaultExecutionOrder(10000)]
public sealed class QuestHmdPoseDriver : MonoBehaviour
{
    [Header("Quest HMD Pose")]
    [Tooltip("Floor基準のHMD高さが取得できない場合に使用する標準目線高です。")]
    [Min(0.5f)]
    [SerializeField] private float fallbackEyeHeight = 1.7f;

    [Tooltip("これ未満のHMD高さは未取得値とみなし、標準目線高へ置き換えます。")]
    [Min(0f)]
    [SerializeField] private float minimumValidTrackedHeight = 0.5f;

    private InputDevice hmdDevice;
    private bool loggedTrackingReady;
    private bool loggedFallback;

    private void OnEnable()
    {
        Application.onBeforeRender += ApplyHmdPose;
        AcquireHmdDevice();
        ApplyHmdPose();
    }

    private void OnDisable()
    {
        Application.onBeforeRender -= ApplyHmdPose;
    }

    private void Update()
    {
        ApplyHmdPose();
    }

    private void AcquireHmdDevice()
    {
        hmdDevice = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
        if (!hmdDevice.isValid)
        {
            hmdDevice = InputDevices.GetDeviceAtXRNode(XRNode.Head);
        }
    }

    private void ApplyHmdPose()
    {
        if (!hmdDevice.isValid)
        {
            AcquireHmdDevice();
        }

        Vector3 position = default;
        Quaternion rotation = Quaternion.identity;
        bool hasPosition = hmdDevice.isValid &&
                           hmdDevice.TryGetFeatureValue(CommonUsages.devicePosition, out position);
        bool hasRotation = hmdDevice.isValid &&
                           hmdDevice.TryGetFeatureValue(CommonUsages.deviceRotation, out rotation);

        if (hasPosition)
        {
            if (position.y < minimumValidTrackedHeight)
            {
                position.y = fallbackEyeHeight;
                LogFallbackOnce();
            }

            transform.localPosition = position;
        }
        else
        {
            Vector3 fallbackPosition = transform.localPosition;
            fallbackPosition.y = fallbackEyeHeight;
            transform.localPosition = fallbackPosition;
            LogFallbackOnce();
        }

        if (hasRotation)
        {
            transform.localRotation = rotation;
        }

        if (!loggedTrackingReady && hasPosition && hasRotation)
        {
            loggedTrackingReady = true;
            Debug.Log($"[QuestHmdPoseDriver] HMDトラッキングを取得しました。Eye Height={transform.localPosition.y:F2}m");
        }
    }

    private void LogFallbackOnce()
    {
        if (loggedFallback) return;
        loggedFallback = true;
        Debug.LogWarning($"[QuestHmdPoseDriver] HMD位置が未取得のため、目線高{fallbackEyeHeight:F2}mを使用します。回転は取得でき次第反映します。");
    }
}
