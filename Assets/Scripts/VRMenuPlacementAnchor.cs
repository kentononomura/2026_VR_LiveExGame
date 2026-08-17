using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 常設VRメニューの編集用配置アンカー。
/// Sceneビューの枠を見ながら、位置と回転を調整できます。
/// </summary>
[ExecuteAlways]
public sealed class VRMenuPlacementAnchor : MonoBehaviour
{
    [Header("Window")]
    [Tooltip("実行時に生成されるメニューのTransformスケールです。")]
    [Min(0.0001f)]
    [SerializeField] private float runtimeMenuScale = 0.0012f;

    [Tooltip("メニューウィンドウのUIサイズです。Sceneビューの水色枠にも反映されます。")]
    [SerializeField] private Vector2 windowSize = new Vector2(800f, 600f);

    [Header("Menu Text")]
    [Tooltip("メニュー上部のタイトルです。")]
    [SerializeField] private string windowTitle = "SETTINGS";

    [Tooltip("音量スライダー上部の文字です。")]
    [SerializeField] private string volumeLabel = "Master Volume";

    [Tooltip("再開ボタンの文字です。Testシーンで使用されます。")]
    [SerializeField] private string resumeButtonText = "Resume Game";

    [Tooltip("タイトルへ戻るボタンの文字です。")]
    [SerializeField] private string titleButtonText = "Return to Title";

    [Tooltip("ゲーム終了ボタンの文字です。")]
    [SerializeField] private string quitButtonText = "Quit Game";

    [Tooltip("メニュー内の全文字へ適用するフォントです。未設定ならPrefabのフォントを維持します。")]
    [SerializeField] private Font menuFont;

    [Header("Text Layout")]
    [SerializeField] private Vector2 titlePosition = new Vector2(0f, -50f);
    [Tooltip("TitleTextは横方向へStretchしているため、X=0でウィンドウ幅に追従します。")]
    [SerializeField] private Vector2 titleSize = new Vector2(0f, 100f);
    [Min(1)] [SerializeField] private int titleFontSize = 80;

    [SerializeField] private Vector2 volumeLabelPosition = new Vector2(0f, 60f);
    [SerializeField] private Vector2 volumeLabelSize = new Vector2(400f, 50f);
    [Min(1)] [SerializeField] private int labelFontSize = 40;

    [Header("Slider Layout")]
    [SerializeField] private Vector2 sliderPosition = Vector2.zero;
    [SerializeField] private Vector2 sliderSize = new Vector2(400f, 40f);

    [Header("Button Layout")]
    [SerializeField] private Vector2 resumeButtonPosition = new Vector2(0f, -100f);
    [SerializeField] private Vector2 titleButtonPosition = new Vector2(0f, -180f);
    [SerializeField] private Vector2 quitButtonPosition = new Vector2(0f, -260f);
    [SerializeField] private Vector2 buttonSize = new Vector2(300f, 60f);
    [Min(1)] [SerializeField] private int buttonFontSize = 40;

    public float RuntimeMenuScale => runtimeMenuScale;

    public void ApplyTo(GameObject menuRoot)
    {
        if (menuRoot == null) return;

        RectTransform rootRect = menuRoot.GetComponent<RectTransform>();
        if (rootRect != null) rootRect.sizeDelta = windowSize;

        Transform panel = menuRoot.transform.Find("Panel");
        if (panel == null) return;

        ApplyText(panel.Find("TitleText"), windowTitle, titleFontSize, titlePosition, titleSize);
        ApplyText(panel.Find("VolumeLabel"), volumeLabel, labelFontSize, volumeLabelPosition, volumeLabelSize);
        ApplyRect(panel.Find("Slider"), sliderPosition, sliderSize);
        ApplyButton(panel.Find("ResumeBtn"), resumeButtonText, resumeButtonPosition);
        ApplyButton(panel.Find("TitleBtn"), titleButtonText, titleButtonPosition);
        ApplyButton(panel.Find("QuitBtn"), quitButtonText, quitButtonPosition);
    }

    private void ApplyButton(Transform buttonTransform, string text, Vector2 position)
    {
        ApplyRect(buttonTransform, position, buttonSize);
        if (buttonTransform == null) return;

        Text buttonLabel = buttonTransform.GetComponentInChildren<Text>(true);
        ApplyTextComponent(buttonLabel, text, buttonFontSize);
    }

    private void ApplyText(
        Transform textTransform,
        string text,
        int fontSize,
        Vector2 position,
        Vector2 size)
    {
        ApplyRect(textTransform, position, size);
        Text label = textTransform != null ? textTransform.GetComponent<Text>() : null;
        ApplyTextComponent(label, text, fontSize);
    }

    private void ApplyTextComponent(Text label, string text, int fontSize)
    {
        if (label == null) return;

        label.text = text;
        label.fontSize = fontSize;
        if (menuFont != null) label.font = menuFont;
    }

    private static void ApplyRect(Transform target, Vector2 position, Vector2 size)
    {
        RectTransform rect = target as RectTransform;
        if (rect == null) return;

        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private void OnDrawGizmos()
    {
        Matrix4x4 previousMatrix = Gizmos.matrix;
        Color previousColor = Gizmos.color;

        Vector2 previewWorldSize = windowSize * runtimeMenuScale;
        Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
        Gizmos.color = new Color(0.15f, 0.9f, 1f, 1f);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(previewWorldSize.x, previewWorldSize.y, 0.01f));

        Vector2 resolvedTitleSize = titleSize;
        if (Mathf.Approximately(resolvedTitleSize.x, 0f))
        {
            resolvedTitleSize.x = windowSize.x;
        }

        // Preview the editable UI layout without entering Play Mode.
        DrawPreviewRect(
            new Vector2(titlePosition.x, windowSize.y * 0.5f + titlePosition.y),
            resolvedTitleSize,
            new Color(0.95f, 0.8f, 0.2f, 1f));
        DrawPreviewRect(volumeLabelPosition, volumeLabelSize, new Color(0.5f, 1f, 0.5f, 1f));
        DrawPreviewRect(sliderPosition, sliderSize, new Color(0.5f, 1f, 0.5f, 1f));
        DrawPreviewRect(resumeButtonPosition, buttonSize, new Color(1f, 0.55f, 0.2f, 1f));
        DrawPreviewRect(titleButtonPosition, buttonSize, new Color(1f, 0.55f, 0.2f, 1f));
        DrawPreviewRect(quitButtonPosition, buttonSize, new Color(1f, 0.55f, 0.2f, 1f));

        // The line indicates the front direction of the generated Canvas.
        Gizmos.color = new Color(1f, 0.75f, 0.1f, 1f);
        Gizmos.DrawLine(Vector3.zero, Vector3.forward * 0.25f);
        Gizmos.DrawSphere(Vector3.forward * 0.25f, 0.025f);

        Gizmos.matrix = previousMatrix;
        Gizmos.color = previousColor;

#if UNITY_EDITOR
        UnityEditor.Handles.Label(
            transform.position + transform.up * (previewWorldSize.y * 0.55f),
            "Persistent Settings Menu");
#endif
    }

    private void DrawPreviewRect(Vector2 pixelPosition, Vector2 pixelSize, Color color)
    {
        Gizmos.color = color;
        Vector3 localPosition = new Vector3(
            pixelPosition.x * runtimeMenuScale,
            pixelPosition.y * runtimeMenuScale,
            0f);
        Vector3 localSize = new Vector3(
            pixelSize.x * runtimeMenuScale,
            pixelSize.y * runtimeMenuScale,
            0.012f);
        Gizmos.DrawWireCube(localPosition, localSize);
    }
}
