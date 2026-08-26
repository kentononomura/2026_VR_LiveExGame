using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public sealed class PerformanceSyncTunerWindow : EditorWindow
{
    private const double BasePerformancePreroll = 2.01666665;
    private const int WaveformResolution = 1000;

    private StageDirector stageDirector;
    private AudioClip musicClip;
    private AnimationClip danceClip;
    private AnimationClip lipSyncClip;
    private GameObject characterPrefab;
    private GameObject lipSyncPrefab;

    private GameObject previewCharacter;
    private GameObject previewLipSync;
    private LipSyncController previewLipSyncController;
    private SkinnedMeshRenderer previewMouth;

    private float workingLeadTime = 0.2f;
    private float previewTime;
    private bool isPlaying;
    private bool loopEnabled = true;
    private float loopStart;
    private float loopEnd = 8f;
    private double playbackStartedAt;
    private float playbackStartedFrom;
    private float[] waveformPeaks;
    private AudioClip waveformClip;
    private Vector2 scrollPosition;
    private bool waitingForAudioLoad;
    private bool restoreEditorAudioMute;
    private string audioStatus = "停止中";

    [MenuItem("Tools/Performance Sync Tuner")]
    private static void OpenWindow()
    {
        PerformanceSyncTunerWindow window = GetWindow<PerformanceSyncTunerWindow>();
        window.titleContent = new GUIContent("Performance Sync");
        window.minSize = new Vector2(620f, 520f);
        window.Show();
    }

    private void OnEnable()
    {
        EditorApplication.update += EditorUpdate;
        Undo.undoRedoPerformed += HandleUndoRedo;
        TryFindStageDirector();
    }

    private void OnDisable()
    {
        EditorApplication.update -= EditorUpdate;
        Undo.undoRedoPerformed -= HandleUndoRedo;
        StopPreview();
        DestroyPreviewObjects();
    }

    private void OnGUI()
    {
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        EditorGUILayout.HelpBox(
            "Play Modeに入らず、音楽を聴きながらダンスとLipSyncの時刻を確認できます。" +
            "プレビュー中の値は『StageDirectorへ適用』を押すまで保存されません。",
            MessageType.Info);

        DrawSourceSection();

        if (stageDirector == null || musicClip == null || danceClip == null)
        {
            EditorGUILayout.HelpBox(
                "TestSceneを開いてStageDirectorを指定してください。素材は自動検出できます。",
                MessageType.Warning);
            EditorGUILayout.EndScrollView();
            return;
        }

        DrawOffsetSection();
        DrawTransportSection();
        DrawWaveform();
        DrawLoopSection();
        DrawStatusSection();

        EditorGUILayout.EndScrollView();
    }

    private void DrawSourceSection()
    {
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Preview Sources", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        StageDirector selectedDirector = (StageDirector)EditorGUILayout.ObjectField(
            "Stage Director", stageDirector, typeof(StageDirector), true);
        if (EditorGUI.EndChangeCheck())
        {
            AssignStageDirector(selectedDirector);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("シーンから自動検出"))
            {
                TryFindStageDirector(true);
            }

            if (GUILayout.Button("素材を再検出"))
            {
                ResolveSourcesFromDirector();
            }
        }

        EditorGUI.BeginChangeCheck();
        AudioClip selectedMusic = (AudioClip)EditorGUILayout.ObjectField(
            "Music", musicClip, typeof(AudioClip), false);
        AnimationClip selectedDance = (AnimationClip)EditorGUILayout.ObjectField(
            "Dance", danceClip, typeof(AnimationClip), false);
        AnimationClip selectedLipSync = (AnimationClip)EditorGUILayout.ObjectField(
            "LipSync", lipSyncClip, typeof(AnimationClip), false);
        if (EditorGUI.EndChangeCheck())
        {
            StopPreview();
            musicClip = selectedMusic;
            danceClip = selectedDance;
            lipSyncClip = selectedLipSync;
            InvalidateWaveform();
            ClampTimelineValues();
            SamplePreview();
        }
    }

    private void DrawOffsetSection()
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Synchronization Offset", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "値を大きくするとダンスとLipSyncが音楽より早く進みます。",
            MessageType.None);

        EditorGUI.BeginChangeCheck();
        workingLeadTime = EditorGUILayout.Slider(
            new GUIContent("Performance Lead Time", "音楽より先行させる秒数"),
            workingLeadTime,
            0f,
            1f);
        if (EditorGUI.EndChangeCheck())
        {
            SamplePreview();
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            DrawNudgeButton("-0.05", -0.05f);
            DrawNudgeButton("-0.01", -0.01f);
            GUILayout.FlexibleSpace();
            GUILayout.Label($"{workingLeadTime:F3} 秒", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            DrawNudgeButton("+0.01", 0.01f);
            DrawNudgeButton("+0.05", 0.05f);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            GUI.enabled = stageDirector != null;
            if (GUILayout.Button("StageDirectorへ適用", GUILayout.Height(28f)))
            {
                ApplyLeadTimeToDirector();
            }

            if (GUILayout.Button("保存値へ戻す", GUILayout.Height(28f)))
            {
                LoadLeadTimeFromDirector();
                SamplePreview();
            }
            GUI.enabled = true;
        }
    }

    private void DrawTransportSection()
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(isPlaying ? "一時停止" : "再生", GUILayout.Height(30f)))
            {
                if (isPlaying) PausePreview();
                else PlayPreview();
            }

            if (GUILayout.Button("停止", GUILayout.Height(30f)))
            {
                StopPreview();
                SetPreviewTime(0f, false);
            }

            if (GUILayout.Button("プレビューを再生成", GUILayout.Height(30f)))
            {
                RecreatePreviewObjects();
                SamplePreview();
            }

            if (GUILayout.Button("Sceneビューで表示", GUILayout.Height(30f)))
            {
                FocusPreviewCharacter();
            }
        }

        float duration = GetPreviewDuration();
        EditorGUI.BeginChangeCheck();
        float selectedTime = EditorGUILayout.Slider("Music Time", previewTime, 0f, duration);
        if (EditorGUI.EndChangeCheck())
        {
            SetPreviewTime(selectedTime, isPlaying);
        }
    }

    private void DrawWaveform()
    {
        EnsureWaveform();

        Rect waveformRect = GUILayoutUtility.GetRect(10f, 110f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(waveformRect, new Color(0.08f, 0.09f, 0.11f, 1f));

        if (waveformPeaks != null && waveformPeaks.Length > 0)
        {
            float centerY = waveformRect.center.y;
            float halfHeight = waveformRect.height * 0.43f;
            Color waveformColor = new Color(0.2f, 0.75f, 1f, 1f);
            int columns = Mathf.Max(1, Mathf.RoundToInt(waveformRect.width));

            for (int x = 0; x < columns; x++)
            {
                int peakIndex = Mathf.Clamp(
                    Mathf.FloorToInt((float)x / columns * waveformPeaks.Length),
                    0,
                    waveformPeaks.Length - 1);
                float height = waveformPeaks[peakIndex] * halfHeight;
                EditorGUI.DrawRect(
                    new Rect(waveformRect.x + x, centerY - height, 1f, height * 2f + 1f),
                    waveformColor);
            }
        }
        else
        {
            GUI.Label(waveformRect, "波形を読み込めませんでした", CenteredMiniLabelStyle());
        }

        float duration = GetPreviewDuration();
        if (loopEnabled && duration > 0f)
        {
            float loopX = waveformRect.x + waveformRect.width * (loopStart / duration);
            float loopWidth = waveformRect.width * ((loopEnd - loopStart) / duration);
            EditorGUI.DrawRect(
                new Rect(loopX, waveformRect.y, Mathf.Max(1f, loopWidth), waveformRect.height),
                new Color(1f, 0.75f, 0.1f, 0.09f));
        }

        if (duration > 0f)
        {
            float playheadX = waveformRect.x + waveformRect.width * (previewTime / duration);
            EditorGUI.DrawRect(
                new Rect(playheadX - 1f, waveformRect.y, 2f, waveformRect.height),
                new Color(1f, 0.3f, 0.25f, 1f));
        }

        HandleWaveformInput(waveformRect, duration);
    }

    private void DrawLoopSection()
    {
        EditorGUILayout.Space(4f);
        loopEnabled = EditorGUILayout.Toggle("区間ループ", loopEnabled);

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUI.BeginChangeCheck();
            float newStart = EditorGUILayout.FloatField("開始", loopStart);
            float newEnd = EditorGUILayout.FloatField("終了", loopEnd);
            if (EditorGUI.EndChangeCheck())
            {
                float duration = GetPreviewDuration();
                loopStart = Mathf.Clamp(newStart, 0f, duration);
                loopEnd = Mathf.Clamp(newEnd, loopStart + 0.05f, duration);
            }

            if (GUILayout.Button("現在位置を開始", GUILayout.Width(110f)))
            {
                loopStart = Mathf.Min(previewTime, loopEnd - 0.05f);
            }

            if (GUILayout.Button("現在位置を終了", GUILayout.Width(110f)))
            {
                loopEnd = Mathf.Max(previewTime, loopStart + 0.05f);
            }
        }
    }

    private void DrawStatusSection()
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Current Sample", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("音声プレビュー", audioStatus);
        EditorGUILayout.LabelField("音楽", $"{previewTime:F3} 秒");
        EditorGUILayout.LabelField(
            "ダンス / LipSync",
            $"{BasePerformancePreroll + workingLeadTime + previewTime:F3} 秒 " +
            $"(基本プリロール {BasePerformancePreroll:F3} + 調整 {workingLeadTime:F3})");

        if (previewCharacter == null)
        {
            EditorGUILayout.HelpBox(
                "プレビューキャラクターがありません。『プレビューを再生成』を押してください。",
                MessageType.Warning);
        }
    }

    private void DrawNudgeButton(string label, float amount)
    {
        if (!GUILayout.Button(label, GUILayout.Width(64f))) return;
        workingLeadTime = Mathf.Clamp(workingLeadTime + amount, 0f, 1f);
        SamplePreview();
    }

    private void TryFindStageDirector(bool showDialogOnFailure = false)
    {
        StageDirector found = UnityEngine.Object.FindAnyObjectByType<StageDirector>(FindObjectsInactive.Include);
        AssignStageDirector(found);

        if (found == null && showDialogOnFailure)
        {
            EditorUtility.DisplayDialog(
                "StageDirectorが見つかりません",
                "TestSceneを開いてから再度実行してください。",
                "OK");
        }
    }

    private void AssignStageDirector(StageDirector value)
    {
        if (stageDirector == value) return;

        StopPreview();
        DestroyPreviewObjects();
        stageDirector = value;
        ResolveSourcesFromDirector();
        LoadLeadTimeFromDirector();
    }

    private void ResolveSourcesFromDirector()
    {
        StopPreview();
        DestroyPreviewObjects();

        musicClip = null;
        danceClip = null;
        lipSyncClip = null;
        characterPrefab = null;
        lipSyncPrefab = null;

        if (stageDirector == null)
        {
            InvalidateWaveform();
            return;
        }

        ResolveMusicSource();
        ResolvePerformanceSources();
        InvalidateWaveform();
        ClampTimelineValues();
        RecreatePreviewObjects();
        SamplePreview();
        Repaint();
    }

    private void ResolveMusicSource()
    {
        if (stageDirector.musicPlayerPrefab == null) return;

        AudioSource fallback = null;
        AudioSource[] sources = stageDirector.musicPlayerPrefab.GetComponentsInChildren<AudioSource>(true);
        foreach (AudioSource source in sources)
        {
            if (source == null || source.clip == null) continue;
            if (fallback == null) fallback = source;

            if (source.gameObject.name == "Main" || source.volume > 0f)
            {
                musicClip = source.clip;
                return;
            }
        }

        if (fallback != null) musicClip = fallback.clip;
    }

    private void ResolvePerformanceSources()
    {
        if (stageDirector.prefabsOnTimeline == null) return;

        foreach (GameObject prefab in stageDirector.prefabsOnTimeline)
        {
            if (prefab == null) continue;

            LipSyncController lipController = prefab.GetComponentInChildren<LipSyncController>(true);
            Animator animator = prefab.GetComponentInChildren<Animator>(true);

            if (lipController != null)
            {
                lipSyncPrefab = prefab;
                lipSyncClip = FindPreferredClip(animator, "LipSync");
                continue;
            }

            if (animator != null && characterPrefab == null)
            {
                characterPrefab = prefab;
                danceClip = FindPreferredClip(animator, "003_NOT01_Final");
            }
        }
    }

    private static AnimationClip FindPreferredClip(Animator animator, string preferredName)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return null;

        AnimationClip longest = null;
        foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips)
        {
            if (clip == null) continue;
            if (clip.name.IndexOf(preferredName, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return clip;
            }

            if (longest == null || clip.length > longest.length)
            {
                longest = clip;
            }
        }

        return longest;
    }

    private void RecreatePreviewObjects()
    {
        DestroyPreviewObjects();

        if (characterPrefab != null)
        {
            previewCharacter = Instantiate(characterPrefab);
            previewCharacter.name = "PerformanceSyncPreview_Character";
            SetPreviewHideFlags(previewCharacter);

            Animator animator = previewCharacter.GetComponentInChildren<Animator>(true);
            if (animator != null) animator.enabled = false;
        }

        if (lipSyncPrefab != null)
        {
            previewLipSync = Instantiate(lipSyncPrefab);
            previewLipSync.name = "PerformanceSyncPreview_LipSync";
            SetPreviewHideFlags(previewLipSync);

            Animator animator = previewLipSync.GetComponentInChildren<Animator>(true);
            if (animator != null) animator.enabled = false;

            previewLipSyncController = previewLipSync.GetComponentInChildren<LipSyncController>(true);
        }

        ResolvePreviewMouth();
    }

    private static void SetPreviewHideFlags(GameObject root)
    {
        if (root == null) return;
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            child.gameObject.hideFlags = HideFlags.HideAndDontSave;
        }
    }

    private void DestroyPreviewObjects()
    {
        if (previewCharacter != null) DestroyImmediate(previewCharacter);
        if (previewLipSync != null) DestroyImmediate(previewLipSync);
        previewCharacter = null;
        previewLipSync = null;
        previewLipSyncController = null;
        previewMouth = null;
        SceneView.RepaintAll();
    }

    private void ResolvePreviewMouth()
    {
        previewMouth = null;
        if (previewCharacter == null || previewLipSyncController == null) return;

        foreach (SkinnedMeshRenderer renderer in previewCharacter.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (renderer.gameObject.name == previewLipSyncController.targetName)
            {
                previewMouth = renderer;
                break;
            }
        }
    }

    private void SamplePreview()
    {
        if (previewCharacter == null && characterPrefab != null)
        {
            RecreatePreviewObjects();
        }

        float performanceTime = (float)BasePerformancePreroll + workingLeadTime + previewTime;

        if (previewCharacter != null && danceClip != null)
        {
            danceClip.SampleAnimation(previewCharacter, Mathf.Clamp(performanceTime, 0f, danceClip.length));
        }

        if (previewLipSync != null && lipSyncClip != null)
        {
            lipSyncClip.SampleAnimation(previewLipSync, Mathf.Clamp(performanceTime, 0f, lipSyncClip.length));
            ApplyLipSyncPreview();
        }

        SceneView.RepaintAll();
    }

    private void ApplyLipSyncPreview()
    {
        if (previewLipSyncController == null || previewMouth == null ||
            previewMouth.sharedMesh == null || previewLipSyncController.weightCurve == null)
        {
            return;
        }

        float total = 100f;
        total = SetMouthWeight(6, previewLipSyncController.nodeA, total);
        total = SetMouthWeight(7, previewLipSyncController.nodeI, total);
        total = SetMouthWeight(8, previewLipSyncController.nodeU, total);
        total = SetMouthWeight(9, previewLipSyncController.nodeE, total);
        SetMouthWeight(10, previewLipSyncController.nodeO, total);
    }

    private float SetMouthWeight(int blendShapeIndex, Transform node, float availableWeight)
    {
        if (node == null || blendShapeIndex < 0 ||
            blendShapeIndex >= previewMouth.sharedMesh.blendShapeCount)
        {
            return availableWeight;
        }

        float normalizedWeight = previewLipSyncController.weightCurve.Evaluate(node.localPosition.z);
        float weight = availableWeight * normalizedWeight;
        previewMouth.SetBlendShapeWeight(blendShapeIndex, weight);
        return availableWeight - weight;
    }

    private void PlayPreview()
    {
        if (musicClip == null) return;
        if (loopEnabled && previewTime >= loopEnd) previewTime = loopStart;

        if (musicClip.loadState == AudioDataLoadState.Unloaded)
        {
            musicClip.LoadAudioData();
        }

        if (musicClip.loadState == AudioDataLoadState.Loading)
        {
            waitingForAudioLoad = true;
            audioStatus = "音源を読み込み中…";
            Repaint();
            return;
        }

        if (musicClip.loadState == AudioDataLoadState.Failed)
        {
            audioStatus = "音源の読み込みに失敗しました";
            Repaint();
            return;
        }

        StartLoadedAudioPreview();
    }

    private void StartLoadedAudioPreview()
    {
        EnsureEditorAudioIsAudible();

        if (!AudioPreviewUtility.Play(musicClip, TimeToSample(previewTime)))
        {
            audioStatus = "Unity Editorの音声プレビューを開始できませんでした";
            RestoreEditorAudioMute();
            isPlaying = false;
            Repaint();
            return;
        }

        playbackStartedAt = EditorApplication.timeSinceStartup;
        playbackStartedFrom = previewTime;
        isPlaying = true;
        waitingForAudioLoad = false;
        audioStatus = "再生中";
    }

    private void PausePreview()
    {
        UpdatePreviewTimeFromClock();
        AudioPreviewUtility.Stop();
        isPlaying = false;
        waitingForAudioLoad = false;
        audioStatus = "一時停止";
        RestoreEditorAudioMute();
        SamplePreview();
        Repaint();
    }

    private void StopPreview()
    {
        AudioPreviewUtility.Stop();
        isPlaying = false;
        waitingForAudioLoad = false;
        audioStatus = "停止中";
        RestoreEditorAudioMute();
    }

    private void SetPreviewTime(float time, bool continuePlaying)
    {
        previewTime = Mathf.Clamp(time, 0f, GetPreviewDuration());
        SamplePreview();

        if (continuePlaying)
        {
            EnsureEditorAudioIsAudible();
            if (AudioPreviewUtility.Play(musicClip, TimeToSample(previewTime)))
            {
                playbackStartedAt = EditorApplication.timeSinceStartup;
                playbackStartedFrom = previewTime;
                audioStatus = "再生中";
            }
            else
            {
                isPlaying = false;
                audioStatus = "Unity Editorの音声プレビューを開始できませんでした";
                RestoreEditorAudioMute();
            }
        }

        Repaint();
    }

    private void EditorUpdate()
    {
        if (waitingForAudioLoad)
        {
            if (musicClip == null || musicClip.loadState == AudioDataLoadState.Failed)
            {
                waitingForAudioLoad = false;
                audioStatus = "音源の読み込みに失敗しました";
                Repaint();
            }
            else if (musicClip.loadState == AudioDataLoadState.Loaded)
            {
                StartLoadedAudioPreview();
                Repaint();
            }
            return;
        }

        if (!isPlaying) return;

        UpdatePreviewTimeFromClock();
        float duration = GetPreviewDuration();

        if (loopEnabled && previewTime >= loopEnd)
        {
            SetPreviewTime(loopStart, true);
            return;
        }

        if (previewTime >= duration)
        {
            previewTime = duration;
            StopPreview();
        }

        SamplePreview();
        Repaint();
    }

    private void EnsureEditorAudioIsAudible()
    {
        if (!EditorUtility.audioMasterMute) return;
        restoreEditorAudioMute = true;
        EditorUtility.audioMasterMute = false;
    }

    private void RestoreEditorAudioMute()
    {
        if (!restoreEditorAudioMute) return;
        EditorUtility.audioMasterMute = true;
        restoreEditorAudioMute = false;
    }

    private void UpdatePreviewTimeFromClock()
    {
        if (!isPlaying) return;
        previewTime = playbackStartedFrom +
                      (float)(EditorApplication.timeSinceStartup - playbackStartedAt);
    }

    private int TimeToSample(float time)
    {
        if (musicClip == null) return 0;
        return Mathf.Clamp(Mathf.RoundToInt(time * musicClip.frequency), 0, musicClip.samples - 1);
    }

    private void HandleWaveformInput(Rect rect, float duration)
    {
        Event current = Event.current;
        if ((current.type != EventType.MouseDown && current.type != EventType.MouseDrag) ||
            current.button != 0 || !rect.Contains(current.mousePosition) || duration <= 0f)
        {
            return;
        }

        float normalized = Mathf.InverseLerp(rect.x, rect.xMax, current.mousePosition.x);
        SetPreviewTime(normalized * duration, isPlaying);
        current.Use();
    }

    private void EnsureWaveform()
    {
        if (waveformClip == musicClip && waveformPeaks != null) return;

        waveformClip = musicClip;
        waveformPeaks = null;
        if (musicClip == null || musicClip.samples <= 0 || musicClip.channels <= 0) return;

        musicClip.LoadAudioData();
        float[] peaks = new float[WaveformResolution];
        int framesPerProbe = 128;
        float[] samples = new float[framesPerProbe * musicClip.channels];

        for (int i = 0; i < peaks.Length; i++)
        {
            int offset = Mathf.Clamp(
                Mathf.FloorToInt((float)i / peaks.Length * musicClip.samples),
                0,
                Mathf.Max(0, musicClip.samples - framesPerProbe));

            Array.Clear(samples, 0, samples.Length);
            if (!musicClip.GetData(samples, offset))
            {
                waveformPeaks = null;
                return;
            }

            float peak = 0f;
            foreach (float sample in samples)
            {
                peak = Mathf.Max(peak, Mathf.Abs(sample));
            }
            peaks[i] = peak;
        }

        waveformPeaks = peaks;
    }

    private void InvalidateWaveform()
    {
        waveformClip = null;
        waveformPeaks = null;
    }

    private void ApplyLeadTimeToDirector()
    {
        if (stageDirector == null) return;

        SerializedObject serializedDirector = new SerializedObject(stageDirector);
        SerializedProperty leadProperty = serializedDirector.FindProperty("performanceLeadTime");
        if (leadProperty == null)
        {
            EditorUtility.DisplayDialog(
                "適用できません",
                "StageDirectorにperformanceLeadTimeが見つかりません。スクリプトのコンパイル完了を確認してください。",
                "OK");
            return;
        }

        Undo.RecordObject(stageDirector, "Adjust Performance Lead Time");
        leadProperty.floatValue = workingLeadTime;
        serializedDirector.ApplyModifiedProperties();
        EditorUtility.SetDirty(stageDirector);
        Repaint();
    }

    private void LoadLeadTimeFromDirector()
    {
        if (stageDirector == null) return;
        SerializedObject serializedDirector = new SerializedObject(stageDirector);
        SerializedProperty leadProperty = serializedDirector.FindProperty("performanceLeadTime");
        if (leadProperty != null) workingLeadTime = leadProperty.floatValue;
    }

    private void HandleUndoRedo()
    {
        LoadLeadTimeFromDirector();
        SamplePreview();
        Repaint();
    }

    private void ClampTimelineValues()
    {
        float duration = GetPreviewDuration();
        previewTime = Mathf.Clamp(previewTime, 0f, duration);
        loopStart = Mathf.Clamp(loopStart, 0f, duration);
        loopEnd = Mathf.Clamp(loopEnd, Mathf.Min(duration, loopStart + 0.05f), duration);
        if (loopEnd <= loopStart) loopEnd = Mathf.Min(duration, loopStart + 8f);
    }

    private float GetPreviewDuration()
    {
        if (musicClip == null) return 0f;
        return Mathf.Max(0.01f, musicClip.length);
    }

    private void FocusPreviewCharacter()
    {
        if (previewCharacter == null) RecreatePreviewObjects();
        if (previewCharacter == null || SceneView.lastActiveSceneView == null) return;

        Renderer[] renderers = previewCharacter.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        SceneView.lastActiveSceneView.Frame(bounds, false);
        SceneView.lastActiveSceneView.Repaint();
    }

    private static GUIStyle CenteredMiniLabelStyle()
    {
        GUIStyle style = new GUIStyle(EditorStyles.miniLabel);
        style.alignment = TextAnchor.MiddleCenter;
        style.normal.textColor = Color.gray;
        return style;
    }

    private static class AudioPreviewUtility
    {
        private static readonly Type AudioUtilType = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");

        public static bool Play(AudioClip clip, int startSample)
        {
            if (clip == null || AudioUtilType == null) return false;
            Stop();

            MethodInfo method = FindMethod("PlayPreviewClip") ?? FindMethod("PlayClip");
            if (method == null) return false;

            try
            {
                method.Invoke(null, BuildArguments(method, clip, startSample));
                // Unity 6では再生状態が次のEditor更新までfalseのことがあるため、
                // 呼び出し成功を開始成功として扱う。
                return true;
            }
            catch (TargetInvocationException exception)
            {
                Debug.LogWarning($"[PerformanceSyncTuner] 音声プレビューを開始できませんでした: {exception.InnerException?.Message}");
                return false;
            }
        }

        public static void Stop()
        {
            if (AudioUtilType == null) return;
            MethodInfo method = FindMethod("StopAllPreviewClips") ?? FindMethod("StopAllClips");
            if (method == null) return;

            try
            {
                method.Invoke(null, null);
            }
            catch (TargetInvocationException)
            {
                // Unityの再コンパイル中はAudioUtilが一時的に利用できないことがある。
            }
        }

        private static MethodInfo FindMethod(string name)
        {
            MethodInfo[] methods = AudioUtilType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (MethodInfo method in methods)
            {
                if (method.Name == name) return method;
            }
            return null;
        }

        private static object[] BuildArguments(MethodInfo method, AudioClip clip, int startSample)
        {
            ParameterInfo[] parameters = method.GetParameters();
            object[] arguments = new object[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                Type type = parameters[i].ParameterType;
                if (type == typeof(AudioClip)) arguments[i] = clip;
                else if (type == typeof(int)) arguments[i] = startSample;
                else if (type == typeof(bool)) arguments[i] = false;
                else arguments[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : null;
            }

            return arguments;
        }
    }
}
