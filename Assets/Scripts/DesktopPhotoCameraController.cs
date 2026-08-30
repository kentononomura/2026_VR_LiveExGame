using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// TestScene専用のデスクトップ撮影カメラ。
/// シーンファイルへ撮影用オブジェクトを持たせず、TestScene読込時だけ自動生成する。
/// </summary>
[DefaultExecutionOrder(10000)]
public sealed class DesktopPhotoCameraController : MonoBehaviour
{
    private const string TargetSceneName = "TestScene";
    private const int BaseCaptureWidth = 1920;
    private const int BaseCaptureHeight = 1080;
    private const float CaptureDpi = 72f;

    [Header("Movement")]
    [SerializeField, Min(0.01f)] private float moveSpeed = 2.5f;
    [SerializeField, Min(1f)] private float fastMoveMultiplier = 4f;
    [SerializeField, Min(0.01f)] private float lookSensitivity = 0.15f;
    [SerializeField, Min(0.01f)] private float scrollSpeedStep = 0.5f;

    [Header("Capture")]
    [SerializeField, Range(1, 4)] private int captureScale = 2;

    private readonly Dictionary<Camera, bool> overriddenCameras = new Dictionary<Camera, bool>();
    private readonly Dictionary<AudioListener, bool> overriddenListeners = new Dictionary<AudioListener, bool>();

    private Camera photoCamera;
    private UniversalAdditionalCameraData photoCameraData;
    private AudioListener photoListener;
    private bool photoMode;
    private bool showHelp = true;
    private bool suppressOverlay;
    private bool ownsPause;
    private bool captureInProgress;
    private float timeScaleBeforePause = 1f;
    private float yaw;
    private float pitch;
    private CursorLockMode cursorLockBeforePhotoMode;
    private bool cursorVisibleBeforePhotoMode;
    private string statusMessage = "F8: 撮影カメラを開始";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InstallSceneHook()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateForInitialScene()
    {
        EnsureController(SceneManager.GetActiveScene());
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsureController(scene);
    }

    private static void EnsureController(Scene scene)
    {
        if (!IsDesktopRuntime() ||
            scene.name != TargetSceneName ||
            FindAnyObjectByType<DesktopPhotoCameraController>(FindObjectsInactive.Include) != null)
        {
            return;
        }

        GameObject controllerObject = new GameObject("Desktop Photo Camera");
        SceneManager.MoveGameObjectToScene(controllerObject, scene);
        controllerObject.AddComponent<DesktopPhotoCameraController>();
    }

    private static bool IsDesktopRuntime()
    {
        if (Application.isEditor) return true;

        return Application.platform == RuntimePlatform.WindowsPlayer ||
               Application.platform == RuntimePlatform.OSXPlayer ||
               Application.platform == RuntimePlatform.LinuxPlayer;
    }

    private IEnumerator Start()
    {
        photoCamera = gameObject.AddComponent<Camera>();
        photoCamera.enabled = false;
        photoCamera.stereoTargetEye = StereoTargetEyeMask.None;
        photoCameraData = gameObject.AddComponent<UniversalAdditionalCameraData>();
        photoCameraData.allowXRRendering = false;

        photoListener = gameObject.AddComponent<AudioListener>();
        photoListener.enabled = false;

        // StageDirectorがXRリグを生成し、最初のカメラ位置へ移動するまで待つ。
        yield return null;
        yield return null;
        CopyFromCurrentCamera();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F8))
        {
            SetPhotoMode(!photoMode);
        }

        if (!photoMode) return;

        if (Input.GetKeyDown(KeyCode.H)) showHelp = !showHelp;
        if (Input.GetKeyDown(KeyCode.P)) TogglePause();
        if (Input.GetKeyDown(KeyCode.F12)) StartCoroutine(CaptureScreenshot());

        if (Input.GetKeyDown(KeyCode.Alpha1)) captureScale = 1;
        if (Input.GetKeyDown(KeyCode.Alpha2)) captureScale = 2;
        if (Input.GetKeyDown(KeyCode.Alpha3)) captureScale = 3;
        if (Input.GetKeyDown(KeyCode.Alpha4)) captureScale = 4;

        if (Input.GetKeyDown(KeyCode.LeftBracket))
            photoCamera.fieldOfView = Mathf.Clamp(photoCamera.fieldOfView - 2f, 10f, 100f);
        if (Input.GetKeyDown(KeyCode.RightBracket))
            photoCamera.fieldOfView = Mathf.Clamp(photoCamera.fieldOfView + 2f, 10f, 100f);

        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) > 0.01f)
        {
            moveSpeed = Mathf.Max(0.1f, moveSpeed + scroll * scrollSpeedStep);
        }

        UpdateLook();
        UpdateMovement();
    }

    private void LateUpdate()
    {
        if (photoMode)
        {
            SuppressOtherDisplayCameras();
            SuppressOtherListeners();
        }
    }

    private void UpdateLook()
    {
        if (Input.GetMouseButtonDown(1))
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        if (Input.GetMouseButtonUp(1))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        if (!Input.GetMouseButton(1)) return;

        yaw += Input.GetAxisRaw("Mouse X") * lookSensitivity;
        pitch -= Input.GetAxisRaw("Mouse Y") * lookSensitivity;
        pitch = Mathf.Clamp(pitch, -89f, 89f);
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    private void UpdateMovement()
    {
        Vector3 input = new Vector3(
            Input.GetAxisRaw("Horizontal"),
            0f,
            Input.GetAxisRaw("Vertical"));

        if (Input.GetKey(KeyCode.E)) input.y += 1f;
        if (Input.GetKey(KeyCode.Q)) input.y -= 1f;

        if (input.sqrMagnitude > 1f) input.Normalize();

        float speed = moveSpeed;
        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
        {
            speed *= fastMoveMultiplier;
        }

        Vector3 movement = transform.right * input.x +
                           Vector3.up * input.y +
                           transform.forward * input.z;
        transform.position += movement * speed * Time.unscaledDeltaTime;
    }

    private void SetPhotoMode(bool enabled)
    {
        if (photoCamera == null) return;

        if (enabled)
        {
            CopyFromCurrentCamera();
            cursorLockBeforePhotoMode = Cursor.lockState;
            cursorVisibleBeforePhotoMode = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            photoMode = true;
            photoCamera.enabled = true;
            photoListener.enabled = true;
            SuppressOtherDisplayCameras();
            SuppressOtherListeners();
            statusMessage = "撮影カメラ ON";
        }
        else
        {
            photoMode = false;
            photoCamera.enabled = false;
            photoListener.enabled = false;
            RestoreOverriddenComponents();
            Cursor.lockState = cursorLockBeforePhotoMode;
            Cursor.visible = cursorVisibleBeforePhotoMode;
            statusMessage = "F8: 撮影カメラを開始";
        }
    }

    private void CopyFromCurrentCamera()
    {
        if (photoCamera == null) return;

        Camera source = Camera.main;
        if (source == null || source == photoCamera)
        {
            Camera[] cameras = FindObjectsByType<Camera>(FindObjectsInactive.Exclude);
            foreach (Camera candidate in cameras)
            {
                if (candidate != photoCamera && candidate.targetTexture == null)
                {
                    source = candidate;
                    break;
                }
            }
        }

        if (source != null && source != photoCamera)
        {
            photoCamera.CopyFrom(source);
            photoCamera.targetTexture = null;
            photoCamera.stereoTargetEye = StereoTargetEyeMask.None;

            UniversalAdditionalCameraData sourceData = source.GetComponent<UniversalAdditionalCameraData>();
            if (sourceData != null && photoCameraData != null)
            {
                // URPのポストプロセス、Volume、AA、Renderer設定も撮影カメラへ引き継ぐ。
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(sourceData), photoCameraData);
                photoCameraData.allowXRRendering = false;
            }

            transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
        }
        else
        {
            transform.SetPositionAndRotation(new Vector3(0f, 1.5f, 4f), Quaternion.Euler(0f, 180f, 0f));
        }

        Vector3 angles = transform.eulerAngles;
        yaw = angles.y;
        pitch = NormalizeAngle(angles.x);
    }

    private void SuppressOtherDisplayCameras()
    {
        Camera[] cameras = FindObjectsByType<Camera>(FindObjectsInactive.Include);
        foreach (Camera candidate in cameras)
        {
            if (candidate == null || candidate == photoCamera || candidate.targetTexture != null) continue;
            if (!candidate.enabled) continue;

            if (!overriddenCameras.ContainsKey(candidate)) overriddenCameras.Add(candidate, true);
            candidate.enabled = false;
        }
    }

    private void SuppressOtherListeners()
    {
        AudioListener[] listeners = FindObjectsByType<AudioListener>(FindObjectsInactive.Include);
        foreach (AudioListener listener in listeners)
        {
            if (listener == null || listener == photoListener || !listener.enabled) continue;

            if (!overriddenListeners.ContainsKey(listener)) overriddenListeners.Add(listener, true);
            listener.enabled = false;
        }
    }

    private void RestoreOverriddenComponents()
    {
        foreach (KeyValuePair<Camera, bool> item in overriddenCameras)
        {
            if (item.Key != null) item.Key.enabled = item.Value;
        }
        overriddenCameras.Clear();

        foreach (KeyValuePair<AudioListener, bool> item in overriddenListeners)
        {
            if (item.Key != null) item.Key.enabled = item.Value;
        }
        overriddenListeners.Clear();
    }

    private void TogglePause()
    {
        if (!ownsPause)
        {
            timeScaleBeforePause = Time.timeScale;
            Time.timeScale = 0f;
            ownsPause = true;
            statusMessage = "一時停止中（カメラ操作は可能）";
        }
        else
        {
            Time.timeScale = timeScaleBeforePause;
            ownsPause = false;
            statusMessage = "再生中";
        }
    }

    private IEnumerator CaptureScreenshot()
    {
        if (captureInProgress) yield break;

        captureInProgress = true;
        string directory = GetCaptureDirectory();
        Directory.CreateDirectory(directory);
        int width = BaseCaptureWidth * captureScale;
        int height = BaseCaptureHeight * captureScale;
        string fileName =
            $"MainVisual_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{width}x{height}_{CaptureDpi:0}dpi.png";
        string path = Path.Combine(directory, fileName);

        suppressOverlay = true;
        statusMessage = $"保存中: {path}";

        // LateUpdate（SpringBoneなど）まで反映された姿勢を撮る。
        yield return new WaitForEndOfFrame();

        RenderTexture captureTarget = null;
        Texture2D captureTexture = null;
        RenderTexture previousActive = RenderTexture.active;

        try
        {
            captureTarget = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB)
            {
                name = "Desktop Photo Capture",
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false
            };
            captureTarget.Create();

            var renderRequest = new RenderPipeline.StandardRequest
            {
                destination = captureTarget,
                mipLevel = 0,
                slice = 0,
                face = CubemapFace.Unknown
            };

            if (!RenderPipeline.SupportsRenderRequest(photoCamera, renderRequest))
            {
                throw new NotSupportedException("現在のRender Pipelineはカメラ直接撮影に対応していません。");
            }

            // XRのバックバッファではなく、この撮影カメラだけをURPで専用Textureへ描画する。
            RenderPipeline.SubmitRenderRequest(photoCamera, renderRequest);

            RenderTexture.active = captureTarget;
            captureTexture = new Texture2D(width, height, TextureFormat.RGB24, false, false);
            captureTexture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
            captureTexture.Apply(false, false);
            byte[] pngBytes = AddOrReplacePngDensity(captureTexture.EncodeToPNG(), CaptureDpi);
            File.WriteAllBytes(path, pngBytes);

            statusMessage = $"保存しました: {path}";
            Debug.Log($"[DesktopPhotoCamera] Screenshot saved: {path}");
        }
        catch (Exception exception)
        {
            statusMessage = $"保存に失敗しました: {exception.Message}";
            Debug.LogException(exception);
        }
        finally
        {
            RenderTexture.active = previousActive;
            if (captureTexture != null) Destroy(captureTexture);
            if (captureTarget != null)
            {
                captureTarget.Release();
                Destroy(captureTarget);
            }

            suppressOverlay = false;
            captureInProgress = false;
        }
    }

    private static string GetCaptureDirectory()
    {
#if UNITY_EDITOR
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Captures"));
#else
        string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        return string.IsNullOrEmpty(pictures)
            ? Path.Combine(Application.persistentDataPath, "MainVisualCaptures")
            : Path.Combine(pictures, Application.productName, "MainVisualCaptures");
#endif
    }

    /// <summary>
    /// PNGのpHYsチャンクへDPI相当のpixels-per-meterを書き込む。
    /// Texture2D.EncodeToPNGはDPIメタデータを付加しないため、IHDR直後へ追加する。
    /// </summary>
    private static byte[] AddOrReplacePngDensity(byte[] pngBytes, float dpi)
    {
        const int pngSignatureLength = 8;
        const int chunkHeaderLength = 8;
        const int chunkCrcLength = 4;
        const int physicalDataLength = 9;

        if (pngBytes == null || pngBytes.Length < 33) return pngBytes;

        uint pixelsPerMeter = (uint)Mathf.RoundToInt(dpi / 0.0254f);
        byte[] physicalChunk = new byte[chunkHeaderLength + physicalDataLength + chunkCrcLength];
        WriteUInt32BigEndian(physicalChunk, 0, physicalDataLength);
        physicalChunk[4] = (byte)'p';
        physicalChunk[5] = (byte)'H';
        physicalChunk[6] = (byte)'Y';
        physicalChunk[7] = (byte)'s';
        WriteUInt32BigEndian(physicalChunk, 8, pixelsPerMeter);
        WriteUInt32BigEndian(physicalChunk, 12, pixelsPerMeter);
        physicalChunk[16] = 1; // 単位はmeter
        WriteUInt32BigEndian(physicalChunk, 17, CalculateCrc32(physicalChunk, 4, 13));

        int offset = pngSignatureLength;
        while (offset + 12 <= pngBytes.Length)
        {
            int dataLength = ReadInt32BigEndian(pngBytes, offset);
            if (dataLength < 0 || offset + 12 + dataLength > pngBytes.Length) break;

            bool isPhysicalChunk = pngBytes[offset + 4] == (byte)'p' &&
                                   pngBytes[offset + 5] == (byte)'H' &&
                                   pngBytes[offset + 6] == (byte)'Y' &&
                                   pngBytes[offset + 7] == (byte)'s';
            if (isPhysicalChunk && dataLength == physicalDataLength)
            {
                Buffer.BlockCopy(physicalChunk, 0, pngBytes, offset, physicalChunk.Length);
                return pngBytes;
            }

            bool isHeaderChunk = pngBytes[offset + 4] == (byte)'I' &&
                                 pngBytes[offset + 5] == (byte)'H' &&
                                 pngBytes[offset + 6] == (byte)'D' &&
                                 pngBytes[offset + 7] == (byte)'R';
            int nextOffset = offset + chunkHeaderLength + dataLength + chunkCrcLength;
            if (isHeaderChunk)
            {
                byte[] result = new byte[pngBytes.Length + physicalChunk.Length];
                Buffer.BlockCopy(pngBytes, 0, result, 0, nextOffset);
                Buffer.BlockCopy(physicalChunk, 0, result, nextOffset, physicalChunk.Length);
                Buffer.BlockCopy(pngBytes, nextOffset, result, nextOffset + physicalChunk.Length,
                    pngBytes.Length - nextOffset);
                return result;
            }

            offset = nextOffset;
        }

        return pngBytes;
    }

    private static int ReadInt32BigEndian(byte[] data, int offset)
    {
        return (data[offset] << 24) |
               (data[offset + 1] << 16) |
               (data[offset + 2] << 8) |
               data[offset + 3];
    }

    private static void WriteUInt32BigEndian(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)(value >> 24);
        data[offset + 1] = (byte)(value >> 16);
        data[offset + 2] = (byte)(value >> 8);
        data[offset + 3] = (byte)value;
    }

    private static uint CalculateCrc32(byte[] data, int offset, int count)
    {
        uint crc = 0xffffffffu;
        for (int i = offset; i < offset + count; i++)
        {
            crc ^= data[i];
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1u) != 0u ? 0xedb88320u ^ (crc >> 1) : crc >> 1;
            }
        }

        return crc ^ 0xffffffffu;
    }

    private void OnGUI()
    {
        if (suppressOverlay) return;

        if (!photoMode)
        {
            GUI.Box(new Rect(12f, 12f, 225f, 30f), statusMessage);
            return;
        }

        string pauseLabel = ownsPause ? "停止中" : "再生中";
        string summary = $"撮影カメラ ON  |  F8: 終了  H: ヘルプ  |  {pauseLabel}";
        GUI.Box(new Rect(12f, 12f, 420f, 30f), summary);

        if (!showHelp) return;

        string help =
            "右ドラッグ: 視点変更\n" +
            "WASD: 移動 / Q・E: 下降・上昇 / Shift: 加速\n" +
            "マウスホイール: 移動速度\n" +
            "［ / ］: 画角を狭く / 広く\n" +
            "P: 演出を一時停止（停止中もカメラ操作可）\n" +
            "1～4: 保存倍率 / F12: UIなしPNG保存\n" +
            $"出力 {BaseCaptureWidth * captureScale}×{BaseCaptureHeight * captureScale} / {CaptureDpi:0}dpi\n" +
            $"速度 {moveSpeed:0.0}  FOV {photoCamera.fieldOfView:0}°  保存倍率 {captureScale}x\n" +
            statusMessage;
        GUI.Box(new Rect(12f, 48f, 560f, 185f), help);
    }

    private void OnDestroy()
    {
        RestoreOverriddenComponents();
        if (ownsPause) Time.timeScale = timeScaleBeforePause;
    }

    private static float NormalizeAngle(float angle)
    {
        return angle > 180f ? angle - 360f : angle;
    }
}
