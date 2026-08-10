using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[System.Serializable]
public class MovementNode
{
    [Tooltip("移動を開始する曲の秒数（BGMの経過時間）")]
    public float startTime;
    
    [Tooltip("移動目標となるシーン上のTransformオブジェクト（位置と向き）")]
    public Transform targetMarker;
    
    [Tooltip("移動にかける時間（秒）。0以下の場合は瞬間移動（ワープ）します")]
    public float duration = 1.0f;

    [Tooltip("移動開始時に再生するアニメーションステート名（空欄の場合は再生しません）")]
    public string animationStateName;

    [Tooltip("移動終了時に待機モーション（WAIT00など）へ戻すか")]
    public bool returnToIdleOnComplete = true;

    [Tooltip("移動のイージングカーブ（デフォルトは開始・終了時になめらかになるEaseInOut）")]
    public AnimationCurve easeCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    
    [HideInInspector] public bool hasTriggered = false;
}

public class UnityChanChoreography : MonoBehaviour
{
    [Header("Choreography Settings")]
    [Tooltip("自動移動の登録ノードリスト（インスペクター上で追加・編集可能）")]
    public List<MovementNode> movementNodes = new List<MovementNode>();

    [Tooltip("待機時（Idle）のアニメーションステート名。returnToIdleOnCompleteの際に使用されます")]
    public string idleStateName = "WAIT00";

    private Animator animator;
    private AudioSource bgmSource;
    private Coroutine currentMoveCoroutine;

    // 現在自動移動が進行中かどうかを取得するプロパティ
    public bool IsMoving => currentMoveCoroutine != null;

    void Start()
    {
        animator = GetComponent<Animator>();
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        // GameManagerからBGM再生源(AudioSource)を取得
        if (GameManager.Instance != null)
        {
            bgmSource = GameManager.Instance.bgmSource;
        }
        else
        {
            bgmSource = FindAnyObjectByType<AudioSource>();
        }

        if (bgmSource == null)
        {
            Debug.LogError("UnityChanChoreography: BGM再生用のAudioSourceが見つかりません。");
        }

        // 念のため、ノードを開始時間の昇順で並び替える
        movementNodes.Sort((a, b) => a.startTime.CompareTo(b.startTime));
    }

    void Update()
    {
        if (bgmSource == null || !bgmSource.isPlaying) return;

        float currentTrackTime = bgmSource.time;

        // 時間に到達した未トリガーのノードをトリガーする
        for (int i = 0; i < movementNodes.Count; i++)
        {
            var node = movementNodes[i];
            if (!node.hasTriggered && currentTrackTime >= node.startTime)
            {
                node.hasTriggered = true;
                TriggerMovement(node);
            }
        }
    }

    private void TriggerMovement(MovementNode node)
    {
        if (node.targetMarker == null)
        {
            Debug.LogWarning($"[Choreography] タイミング {node.startTime}s の targetMarker が設定されていません。");
            return;
        }

        // すでに進行中の自動移動があれば強制的に上書き中断する
        if (currentMoveCoroutine != null)
        {
            StopCoroutine(currentMoveCoroutine);
        }

        currentMoveCoroutine = StartCoroutine(MoveSequence(node));
    }

    private IEnumerator MoveSequence(MovementNode node)
    {
        // 1. 指定アニメーションの再生開始
        if (animator != null && !string.IsNullOrEmpty(node.animationStateName))
        {
            Debug.Log($"[Choreography] アニメーション再生: {node.animationStateName}");
            // アニメーションを0.15秒のブレンド時間でスムーズに再生
            animator.CrossFadeInFixedTime(node.animationStateName, 0.15f);
        }

        Vector3 startPos = transform.position;
        Quaternion startRot = transform.rotation;

        Vector3 targetPos = node.targetMarker.position;
        Quaternion targetRot = node.targetMarker.rotation;

        float duration = node.duration;

        if (duration <= 0f)
        {
            // 瞬間移動（ワープ）
            transform.position = targetPos;
            transform.rotation = targetRot;
        }
        else
        {
            // 時間をかけた補間移動
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float normalizedTime = Mathf.Clamp01(elapsed / duration);
                
                // イージング（加速・減速カーブ）の適用
                float t = node.easeCurve.Evaluate(normalizedTime);

                // 線形補間（Lerp）と球面線形補間（Slerp）で座標と角度を滑らかに更新
                transform.position = Vector3.Lerp(startPos, targetPos, t);
                transform.rotation = Quaternion.Slerp(startRot, targetRot, t);

                yield return null;
            }

            // 移動完了時に目標位置にぴったり合わせる
            transform.position = targetPos;
            transform.rotation = targetRot;
        }

        // 2. 移動完了後に待機モーションに戻す
        if (node.returnToIdleOnComplete && animator != null)
        {
            animator.CrossFadeInFixedTime(idleStateName, 0.25f);
        }

        currentMoveCoroutine = null;
    }

    /// <summary>
    /// シーンがロード・再起動されたときなどのために、トリガー状態をすべてリセットするメソッド
    /// </summary>
    public void ResetChoreography()
    {
        if (currentMoveCoroutine != null)
        {
            StopCoroutine(currentMoveCoroutine);
            currentMoveCoroutine = null;
        }
        
        foreach (var node in movementNodes)
        {
            node.hasTriggered = false;
        }
    }
}
