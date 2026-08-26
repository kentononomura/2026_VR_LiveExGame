using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    public int score = 0;
    public int combo = 0;
    public int maxCombo = 0;

    [Header("UI References")]
    public Text scoreText;
    public Text comboText;
    public Text feedbackText;
    public Text countdownText;

    [Header("UnityChan Integration")]
    public UnityChanReaction unityChanReaction;

    [Header("Result UI")]
    public GameObject resultPanel;
    public Text resultScoreText;
    public Button restartButton;

    [Header("Audio Settings")]
    [Tooltip("ここに自作の楽曲ファイル（mp3, wavなど）をドラッグ＆ドロップしてください")]
    public AudioClip bgmClip;
    public AudioSource bgmSource;
    public AudioSource sfxSource;
    private AudioClip beepClip;
    private AudioClip highBeepClip;

    [HideInInspector] public int activeNoteCount = 0;
    [HideInInspector] public bool isSpawningFinished = false;
    [HideInInspector] public bool isGamePlaying = false;

    private Coroutine feedbackCoroutine;

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
            
        CreateBeepSound();
    }

    private void Start()
    {
        UpdateUI();
        if (feedbackText != null)
        {
            feedbackText.text = "";
        }
        if (resultPanel != null)
        {
            resultPanel.SetActive(false);
        }
        if (restartButton != null)
        {
            restartButton.onClick.AddListener(RestartGame);
        }
        
        // シーン内のUnityちゃんを自動で検索して登録（セットアップによるリセット対策）
        if (unityChanReaction == null)
        {
            unityChanReaction = FindAnyObjectByType<UnityChanReaction>();
            if (unityChanReaction != null)
            {
                Debug.Log("GameManager: シーン内の UnityChanReaction を自動で登録しました。");
            }
        }
        
        // BGMが設定されていればAudioSourceにセットする
        if (bgmSource != null && bgmClip != null)
        {
            bgmSource.clip = bgmClip;
        }

        StartCoroutine(StartCountdownAndPlay());
    }

    private IEnumerator StartCountdownAndPlay()
    {
        if (countdownText != null)
        {
            countdownText.gameObject.SetActive(true);
            
            for (int i = 3; i > 0; i--)
            {
                countdownText.text = i.ToString();
                // 透明度を1に戻してから、1秒かけて0にフェードアウト
                countdownText.canvasRenderer.SetAlpha(1.0f);
                countdownText.CrossFadeAlpha(0.0f, 1.0f, false);
                
                PlayBeep(false); 
                
                yield return new WaitForSeconds(1.0f);
            }

            countdownText.text = "START!";
            countdownText.canvasRenderer.SetAlpha(1.0f);
            countdownText.CrossFadeAlpha(0.0f, 1.0f, false);
            PlayBeep(true); 
            
            yield return new WaitForSeconds(1.0f);
            countdownText.gameObject.SetActive(false);
        }

        // ゲーム開始
        isGamePlaying = true;
        
        if (bgmSource != null && bgmSource.clip != null)
        {
            bgmSource.Play();
        }
    }

    public void AddActiveNote()
    {
        activeNoteCount++;
    }

    public void RemoveActiveNote()
    {
        activeNoteCount--;
        CheckGameOver();
    }

    public void CheckGameOver()
    {
        if (isSpawningFinished && activeNoteCount <= 0)
        {
            ShowResultScreen();
        }
    }

    public void ForceGameOver()
    {
        ShowResultScreen();
    }

    private void ShowResultScreen()
    {
        if (resultPanel != null)
        {
            resultPanel.SetActive(true);
            if (resultScoreText != null)
            {
                resultScoreText.text = "Final Score\n" + score;
            }
        }
    }

    public void RestartGame()
    {
        VRScreenFader.Instance.LoadSceneWithFade(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
            0.5f);
    }

    public void AddScore(int points, string feedback)
    {
        score += points;
        combo++;
        if (combo > maxCombo) maxCombo = combo;

        ShowFeedback(feedback);
        UpdateUI();
        PlayBeep(false);

        if (unityChanReaction != null)
        {
            unityChanReaction.PlayReaction(feedback);
        }
    }

    public void Miss()
    {
        combo = 0;
        ShowFeedback("Miss");
        UpdateUI();

        if (unityChanReaction != null)
        {
            unityChanReaction.PlayReaction("Miss");
        }
    }

    private void UpdateUI()
    {
        if (scoreText != null) scoreText.text = "Score: " + score;
        if (comboText != null) comboText.text = "Combo: " + combo;
    }

    private void ShowFeedback(string text)
    {
        if (feedbackText != null)
        {
            feedbackText.text = text;
            if (feedbackCoroutine != null) StopCoroutine(feedbackCoroutine);
            feedbackCoroutine = StartCoroutine(FadeFeedback());
        }
    }

    private IEnumerator FadeFeedback()
    {
        // 以前のアニメーションをリセットし、不透明度を100%にする（CrossFadeAlphaのバグ対策）
        Color c = feedbackText.color;
        c.a = 1f;
        feedbackText.color = c;
        
        yield return new WaitForSeconds(0.5f);

        // 手動で確実にフェードアウトさせる
        float fadeDuration = 0.5f;
        float timer = 0f;
        while (timer < fadeDuration)
        {
            timer += Time.deltaTime;
            c.a = Mathf.Lerp(1f, 0f, timer / fadeDuration);
            feedbackText.color = c;
            yield return null;
        }
        
        c.a = 0f;
        feedbackText.color = c;
    }

    private void CreateBeepSound()
    {
        int sampleRate = 44100;
        float duration = 0.1f;
        
        beepClip = CreateTone(440f, duration, sampleRate); // 通常音
        highBeepClip = CreateTone(880f, duration, sampleRate); // 高い音（スタート用）
        
        if (sfxSource == null)
        {
            sfxSource = gameObject.AddComponent<AudioSource>();
        }
    }

    private AudioClip CreateTone(float frequency, float duration, int sampleRate)
    {
        AudioClip clip = AudioClip.Create("Tone", (int)(sampleRate * duration), 1, sampleRate, false);
        float[] samples = new float[clip.samples];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = Mathf.Sin(2 * Mathf.PI * frequency * i / sampleRate);
            samples[i] *= 1.0f - ((float)i / samples.Length); // フェードアウト
        }
        clip.SetData(samples, 0);
        return clip;
    }

    private void PlayBeep(bool isHigh)
    {
        if (sfxSource != null)
        {
            sfxSource.PlayOneShot(isHigh ? highBeepClip : beepClip, 0.5f);
        }
    }
}
