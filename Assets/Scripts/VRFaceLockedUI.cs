using UnityEngine;

public class VRFaceLockedUI : MonoBehaviour
{
    [Header("HUD Settings")]
    [Tooltip("カメラから前方に配置する距離（メートル）")]
    public float distanceFromCamera = 2.5f;
    
    [Tooltip("追従の滑らかさ。数値を大きくすると素早く追従し、小さくするとふわっと遅れて追従します（酔い防止）")]
    public float followSpeed = 5.0f;

    [Tooltip("プレイヤーの目線からの上下のズレ。マイナス値にすると少し視界の下に配置されます")]
    public float heightOffset = -0.2f;

    private void Update()
    {
        Camera mainCam = Camera.main;
        if (mainCam == null) return;

        // カメラの位置と正面の向きから、ターゲットの座標を計算
        Vector3 targetPos = mainCam.transform.position + (mainCam.transform.forward * distanceFromCamera);
        targetPos.y += heightOffset; // 上下の位置調整

        // カメラと同じ回転（向き）に合わせる
        Quaternion targetRot = mainCam.transform.rotation;

        // 位置と回転を滑らかに補間して追従させる（遅延追従がVRでのベストプラクティスです）
        transform.position = Vector3.Lerp(transform.position, targetPos, Time.deltaTime * followSpeed);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * followSpeed);
    }
}
