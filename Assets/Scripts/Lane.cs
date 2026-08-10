using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class Lane : MonoBehaviour
{
#if ENABLE_INPUT_SYSTEM
    public Key inputKey;
#else
    public KeyCode inputKey;
#endif
    public int laneIndex;

    [Header("判定の閾値（ノーツとの距離）")]
    [Tooltip("Perfect判定の距離閾値（初期値は少し緩めの0.6に最適化）")]
    public float perfectThreshold = 0.6f;
    [Tooltip("Great判定の距離閾値（初期値は少し緩めの1.2に最適化）")]
    public float greatThreshold = 1.2f;
    [Tooltip("Good判定の距離閾値（初期値は少し緩めの1.8に最適化）")]
    public float goodThreshold = 1.8f;
    
    [Header("デバッグ・可視化")]
    [Tooltip("判定エリア（Perfect=赤, Great=黄, Good=緑）をゲーム画面に可視化するかどうか")]
    public bool showJudgmentZones = true;

    private GameObject perfectZoneObj;
    private GameObject greatZoneObj;
    private GameObject goodZoneObj;
    
    private List<Note> notesInLane = new List<Note>();
    private Note holdingNote = null;

    private void Awake()
    {
        // VR版ではSaberで直接ノーツを叩くため、古いキーボード用のLaneオブジェクトは完全に不要です。
        // 万が一ヒエラルキーにオブジェクトが残っていた場合、自動で破棄して競合を防ぎます。
        Destroy(gameObject);
    }

    private void Start()
    {
        // 【重要バグ修正】
        // レーンの見た目の厚み(localScale.y)を小さくした影響で、ノーツを検知するコライダーまで縮小してしまい、
        // GreatやGoodの範囲に入ってもコライダーに接触せず検知されない不具合を防止するため、
        // コライダーの大きさを「goodThreshold」の範囲より常に大きくなるように自動拡張します。
        BoxCollider col = GetComponent<BoxCollider>();
        if (col != null)
        {
            // Good判定の上下幅(goodThreshold * 2) ＋ 少しの余裕(2.0f) を確保
            float requiredHeight = (goodThreshold * 2f) + 2.0f;
            col.size = new Vector3(col.size.x, requiredHeight / transform.localScale.y, col.size.z);
        }

        if (showJudgmentZones)
        {
            CreateJudgmentZones();
        }
    }

    private void CreateJudgmentZones()
    {
        float parentYScale = transform.localScale.y;

        goodZoneObj = CreateZoneQuad("GoodZone", new Color(0f, 1f, 0f, 0.2f), goodThreshold, parentYScale, 1);
        greatZoneObj = CreateZoneQuad("GreatZone", new Color(1f, 0.8f, 0f, 0.3f), greatThreshold, parentYScale, 2);
        perfectZoneObj = CreateZoneQuad("PerfectZone", new Color(1f, 0.2f, 0.2f, 0.5f), perfectThreshold, parentYScale, 3);
    }

    private GameObject CreateZoneQuad(string name, Color color, float threshold, float parentYScale, int order)
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = name;
        Destroy(quad.GetComponent<Collider>());
        
        quad.transform.SetParent(transform, false);
        quad.transform.localPosition = new Vector3(0f, 0f, -0.01f * order);
        quad.transform.localScale = new Vector3(1f, (threshold * 2f) / parentYScale, 1f);

        Material mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = color;
        quad.GetComponent<Renderer>().sharedMaterial = mat;

        return quad;
    }

    private void Update()
    {
        // 判定エリアの表示切り替え・スケール動的更新（インスペクター調整用）
        if (showJudgmentZones && perfectZoneObj == null) CreateJudgmentZones();
        else if (!showJudgmentZones && perfectZoneObj != null)
        {
            Destroy(perfectZoneObj);
            Destroy(greatZoneObj);
            Destroy(goodZoneObj);
        }
        else if (showJudgmentZones && perfectZoneObj != null)
        {
            float parentYScale = transform.localScale.y;
            perfectZoneObj.transform.localScale = new Vector3(1f, (perfectThreshold * 2f) / parentYScale, 1f);
            greatZoneObj.transform.localScale = new Vector3(1f, (greatThreshold * 2f) / parentYScale, 1f);
            goodZoneObj.transform.localScale = new Vector3(1f, (goodThreshold * 2f) / parentYScale, 1f);
        }

        notesInLane.RemoveAll(n => n == null);

        // --- 1. 見逃しMiss（スルーMiss）の自動判定 ---
        // ノーツを叩かずに判定ラインを完全に通り過ぎた場合、即座にMiss判定にしてオブジェクトを削除します。
        // これにより、通り過ぎた後の違和感のある遅れが解消されます。
        for (int i = notesInLane.Count - 1; i >= 0; i--)
        {
            Note note = notesInLane[i];
            Vector3 noteLocalPos = transform.InverseTransformPoint(note.transform.position);
            
            // 判定ラインを通り過ぎて、Goodの閾値よりも手前に来てしまった場合
            // スケールの影響を補正するため、localScale.y を掛けます
            float unscaledY = noteLocalPos.y * transform.localScale.y;
            if (unscaledY < -goodThreshold)
            {
                GameManager.Instance.Miss();
                notesInLane.RemoveAt(i);
                Destroy(note.gameObject);
            }
        }

        bool isPressedDown = false;
        bool isHoldingKey = false;
        bool isReleased = false;

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            isPressedDown = Keyboard.current[inputKey].wasPressedThisFrame;
            isHoldingKey = Keyboard.current[inputKey].isPressed;
            isReleased = Keyboard.current[inputKey].wasReleasedThisFrame;
        }
#else
        isPressedDown = Input.GetKeyDown(inputKey);
        isHoldingKey = Input.GetKey(inputKey);
        isReleased = Input.GetKeyUp(inputKey);
#endif

        if (holdingNote != null)
        {
            if (isReleased || !isHoldingKey)
            {
                EvaluateHoldEnd(holdingNote);
                holdingNote = null;
            }
            else
            {
                // ロングノーツの押し続け判定
                Vector3 tailLocalPos = transform.InverseTransformPoint(holdingNote.GetTailWorldPosition());
                float unscaledTailY = tailLocalPos.y * transform.localScale.y;
                
                // テールが判定ライン(0)を通過するまで押し続けた場合、自動的にPerfect判定にする（現代の音ゲーの標準的な仕様）
                if (unscaledTailY <= 0f)
                {
                    GameManager.Instance.AddScore(100, "Perfect");
                    Destroy(holdingNote.gameObject);
                    holdingNote = null;
                }
                // 万が一通り過ぎた場合のフェールセーフ
                else if (unscaledTailY < -goodThreshold) 
                {
                    GameManager.Instance.Miss();
                    Destroy(holdingNote.gameObject);
                    holdingNote = null;
                }
            }
        }
        else
        {
            if (isPressedDown)
            {
                if (notesInLane.Count > 0)
                {
                    Note targetNote = notesInLane[0];
                    EvaluateNoteStart(targetNote);
                }
            }
        }
    }

    private void EvaluateNoteStart(Note note)
    {
        Vector3 noteLocalPos = transform.InverseTransformPoint(note.transform.position);
        
        // スケールの影響（0.3倍）を補正して、実際の距離（ワールド/親空間ベース）を計算
        float distance = Mathf.Abs(noteLocalPos.y * transform.localScale.y);

        // 閾値より遠い場合は空打ちとして反応させない
        if (distance > goodThreshold)
        {
            return;
        }

        bool isHit = false;

        if (distance <= perfectThreshold) { GameManager.Instance.AddScore(100, "Perfect"); isHit = true; }
        else if (distance <= greatThreshold) { GameManager.Instance.AddScore(50, "Great"); isHit = true; }
        else if (distance <= goodThreshold) { GameManager.Instance.AddScore(10, "Good"); isHit = true; }

        notesInLane.Remove(note);

        if (isHit && note.type == NoteType.Long)
        {
            holdingNote = note;
            note.isHeld = true; 
        }
        else
        {
            Destroy(note.gameObject);
        }
    }

    private void EvaluateHoldEnd(Note note)
    {
        Vector3 tailLocalPos = transform.InverseTransformPoint(note.GetTailWorldPosition());
        
        // スケールの影響を補正
        float distance = Mathf.Abs(tailLocalPos.y * transform.localScale.y);
        
        if (distance <= perfectThreshold) { GameManager.Instance.AddScore(100, "Perfect"); }
        else if (distance <= greatThreshold) { GameManager.Instance.AddScore(50, "Great"); }
        else if (distance <= goodThreshold) { GameManager.Instance.AddScore(10, "Good"); }
        else { GameManager.Instance.Miss(); }

        Destroy(note.gameObject);
    }

    private void OnTriggerEnter(Collider other)
    {
        Note note = other.GetComponentInParent<Note>();
        if (note != null && note.laneIndex == this.laneIndex)
        {
            if (!notesInLane.Contains(note))
                notesInLane.Add(note);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        Note note = other.GetComponentInParent<Note>();
        if (note != null && notesInLane.Contains(note))
        {
            notesInLane.Remove(note);
        }
    }
}
