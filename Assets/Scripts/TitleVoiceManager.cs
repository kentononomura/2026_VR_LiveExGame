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
    private bool isTransitioning = false; // 二重ロード防止
    private bool isShuttingDown = false;
    private bool wasRightTriggerPressed = false;
    private bool wasLeftPrimaryPressed = false;

    private const int SampleRate = 16000;

    // Threading and Queueing
    private enum VoskCommandType { Reset, ProcessAudio, FinalResult }
    private struct VoskCommand
    {
        public VoskCommandType type;
        public byte[] audioData;
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

        // モデルの初期化
        string modelPath = Path.Combine(Application.streamingAssetsPath, modelFolderName);
        if (!Directory.Exists(modelPath))
        {
            Debug.LogError($"[Vosk] モデルが見つかりません: {modelPath}。");
            return;
        }

        // バックグラウンドでVoskのモデルをロード
        Task.Run(() =>
        {
            model = new Model(modelPath);
            recognizer = new VoskRecognizer(model, SampleRate);
            recognizer.SetMaxAlternatives(0);
            recognizer.SetWords(true);
            
            workerThread = new Thread(VoskWorkerLoop);
            workerThread.IsBackground = true;
            workerThread.Start();
            
            isModelLoaded = true;
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
                    if (recognizer.AcceptWaveform(cmd.audioData, cmd.audioData.Length))
                    {
                        resultQueue.Enqueue(recognizer.Result());
                    }
                    else
                    {
                        resultQueue.Enqueue(recognizer.PartialResult());
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
        if (Microphone.devices.Length == 0)
        {
            Debug.LogWarning("[Vosk] マイクデバイスが見つかりません");
            return;
        }

        // デフォルトマイク
        microphoneDevice = Microphone.devices[0]; 

        // VRデバイス（Oculus / Meta Quest等）のマイクを自動検出して優先
        foreach (var device in Microphone.devices)
        {
            string lowerName = device.ToLower();
            if (lowerName.Contains("oculus") || lowerName.Contains("meta quest") || lowerName.Contains("virtual audio"))
            {
                microphoneDevice = device;
                Debug.Log($"[Vosk] VRマイクを自動検出しました: {device}");
                break;
            }
        }

        // customMicrophoneName が指定されている場合は最優先
        if (!string.IsNullOrEmpty(customMicrophoneName))
        {
            foreach (var device in Microphone.devices)
            {
                if (device.Contains(customMicrophoneName))
                {
                    microphoneDevice = device;
                    break;
                }
            }
        }

        audioClip = Microphone.Start(microphoneDevice, true, 1, SampleRate);
        isListening = true;
        Debug.Log($"[Vosk] タイトル用の音声認識マイクを開始しました: {microphoneDevice}");
    }

    void Update()
    {
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
        var leftHandDevices = new List<UnityEngine.XR.InputDevice>();
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
        var rightHandDevices = new List<UnityEngine.XR.InputDevice>();
        UnityEngine.XR.InputDevices.GetDevicesWithCharacteristics(UnityEngine.XR.InputDeviceCharacteristics.Right | UnityEngine.XR.InputDeviceCharacteristics.Controller, rightHandDevices);
        if (rightHandDevices.Count > 0)
        {
            if (rightHandDevices[0].TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out bool isXrTriggerPressed))
            {
                isTriggerPressed |= isXrTriggerPressed;
            }
        }

        // フォールバック: 左手トリガーから直接取得 (Quest 2/Quest 3 互換用)
        var leftHandDevices2 = new List<UnityEngine.XR.InputDevice>();
        UnityEngine.XR.InputDevices.GetDevicesWithCharacteristics(UnityEngine.XR.InputDeviceCharacteristics.Left | UnityEngine.XR.InputDeviceCharacteristics.Controller, leftHandDevices2);
        if (leftHandDevices2.Count > 0)
        {
            if (leftHandDevices2[0].TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out bool isXrTriggerPressed))
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

        if (isReleased)
        {
            commandQueue.Enqueue(new VoskCommand { type = VoskCommandType.FinalResult });
            Debug.Log($"<color=#FF8800>[Vosk] 🛑 聞き取りを終了しました</color>");
        }

        int currentPosition = Microphone.GetPosition(microphoneDevice);
        if (currentPosition < 0 || lastSamplePosition == currentPosition) return;

        int sampleCount = currentPosition - lastSamplePosition;
        if (sampleCount < 0)
        {
            sampleCount += audioClip.samples;
        }

        float[] samples = new float[sampleCount];
        audioClip.GetData(samples, lastSamplePosition);
        lastSamplePosition = currentPosition;

        // ボタンを押している間だけ音声データをVoskに送る（本編と共通の仕様）
        if (isHolding)
        {
            // 音圧チェック（無音・ミュート検知）
            float maxVal = 0f;
            foreach (var s in samples)
            {
                float absVal = Mathf.Abs(s);
                if (absVal > maxVal) maxVal = absVal;
            }
            if (maxVal < 0.001f) // ほぼ完全な無音
            {
                Debug.LogWarning("[Vosk] 🎤 (Title) 音声データが極端に小さいか無音です。マイクがミュートされているか、正しいマイクデバイスが選択されていない可能性があります。");
            }

            short[] shortSamples = new short[samples.Length];
            for (int i = 0; i < samples.Length; i++)
            {
                shortSamples[i] = (short)(samples[i] * short.MaxValue);
            }

            byte[] byteData = new byte[shortSamples.Length * 2];
            System.Buffer.BlockCopy(shortSamples, 0, byteData, 0, byteData.Length);

            commandQueue.Enqueue(new VoskCommand { type = VoskCommandType.ProcessAudio, audioData = byteData });
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
        if (VRScreenFader.Instance != null)
        {
            VRScreenFader.Instance.FadeOut(1.0f, () => 
            {
                SceneManager.LoadSceneAsync(targetSceneName);
            });
        }
        else
        {
            SceneManager.LoadSceneAsync(targetSceneName);
        }
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
        
        if (model != null)
        {
            model.Dispose();
        }
    }
}
