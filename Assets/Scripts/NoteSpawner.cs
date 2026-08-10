using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum NoteType
{
    Normal,
    Long
}

[System.Serializable]
public class NoteData
{
    [Tooltip("ノーツが判定ラインに到達する時間（秒）")]
    public float spawnTime;
    
    [Tooltip("レーン番号 (0, 1, 2, 3)")]
    public int laneIndex;

    [Tooltip("ノーツの種類")]
    public NoteType type = NoteType.Normal;

    [Tooltip("ロングノーツの場合の長さ（秒）。Normalの場合は無視されます")]
    public float duration = 0f;
    
    [Tooltip("ロングノーツの終了レーン番号。-1または始点と同じであれば直線になります")]
    public int endLaneIndex = -1;
    
    [HideInInspector] public bool isSimultaneous = false;
}

public class NoteSpawner : MonoBehaviour
{
    public GameObject notePrefab;
    public float noteSpeed = 5f;

    [Header("VR Spatial Settings (生成位置の調整)")]
    [Tooltip("4つのレーンの位置を表すオブジェクトをここに割り当ててください (要素0〜3)")]
    public Transform[] laneTransforms;

    [Tooltip("ノーツが生成される奥の距離（Z座標）")]
    public float spawnZ = 20f; 
    [Tooltip("プレイヤーがノーツを叩く位置（Z座標）")]
    public float hitZoneZ = 0f;
    [Tooltip("ノーツの大きさ（横幅、高さ、奥行き）")]
    public Vector3 noteScale = new Vector3(0.5f, 0.5f, 0.5f);
    [Tooltip("ノーツ全体の高さ（Y座標）。胸〜腰の高さに調整してください (レーンオブジェクトが未設定時のフォールバック値)")]
    public float notesHeightY = 1.0f;
    [Tooltip("ノーツのレーン高さをプレイヤーの目の高さ（カメラ位置）に合わせて自動調整するかどうか")]
    public bool autoAlignHeightToCamera = true;
    [Tooltip("プレイヤーの目の高さ（カメラ位置）からのオフセット。マイナス値で目線より低く（胸〜腰の高さ）にします")]
    public float heightOffsetFromCamera = -0.4f;
    [Tooltip("レーン同士の間隔（横幅）。(レーンオブジェクトが未設定時のフォールバック値)")]
    public float laneSpacing = 0.6f;
    [Tooltip("全体の左右のズレ（X座標）。(レーンオブジェクトが未設定時のフォールバック値)")]
    public float offsetX = 0f;

    [Header("Hit Detection (判定の甘さ調整)")]
    public float perfectThreshold = 0.4f;
    public float greatThreshold = 0.8f;
    public float goodThreshold = 1.2f;

    [Header("Game Over Settings")]
    [Tooltip("GameManagerに設定されたBGMの長さに合わせて、自動的にゲーム終了時間を設定するかどうか")]
    public bool autoSyncWithBGM = true;

    [Tooltip("曲の終了時間（秒）。autoSyncWithBGMがオフの場合やBGMがない場合に使用されます。0の場合は最後のノーツを叩き終わった時点で終了します。")]
    public float customEndTime = 0f;

    public Material normalNoteMat;
    public Material simultaneousNoteMat;

    [Header("Beatmap / 譜面")]
    public List<NoteData> beatmap;

    private float timer = 0f;
    private int nextNoteIndex = 0;
    private float fallTime;

    private void Start()
    {
        // プレイヤーの目の高さに合わせてノーツの生成高さを自動設定する
        if (autoAlignHeightToCamera)
        {
            Camera mainCam = Camera.main;
            if (mainCam != null)
            {
                // 親（RhythmTrack等）のローカル座標系でカメラの高さを取得
                Vector3 localCamPos = transform.parent != null 
                    ? transform.parent.InverseTransformPoint(mainCam.transform.position) 
                    : mainCam.transform.position;
                
                notesHeightY = localCamPos.y + heightOffsetFromCamera;
                Debug.Log($"NoteSpawner: ノーツ高さをプレイヤーのカメラ（Y: {mainCam.transform.position.y:F2}）に合わせて {notesHeightY:F2} に自動調整しました。");
            }
        }

        fallTime = (spawnZ - hitZoneZ) / noteSpeed;
        
        // --- BGMの長さに自動で合わせる機能 ---
        if (autoSyncWithBGM && GameManager.Instance != null && GameManager.Instance.bgmClip != null)
        {
            // 曲が終わってからリザルト画面が出るまでに1秒の余韻を持たせる
            customEndTime = GameManager.Instance.bgmClip.length + 1.0f;
            Debug.Log($"自動設定: ゲーム終了時間をBGMの長さ ({customEndTime:F1}秒) に自動設定しました。");
        }
        
        if (beatmap != null)
        {
            beatmap.Sort((a, b) => a.spawnTime.CompareTo(b.spawnTime));
            
            // 同時押し判定のフラグ付け
            for (int i = 0; i < beatmap.Count; i++)
            {
                for (int j = i + 1; j < beatmap.Count; j++)
                {
                    if (Mathf.Abs(beatmap[i].spawnTime - beatmap[j].spawnTime) < 0.01f)
                    {
                        beatmap[i].isSimultaneous = true;
                        beatmap[j].isSimultaneous = true;
                    }
                    else
                    {
                        break; 
                    }
                }
            }
        }
    }

    private void Update()
    {
        if (GameManager.Instance != null && !GameManager.Instance.isGamePlaying)
            return; // カウントダウンが終わるまで待機

        timer += Time.deltaTime;

        while (beatmap != null && nextNoteIndex < beatmap.Count)
        {
            NoteData nextNote = beatmap[nextNoteIndex];
            
            float exactSpawnTime = nextNote.spawnTime - fallTime;
            if (timer >= exactSpawnTime)
            {
                // フレーム間の時間ズレ（オーバーシュート）を計算
                float overshoot = timer - exactSpawnTime;
                SpawnNote(nextNote, overshoot);
                nextNoteIndex++;
            }
            else
            {
                break;
            }
        }

        // すべてのノーツを生成し終えたかチェック
        if (beatmap != null && nextNoteIndex >= beatmap.Count)
        {
            if (customEndTime > 0f)
            {
                if (timer >= customEndTime && GameManager.Instance != null && !GameManager.Instance.isSpawningFinished)
                {
                    GameManager.Instance.isSpawningFinished = true;
                    GameManager.Instance.ForceGameOver();
                }
            }
            else
            {
                if (GameManager.Instance != null && !GameManager.Instance.isSpawningFinished)
                {
                    GameManager.Instance.isSpawningFinished = true;
                    GameManager.Instance.CheckGameOver();
                }
            }
        }
    }

    private void SpawnNote(NoteData data, float overshoot)
    {
        // ユーザーが設定した値を使ってX, Y座標を決定
        float xPos = offsetX + (data.laneIndex - 1.5f) * laneSpacing; 
        float yPos = notesHeightY;
        
        Transform laneTrans = null;
        if (laneTransforms != null && data.laneIndex >= 0 && data.laneIndex < laneTransforms.Length)
        {
            laneTrans = laneTransforms[data.laneIndex];
        }

        Vector3 spawnPos;
        Quaternion spawnRot;
        Transform parent;

        if (laneTrans != null)
        {
            // レーンオブジェクトが指定されている場合は、そのレーンを親にする
            parent = laneTrans;
            float correctedSpawnZ = spawnZ - (overshoot * noteSpeed);
            // レーンの前方（ローカルZ軸）に向けて生成位置をワールド座標に変換
            spawnPos = laneTrans.TransformPoint(new Vector3(0f, 0f, correctedSpawnZ));
            spawnRot = laneTrans.rotation;
        }
        else
        {
            // 設定されていない場合は従来のフォールバック計算
            parent = transform;
            float correctedSpawnZ = spawnZ - (overshoot * noteSpeed);
            spawnPos = transform.TransformPoint(new Vector3(xPos, yPos, correctedSpawnZ));
            spawnRot = transform.rotation;
        }
        
        GameObject noteObj = Instantiate(notePrefab, spawnPos, spawnRot, parent);
        
        // 生成直後のローカル位置・回転をレーン基準でリセット（Z軸だけ補正）
        if (laneTrans != null)
        {
            float correctedSpawnZ = spawnZ - (overshoot * noteSpeed);
            noteObj.transform.localPosition = new Vector3(0f, 0f, correctedSpawnZ);
            noteObj.transform.localRotation = Quaternion.identity;
        }
        
        noteObj.SetActive(true); 

        Note note = noteObj.GetComponent<Note>();
        note.Initialize(data, noteSpeed, normalNoteMat, simultaneousNoteMat, this);
    }

    // シーンビューに生成位置と判定ラインを視覚的に表示する機能（Gizmos）
    private void OnDrawGizmos()
    {
        // 4つのレーンを描画
        for (int i = 0; i < 4; i++)
        {
            Transform laneTrans = null;
            if (laneTransforms != null && i < laneTransforms.Length)
            {
                laneTrans = laneTransforms[i];
            }

            Vector3 spawnPos;
            Vector3 hitPos;

            if (laneTrans != null)
            {
                spawnPos = laneTrans.TransformPoint(new Vector3(0f, 0f, spawnZ));
                hitPos = laneTrans.TransformPoint(new Vector3(0f, 0f, hitZoneZ));
            }
            else
            {
                float xPos = offsetX + (i - 1.5f) * laneSpacing;
                spawnPos = transform.position + new Vector3(xPos, notesHeightY, spawnZ);
                hitPos = transform.position + new Vector3(xPos, notesHeightY, hitZoneZ);
            }

            // 生成位置（奥）を青色の球で表示
            Gizmos.color = new Color(0, 0, 1, 0.5f);
            Gizmos.DrawSphere(spawnPos, 0.1f);

            // 判定位置（手前）を赤色の球で表示
            Gizmos.color = new Color(1, 0, 0, 0.5f);
            Gizmos.DrawSphere(hitPos, 0.15f);

            // 生成位置から判定位置へのラインを引く
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(spawnPos, hitPos);
        }
    }
}
