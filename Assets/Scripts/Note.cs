using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Note : MonoBehaviour
{
    public float speed = 5f;
    public int laneIndex;
    public NoteType type;
    public float duration;
    public bool isSimultaneous;
    public bool isHeld = false;

    public Transform visual;
    private Material generatedBodyMat;

    // 判定用の設定（Spawnerから受け取る）
    private float hitZoneZ;
    private float perfectThreshold;
    private float greatThreshold;
    private float goodThreshold;
    private Vector3 noteScale;
    
    // なぞり判定の緩和（猶予時間）用の変数
    private float graceTimer = 0f;
    private bool isGracePeriodActive = false;
    private float graceDuration = 0.25f; // 0.25秒の猶予

    // ローカル座標でのTailZ計算（親オブジェクトの中での相対奥行き）
    public float TailLocalZ => transform.localPosition.z + (type == NoteType.Long ? duration * speed : 0f);

    // 終点のワールド座標を取得するメソッド
    public Vector3 GetTailWorldPosition()
    {
        if (type == NoteType.Long)
        {
            // 斜めロングノーツに対応するため、実体オブジェクト（Tail）の位置を返す
            Transform tailTrans = transform.Find("Tail");
            if (tailTrans != null) return tailTrans.position;
            
            return transform.TransformPoint(new Vector3(0, 0, duration * speed));
        }
        return transform.position;
    }

    public void Initialize(NoteData data, float noteSpeed, Material normalMat, Material simMat, NoteSpawner spawner)
    {
        this.speed = noteSpeed;
        this.laneIndex = data.laneIndex;
        this.type = data.type;
        this.duration = data.duration;
        this.isSimultaneous = data.isSimultaneous;

        this.hitZoneZ = spawner.hitZoneZ;
        this.perfectThreshold = spawner.perfectThreshold;
        this.greatThreshold = spawner.greatThreshold;
        this.goodThreshold = spawner.goodThreshold;
        this.noteScale = spawner.noteScale;

        Renderer rend = visual.GetComponent<Renderer>();
        Material baseMat = isSimultaneous ? simMat : normalMat;
        
        // 始点（叩く場所）は常に濃いマテリアルをそのまま適用
        rend.sharedMaterial = baseMat;

        if (type == NoteType.Long)
        {
            // 【超重要バグ修正】
            // 親オブジェクト(Note)と子オブジェクト(Body, Tail)にそれぞれコライダーが付いていると、
            // 剣がヘッドからボディへ移動する瞬間に「親の判定から外れた」と誤検知（TriggerExit）され、
            // なぞっている途中で勝手にMissになってしまう重大な競合不具合がありました。
            // これを防ぐため、ロングノーツでは判定コライダーを「Body」の1枚だけに一本化し、
            // 親(Note)と終点(Tail)の不要なコライダーは削除します。
            BoxCollider rootCol = GetComponent<BoxCollider>();
            if (rootCol != null)
            {
                Destroy(rootCol);
            }

            // 始点レーンと終点レーンを取得
            Transform startLane = spawner.laneTransforms != null && laneIndex < spawner.laneTransforms.Length 
                ? spawner.laneTransforms[laneIndex] 
                : null;
            
            int targetEndLane = data.endLaneIndex != -1 ? data.endLaneIndex : laneIndex;
            Transform endLane = spawner.laneTransforms != null && targetEndLane < spawner.laneTransforms.Length 
                ? spawner.laneTransforms[targetEndLane] 
                : null;

            // 始点（ヘッド）の描画設定
            visual.localScale = noteScale;
            visual.localPosition = new Vector3(0f, 0f, noteScale.z / 2f);

            // テールのローカル目標位置を計算
            Vector3 posHead = Vector3.zero;
            Vector3 posTail;

            if (startLane != null && endLane != null)
            {
                // 始点レーンから見た、終点レーンの相対位置
                Vector3 targetLocalPos = startLane.InverseTransformPoint(endLane.position);
                posTail = new Vector3(targetLocalPos.x, targetLocalPos.y, targetLocalPos.z + duration * speed);
            }
            else
            {
                // フォールバック（レーンオブジェクトが割り当てられていない場合）
                float startX = (laneIndex - 1.5f) * spawner.laneSpacing;
                float endX = (targetEndLane - 1.5f) * spawner.laneSpacing;
                float deltaX = endX - startX;
                posTail = new Vector3(deltaX, 0f, duration * speed);
            }

            Vector3 direction = posTail - posHead;
            float length = Mathf.Max(direction.magnitude, noteScale.z * 1.2f);
            float bodyLength = length - noteScale.z;
            Vector3 posMid = posTail / 2f;

            // 押し続ける部分（ボディ）
            GameObject bodyObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bodyObj.name = "Body";
            bodyObj.GetComponent<Collider>().isTrigger = true; // この「Body」の判定だけを唯一有効にする
            
            bodyObj.transform.SetParent(transform, false);
            // なぞり判定をさらに緩和するため、ボディの横幅(X)と縦幅(Y)を少し太め(1.4倍)にする
            bodyObj.transform.localScale = new Vector3(noteScale.x * 1.4f, noteScale.y * 1.4f, bodyLength); 
            bodyObj.transform.localPosition = posMid;
            bodyObj.transform.localRotation = Quaternion.LookRotation(direction);

            Renderer bodyRend = bodyObj.GetComponent<Renderer>();
            generatedBodyMat = CreateTransparentMaterial(baseMat);
            bodyRend.material = generatedBodyMat;

            // 終点（テール）
            GameObject tailObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tailObj.name = "Tail";
            
            // 判定をボディ1枚に一本化するため、テールのコライダーは不要なので削除
            Destroy(tailObj.GetComponent<Collider>());
            
            tailObj.transform.SetParent(transform, false);
            tailObj.transform.localScale = noteScale; 
            tailObj.transform.localPosition = posTail;
            tailObj.transform.localRotation = Quaternion.LookRotation(direction);

            Renderer tailRend = tailObj.GetComponent<Renderer>();
            tailRend.sharedMaterial = baseMat; 
        }
        else
        {
            visual.localScale = noteScale;
            visual.localPosition = new Vector3(0f, 0f, noteScale.z / 2f);
        }

        if (GameManager.Instance != null)
        {
            GameManager.Instance.AddActiveNote();
        }
    }

    // 元のマテリアルから透明なマテリアルを生成するメソッド
    private Material CreateTransparentMaterial(Material source)
    {
        Material mat = new Material(source);
        
        if (mat.shader.name.Contains("Universal Render Pipeline"))
        {
            // URP用の透明描画設定
            mat.SetFloat("_Surface", 1.0f); // 1 = Transparent
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        else
        {
            // Standard Shader用（URP環境外の場合の念のため）
            mat.SetFloat("_Mode", 3);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;
        }

        // 透明度を 40% に下げる
        Color c = mat.color;
        c.a = 0.4f; 
        mat.color = c;
        
        return mat;
    }

    private void Update()
    {
        // ローカルのZ軸の手前（負の方向）へ移動（レーンの向きに沿う）
        transform.Translate(Vector3.back * speed * Time.deltaTime, Space.Self);

        // --- 猶予時間（Grace Period）の判定 ---
        if (type == NoteType.Long && isHeld && isGracePeriodActive)
        {
            graceTimer -= Time.deltaTime;
            if (graceTimer <= 0f)
            {
                // 猶予時間を超えても剣が戻らなかったらMiss判定
                isGracePeriodActive = false;
                GameManager.Instance.Miss();
                Destroy(gameObject);
                return;
            }
        }

        Camera mainCam = Camera.main;

        // --- なぞり中のロングノーツが最後まで正常に到達したかの判定 ---
        if (type == NoteType.Long && isHeld && !isGracePeriodActive)
        {
            // テール（終点）のローカルZ座標（カメラから見た位置）を計算
            Vector3 tailWorldPos = GetTailWorldPosition();
            Vector3 localTailPos = mainCam != null 
                ? mainCam.transform.InverseTransformPoint(tailWorldPos) 
                : transform.position; // フォールバック

            float tailZ = mainCam != null ? localTailPos.z : TailLocalZ;
            // プレイヤーの目の前（カメラから0.8m以内）までなぞり続けていれば自動的にクリア！
            if (tailZ <= 0.8f)
            {
                GameManager.Instance.AddScore(100, "Perfect"); // 完走ボーナス
                Destroy(gameObject);
                return;
            }
        }

        // カメラ（プレイヤーの頭）の位置を基準にして、見逃し判定を行う
        if (mainCam != null)
        {
            // ノーツ（またはテール）の位置をカメラから見たローカル座標系に変換
            Vector3 tailWorldPos = GetTailWorldPosition();
            Vector3 localTailPos = mainCam.transform.InverseTransformPoint(tailWorldPos);
            
            // カメラの後方（ローカルZがマイナス値）を一定距離通り過ぎたらMissとして破棄
            if (localTailPos.z < -1.0f)
            {
                if (!isHeld)
                {
                    GameManager.Instance.Miss();
                }
                Destroy(gameObject);
            }
        }
        else
        {
            // カメラが見つからない場合のフォールバック（従来のローカル座標チェック）
            float tailLocalZ = TailLocalZ;
            if (tailLocalZ < (hitZoneZ - goodThreshold - 0.5f))
            {
                if (!isHeld)
                {
                    GameManager.Instance.Miss();
                }
                Destroy(gameObject);
            }
        }
    }

    // --- Saberとの衝突判定 ---
    private HashSet<Collider> touchingSabers = new HashSet<Collider>();

    private void OnTriggerEnter(Collider other)
    {
        Saber saber = other.GetComponent<Saber>();
        if (saber != null)
        {
            if (!touchingSabers.Contains(other))
            {
                touchingSabers.Add(other);
            }
            
            if (type == NoteType.Normal && !isHeld)
            {
                HitNormalNote(saber);
            }
            else if (type == NoteType.Long)
            {
                if (!isHeld)
                {
                    // ロングノーツの開始判定
                    isHeld = true;
                    GameManager.Instance.AddScore(100, "Perfect"); // ヘッドヒット
                }
                else if (isGracePeriodActive)
                {
                    // 猶予時間内に剣が戻ってきた
                    isGracePeriodActive = false;
                }
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        Saber saber = other.GetComponent<Saber>();
        if (saber != null)
        {
            touchingSabers.Remove(other);
            
            // まだ他のSaberが触れているか確認
            bool stillTouching = false;
            foreach (var col in touchingSabers)
            {
                if (col != null && col.GetComponent<Saber>() != null)
                {
                    stillTouching = true;
                    break;
                }
            }

            if (type == NoteType.Long && isHeld && !stillTouching)
            {
                // テールが通り過ぎる前に剣が離れたら、猶予時間を開始
                Vector3 tailWorldPos = GetTailWorldPosition();
                Camera mainCam = Camera.main;
                float tailZ = mainCam != null 
                    ? mainCam.transform.InverseTransformPoint(tailWorldPos).z 
                    : TailLocalZ;

                // プレイヤーの剣の届く範囲（カメラから1.5m以内）までなぞりきっていればクリア！
                // それより遠い位置で離した場合は、猶予時間（Grace Period）を開始
                if (tailZ > 1.5f)
                {
                    isGracePeriodActive = true;
                    graceTimer = graceDuration;
                }
                else
                {
                    // 十分引きつけてから離した、あるいは最後までなぞりきって離れたらPerfect
                    GameManager.Instance.AddScore(100, "Perfect");
                    Destroy(gameObject);
                }
            }
        }
    }

    private void HitNormalNote(Saber saber)
    {
        isHeld = true; // 2回判定されないようにロック
        
        // --- 奥行き(Z)のタイミングではなく、剣を振るスピードで判定する（BeatSaber方式） ---
        float swingSpeed = saber != null ? saber.VelocityMagnitude : 0f;
        
        if (swingSpeed > 2.0f) 
        {
            // しっかりと剣を振って当てた
            GameManager.Instance.AddScore(100, "Perfect");
        }
        else if (swingSpeed > 0.5f) 
        {
            // 軽く振って当てた
            GameManager.Instance.AddScore(50, "Great");
        }
        else 
        {
            // 剣を止めたまま当てた（突き刺したなど）
            GameManager.Instance.AddScore(10, "Good");
        }
        
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        // 生成した透明マテリアルがメモリリークしないように破棄
        if (generatedBodyMat != null)
        {
            Destroy(generatedBodyMat);
        }

        if (GameManager.Instance != null)
        {
            GameManager.Instance.RemoveActiveNote();
        }
    }
}
