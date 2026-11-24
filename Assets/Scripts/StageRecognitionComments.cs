using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Final transcripts over the stage screen; queued independently of command success.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter))]
public sealed class StageRecognitionComments : MonoBehaviour
{
    [SerializeField] private TMP_FontAsset japaneseFont;
    [Min(0.5f)] [SerializeField] private float travelSeconds = 5f;
    [Min(12f)] [SerializeField] private float fontSize = 64f;
    [Range(0.1f, 0.9f)] [SerializeField] private float verticalPosition = 0.75f;
    [Min(1)] [SerializeField] private int maximumPendingComments = 20;
    [Tooltip("スクリーンのローカル+Z側に表示する距離。")]
    [Min(0.001f)] [SerializeField] private float surfaceOffset = 0.02f;

    private const float CanvasWidth = 1920f;
    private readonly Queue<string> pending = new Queue<string>();
    private RectTransform canvasRect;
    private TextMeshProUGUI label;
    private Material textMaterial;
    private float elapsed;
    private float textWidth;
    private float activeDuration;
    private bool showing;

    public void Enqueue(string transcript)
    {
        if (!isActiveAndEnabled || string.IsNullOrWhiteSpace(transcript)) return;
        if (!EnsureDisplay()) return;
        // Keep recognition spelling (including [unk]); only remove line breaks.
        string singleLine = transcript.Replace('\r', ' ').Replace('\n', ' ').Trim();
        while (pending.Count >= Mathf.Max(1, maximumPendingComments)) pending.Dequeue();
        pending.Enqueue(singleLine);
        if (!showing) BeginNext();
    }

    private bool EnsureDisplay()
    {
        if (canvasRect != null) return true;
        Mesh mesh = GetComponent<MeshFilter>().sharedMesh;
        if (mesh == null || japaneseFont == null) return false;
        Bounds bounds = mesh.bounds;
        if (bounds.size.x <= 0f || bounds.size.y <= 0f) return false;

        GameObject root = new GameObject("Recognition Comments", typeof(RectTransform),
            typeof(Canvas), typeof(RectMask2D));
        root.layer = gameObject.layer; // Back-screen camera excludes this layer to avoid feedback.
        canvasRect = (RectTransform)root.transform;
        canvasRect.SetParent(transform, false);
        canvasRect.localPosition = new Vector3(bounds.center.x, bounds.center.y, bounds.max.z + surfaceOffset);
        // Audience is on +Z; Canvas text faces local -Z.
        canvasRect.localRotation = Quaternion.Euler(0f, 180f, 0f);
        canvasRect.localScale = Vector3.one * (bounds.size.x / CanvasWidth);
        canvasRect.sizeDelta = new Vector2(CanvasWidth, CanvasWidth * bounds.size.y / bounds.size.x);
        root.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;

        GameObject textObject = new GameObject("Transcript", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObject.layer = gameObject.layer;
        textObject.transform.SetParent(canvasRect, false);
        label = textObject.GetComponent<TextMeshProUGUI>();
        label.font = japaneseFont;
        label.fontSize = fontSize;
        label.color = Color.white;
        label.richText = false;
        label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        textMaterial = new Material(japaneseFont.material);
        textMaterial.SetColor(ShaderUtilities.ID_OutlineColor, Color.black);
        textMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.2f);
        label.fontSharedMaterial = textMaterial;
        label.UpdateMeshPadding();
        label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0f, verticalPosition);
        label.rectTransform.pivot = new Vector2(0f, 0.5f);
        textObject.SetActive(false);
        return true;
    }

    private void BeginNext()
    {
        if (pending.Count == 0) return;
        label.text = pending.Dequeue();
        label.fontSize = Mathf.Max(12f, fontSize);
        textWidth = label.GetPreferredValues(label.text).x + label.fontSize;
        label.rectTransform.sizeDelta = new Vector2(textWidth, label.fontSize * 2f);
        label.rectTransform.anchoredPosition = new Vector2(CanvasWidth, 0f);
        label.gameObject.SetActive(true);
        elapsed = 0f;
        activeDuration = Mathf.Max(0.5f, travelSeconds);
        showing = true;
    }

    private void Update()
    {
        if (!showing) return;
        elapsed += Time.unscaledDeltaTime;
        label.rectTransform.anchoredPosition = new Vector2(
            Mathf.Lerp(CanvasWidth, -textWidth, Mathf.Clamp01(elapsed / activeDuration)), 0f);
        if (elapsed < activeDuration) return;
        label.gameObject.SetActive(false);
        showing = false;
        BeginNext();
    }

    private void OnDisable()
    {
        pending.Clear();
        showing = false;
        if (label != null) label.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (canvasRect != null) Destroy(canvasRect.gameObject);
        if (textMaterial != null) Destroy(textMaterial);
    }
}
