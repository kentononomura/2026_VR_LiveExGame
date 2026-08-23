using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using Unity.XR.Oculus;
#endif

/// <summary>
/// Quest 実機でのみ、安全な初期パフォーマンス設定を適用します。
/// PC のEditor再生やStandaloneビルドには影響しません。
/// </summary>
public static class QuestPerformanceBootstrap
{
    private const int QuestTargetFrameRate = 72;
    private const int InitialFoveationLevel = 2;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void ApplyQuestSettings()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        Application.targetFrameRate = QuestTargetFrameRate;

        // 中程度を初期値にし、端末負荷に応じてOculus Runtimeへ動的調整させる。
        Utils.foveatedRenderingLevel = InitialFoveationLevel;
        Utils.useDynamicFoveatedRendering = true;
#endif
    }
}
