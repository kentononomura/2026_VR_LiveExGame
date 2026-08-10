#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class RhythmGameSetup : EditorWindow
{
    [MenuItem("RhythmGame/Setup Scene")]
    public static void SetupScene()
    {
        // --- 1. 自動クリーンアップ（二重生成・二重UI防止） ---
        // 既存のオブジェクトがあれば全自動で削除してから再生成するため、手動で削除する手間が省けます
        string[] oldObjects = { "RhythmTrack", "GameManager", "Canvas", "EventSystem", "NotePrefab" };
        foreach (string objName in oldObjects)
        {
            GameObject oldObj = GameObject.Find(objName);
            if (oldObj != null)
            {
                DestroyImmediate(oldObj);
            }
        }

        // --- 2. Setup Camera ---
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            mainCam.orthographic = true;
            mainCam.orthographicSize = 5;
            mainCam.transform.position = new Vector3(0, 3, -10);
            mainCam.backgroundColor = new Color(0.1f, 0.1f, 0.15f);
        }

        // --- 3. GameManager setup ---
        GameObject gameManagerObj = new GameObject("GameManager");
        GameManager gm = gameManagerObj.AddComponent<GameManager>();
        gameManagerObj.AddComponent<AudioSource>(); 
        gm.bgmSource = gameManagerObj.GetComponent<AudioSource>();

        // --- 4. NoteSpawner setup ---
        GameObject spawnerObj = new GameObject("NoteSpawner");
        NoteSpawner spawner = spawnerObj.AddComponent<NoteSpawner>();
        
        // --- 5. Setup Prefab for Note ---
        GameObject notePrefab = new GameObject("NotePrefab");
        Note noteScript = notePrefab.AddComponent<Note>();
        
        Rigidbody rb = notePrefab.AddComponent<Rigidbody>();
        rb.useGravity = false;
        rb.isKinematic = true;

        GameObject visualObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visualObj.name = "Visual";
        visualObj.transform.SetParent(notePrefab.transform);
        visualObj.transform.localPosition = Vector3.zero;
        visualObj.transform.localScale = new Vector3(1f, 0.5f, 1f);
        
        Collider visualCol = visualObj.GetComponent<Collider>();
        visualCol.isTrigger = true;

        Material noteMat = new Material(Shader.Find("Sprites/Default"));
        noteMat.color = Color.cyan;
        visualObj.GetComponent<Renderer>().sharedMaterial = noteMat;

        Material simMat = new Material(Shader.Find("Sprites/Default"));
        simMat.color = Color.yellow;

        noteScript.visual = visualObj.transform;
        spawner.notePrefab = notePrefab;
        spawner.normalNoteMat = noteMat;
        spawner.simultaneousNoteMat = simMat;

        notePrefab.SetActive(false);

        // --- 6. Setup Lanes ---
        GameObject lanesParent = new GameObject("Lanes");
#if ENABLE_INPUT_SYSTEM
        Key[] keys = { Key.D, Key.F, Key.J, Key.K };
#else
        KeyCode[] keys = { KeyCode.D, KeyCode.F, KeyCode.J, KeyCode.K };
#endif
        for (int i = 0; i < 4; i++)
        {
            GameObject laneObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
            laneObj.name = "Lane_" + i;
            laneObj.transform.SetParent(lanesParent.transform);
            
            float xPos = (i - 1.5f) * 1.2f; 
            laneObj.transform.localPosition = new Vector3(xPos, 0f, 0f);
            // 横幅を1.2fに広げ、隣のレーンと隙間なく連結させて太い1本線にする。厚みも0.3fに変更。
            laneObj.transform.localScale = new Vector3(1.2f, 0.3f, 1f);

            Material laneMat = new Material(Shader.Find("Sprites/Default"));
            laneMat.color = new Color(1f, 1f, 1f, 0.3f);
            laneObj.GetComponent<Renderer>().sharedMaterial = laneMat;

            BoxCollider col = laneObj.GetComponent<BoxCollider>();
            if (col == null) col = laneObj.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = new Vector3(1f, 5f, 1f); 

            Lane laneScript = laneObj.AddComponent<Lane>();
            laneScript.laneIndex = i;
            laneScript.inputKey = keys[i];
        }

        // --- 7. Setup Lane Dividers ---
        GameObject dividersParent = new GameObject("LaneDividers");
        Material dividerMat = new Material(Shader.Find("Sprites/Default"));
        dividerMat.color = new Color(1f, 1f, 1f, 0.3f);

        for (int i = 0; i <= 4; i++)
        {
            GameObject dividerObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
            dividerObj.name = "Divider_" + i;
            dividerObj.transform.SetParent(dividersParent.transform);
            
            float xPos = (i - 2f) * 1.2f; 
            dividerObj.transform.localPosition = new Vector3(xPos, 3f, 0.5f); 
            dividerObj.transform.localScale = new Vector3(0.05f, 15f, 1f);
            
            DestroyImmediate(dividerObj.GetComponent<Collider>());
            dividerObj.GetComponent<Renderer>().sharedMaterial = dividerMat;
        }

        // --- 8. プレイエリアの統合 (RhythmTrack) ---
        GameObject trackObj = new GameObject("RhythmTrack");
        lanesParent.transform.SetParent(trackObj.transform);
        dividersParent.transform.SetParent(trackObj.transform);
        spawnerObj.transform.SetParent(trackObj.transform);

        // --- 9. Setup UI ---
        GameObject canvasObj = new GameObject("Canvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasObj.AddComponent<CanvasScaler>();
        canvasObj.AddComponent<GraphicRaycaster>();

        if (FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            GameObject eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
#if ENABLE_INPUT_SYSTEM
            eventSystem.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            eventSystem.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
#endif
        }

        Text scoreText = CreateText(canvasObj.transform, "ScoreText", new Vector2(-150, -50), "Score: 0", TextAnchor.UpperLeft);
        Text comboText = CreateText(canvasObj.transform, "ComboText", new Vector2(150, -50), "Combo: 0", TextAnchor.UpperRight);
        Text feedbackText = CreateText(canvasObj.transform, "FeedbackText", new Vector2(0, 0), "", TextAnchor.MiddleCenter);
        feedbackText.fontSize = 40;

        Text countdownText = CreateText(canvasObj.transform, "CountdownText", Vector2.zero, "", TextAnchor.MiddleCenter);
        countdownText.fontSize = 150;
        countdownText.fontStyle = FontStyle.Bold;
        countdownText.color = Color.yellow;
        countdownText.GetComponent<RectTransform>().sizeDelta = new Vector2(600, 600);

        gm.scoreText = scoreText;
        gm.comboText = comboText;
        gm.feedbackText = feedbackText;
        gm.countdownText = countdownText;

        // --- 10. Result Panel Setup ---
        GameObject resultPanelObj = new GameObject("ResultPanel");
        resultPanelObj.transform.SetParent(canvasObj.transform);
        Image panelImage = resultPanelObj.AddComponent<Image>();
        panelImage.color = new Color(0, 0, 0, 0.8f);
        RectTransform panelRt = resultPanelObj.GetComponent<RectTransform>();
        panelRt.anchorMin = Vector2.zero;
        panelRt.anchorMax = Vector2.one;
        panelRt.anchoredPosition = Vector2.zero;
        panelRt.sizeDelta = Vector2.zero;

        Text resultScoreText = CreateText(resultPanelObj.transform, "ResultScoreText", new Vector2(0, 100), "Final Score\n0", TextAnchor.MiddleCenter);
        resultScoreText.fontSize = 60;
        resultScoreText.GetComponent<RectTransform>().sizeDelta = new Vector2(600, 200);

        GameObject buttonObj = new GameObject("RestartButton");
        buttonObj.transform.SetParent(resultPanelObj.transform);
        Image btnImage = buttonObj.AddComponent<Image>();
        btnImage.color = new Color(0.2f, 0.6f, 1f); // 青いボタン
        Button btn = buttonObj.AddComponent<Button>();
        RectTransform btnRt = buttonObj.GetComponent<RectTransform>();
        btnRt.anchorMin = new Vector2(0.5f, 0.5f);
        btnRt.anchorMax = new Vector2(0.5f, 0.5f);
        btnRt.anchoredPosition = new Vector2(0, -100);
        btnRt.sizeDelta = new Vector2(250, 80);

        Text btnText = CreateText(buttonObj.transform, "ButtonText", Vector2.zero, "Restart", TextAnchor.MiddleCenter);
        btnText.color = Color.white;
        btnText.fontSize = 40;

        gm.resultPanel = resultPanelObj;
        gm.resultScoreText = resultScoreText;
        gm.restartButton = btn;

        resultPanelObj.SetActive(false);

        // --- 11. Beatmap Setup ---
        spawner.beatmap = new System.Collections.Generic.List<NoteData>();
        
        spawner.beatmap.Add(new NoteData { spawnTime = 2.0f, laneIndex = 0, type = NoteType.Normal });
        spawner.beatmap.Add(new NoteData { spawnTime = 2.5f, laneIndex = 1, type = NoteType.Normal });
        
        spawner.beatmap.Add(new NoteData { spawnTime = 3.5f, laneIndex = 0, type = NoteType.Normal });
        spawner.beatmap.Add(new NoteData { spawnTime = 3.5f, laneIndex = 3, type = NoteType.Normal });
        
        spawner.beatmap.Add(new NoteData { spawnTime = 5.0f, laneIndex = 2, type = NoteType.Long, duration = 1.0f });

        spawner.beatmap.Add(new NoteData { spawnTime = 7.0f, laneIndex = 1, type = NoteType.Long, duration = 1.5f });
        spawner.beatmap.Add(new NoteData { spawnTime = 7.0f, laneIndex = 2, type = NoteType.Normal });

        Debug.Log("Rhythm Game Scene Setup Complete!");
    }

    private static Text CreateText(Transform parent, string name, Vector2 anchoredPos, string defaultText, TextAnchor alignment)
    {
        GameObject textObj = new GameObject(name);
        textObj.transform.SetParent(parent);
        Text text = textObj.AddComponent<Text>();
        text.text = defaultText;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 24;
        text.color = Color.white;
        text.alignment = alignment;

        RectTransform rt = textObj.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        if (alignment == TextAnchor.MiddleCenter)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
        }
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = new Vector2(300, 50);

        return text;
    }

    [MenuItem("RhythmGame/Upgrade to RhythmTrack")]
    public static void UpgradeToRhythmTrack()
    {
        if (GameObject.Find("RhythmTrack") != null)
        {
            Debug.LogWarning("既に RhythmTrack が存在します。");
            return;
        }

        GameObject track = new GameObject("RhythmTrack");
        
        GameObject lanes = GameObject.Find("Lanes");
        GameObject dividers = GameObject.Find("LaneDividers");
        GameObject spawner = GameObject.Find("NoteSpawner");
        
        if (lanes != null) lanes.transform.SetParent(track.transform);
        if (dividers != null) dividers.transform.SetParent(track.transform);
        if (spawner != null) spawner.transform.SetParent(track.transform);
        
        Debug.Log("RhythmTrackへの移行が完了しました！このRhythmTrackオブジェクトを移動・回転・縮小させてみてください。");
    }

    [MenuItem("RhythmGame/Setup 3D Layout & Fix Alignment")]
    public static void Setup3DLayoutAndFixAlignment()
    {
        GameObject track = GameObject.Find("RhythmTrack");
        if (track == null)
        {
            // なければまず自動移行を行う
            UpgradeToRhythmTrack();
            track = GameObject.Find("RhythmTrack");
        }

        if (track == null)
        {
            Debug.LogError("RhythmTrack オブジェクトが見つかりません。Setup Sceneを先に実行してください。");
            return;
        }

        // 1. 各種オブジェクトの位置合わせを完全に修正する（リセット）
        GameObject lanes = GameObject.Find("Lanes");
        GameObject dividers = GameObject.Find("LaneDividers");
        GameObject spawner = GameObject.Find("NoteSpawner");

        // カメラとトラックをまとめてUndoに登録
        Undo.RecordObjects(new UnityEngine.Object[] { track.transform, Camera.main.transform }, "Setup 3D Layout");

        if (lanes != null)
        {
            Undo.RecordObject(lanes.transform, "Align Lanes");
            lanes.transform.localPosition = Vector3.zero;
            lanes.transform.localRotation = Quaternion.identity;
            lanes.transform.localScale = Vector3.one;

            // 3Dレイアウトに合わせて、各レーンの大きさをぴったりフィットさせる
            // 隙間を無くすために横幅を1.2fにし、厚みを0.3fにします。
            for (int i = 0; i < lanes.transform.childCount; i++)
            {
                Transform laneChild = lanes.transform.GetChild(i);
                laneChild.localScale = new Vector3(1.2f, 0.3f, 1f);
            }
        }

        if (dividers != null)
        {
            Undo.RecordObject(dividers.transform, "Align Dividers");
            dividers.transform.localPosition = Vector3.zero;
            dividers.transform.localRotation = Quaternion.identity;
            dividers.transform.localScale = Vector3.one;
        }

        if (spawner != null)
        {
            Undo.RecordObject(spawner.transform, "Align Spawner");
            spawner.transform.localPosition = Vector3.zero;
            spawner.transform.localRotation = Quaternion.identity;
            spawner.transform.localScale = Vector3.one;
        }

        // 2. RhythmTrackを傾けて3Dのコースにする
        // X軸を70度回転させると、奥から手前に滑る傾斜になります
        track.transform.position = Vector3.zero;
        track.transform.rotation = Quaternion.Euler(70f, 0f, 0f);
        track.transform.localScale = Vector3.one;

        // 3. カメラを3Dリズムゲームに最適な位置に配置する
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            Undo.RecordObject(mainCam.transform, "Align Camera");
            mainCam.orthographic = false; // パース（遠近感）を有効に
            
            // プレイエリアを斜め上から見下ろす位置に配置
            mainCam.transform.position = new Vector3(0f, 1.5f, -4.2f);
            mainCam.transform.rotation = Quaternion.Euler(20f, 0f, 0f);
            
            // 視野角（Field of View）を少し広げて奥行き感を出す
            mainCam.fieldOfView = 65f;
            
            Debug.Log("3Dレイアウトの設定と位置ズレの修正が完了しました！");
        }
        else
        {
            Debug.LogWarning("メインカメラ（Main Camera）が見つかりませんでした。カメラの位置は手動で調整してください。");
        }
    }
}
#endif
