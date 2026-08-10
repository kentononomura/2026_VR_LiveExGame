using System.Collections;
using UnityEngine;

public class UnityChanReaction : MonoBehaviour
{
    private Animator animator;

    [Header("Animation States")]
    [Tooltip("待機時（Idle）のアニメーションステート名")]
    public string idleStateName = "WAIT00";
    
    [Tooltip("Perfect判定時のアニメーションステート名")]
    public string perfectStateName = "WIN00";
    
    [Tooltip("Great判定時のアニメーションステート名")]
    public string greatStateName = "WIN00";
    
    [Tooltip("Good判定時のアニメーションステート名")]
    public string goodStateName = "JUMP00";
    
    [Tooltip("Miss判定時のアニメーションステート名")]
    public string missStateName = "DAMAGED00";

    [Header("Reaction Settings")]
    [Tooltip("リアクションポーズから待機状態に戻るまでの時間（秒）")]
    public float returnToIdleDelay = 1.2f;

    private Coroutine reactionCoroutine;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        if (animator == null)
        {
            Debug.LogError("UnityChanReaction: Animatorコンポーネントが見つかりません。UnityちゃんにAnimatorがアタッチされているか確認してください。");
        }
    }

    /// <summary>
    /// 判定に応じたリアクションアニメーションを再生します。
    /// </summary>
    /// <param name="judgment">"Perfect", "Great", "Good", "Miss" のいずれか</param>
    public void PlayReaction(string judgment)
    {
        if (animator == null)
        {
            Debug.LogWarning("UnityChanReaction: Animatorがヌルのため、リアクションをスキップします。");
            return;
        }

        // 自動移動（振付）中は、ノーツ判定によるリアクションアニメーションを無視して移動モーションを維持する
        UnityChanChoreography choreography = GetComponent<UnityChanChoreography>();
        if (choreography == null) choreography = GetComponentInParent<UnityChanChoreography>();
        if (choreography != null && choreography.IsMoving)
        {
            return;
        }

        string targetState = "";

        switch (judgment)
        {
            case "Perfect":
                targetState = perfectStateName;
                break;
            case "Great":
                targetState = greatStateName;
                break;
            case "Good":
                targetState = goodStateName;
                break;
            case "Miss":
                targetState = missStateName;
                break;
            default:
                // 上記の標準判定名以外が渡された場合は、その文字列をそのままステート名として扱う（自由なアニメーション用）
                targetState = judgment;
                break;
        }

        if (!string.IsNullOrEmpty(targetState))
        {
            Debug.Log($"UnityChanReaction: 判定 '{judgment}' に基づき、アニメーション '{targetState}' を再生します。");
            
            if (reactionCoroutine != null)
            {
                StopCoroutine(reactionCoroutine);
            }
            reactionCoroutine = StartCoroutine(ReactionSequence(targetState));
        }
    }

    private IEnumerator ReactionSequence(string stateName)
    {
        // アプリケーション実行中にステートが存在するか簡易的に判定して警告を出す
        if (animator.runtimeAnimatorController == null)
        {
            Debug.LogError("UnityChanReaction: Animator Controllerが設定されていません！UnityちゃんにAnimatorControllerをアサインしてください。");
            yield break;
        }

        // 指定したアニメーションステートにスムーズに切り替える
        animator.CrossFadeInFixedTime(stateName, 0.1f);

        // 指定時間待機
        yield return new WaitForSeconds(returnToIdleDelay);

        // 待機状態に戻す
        animator.CrossFadeInFixedTime(idleStateName, 0.25f);
        reactionCoroutine = null;
    }
}
