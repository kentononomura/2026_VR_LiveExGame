using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
public sealed class VoiceCommandHUDEntry
{
    public string commandId;
    public string displayText;
}

/// <summary>
/// ステージ固定の応援掲示板、または従来の頭部追従HUDを表示します。
/// UI階層は起動時に一度だけ生成し、リアクション成立時だけ該当項目を強調します。
/// </summary>
[DisallowMultipleComponent]
public sealed class VoiceCommandHUD : MonoBehaviour
{
    [Header("Content")]
    [SerializeField] private string heading = "ボイスコマンド";
    [SerializeField] private List<VoiceCommandHUDEntry> commands = new List<VoiceCommandHUDEntry>
    {
        new VoiceCommandHUDEntry { commandId = "LookAt", displayText = "こっち向いて" },
        new VoiceCommandHUDEntry { commandId = "Wave", displayText = "手を振って" },
        new VoiceCommandHUDEntry { commandId = "Cute", displayText = "かわいい" },
        new VoiceCommandHUDEntry { commandId = "UnityChanCall", displayText = "ユニティちゃん" }
    };
    [SerializeField] private TMP_FontAsset japaneseFont;

    [Header("Stage Billboard")]
    [SerializeField] private bool useStageBillboard;
    [Tooltip("掲示板表面の中心。Canvasの正面はローカル-Z方向です。")]
    [SerializeField] private Transform billboardAnchor;
    [SerializeField] private Material housingMaterial;
    [SerializeField] private bool showVoicePointDebug;
    [Min(0f)] [SerializeField] private float supportHeight = 0.6f;

    [Header("Placement")]
    [Min(0.1f)]
    [SerializeField] private float distanceFromCamera = 2f;
    [SerializeField] private float horizontalOffset = 0.75f;
    [SerializeField] private float verticalOffset = -0.05f;
    [Min(0f)]
    [SerializeField] private float positionFollowSpeed = 5f;
    [Min(0f)]
    [SerializeField] private float rotationFollowSpeed = 5f;
    [Min(0.0001f)]
    [SerializeField] private float worldScale = 0.0014f;

    [Header("Appearance")]
    [SerializeField] private Color panelColor = new Color(0.025f, 0.035f, 0.055f, 0.78f);
    [SerializeField] private Color normalBackgroundColor = new Color(1f, 1f, 1f, 0.1f);
    [SerializeField] private Color normalTextColor = Color.white;
    [SerializeField] private Color highlightBackgroundColor = new Color(1f, 0.22f, 0.52f, 0.94f);
    [SerializeField] private Color highlightTextColor = Color.white;
    [Range(1f, 1.3f)]
    [SerializeField] private float highlightScale = 1.08f;

    [Header("Highlight Timing")]
    [Min(0f)]
    [SerializeField] private float fadeInDuration = 0.12f;
    [Min(0f)]
    [SerializeField] private float highlightHoldDuration = 1.5f;
    [Min(0f)]
    [SerializeField] private float fadeOutDuration = 0.35f;

    private sealed class ItemVisual
    {
        public RectTransform rectTransform;
        public Image background;
        public TMP_Text label;
    }

    private readonly Dictionary<string, ItemVisual> items =
        new Dictionary<string, ItemVisual>(StringComparer.OrdinalIgnoreCase);
    private readonly List<ItemVisual> itemList = new List<ItemVisual>();

    private RectTransform canvasRect;
    private TMP_Text voicePointText;
    private Coroutine highlightCoroutine;
    private bool hasInitialPlacement;
    private GameObject billboardHousing;

    private void Awake()
    {
        BuildHUD();
        ResetAllItemsImmediate();
    }

    private void OnEnable()
    {
        hasInitialPlacement = false;
        if (canvasRect != null) canvasRect.gameObject.SetActive(true);
        if (billboardHousing != null) billboardHousing.SetActive(true);
        ResetAllItemsImmediate();
    }

    private void LateUpdate()
    {
        if (useStageBillboard) return;
        Camera mainCamera = Camera.main;
        if (mainCamera == null || canvasRect == null) return;

        Transform cameraTransform = mainCamera.transform;
        Vector3 targetPosition = cameraTransform.position +
            cameraTransform.forward * distanceFromCamera +
            cameraTransform.right * horizontalOffset +
            cameraTransform.up * verticalOffset;
        Quaternion targetRotation = cameraTransform.rotation;

        if (!hasInitialPlacement)
        {
            canvasRect.SetPositionAndRotation(targetPosition, targetRotation);
            hasInitialPlacement = true;
            return;
        }

        float positionT = DampFactor(positionFollowSpeed);
        float rotationT = DampFactor(rotationFollowSpeed);
        canvasRect.position = Vector3.Lerp(canvasRect.position, targetPosition, positionT);
        canvasRect.rotation = Quaternion.Slerp(canvasRect.rotation, targetRotation, rotationT);
    }

    public void HighlightCommand(string commandId)
    {
        if (!isActiveAndEnabled) return;
        if (string.IsNullOrWhiteSpace(commandId) ||
            !items.TryGetValue(commandId, out ItemVisual target))
        {
            return;
        }

        if (highlightCoroutine != null)
        {
            StopCoroutine(highlightCoroutine);
        }

        highlightCoroutine = StartCoroutine(PlayHighlightRoutine(target));
    }

    /// <summary>直近の声かけ判定を、次の判定まで表示します。表示のための再計算は行いません。</summary>
    public void ShowVoicePoint(VoicePointEvaluator.EvaluationResult? result)
    {
        if (!showVoicePointDebug) return;
        if (voicePointText == null) BuildHUD();
        if (voicePointText == null) return;
        if (!result.HasValue)
        {
            voicePointText.text = "音声ポイント: 計算できません";
            return;
        }

        VoicePointEvaluator.EvaluationResult value = result.Value;
        voicePointText.text =
            $"直近の音声ポイント: {value.FinalPoint:F2}点\n" +
            $"必要 {value.Threshold:F2}点 / {(value.Succeeded ? "成功" : "不足")}\n" +
            $"距離 {value.Distance:F2}m / 基礎 {value.BasePoint:F2}点\n" +
            $"ペンライト x{value.PenlightMultiplier:F2} / ハート x{value.HeartMultiplier:F2}";
    }

    private void BuildHUD()
    {
        if (canvasRect != null) return;

        GameObject canvasObject = new GameObject(
            "Voice Command HUD Canvas",
            typeof(RectTransform),
            typeof(Canvas));
        canvasObject.layer = gameObject.layer;
        Transform displayParent = useStageBillboard && billboardAnchor != null ? billboardAnchor : transform;
        canvasObject.transform.SetParent(displayParent, false);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = !useStageBillboard;
        canvas.sortingOrder = useStageBillboard ? 0 : 50;

        canvasRect = canvasObject.GetComponent<RectTransform>();
        float commandAreaHeight = Mathf.Max(430f, 134f + (commands?.Count ?? 0) * 74f);
        canvasRect.sizeDelta = new Vector2(560f, commandAreaHeight + (showVoicePointDebug ? 150f : 0f));
        canvasRect.localScale = Vector3.one * worldScale;

        if (useStageBillboard) BuildHousing(displayParent, canvasRect.sizeDelta * worldScale);

        Image panel = CreateImage("Panel", canvasRect, panelColor);
        StretchToParent(panel.rectTransform);

        Image accent = CreateImage(
            "Accent",
            canvasRect,
            new Color(highlightBackgroundColor.r, highlightBackgroundColor.g,
                highlightBackgroundColor.b, 0.9f));
        RectTransform accentRect = accent.rectTransform;
        accentRect.anchorMin = new Vector2(0f, 1f);
        accentRect.anchorMax = new Vector2(1f, 1f);
        accentRect.pivot = new Vector2(0.5f, 1f);
        accentRect.anchoredPosition = Vector2.zero;
        accentRect.sizeDelta = new Vector2(0f, 8f);

        TMP_Text headingText = CreateText("Heading", canvasRect, heading, 38f);
        RectTransform headingRect = headingText.rectTransform;
        headingRect.anchorMin = new Vector2(0.5f, 1f);
        headingRect.anchorMax = new Vector2(0.5f, 1f);
        headingRect.pivot = new Vector2(0.5f, 1f);
        headingRect.anchoredPosition = new Vector2(0f, -28f);
        headingRect.sizeDelta = new Vector2(500f, 62f);
        headingText.alignment = TextAlignmentOptions.Center;
        headingText.fontStyle = FontStyles.Bold;

        if (showVoicePointDebug)
        {
            voicePointText = CreateText("Voice Point Debug", canvasRect,
                "直近の音声ポイント: --\n声かけを認識すると表示します", 26f);
            RectTransform pointRect = voicePointText.rectTransform;
            pointRect.anchorMin = new Vector2(0.5f, 1f);
            pointRect.anchorMax = new Vector2(0.5f, 1f);
            pointRect.pivot = new Vector2(0.5f, 1f);
            pointRect.anchoredPosition = new Vector2(0f, -commandAreaHeight);
            pointRect.sizeDelta = new Vector2(500f, 132f);
            voicePointText.alignment = TextAlignmentOptions.TopLeft;
            voicePointText.enableAutoSizing = true;
            voicePointText.fontSizeMin = 20f;
            voicePointText.fontSizeMax = 26f;
        }

        items.Clear();
        itemList.Clear();

        if (commands == null) return;

        for (int index = 0; index < commands.Count; index++)
        {
            VoiceCommandHUDEntry entry = commands[index];
            if (entry == null || string.IsNullOrWhiteSpace(entry.commandId)) continue;

            if (items.ContainsKey(entry.commandId))
            {
                Debug.LogWarning(
                    $"[VoiceCommandHUD] commandId '{entry.commandId}' が重複しています。最初の項目だけを使用します。",
                    this);
                continue;
            }

            Image row = CreateImage($"Command {entry.commandId}", canvasRect, normalBackgroundColor);
            RectTransform rowRect = row.rectTransform;
            rowRect.anchorMin = new Vector2(0.5f, 1f);
            rowRect.anchorMax = new Vector2(0.5f, 1f);
            rowRect.pivot = new Vector2(0.5f, 0.5f);
            rowRect.anchoredPosition = new Vector2(0f, -124f - index * 74f);
            rowRect.sizeDelta = new Vector2(480f, 60f);

            TMP_Text label = CreateText(
                "Label",
                rowRect,
                string.IsNullOrWhiteSpace(entry.displayText) ? entry.commandId : entry.displayText,
                32f);
            StretchToParent(label.rectTransform, 16f, 5f);
            label.alignment = TextAlignmentOptions.Center;

            ItemVisual visual = new ItemVisual
            {
                rectTransform = rowRect,
                background = row,
                label = label
            };
            items.Add(entry.commandId, visual);
            itemList.Add(visual);
        }
    }

    private void BuildHousing(Transform parent, Vector2 screenSize)
    {
        billboardHousing = new GameObject("Billboard Housing");
        billboardHousing.layer = gameObject.layer;
        billboardHousing.transform.SetParent(parent, false);
        // Canvas faces -Z. Keep the front of the opaque housing behind the screen.
        CreateHousingPart("Cabinet", new Vector3(0f, 0f, 0.09f),
            new Vector3(screenSize.x + 0.12f, screenSize.y + 0.12f, 0.16f));
        if (supportHeight <= 0f) return;
        float bottom = -screenSize.y * 0.5f - 0.06f;
        foreach (float side in new[] { -1f, 1f })
        {
            CreateHousingPart("Support", new Vector3(side * screenSize.x * 0.32f,
                bottom - supportHeight * 0.5f, 0.09f), new Vector3(0.07f, supportHeight, 0.1f));
        }
        CreateHousingPart("Base", new Vector3(0f, bottom - supportHeight, 0.09f),
            new Vector3(screenSize.x + 0.2f, 0.08f, 0.5f));
    }

    private void CreateHousingPart(string partName, Vector3 position, Vector3 scale)
    {
        GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = partName;
        part.layer = gameObject.layer;
        part.transform.SetParent(billboardHousing.transform, false);
        part.transform.localPosition = position;
        part.transform.localScale = scale;
        Collider partCollider = part.GetComponent<Collider>();
        partCollider.enabled = false;
        Destroy(partCollider);
        if (housingMaterial != null) part.GetComponent<MeshRenderer>().sharedMaterial = housingMaterial;
    }

    private void OnDrawGizmosSelected()
    {
        if (!useStageBillboard) return;
        Transform anchor = billboardAnchor != null ? billboardAnchor : transform;
        Matrix4x4 previous = Gizmos.matrix;
        Gizmos.matrix = anchor.localToWorldMatrix;
        Gizmos.color = Color.cyan;
        float height = Mathf.Max(430f, 134f + (commands?.Count ?? 0) * 74f)
            + (showVoicePointDebug ? 150f : 0f);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(560f * worldScale, height * worldScale, 0.02f));
        Gizmos.DrawLine(Vector3.zero, Vector3.back * 0.5f);
        Gizmos.matrix = previous;
    }

    private void OnDestroy()
    {
        if (canvasRect != null) Destroy(canvasRect.gameObject);
        if (billboardHousing != null) Destroy(billboardHousing);
    }

    private Image CreateImage(string objectName, Transform parent, Color color)
    {
        GameObject imageObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        imageObject.layer = gameObject.layer;
        imageObject.transform.SetParent(parent, false);
        Image image = imageObject.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private TMP_Text CreateText(
        string objectName,
        Transform parent,
        string value,
        float fontSize)
    {
        GameObject textObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        textObject.layer = gameObject.layer;
        textObject.transform.SetParent(parent, false);

        TMP_Text text = textObject.GetComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = fontSize;
        text.color = normalTextColor;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;

        if (japaneseFont != null)
        {
            text.font = japaneseFont;
        }
        else
        {
            Debug.LogWarning(
                "[VoiceCommandHUD] 日本語フォントが未設定です。既定フォントでは日本語が表示されない可能性があります。",
                this);
        }

        return text;
    }

    private IEnumerator PlayHighlightRoutine(ItemVisual target)
    {
        Color[] startBackgroundColors = new Color[itemList.Count];
        Color[] startTextColors = new Color[itemList.Count];
        Vector3[] startScales = new Vector3[itemList.Count];

        for (int index = 0; index < itemList.Count; index++)
        {
            ItemVisual item = itemList[index];
            startBackgroundColors[index] = item.background.color;
            startTextColors[index] = item.label.color;
            startScales[index] = item.rectTransform.localScale;
        }

        float elapsed = 0f;
        while (elapsed < fadeInDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = SmoothProgress(elapsed, fadeInDuration);
            for (int index = 0; index < itemList.Count; index++)
            {
                ItemVisual item = itemList[index];
                bool isTarget = item == target;
                ApplyVisual(
                    item,
                    Color.Lerp(startBackgroundColors[index],
                        isTarget ? highlightBackgroundColor : normalBackgroundColor, t),
                    Color.Lerp(startTextColors[index],
                        isTarget ? highlightTextColor : normalTextColor, t),
                    Vector3.Lerp(startScales[index],
                        Vector3.one * (isTarget ? highlightScale : 1f), t));
            }
            yield return null;
        }

        SetOnlyTargetHighlighted(target);

        elapsed = 0f;
        while (elapsed < highlightHoldDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        elapsed = 0f;
        while (elapsed < fadeOutDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = SmoothProgress(elapsed, fadeOutDuration);
            ApplyVisual(
                target,
                Color.Lerp(highlightBackgroundColor, normalBackgroundColor, t),
                Color.Lerp(highlightTextColor, normalTextColor, t),
                Vector3.Lerp(Vector3.one * highlightScale, Vector3.one, t));
            yield return null;
        }

        ResetAllItemsImmediate();
        highlightCoroutine = null;
    }

    private void SetOnlyTargetHighlighted(ItemVisual target)
    {
        foreach (ItemVisual item in itemList)
        {
            bool isTarget = item == target;
            ApplyVisual(
                item,
                isTarget ? highlightBackgroundColor : normalBackgroundColor,
                isTarget ? highlightTextColor : normalTextColor,
                Vector3.one * (isTarget ? highlightScale : 1f));
        }
    }

    private void ResetAllItemsImmediate()
    {
        foreach (ItemVisual item in itemList)
        {
            ApplyVisual(item, normalBackgroundColor, normalTextColor, Vector3.one);
        }
    }

    private static void ApplyVisual(
        ItemVisual item,
        Color backgroundColor,
        Color textColor,
        Vector3 scale)
    {
        item.background.color = backgroundColor;
        item.label.color = textColor;
        item.rectTransform.localScale = scale;
    }

    private static float SmoothProgress(float elapsed, float duration)
    {
        if (duration <= 0f) return 1f;
        return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
    }

    private static void StretchToParent(RectTransform rectTransform, float horizontal = 0f, float vertical = 0f)
    {
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = Vector2.zero;
        rectTransform.sizeDelta = new Vector2(-horizontal * 2f, -vertical * 2f);
    }

    private static float DampFactor(float speed)
    {
        if (speed <= 0f) return 1f;
        return 1f - Mathf.Exp(-speed * Time.unscaledDeltaTime);
    }

    private void OnDisable()
    {
        if (highlightCoroutine != null)
        {
            StopCoroutine(highlightCoroutine);
            highlightCoroutine = null;
        }
        ResetAllItemsImmediate();
        if (canvasRect != null) canvasRect.gameObject.SetActive(false);
        if (billboardHousing != null) billboardHousing.SetActive(false);
    }
}
