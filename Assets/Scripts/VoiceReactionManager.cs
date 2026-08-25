using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Vosk;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[System.Serializable]
public class KeywordReaction
{
    [Tooltip("同じリアクションを表す別表記で共有する論理コマンドIDです。視覚演出の切り替えにも使用します。")]
    public string commandId;

    [Tooltip("音声認識で検知するキーワード（ひらがな、カタカナ、漢字など認識されやすい文字）")]
    public string keyword;
    [Tooltip("再生するアニメーションのステート名（例: Perfect, Great, Miss, Jump）")]
    public string reactionName;
    
    [Tooltip("上半身に再生するAnimatorステート名（例: Wave, Kiss）。空欄なら再生しません")]
    public string bodyReactionName;

    [Tooltip("体リアクションを再生するAnimatorレイヤー名です。空欄ならReactionLayerを使用します。コマンドごとに異なるAvatarMaskを選べます。")]
    public string bodyReactionLayerName;
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
        new KeywordReaction { commandId = "Perfect", keyword = "すごい", reactionName = "Perfect" },
        new KeywordReaction { commandId = "Perfect", keyword = "かわいい", reactionName = "Perfect" },
        new KeywordReaction { commandId = "Jump", keyword = "ジャンプ", reactionName = "JUMP00" },
        new KeywordReaction { commandId = "Great", keyword = "どんまい", reactionName = "Great" },
        new KeywordReaction { commandId = "Miss", keyword = "ミス", reactionName = "Miss" }
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
    private bool isShuttingDown = false;

    private const int SampleRate = 16000; // Voskは16kHzを推奨

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
#endif

        // UnityChanReactionが未設定の場合はシーン内から探す
        if (unityChanReaction == null)
        {
            unityChanReaction = FindAnyObjectByType<UnityChanReaction>();
        }

        // シーン遷移直後の高負荷を避けるため、数秒待ってから非同期でモデルロードを開始する
        StartCoroutine(DelayedModelLoadRoutine());
    }

    private IEnumerator DelayedModelLoadRoutine()
    {
        // シーン遷移直後のアセット初期化スパイクを逃がすため、2.0秒間待機
        yield return new WaitForSeconds(2.0f);


        string modelPath = null;
        string modelPrepareError = null;
        yield return VoskModelPathResolver.Prepare(
            modelFolderName,
            path => modelPath = path,
            error => modelPrepareError = error);

        if (!string.IsNullOrEmpty(modelPrepareError) || string.IsNullOrEmpty(modelPath))
        {
            Debug.LogError($"[Vosk] モデルを準備できませんでした: {modelPrepareError}");
            yield break;
        }

        Debug.Log($"[Vosk] モデルロード非同期タスクを起動します: {modelPath}");
        // バックグラウンドでVoskのモデルをロード（重いため）
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
                
                // ロード完了後、ワーカースレッドを起動
                workerThread = new Thread(VoskWorkerLoop);
                workerThread.IsBackground = true;
                workerThread.Start();
                
                isModelLoaded = true;
                Debug.Log("[Vosk] モデルロードおよび音声認識スレッドが正常に起動しました。");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Vosk] モデル初期化例外: {ex.Message}\n{ex.StackTrace}");
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
                Thread.Sleep(10); // Sleep slightly to prevent high CPU usage when idle
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

        // デフォルトマイクをセット
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

        // カスタムマイク名が指定されている場合は最優先で検索する
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
                Debug.LogWarning($"[Vosk] 指定されたマイク '{customMicrophoneName}' が見つかりませんでした。デフォルトまたは検出されたマイクを使用します。");
            }
        }

        // 利用可能なすべてのマイクをログに出力（デバッグ用）
        Debug.Log("[Vosk] 認識されたマイク一覧:\n - " + string.Join("\n - ", Microphone.devices));

        // 処理落ち時にも音声が上書きされにくい3秒間のリングバッファで録音する。
        audioClip = Microphone.Start(microphoneDevice, true, VoskPcmUtility.MicrophoneBufferSeconds, SampleRate);
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

        // 【修正点】結果の受け取り（メインスレッドでのアニメーション再生等）
        while (resultQueue.TryDequeue(out string result))
        {
            ProcessRecognitionResult(result);
        }

        if (isPressedDown)
        {
            commandQueue.Enqueue(new VoskCommand { type = VoskCommandType.Reset });
            Debug.Log($"<color=#00FF00>[Vosk] 🎤 音声入力の受付を開始しました</color>");
        }

        int currentPosition = Microphone.GetPosition(microphoneDevice);
        if (currentPosition >= 0 && lastSamplePosition != currentPosition)
        {
            int sampleCount = currentPosition - lastSamplePosition;
            if (sampleCount < 0) sampleCount += audioClip.samples;

            float[] samples = new float[sampleCount];
            audioClip.GetData(samples, lastSamplePosition);
            lastSamplePosition = currentPosition;

            // 離したフレームの語尾も、確定要求より先に必ずVoskへ渡す。
            if (isHolding || isReleased)
            {
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
            Debug.Log($"<color=#FF8800>[Vosk] 🛑 音声入力の受付を終了しました</color>");
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

        isShuttingDown = true;
        if (workerThread != null && workerThread.IsAlive)
        {
            workerThread.Join(500); // Wait up to 500ms for thread to end
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
