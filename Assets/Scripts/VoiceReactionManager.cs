using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Vosk;
using System.IO;
using System.Threading.Tasks;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[System.Serializable]
public class KeywordReaction
{
    [Tooltip("音声認識で検知するキーワード（ひらがな、カタカナ、漢字など認識されやすい文字）")]
    public string keyword;
    [Tooltip("再生するアニメーションのステート名（例: Perfect, Great, Miss, Jump）")]
    public string reactionName;
}

public class VoiceReactionManager : MonoBehaviour
{
    [Header("Vosk Settings")]
    [Tooltip("StreamingAssets内のVoskモデルフォルダ名")]
    public string modelFolderName = "vosk-model-small-ja-0.22";
    
    [Header("Microphone Settings")]
    [Tooltip("空欄の場合はデフォルトのマイクを使用します。特定のマイクを使用したい場合は、ここにマイクの名前（の一部）を入力してください。")]
    public string customMicrophoneName = "";

    [Tooltip("マイクが認識した生の文字をコンソールに表示して、精度を確認できるようにするかどうか")]
    public bool showRecognitionLog = true;

    [Header("Input Settings")]
#if ENABLE_INPUT_SYSTEM
    [Tooltip("VR用: 右手のトリガーボタンで音声認識を行います")]
    public InputAction pushToTalkAction = new InputAction("PushToTalk", InputActionType.Button, "<XRController>{RightHand}/triggerPressed");
#endif
    [Tooltip("PC用: 音声認識を行うキー。VRでない場合やテスト用に使用します")]
    public KeyCode pushToTalkKey = KeyCode.Space;
    
    [Header("Keywords & Reactions")]
    [Tooltip("ここで設定したキーワードをマイクで話すと、対応するリアクションが再生されます")]
    public List<KeywordReaction> keywordReactions = new List<KeywordReaction>
    {
        new KeywordReaction { keyword = "すごい", reactionName = "Perfect" },
        new KeywordReaction { keyword = "かわいい", reactionName = "Perfect" },
        new KeywordReaction { keyword = "ジャンプ", reactionName = "JUMP00" },
        new KeywordReaction { keyword = "どんまい", reactionName = "Great" },
        new KeywordReaction { keyword = "ミス", reactionName = "Miss" }
    };

    [Header("References")]
    public UnityChanReaction unityChanReaction;

    private Model model;
    private VoskRecognizer recognizer;
    private string microphoneDevice;
    private AudioClip audioClip;
    private int lastSamplePosition = 0;
    private bool isListening = false;

    private bool isModelLoaded = false;

    private const int SampleRate = 16000; // Voskは16kHzを推奨

    void Start()
    {
#if ENABLE_INPUT_SYSTEM
        pushToTalkAction.Enable();
#endif

        // UnityChanReactionが未設定の場合はシーン内から探す
        if (unityChanReaction == null)
        {
            unityChanReaction = FindAnyObjectByType<UnityChanReaction>();
        }

        // モデルの初期化
        string modelPath = Path.Combine(Application.streamingAssetsPath, modelFolderName);
        if (!Directory.Exists(modelPath))
        {
            Debug.LogError($"[Vosk] モデルが見つかりません: {modelPath}。StreamingAssets内に配置してください。");
            return;
        }

        // バックグラウンドでVoskのモデルをロード（重いため）
        Task.Run(() =>
        {
            model = new Model(modelPath);
            recognizer = new VoskRecognizer(model, SampleRate);
            recognizer.SetMaxAlternatives(0);
            recognizer.SetWords(true);
            
            // ロード完了フラグを立てて、Update内でメインスレッド処理を呼ぶ
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

        // デフォルトマイクをセット
        microphoneDevice = Microphone.devices[0]; 

        // カスタムマイク名が指定されている場合は検索する
        if (!string.IsNullOrEmpty(customMicrophoneName))
        {
            bool found = false;
            foreach (var device in Microphone.devices)
            {
                if (device.Contains(customMicrophoneName))
                {
                    microphoneDevice = device;
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                Debug.LogWarning($"[Vosk] 指定されたマイク '{customMicrophoneName}' が見つかりませんでした。デフォルトのマイクを使用します。");
            }
        }

        // 利用可能なすべてのマイクをログに出力（デバッグ用）
        Debug.Log("[Vosk] 認識されたマイク一覧:\n - " + string.Join("\n - ", Microphone.devices));

        // ループ録音を開始（1秒間のバッファをループ）
        audioClip = Microphone.Start(microphoneDevice, true, 1, SampleRate);
        isListening = true;
        Debug.Log($"[Vosk] 音声認識を開始しました。マイク: {microphoneDevice}");
    }

    void Update()
    {
        if (isModelLoaded && !isListening)
        {
            StartMicrophone();
            return;
        }

        if (!isListening || recognizer == null || audioClip == null) return;

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

        // 【修正点】キー入力の判定を一番最初に行う
        if (isPressedDown)
        {
            recognizer.Reset();
            Debug.Log($"<color=#00FF00>[Vosk] 🎤 音声入力の受付を開始しました</color>");
        }

        if (isReleased)
        {
            string finalResult = recognizer.FinalResult();
            ProcessRecognitionResult(finalResult);
            Debug.Log($"<color=#FF8800>[Vosk] 🛑 音声入力の受付を終了しました</color>");
        }

        int currentPosition = Microphone.GetPosition(microphoneDevice);
        if (currentPosition < 0 || lastSamplePosition == currentPosition) return;

        // 新しい音声データを取得
        int sampleCount = currentPosition - lastSamplePosition;
        if (sampleCount < 0)
        {
            sampleCount += audioClip.samples;
        }

        float[] samples = new float[sampleCount];
        audioClip.GetData(samples, lastSamplePosition);
        lastSamplePosition = currentPosition;

        // キーを押している間だけ音声データをVoskに送る
        if (isHolding)
        {
            // float(Unity)からshort(PCM 16bit)へ変換
            short[] shortSamples = new short[samples.Length];
            for (int i = 0; i < samples.Length; i++)
            {
                shortSamples[i] = (short)(samples[i] * short.MaxValue);
            }

            // バイト配列に変換
            byte[] byteData = new byte[shortSamples.Length * 2];
            System.Buffer.BlockCopy(shortSamples, 0, byteData, 0, byteData.Length);

            // Voskへデータを送る
            if (recognizer.AcceptWaveform(byteData, byteData.Length))
            {
                string result = recognizer.Result();
                ProcessRecognitionResult(result);
            }
        }
    }

    private void ProcessRecognitionResult(string jsonResult)
    {
        if (string.IsNullOrEmpty(jsonResult)) return;

        // jsonResultは {"text": "あいうえお"} または {"partial": "あい"} のような形式
        if (showRecognitionLog && jsonResult.Contains("\"text\""))
        {
            Debug.Log($"[Vosk 音声認識の生データ] {jsonResult}");
        }

        string textWithoutSpaces = jsonResult.Replace(" ", "").Replace("　", "");

        // シンプルに文字列検索でキーワードが含まれているかチェックする
        foreach (var kr in keywordReactions)
        {
            string cleanKeyword = kr.keyword.Replace(" ", "").Replace("　", "");

            if (textWithoutSpaces.Contains(cleanKeyword))
            {
                Debug.Log($"[Vosk] キーワード検知: {kr.keyword} -> アニメーション: {kr.reactionName}");
                if (unityChanReaction != null)
                {
                    unityChanReaction.PlayReaction(kr.reactionName);
                }
                
                // 一度反応したら、次の認識結果が来るまでリセット
                // recognizer.Reset(); // 状況によってはリセットした方が良い
                break;
            }
        }
    }

    void OnDestroy()
    {
#if ENABLE_INPUT_SYSTEM
        pushToTalkAction.Disable();
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
