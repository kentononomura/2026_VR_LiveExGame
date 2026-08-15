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
    public bool showHitZoneInGame = false;

    [Header("Custom Visual")]
    [Tooltip("ペンライトの3Dモデル（FBX等）。未設定の場合はCylinderが生成されます。")]
    public GameObject visualPrefab;
    [Tooltip("生成時のモデルの向き（角度）。UIなどを正面に向けるために調整できます。")]
    public Vector3 visualRotation = new Vector3(90f, 90f, 0f);
    [Tooltip("生成時のモデルの位置ズレ補正。コントローラーの持ち手とペンライトの持ち手を合わせるのに使います。")]
    public Vector3 visualPositionOffset = Vector3.zero;

    // スイングの速さを記録（Note.cs側で判定に使用します）
    public float VelocityMagnitude { get; private set; }
    private Vector3 previousPosition;

    private void Start()
    {
        GameObject visual = null;

        if (visualPrefab != null)
        {
            // カスタムモデルの生成
            visual = Instantiate(visualPrefab, transform);
            visual.name = "SaberVisual";
            
            // ユーザー指定の向き（初期値は X:90, Y:90, Z:0）を適用
            visual.transform.localRotation = Quaternion.Euler(visualRotation);
            
            // ユーザー指定によりスケールを(2,2,2)に固定
            visual.transform.localScale = new Vector3(2f, 2f, 2f);
            
            // ユーザー指定の位置ズレ補正を適用
            visual.transform.localPosition = visualPositionOffset;

            // 色と発光（Emission）の適用
            Renderer[] renderers = visual.GetComponentsInChildren<Renderer>();
            foreach (Renderer rend in renderers)
            {
                // マテリアルをインスタンス化して元データを汚さないようにする
                Material[] mats = rend.materials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i].name.Contains("Light"))
                    {
                        mats[i].color = saberColor;
                        mats[i].EnableKeyword("_EMISSION");
                        // HDRカラーとして設定し、ブルームで光るようにする
                        mats[i].SetColor("_EmissionColor", saberColor * 2.0f); 
                    }
                }
                rend.materials = mats;
            }

            // 当たり判定をモデルのサイズに合わせる
            // スケール2倍の場合、元の長さ(0.16) * 2 = 0.32m
            float modelLength = 0.32f; 
            float modelRadius = 0.02f;

            CapsuleCollider col = gameObject.AddComponent<CapsuleCollider>();
            col.isTrigger = true;
            col.radius = modelRadius;
            col.height = modelLength;
            col.direction = 2; // Z-Axis
            // 原点が根本の場合、中心はZ方向に半分の位置
            col.center = new Vector3(0f, 0f, modelLength / 2f);
        }
        else
        {
            // 旧バージョンのCylinder生成ロジック（フォールバック）
            visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            visual.name = "SaberVisual";
            visual.transform.SetParent(transform, false);
            visual.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            visual.transform.localScale = new Vector3(thickness, length / 2f, thickness);
            visual.transform.localPosition = new Vector3(0f, 0f, length / 2f);

            Renderer rend = visual.GetComponent<Renderer>();
            Material mat = new Material(Shader.Find("Sprites/Default"));
            mat.color = saberColor;
            rend.sharedMaterial = mat;

            Destroy(visual.GetComponent<Collider>());
            CapsuleCollider col = gameObject.AddComponent<CapsuleCollider>();
            col.isTrigger = true;
            col.radius = hitThickness;
            col.height = length;
            col.direction = 2;
            col.center = new Vector3(0f, 0f, length / 2f);
        }

        // --- 実際の当たり判定（コライダー）の視覚化 ---
        if (showHitZoneInGame)
        {
            GameObject hitboxVisual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            hitboxVisual.name = "HitboxVisual";
            hitboxVisual.transform.SetParent(transform, false);
            hitboxVisual.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            
            if (visualPrefab != null)
            {
                hitboxVisual.transform.localScale = new Vector3(0.02f, 0.32f / 2f, 0.02f);
                hitboxVisual.transform.localPosition = new Vector3(0f, 0f, 0.32f / 2f);
            }
            else
            {
                hitboxVisual.transform.localScale = new Vector3(hitThickness, length / 2f, hitThickness);
                hitboxVisual.transform.localPosition = new Vector3(0f, 0f, length / 2f);
            }
            
            Destroy(hitboxVisual.GetComponent<Collider>()); 

            Renderer hitRend = hitboxVisual.GetComponent<Renderer>();
            Material hitMat = new Material(Shader.Find("Standard"));
            Shader urpShader = Shader.Find("Universal Render Pipeline/Lit");
            if (urpShader != null)
            {
                hitMat.shader = urpShader;
                hitMat.SetFloat("_Surface", 1.0f);
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
            
            hitMat.color = new Color(1f, 0f, 0f, 0.3f); 
            hitRend.material = hitMat;
        }

        // 物理エンジンのトリガー判定用
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
        }
        rb.isKinematic = true;
        rb.useGravity = false;

        // --- 自動追従設定 ---
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
        VelocityMagnitude = (transform.position - previousPosition).magnitude / Time.deltaTime;
        previousPosition = transform.position;
    }
}
