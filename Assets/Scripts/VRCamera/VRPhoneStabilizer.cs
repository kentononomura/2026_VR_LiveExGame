using UnityEngine;

/// <summary>
/// Smooths the phone as a whole, without changing controller tracking or input.
/// Keep this object directly under the tracked controller; its parent is the raw source.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(10000)]
public sealed class VRPhoneStabilizer : MonoBehaviour
{
    [Header("Phone Stabilization")]
    [Tooltip("スマホ本体・画面・撮影カメラをまとめて補正します。")]
    [SerializeField] private bool stabilizationEnabled = true;

    [Header("Position")]
    [Tooltip("静止時のカットオフ周波数（Hz）。小さいほど補正が強く、遅れが増えます。")]
    [Min(0.1f)] [SerializeField] private float positionMinCutoff = 4f;
    [Tooltip("移動速度（m/s）に応じて追従を速める係数。0なら固定ローパスです。")]
    [Min(0f)] [SerializeField] private float positionSpeedCoefficient = 4f;

    [Header("Rotation")]
    [Tooltip("静止時のカットオフ周波数（Hz）。位置より小さくすると回転を強く補正します。")]
    [Min(0.1f)] [SerializeField] private float rotationMinCutoff = 2f;
    [Tooltip("角速度（度/s）に応じて追従を速める係数。0なら固定ローパスです。")]
    [Min(0f)] [SerializeField] private float rotationSpeedCoefficient = 0.025f;

    [Header("Recovery")]
    [Tooltip("1フレームの位置変化がこの距離を超えたら補正履歴をリセットします（m）。")]
    [Min(0.01f)] [SerializeField] private float resetDistance = 0.5f;
    [Tooltip("1フレームの回転変化がこの角度を超えたら補正履歴をリセットします（度）。")]
    [Range(1f, 180f)] [SerializeField] private float resetAngle = 90f;

    private Transform source;
    private Transform trackingSpace;
    private Vector3 mountPosition;
    private Quaternion mountRotation;
    private Vector3 filteredPosition;
    private Quaternion filteredRotation;
    private Vector3 previousPosition;
    private Quaternion previousRotation;
    private Vector3 filteredVelocity;
    private float filteredAngularSpeed;
    private bool initialized;
    private bool mountCaptured;

    private void Awake()
    {
        mountPosition = transform.localPosition;
        mountRotation = transform.localRotation;
        mountCaptured = true;
    }

    private void OnEnable()
    {
        initialized = false;
        Application.onBeforeRender += ApplyBeforeRender;
    }

    private void OnDisable()
    {
        Application.onBeforeRender -= ApplyBeforeRender;
        initialized = false;
        RestoreMount();
    }

    private void RestoreMount()
    {
        if (mountCaptured)
            transform.SetLocalPositionAndRotation(mountPosition, mountRotation);
    }

    private void LateUpdate()
    {
        UpdatePose(Time.unscaledDeltaTime);
    }

    private void UpdatePose(float dt)
    {
        if (!stabilizationEnabled || transform.parent == null)
        {
            initialized = false;
            RestoreMount();
            return;
        }

        // Filter in tracking space so locomotion/rig rotation does not create lag.
        // Never sample this object's compensated transform (that would cause feedback).
        if (source != transform.parent || trackingSpace != transform.parent.parent)
        {
            source = transform.parent;
            trackingSpace = source.parent;
            initialized = false;
        }

        Vector3 position = source.localPosition;
        Quaternion rotation = source.localRotation;
        if (!initialized || dt <= 0f || dt > 0.25f ||
            Vector3.Distance(position, previousPosition) > resetDistance ||
            Quaternion.Angle(rotation, previousRotation) > resetAngle)
        {
            filteredPosition = position;
            filteredRotation = rotation;
            filteredVelocity = Vector3.zero;
            filteredAngularSpeed = 0f;
            initialized = true;
        }
        else
        {
            // Smooth the speed estimate before adapting the low-pass cutoff.
            float speedAlpha = Alpha(1f, dt);
            filteredVelocity = Vector3.Lerp(filteredVelocity,
                (position - previousPosition) / dt, speedAlpha);
            filteredAngularSpeed = Mathf.Lerp(filteredAngularSpeed,
                Quaternion.Angle(previousRotation, rotation) / dt, speedAlpha);
            float positionCutoff = Mathf.Max(0.1f, positionMinCutoff) +
                Mathf.Max(0f, positionSpeedCoefficient) * filteredVelocity.magnitude;
            float rotationCutoff = Mathf.Max(0.1f, rotationMinCutoff) +
                Mathf.Max(0f, rotationSpeedCoefficient) * filteredAngularSpeed;
            filteredPosition = Vector3.Lerp(filteredPosition, position, Alpha(positionCutoff, dt));
            filteredRotation = Quaternion.Slerp(filteredRotation, rotation, Alpha(rotationCutoff, dt));
        }

        previousPosition = position;
        previousRotation = rotation;
        ApplyFilteredPose();
    }

    private static float Alpha(float cutoff, float dt)
    {
        float step = 2f * Mathf.PI * cutoff * dt;
        return step / (1f + step);
    }

    // XR tracking updates parents again before rendering. Reapply the SAME filtered
    // pose after that update, without advancing the filter twice in a frame.
    [BeforeRenderOrder(10000)]
    private void ApplyBeforeRender()
    {
        if (!stabilizationEnabled)
        {
            RestoreMount();
            return;
        }
        if (initialized && source != null && source == transform.parent &&
            trackingSpace == source.parent)
            ApplyFilteredPose();
    }

    private void ApplyFilteredPose()
    {
        // Compose the original mounting offset with the smoothed controller pose,
        // including its scale, so rotation cannot shake the phone via its offset.
        Vector3 position = filteredPosition + filteredRotation *
            Vector3.Scale(source.localScale, mountPosition);
        Quaternion rotation = filteredRotation * mountRotation;
        if (trackingSpace != null)
        {
            position = trackingSpace.TransformPoint(position);
            rotation = trackingSpace.rotation * rotation;
        }
        transform.SetPositionAndRotation(position, rotation);
    }
}
