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

    [Header("Input Settings")]
    [SerializeField] private InputActionProperty shootAction;

    [Header("Effects")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip shutterSound;
    [SerializeField] private Material flashMaterial;
    [SerializeField] private float flashDuration = 0.15f;

    private Material originalScreenMaterial;
    private bool isFlashActive = false;
    private bool isTriggerDown = false;

    private void Awake()
    {
        if (screenRenderer != null)
        {
            originalScreenMaterial = screenRenderer.sharedMaterial;
        }
    }

    private void OnEnable()
    {
        shootAction.action.Enable();
        shootAction.action.performed += OnShootPerformed;
    }

    private void OnDisable()
    {
        shootAction.action.performed -= OnShootPerformed;
        shootAction.action.Disable();
    }

    private void OnShootPerformed(InputAction.CallbackContext context)
    {
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
        float zAngle = transform.eulerAngles.z;
        // 0~360度を -180~180度に変換して扱いやすくする
        if (zAngle > 180f) zAngle -= 360f;

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
