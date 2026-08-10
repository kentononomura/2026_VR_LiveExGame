using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
#endif

public class Saber : MonoBehaviour
{
    public enum HandType { Left, Right }

    [Header("Tracking Settings")]
    [Tooltip("このSaberを持たせる手（左手か右手か）")]
    public HandType handType = HandType.Right;

    [Header("Saber Visual Settings")]
    [Tooltip("コントローラーの先端からどのくらい長くするか")]
    public float length = 1.0f;
    [Tooltip("ペンライトの太さ（中心の芯）")]
    public float thickness = 0.03f;
    [Tooltip("実際の当たり判定の太さ（見た目より太くすると当てやすくなります）")]
    public float hitThickness = 0.1f;
    [Tooltip("ペンライトの色（後で好きなマテリアルに変更可能）")]
    public Color saberColor = Color.cyan;
    [Tooltip("テストプレイ中に実際の当たり判定を半透明で見せるかどうか")]
    public bool showHitZoneInGame = true;

    // スイングの速さを記録（Note.cs側で判定に使用します）
    public float VelocityMagnitude { get; private set; }
    private Vector3 previousPosition;

    private void Start()
    {
        // ペンライトの見た目を自動生成（シリンダー）
        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        visual.name = "SaberVisual";
        visual.transform.SetParent(transform, false);
        
        // シリンダーはデフォルトでY軸方向に長さ2なので、Z軸（前方）に伸ばすために回転させる
        visual.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        visual.transform.localScale = new Vector3(thickness, length / 2f, thickness);
        
        // コントローラーの先端から前に伸びるように位置を調整
        visual.transform.localPosition = new Vector3(0f, 0f, length / 2f);

        // マテリアルを設定して光らせる（簡単なUnlit風）
        Renderer rend = visual.GetComponent<Renderer>();
        Material mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = saberColor;
        rend.sharedMaterial = mat;

        // --- 実際の当たり判定（コライダー）の視覚化 ---
        if (showHitZoneInGame)
        {
            GameObject hitboxVisual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            hitboxVisual.name = "HitboxVisual";
            hitboxVisual.transform.SetParent(transform, false);
            hitboxVisual.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            hitboxVisual.transform.localScale = new Vector3(hitThickness, length / 2f, hitThickness);
            hitboxVisual.transform.localPosition = new Vector3(0f, 0f, length / 2f);
            Destroy(hitboxVisual.GetComponent<Collider>()); // 見た目だけなのでコライダー削除

            Renderer hitRend = hitboxVisual.GetComponent<Renderer>();
            
            // 半透明の赤いマテリアルを作成
            Material hitMat = new Material(Shader.Find("Standard"));
            Shader urpShader = Shader.Find("Universal Render Pipeline/Lit");
            if (urpShader != null)
            {
                hitMat.shader = urpShader;
                hitMat.SetFloat("_Surface", 1.0f); // Transparent
                hitMat.SetOverrideTag("RenderType", "Transparent");
                hitMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                hitMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                hitMat.SetInt("_ZWrite", 0);
                hitMat.DisableKeyword("_ALPHATEST_ON");
                hitMat.EnableKeyword("_ALPHABLEND_ON");
                hitMat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                hitMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            else
            {
                hitMat.SetFloat("_Mode", 3);
                hitMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                hitMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                hitMat.SetInt("_ZWrite", 0);
                hitMat.DisableKeyword("_ALPHATEST_ON");
                hitMat.EnableKeyword("_ALPHABLEND_ON");
                hitMat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                hitMat.renderQueue = 3000;
            }
            
            hitMat.color = new Color(1f, 0f, 0f, 0.3f); // 半透明の赤
            hitRend.material = hitMat;
        }

        // コライダーの設定（物理判定を正確にするためシリンダーのコライダーを削除し、CapsuleColliderを親に追加）
        Destroy(visual.GetComponent<Collider>());
        
        CapsuleCollider col = gameObject.AddComponent<CapsuleCollider>();
        col.isTrigger = true; // ノーツとすり抜ける（OnTriggerEnterで判定）
        col.radius = hitThickness; // 実際の判定の太さ
        col.height = length;
        col.direction = 2; // Z-Axis
        col.center = new Vector3(0f, 0f, length / 2f);

        // 物理エンジンのトリガー判定を確実に動作させるため、KinematicなRigidbodyを追加
        Rigidbody rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        // --- 自動追従設定（位置と回転） ---
#if ENABLE_INPUT_SYSTEM
        TrackedPoseDriver poseDriver = GetComponent<TrackedPoseDriver>();
        if (poseDriver == null)
        {
            poseDriver = gameObject.AddComponent<TrackedPoseDriver>();
        }

        string hand = (handType == HandType.Right) ? "RightHand" : "LeftHand";
        
        InputAction posAction = new InputAction("Position", binding: $"<XRController>{{{hand}}}/devicePosition");
        InputAction rotAction = new InputAction("Rotation", binding: $"<XRController>{{{hand}}}/deviceRotation");
        
        posAction.Enable();
        rotAction.Enable();

        poseDriver.positionAction = posAction;
        poseDriver.rotationAction = rotAction;
#endif

        previousPosition = transform.position;
    }

    private void Update()
    {
        // 1フレーム前の位置との差分から、剣を振るスピード（速度）を計算する
        VelocityMagnitude = (transform.position - previousPosition).magnitude / Time.deltaTime;
        previousPosition = transform.position;
    }
}
