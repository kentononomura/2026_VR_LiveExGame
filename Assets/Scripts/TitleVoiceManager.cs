using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Vosk;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class TitleVoiceManager : MonoBehaviour
{
    [Header("Scene Transition Settings")]
    [Tooltip("次に遷移するシーンの名前を設定します")]
    public string targetSceneName = "TestScene";

    [Header("Vosk Settings")]
    [Tooltip("StreamingAssets内のVoskモデルフォルダ名")]
    public string modelFolderName = "vosk-model-small-ja-0.22";
    
    [Header("Microphone Settings")]
    [Tooltip("空欄の場合はデフォルトのマイクを使用します")]
    public string customMicrophoneName = "";
    [Tooltip("マイクが認識した文字をコンソールに表示するかどうか")]
    public bool showRecognitionLog = true;

    [Tooltip("メニューのマイク入力メーターへ反映する音量感度です。小さい声でバーが動かない場合は上げてください。")]
    [Range(1f, 100f)]
    [SerializeField] private float microphoneMeterSensitivity = 20f;

    [Header("Input Settings (Push to Talk)")]
#if ENABLE_INPUT_SYSTEM
    [Tooltip("VR用: 右手のトリガーボタンで音声認識を行います")]
    public InputAction pushToTalkAction = new InputAction("PushToTalk", InputActionType.Button, "<XRController>{RightHand}/triggerPressed");
    
    [Tooltip("VR用: 左手のトリガーボタンで音声認識を行います")]
    public InputAction pushToTalkLeftAction = new InputAction("PushToTalkLeft", InputActionType.Button, "<XRController>{LeftHand}/triggerPressed");
    
    [Tooltip("デバッグ用: 左手のXボタンで即座に遷移します")]
    public InputAction debugTransitionAction = new InputAction("DebugTransition", InputActionType.Button, "<XRController>{LeftHand}/primaryButton");
#endif
    [Tooltip("PC用: 音声認識を行うキー。Spaceキー長押し")]
    public KeyCode pushToTalkKey = KeyCode.Space;

    [Header("Keywords")]
    [Tooltip("この文字が含まれていたらシーン遷移します")]
    public List<string> startKeywords = new List<string> { "らいぶすたーと", "ライブスタート", "スタート", "すたーと", "らいぶ" };

    private Model model;
    private VoskRecognizer recognizer;
    private string microphoneDevice;
    private AudioClip audioClip;
    private int lastSamplePosition = 0;
    private bool isListening = false;
    private bool isModelLoaded = false;
    private float nextMicrophoneStartAttemptTime;
    private bool isTransitioning = false; // 二重ロード防止
    private bool isShuttingDown = false;
    private bool wasRightTriggerPressed = false;
    private bool wasLeftPrimaryPressed = false;
    private float microphoneInputLevel;
    private float lastMicrophoneDataTime = -1f;
    private string voiceModelStatus = "モデル準備待ち";

    private const int SampleRate = 16000;
    private readonly List<UnityEngine.XR.InputDevice> leftHandDevices = new List<UnityEngine.XR.InputDevice>(1);
    private readonly List<UnityEngine.XR.InputDevice> rightHandDevices = new List<UnityEngine.XR.InputDevice>(1);

    public bool IsVoiceModelReady => isModelLoaded && recognizer != null;
    public string VoiceModelStatus => voiceModelStatus;
    public bool IsMicrophoneListening =>
        isListening && audioClip != null && Microphone.IsRecording(microphoneDevice);
    public bool HasMicrophoneDataStream =>
        lastMicrophoneDataTime >= 0f && Time.unscaledTime - lastMicrophoneDataTime < 1f;
    public float MicrophoneInputLevel => microphoneInputLevel;

    // Threading and Queueing
    private enum VoskCommandType { Reset, ProcessAudio, FinalResult }
    private struct VoskCommand
    {
        public VoskCommandType type;
        public byte[] audioData;
        public int audioLength;
    }
    
    private ConcurrentQueue<VoskCommand> commandQueue = new ConcurrentQueue<VoskCommand>();
    private ConcurrentQueue<string> resultQueue = new ConcurrentQueue<string>();
    private Thread workerThread;

    void Start()
    {
#if ENABLE_INPUT_SYSTEM
        pushToTalkAction.Enable();
        pushToTalkLeftAction.Enable();
        debugTransitionAction.Enable();
#endif

        StartCoroutine(LoadModelRoutine());
    }

    private IEnumerator LoadModelRoutine()
    {
        voiceModelStatus = "モデルファイル準備中";
        string modelPath = null;
        string modelPrepareError = null;
        yield return VoskModelPathResolver.Prepare(
            modelFolderName,
            path => modelPath = path,
            error => modelPrepareError = error,
            (progress, fileName) =>
                voiceModelStatus = $"モデル展開中 {Mathf.RoundToInt(progress * 100f)}%" );

        if (!string.IsNullOrEmpty(modelPrepareError) || string.IsNullOrEmpty(modelPath))
        {
            voiceModelStatus = "モデルファイル準備失敗";
            Debug.LogError($"[Vosk] タイトル用モデルを準備できませんでした: {modelPrepareError}");
            yield break;
        }

        voiceModelStatus = "モデル初期化中";

        // バックグラウンドでVoskのモデルをロード
        Task.Run(() =>
        {
            try
            {
                model = VoskModelCache.GetOrLoad(modelPath);
                recognizer = new VoskRecognizer(model, SampleRate);
                recognizer.SetMaxAlternatives(0);
                recognizer.SetWords(false);

                if (isShuttingDown)
                {
                    recognizer.Dispose();
                    recognizer = null;
                    return;
                }

                workerThread = new Thread(VoskWorkerLoop);
                workerThread.IsBackground = true;
                workerThread.Start();

                isModelLoaded = true;
                voiceModelStatus = "モデル準備完了";
                Debug.Log("[Vosk] タイトル用モデルロードおよび音声認識スレッドが正常に起動しました。");
            }
            catch (System.Exception ex)
            {
                voiceModelStatus = "モデル初期化失敗";
                Debug.LogError($"[Vosk] タイトル用モデル初期化例外: {ex.Message}\n{ex.StackTrace}");
            }
        });
    }

    private void VoskWorkerLoop()
    {
        while (!isShuttingDown)
        {
            if (commandQueue.TryDequeue(out VoskCommand cmd))
            {
                if (cmd.type == VoskCommandType.Reset)
                {
                    recognizer.Reset();
                }
                else if (cmd.type == VoskCommandType.ProcessAudio && cmd.audioData != null)
                {
                    try
                    {
                        if (recognizer.AcceptWaveform(cmd.audioData, cmd.audioLength))
                        {
                            resultQueue.Enqueue(recognizer.Result());
                        }
                        else
                        {
                            resultQueue.Enqueue(recognizer.PartialResult());
                        }
                    }
                    finally
                    {
                        VoskPcmUtility.Return(cmd.audioData);
                    }
                }
                else if (cmd.type == VoskCommandType.FinalResult)
                {
                    resultQueue.Enqueue(recognizer.FinalResult());
                }
            }
            else
            {
                Thread.Sleep(10);
            }
        }
    }

    private void StartMicrophone()
    {
        if (!VRMicrophonePermission.EnsureGranted()) return;
        if (Time.unscaledTime < nextMicrophoneStartAttemptTime) return;
        nextMicrophoneStartAttemptTime = Time.unscaledTime + 2f;

        string[] availableDevices = Microphone.devices;

        // Questではデバイス名一覧が空でも、null指定でシステム既定マイクを開始できる。
        microphoneDevice = availableDevices.Length > 0 ? availableDevices[0] : null;

        // VRデバイス（Oculus / Meta Quest等）のマイクを自動検出して優先
        foreach (var device in availableDevices)
        {
            string lowerName = device.ToLower();
            if (lowerName.Contains("oculus") || lowerName.Contains("meta quest") ||
                lowerName.Contains("quest") || lowerName.Contains("android") ||
                lowerName.Contains("virtual audio"))
            {
                microphoneDevice = device;
                Debug.Log($"[Vosk] VRマイクを自動検出しました: {device}");
                break;
            }
        }

        // customMicrophoneName が指定されている場合は最優先
        if (!string.IsNullOrEmpty(customMicrophoneName))
        {
            foreach (var device in availableDevices)
            {
                if (device.IndexOf(customMicrophoneName, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    microphoneDevice = device;
                    break;
                }
            }
        }

        audioClip = Microphone.Start(microphoneDevice, true, VoskPcmUtility.MicrophoneBufferSeconds, SampleRate);
        isListening = audioClip != null;

        if (isListening)
        {
            string selectedName = string.IsNullOrEmpty(microphoneDevice)
                ? "Quest/Android システム既定マイク"
                : microphoneDevice;
            Debug.Log($"[Vosk] タイトル用の音声認識マイクを開始しました: {selectedName}");
        }
        else
        {
            Debug.LogError("[Vosk] マイクの開始に失敗しました。アプリのマイク権限を確認してください。");
        }
    }

    void Update()
    {
        // 入力が止まった場合にメーターを滑らかに0へ戻す。
        microphoneInputLevel = Mathf.MoveTowards(
            microphoneInputLevel,
            0f,
            Time.unscaledDeltaTime * 1.5f);

        if (isModelLoaded && !isListening)
        {
            StartMicrophone();
            return;
        }

        if (!isListening || recognizer == null || audioClip == null || isTransitioning) return;

        // 【デバッグ用バイパス】PCのEnterキー、またはVRの左手Xボタンで即座にシーン開始
        bool isDebugTriggered = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
#if ENABLE_INPUT_SYSTEM
        if (debugTransitionAction != null && debugTransitionAction.WasPressedThisFrame())
        {
            isDebugTriggered = true;
        }
#endif
        // フォールバック: 左手プライマリボタンから直接取得 (Quest 2/Quest 3 互換用)
        leftHandDevices.Clear();
        UnityEngine.XR.InputDevices.GetDevicesWithCharacteristics(UnityEngine.XR.InputDeviceCharacteristics.Left | UnityEngine.XR.InputDeviceCharacteristics.Controller, leftHandDevices);
        if (leftHandDevices.Count > 0)
        {
            if (leftHandDevices[0].TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out bool isXrPrimaryPressed))
            {
                if (isXrPrimaryPressed && !wasLeftPrimaryPressed)
                {
                    isDebugTriggered = true;
                }
                wasLeftPrimaryPressed = isXrPrimaryPressed;
            }
        }

        if (isDebugTriggered)
        {
            Debug.Log("[Title] デバッグ入力を検知しました。指定のシーンを開始します。");
            StartGameScene();
            return;
        }

        bool isTriggerPressed = Input.GetKey(pushToTalkKey);

#if ENABLE_INPUT_SYSTEM
        if (pushToTalkAction.enabled)
        {
            isTriggerPressed |= pushToTalkAction.IsPressed();
        }
        if (pushToTalkLeftAction.enabled)
        {
            isTriggerPressed |= pushToTalkLeftAction.IsPressed();
        }
#endif

        // フォールバック: 右手トリガーから直接取得 (Quest 2/Quest 3 互換用)
        rightHandDevices.Clear();
        UnityEngine.XR.InputDevices.GetDevicesWithCharacteristics(UnityEngine.XR.InputDeviceCharacteristics.Right | UnityEngine.XR.InputDeviceCharacteristics.Controller, rightHandDevices);
        if (rightHandDevices.Count > 0)
        {
            if (rightHandDevices[0].TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out bool isXrTriggerPressed))
            {
                isTriggerPressed |= isXrTriggerPressed;
            }
        }

        // フォールバック: 左手トリガーから直接取得 (Quest 2/Quest 3 互換用)
        if (leftHandDevices.Count > 0)
        {
            if (leftHandDevices[0].TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out bool isXrTriggerPressed))
            {
                isTriggerPressed |= isXrTriggerPressed;
            }
        }

        // 状態変化（押し下げ・押し上げ）を確実に判定するロジック
        bool isPressedDown = false;
        bool isReleased = false;
        bool isHolding = isTriggerPressed;

        if (isTriggerPressed)
        {
            if (!wasRightTriggerPressed)
            {
                isPressedDown = true;
                wasRightTriggerPressed = true;
            }
        }
        else
        {
            if (wasRightTriggerPressed)
            {
                isReleased = true;
                wasRightTriggerPressed = false;
            }
        }

        // メインスレッドでの結果受け取りと処理
        while (resultQueue.TryDequeue(out string result))
        {
            ProcessRecognitionResult(result);
        }

        // プッシュ・トゥ・トークのトリガーイベント
        if (isPressedDown)
        {
            commandQueue.Enqueue(new VoskCommand { type = VoskCommandType.Reset });
            Debug.Log($"<color=#00FF00>[Vosk] 🎤 ライブスタートの聞き取りを開始しました（ボタン長押し中）</color>");
        }

        int currentPosition = Microphone.GetPosition(microphoneDevice);
        if (currentPosition >= 0 && lastSamplePosition != currentPosition)
        {
            int sampleCount = currentPosition - lastSamplePosition;
            if (sampleCount < 0) sampleCount += audioClip.samples;

            float[] samples = new float[sampleCount];
            audioClip.GetData(samples, lastSamplePosition);
            lastSamplePosition = currentPosition;
            lastMicrophoneDataTime = Time.unscaledTime;

            float maxVal = 0f;
            double squareSum = 0d;
            foreach (float sample in samples)
            {
                float absolute = Mathf.Abs(sample);
                if (absolute > maxVal) maxVal = absolute;
                squareSum += sample * sample;
            }

            if (samples.Length > 0)
            {
                float rms = Mathf.Sqrt((float)(squareSum / samples.Length));
                float detectedLevel = Mathf.Clamp01(rms * microphoneMeterSensitivity);
                microphoneInputLevel = Mathf.Max(microphoneInputLevel, detectedLevel);
            }

            // 離したフレームの語尾も、確定要求より先に必ずVoskへ渡す。
            if (isHolding || isReleased)
            {
                if (maxVal < 0.001f)
                {
                    Debug.LogWarning("[Vosk] 🎤 (Title) 音声データが極端に小さいか無音です。マイクがミュートされているか、正しいマイクデバイスが選択されていない可能性があります。");
                }

                byte[] byteData = VoskPcmUtility.RentAndConvert(samples, out int byteCount);
                commandQueue.Enqueue(new VoskCommand
                {
                    type = VoskCommandType.ProcessAudio,
                    audioData = byteData,
                    audioLength = byteCount
                });
            }
        }

        if (isReleased)
        {
            commandQueue.Enqueue(new VoskCommand { type = VoskCommandType.FinalResult });
            Debug.Log($"<color=#FF8800>[Vosk] 🛑 聞き取りを終了しました</color>");
        }
    }

    private void ProcessRecognitionResult(string jsonResult)
    {
        if (string.IsNullOrEmpty(jsonResult)) return;

        if (showRecognitionLog && jsonResult.Contains("\"text\""))
        {
            Debug.Log($"[Vosk タイトル音声認識結果] {jsonResult}");
        }

        CheckKeywordsAndStart(jsonResult);
    }

    private void CheckKeywordsAndStart(string jsonResult)
    {
        if (string.IsNullOrEmpty(jsonResult) || isTransitioning) return;

        foreach (var keyword in startKeywords)
        {
            if (jsonResult.Contains(keyword))
            {
                Debug.Log($"[Vosk] キーワード '{keyword}' を検知。ゲームを開始します！");
                StartGameScene();
                break;
            }
        }
    }

    // ゲームシーンへの遷移処理（ポインタークリックでも呼ばれる）
    public void StartGameScene()
    {
        if (isTransitioning) return;
        isTransitioning = true;

        Debug.Log($"{targetSceneName} をロード中...");
        VRScreenFader.Instance.LoadSceneWithFade(targetSceneName, 1.0f);
    }

    void OnDestroy()
    {
#if ENABLE_INPUT_SYSTEM
        pushToTalkAction.Disable();
        pushToTalkLeftAction.Disable();
        debugTransitionAction.Disable();
#endif
        isShuttingDown = true;
        if (workerThread != null && workerThread.IsAlive)
        {
            workerThread.Join(500);
        }

        if (isListening)
        {
            Microphone.End(microphoneDevice);
        }
        
        if (recognizer != null)
        {
            recognizer.Dispose();
        }
        
        while (commandQueue.TryDequeue(out VoskCommand pendingCommand))
        {
            VoskPcmUtility.Return(pendingCommand.audioData);
        }
    }
}
