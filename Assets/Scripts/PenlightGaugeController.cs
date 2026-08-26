using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

public class PenlightGaugeController : MonoBehaviour
{
    public enum GaugeGainMode
    {
        SwingAmplitude,
        LegacyVelocity
    }

    public enum SwingAmplitudeMode
    {
        LargerOfRotationOrDistance,
        RotationOnly,
        DistanceOnly
    }

    public enum PenlightColorState
    {
        Normal = 0,
        Blue = 1,
        Yellow = 2,
        Pink = 3
    }

    [Header("References")]
    [Tooltip("Saber script to read velocity from")]
    public Saber saber;
    [Tooltip("UI Image representing the meter (must be Image Type: Filled)")]
    public Image meterFillImage;

    [Header("Gauge Settings")]
    [Tooltip("Maximum gauge value")]
    public float maxGauge = 100f;
    [Tooltip("How much gauge decreases per second when not shaking")]
    public float gaugeDecreaseRate = 3f;

    [Header("Swing Amplitude Settings")]
    [Tooltip("Swing Amplitudeは振り幅中心、Legacy Velocityは従来の速度中心の判定です。")]
    [SerializeField] private GaugeGainMode gaugeGainMode = GaugeGainMode.SwingAmplitude;

    [Tooltip("角度幅と手の移動幅のどちらを一振りの大きさとして使用するか選択します。")]
    [SerializeField] private SwingAmplitudeMode swingAmplitudeMode =
        SwingAmplitudeMode.LargerOfRotationOrDistance;

    [Tooltip("これ未満の回転幅はゲージへ加算しません。小さくすると軽い振りでも反応します。")]
    [Range(0f, 90f)]
    [SerializeField] private float minimumSwingAngle = 12f;

    [Tooltip("この回転幅で一振り分の最大評価になります。小さくするとレベルが上がりやすくなります。")]
    [Range(10f, 180f)]
    [SerializeField] private float fullSwingAngle = 65f;

    [Tooltip("これ未満の手の移動幅はゲージへ加算しません。")]
    [Range(0f, 0.5f)]
    [SerializeField] private float minimumSwingDistance = 0.06f;

    [Tooltip("この移動幅で一振り分の最大評価になります。")]
    [Range(0.05f, 1.5f)]
    [SerializeField] private float fullSwingDistance = 0.3f;

    [Tooltip("最大振り幅の一振りで増えるゲージ量です。30なら大振り3回ほどでレベル3になります。")]
    [Min(0f)]
    [SerializeField] private float gaugePerFullSwing = 30f;

    [Tooltip("振り幅が最大値からこの割合だけ戻ったら、折り返して次の一振りが始まったと判定します。")]
    [Range(0.01f, 0.5f)]
    [SerializeField] private float reversalSensitivity = 0.06f;

    [Tooltip("この時間静止したら、現在位置を次の振り始めとして再設定します。")]
    [Range(0.05f, 1f)]
    [SerializeField] private float stationaryResetDelay = 0.2f;

    [Tooltip("1フレームの回転がこれ未満ならトラッキングの微細な揺れとして無視します。")]
    [Range(0f, 3f)]
    [SerializeField] private float rotationNoiseDegrees = 0.15f;

    [Tooltip("1フレームの移動がこれ未満ならトラッキングの微細な揺れとして無視します。")]
    [Range(0f, 0.02f)]
    [SerializeField] private float positionNoiseDistance = 0.0005f;

    [Tooltip("これ以上の瞬間移動は振りではなくトラッキング補正として無視します。")]
    [Min(0.1f)]
    [SerializeField] private float trackingJumpDistance = 0.5f;

    [Header("Legacy Velocity Settings")]
    [Tooltip("Minimum velocity (m/s) to consider as shaking")]
    public float shakeThreshold = 1.5f; 
    [Tooltip("How much gauge is added per second while shaking")]
    public float gaugePerShake = 15f; 

    [Header("Level Settings")]
    [Tooltip("レベル1（青）になるゲージ値です。")]
    [Min(0f)]
    [SerializeField] private float level1Threshold = 25f;
    [Tooltip("レベル2（黄）になるゲージ値です。")]
    [Min(0f)]
    [SerializeField] private float level2Threshold = 50f;
    [Tooltip("レベル3（ピンク）になるゲージ値です。")]
    [Min(0f)]
    [SerializeField] private float level3Threshold = 75f;

    [Tooltip("Colors for Level 0, 1, 2, 3 (White, Blue, Yellow, Pink)")]
    public Color[] levelColors = new Color[] 
    { 
        Color.white, 
        Color.blue, 
        Color.yellow, 
        new Color(1f, 0.4f, 0.7f) // Pink
    };
    [Tooltip("Vibration duration on level up")]
    public float hapticDuration = 0.3f;
    [Tooltip("Vibration strength on level up")]
    public float hapticAmplitude = 0.8f;

    private float currentGauge = 0f;
    private int currentLevel = 0;
    private bool swingTrackingInitialized;
    private Vector3 previousTrackedPosition;
    private Quaternion previousTrackedRotation;
    private Vector3 strokeStartPosition;
    private Quaternion strokeStartRotation;
    private Vector3 peakStrokePosition;
    private Quaternion peakStrokeRotation;
    private float peakStrokeProgress;
    private float awardedStrokeProgress;
    private float stationaryTime;

    /// <summary>
    /// 既存のゲージレベルを色状態として公開します。
    /// 色状態を別途保持せず、既存のcurrentLevelを唯一の情報源として使用します。
    /// </summary>
    public PenlightColorState CurrentColorState =>
        (PenlightColorState)Mathf.Clamp(currentLevel, 0, 3);
    public float CurrentGauge => currentGauge;
    
    // パフォーマンスのため、定期的にモデルの色を再適用するためのフラグ
    // （Saber.csのStartによるモデル生成の遅延対応）
    private bool initialColorApplied = false;

    void Start()
    {
        if (saber == null)
            saber = GetComponent<Saber>();

        ResetSwingTracking(transform.position, transform.rotation);
    }

    private void OnValidate()
    {
        maxGauge = Mathf.Max(0.1f, maxGauge);
        minimumSwingAngle = Mathf.Max(0f, minimumSwingAngle);
        fullSwingAngle = Mathf.Max(minimumSwingAngle + 0.01f, fullSwingAngle);
        minimumSwingDistance = Mathf.Max(0f, minimumSwingDistance);
        fullSwingDistance = Mathf.Max(minimumSwingDistance + 0.001f, fullSwingDistance);
    }

    void Update()
    {
        if (saber == null) return;

        // 初回のみ少し遅れて色を適用（Saber.csがPrefabをInstantiateした後に適用するため）
        if (!initialColorApplied && transform.Find("SaberVisual") != null)
        {
            UpdateColor(currentLevel);
            initialColorApplied = true;
        }

        bool isSwinging = gaugeGainMode == GaugeGainMode.SwingAmplitude
            ? UpdateAmplitudeGauge()
            : UpdateLegacyVelocityGauge();

        if (!isSwinging)
        {
            // 自然減衰
            currentGauge -= gaugeDecreaseRate * Time.deltaTime;
        }

        currentGauge = Mathf.Clamp(currentGauge, 0f, maxGauge);

        // UIメーターの更新
        if (meterFillImage != null)
        {
            meterFillImage.fillAmount = currentGauge / maxGauge;
            // メーターの色も現在のレベルの色に合わせる（視覚的にわかりやすくする）
            meterFillImage.color = levelColors[Mathf.Clamp(currentLevel, 0, levelColors.Length - 1)];
        }

        // Inspectorで設定したゲージ値からレベルを判定
        // Inspectorの保存値は書き換えず、実行時だけ安全な昇順として解釈する。
        float runtimeLevel1Threshold = Mathf.Max(0f, level1Threshold);
        float runtimeLevel2Threshold = Mathf.Max(runtimeLevel1Threshold, level2Threshold);
        float runtimeLevel3Threshold = Mathf.Max(runtimeLevel2Threshold, level3Threshold);
        int newLevel = 0;
        if (currentGauge >= runtimeLevel3Threshold) newLevel = 3;
        else if (currentGauge >= runtimeLevel2Threshold) newLevel = 2;
        else if (currentGauge >= runtimeLevel1Threshold) newLevel = 1;
        else newLevel = 0;

        // レベルが変わったときの処理
        if (newLevel != currentLevel)
        {
            if (newLevel > currentLevel)
            {
                // レベルアップ時のみハプティクス（振動）を鳴らす
                TriggerHaptics();
            }
            currentLevel = newLevel;
            UpdateColor(currentLevel);
        }
    }

    private bool UpdateLegacyVelocityGauge()
    {
        if (saber.VelocityMagnitude <= shakeThreshold) return false;

        float intensity = Mathf.Clamp(saber.VelocityMagnitude - shakeThreshold, 0.5f, 5f);
        currentGauge += gaugePerShake * intensity * Time.deltaTime;
        return true;
    }

    private bool UpdateAmplitudeGauge()
    {
        Vector3 currentPosition = transform.position;
        Quaternion currentRotation = transform.rotation;

        if (!swingTrackingInitialized)
        {
            ResetSwingTracking(currentPosition, currentRotation);
            return false;
        }

        float frameDistance = Vector3.Distance(previousTrackedPosition, currentPosition);
        float frameAngle = Quaternion.Angle(previousTrackedRotation, currentRotation);

        // Recenterやトラッキング復帰の瞬間移動を大振りとして数えない。
        if (frameDistance >= Mathf.Max(0.1f, trackingJumpDistance))
        {
            ResetSwingTracking(currentPosition, currentRotation);
            return false;
        }

        bool isMoving =
            frameDistance >= Mathf.Max(0f, positionNoiseDistance) ||
            frameAngle >= Mathf.Max(0f, rotationNoiseDegrees);

        previousTrackedPosition = currentPosition;
        previousTrackedRotation = currentRotation;

        if (!isMoving)
        {
            stationaryTime += Time.deltaTime;
            if (stationaryTime >= Mathf.Max(0.05f, stationaryResetDelay))
            {
                BeginStroke(currentPosition, currentRotation);
            }
            return false;
        }

        stationaryTime = 0f;
        float progress = CalculateSwingProgress(
            strokeStartPosition,
            strokeStartRotation,
            currentPosition,
            currentRotation);

        if (progress >= peakStrokeProgress)
        {
            peakStrokeProgress = progress;
            peakStrokePosition = currentPosition;
            peakStrokeRotation = currentRotation;
            AwardNewAmplitude(progress);
        }
        else if (peakStrokeProgress - progress >= Mathf.Max(0.01f, reversalSensitivity))
        {
            // 最大地点から戻り始めたら、最大地点を次の振り始めとして往復を分離する。
            BeginStroke(peakStrokePosition, peakStrokeRotation);
            float returnProgress = CalculateSwingProgress(
                strokeStartPosition,
                strokeStartRotation,
                currentPosition,
                currentRotation);
            peakStrokeProgress = returnProgress;
            peakStrokePosition = currentPosition;
            peakStrokeRotation = currentRotation;
            AwardNewAmplitude(returnProgress);
        }

        // 最低振り幅へ届く前でも、ゆっくり大きく振っている途中は減衰させない。
        return true;
    }

    private float CalculateSwingProgress(
        Vector3 startPosition,
        Quaternion startRotation,
        Vector3 currentPosition,
        Quaternion currentRotation)
    {
        float angle = Quaternion.Angle(startRotation, currentRotation);
        float distance = Vector3.Distance(startPosition, currentPosition);
        float angleProgress = Mathf.InverseLerp(
            minimumSwingAngle,
            Mathf.Max(fullSwingAngle, minimumSwingAngle + 0.01f),
            angle);
        float distanceProgress = Mathf.InverseLerp(
            minimumSwingDistance,
            Mathf.Max(fullSwingDistance, minimumSwingDistance + 0.001f),
            distance);

        switch (swingAmplitudeMode)
        {
            case SwingAmplitudeMode.RotationOnly:
                return angleProgress;
            case SwingAmplitudeMode.DistanceOnly:
                return distanceProgress;
            default:
                return Mathf.Max(angleProgress, distanceProgress);
        }
    }

    private void AwardNewAmplitude(float progress)
    {
        float clampedProgress = Mathf.Clamp01(progress);
        float newProgress = Mathf.Max(0f, clampedProgress - awardedStrokeProgress);
        if (newProgress <= 0f) return;

        currentGauge += newProgress * Mathf.Max(0f, gaugePerFullSwing);
        awardedStrokeProgress = clampedProgress;
    }

    private void ResetSwingTracking(Vector3 position, Quaternion rotation)
    {
        swingTrackingInitialized = true;
        previousTrackedPosition = position;
        previousTrackedRotation = rotation;
        stationaryTime = 0f;
        BeginStroke(position, rotation);
    }

    private void BeginStroke(Vector3 position, Quaternion rotation)
    {
        strokeStartPosition = position;
        strokeStartRotation = rotation;
        peakStrokePosition = position;
        peakStrokeRotation = rotation;
        peakStrokeProgress = 0f;
        awardedStrokeProgress = 0f;
    }

    private void UpdateColor(int level)
    {
        if (level < 0 || level >= levelColors.Length) return;

        Color targetColor = levelColors[level];
        
        // Saberの3Dモデルを探して色を変更する
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        foreach (Renderer rend in renderers)
        {
            // メーターのCanvasなどを誤って処理しないように確認
            if (rend.name.Contains("Canvas") || rend.name.Contains("Meter")) continue;

            Material[] mats = rend.materials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                // 大文字小文字を区別せずに名前に "light" が含まれるか判定
                if (mats[i].name.ToLower().Contains("light"))
                {
                    mats[i].EnableKeyword("_EMISSION");
                    
                    // Standard Shader 系
                    if (mats[i].HasProperty("_Color")) 
                        mats[i].color = targetColor;
                    if (mats[i].HasProperty("_EmissionColor")) 
                        mats[i].SetColor("_EmissionColor", targetColor * 2.0f);
                    
                    // URP 系 / カスタムシェーダー系 (ArnoldStandardSurface 等)
                    if (mats[i].HasProperty("_BaseColor")) 
                        mats[i].SetColor("_BaseColor", targetColor);
                    if (mats[i].HasProperty("_BASE_COLOR")) 
                        mats[i].SetColor("_BASE_COLOR", targetColor);
                    if (mats[i].HasProperty("_EMISSION_COLOR")) 
                        mats[i].SetColor("_EMISSION_COLOR", targetColor * 2.0f);

                    changed = true;
                }
            }
            
            if (changed)
            {
                rend.materials = mats;
            }
        }
    }

    private void TriggerHaptics()
    {
        // 堅牢なハプティクス呼び出し (XRIのバージョンに依存せず、InputDeviceから直接呼ぶ)
        XRNode node = (saber != null && saber.handType == Saber.HandType.Left) ? XRNode.LeftHand : XRNode.RightHand;
        InputDevice device = InputDevices.GetDeviceAtXRNode(node);
        
        if (device.isValid)
        {
            HapticCapabilities capabilities;
            if (device.TryGetHapticCapabilities(out capabilities) && capabilities.supportsImpulse)
            {
                device.SendHapticImpulse(0, hapticAmplitude, hapticDuration);
            }
        }
    }
}
