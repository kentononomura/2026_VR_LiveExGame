using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Vosk;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using UnityEngine.Animations.Rigging;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// We use the KeywordReaction class defined in VoiceReactionManager.cs

public enum VoiceLookAtMode
{
    Legacy,
    Stabilized
}

public enum VoiceReactionPresentationMode
{
    Legacy,
    Coordinated
}

public class TestSceneVoiceManager : MonoBehaviour
{
    [Header("Vosk Settings")]
    public string modelFolderName = "vosk-model-small-ja-0.22";
    public string customMicrophoneName = "";
    public bool showRecognitionLog = true;

    [Tooltip("一般日本語の自由認識ではなく、下のコマンド文法から認識結果を選ばせます。")]
    [SerializeField] private bool constrainRecognitionToCommands = true;

    [Tooltip("Voskへ渡すコマンド文法です。日本語モデルの単語境界に合わせ、語の間を半角スペースで区切ります。")]
    [SerializeField] private List<string> recognitionGrammarPhrases = new List<string>
    {
        "こっち 向いて",
        "こっち 見て",
        "手 を 振って",
        "手 振って",
        "かわいい",
        "可愛い",
        "かわいい ね",
        "ユニティちゃん",
        "ユニティ ちゃん",
        "[unk]"
    };

    [Header("Input Settings")]
#if ENABLE_INPUT_SYSTEM
    [Tooltip("左手トリガーで音声認識を開始します")]
    public InputAction pushToTalkAction = new InputAction("PushToTalk", InputActionType.Button, "<XRController>{LeftHand}/triggerPressed");
#endif

    [Header("Keywords & Reactions")]
    public List<KeywordReaction> keywordReactions = new List<KeywordReaction>
    {
        new KeywordReaction { commandId = "LookAt", keyword = "こっちむいて", reactionName = "smile1@unitychan", bodyReactionName = "" },
        new KeywordReaction { commandId = "Wave", keyword = "手振って", reactionName = "smile2@unitychan", bodyReactionName = "Waving", bodyReactionLayerName = "ArmReactionLayer", bodyReturnStartNormalizedTime = 0.87f, bodyReturnBlendDuration = 0.7f },
        new KeywordReaction { commandId = "Cute", keyword = "かわいい", reactionName = "smile3@unitychan", bodyReactionName = "Kiss", bodyReturnStartNormalizedTime = 0.875f, bodyReturnBlendDuration = 0.9f },
        new KeywordReaction { commandId = "Default", keyword = "デフォルト", reactionName = "default@unitychan", bodyReactionName = "" }
    };

    [Header("Recognition Matching")]
    [Tooltip("完全一致しなかった認識結果を、レーベンシュタイン距離による類似度で救済します。")]
    [SerializeField] private bool enableFuzzyMatching = true;

    [Tooltip("発話途中で曖昧一致を成立させる類似度です。誤反応を防ぐため高めに設定します。")]
    [Range(0.5f, 1f)]
    [SerializeField] private float partialSimilarityThreshold = 0.9f;

    [Tooltip("ボタンを離した後の確定結果を救済する類似度です。")]
    [Range(0.5f, 1f)]
    [SerializeField] private float finalSimilarityThreshold = 0.8f;

    [Tooltip("途中結果の曖昧一致で、同じ候補が安定して認識される必要がある時間（秒）です。")]
    [Min(0f)]
    [SerializeField] private float partialStableDuration = 0.2f;

    [Tooltip("曖昧一致を許可するキーワードの最低文字数です。短い単語の誤反応を防ぎます。")]
    [Min(1)]
    [SerializeField] private int fuzzyMinimumCharacters = 4;

    [Tooltip("1位と2位の類似度に必要な差です。候補が紛らわしい場合は反応させません。")]
    [Range(0f, 0.5f)]
    [SerializeField] private float minimumBestMatchMargin = 0.05f;

    [Header("Push To Talk Audio Buffer")]
    [Tooltip("トリガーを押す直前から認識へ含める時間です。語頭の欠けを防ぎます。")]
    [Range(0f, 0.5f)]
    [SerializeField] private float preRollDuration = 0.2f;

    [Tooltip("トリガーを離した後も認識へ含める時間です。語尾の欠けを防ぎます。")]
    [Range(0f, 0.5f)]
    [SerializeField] private float postRollDuration = 0.25f;

    [Header("Reaction Settings")]
    public float lookAtDuration = 3f;
    public float rigBlendSpeed = 5f;

    [Tooltip("Legacyは従来の上半身追従、CoordinatedはRoot Motionを保護した安定化上半身追従を使用します。Play開始後の変更は反映されません。")]
    [SerializeField] private VoiceReactionPresentationMode presentationMode =
        VoiceReactionPresentationMode.Coordinated;

    [Tooltip("体リアクションのレイヤーWeightを滑らかに1へ上げる時間（秒）です。")]
    [Min(0f)]
    [SerializeField] private float bodyReactionBlendInDuration = 0.3f;

    [Tooltip("上半身リアクションを再生してからダンスへ戻し始めるまでの時間（秒）です。")]
    [Min(0f)]
    [SerializeField] private float bodyReactionDuration = 2.5f;

    [Tooltip("上半身リアクションのWeightを0へ戻す時間（秒）です。急な姿勢変化を防ぎます。")]
    [Min(0.01f)]
    [SerializeField] private float bodyReturnBlendDuration = 0.5f;

    // 旧バージョンのシーン保存値との互換性を維持するため残す。
    // Root Motionを曲げる原因になるため、現在の実装では使用しない。
#pragma warning disable 0414
    [SerializeField, HideInInspector]
    [Range(0.05f, 1f)]
    private float characterFacingSmoothTime = 0.3f;

    [SerializeField, HideInInspector]
    [Range(-180f, 180f)]
    private float characterFacingYawOffset = 0f;
#pragma warning restore 0414

    [Tooltip("VRカメラ位置の微細な揺れを目線ターゲットへ反映しにくくする時間（秒）です。")]
    [Range(0f, 0.5f)]
    [SerializeField] private float aimTargetSmoothTime = 0.08f;

    [Tooltip("音声リアクションの表情を維持してからデフォルト表情へ戻すまでの時間（秒）です。")]
    [Min(0f)]
    [SerializeField] private float faceReactionDuration = 3f;

    [Header("Upper Body Look At")]
    [Tooltip("胸上部・首・頭をプレイヤーへ向けます。キャラクター本体と足元は回転させません。")]
    [SerializeField] private bool enableUpperBodyLookAt = true;

    [Tooltip("Legacyは従来の5ボーン制御、StabilizedはVRトラッキングの微細な揺れを除去して向けます。変更後はPlayし直してください。")]
    [SerializeField] private VoiceLookAtMode lookAtMode = VoiceLookAtMode.Stabilized;

    [Header("Stabilized Look At")]
    [Tooltip("HMD方向の変化を滑らかにする時間です。")]
    [Range(0f, 0.5f)]
    [SerializeField] private float stabilizedDirectionSmoothTime = 0.18f;

    [Tooltip("この角度以内のHMD方向の変化を無視し、VRトラッキングの微細な揺れを抑えます。")]
    [Range(0f, 10f)]
    [SerializeField] private float stabilizedDirectionDeadZone = 1.5f;

    [Tooltip("安定化した方向上にAimTargetを置く距離です。HMDとの実距離変化をAimへ伝えません。")]
    [Min(0.1f)]
    [SerializeField] private float stabilizedTargetDistance = 2f;

    [Range(0f, 1f)]
    [SerializeField] private float stabilizedUpperChestWeight = 0.12f;

    [Range(0f, 1f)]
    [SerializeField] private float stabilizedHeadWeight = 0.55f;

    [Range(0f, 90f)]
    [SerializeField] private float stabilizedUpperChestMaxAngle = 40f;

    [Range(0f, 120f)]
    [SerializeField] private float stabilizedHeadMaxAngle = 50f;

    [Tooltip("プレイヤーを見始めるブレンド時間です。")]
    [Min(0.01f)]
    [SerializeField] private float stabilizedRigBlendInDuration = 0.4f;

    [Tooltip("ダンスの姿勢へ目線制御を戻すブレンド時間です。")]
    [Min(0.01f)]
    [SerializeField] private float stabilizedRigBlendOutDuration = 0.7f;

    [Tooltip("キャラクター正面から目線ターゲットを許可する左右の最大角度です。後方のターゲットによるAimの反転を防ぎます。")]
    [Range(0f, 89f)]
    [SerializeField] private float stabilizedMaxTargetYaw = 60f;

    [Tooltip("目線ターゲットを許可する上下の最大角度です。")]
    [Range(0f, 60f)]
    [SerializeField] private float stabilizedMaxTargetPitch = 25f;

    [Header("Eye Contact")]
    [Tooltip("両目がプレイヤーを見始めるブレンド時間です。")]
    [Min(0.01f)]
    [SerializeField] private float eyeContactBlendInDuration = 0.35f;

    [Tooltip("両目を元のダンス姿勢へ戻すブレンド時間です。")]
    [Min(0.01f)]
    [SerializeField] private float eyeContactBlendOutDuration = 0.65f;

    [Tooltip("眼球の回転を制限する強さです。大きいほど眼球が横を向きすぎにくくなります。")]
    [Range(0f, 1f)]
    [SerializeField] private float eyeContactClampWeight = 0.65f;

    [Header("Legacy Look At")]

    [Tooltip("背骨下部へ配分する追従強度です。")]
    [Range(0f, 1f)]
    [SerializeField] private float spineLookWeight = 0.1f;

    [Tooltip("胸へ配分する追従強度です。")]
    [Range(0f, 1f)]
    [SerializeField] private float chestLookWeight = 0.15f;

    [Tooltip("胸上部へ配分する追従強度です。")]
    [Range(0f, 1f)]
    [SerializeField] private float upperChestLookWeight = 0.25f;

    [Tooltip("首へ配分する追従強度です。")]
    [Range(0f, 1f)]
    [SerializeField] private float neckLookWeight = 0.2f;

    [Tooltip("頭へ配分する追従強度です。")]
    [Range(0f, 1f)]
    [SerializeField] private float headLookWeight = 0.3f;

    [Tooltip("背骨・胸・首に許可する最大追従角度です。")]
    [Range(0f, 90f)]
    [SerializeField] private float upperBodyMaxLookAngle = 40f;

    [Tooltip("頭に許可する最大追従角度です。")]
    [Range(0f, 120f)]
    [SerializeField] private float headMaxLookAngle = 70f;

    [Header("Voice Point Settings")]
    [Tooltip("距離と左手ペンライト色から、リアクション成立ポイントを計算します。")]
    [SerializeField] private VoicePointEvaluator voicePointEvaluator = new VoicePointEvaluator();

    [Header("Voice Point References (Optional)")]
    [Tooltip("距離計算に使用するプレイヤー位置です。未設定ならVRカメラを自動取得します。")]
    [SerializeField] private Transform playerTransformOverride;

    [Tooltip("距離計算に使用するUnityちゃん位置です。未設定なら既存のFaceUpdate参照を使用します。")]
    [SerializeField] private Transform unityChanTransformOverride;

    [Tooltip("左手ペンライトの既存ゲージコントローラーです。未設定ならSaberのHandTypeから自動取得します。")]
    [SerializeField] private PenlightGaugeController leftPenlightOverride;

    private Model model;
    private VoskRecognizer recognizer;
    private string microphoneDevice;
    private AudioClip audioClip;
    private int lastSamplePosition = 0;
    private bool isListening = false;
    private bool isModelLoaded = false;
    private float nextMicrophoneStartAttemptTime;
    private bool isShuttingDown = false;
    private const int SampleRate = 16000;
    private readonly List<UnityEngine.XR.InputDevice> leftHandDevices = new List<UnityEngine.XR.InputDevice>(1);

    private bool isLeftTriggerDown = false;
    private bool hasHandledRecognitionThisPress;
    private KeywordReaction stablePartialCandidate;
    private float stablePartialSince;
    private float[] preRollBuffer;
    private int preRollWriteIndex;
    private int preRollSampleCount;
    private bool shouldSendPreRoll;
    private bool isPostRollActive;
    private int postRollSamplesRemaining;

    [System.Serializable]
    private class VoskRecognitionResult
    {
        public string text;
        public string partial;
    }

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

    // Unity-chan references
    private GameObject unityChanObj;
    private UnityChan.FaceUpdate faceUpdate;
    private Rig targetRig;
    private float targetRigWeight = 0f;
    private MultiAimConstraint stabilizedUpperChestConstraint;
    private VoiceEyeContactIK eyeContactController;
    private LipSyncMouthPriority lipSyncMouthPriority;
    private VoiceReactionVisualFeedback visualFeedback;

    void Start()
    {
        // TestSceneでは右手にスマホカメラを持つため、右手のペンライト（Saber）を非表示にする
        StartCoroutine(HideRightSaberRoutine());

#if ENABLE_INPUT_SYSTEM
        pushToTalkAction.expectedControlType = "Button";
        pushToTalkAction.Enable();
#endif

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
            Debug.LogError($"[Vosk] TestScene 用モデルを準備できませんでした: {modelPrepareError}");
            yield break;
        }

        string recognitionGrammar = BuildRecognitionGrammarJson();
        Debug.Log($"[Vosk] TestScene 用のモデルロード非同期タスクを起動します: {modelPath}");
        Task.Run(() =>
        {
            try
            {
                model = VoskModelCache.GetOrLoad(modelPath);
                recognizer = string.IsNullOrEmpty(recognitionGrammar)
                    ? new VoskRecognizer(model, SampleRate)
                    : new VoskRecognizer(model, SampleRate, recognitionGrammar);
                recognizer.SetMaxAlternatives(0);
                // キーワード判定では単語ごとの時刻情報を使わないため、JSON生成負荷を抑える。
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
                string mode = string.IsNullOrEmpty(recognitionGrammar) ? "一般日本語" : "コマンド文法制限";
                Debug.Log($"[Vosk] TestScene 用のモデルロードおよび音声認識スレッドが正常に起動しました。認識モード: {mode}");
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
                Thread.Sleep(10);
            }
        }
    }

    private IEnumerator HideRightSaberRoutine()
    {
        // SaberのStart()によるモデル生成を待つため1フレーム待機
        yield return null; 
        
        Saber[] sabers = FindObjectsByType<Saber>(FindObjectsInactive.Include);
        foreach (var saber in sabers)
        {
            if (saber.handType == Saber.HandType.Right)
            {
                saber.enabled = false;
                foreach (Transform child in saber.transform)
                {
                    if (child.name == "SaberVisual" || child.name == "HitboxVisual" || child.name == "PenlightMeterCanvas")
                    {
                        child.gameObject.SetActive(false);
                    }
                }
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
            lastSamplePosition = 0;
            InitializePreRollBuffer();
            string selectedName = string.IsNullOrEmpty(microphoneDevice)
                ? "Quest/Android システム既定マイク"
                : microphoneDevice;
            Debug.Log($"[Vosk] TestScene 音声認識マイクを開始しました: {selectedName}");
        }
        else
        {
            Debug.LogError("[Vosk] マイクの開始に失敗しました。アプリのマイク権限を確認してください。");
        }
    }

    void Update()
    {
        TrySetupUnityChan();

        // Process queued results on main thread
        while (resultQueue.TryDequeue(out string result))
        {
            ProcessRecognitionResult(result);
        }

        // PTT（プッシュ・トゥ・トーク）入力判定を先に実行（早期リターンの前へ！）
        bool isHolding = false;
        bool isTriggerPressed = false;

#if ENABLE_INPUT_SYSTEM
        if (pushToTalkAction.enabled)
        {
            isTriggerPressed = pushToTalkAction.IsPressed();
        }
#endif

        // フォールバック: UnityEngine.XR.InputDeviceから直接トリガーボタン状態を取得 (Quest 2/Quest 3 互換性向上)
        leftHandDevices.Clear();
        UnityEngine.XR.InputDevices.GetDevicesWithCharacteristics(UnityEngine.XR.InputDeviceCharacteristics.Left | UnityEngine.XR.InputDeviceCharacteristics.Controller, leftHandDevices);
        if (leftHandDevices.Count > 0)
        {
            if (leftHandDevices[0].TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out bool xrTriggerPressed))
            {
                isTriggerPressed |= xrTriggerPressed;
            }
        }

        // デバッグ用のトリガー押し込み値（アナログ）
        float debugTriggerValue = 0f;
        if (leftHandDevices.Count > 0)
        {
            leftHandDevices[0].TryGetFeatureValue(UnityEngine.XR.CommonUsages.trigger, out debugTriggerValue);
        }

        bool shouldFinalize = false;
        if (isTriggerPressed)
        {
            if (!isLeftTriggerDown)
            {
                isLeftTriggerDown = true;
                isPostRollActive = false;
                postRollSamplesRemaining = 0;
                shouldSendPreRoll = true;
                ResetRecognitionMatchState();
                if (isListening && recognizer != null)
                {
                    commandQueue.Enqueue(new VoskCommand { type = VoskCommandType.Reset });
                    Debug.Log($"<color=#00FF00>[Vosk] 🎤 左手トリガー検知：音声入力の受付を開始しました (TriggerValue: {debugTriggerValue:F2})</color>");
                }
                else
                {
                    Debug.LogWarning($"<color=#FFAA00>[Vosk] 🎤 左手トリガーを検知しましたが、音声認識の準備が整っていません（モデルロード状態: {isModelLoaded}, マイク録音状態: {isListening}）</color>");
                }
            }
            isHolding = true;
        }
        else
        {
            if (isLeftTriggerDown)
            {
                isLeftTriggerDown = false;
                if (isListening && recognizer != null)
                {
                    postRollSamplesRemaining = Mathf.CeilToInt(postRollDuration * SampleRate);
                    isPostRollActive = postRollSamplesRemaining > 0;
                    shouldFinalize = !isPostRollActive;
                }
            }
        }

        // ここでマイク/モデルロードのチェックと起動を行う
        if (isModelLoaded && !isListening)
        {
            StartMicrophone();
            return;
        }

        if (!isListening || recognizer == null || audioClip == null) return;

        int currentPosition = Microphone.GetPosition(microphoneDevice);
        if (currentPosition >= 0 && lastSamplePosition != currentPosition)
        {
            int sampleCount = currentPosition - lastSamplePosition;
            if (sampleCount < 0) sampleCount += audioClip.samples;

            float[] samples = new float[sampleCount];
            audioClip.GetData(samples, lastSamplePosition);
            lastSamplePosition = currentPosition;

            if (shouldSendPreRoll && (isHolding || isPostRollActive || shouldFinalize))
            {
                EnqueuePreRollAudio();
                shouldSendPreRoll = false;
            }

            // 離した後も postRollDuration 分の語尾を Vosk へ渡してから確定する。
            if (isHolding || isPostRollActive || shouldFinalize)
            {
                EnqueueAudioSamples(samples);
            }
            else
            {
                AppendPreRollSamples(samples);
            }

            if (isPostRollActive && !isHolding)
            {
                postRollSamplesRemaining -= sampleCount;
                if (postRollSamplesRemaining <= 0)
                {
                    isPostRollActive = false;
                    shouldFinalize = true;
                }
            }
        }

        if (shouldFinalize)
        {
            commandQueue.Enqueue(new VoskCommand { type = VoskCommandType.FinalResult });
            Debug.Log($"<color=#FF8800>[Vosk] 🛑 音声入力の受付を終了しました</color>");
        }
    }

    private string BuildRecognitionGrammarJson()
    {
        if (!constrainRecognitionToCommands || recognitionGrammarPhrases == null)
        {
            return null;
        }

        StringBuilder json = new StringBuilder("[");
        int phraseCount = 0;
        foreach (string phrase in recognitionGrammarPhrases)
        {
            if (string.IsNullOrWhiteSpace(phrase)) continue;

            if (phraseCount > 0) json.Append(',');
            json.Append('"');
            foreach (char character in phrase.Trim())
            {
                if (character == '"' || character == '\\') json.Append('\\');
                json.Append(character);
            }
            json.Append('"');
            phraseCount++;
        }
        json.Append(']');

        if (phraseCount == 0)
        {
            Debug.LogWarning("[Vosk] コマンド文法が空のため、一般日本語認識に切り替えます。");
            return null;
        }

        Debug.Log($"[Vosk] コマンド文法を使用します: {json}");
        return json.ToString();
    }

    private void InitializePreRollBuffer()
    {
        int capacity = Mathf.CeilToInt(preRollDuration * SampleRate);
        preRollBuffer = capacity > 0 ? new float[capacity] : null;
        preRollWriteIndex = 0;
        preRollSampleCount = 0;
        shouldSendPreRoll = false;
        isPostRollActive = false;
        postRollSamplesRemaining = 0;
    }

    private void AppendPreRollSamples(float[] samples)
    {
        if (preRollBuffer == null || preRollBuffer.Length == 0 || samples == null) return;

        foreach (float sample in samples)
        {
            preRollBuffer[preRollWriteIndex] = sample;
            preRollWriteIndex = (preRollWriteIndex + 1) % preRollBuffer.Length;
            if (preRollSampleCount < preRollBuffer.Length) preRollSampleCount++;
        }
    }

    private void EnqueuePreRollAudio()
    {
        if (preRollBuffer == null || preRollSampleCount == 0) return;

        float[] orderedSamples = new float[preRollSampleCount];
        int startIndex = (preRollWriteIndex - preRollSampleCount + preRollBuffer.Length) % preRollBuffer.Length;
        for (int i = 0; i < preRollSampleCount; i++)
        {
            orderedSamples[i] = preRollBuffer[(startIndex + i) % preRollBuffer.Length];
        }

        EnqueueAudioSamples(orderedSamples);
        preRollSampleCount = 0;
        preRollWriteIndex = 0;
    }

    private void EnqueueAudioSamples(float[] samples)
    {
        if (samples == null || samples.Length == 0) return;

        float maxVal = 0f;
        foreach (float sample in samples)
        {
            float absVal = Mathf.Abs(sample);
            if (absVal > maxVal) maxVal = absVal;
        }
        if (maxVal < 0.001f)
        {
            Debug.LogWarning("[Vosk] 🎤 音声データが極端に小さいか無音です。マイクがミュートされているか、正しいマイクデバイスが選択されていない可能性があります。");
        }

        byte[] byteData = VoskPcmUtility.RentAndConvert(samples, out int byteCount);
        commandQueue.Enqueue(new VoskCommand
        {
            type = VoskCommandType.ProcessAudio,
            audioData = byteData,
            audioLength = byteCount
        });
    }

    private Animator targetAnimator;
    private Coroutine bodyReactionCoroutine;
    private Coroutine faceReactionCoroutine;
    private Coroutine lookAtCoroutine;
    private Coroutine rigBlendCoroutine;
    private Coroutine upperChestBlendCoroutine;
    private int activeBodyLayerIndex = -1;

    private void TrySetupUnityChan()
    {
        if (unityChanObj != null) return;

        // StageDirector spawns UnityChan at runtime, so we wait until she appears
        var face = FindAnyObjectByType<UnityChan.FaceUpdate>();
        if (face != null)
        {
            faceUpdate = face;
            unityChanObj = face.gameObject;
            targetAnimator = unityChanObj.GetComponentInChildren<Animator>();
            lipSyncMouthPriority = unityChanObj.GetComponent<LipSyncMouthPriority>();
            if (lipSyncMouthPriority == null)
            {
                lipSyncMouthPriority = unityChanObj.AddComponent<LipSyncMouthPriority>();
            }
            SetupAnimationRigging(unityChanObj);

        }
    }

    private void SetupAnimationRigging(GameObject character)
    {
        // 1. Add RigBuilder
        var rigBuilder = character.GetComponent<RigBuilder>();
        if (rigBuilder == null) rigBuilder = character.AddComponent<RigBuilder>();

        // 2. Add Rig
        var rigObj = new GameObject("VoiceReactionRig");
        rigObj.transform.SetParent(character.transform, false);
        targetRig = rigObj.AddComponent<Rig>();
        targetRig.weight = 0f;
        rigBuilder.layers.Add(new RigLayer(targetRig));

        if (targetAnimator == null || !targetAnimator.isHuman)
        {
            Debug.LogWarning("TestSceneVoiceManager: Humanoid Animatorが見つからないため、上半身の目線制御を設定できません。");
            return;
        }

        // 3. Create a shared target that follows the player's VR camera.
        Camera mainCam = null;
        Unity.XR.CoreUtils.XROrigin xrOrigin =
            FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
        if (xrOrigin != null)
        {
            mainCam = xrOrigin.Camera;
        }
        if (mainCam == null)
        {
            mainCam = Camera.main;
        }
        
        var aimTarget = new GameObject("AimTarget");
        aimTarget.transform.SetParent(rigObj.transform, false);

        if (lookAtMode == VoiceLookAtMode.Stabilized)
        {
            var follower = aimTarget.AddComponent<StabilizedAimTargetFollower>();
            follower.targetCamera = mainCam;
            follower.originTransform = character.transform;
            Transform head = targetAnimator.GetBoneTransform(HumanBodyBones.Head);
            follower.originLocalOffset = head != null
                ? character.transform.InverseTransformPoint(head.position)
                : new Vector3(0f, 1.4f, 0f);
            follower.directionSmoothTime = stabilizedDirectionSmoothTime;
            follower.directionDeadZoneDegrees = stabilizedDirectionDeadZone;
            follower.targetDistance = stabilizedTargetDistance;
            follower.maxYawDegrees = stabilizedMaxTargetYaw;
            follower.maxPitchDegrees = stabilizedMaxTargetPitch;
        }
        else
        {
            var follower = aimTarget.AddComponent<AimTargetFollower>();
            follower.targetCamera = mainCam;
            follower.smoothTime = aimTargetSmoothTime;
        }

        if (mainCam != null)
        {
            aimTarget.transform.position = mainCam.transform.position;
        }

        SetupEyeContact(aimTarget.transform);

        // 4. Root Motionの進行方向を変えないよう、キャラクタールートは回さない。
        //    安定化した胸上部・首・頭のAimだけでプレイヤーを見る。
        if (lookAtMode == VoiceLookAtMode.Stabilized)
        {
            if (enableUpperBodyLookAt)
            {
                HumanBodyBones upperBodyBone =
                    targetAnimator.GetBoneTransform(HumanBodyBones.UpperChest) != null
                        ? HumanBodyBones.UpperChest
                        : HumanBodyBones.Chest;
                stabilizedUpperChestConstraint = AddUpperBodyAimConstraint(
                    rigObj.transform, aimTarget.transform, upperBodyBone,
                    "StabilizedUpperChestAimConstraint", stabilizedUpperChestWeight,
                    stabilizedUpperChestMaxAngle, false);

                // 胸と頭の間で回転解が競合しやすいため、Stabilizedでは首Aimを使用しない。
            }

            AddUpperBodyAimConstraint(
                rigObj.transform, aimTarget.transform, HumanBodyBones.Head,
                "StabilizedHeadAimConstraint", stabilizedHeadWeight,
                stabilizedHeadMaxAngle, true);
        }
        else
        {
            // Legacy: 以前の5ボーン制御を数値も含めてそのまま保持する。
            if (enableUpperBodyLookAt)
            {
                AddUpperBodyAimConstraint(
                    rigObj.transform, aimTarget.transform, HumanBodyBones.Spine,
                    "SpineAimConstraint", spineLookWeight, upperBodyMaxLookAngle, false);
                AddUpperBodyAimConstraint(
                    rigObj.transform, aimTarget.transform, HumanBodyBones.Chest,
                    "ChestAimConstraint", chestLookWeight, upperBodyMaxLookAngle, false);
                AddUpperBodyAimConstraint(
                    rigObj.transform, aimTarget.transform, HumanBodyBones.UpperChest,
                    "UpperChestAimConstraint", upperChestLookWeight, upperBodyMaxLookAngle, false);
                AddUpperBodyAimConstraint(
                    rigObj.transform, aimTarget.transform, HumanBodyBones.Neck,
                    "NeckAimConstraint", neckLookWeight, upperBodyMaxLookAngle, false);
            }

            // Unity-chan's head local Y axis points out of her face.
            AddUpperBodyAimConstraint(
                rigObj.transform, aimTarget.transform, HumanBodyBones.Head,
                "HeadAimConstraint", headLookWeight, headMaxLookAngle, true);
        }

        rigBuilder.Build();
        Debug.Log($"TestSceneVoiceManager: Voice look-at rig setup. Presentation: {presentationMode}, Aim: {lookAtMode}");
    }

    private void SetupEyeContact(Transform aimTarget)
    {
        if (targetAnimator == null || aimTarget == null) return;

        Transform leftEye = targetAnimator.GetBoneTransform(HumanBodyBones.LeftEye);
        Transform rightEye = targetAnimator.GetBoneTransform(HumanBodyBones.RightEye);
        if (leftEye == null || rightEye == null)
        {
            Debug.LogWarning(
                "[VoiceReaction] Humanoidの両目ボーンが見つからないため、" +
                "アイコンタク制御を無効にします。");
            return;
        }

        // Prefabの従来IKLookAtは眼球Weightが0固定で、
        // ここで追加する眼球専用IKとSetLookAtWeightが競合するためTestScene内だけ無効化する。
        UnityChan.IKLookAt legacyLookAt =
            unityChanObj != null
                ? unityChanObj.GetComponentInChildren<UnityChan.IKLookAt>(true)
                : null;
        if (legacyLookAt != null)
        {
            legacyLookAt.enabled = false;
        }

        eyeContactController =
            targetAnimator.GetComponent<VoiceEyeContactIK>();
        if (eyeContactController == null)
        {
            eyeContactController =
                targetAnimator.gameObject.AddComponent<VoiceEyeContactIK>();
        }

        eyeContactController.Configure(
            targetAnimator,
            aimTarget,
            eyeContactClampWeight);
    }

    private MultiAimConstraint AddUpperBodyAimConstraint(
        Transform rigParent,
        Transform aimTarget,
        HumanBodyBones bone,
        string constraintName,
        float weight,
        float maxAngle,
        bool isHead)
    {
        Transform boneTransform = targetAnimator.GetBoneTransform(bone);
        if (boneTransform == null || weight <= 0f)
        {
            return null;
        }

        GameObject constraintObject = new GameObject(constraintName);
        constraintObject.transform.SetParent(rigParent, false);
        MultiAimConstraint aimConstraint = constraintObject.AddComponent<MultiAimConstraint>();
        aimConstraint.weight = weight;

        MultiAimConstraintData data = aimConstraint.data;
        data.constrainedObject = boneTransform;
        WeightedTransformArray sources = data.sourceObjects;
        sources.Clear();
        sources.Add(new WeightedTransform(aimTarget, 1f));
        data.sourceObjects = sources;
        data.maintainOffset = false;
        data.limits = new Vector2(-maxAngle, maxAngle);
        data.worldUpType = MultiAimConstraintData.WorldUpType.SceneUp;

        if (isHead)
        {
            data.aimAxis = MultiAimConstraintData.Axis.Y;
            data.upAxis = MultiAimConstraintData.Axis.Z;
            data.constrainedXAxis = true;
            data.constrainedYAxis = true;
            data.constrainedZAxis = false;
        }
        else
        {
            data.aimAxis = MultiAimConstraintData.Axis.Z;
            data.upAxis = MultiAimConstraintData.Axis.Y;
            // The torso only twists horizontally, preserving the song choreography's posture.
            data.constrainedXAxis = false;
            data.constrainedYAxis = true;
            data.constrainedZAxis = false;
        }

        aimConstraint.data = data;
        return aimConstraint;
    }

    private void ProcessRecognitionResult(string jsonResult)
    {
        if (string.IsNullOrEmpty(jsonResult) || hasHandledRecognitionThisPress) return;

        VoskRecognitionResult recognitionResult;
        try
        {
            recognitionResult = JsonUtility.FromJson<VoskRecognitionResult>(jsonResult);
        }
        catch (System.ArgumentException)
        {
            return;
        }

        if (recognitionResult == null) return;

        bool isFinalResult = recognitionResult.text != null;
        string recognizedText = isFinalResult ? recognitionResult.text : recognitionResult.partial;
        if (string.IsNullOrWhiteSpace(recognizedText)) return;

        if (showRecognitionLog && isFinalResult)
        {
            Debug.Log($"[Vosk TestScene 音声認識] {jsonResult}");
        }

        string normalizedText = NormalizeRecognitionText(recognizedText);
        if (normalizedText.Length == 0) return;

        KeywordReaction matchedReaction = FindExactMatch(normalizedText);
        float matchSimilarity = matchedReaction != null ? 1f : 0f;

        if (matchedReaction == null && enableFuzzyMatching)
        {
            float threshold = isFinalResult ? finalSimilarityThreshold : partialSimilarityThreshold;
            matchedReaction = FindBestFuzzyMatch(normalizedText, threshold, out matchSimilarity);
        }

        if (matchedReaction == null)
        {
            stablePartialCandidate = null;
            return;
        }

        if (!isFinalResult && matchSimilarity < 1f)
        {
            if (stablePartialCandidate != matchedReaction)
            {
                stablePartialCandidate = matchedReaction;
                stablePartialSince = Time.unscaledTime;
                return;
            }

            if (Time.unscaledTime - stablePartialSince < partialStableDuration)
            {
                return;
            }
        }

        hasHandledRecognitionThisPress = true;
        Debug.Log($"[Vosk] キーワード検知: {matchedReaction.keyword} / 認識: {recognizedText} / 類似度: {matchSimilarity:F2} -> 表情: {matchedReaction.reactionName} / 体: {matchedReaction.bodyReactionName}");
        ExecuteReaction(matchedReaction);
    }

    private void ExecuteReaction(KeywordReaction kr)
    {
        // 音声認識と既存リアクション実行の間で、今回分だけポイント判定する。
        // ポイントは保持・蓄積しない。
        if (!EvaluateCurrentVoicePoint())
        {
            return;
        }

        bool reactionStarted = false;

        // 表情を変更
        if (faceUpdate != null && !string.IsNullOrEmpty(kr.reactionName))
        {
            if (faceReactionCoroutine != null)
            {
                StopCoroutine(faceReactionCoroutine);
            }

            faceUpdate.OnCallChangeFace(kr.reactionName);
            if (lipSyncMouthPriority != null)
            {
                lipSyncMouthPriority.PrioritizeFor(faceReactionDuration);
            }
            faceReactionCoroutine = StartCoroutine(ResetFaceReactionRoutine(faceReactionDuration));
            reactionStarted = true;
        }

        // 体のアニメーションを変更（上半身レイヤー）
        if (targetAnimator != null && !string.IsNullOrEmpty(kr.bodyReactionName))
        {
            string requestedLayerName =
                presentationMode == VoiceReactionPresentationMode.Legacy
                    ? "ReactionLayer"
                    : (!string.IsNullOrWhiteSpace(kr.bodyReactionLayerName)
                        ? kr.bodyReactionLayerName
                        : "ReactionLayer");
            int layerIndex = targetAnimator.GetLayerIndex(requestedLayerName);
            if (layerIndex == -1 && requestedLayerName != "ReactionLayer")
            {
                Debug.LogWarning($"[VoiceReaction] Animatorレイヤー '{requestedLayerName}' が見つからないためReactionLayerを使用します。");
                layerIndex = targetAnimator.GetLayerIndex("ReactionLayer");
            }

            if (layerIndex != -1)
            {
                if (bodyReactionCoroutine != null)
                {
                    StopCoroutine(bodyReactionCoroutine);
                }

                if (activeBodyLayerIndex != -1 && activeBodyLayerIndex != layerIndex)
                {
                    targetAnimator.SetLayerWeight(activeBodyLayerIndex, 0f);
                    targetAnimator.Play("Empty", activeBodyLayerIndex, 0f);
                }

                targetAnimator.CrossFade(kr.bodyReactionName, 0.2f, layerIndex);
                activeBodyLayerIndex = layerIndex;
                if (presentationMode == VoiceReactionPresentationMode.Coordinated)
                {
                    bodyReactionCoroutine = StartCoroutine(
                        PlayBodyReactionRoutine(layerIndex, kr));
                }
                else
                {
                    targetAnimator.SetLayerWeight(layerIndex, 1f);
                    bodyReactionCoroutine = StartCoroutine(
                        ResetBodyReactionRoutine(bodyReactionDuration, layerIndex));
                }
                reactionStarted = true;
            }
        }

        // 目線を合わせる。腕リアクションでは弱め、ダンス姿勢との競合を防ぐ。
        if (targetRig != null || eyeContactController != null)
        {
            if (lookAtCoroutine != null)
            {
                StopCoroutine(lookAtCoroutine);
                lookAtCoroutine = null;
            }

            float lookAtStrength = ResolveLookAtStrength(kr);
            float eyeContactStrength = ResolveEyeContactStrength(kr);
            float upperBodyFacingStrength = ResolveUpperBodyFacingStrength(kr);
            SetGazeTargetWeights(
                lookAtStrength,
                eyeContactStrength,
                upperBodyFacingStrength);
            if (lookAtStrength > 0f || eyeContactStrength > 0f ||
                upperBodyFacingStrength > 0f)
            {
                lookAtCoroutine = StartCoroutine(
                    ResetLookAtRoutine(ResolveLookAtHoldDuration(kr)));
                reactionStarted = true;
            }
        }

        if (reactionStarted)
        {
            if (visualFeedback == null)
            {
                visualFeedback = GetComponent<VoiceReactionVisualFeedback>();
                if (visualFeedback == null)
                {
                    visualFeedback = gameObject.AddComponent<VoiceReactionVisualFeedback>();
                }
            }

            string commandId = !string.IsNullOrWhiteSpace(kr.commandId)
                ? kr.commandId
                : (!string.IsNullOrWhiteSpace(kr.bodyReactionName)
                    ? kr.bodyReactionName
                    : kr.reactionName);
            visualFeedback.Play(
                commandId,
                targetAnimator,
                unityChanObj != null ? unityChanObj.transform : null);
        }
    }

    private KeywordReaction FindExactMatch(string normalizedText)
    {
        foreach (KeywordReaction reaction in keywordReactions)
        {
            string keyword = NormalizeRecognitionText(reaction.keyword);
            if (keyword.Length > 0 && normalizedText.Contains(keyword))
            {
                return reaction;
            }
        }

        return null;
    }

    private KeywordReaction FindBestFuzzyMatch(string normalizedText, float threshold, out float bestSimilarity)
    {
        KeywordReaction bestReaction = null;
        bestSimilarity = 0f;
        float secondBestSimilarity = 0f;

        foreach (KeywordReaction reaction in keywordReactions)
        {
            string keyword = NormalizeRecognitionText(reaction.keyword);
            if (keyword.Length < fuzzyMinimumCharacters) continue;

            float similarity = CalculateBestSubstringSimilarity(normalizedText, keyword);
            if (similarity > bestSimilarity)
            {
                secondBestSimilarity = bestSimilarity;
                bestSimilarity = similarity;
                bestReaction = reaction;
            }
            else if (similarity > secondBestSimilarity)
            {
                secondBestSimilarity = similarity;
            }
        }

        if (bestSimilarity < threshold || bestSimilarity - secondBestSimilarity < minimumBestMatchMargin)
        {
            bestSimilarity = 0f;
            return null;
        }

        return bestReaction;
    }

    private static float CalculateBestSubstringSimilarity(string text, string keyword)
    {
        if (text.Length == 0 || keyword.Length == 0) return 0f;

        float best = CalculateSimilarity(text, keyword);
        int lengthDifferenceAllowance = Mathf.Max(1, Mathf.CeilToInt(keyword.Length * 0.35f));
        int minLength = Mathf.Max(1, keyword.Length - lengthDifferenceAllowance);
        int maxLength = Mathf.Min(text.Length, keyword.Length + lengthDifferenceAllowance);

        for (int length = minLength; length <= maxLength; length++)
        {
            for (int start = 0; start + length <= text.Length; start++)
            {
                best = Mathf.Max(best, CalculateSimilarity(text.Substring(start, length), keyword));
            }
        }

        return best;
    }

    private static float CalculateSimilarity(string left, string right)
    {
        int maxLength = Mathf.Max(left.Length, right.Length);
        return maxLength == 0 ? 1f : 1f - (float)CalculateLevenshteinDistance(left, right) / maxLength;
    }

    private static int CalculateLevenshteinDistance(string left, string right)
    {
        int[] previous = new int[right.Length + 1];
        int[] current = new int[right.Length + 1];

        for (int j = 0; j <= right.Length; j++) previous[j] = j;

        for (int i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= right.Length; j++)
            {
                int substitutionCost = left[i - 1] == right[j - 1] ? 0 : 1;
                current[j] = Mathf.Min(
                    Mathf.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + substitutionCost);
            }

            int[] swap = previous;
            previous = current;
            current = swap;
        }

        return previous[right.Length];
    }

    private static string NormalizeRecognitionText(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        string normalized = text.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        StringBuilder builder = new StringBuilder(normalized.Length);

        foreach (char originalCharacter in normalized)
        {
            char character = originalCharacter;
            if (character >= '\u30A1' && character <= '\u30F6')
            {
                character = (char)(character - '\u30A1' + '\u3041');
            }

            UnicodeCategory category = char.GetUnicodeCategory(character);
            if (!char.IsWhiteSpace(character) &&
                category != UnicodeCategory.Control &&
                category != UnicodeCategory.Format &&
                category != UnicodeCategory.ConnectorPunctuation &&
                category != UnicodeCategory.DashPunctuation &&
                category != UnicodeCategory.OpenPunctuation &&
                category != UnicodeCategory.ClosePunctuation &&
                category != UnicodeCategory.InitialQuotePunctuation &&
                category != UnicodeCategory.FinalQuotePunctuation &&
                category != UnicodeCategory.OtherPunctuation)
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private void ResetRecognitionMatchState()
    {
        hasHandledRecognitionThisPress = false;
        stablePartialCandidate = null;
        stablePartialSince = 0f;
    }

    private bool EvaluateCurrentVoicePoint()
    {
        Transform playerTransform = ResolvePlayerTransform();
        Transform unityChanTransform =
            unityChanTransformOverride != null
                ? unityChanTransformOverride
                : unityChanObj != null ? unityChanObj.transform : null;
        PenlightGaugeController leftPenlight = ResolveLeftPenlight();

        return voicePointEvaluator != null &&
               voicePointEvaluator.Evaluate(playerTransform, unityChanTransform, leftPenlight);
    }

    private Transform ResolvePlayerTransform()
    {
        if (playerTransformOverride != null)
        {
            return playerTransformOverride;
        }

        Unity.XR.CoreUtils.XROrigin xrOrigin =
            FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
        if (xrOrigin != null && xrOrigin.Camera != null)
        {
            return xrOrigin.Camera.transform;
        }

        if (Camera.main != null)
        {
            return Camera.main.transform;
        }

        return null;
    }

    private PenlightGaugeController ResolveLeftPenlight()
    {
        if (leftPenlightOverride != null)
        {
            return leftPenlightOverride;
        }

        PenlightGaugeController[] controllers =
            FindObjectsByType<PenlightGaugeController>(FindObjectsInactive.Include);
        foreach (PenlightGaugeController controller in controllers)
        {
            if (controller == null) continue;

            Saber saber = controller.saber != null
                ? controller.saber
                : controller.GetComponent<Saber>();
            if (saber != null && saber.handType == Saber.HandType.Left)
            {
                leftPenlightOverride = controller;
                return controller;
            }
        }

        return null;
    }

    private IEnumerator ResetBodyReactionRoutine(float delay, int layerIndex)
    {
        yield return new WaitForSeconds(delay);

        if (targetAnimator == null)
        {
            bodyReactionCoroutine = null;
            yield break;
        }

        float startWeight = targetAnimator.GetLayerWeight(layerIndex);
        float elapsed = 0f;
        while (elapsed < bodyReturnBlendDuration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / bodyReturnBlendDuration);
            float easedProgress = Mathf.SmoothStep(0f, 1f, progress);
            targetAnimator.SetLayerWeight(layerIndex, Mathf.Lerp(startWeight, 0f, easedProgress));
            yield return null;
        }

        // Weightを厳密に0へ確定し、空のOverride Layerとダンスモーションの競合を止める。
        targetAnimator.SetLayerWeight(layerIndex, 0f);
        targetAnimator.Play("Empty", layerIndex, 0f);
        activeBodyLayerIndex = -1;
        bodyReactionCoroutine = null;
    }

    private IEnumerator PlayBodyReactionRoutine(int layerIndex, KeywordReaction reaction)
    {
        if (targetAnimator == null)
        {
            bodyReactionCoroutine = null;
            yield break;
        }

        float startWeight = targetAnimator.GetLayerWeight(layerIndex);
        float elapsed = 0f;
        float blendInDuration = Mathf.Max(0f, bodyReactionBlendInDuration);
        while (elapsed < blendInDuration && targetAnimator != null)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, blendInDuration));
            float easedProgress = SmootherStep(progress);
            targetAnimator.SetLayerWeight(layerIndex, Mathf.Lerp(startWeight, 1f, easedProgress));
            yield return null;
        }

        if (targetAnimator == null)
        {
            bodyReactionCoroutine = null;
            yield break;
        }

        targetAnimator.SetLayerWeight(layerIndex, 1f);

        float returnStartNormalizedTime = reaction != null
            ? reaction.bodyReturnStartNormalizedTime
            : -1f;
        if (returnStartNormalizedTime >= 0f && reaction != null &&
            !string.IsNullOrEmpty(reaction.bodyReactionName))
        {
            yield return WaitForBodyReactionPosition(
                layerIndex,
                reaction.bodyReactionName,
                Mathf.Clamp01(returnStartNormalizedTime));
        }
        else
        {
            float holdDuration = Mathf.Max(0f, bodyReactionDuration - blendInDuration);
            if (holdDuration > 0f)
            {
                yield return new WaitForSeconds(holdDuration);
            }
        }

        if (targetAnimator == null)
        {
            bodyReactionCoroutine = null;
            yield break;
        }

        elapsed = 0f;
        float requestedBlendDuration = reaction != null
            ? reaction.bodyReturnBlendDuration
            : -1f;
        float blendOutDuration = Mathf.Max(
            0.01f,
            requestedBlendDuration > 0f
                ? requestedBlendDuration
                : bodyReturnBlendDuration);
        while (elapsed < blendOutDuration && targetAnimator != null)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / blendOutDuration);
            float easedProgress = SmootherStep(progress);
            targetAnimator.SetLayerWeight(layerIndex, 1f - easedProgress);
            yield return null;
        }

        if (targetAnimator != null)
        {
            targetAnimator.SetLayerWeight(layerIndex, 0f);
            targetAnimator.Play("Empty", layerIndex, 0f);
        }
        activeBodyLayerIndex = -1;
        bodyReactionCoroutine = null;
    }

    private IEnumerator WaitForBodyReactionPosition(
        int layerIndex,
        string stateName,
        float targetNormalizedTime)
    {
        int stateHash = Animator.StringToHash(stateName);
        float safetyElapsed = 0f;
        float safetyTimeout = Mathf.Max(5f, bodyReactionDuration + 2f);

        while (targetAnimator != null && safetyElapsed < safetyTimeout)
        {
            AnimatorStateInfo currentState =
                targetAnimator.GetCurrentAnimatorStateInfo(layerIndex);
            AnimatorStateInfo nextState =
                targetAnimator.GetNextAnimatorStateInfo(layerIndex);

            bool currentIsReaction = currentState.shortNameHash == stateHash;
            bool nextIsReaction = nextState.shortNameHash == stateHash;
            if ((currentIsReaction && currentState.normalizedTime >= targetNormalizedTime) ||
                (nextIsReaction && nextState.normalizedTime >= targetNormalizedTime))
            {
                yield break;
            }

            safetyElapsed += Time.deltaTime;
            yield return null;
        }

        if (targetAnimator != null)
        {
            Debug.LogWarning(
                $"[VoiceReaction] '{stateName}' の再生位置を確認できなかったため、" +
                "安全のためダンスへ戻します。");
        }
    }

    private static float SmootherStep(float value)
    {
        float t = Mathf.Clamp01(value);
        return t * t * t * (t * (t * 6f - 15f) + 10f);
    }

    private IEnumerator ResetFaceReactionRoutine(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (faceUpdate != null)
        {
            faceUpdate.OnCallChangeFace("default@unitychan");
        }
        faceReactionCoroutine = null;
    }

    private IEnumerator ResetLookAtRoutine(float holdDuration)
    {
        yield return new WaitForSeconds(Mathf.Max(0f, holdDuration));
        SetGazeTargetWeights(0f, 0f, 0f);
        lookAtCoroutine = null;
    }

    private void SetGazeTargetWeights(
        float rigWeight,
        float eyeWeight,
        float upperChestWeight)
    {
        targetRigWeight = Mathf.Clamp01(rigWeight);

        if (stabilizedUpperChestConstraint != null)
        {
            if (upperChestBlendCoroutine != null)
            {
                StopCoroutine(upperChestBlendCoroutine);
            }

            float destinationUpperChestWeight = Mathf.Clamp01(upperChestWeight);
            float upperChestBlendDuration =
                destinationUpperChestWeight > stabilizedUpperChestConstraint.weight
                    ? stabilizedRigBlendInDuration
                    : stabilizedRigBlendOutDuration;
            upperChestBlendCoroutine = StartCoroutine(
                BlendUpperChestWeightRoutine(
                    destinationUpperChestWeight,
                    upperChestBlendDuration));
        }

        if (eyeContactController != null)
        {
            float destinationEyeWeight = Mathf.Clamp01(eyeWeight);
            float eyeBlendDuration =
                destinationEyeWeight > eyeContactController.CurrentWeight
                    ? eyeContactBlendInDuration
                    : eyeContactBlendOutDuration;
            eyeContactController.BlendTo(
                destinationEyeWeight,
                eyeBlendDuration);
        }

        if (targetRig == null) return;

        if (rigBlendCoroutine != null)
        {
            StopCoroutine(rigBlendCoroutine);
        }

        rigBlendCoroutine = StartCoroutine(BlendRigWeightRoutine(targetRigWeight));
    }

    private IEnumerator BlendUpperChestWeightRoutine(
        float destination,
        float duration)
    {
        if (stabilizedUpperChestConstraint == null)
        {
            upperChestBlendCoroutine = null;
            yield break;
        }

        float startWeight = stabilizedUpperChestConstraint.weight;
        float elapsed = 0f;
        duration = Mathf.Max(0.01f, duration);
        while (stabilizedUpperChestConstraint != null && elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            stabilizedUpperChestConstraint.weight = Mathf.Lerp(
                startWeight,
                destination,
                SmootherStep(progress));
            yield return null;
        }

        if (stabilizedUpperChestConstraint != null)
        {
            stabilizedUpperChestConstraint.weight = destination;
        }
        upperChestBlendCoroutine = null;
    }

    private IEnumerator BlendRigWeightRoutine(float destination)
    {
        if (targetRig == null)
        {
            rigBlendCoroutine = null;
            yield break;
        }

        float startWeight = targetRig.weight;
        float duration;
        if (lookAtMode == VoiceLookAtMode.Stabilized)
        {
            duration = destination > startWeight
                ? stabilizedRigBlendInDuration
                : stabilizedRigBlendOutDuration;
        }
        else
        {
            float activeBlendSpeed = Mathf.Max(0.01f, rigBlendSpeed);
            duration = Mathf.Abs(destination - startWeight) / activeBlendSpeed;
        }

        float elapsed = 0f;
        duration = Mathf.Max(0.01f, duration);
        while (targetRig != null && elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            targetRig.weight = Mathf.Lerp(
                startWeight,
                destination,
                SmootherStep(progress));
            yield return null;
        }

        if (targetRig != null)
        {
            targetRig.weight = destination;
        }
        rigBlendCoroutine = null;
    }

    private static float ResolveLookAtStrength(KeywordReaction reaction)
    {
        if (reaction == null) return 0f;
        if (reaction.overrideLookAtStrength)
        {
            return Mathf.Clamp01(reaction.lookAtStrength);
        }

        switch (reaction.commandId)
        {
            case "LookAt":
                return 0.72f;
            case "Wave":
            case "UnityChanCall":
                return 0.48f;
            case "Cute":
                return 0.65f;
            case "Default":
                return 0f;
            default:
                return 0.4f;
        }
    }

    private static float ResolveUpperBodyFacingStrength(KeywordReaction reaction)
    {
        if (reaction == null) return 0f;
        if (reaction.overrideUpperBodyFacingStrength)
        {
            return Mathf.Clamp01(reaction.upperBodyFacingStrength);
        }

        switch (reaction.commandId)
        {
            case "LookAt":
                return 0.22f;
            case "Wave":
            case "UnityChanCall":
                return 0.18f;
            case "Cute":
                return 0.85f;
            default:
                return 0f;
        }
    }

    private float ResolveLookAtHoldDuration(KeywordReaction reaction)
    {
        if (reaction == null) return lookAtDuration;
        if (reaction.overrideLookAtHoldDuration)
        {
            return Mathf.Max(0f, reaction.lookAtHoldDuration);
        }

        return reaction.commandId == "Cute"
            ? 3.6f
            : lookAtDuration;
    }

    private static float ResolveEyeContactStrength(KeywordReaction reaction)
    {
        if (reaction == null) return 0f;
        if (reaction.overrideEyeContactStrength)
        {
            return Mathf.Clamp01(reaction.eyeContactStrength);
        }

        switch (reaction.commandId)
        {
            case "LookAt":
                return 0.95f;
            case "Wave":
            case "UnityChanCall":
                return 0.85f;
            case "Cute":
                return 0.7f;
            case "Default":
                return 0f;
            default:
                return 0.65f;
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
            workerThread.Join(500);
        }

        if (isListening) Microphone.End(microphoneDevice);
        if (recognizer != null) recognizer.Dispose();
        // Model は VoskModelCache がシーン間で共有し、アプリ終了時に破棄する。

        while (commandQueue.TryDequeue(out VoskCommand pendingCommand))
        {
            VoskPcmUtility.Return(pendingCommand.audioData);
        }
    }
}

/// <summary>
/// Humanoidの両目だけを安定化済みAimTargetへ向ける。
/// 胸と頭はAnimation Rigging側で制御し、ここでは変更しない。
/// </summary>
[RequireComponent(typeof(Animator))]
public class VoiceEyeContactIK : MonoBehaviour
{
    private Animator targetAnimator;
    private Transform aimTarget;
    private float clampWeight = 0.65f;
    private float startWeight;
    private float destinationWeight;
    private float blendDuration = 0.01f;
    private float blendElapsed;

    public float CurrentWeight { get; private set; }

    public void Configure(
        Animator animator,
        Transform target,
        float requestedClampWeight)
    {
        targetAnimator = animator != null ? animator : GetComponent<Animator>();
        aimTarget = target;
        clampWeight = Mathf.Clamp01(requestedClampWeight);
        CurrentWeight = 0f;
        startWeight = 0f;
        destinationWeight = 0f;
        blendElapsed = blendDuration;
    }

    public void BlendTo(float weight, float duration)
    {
        startWeight = CurrentWeight;
        destinationWeight = Mathf.Clamp01(weight);
        blendDuration = Mathf.Max(0.01f, duration);
        blendElapsed = 0f;
    }

    private void Update()
    {
        if (Mathf.Approximately(CurrentWeight, destinationWeight)) return;

        blendElapsed += Time.deltaTime;
        float progress = Mathf.Clamp01(blendElapsed / blendDuration);
        CurrentWeight = Mathf.Lerp(
            startWeight,
            destinationWeight,
            SmootherStep(progress));
        if (progress >= 1f)
        {
            CurrentWeight = destinationWeight;
        }
    }

    private void OnAnimatorIK(int layerIndex)
    {
        if (layerIndex != 0 || targetAnimator == null) return;

        if (aimTarget == null || CurrentWeight <= 0.0001f)
        {
            targetAnimator.SetLookAtWeight(0f);
            return;
        }

        // bodyWeight/headWeightは0にし、両目のみでアイコンタクトを作る。
        targetAnimator.SetLookAtWeight(
            CurrentWeight,
            0f,
            0f,
            1f,
            clampWeight);
        targetAnimator.SetLookAtPosition(aimTarget.position);
    }

    private static float SmootherStep(float value)
    {
        float t = Mathf.Clamp01(value);
        return t * t * t * (t * (t * 6f - 15f) + 10f);
    }
}

public class AimTargetFollower : MonoBehaviour
{
    public Camera targetCamera;
    [Min(0f)] public float smoothTime = 0.08f;

    private Vector3 velocity;
    private bool initialized;

    void Update()
    {
        if (targetCamera != null)
        {
            Vector3 targetPosition = targetCamera.transform.position;
            if (!initialized || smoothTime <= 0f)
            {
                transform.position = targetPosition;
                initialized = true;
                return;
            }

            transform.position = Vector3.SmoothDamp(
                transform.position,
                targetPosition,
                ref velocity,
                smoothTime,
                Mathf.Infinity,
                Time.deltaTime);
        }
    }
}

/// <summary>
/// 拘束対象ボーン自身ではなくキャラクタールート上の固定基準点からHMD方向を計算し、
/// 距離変化を捨てた方向へデッドゾーンと角度平滑化を適用します。
/// </summary>
public class StabilizedAimTargetFollower : MonoBehaviour
{
    public Camera targetCamera;
    public Transform originTransform;
    public Vector3 originLocalOffset = new Vector3(0f, 1.4f, 0f);
    [Min(0f)] public float directionSmoothTime = 0.18f;
    [Range(0f, 10f)] public float directionDeadZoneDegrees = 1.5f;
    [Min(0.1f)] public float targetDistance = 2f;
    [Range(0f, 89f)] public float maxYawDegrees = 60f;
    [Range(0f, 60f)] public float maxPitchDegrees = 25f;

    private Vector3 stableDirection;
    private bool initialized;

    void Update()
    {
        if (targetCamera == null || originTransform == null) return;

        Vector3 origin = originTransform.TransformPoint(originLocalOffset);
        Vector3 desiredDirection = targetCamera.transform.position - origin;
        if (desiredDirection.sqrMagnitude < 0.000001f) return;
        desiredDirection = ClampDirectionToForwardCone(desiredDirection.normalized);

        if (!initialized)
        {
            stableDirection = desiredDirection;
            initialized = true;
        }
        else
        {
            float angle = Vector3.Angle(stableDirection, desiredDirection);
            float deadZone = Mathf.Max(0f, directionDeadZoneDegrees);
            if (angle > deadZone)
            {
                float followFraction = (angle - deadZone) / Mathf.Max(angle, 0.0001f);
                Vector3 directionOutsideDeadZone = Vector3.Slerp(
                    stableDirection,
                    desiredDirection,
                    followFraction);
                if (directionSmoothTime <= 0f)
                {
                    stableDirection = directionOutsideDeadZone;
                }
                else
                {
                    float blend = 1f - Mathf.Exp(-Time.deltaTime / directionSmoothTime);
                    stableDirection = Vector3.Slerp(
                        stableDirection,
                        directionOutsideDeadZone,
                        blend).normalized;
                }
            }
        }

        transform.position = origin + stableDirection * Mathf.Max(0.1f, targetDistance);
    }

    private Vector3 ClampDirectionToForwardCone(Vector3 worldDirection)
    {
        Vector3 localDirection =
            originTransform.InverseTransformDirection(worldDirection).normalized;
        float horizontalLength = Mathf.Sqrt(
            localDirection.x * localDirection.x +
            localDirection.z * localDirection.z);
        float yaw = Mathf.Atan2(localDirection.x, localDirection.z) * Mathf.Rad2Deg;
        float pitch = Mathf.Atan2(localDirection.y, horizontalLength) * Mathf.Rad2Deg;

        yaw = Mathf.Clamp(yaw, -Mathf.Max(0f, maxYawDegrees), Mathf.Max(0f, maxYawDegrees));
        pitch = Mathf.Clamp(
            pitch,
            -Mathf.Max(0f, maxPitchDegrees),
            Mathf.Max(0f, maxPitchDegrees));

        float yawRadians = yaw * Mathf.Deg2Rad;
        float pitchRadians = pitch * Mathf.Deg2Rad;
        float pitchCosine = Mathf.Cos(pitchRadians);
        Vector3 clampedLocalDirection = new Vector3(
            Mathf.Sin(yawRadians) * pitchCosine,
            Mathf.Sin(pitchRadians),
            Mathf.Cos(yawRadians) * pitchCosine);
        return originTransform.TransformDirection(clampedLocalDirection).normalized;
    }
}
