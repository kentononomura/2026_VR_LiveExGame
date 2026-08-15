using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

public class PenlightGaugeController : MonoBehaviour
{
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
    [Tooltip("Minimum velocity (m/s) to consider as shaking")]
    public float shakeThreshold = 1.5f; 
    [Tooltip("How much gauge is added per second while shaking")]
    public float gaugePerShake = 15f; 

    [Header("Level Settings")]
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
    
    // パフォーマンスのため、定期的にモデルの色を再適用するためのフラグ
    // （Saber.csのStartによるモデル生成の遅延対応）
    private bool initialColorApplied = false;

    void Start()
    {
        if (saber == null)
            saber = GetComponent<Saber>();
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

        // 加速度（速度）によるゲージ加算
        if (saber.VelocityMagnitude > shakeThreshold)
        {
            // 振りの強さに応じて加算量を調整
            float intensity = Mathf.Clamp(saber.VelocityMagnitude - shakeThreshold, 0.5f, 5f);
            currentGauge += gaugePerShake * intensity * Time.deltaTime;
        }
        else
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

        // レベル判定 (0: 0-24, 1: 25-49, 2: 50-74, 3: 75-100)
        int newLevel = 0;
        if (currentGauge >= 75f) newLevel = 3;
        else if (currentGauge >= 50f) newLevel = 2;
        else if (currentGauge >= 25f) newLevel = 1;
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
