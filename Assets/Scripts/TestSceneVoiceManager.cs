using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Vosk;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using UnityEngine.Animations.Rigging;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// We use the KeywordReaction class defined in VoiceReactionManager.cs

public class TestSceneVoiceManager : MonoBehaviour
{
    [Header("Vosk Settings")]
    public string modelFolderName = "vosk-model-small-ja-0.22";
    public string customMicrophoneName = "";
    public bool showRecognitionLog = true;

    [Header("Input Settings")]
#if ENABLE_INPUT_SYSTEM
    [Tooltip("左手トリガーで音声認識を開始します")]
    public InputAction pushToTalkAction = new InputAction("PushToTalk", InputActionType.Button, "<XRController>{LeftHand}/triggerPressed");
#endif

    [Header("Keywords & Reactions")]
    public List<KeywordReaction> keywordReactions = new List<KeywordReaction>
    {
        new KeywordReaction { keyword = "こっちむいて", reactionName = "smile1@unitychan", bodyReactionName = "" },
        new KeywordReaction { keyword = "手振って", reactionName = "smile2@unitychan", bodyReactionName = "Wave" },
        new KeywordReaction { keyword = "かわいい", reactionName = "smile3@unitychan", bodyReactionName = "Kiss" },
        new KeywordReaction { keyword = "デフォルト", reactionName = "default@unitychan", bodyReactionName = "" }
    };

    [Header("Reaction Settings")]
    public float lookAtDuration = 3f;
    public float rigBlendSpeed = 5f;

    private Model model;
    private VoskRecognizer recognizer;
    private string microphoneDevice;
    private AudioClip audioClip;
    private int lastSamplePosition = 0;
    private bool isListening = false;
    private bool isModelLoaded = false;
    private bool isShuttingDown = false;
    private const int SampleRate = 16000;

    private bool isLeftTriggerDown = false;

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

    // Unity-chan references
    private GameObject unityChanObj;
    private UnityChan.FaceUpdate faceUpdate;
    private Rig targetRig;
    private float targetRigWeight = 0f;

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


        string modelPath = Path.Combine(Application.streamingAssetsPath, modelFolderName);
        if (Directory.Exists(modelPath))
        {
            Debug.Log($"[Vosk] TestScene 用のモデルロード非同期タスクを起動します: {modelPath}");
            Task.Run(() =>
            {
                try
                {
                    model = new Model(modelPath);
                    recognizer = new VoskRecognizer(model, SampleRate);
                    recognizer.SetMaxAlternatives(0);
                    recognizer.SetWords(true);
                    
                    workerThread = new Thread(VoskWorkerLoop);
                    workerThread.IsBackground = true;
                    workerThread.Start();
                    
                    isModelLoaded = true;
                    Debug.Log("[Vosk] TestScene 用のモデルロードおよび音声認識スレッドが正常に起動しました。");
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[Vosk] モデル初期化例外: {ex.Message}\n{ex.StackTrace}");
                }
            });
        }
        else
        {
            Debug.LogError($"[Vosk] StreamingAssets 内のモデルフォルダが見つかりません: {modelPath}");
        }
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
        if (Microphone.devices.Length == 0) return;

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
    }

    void Update()
    {
        TrySetupUnityChan();

        // Smoothly blend the rig weight
        if (targetRig != null)
        {
            targetRig.weight = Mathf.Lerp(targetRig.weight, targetRigWeight, Time.deltaTime * rigBlendSpeed);
        }

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
        var leftHandDevices = new List<UnityEngine.XR.InputDevice>();
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

        if (isTriggerPressed)
        {
            if (!isLeftTriggerDown)
            {
                isLeftTriggerDown = true;
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
                    commandQueue.Enqueue(new VoskCommand { type = VoskCommandType.FinalResult });
                    Debug.Log($"<color=#FF8800>[Vosk] 🛑 音声入力の受付を終了しました</color>");
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
        if (currentPosition < 0 || lastSamplePosition == currentPosition) return;

        int sampleCount = currentPosition - lastSamplePosition;
        if (sampleCount < 0) sampleCount += audioClip.samples;

        float[] samples = new float[sampleCount];
        audioClip.GetData(samples, lastSamplePosition);
        lastSamplePosition = currentPosition;

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
                Debug.LogWarning("[Vosk] 🎤 音声データが極端に小さいか無音です。マイクがミュートされているか、正しいマイクデバイスが選択されていない可能性があります。");
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

    private Animator targetAnimator;
    private Coroutine bodyReactionCoroutine;

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

        // 3. Find Head
        Transform headBone = character.transform.Find("Character1_Reference/Character1_Hips/Character1_Spine/Character1_Spine1/Character1_Spine2/Character1_Neck/Character1_Head");
        if (headBone == null)
        {
            Debug.LogWarning("TestSceneVoiceManager: Head bone not found on Unity-chan!");
            return;
        }

        // 4. Add MultiAimConstraint
        var aimObj = new GameObject("HeadAimConstraint");
        aimObj.transform.SetParent(rigObj.transform, false);
        var aimConstraint = aimObj.AddComponent<MultiAimConstraint>();

        // 5. Create Target (looking at Main Camera)
        var mainCam = Camera.main;
        if (mainCam == null)
        {
            var xrOrigin = GameObject.Find("XROriginVR");
            if (xrOrigin != null) mainCam = xrOrigin.GetComponentInChildren<Camera>();
        }
        
        // We will make the aim target constantly follow the camera
        var aimTarget = new GameObject("AimTarget");
        aimTarget.transform.SetParent(aimObj.transform, false);
        // Add a script to make the target follow the camera
        var follower = aimTarget.AddComponent<AimTargetFollower>();
        follower.targetCamera = mainCam;

        // 6. Configure constraint
        var data = aimConstraint.data;
        data.constrainedObject = headBone;
        var sourceObjects = data.sourceObjects;
        sourceObjects.Clear();
        sourceObjects.Add(new WeightedTransform(aimTarget.transform, 1f));
        data.sourceObjects = sourceObjects;
        data.aimAxis = MultiAimConstraintData.Axis.Z;
        data.upAxis = MultiAimConstraintData.Axis.Y;
        aimConstraint.data = data;

        rigBuilder.Build();
        Debug.Log("TestSceneVoiceManager: Animation Rigging dynamically setup on UnityChan.");
    }

    private void ProcessRecognitionResult(string jsonResult)
    {
        if (string.IsNullOrEmpty(jsonResult)) return;

        if (showRecognitionLog && jsonResult.Contains("\"text\""))
        {
            Debug.Log($"[Vosk TestScene 音声認識] {jsonResult}");
        }

        // Voskは単語（形態素）の間にスペースを入れる仕様があるため、
        // ユーザーが設定したキーワードと照合しやすくするためにスペースを除去した文字列を作成します。
        string textWithoutSpaces = jsonResult.Replace(" ", "").Replace("　", "");

        foreach (var kr in keywordReactions)
        {
            // ユーザーが設定したキーワードからも念のためスペースを除去
            string cleanKeyword = kr.keyword.Replace(" ", "").Replace("　", "");

            if (textWithoutSpaces.Contains(cleanKeyword))
            {
                Debug.Log($"[Vosk] キーワード検知: {kr.keyword} -> 表情: {kr.reactionName} / 体: {kr.bodyReactionName}");
                
                StopAllCoroutines();

                // 表情を変更
                if (faceUpdate != null && !string.IsNullOrEmpty(kr.reactionName))
                {
                    faceUpdate.OnCallChangeFace(kr.reactionName);
                }

                // 体のアニメーションを変更（上半身レイヤー）
                if (targetAnimator != null && !string.IsNullOrEmpty(kr.bodyReactionName))
                {
                    int layerIndex = targetAnimator.GetLayerIndex("ReactionLayer");
                    if (layerIndex != -1)
                    {
                        targetAnimator.SetLayerWeight(layerIndex, 1f);
                        targetAnimator.CrossFade(kr.bodyReactionName, 0.2f, layerIndex);
                        // アニメーションの長さ分待機して元の状態に戻す（仮で2.5秒）
                        StartCoroutine(ResetBodyReactionRoutine(2.5f, layerIndex));
                    }
                }

                // 目線を合わせる (Weightを1にする)
                if (targetRig != null)
                {
                    targetRigWeight = 1f;
                    StartCoroutine(ResetLookAtRoutine());
                }
                
                break;
            }
        }
    }

    private IEnumerator ResetBodyReactionRoutine(float delay, int layerIndex)
    {
        yield return new WaitForSeconds(delay);
        if (targetAnimator != null)
        {
            targetAnimator.CrossFade("Empty", 0.5f, layerIndex);
        }
    }

    private IEnumerator ResetLookAtRoutine()
    {
        yield return new WaitForSeconds(lookAtDuration);
        targetRigWeight = 0f;
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
        if (model != null) model.Dispose();
    }
}

public class AimTargetFollower : MonoBehaviour
{
    public Camera targetCamera;

    void Update()
    {
        if (targetCamera != null)
        {
            transform.position = targetCamera.transform.position;
        }
    }
}
