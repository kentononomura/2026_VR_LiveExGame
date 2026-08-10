using UnityEngine;

public class VRStageCalibrator : MonoBehaviour
{
    [Header("Calibration Settings")]
    [Tooltip("このステージオブジェクトを配置する床の高さ（通常は0でOK）")]
    public float floorHeightY = 0f;

    [Tooltip("プレイヤーの立ち位置からステージ全体を前方にどれだけオフセット（移動）させるか")]
    public float forwardOffset = 0f;

    private void Start()
    {
        // プレイヤーのメインカメラを取得
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            // カメラの位置（X, Z）と向き（Y軸回転：水平方向の角度）を取得
            Vector3 camPos = mainCam.transform.position;
            float yaw = mainCam.transform.eulerAngles.y;

            // 回転を計算
            Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);

            // カメラの真下の床の位置（YはfloorHeightY）を基準に、正面方向にforwardOffset分進めた位置をターゲットにする
            Vector3 targetPos = new Vector3(camPos.x, floorHeightY, camPos.z) + (rotation * Vector3.forward * forwardOffset);

            // このオブジェクトの位置と回転を同期
            transform.position = targetPos;
            transform.rotation = rotation;

            Debug.Log($"[VRStageCalibrator] {gameObject.name} をプレイヤーの正面位置にアラインしました。(Yaw: {yaw:F1}°, Position: {targetPos})");
        }
        else
        {
            Debug.LogWarning("[VRStageCalibrator] メインカメラが見つかりません。自動配置調整をスキップします。");
        }
    }
}
