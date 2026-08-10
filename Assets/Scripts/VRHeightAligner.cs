using UnityEngine;

public class VRHeightAligner : MonoBehaviour
{
    [Header("Height Alignment")]
    [Tooltip("プレイヤーの目線（カメラ）からの高さのオフセット。マイナス値で目線より低く（胸元など）、プラス値で高くします。")]
    public float heightOffsetFromCamera = -0.3f;

    private void Start()
    {
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            // 親オブジェクトが存在する場合を考慮し、ローカル座標で高さを合わせる
            Vector3 camWorldPos = mainCam.transform.position;
            
            if (transform.parent != null)
            {
                // カメラのワールド座標を、親オブジェクトから見たローカル座標に変換
                Vector3 localCamPos = transform.parent.InverseTransformPoint(camWorldPos);
                transform.localPosition = new Vector3(transform.localPosition.x, localCamPos.y + heightOffsetFromCamera, transform.localPosition.z);
            }
            else
            {
                // 親がない場合はワールド座標で直接高さを合わせる
                transform.position = new Vector3(transform.position.x, camWorldPos.y + heightOffsetFromCamera, transform.position.z);
            }

            Debug.Log($"[VRHeightAligner] {gameObject.name} の高さをプレイヤーのカメラ（Y: {camWorldPos.y:F2}）に合わせて自動調整しました。");
        }
    }
}
