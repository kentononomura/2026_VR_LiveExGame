using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class BeatmapEditorWindow : EditorWindow
{
    private NoteSpawner spawner;
    private Vector2 scrollPos;
    
    // ドラッグ＆ドロップ移動用の変数
    private NoteData draggedNote = null;
    private bool isDragging = false;
    
    // テールドラッグ用（斜めロングノーツ＆長さ変更）の変数
    private NoteData draggedTailNote = null;
    private bool isDraggingTail = false;
    
    // 見た目の設定
    private float pixelsPerSecond = 100f; // 1秒あたりのピクセル数(Y軸の拡大率)
    private float laneWidth = 80f;
    private float timeOffset = 50f; // 左側の時間表示の幅
    private float totalHeight = 3000f; // スクロール領域の最大高さ(後で動的計算)
    private float noteHeight = 20f; // 通常ノーツの描画高さ

    public static void ShowWindow(NoteSpawner targetSpawner)
    {
        BeatmapEditorWindow window = GetWindow<BeatmapEditorWindow>("Beatmap Editor");
        window.spawner = targetSpawner;
        window.Show();
    }

    private void OnGUI()
    {
        if (spawner == null)
        {
            EditorGUILayout.HelpBox("NoteSpawner が選択されていません。", MessageType.Warning);
            if (GUILayout.Button("シーンからNoteSpawnerを探す"))
            {
                spawner = FindAnyObjectByType<NoteSpawner>();
            }
            return;
        }

        EditorGUILayout.LabelField("Beatmap Timeline Editor", EditorStyles.boldLabel);
        
        // ヘッダーUI（拡大縮小など）
        EditorGUILayout.BeginHorizontal();
        pixelsPerSecond = EditorGUILayout.Slider("ズーム (Pixels/Sec)", pixelsPerSecond, 20f, 300f);
        if (GUILayout.Button("ソート", GUILayout.Width(60)))
        {
            Undo.RecordObject(spawner, "Sort Beatmap");
            spawner.beatmap.Sort((a, b) => a.spawnTime.CompareTo(b.spawnTime));
            EditorUtility.SetDirty(spawner);
        }
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.HelpBox("左クリック: 追加 | 右クリック: 削除 | 中クリック: Normal/Long切替", MessageType.Info);

        // 譜面の最大時間を計算してスクロール領域を設定
        float maxTime = 15f; // デフォルトの最低保証時間
        
        // 1. GameManagerに設定されたBGMの長さがあればそれを基準にする
        GameManager gm = Object.FindAnyObjectByType<GameManager>();
        if (gm != null && gm.bgmClip != null)
        {
            maxTime = gm.bgmClip.length;
        }

        // 2. もし曲より後ろにノーツがある場合はそこまで拡張する
        if (spawner.beatmap != null && spawner.beatmap.Count > 0)
        {
            foreach (var note in spawner.beatmap)
            {
                float end = note.type == NoteType.Long ? note.spawnTime + note.duration : note.spawnTime;
                if (end > maxTime) maxTime = end;
            }
        }
        
        // 最後に数秒の余裕（余白）を持たせる
        totalHeight = (maxTime + 5f) * pixelsPerSecond;

        // レーンのヘッダー描画
        Rect headerRect = EditorGUILayout.GetControlRect(false, 20);
        for (int i = 0; i < 4; i++)
        {
            Rect laneHeaderRect = new Rect(headerRect.x + timeOffset + i * laneWidth, headerRect.y, laneWidth, 20);
            GUI.Box(laneHeaderRect, "Lane " + i);
        }

        // スクロールビュー開始
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
        
        // 全体の背景領域 (高さを確保)
        Rect timelineArea = GUILayoutUtility.GetRect(timeOffset + laneWidth * 4, totalHeight);
        
        // 背景とグリッドの描画
        DrawGrid(timelineArea);

        // ノーツの描画とクリック判定
        HandleEvents(timelineArea);
        DrawNotes(timelineArea);

        EditorGUILayout.EndScrollView();
    }

    private void DrawGrid(Rect area)
    {
        // 背景色
        EditorGUI.DrawRect(area, new Color(0.15f, 0.15f, 0.15f));

        // 縦線（レーンの区切り）
        for (int i = 0; i <= 4; i++)
        {
            Rect line = new Rect(area.x + timeOffset + i * laneWidth, area.y, 1, area.height);
            EditorGUI.DrawRect(line, Color.gray);
        }

        // 横線（1秒ごと、0.5秒ごと）
        float maxTime = area.height / pixelsPerSecond;
        for (float t = 0; t <= maxTime; t += 0.5f)
        {
            float yPos = area.y + area.height - (t * pixelsPerSecond); // 下から上へ時間が進む
            
            bool isSecond = (t % 1.0f == 0);
            Rect line = new Rect(area.x + timeOffset, yPos, laneWidth * 4, isSecond ? 2 : 1);
            EditorGUI.DrawRect(line, isSecond ? Color.gray : new Color(0.3f, 0.3f, 0.3f));

            if (isSecond)
            {
                GUI.Label(new Rect(area.x, yPos - 10, timeOffset, 20), t.ToString("0.0") + "s");
            }
        }
    }

    private void DrawNotes(Rect area)
    {
        if (spawner.beatmap == null) return;

        foreach (var note in spawner.beatmap)
        {
            float yPosBottom = area.y + area.height - (note.spawnTime * pixelsPerSecond);
            
            Color noteColor = Color.cyan;
            if (note.type == NoteType.Long) noteColor = new Color(0f, 0.8f, 1f); // 少し濃い水色
            
            // ドラッグ中のノーツは少し半透明にする
            if (note == draggedNote || note == draggedTailNote)
            {
                noteColor.a = 0.6f;
            }

            if (note.type == NoteType.Long)
            {
                // ロングノーツの描画（斜め含む）
                int targetEndLane = note.endLaneIndex != -1 ? note.endLaneIndex : note.laneIndex;
                float yPosTop = yPosBottom - (note.duration * pixelsPerSecond);
                
                // ヘッド（始点）とテール（終点）のボックス座標
                Rect headRect = new Rect(area.x + timeOffset + note.laneIndex * laneWidth + 5, yPosBottom - noteHeight, laneWidth - 10, noteHeight);
                Rect tailRect = new Rect(area.x + timeOffset + targetEndLane * laneWidth + 5, yPosTop, laneWidth - 10, noteHeight);
                
                // ボディ（なぞり帯）をヘッドの中心とテールの中心を結ぶラインとして描画
                Vector3 headCenter = new Vector3(headRect.x + headRect.width / 2f, headRect.y + headRect.height / 2f, 0f);
                Vector3 tailCenter = new Vector3(tailRect.x + tailRect.width / 2f, tailRect.y + tailRect.height / 2f, 0f);
                
                Handles.color = noteColor;
                Handles.DrawAAPolyLine(14f, headCenter, tailCenter); // 太さ14のラインで描画

                // ヘッドとテールのソリッドボックスを描画
                EditorGUI.DrawRect(headRect, noteColor);
                EditorGUI.DrawRect(tailRect, noteColor);

                // ドラッグ中の枠線強調
                if (note == draggedNote)
                {
                    Handles.DrawSolidRectangleWithOutline(headRect, new Color(0, 0, 0, 0), Color.white);
                }
                if (note == draggedTailNote)
                {
                    Handles.DrawSolidRectangleWithOutline(tailRect, new Color(0, 0, 0, 0), Color.white);
                }

                // テキスト表示
                GUIStyle style = new GUIStyle(EditorStyles.miniLabel);
                style.normal.textColor = Color.black;
                GUI.Label(headRect, note.spawnTime.ToString("0.00"), style);
                GUI.Label(tailRect, (note.spawnTime + note.duration).ToString("0.00"), style);
            }
            else
            {
                // 通常ノーツの描画
                Rect noteRect = new Rect(area.x + timeOffset + note.laneIndex * laneWidth + 5, yPosBottom - noteHeight, laneWidth - 10, noteHeight);
                
                EditorGUI.DrawRect(noteRect, noteColor);
                
                if (note == draggedNote)
                {
                    Handles.DrawSolidRectangleWithOutline(noteRect, new Color(0, 0, 0, 0), Color.white);
                }
                
                GUIStyle style = new GUIStyle(EditorStyles.miniLabel);
                style.normal.textColor = Color.black;
                GUI.Label(noteRect, note.spawnTime.ToString("0.00"), style);
            }
        }
    }

    private void HandleEvents(Rect area)
    {
        Event e = Event.current;
        Vector2 mousePos = e.mousePosition;

        // マウスポジションからレーンと時間を計算
        float mouseX = mousePos.x - (area.x + timeOffset);
        int lane = -1;
        if (mouseX >= 0 && mouseX <= laneWidth * 4)
        {
            lane = (int)(mouseX / laneWidth);
        }
        
        float yFromBottom = area.y + area.height - mousePos.y;
        float time = yFromBottom / pixelsPerSecond;
        // 0.05秒単位にスナップ
        time = Mathf.Round(time * 20f) / 20f;
        if (time < 0) time = 0;

        switch (e.type)
        {
            case EventType.MouseDown:
                if (!area.Contains(mousePos)) return;
                
                // クリックされた位置にある既存のノーツを検出
                NoteData clickedNote = null;
                bool clickedTail = false;

                for (int i = spawner.beatmap.Count - 1; i >= 0; i--)
                {
                    NoteData note = spawner.beatmap[i];
                    if (note.type == NoteType.Long)
                    {
                        // テール（終点）の判定
                        int targetEndLane = note.endLaneIndex != -1 ? note.endLaneIndex : note.laneIndex;
                        float yPosTop = area.y + area.height - ((note.spawnTime + note.duration) * pixelsPerSecond);
                        Rect tailRect = new Rect(area.x + timeOffset + targetEndLane * laneWidth + 5, yPosTop, laneWidth - 10, noteHeight);
                        
                        if (tailRect.Contains(mousePos))
                        {
                            clickedNote = note;
                            clickedTail = true;
                            break;
                        }
                    }

                    // ヘッド（始点）または通常ノーツの判定
                    float yPosBottom = area.y + area.height - (note.spawnTime * pixelsPerSecond);
                    Rect headRect = new Rect(area.x + timeOffset + note.laneIndex * laneWidth + 5, yPosBottom - noteHeight, laneWidth - 10, noteHeight);
                    
                    if (headRect.Contains(mousePos))
                    {
                        clickedNote = note;
                        clickedTail = false;
                        break;
                    }
                }

                if (e.button == 0) // 左クリック
                {
                    if (clickedNote != null)
                    {
                        if (clickedTail)
                        {
                            // ロングノーツのテールがクリックされた場合はテールドラッグ開始（長さ＆終了レーン調整）
                            draggedTailNote = clickedNote;
                            isDraggingTail = true;
                            Undo.RecordObject(spawner, "Resize/Bend Long Note");
                            e.Use();
                        }
                        else
                        {
                            // 通常ノーツかロングのヘッドがクリックされた場合はドラッグ移動開始
                            draggedNote = clickedNote;
                            isDragging = true;
                            Undo.RecordObject(spawner, "Move Note");
                            e.Use();
                        }
                    }
                    else
                    {
                        // 何もない場所なら新規ノーツ追加
                        if (lane != -1)
                        {
                            Undo.RecordObject(spawner, "Add Note via Editor");
                            spawner.beatmap.Add(new NoteData { spawnTime = time, laneIndex = lane, type = NoteType.Normal, duration = 1.0f });
                            EditorUtility.SetDirty(spawner);
                            e.Use();
                        }
                    }
                }
                else if (e.button == 1) // 右クリック (削除)
                {
                    if (clickedNote != null)
                    {
                        Undo.RecordObject(spawner, "Remove Note");
                        spawner.beatmap.Remove(clickedNote);
                        EditorUtility.SetDirty(spawner);
                        e.Use();
                    }
                }
                else if (e.button == 2) // 中クリック (タイプ切り替え)
                {
                    if (clickedNote != null)
                    {
                        Undo.RecordObject(spawner, "Toggle Note Type");
                        clickedNote.type = clickedNote.type == NoteType.Normal ? NoteType.Long : NoteType.Normal;
                        // タイプ切り替え時に終了レーン情報をリセット
                        clickedNote.endLaneIndex = -1;
                        EditorUtility.SetDirty(spawner);
                        e.Use();
                    }
                }
                break;

            case EventType.MouseDrag:
                if (isDragging && draggedNote != null)
                {
                    if (lane != -1)
                    {
                        draggedNote.laneIndex = Mathf.Clamp(lane, 0, 3);
                    }
                    draggedNote.spawnTime = time;
                    EditorUtility.SetDirty(spawner);
                    e.Use();
                    Repaint();
                }
                else if (isDraggingTail && draggedTailNote != null)
                {
                    // テールドラッグ中：終了時間と終了レーンを更新
                    if (lane != -1)
                    {
                        draggedTailNote.endLaneIndex = Mathf.Clamp(lane, 0, 3);
                    }
                    // 終了時間は開始時間より前にならないように制限
                    float newDuration = time - draggedTailNote.spawnTime;
                    draggedTailNote.duration = Mathf.Max(newDuration, 0.1f);
                    EditorUtility.SetDirty(spawner);
                    e.Use();
                    Repaint();
                }
                break;

            case EventType.MouseUp:
                if (isDragging || isDraggingTail)
                {
                    isDragging = false;
                    isDraggingTail = false;
                    draggedNote = null;
                    draggedTailNote = null;
                    e.Use();
                    Repaint();
                }
                break;
        }
    }
}
