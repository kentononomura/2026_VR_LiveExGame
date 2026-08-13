using UnityEngine;

/// <summary>
/// XR Originの位置と向きをシーンビュー上で分かりやすく可視化するスクリプト。
/// ゲームプレイ中には影響を与えません。
/// </summary>
public class VRStartVisualizer : MonoBehaviour
{
    [Header("表示設定")]
    [Tooltip("可視化する際の色")]
    public Color gizmoColor = new Color(0f, 1f, 0.5f, 0.5f);
    
    [Tooltip("想定されるプレイヤーの身長（メートル）")]
    public float estimatedHeight = 1.6f;
    
    [Tooltip("プレイヤーの体の半径（メートル）")]
    public float radius = 0.3f;

    private void OnDrawGizmos()
    {
        // ギズモの色を設定
        Gizmos.color = gizmoColor;

        Vector3 originPos = transform.position;
        Vector3 forward = transform.forward;
        Vector3 right = transform.right;

        // 1. プレイヤーの立ち位置（足元）に円を描画
        DrawCircle(originPos, radius);

        // 2. プレイヤーの向いている方向（正面）を示す矢印を描画
        Vector3 arrowTip = originPos + forward * 0.8f;
        Gizmos.DrawLine(originPos, arrowTip);
        Gizmos.DrawLine(arrowTip, arrowTip - forward * 0.2f + right * 0.2f);
        Gizmos.DrawLine(arrowTip, arrowTip - forward * 0.2f - right * 0.2f);

        // 3. 頭の位置（想定）をワイヤースフィアで描画
        Vector3 headPos = originPos + Vector3.up * estimatedHeight;
        Gizmos.DrawWireSphere(headPos, 0.15f);

        // 4. 足元から頭までの中心線を描画
        Gizmos.DrawLine(originPos, headPos);
    }

    // 簡単な円（ワイヤーフレーム）を描画するヘルパー
    private void DrawCircle(Vector3 center, float r)
    {
        int segments = 24;
        float angle = 0f;
        float step = 360f / segments;

        Vector3 lastPoint = center + new Vector3(Mathf.Sin(angle * Mathf.Deg2Rad) * r, 0, Mathf.Cos(angle * Mathf.Deg2Rad) * r);
        
        for (int i = 1; i <= segments; i++)
        {
            angle += step;
            Vector3 nextPoint = center + new Vector3(Mathf.Sin(angle * Mathf.Deg2Rad) * r, 0, Mathf.Cos(angle * Mathf.Deg2Rad) * r);
            Gizmos.DrawLine(lastPoint, nextPoint);
            lastPoint = nextPoint;
        }
    }
}
