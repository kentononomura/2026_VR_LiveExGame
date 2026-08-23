using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

public class VRPhotoViewer : MonoBehaviour
{
    [Header("Default Settings")]
    [SerializeField] private Texture2D noPhotoTexture;

    [Header("VR Score UI Settings")]
    [Tooltip("If the text appears mirrored in VR, check this box.")]
    [SerializeField] private bool flipUIText = false;
    
    [Header("Positions & Sizes")]
    [Tooltip("いいね表示行のCanvas内位置です。")]
    [SerializeField] private Vector2 scoreTextPosition = new Vector2(0, 50);

    [Tooltip("アイコン・ラベル・数値を含む、いいね表示行全体のサイズです。")]
    [SerializeField] private Vector2 scoreRowSize = new Vector2(1000f, 200f);

    [Tooltip("カウントアップ数値を配置する領域のサイズです。")]
    [SerializeField] private Vector2 scoreNumberAreaSize = new Vector2(320f, 200f);

    [SerializeField] private Vector2 rankTextPosition = new Vector2(0, 100);
    [SerializeField] private Vector2 detailsTextPosition = new Vector2(20, -20);
    [Tooltip("Size of the RectTransform for the details text")]
    [SerializeField] private Vector2 detailsTextSize = new Vector2(500, 300);
    
    [Header("Font Sizes")]
    [Tooltip("カウントアップするいいね数の文字サイズです。")]
    [SerializeField] private int scoreFontSize = 120;
    [SerializeField] private int rankFontSize = 300;
    [SerializeField] private int detailsFontSize = 60;

    [Header("Score Decoration")]
    [Tooltip("カウントアップ数値の左側に表示するアイコンです。未設定の場合はアイコンを表示しません。")]
    [SerializeField] private Sprite scoreIcon;

    [Tooltip("アイコンと数値の間に表示する文字です。空欄の場合は文字を表示しません。")]
    [SerializeField] private string scoreLabel = "PHOTO SCORE";

    [Tooltip("スコアアイコンの表示サイズです。")]
    [SerializeField] private Vector2 scoreIconSize = new Vector2(100f, 100f);

    [Tooltip("数値左側の文字サイズです。")]
    [SerializeField] private int scoreLabelFontSize = 64;

    [Tooltip("アイコン横の文字とカウントアップ数値に使用するTMP Font Assetです。日本語を表示する場合は日本語対応フォントを指定してください。")]
    [SerializeField] private TMP_FontAsset scoreFontAsset;

    [Tooltip("アイコン・文字・数値の間隔です。")]
    [Min(0f)]
    [SerializeField] private float scoreElementSpacing = 24f;

    [Tooltip("写真評価点を表示用のいいね数へ変換する倍率です。100なら評価80点を8,000と表示します。")]
    [Min(1)]
    [SerializeField] private int scoreDisplayMultiplier = 100;

    [Header("Score Colors")]
    [Tooltip("いいねラベルの通常色です。")]
    [SerializeField] private Color scoreLabelColor = new Color(1f, 0.25f, 0.65f, 1f);

    [Tooltip("いいねラベルのアウトライン色です。")]
    [SerializeField] private Color scoreLabelOutlineColor = Color.white;

    [Tooltip("カウントアップ中の数値の通常色です。")]
    [SerializeField] private Color scoreNumberColor = Color.yellow;

    [Tooltip("目標いいね数へ到達した瞬間の数値色です。")]
    [SerializeField] private Color scoreHighlightColor = new Color(1f, 0.85f, 0.15f, 1f);

    [Tooltip("文字アウトラインの色です。")]
    [SerializeField] private Color scoreOutlineColor = Color.black;

    [Tooltip("いいね数のアウトライン幅です。")]
    [Range(0f, 1f)]
    [SerializeField] private float scoreOutlineWidth = 0.2f;

    [Tooltip("いいねラベルのアウトライン幅です。")]
    [Range(0f, 1f)]
    [SerializeField] private float scoreLabelOutlineWidth = 0.15f;

#if UNITY_EDITOR
    [Header("Scene Preview")]
    [Tooltip("SceneビューでいいねUIのレイアウト枠を表示します。")]
    [SerializeField] private bool showScoreLayoutPreview = true;
#endif

    [Header("Score Count-Up Sound")]
    [Tooltip("写真評価スコアのカウントアップ中に鳴らすループ音です。未設定でもアニメーションは動作します。")]
    [SerializeField] private AudioClip scoreCountLoopClip;

    [Tooltip("写真評価スコアへ到達した瞬間に鳴らす決定音です。未設定でもアニメーションは動作します。")]
    [SerializeField] private AudioClip scoreReachedClip;

    private Renderer screenRenderer;
    private List<PhotoData> photos;
    private int currentPhotoIndex = 0;

    private InputAction nextAction;
    private InputAction prevAction;

    private Canvas scoreCanvas;
    private TextMeshProUGUI vrScoreText;
    private UnityEngine.UI.Text vrRankText;
    private UnityEngine.UI.Text vrDetailsText;
    private CountUpUI scoreCountUpUI;

    private void Awake()
    {
        screenRenderer = GetComponent<Renderer>();
        SetupVRUI();
    }

    private void SetupVRUI()
    {
        // Create a child Canvas for World Space UI
        GameObject canvasObj = new GameObject("VRScoreCanvas");
        canvasObj.transform.SetParent(transform, false);
        canvasObj.transform.localPosition = new Vector3(0, 0, -0.001f); // slightly in front to prevent z-fighting
        
        // Use the flipUIText variable to determine rotation
        canvasObj.transform.localRotation = flipUIText ? Quaternion.Euler(0, 180, 0) : Quaternion.identity;
        
        scoreCanvas = canvasObj.AddComponent<Canvas>();
        scoreCanvas.renderMode = RenderMode.WorldSpace;
        
        RectTransform rt = canvasObj.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(1000, 1000); 
        rt.localScale = new Vector3(0.001f, 0.001f, 0.001f); 

        // Add Rank Text
        GameObject rankObj = new GameObject("RankText");
        rankObj.transform.SetParent(canvasObj.transform, false);
        vrRankText = rankObj.AddComponent<UnityEngine.UI.Text>();
        vrRankText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        vrRankText.fontSize = rankFontSize;
        vrRankText.fontStyle = FontStyle.Bold;
        vrRankText.alignment = TextAnchor.MiddleCenter;
        vrRankText.color = Color.red;
        
        UnityEngine.UI.Outline out1 = rankObj.AddComponent<UnityEngine.UI.Outline>();
        out1.effectColor = Color.white;
        out1.effectDistance = new Vector2(4, -4);
        
        RectTransform rankRt = rankObj.GetComponent<RectTransform>();
        rankRt.anchoredPosition = rankTextPosition; // Use Inspector value
        rankRt.sizeDelta = new Vector2(800, 400);
        rankRt.localRotation = Quaternion.Euler(0, 0, 15f);

        // Add an automatically aligned row: Icon | Label | Animated Score
        GameObject scoreRowObj = new GameObject(
            "ScoreRow",
            typeof(RectTransform),
            typeof(UnityEngine.UI.HorizontalLayoutGroup));
        scoreRowObj.transform.SetParent(canvasObj.transform, false);

        RectTransform scoreRowRt = scoreRowObj.GetComponent<RectTransform>();
        scoreRowRt.anchorMin = new Vector2(0.5f, 0f);
        scoreRowRt.anchorMax = new Vector2(0.5f, 0f);
        scoreRowRt.pivot = new Vector2(0.5f, 0.5f);
        scoreRowRt.anchoredPosition = scoreTextPosition;
        scoreRowRt.sizeDelta = scoreRowSize;

        UnityEngine.UI.HorizontalLayoutGroup scoreLayout =
            scoreRowObj.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        scoreLayout.childAlignment = TextAnchor.MiddleCenter;
        scoreLayout.spacing = scoreElementSpacing;
        scoreLayout.childControlWidth = false;
        scoreLayout.childControlHeight = false;
        scoreLayout.childForceExpandWidth = false;
        scoreLayout.childForceExpandHeight = false;

        if (scoreIcon != null)
        {
            GameObject iconObj = new GameObject(
                "ScoreIcon",
                typeof(RectTransform),
                typeof(UnityEngine.UI.Image),
                typeof(UnityEngine.UI.LayoutElement));
            iconObj.transform.SetParent(scoreRowObj.transform, false);

            UnityEngine.UI.Image iconImage = iconObj.GetComponent<UnityEngine.UI.Image>();
            iconImage.sprite = scoreIcon;
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;

            UnityEngine.UI.LayoutElement iconLayout =
                iconObj.GetComponent<UnityEngine.UI.LayoutElement>();
            iconLayout.preferredWidth = scoreIconSize.x;
            iconLayout.preferredHeight = scoreIconSize.y;

            RectTransform iconRt = iconObj.GetComponent<RectTransform>();
            iconRt.sizeDelta = scoreIconSize;
        }

        if (!string.IsNullOrWhiteSpace(scoreLabel))
        {
            GameObject labelObj = new GameObject(
                "ScoreLabel",
                typeof(RectTransform),
                typeof(TextMeshProUGUI),
                typeof(UnityEngine.UI.LayoutElement));
            labelObj.transform.SetParent(scoreRowObj.transform, false);

            TextMeshProUGUI labelText = labelObj.GetComponent<TextMeshProUGUI>();
            if (scoreFontAsset != null)
            {
                labelText.font = scoreFontAsset;
            }

            labelText.text = scoreLabel;
            labelText.fontSize = scoreLabelFontSize;
            labelText.fontStyle = FontStyles.Bold;
            labelText.alignment = TextAlignmentOptions.MidlineRight;
            labelText.color = scoreLabelColor;
            labelText.textWrappingMode = TextWrappingModes.NoWrap;
            labelText.raycastTarget = false;
            labelText.outlineColor = scoreLabelOutlineColor;
            labelText.outlineWidth = scoreLabelOutlineWidth;

            Vector2 labelSize = labelText.GetPreferredValues(
                scoreLabel,
                Mathf.Infinity,
                scoreRowRt.rect.height);
            UnityEngine.UI.LayoutElement labelLayout =
                labelObj.GetComponent<UnityEngine.UI.LayoutElement>();
            labelLayout.preferredWidth = labelSize.x;
            labelLayout.preferredHeight = scoreRowRt.rect.height;

            RectTransform labelRt = labelObj.GetComponent<RectTransform>();
            labelRt.sizeDelta = new Vector2(labelSize.x, scoreRowRt.rect.height);
        }

        // The layout controls this slot; CountUpUI shakes only its child text.
        GameObject scoreSlotObj = new GameObject(
            "ScoreSlot",
            typeof(RectTransform),
            typeof(UnityEngine.UI.LayoutElement));
        scoreSlotObj.transform.SetParent(scoreRowObj.transform, false);
        UnityEngine.UI.LayoutElement scoreSlotLayout =
            scoreSlotObj.GetComponent<UnityEngine.UI.LayoutElement>();
        scoreSlotLayout.preferredWidth = scoreNumberAreaSize.x;
        scoreSlotLayout.preferredHeight = scoreNumberAreaSize.y;
        RectTransform scoreSlotRt = scoreSlotObj.GetComponent<RectTransform>();
        scoreSlotRt.sizeDelta = scoreNumberAreaSize;

        GameObject scoreObj = new GameObject("ScoreText", typeof(RectTransform));
        scoreObj.transform.SetParent(scoreSlotObj.transform, false);
        vrScoreText = scoreObj.AddComponent<TextMeshProUGUI>();
        if (scoreFontAsset != null)
        {
            vrScoreText.font = scoreFontAsset;
        }

        vrScoreText.fontSize = scoreFontSize;
        vrScoreText.fontStyle = FontStyles.Bold;
        vrScoreText.alignment = TextAlignmentOptions.Center;
        vrScoreText.color = scoreNumberColor;
        vrScoreText.enableAutoSizing = false;
        vrScoreText.outlineColor = scoreOutlineColor;
        vrScoreText.outlineWidth = scoreOutlineWidth;
        
        RectTransform scoreRt = scoreObj.GetComponent<RectTransform>();
        scoreRt.anchorMin = Vector2.zero;
        scoreRt.anchorMax = Vector2.one;
        scoreRt.offsetMin = Vector2.zero;
        scoreRt.offsetMax = Vector2.zero;

        AudioSource loopAudioSource = scoreObj.AddComponent<AudioSource>();
        loopAudioSource.playOnAwake = false;
        loopAudioSource.spatialBlend = 0f;

        AudioSource reachedAudioSource = scoreObj.AddComponent<AudioSource>();
        reachedAudioSource.playOnAwake = false;
        reachedAudioSource.spatialBlend = 0f;

        scoreCountUpUI = scoreObj.AddComponent<CountUpUI>();
        scoreCountUpUI.Configure(
            vrScoreText,
            loopAudioSource,
            scoreCountLoopClip,
            reachedAudioSource,
            scoreReachedClip);
        scoreCountUpUI.ConfigureHighlightColor(scoreHighlightColor);

        // Add Details Text (Score Breakdown)
        GameObject detailsObj = new GameObject("DetailsText");
        detailsObj.transform.SetParent(canvasObj.transform, false);
        vrDetailsText = detailsObj.AddComponent<UnityEngine.UI.Text>();
        vrDetailsText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        vrDetailsText.fontSize = detailsFontSize;
        vrDetailsText.alignment = TextAnchor.UpperLeft;
        vrDetailsText.color = Color.white;
        
        UnityEngine.UI.Outline out3 = detailsObj.AddComponent<UnityEngine.UI.Outline>();
        out3.effectColor = Color.black;
        out3.effectDistance = new Vector2(2, -2);
        
        RectTransform detailsRt = detailsObj.GetComponent<RectTransform>();
        detailsRt.anchorMin = new Vector2(0, 1); // Top Left anchor
        detailsRt.anchorMax = new Vector2(0, 1);
        detailsRt.pivot = new Vector2(0, 1);     // Top Left pivot
        detailsRt.anchoredPosition = detailsTextPosition; // Use Inspector value
        detailsRt.sizeDelta = detailsTextSize; // Use Inspector value

        scoreCanvas.gameObject.SetActive(false);
    }

    private void Start()
    {
        // Fade in when scene starts
        if (VRScreenFader.Instance != null)
        {
            VRScreenFader.Instance.FadeIn(1.0f, null);
        }

        photos = PhotoGalleryManager.GetPhotos();
        UpdateScreenTexture();
    }

    private bool isRightTriggerDown = false;
    private bool isLeftTriggerDown = false;

    private void OnEnable()
    {
        // Setup direct path bindings for triggers (reading as float axis)
        nextAction = new InputAction(
            name: "NextPhoto",
            type: InputActionType.Value,
            expectedControlType: "Axis",
            binding: "<XRController>{RightHand}/trigger"
        );
        nextAction.Enable();
        nextAction.performed += OnNextPressed;
        nextAction.canceled += OnNextPressed;

        prevAction = new InputAction(
            name: "PrevPhoto",
            type: InputActionType.Value,
            expectedControlType: "Axis",
            binding: "<XRController>{LeftHand}/trigger"
        );
        prevAction.Enable();
        prevAction.performed += OnPrevPressed;
        prevAction.canceled += OnPrevPressed;
    }

    private void OnDisable()
    {
        if (nextAction != null)
        {
            nextAction.performed -= OnNextPressed;
            nextAction.canceled -= OnNextPressed;
            nextAction.Disable();
        }

        if (prevAction != null)
        {
            prevAction.performed -= OnPrevPressed;
            prevAction.canceled -= OnPrevPressed;
            prevAction.Disable();
        }
    }

    private void OnNextPressed(InputAction.CallbackContext context)
    {
        if (VRPauseMenu.IsGamePaused()) return;
        float val = context.ReadValue<float>();
        if (val >= 0.8f)
        {
            if (!isRightTriggerDown)
            {
                isRightTriggerDown = true;
                if (photos == null || photos.Count <= 1) return;
                currentPhotoIndex = (currentPhotoIndex + 1) % photos.Count;
                UpdateScreenTexture();
            }
        }
        else if (val < 0.2f)
        {
            isRightTriggerDown = false;
        }
    }

    private void OnPrevPressed(InputAction.CallbackContext context)
    {
        if (VRPauseMenu.IsGamePaused()) return;
        float val = context.ReadValue<float>();
        if (val >= 0.8f)
        {
            if (!isLeftTriggerDown)
            {
                isLeftTriggerDown = true;
                if (photos == null || photos.Count <= 1) return;
                if (currentPhotoIndex == 0) return;
                currentPhotoIndex--;
                UpdateScreenTexture();
            }
        }
        else if (val < 0.2f)
        {
            isLeftTriggerDown = false;
        }
    }

    private void UpdateScreenTexture()
    {
        if (screenRenderer == null) return;

        Texture2D textureToApply = noPhotoTexture;

        if (photos != null && photos.Count > 0 && currentPhotoIndex < photos.Count)
        {
            var p = photos[currentPhotoIndex];
            if (p != null && p.Texture != null) textureToApply = p.Texture;

            if (p != null && scoreCanvas != null)
            {
                scoreCanvas.gameObject.SetActive(true);
                vrRankText.text = p.Rank;
                if (p.Rank == "S") vrRankText.color = new Color(1f, 0.8f, 0f); // Gold
                else if (p.Rank == "A") vrRankText.color = Color.red;
                else if (p.Rank == "B") vrRankText.color = Color.green;
                else vrRankText.color = Color.blue;

                int displayedLikeCount = p.TotalScore * scoreDisplayMultiplier;
                scoreCountUpUI.PlayCountUp(displayedLikeCount);
                
                // Set the breakdown text
                vrDetailsText.text = $"Center: +{p.CenterBonus}\n" +
                                     $"Gaze: +{p.GazeBonus}\n" +
                                     $"Pose: +{p.PoseBonus}";
            }
            else if (scoreCanvas != null)
            {
                scoreCanvas.gameObject.SetActive(false);
            }
        }
        else if (scoreCanvas != null)
        {
            scoreCanvas.gameObject.SetActive(false);
        }

        if (textureToApply != null)
        {
            // Apply to both URP standard (_BaseMap) and legacy shader (_MainTex) slots
            screenRenderer.material.SetTexture("_BaseMap", textureToApply);
            screenRenderer.material.SetTexture("_MainTex", textureToApply);
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!showScoreLayoutPreview) return;

        Matrix4x4 previousMatrix = Gizmos.matrix;
        Color previousColor = Gizmos.color;
        Gizmos.matrix = transform.localToWorldMatrix;

        const float canvasScale = 0.001f;
        Vector3 rowCenter = new Vector3(
            scoreTextPosition.x * canvasScale,
            (-500f + scoreTextPosition.y) * canvasScale,
            -0.002f);

        DrawScorePreviewRect(
            rowCenter,
            scoreRowSize * canvasScale,
            new Color(0.1f, 0.9f, 1f, 1f));

        float iconWidth = scoreIcon != null ? scoreIconSize.x : 0f;
        float estimatedLabelWidth = string.IsNullOrWhiteSpace(scoreLabel)
            ? 0f
            : scoreLabel.Length * scoreLabelFontSize * 0.55f;
        int visibleElementCount =
            1 + (scoreIcon != null ? 1 : 0) + (!string.IsNullOrWhiteSpace(scoreLabel) ? 1 : 0);
        float totalWidth =
            iconWidth + estimatedLabelWidth + scoreNumberAreaSize.x +
            Mathf.Max(0, visibleElementCount - 1) * scoreElementSpacing;
        float cursor = -totalWidth * 0.5f;

        if (scoreIcon != null)
        {
            Vector3 iconCenter = rowCenter + Vector3.right *
                ((cursor + scoreIconSize.x * 0.5f) * canvasScale);
            DrawScorePreviewRect(
                iconCenter,
                scoreIconSize * canvasScale,
                new Color(0.4f, 1f, 0.4f, 1f));
            cursor += scoreIconSize.x + scoreElementSpacing;
        }

        if (!string.IsNullOrWhiteSpace(scoreLabel))
        {
            Vector2 labelPreviewSize = new Vector2(estimatedLabelWidth, scoreRowSize.y);
            Vector3 labelCenter = rowCenter + Vector3.right *
                ((cursor + estimatedLabelWidth * 0.5f) * canvasScale);
            DrawScorePreviewRect(
                labelCenter,
                labelPreviewSize * canvasScale,
                new Color(1f, 0.8f, 0.2f, 1f));
            cursor += estimatedLabelWidth + scoreElementSpacing;
        }

        Vector3 numberCenter = rowCenter + Vector3.right *
            ((cursor + scoreNumberAreaSize.x * 0.5f) * canvasScale);
        DrawScorePreviewRect(
            numberCenter,
            scoreNumberAreaSize * canvasScale,
            new Color(1f, 0.35f, 0.75f, 1f));

        UnityEditor.Handles.Label(
            transform.TransformPoint(rowCenter + Vector3.up * scoreRowSize.y * canvasScale * 0.6f),
            "Photo Like Count UI");

        Gizmos.matrix = previousMatrix;
        Gizmos.color = previousColor;
    }

    private static void DrawScorePreviewRect(Vector3 center, Vector2 size, Color color)
    {
        Gizmos.color = color;
        Gizmos.DrawWireCube(center, new Vector3(size.x, size.y, 0.01f));
    }
#endif
}
