using System.Collections.Generic;
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
    [SerializeField] private Vector2 scoreTextPosition = new Vector2(0, 50);
    [SerializeField] private Vector2 rankTextPosition = new Vector2(0, 100);
    [SerializeField] private Vector2 detailsTextPosition = new Vector2(20, -20);
    [Tooltip("Size of the RectTransform for the details text")]
    [SerializeField] private Vector2 detailsTextSize = new Vector2(500, 300);
    
    [Header("Font Sizes")]
    [SerializeField] private int scoreFontSize = 120;
    [SerializeField] private int rankFontSize = 300;
    [SerializeField] private int detailsFontSize = 60;

    private Renderer screenRenderer;
    private List<PhotoData> photos;
    private int currentPhotoIndex = 0;

    private InputAction nextAction;
    private InputAction prevAction;

    private Canvas scoreCanvas;
    private UnityEngine.UI.Text vrScoreText;
    private UnityEngine.UI.Text vrRankText;
    private UnityEngine.UI.Text vrDetailsText;

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

        // Add Score Text
        GameObject scoreObj = new GameObject("ScoreText");
        scoreObj.transform.SetParent(canvasObj.transform, false);
        vrScoreText = scoreObj.AddComponent<UnityEngine.UI.Text>();
        vrScoreText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        vrScoreText.fontSize = scoreFontSize;
        vrScoreText.alignment = TextAnchor.LowerCenter;
        vrScoreText.color = Color.yellow;
        
        UnityEngine.UI.Outline out2 = scoreObj.AddComponent<UnityEngine.UI.Outline>();
        out2.effectColor = Color.black;
        out2.effectDistance = new Vector2(3, -3);
        
        RectTransform scoreRt = scoreObj.GetComponent<RectTransform>();
        scoreRt.anchorMin = new Vector2(0, 0);
        scoreRt.anchorMax = new Vector2(1, 0);
        scoreRt.anchoredPosition = scoreTextPosition; // Use Inspector value
        scoreRt.sizeDelta = new Vector2(0, 200);

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

                vrScoreText.text = $"Score: {p.TotalScore}";
                
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
}
