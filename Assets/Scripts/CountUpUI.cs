using DG.Tweening;
using System.Globalization;
using TMPro;
using UnityEngine;

/// <summary>
/// リザルト画面向けの、サウンドと振動を伴う数値カウントアップUI。
/// </summary>
public sealed class CountUpUI : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("カウントアップする数値を表示するTextMeshProUGUIです。")]
    [SerializeField] private TextMeshProUGUI valueText;

    [Header("Sound References")]
    [Tooltip("増加中のループ音を再生するAudioSourceです。到達音用とは別のAudioSourceを指定してください。")]
    [SerializeField] private AudioSource countLoopAudioSource;

    [Tooltip("目標値に到達したときの決定音を再生するAudioSourceです。")]
    [SerializeField] private AudioSource reachedAudioSource;

    [Tooltip("カウントアップ中に再生するループ音です。")]
    [SerializeField] private AudioClip countLoopClip;

    [Tooltip("目標値に到達した瞬間にPlayOneShotで再生する決定音です。")]
    [SerializeField] private AudioClip reachedClip;

    [Header("Count Animation")]
    [Tooltip("数値が100増加するごとに加算されるアニメーション時間（秒）です。")]
    [Min(0.001f)]
    [SerializeField] private float secondsPer100 = 1.5f;

    [Tooltip("目標値が大きい場合でも、この秒数以内にカウントアップを完了します。")]
    [Min(0.001f)]
    [SerializeField] private float maxAnimationDuration = 3f;

    [Tooltip("カウントアップのイージングです。OutExpoにすると、序盤が速く終盤で焦らす動きになります。")]
    [SerializeField] private Ease countEase = Ease.OutExpo;

    [Header("Shake Animation")]
    [Tooltip("カウントアップ中の微振動の強さ（UIのアンカー座標単位）です。")]
    [Min(0f)]
    [SerializeField] private float shakeStrength = 3f;

    [Tooltip("1秒あたりのおおよその振動回数です。")]
    [Min(1)]
    [SerializeField] private int shakeVibratoPerSecond = 30;

    [Tooltip("振動方向のランダムさです。0で規則的、180で完全にランダムになります。")]
    [Range(0f, 180f)]
    [SerializeField] private float shakeRandomness = 90f;

    [Header("Reached Animation")]
    [Tooltip("到達時に元の大きさへ加えるパンチ量です。例: 0.2なら約20%大きく跳ねます。")]
    [Min(0f)]
    [SerializeField] private float punchScaleAmount = 0.2f;

    [Tooltip("到達時のスケールパンチ時間（秒）です。")]
    [Min(0.001f)]
    [SerializeField] private float punchDuration = 0.45f;

    [Tooltip("到達時のパンチの細かさです。")]
    [Min(1)]
    [SerializeField] private int punchVibrato = 8;

    [Tooltip("到達時に数値へ適用するハイライトカラーです。")]
    [SerializeField] private Color highlightColor = new Color(1f, 0.85f, 0.15f, 1f);

    [Tooltip("通常色からハイライトカラーへ変化する時間（秒）です。")]
    [Min(0f)]
    [SerializeField] private float colorChangeDuration = 0.15f;

    private RectTransform textRectTransform;
    private Vector2 baseAnchoredPosition;
    private Vector3 baseScale;
    private Color baseColor;
    private Tween countTween;
    private Tween shakeTween;
    private Tween punchTween;
    private Tween colorTween;

    private void Awake()
    {
        if (valueText == null)
        {
            valueText = GetComponent<TextMeshProUGUI>();
        }

        if (valueText == null)
        {
            Debug.LogError($"[{nameof(CountUpUI)}] TextMeshProUGUIが設定されていません。", this);
            enabled = false;
            return;
        }

        textRectTransform = valueText.rectTransform;
        CacheBaseVisualState();
        SetDisplayedValue(0);
    }

    /// <summary>
    /// 実行時に生成されたリザルトUIから参照を設定します。
    /// Inspectorで設定済みの項目は、nullを渡した場合そのまま維持されます。
    /// </summary>
    public void Configure(
        TextMeshProUGUI text,
        AudioSource loopAudioSource = null,
        AudioClip loopClip = null,
        AudioSource completionAudioSource = null,
        AudioClip completionClip = null)
    {
        if (text != null)
        {
            valueText = text;
            textRectTransform = valueText.rectTransform;
            CacheBaseVisualState();
            SetDisplayedValue(0);
        }

        if (loopAudioSource != null) countLoopAudioSource = loopAudioSource;
        if (loopClip != null) countLoopClip = loopClip;
        if (completionAudioSource != null) reachedAudioSource = completionAudioSource;
        if (completionClip != null) reachedClip = completionClip;
    }

    /// <summary>
    /// 実行時生成UIから、到達時のハイライトカラーを設定します。
    /// </summary>
    public void ConfigureHighlightColor(Color color)
    {
        highlightColor = color;
    }

    /// <summary>
    /// 0から指定値までカウントアップします。
    /// UI ButtonのOnClickや、リザルト表示処理から呼び出してください。
    /// </summary>
    /// <param name="targetValue">目標値。負の値は0として扱います。</param>
    public void PlayCountUp(int targetValue)
    {
        if (!isActiveAndEnabled || valueText == null)
        {
            return;
        }

        targetValue = Mathf.Max(0, targetValue);
        StopCurrentAnimation();
        RestoreBaseVisualState();
        SetDisplayedValue(0);

        if (targetValue == 0)
        {
            CompleteCountUp(0);
            return;
        }

        float duration = Mathf.Min((targetValue / 100f) * secondsPer100, maxAnimationDuration);
        duration = Mathf.Max(0.001f, duration);

        PlayLoopSound();
        StartShake(duration);

        int displayedValue = 0;
        countTween = DOTween.To(
                () => displayedValue,
                value =>
                {
                    displayedValue = value;
                    SetDisplayedValue(value);
                },
                targetValue,
                duration)
            .SetEase(countEase)
            .SetLink(gameObject, LinkBehaviour.KillOnDestroy)
            .OnComplete(() => CompleteCountUp(targetValue));
    }

    private void StartShake(float duration)
    {
        int vibrato = Mathf.Max(1, Mathf.RoundToInt(duration * shakeVibratoPerSecond));
        shakeTween = textRectTransform
            .DOShakeAnchorPos(duration, shakeStrength, vibrato, shakeRandomness, false, false, ShakeRandomnessMode.Full)
            .SetEase(Ease.Linear)
            .SetLink(gameObject, LinkBehaviour.KillOnDestroy);
    }

    private void CompleteCountUp(int targetValue)
    {
        countTween = null;
        SetDisplayedValue(targetValue);

        if (shakeTween != null && shakeTween.IsActive())
        {
            shakeTween.Kill();
        }

        shakeTween = null;
        textRectTransform.anchoredPosition = baseAnchoredPosition;
        StopLoopSound();

        if (reachedAudioSource != null && reachedClip != null)
        {
            reachedAudioSource.PlayOneShot(reachedClip);
        }

        punchTween = textRectTransform
            .DOPunchScale(Vector3.one * punchScaleAmount, punchDuration, punchVibrato, 0.5f)
            .SetLink(gameObject, LinkBehaviour.KillOnDestroy);

        colorTween = valueText
            .DOColor(highlightColor, colorChangeDuration)
            .SetEase(Ease.OutQuad)
            .SetLink(gameObject, LinkBehaviour.KillOnDestroy);
    }

    private void PlayLoopSound()
    {
        if (countLoopAudioSource == null || countLoopClip == null)
        {
            return;
        }

        countLoopAudioSource.Stop();
        countLoopAudioSource.clip = countLoopClip;
        countLoopAudioSource.loop = true;
        countLoopAudioSource.Play();
    }

    private void SetDisplayedValue(int value)
    {
        valueText.text = value.ToString("N0", CultureInfo.InvariantCulture);
    }

    private void StopLoopSound()
    {
        if (countLoopAudioSource != null)
        {
            countLoopAudioSource.Stop();
        }
    }

    private void CacheBaseVisualState()
    {
        baseAnchoredPosition = textRectTransform.anchoredPosition;
        baseScale = textRectTransform.localScale;
        baseColor = valueText.color;
    }

    private void RestoreBaseVisualState()
    {
        textRectTransform.anchoredPosition = baseAnchoredPosition;
        textRectTransform.localScale = baseScale;
        valueText.color = baseColor;
    }

    private void StopCurrentAnimation()
    {
        KillTween(ref countTween);
        KillTween(ref shakeTween);
        KillTween(ref punchTween);
        KillTween(ref colorTween);
        StopLoopSound();
    }

    private static void KillTween(ref Tween tween)
    {
        if (tween != null && tween.IsActive())
        {
            tween.Kill();
        }

        tween = null;
    }

    private void OnDisable()
    {
        StopCurrentAnimation();

        if (valueText != null && textRectTransform != null)
        {
            RestoreBaseVisualState();
        }
    }
}
