using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Titleシーンの常設メニュー上で、マイク接続状態と実入力レベルを表示する。
/// </summary>
public sealed class TitleMicrophoneIndicator : MonoBehaviour
{
    private const string ObjectName = "MicrophoneIndicator";

    private Image backgroundImage;
    private Image statusDot;
    private Image meterBackground;
    private Image meterFill;
    private RectTransform meterFillRect;
    private Text statusText;
    private TitleVoiceManager voiceManager;

    private string labelText = "マイク入力";
    private Color waitingColor = new Color(1f, 0.65f, 0.15f, 1f);
    private Color readyColor = new Color(0.2f, 0.9f, 0.45f, 1f);
    private Color detectingColor = new Color(0.15f, 1f, 0.35f, 1f);
    private Color meterBackgroundColor = new Color(1f, 1f, 1f, 0.18f);
    private Color panelColor = new Color(0f, 0f, 0f, 0.42f);
    private float detectionThreshold = 0.08f;
    private float nextManagerSearchTime;

    public static TitleMicrophoneIndicator Create(Transform parent, Font font)
    {
        if (parent == null) return null;

        Transform existing = parent.Find(ObjectName);
        if (existing != null)
        {
            return existing.GetComponent<TitleMicrophoneIndicator>();
        }

        GameObject root = CreateUIObject(ObjectName, parent);
        TitleMicrophoneIndicator indicator = root.AddComponent<TitleMicrophoneIndicator>();
        indicator.BuildVisuals(font);
        return indicator;
    }

    public void Configure(
        string displayLabel,
        Font font,
        int fontSize,
        Color waiting,
        Color ready,
        Color detecting,
        Color barBackground,
        Color background,
        float inputDetectionThreshold)
    {
        labelText = string.IsNullOrWhiteSpace(displayLabel) ? "マイク入力" : displayLabel;
        waitingColor = waiting;
        readyColor = ready;
        detectingColor = detecting;
        meterBackgroundColor = barBackground;
        panelColor = background;
        detectionThreshold = Mathf.Clamp01(inputDetectionThreshold);

        if (statusText != null)
        {
            if (font != null) statusText.font = font;
            statusText.fontSize = Mathf.Max(1, fontSize);
        }

        if (backgroundImage != null) backgroundImage.color = panelColor;
        if (meterBackground != null) meterBackground.color = meterBackgroundColor;
    }

    private void BuildVisuals(Font font)
    {
        RectTransform rootRect = (RectTransform)transform;
        rootRect.anchorMin = new Vector2(0.5f, 0.5f);
        rootRect.anchorMax = new Vector2(0.5f, 0.5f);
        rootRect.pivot = new Vector2(0.5f, 0.5f);
        rootRect.sizeDelta = new Vector2(900f, 90f);

        backgroundImage = gameObject.AddComponent<Image>();
        backgroundImage.color = panelColor;
        backgroundImage.raycastTarget = false;

        GameObject dotObject = CreateUIObject("StatusDot", transform);
        RectTransform dotRect = (RectTransform)dotObject.transform;
        dotRect.anchorMin = new Vector2(0f, 0.5f);
        dotRect.anchorMax = new Vector2(0f, 0.5f);
        dotRect.pivot = new Vector2(0.5f, 0.5f);
        dotRect.anchoredPosition = new Vector2(28f, 17f);
        dotRect.sizeDelta = new Vector2(25f, 25f);
        statusDot = dotObject.AddComponent<Image>();
        statusDot.color = waitingColor;
        statusDot.raycastTarget = false;

        GameObject textObject = CreateUIObject("StatusText", transform);
        RectTransform textRect = (RectTransform)textObject.transform;
        textRect.anchorMin = new Vector2(0f, 0.38f);
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(52f, 0f);
        textRect.offsetMax = new Vector2(-15f, -2f);
        statusText = textObject.AddComponent<Text>();
        statusText.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        statusText.fontSize = 34;
        statusText.color = Color.white;
        statusText.alignment = TextAnchor.MiddleLeft;
        statusText.horizontalOverflow = HorizontalWrapMode.Overflow;
        statusText.verticalOverflow = VerticalWrapMode.Truncate;
        statusText.raycastTarget = false;

        GameObject meterBackgroundObject = CreateUIObject("LevelBackground", transform);
        RectTransform meterBackgroundRect = (RectTransform)meterBackgroundObject.transform;
        meterBackgroundRect.anchorMin = new Vector2(0f, 0f);
        meterBackgroundRect.anchorMax = new Vector2(1f, 0.28f);
        meterBackgroundRect.offsetMin = new Vector2(52f, 9f);
        meterBackgroundRect.offsetMax = new Vector2(-15f, -3f);
        meterBackground = meterBackgroundObject.AddComponent<Image>();
        meterBackground.color = meterBackgroundColor;
        meterBackground.raycastTarget = false;

        GameObject meterFillObject = CreateUIObject("LevelFill", meterBackgroundObject.transform);
        meterFillRect = (RectTransform)meterFillObject.transform;
        meterFillRect.anchorMin = Vector2.zero;
        meterFillRect.anchorMax = new Vector2(0f, 1f);
        meterFillRect.offsetMin = Vector2.zero;
        meterFillRect.offsetMax = Vector2.zero;
        meterFill = meterFillObject.AddComponent<Image>();
        meterFill.color = readyColor;
        meterFill.raycastTarget = false;

        SetLevel(0f);
    }

    private void Update()
    {
        if (voiceManager == null && Time.unscaledTime >= nextManagerSearchTime)
        {
            nextManagerSearchTime = Time.unscaledTime + 1f;
            voiceManager = FindAnyObjectByType<TitleVoiceManager>();
        }

        if (voiceManager == null)
        {
            SetState($"{labelText}: 音声システムを検索中...", waitingColor, 0f);
            return;
        }

        if (!voiceManager.IsVoiceModelReady)
        {
            SetState($"{labelText}: {voiceManager.VoiceModelStatus}...", waitingColor, 0f);
            return;
        }

        if (!voiceManager.IsMicrophoneListening)
        {
            SetState($"{labelText}: マイク開始待ち...", waitingColor, 0f);
            return;
        }

        if (!voiceManager.HasMicrophoneDataStream)
        {
            SetState($"{labelText}: 録音データ待ち...", waitingColor, 0f);
            return;
        }

        float level = voiceManager.MicrophoneInputLevel;
        bool isDetecting = level >= detectionThreshold;
        Color stateColor = isDetecting ? detectingColor : readyColor;
        string state = isDetecting ? "音声入力を検知" : "接続済み・入力待ち";
        SetState($"{labelText}: {state}  {Mathf.RoundToInt(level * 100f)}%", stateColor, level);
    }

    private void SetState(string message, Color color, float level)
    {
        if (statusText != null) statusText.text = message;
        if (statusDot != null) statusDot.color = color;
        if (meterFill != null) meterFill.color = color;
        SetLevel(level);
    }

    private void SetLevel(float level)
    {
        if (meterFillRect == null) return;
        Vector2 anchorMax = meterFillRect.anchorMax;
        anchorMax.x = Mathf.Clamp01(level);
        meterFillRect.anchorMax = anchorMax;
    }

    private static GameObject CreateUIObject(string objectName, Transform parent)
    {
        GameObject obj = new GameObject(objectName, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        obj.layer = parent.gameObject.layer;
        return obj;
    }
}
