using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class TitleStartCube : MonoBehaviour
{
    private TitleVoiceManager voiceManager;

    [Tooltip("VoiceManagerが無い場合の遷移先シーン")]
    public string targetSceneName = "TestScene";

    void Start()
    {
        // シーン内の TitleVoiceManager を探しておく
        voiceManager = FindAnyObjectByType<TitleVoiceManager>();
    }

    private void OnTriggerEnter(Collider other)
    {
        // 衝突したオブジェクトに Saber コンポーネントがあるか確認
        Saber saber = other.GetComponent<Saber>();
        if (saber != null)
        {
            Debug.Log($"[TitleStartCube] Saber '{other.gameObject.name}' による接触を検知。ゲームを開始します！");
            
            if (voiceManager != null)
            {
                voiceManager.StartGameScene();
            }
            else
            {
                VRScreenFader.Instance.LoadSceneWithFade(targetSceneName, 1.0f);
            }
        }
    }
}
