using System;
using UnityEngine;

/// <summary>
/// 1回の音声認識に対するポイントを計算し、リアクションの成立可否を返します。
/// ポイントは呼び出しごとに完結し、蓄積しません。
/// </summary>
[Serializable]
public sealed class VoicePointEvaluator
{
    [Header("Voice Point Requirement")]
    [Tooltip("リアクション成立に必要な最終音声ポイントです。")]
    [Min(0f)]
    [SerializeField] private float reactionThreshold = 60f;

    [Header("Distance Point Curve")]
    [Tooltip("X軸がプレイヤーとUnityちゃんの距離(m)、Y軸が基礎音声ポイントです。")]
    [SerializeField] private AnimationCurve distancePointCurve = new AnimationCurve(
        new Keyframe(0f, 100f),
        new Keyframe(1f, 90f),
        new Keyframe(3f, 70f),
        new Keyframe(5f, 50f),
        new Keyframe(8f, 20f),
        new Keyframe(10f, 0f));

    [Header("Penlight Multipliers")]
    [Tooltip("通常色のペンライト倍率です。")]
    [Min(0f)]
    [SerializeField] private float normalMultiplier = 1f;

    [Tooltip("青色のペンライト倍率です。")]
    [Min(0f)]
    [SerializeField] private float blueMultiplier = 1.2f;

    [Tooltip("黄色のペンライト倍率です。")]
    [Min(0f)]
    [SerializeField] private float yellowMultiplier = 1.4f;

    [Tooltip("ピンク色のペンライト倍率です。")]
    [Min(0f)]
    [SerializeField] private float pinkMultiplier = 2f;

    [Header("Debug")]
    [Tooltip("音声認識ごとの距離、ポイント、色、倍率、判定結果をConsoleへ表示します。")]
    [SerializeField] private bool enableDebugLog = true;

    public bool Evaluate(
        Transform playerTransform,
        Transform unityChanTransform,
        PenlightGaugeController leftPenlight)
    {
        if (playerTransform == null || unityChanTransform == null)
        {
            if (enableDebugLog)
            {
                Debug.LogWarning(
                    "[VoicePoint] PlayerまたはUnityちゃんのTransformを取得できないため、リアクションを実行しません。");
            }
            return false;
        }

        float distance = Vector3.Distance(playerTransform.position, unityChanTransform.position);
        float basePoint = distancePointCurve != null
            ? Mathf.Max(0f, distancePointCurve.Evaluate(distance))
            : 0f;
        PenlightGaugeController.PenlightColorState colorState =
            leftPenlight != null
                ? leftPenlight.CurrentColorState
                : PenlightGaugeController.PenlightColorState.Normal;
        float multiplier = GetMultiplier(colorState);
        float finalPoint = basePoint * multiplier;
        bool succeeded = finalPoint >= reactionThreshold;

        if (enableDebugLog)
        {
            Debug.Log(
                $"[VoicePoint] Distance: {distance:F2}m / Base: {basePoint:F1} / " +
                $"Penlight: {colorState} / Multiplier: x{multiplier:F1} / " +
                $"Final: {finalPoint:F1} / Threshold: {reactionThreshold:F1} / " +
                $"Result: {(succeeded ? "SUCCESS" : "FAILED")}");
        }

        return succeeded;
    }

    private float GetMultiplier(PenlightGaugeController.PenlightColorState colorState)
    {
        switch (colorState)
        {
            case PenlightGaugeController.PenlightColorState.Blue:
                return blueMultiplier;
            case PenlightGaugeController.PenlightColorState.Yellow:
                return yellowMultiplier;
            case PenlightGaugeController.PenlightColorState.Pink:
                return pinkMultiplier;
            default:
                return normalMultiplier;
        }
    }
}
