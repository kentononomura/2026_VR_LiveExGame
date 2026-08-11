using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Vosk;
using System.IO;
using System.Threading.Tasks;
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
    public InputAction pushToTalkAction = new InputAction("PushToTalk", InputActionType.Value, "<XRController>{LeftHand}/trigger");
#endif

    [Header("Keywords & Reactions")]
    public List<KeywordReaction> keywordReactions = new List<KeywordReaction>
    {
        new KeywordReaction { keyword = "こっちむいて", reactionName = "smile1@unitychan" },
        new KeywordReaction { keyword = "わらって", reactionName = "smile2@unitychan" },
        new KeywordReaction { keyword = "デフォルト", reactionName = "default@unitychan" }
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
    private const int SampleRate = 16000;

    private bool isLeftTriggerDown = false;

    // Unity-chan references
    private GameObject unityChanObj;
    private UnityChan.FaceUpdate faceUpdate;
    private Rig targetRig;
    private float targetRigWeight = 0f;

    void Start()
    {
#if ENABLE_INPUT_SYSTEM
        pushToTalkAction.expectedControlType = "Axis";
        pushToTalkAction.Enable();
#endif

        string modelPath = Path.Combine(Application.streamingAssetsPath, modelFolderName);
        if (Directory.Exists(modelPath))
        {
            Task.Run(() =>
            {
                model = new Model(modelPath);
                recognizer = new VoskRecognizer(model, SampleRate);
                recognizer.SetMaxAlternatives(0);
                recognizer.SetWords(true);
                isModelLoaded = true;
            });
        }
    }

    private void StartMicrophone()
    {
        if (Microphone.devices.Length == 0) return;

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
    }

    void Update()
    {
        TrySetupUnityChan();

        // Smoothly blend the rig weight
        if (targetRig != null)
        {
            targetRig.weight = Mathf.Lerp(targetRig.weight, targetRigWeight, Time.deltaTime * rigBlendSpeed);
        }

        if (isModelLoaded && !isListening)
        {
            StartMicrophone();
            return;
        }

        if (!isListening || recognizer == null || audioClip == null) return;

        bool isHolding = false;

#if ENABLE_INPUT_SYSTEM
        if (pushToTalkAction.enabled)
        {
            float triggerValue = pushToTalkAction.ReadValue<float>();
            if (triggerValue >= 0.8f)
            {
                if (!isLeftTriggerDown)
                {
                    isLeftTriggerDown = true;
                    recognizer.Reset();
                    Debug.Log($"<color=#00FF00>[Vosk] 🎤 左手トリガー検知：音声入力の受付を開始しました</color>");
                }
                isHolding = true;
            }
            else if (triggerValue < 0.2f)
            {
                if (isLeftTriggerDown)
                {
                    isLeftTriggerDown = false;
                    string finalResult = recognizer.FinalResult();
                    ProcessRecognitionResult(finalResult);
                    Debug.Log($"<color=#FF8800>[Vosk] 🛑 音声入力の受付を終了しました</color>");
                }
            }
        }
#endif

        int currentPosition = Microphone.GetPosition(microphoneDevice);
        if (currentPosition < 0 || lastSamplePosition == currentPosition) return;

        int sampleCount = currentPosition - lastSamplePosition;
        if (sampleCount < 0) sampleCount += audioClip.samples;

        float[] samples = new float[sampleCount];
        audioClip.GetData(samples, lastSamplePosition);
        lastSamplePosition = currentPosition;

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
                ProcessRecognitionResult(recognizer.Result());
            }
        }
    }

    private void TrySetupUnityChan()
    {
        if (unityChanObj != null) return;

        // StageDirector spawns UnityChan at runtime, so we wait until she appears
        var face = FindAnyObjectByType<UnityChan.FaceUpdate>();
        if (face != null)
        {
            faceUpdate = face;
            unityChanObj = face.gameObject;
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
                Debug.Log($"[Vosk] キーワード検知: {kr.keyword} -> 表情: {kr.reactionName}");
                
                // 表情を変更
                if (faceUpdate != null)
                {
                    faceUpdate.OnCallChangeFace(kr.reactionName);
                }

                // 目線を合わせる (Weightを1にする)
                if (targetRig != null)
                {
                    targetRigWeight = 1f;
                    StopAllCoroutines();
                    StartCoroutine(ResetLookAtRoutine());
                }
                
                break;
            }
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
