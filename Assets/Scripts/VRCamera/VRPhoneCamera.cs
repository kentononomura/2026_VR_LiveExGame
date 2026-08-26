using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class VRPhoneCamera : MonoBehaviour
{
    [Header("Camera & RenderTexture Settings")]
    [SerializeField] private Camera viewfinderCamera;
    [SerializeField] private RenderTexture viewfinderTexture;
    [SerializeField] private Renderer screenRenderer;

    [Header("Orientation Settings")]
    [Tooltip("カメラがこの角度（度）以上傾いたら横長(Landscape)写真として自動回転して保存します。")]
    [SerializeField] private float landscapeTiltThreshold = 45f;

    [Tooltip("縦横判定の境界でUIが細かく切り替わるのを防ぐ角度です。")]
    [Range(0f, 15f)]
    [SerializeField] private float orientationHysteresis = 5f;

    [Tooltip("横向き時にズーム倍率をプレイヤーから正立して読める向きへ回転します。")]
    [SerializeField] private bool rotateZoomTextInLandscape = true;

    [Header("Input Settings")]
    [SerializeField] private InputActionProperty shootAction;

    [Header("Zoom Settings")]
    [SerializeField] private float maxZoomRatio = 1.5f;
    [SerializeField] private float zoomSpeed = 1.0f;
    [Tooltip("If the UI is mirrored or on the wrong side, toggle this.")]
    [SerializeField] private bool flipZoomUI = false;

    [Header("Zoom UI Layout Settings")]
    [SerializeField] private float sliderWidth = 40f;
    [Tooltip("Distance from the right edge of the screen")]
    [SerializeField] private float sliderXOffset = -20f;
    [SerializeField] private int zoomTextFontSize = 60;

    [Tooltip("縦向き時のズーム倍率表示位置です。ZoomSliderを基準とした座標です。")]
    [SerializeField] private Vector2 portraitZoomTextPosition = new Vector2(0f, -20f);

    [Tooltip("横向き時のズーム倍率表示位置です。ZoomSliderを基準とした座標で、Xを小さくするとシークバーの左側へ移動します。")]
    [SerializeField] private Vector2 landscapeZoomTextPosition = new Vector2(-100f, -20f);

    [Header("Effects")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip shutterSound;
    [SerializeField] private Material flashMaterial;
    [SerializeField] private float flashDuration = 0.15f;

    private Material originalScreenMaterial;
    private bool isFlashActive = false;
    private bool isTriggerDown = false;

    // Zoom state
    private float currentZoomLevel = 1.0f;
    private float defaultFOV = 60f;
    private InputAction zoomInputAction;

    // UI state
    private Canvas uiCanvas;
    private UnityEngine.UI.Slider zoomSlider;
    private UnityEngine.UI.Text zoomText;
    private RectTransform zoomTextRect;
    private bool isLandscape;
    private int landscapeDirection;

    private void Awake()
    {
        if (screenRenderer != null)
        {
            originalScreenMaterial = screenRenderer.sharedMaterial;
        }

        if (viewfinderCamera != null)
        {
            defaultFOV = viewfinderCamera.fieldOfView;
            if (viewfinderTexture != null && viewfinderTexture.height > 0)
            {
                // RenderTextureとカメラ投影の縦横比を明示的に一致させる。
                viewfinderCamera.aspect =
                    viewfinderTexture.width / (float)viewfinderTexture.height;
            }
        }

        SetupZoomUI();
    }

    private void SetupZoomUI()
    {
        if (screenRenderer == null) return;

        // Create Canvas
        GameObject canvasObj = new GameObject("CameraUI");
        canvasObj.transform.SetParent(screenRenderer.transform, false);
        canvasObj.transform.localPosition = new Vector3(0, 0, -0.001f);
        
        // Fix for mirrored UI: Allow toggling rotation between 0 and 180
        canvasObj.transform.localRotation = flipZoomUI ? Quaternion.Euler(0, 180, 0) : Quaternion.identity;
        
        uiCanvas = canvasObj.AddComponent<Canvas>();
        uiCanvas.renderMode = RenderMode.WorldSpace;
        
        RectTransform canvasRt = canvasObj.GetComponent<RectTransform>();
        canvasRt.sizeDelta = new Vector2(1000, 1000); 
        canvasRt.localScale = new Vector3(0.001f, 0.001f, 0.001f); 

        // Create Slider Container
        GameObject sliderObj = new GameObject("ZoomSlider");
        sliderObj.transform.SetParent(canvasObj.transform, false);
        zoomSlider = sliderObj.AddComponent<UnityEngine.UI.Slider>();
        zoomSlider.direction = UnityEngine.UI.Slider.Direction.BottomToTop;
        zoomSlider.minValue = 1.0f;
        zoomSlider.maxValue = maxZoomRatio;
        zoomSlider.value = currentZoomLevel;
        zoomSlider.interactable = false; // Display only
        
        RectTransform sliderRt = sliderObj.GetComponent<RectTransform>();
        sliderRt.anchorMin = new Vector2(1, 0.2f);
        sliderRt.anchorMax = new Vector2(1, 0.8f);
        sliderRt.pivot = new Vector2(1, 0.5f);
        sliderRt.anchoredPosition = new Vector2(sliderXOffset, 0); // Use Inspector value
        sliderRt.sizeDelta = new Vector2(sliderWidth, 0); // Use Inspector value

        // Background
        GameObject bgObj = new GameObject("Background");
        bgObj.transform.SetParent(sliderObj.transform, false);
        var bgImg = bgObj.AddComponent<UnityEngine.UI.Image>();
        bgImg.color = new Color(0, 0, 0, 0.5f);
        RectTransform bgRt = bgObj.GetComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
        bgRt.sizeDelta = Vector2.zero;

        // Fill Area
        GameObject fillArea = new GameObject("Fill Area");
        fillArea.transform.SetParent(sliderObj.transform, false);
        RectTransform fillAreaRt = fillArea.AddComponent<RectTransform>();
        fillAreaRt.anchorMin = Vector2.zero; fillAreaRt.anchorMax = Vector2.one;
        fillAreaRt.sizeDelta = Vector2.zero;

        // Fill
        GameObject fillObj = new GameObject("Fill");
        fillObj.transform.SetParent(fillArea.transform, false);
        var fillImg = fillObj.AddComponent<UnityEngine.UI.Image>();
        fillImg.color = Color.white;
        RectTransform fillRt = fillObj.GetComponent<RectTransform>();
        zoomSlider.fillRect = fillRt;

        // Zoom Text
        GameObject textObj = new GameObject("ZoomText");
        textObj.transform.SetParent(sliderObj.transform, false);
        zoomText = textObj.AddComponent<UnityEngine.UI.Text>();
        zoomText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        zoomText.fontSize = zoomTextFontSize; // Use Inspector value
        zoomText.alignment = TextAnchor.MiddleCenter;
        zoomText.color = Color.white;
        
        UnityEngine.UI.Outline outline = textObj.AddComponent<UnityEngine.UI.Outline>();
        outline.effectColor = Color.black;
        outline.effectDistance = new Vector2(2, -2);
        
        zoomTextRect = textObj.GetComponent<RectTransform>();
        zoomTextRect.anchorMin = new Vector2(0.5f, 0);
        zoomTextRect.anchorMax = new Vector2(0.5f, 0);
        zoomTextRect.pivot = new Vector2(0.5f, 1);
        zoomTextRect.anchoredPosition = portraitZoomTextPosition;
        zoomTextRect.sizeDelta = new Vector2(200, 100);
        
        UpdateZoomUI();
        UpdateOrientationUI(true);
    }

    private void OnEnable()
    {
        if (shootAction != null && shootAction.action != null)
        {
            shootAction.action.Enable();
            shootAction.action.performed += OnShootPerformed;
        }

        // Setup Zoom Action manually for Right Thumbstick Y
        zoomInputAction = new InputAction(
            name: "Zoom",
            type: InputActionType.Value,
            expectedControlType: "Axis",
            binding: "<XRController>{RightHand}/thumbstick/y"
        );
        zoomInputAction.Enable();
    }

    private void OnDisable()
    {
        if (shootAction != null && shootAction.action != null)
        {
            shootAction.action.performed -= OnShootPerformed;
            shootAction.action.Disable();
        }

        if (zoomInputAction != null)
        {
            zoomInputAction.Disable();
        }
    }

    private void Update()
    {
        HandleZoom();
        UpdateOrientationUI(false);
    }

    private void UpdateOrientationUI(bool force)
    {
        if (zoomTextRect == null) return;

        float rollAngle = GetSignedRollAngle();
        float absoluteRoll = Mathf.Abs(rollAngle);
        float hysteresis = Mathf.Max(0f, orientationHysteresis);
        float enterThreshold = Mathf.Clamp(landscapeTiltThreshold + hysteresis, 0f, 89.9f);
        float exitThreshold = Mathf.Clamp(landscapeTiltThreshold - hysteresis, 0f, 89.9f);

        bool nextLandscape = isLandscape
            ? absoluteRoll >= exitThreshold && absoluteRoll <= 180f - exitThreshold
            : absoluteRoll >= enterThreshold && absoluteRoll <= 180f - enterThreshold;
        int nextDirection = nextLandscape ? (rollAngle >= 0f ? 1 : -1) : 0;

        if (!force && nextLandscape == isLandscape && nextDirection == landscapeDirection)
        {
            return;
        }

        isLandscape = nextLandscape;
        landscapeDirection = nextDirection;

        zoomTextRect.anchoredPosition = isLandscape
            ? landscapeZoomTextPosition
            : portraitZoomTextPosition;

        float textRotation = 0f;
        if (rotateZoomTextInLandscape && isLandscape)
        {
            // 端末のRollをUI側で打ち消し、横持ち中も倍率を正立表示する。
            textRotation = landscapeDirection > 0 ? -90f : 90f;
            if (flipZoomUI) textRotation = -textRotation;
        }
        zoomTextRect.localRotation = Quaternion.Euler(0f, 0f, textRotation);
    }

    private float GetSignedRollAngle()
    {
        float rollAngle = transform.eulerAngles.z;
        return rollAngle > 180f ? rollAngle - 360f : rollAngle;
    }

    private void HandleZoom()
    {
        if (VRPauseMenu.IsGamePaused()) return;
        if (zoomInputAction == null || viewfinderCamera == null) return;

        float zoomInput = zoomInputAction.ReadValue<float>();
        
        if (Mathf.Abs(zoomInput) > 0.1f)
        {
            // Up is positive (zoom in), Down is negative (zoom out)
            currentZoomLevel += zoomInput * zoomSpeed * Time.deltaTime;
            currentZoomLevel = Mathf.Clamp(currentZoomLevel, 1.0f, maxZoomRatio);

            // Calculate FOV. Higher zoom level = smaller FOV
            viewfinderCamera.fieldOfView = defaultFOV / currentZoomLevel;
            
            UpdateZoomUI();
        }
    }

    private void UpdateZoomUI()
    {
        if (zoomSlider != null) zoomSlider.value = currentZoomLevel;
        if (zoomText != null) zoomText.text = $"{currentZoomLevel:F1}x";
    }

    private void OnShootPerformed(InputAction.CallbackContext context)
    {
        if (VRPauseMenu.IsGamePaused()) return;
        // Try reading as a float value to handle analog triggers with hysteresis
        float triggerValue = 0f;
        try
        {
            triggerValue = context.ReadValue<float>();
        }
        catch
        {
            // Fallback for digital button bindings
            triggerValue = context.ReadValueAsButton() ? 1f : 0f;
        }

        // Hysteretic threshold: triggers on pressing (>= 0.8), resets on releasing (< 0.2)
        if (triggerValue >= 0.8f)
        {
            if (!isTriggerDown)
            {
                isTriggerDown = true;
                CapturePhoto();
            }
        }
        else if (triggerValue < 0.2f)
        {
            isTriggerDown = false;
        }
    }

    [ContextMenu("Capture Photo")]
    public void CapturePhoto()
    {
        if (viewfinderCamera == null || viewfinderTexture == null)
        {
            Debug.LogError("VRPhoneCamera: Viewfinder Camera or RenderTexture reference is missing!");
            return;
        }

        // 1. Play shutter sound
        if (audioSource != null && shutterSound != null)
        {
            audioSource.PlayOneShot(shutterSound);
        }

        // 2. Play flash effect
        if (!isFlashActive)
        {
            StartCoroutine(FlashRoutine());
        }

        // 3. Capture image from RenderTexture
        StartCoroutine(CapturePixelsRoutine());
    }

    private IEnumerator FlashRoutine()
    {
        isFlashActive = true;
        if (screenRenderer != null && flashMaterial != null)
        {
            screenRenderer.material = flashMaterial;
            yield return new WaitForSeconds(flashDuration);
            screenRenderer.material = originalScreenMaterial;
        }
        isFlashActive = false;
    }

    private IEnumerator CapturePixelsRoutine()
    {
        // Wait for end of frame to ensure RenderTexture is fully rendered
        yield return new WaitForEndOfFrame();

        RenderTexture.active = viewfinderTexture;
        Texture2D photo = new Texture2D(viewfinderTexture.width, viewfinderTexture.height, TextureFormat.RGB24, false);
        photo.ReadPixels(new Rect(0, 0, viewfinderTexture.width, viewfinderTexture.height), 0, 0);
        photo.Apply();
        RenderTexture.active = null;

        // --- 自動回転ロジック（重力センサー風の傾き検知） ---
        float zAngle = GetSignedRollAngle();

        if (zAngle >= landscapeTiltThreshold && zAngle <= (180f - landscapeTiltThreshold))
        {
            photo = RotateTexture(photo, false);
        }
        else if (zAngle <= -landscapeTiltThreshold && zAngle >= (-180f + landscapeTiltThreshold))
        {
            photo = RotateTexture(photo, true);
        }

        // Evaluate the photo using PhotoEvaluator
        PhotoData data = PhotoEvaluator.EvaluateScene(viewfinderCamera, photo);

        // Store in static manager
        PhotoGalleryManager.AddPhoto(data);
        Debug.Log($"VRPhoneCamera: Captured photo #{PhotoGalleryManager.GetPhotos().Count} (Z-Angle: {zAngle:F1}) - Score: {data.TotalScore} Rank: {data.Rank}");
    }

    private Texture2D RotateTexture(Texture2D originalTexture, bool clockwise)
    {
        Color32[] original = originalTexture.GetPixels32();
        int w = originalTexture.width;
        int h = originalTexture.height;
        int nw = h; // 新しい幅は元の高さ
        int nh = w; // 新しい高さは元の幅
        Color32[] rotated = new Color32[original.Length];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int originalIndex = y * w + x;
                int X = clockwise ? y : (h - 1 - y);
                int Y = clockwise ? (w - 1 - x) : x;
                rotated[Y * nw + X] = original[originalIndex];
            }
        }

        Texture2D rotatedTexture = new Texture2D(nw, nh, originalTexture.format, false);
        rotatedTexture.SetPixels32(rotated);
        rotatedTexture.Apply();
        
        // メモリリーク防止のため元の回転前テクスチャは破棄する
        Destroy(originalTexture);
        
        return rotatedTexture;
    }
}
