using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Vosk;
using System.IO;
using System.Threading.Tasks;
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

    private const int SampleRate = 16000;

    void Start()
    {
#if ENABLE_INPUT_SYSTEM
        pushToTalkAction.Enable();
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
            isModelLoaded = true;
        });
    }

    private void StartMicrophone()
    {
        if (Microphone.devices.Length == 0)
        {
            Debug.LogWarning("[Vosk] マイクデバイスが見つかりません");
            return;
        }

        microphoneDevice = Microphone.devices[0]; 
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

        if (isDebugTriggered)
        {
            Debug.Log("[Title] デバッグ入力を検知しました。指定のシーンを開始します。");
            StartGameScene();
            return;
        }

        bool isPressedDown = Input.GetKeyDown(pushToTalkKey);
        bool isHolding = Input.GetKey(pushToTalkKey);
        bool isReleased = Input.GetKeyUp(pushToTalkKey);

#if ENABLE_INPUT_SYSTEM
        if (pushToTalkAction.enabled)
        {
            isPressedDown |= pushToTalkAction.WasPressedThisFrame();
            isHolding |= pushToTalkAction.IsPressed();
            isReleased |= pushToTalkAction.WasReleasedThisFrame();
        }
#endif

        // プッシュ・トゥ・トークのトリガーイベント
        if (isPressedDown)
        {
            recognizer.Reset();
            Debug.Log($"<color=#00FF00>[Vosk] 🎤 ライブスタートの聞き取りを開始しました（ボタン長押し中）</color>");
        }

        if (isReleased)
        {
            string finalResult = recognizer.FinalResult();
            ProcessRecognitionResult(finalResult);
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
            short[] shortSamples = new short[samples.Length];
            for (int i = 0; i < samples.Length; i++)
            {
                shortSamples[i] = (short)(samples[i] * short.MaxValue);
            }

            byte[] byteData = new byte[shortSamples.Length * 2];
            System.Buffer.BlockCopy(shortSamples, 0, byteData, 0, byteData.Length);

            if (recognizer.AcceptWaveform(byteData, byteData.Length))
            {
                string result = recognizer.Result();
                ProcessRecognitionResult(result);
            }
            else
            {
                // 話している途中の部分認識結果もチェック（よりレスポンスを早くするため）
                string partialResult = recognizer.PartialResult();
                CheckKeywordsAndStart(partialResult);
            }
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
        debugTransitionAction.Disable();
#endif

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
