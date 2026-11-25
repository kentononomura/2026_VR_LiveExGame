using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>Each final transcript starts six copies together, independently of command success.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter))]
public sealed class StageRecognitionComments : MonoBehaviour
{
    [SerializeField] private TMP_FontAsset japaneseFont;
    [Min(0.5f)] [SerializeField] private float minimumTravelSeconds = 3f;
    [Min(0.5f)] [SerializeField] private float maximumTravelSeconds = 7f;
    [Min(12f)] [SerializeField] private float fontSize = 320f;
    [FormerlySerializedAs("maximumSimultaneousComments")]
    [Range(1, 6)] [SerializeField] private int copiesPerRecognition = 6;
    [Tooltip("スクリーンのローカル+Z側に表示する距離。")]
    [Min(0.001f)] [SerializeField] private float surfaceOffset = 0.02f;

    private const float CanvasWidth = 1920f;
    private RectTransform canvasRect;
    private Material textMaterial;
    private readonly List<CommentVisual> comments = new List<CommentVisual>(6);

    private sealed class CommentVisual
    {
        public TextMeshProUGUI label;
        public float elapsed;
        public float textWidth;
        public float duration;
        public float height;
        public bool showing;
    }

    public void Enqueue(string transcript)
    {
        if (!isActiveAndEnabled || string.IsNullOrWhiteSpace(transcript)) return;
        if (!EnsureDisplay()) return;
        // Keep recognition spelling (including [unk]); only remove line breaks.
        string singleLine = transcript.Replace('\r', ' ').Replace('\n', ' ').Trim();
        // A new result replaces the previous group, so all copies start together
        // and always show the latest recognized words without waiting for a queue.
        int count = Mathf.Clamp(copiesPerRecognition, 1, comments.Count);
        for (int index = 0; index < comments.Count; index++)
        {
            if (index < count) BeginComment(comments[index], singleLine, index, count);
            else
            {
                comments[index].showing = false;
                comments[index].label.gameObject.SetActive(false);
            }
        }
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

        textMaterial = new Material(japaneseFont.material);
        textMaterial.SetColor(ShaderUtilities.ID_OutlineColor, Color.black);
        textMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.2f);
        // Reuse six text objects; each active comment owns its timing and position.
        for (int index = 0; index < 6; index++) comments.Add(CreateComment(index));
        return true;
    }

    private CommentVisual CreateComment(int index)
    {
        GameObject textObject = new GameObject($"Transcript {index + 1}", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObject.layer = gameObject.layer;
        textObject.transform.SetParent(canvasRect, false);
        TextMeshProUGUI label = textObject.GetComponent<TextMeshProUGUI>();
        label.font = japaneseFont;
        label.fontSize = fontSize;
        label.color = Color.white;
        label.richText = false;
        label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.fontSharedMaterial = textMaterial;
        label.UpdateMeshPadding();
        label.rectTransform.anchorMin = label.rectTransform.anchorMax = Vector2.zero;
        label.rectTransform.pivot = new Vector2(0f, 0.5f);
        textObject.SetActive(false);
        return new CommentVisual { label = label };
    }

    private void BeginComment(CommentVisual comment, string transcript, int laneIndex, int laneCount)
    {
        TextMeshProUGUI label = comment.label;
        label.text = transcript;
        label.fontSize = Mathf.Max(12f, fontSize);
        Vector2 preferredSize = label.GetPreferredValues(label.text);
        comment.textWidth = preferredSize.x + label.fontSize;
        float textHeight = Mathf.Max(preferredSize.y, label.fontSize * 2f);
        label.rectTransform.sizeDelta = new Vector2(comment.textWidth, textHeight);
        // Keep the text rectangle inside the screen vertically, including outline padding.
        float margin = Mathf.Min(textHeight * 0.5f, canvasRect.rect.height * 0.5f);
        // Stratify the usable height: one copy per band. Restrict jitter to
        // the middle half of each band so adjacent copies cannot bunch up
        // at a shared boundary, while each recognition still looks different.
        float laneHeight = Mathf.Max(0f, canvasRect.rect.height - margin * 2f) / laneCount;
        comment.height = margin + laneHeight * (laneIndex + Random.Range(0.25f, 0.75f));
        label.rectTransform.anchoredPosition = new Vector2(CanvasWidth, comment.height);
        label.gameObject.SetActive(true);
        comment.elapsed = 0f;
        float minimum = Mathf.Max(0.5f, minimumTravelSeconds);
        comment.duration = Random.Range(minimum, Mathf.Max(minimum, maximumTravelSeconds));
        comment.showing = true;
    }

    private void Update()
    {
        if (canvasRect == null) return;
        foreach (CommentVisual comment in comments)
        {
            if (!comment.showing) continue;
            comment.elapsed += Time.unscaledDeltaTime;
            comment.label.rectTransform.anchoredPosition = new Vector2(
                Mathf.Lerp(CanvasWidth, -comment.textWidth,
                    Mathf.Clamp01(comment.elapsed / comment.duration)), comment.height);
            if (comment.elapsed < comment.duration) continue;
            comment.label.gameObject.SetActive(false);
            comment.showing = false;
        }
    }

    private void OnDisable()
    {
        foreach (CommentVisual comment in comments)
        {
            comment.showing = false;
            if (comment.label != null) comment.label.gameObject.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        if (canvasRect != null) Destroy(canvasRect.gameObject);
        if (textMaterial != null) Destroy(textMaterial);
    }
}
