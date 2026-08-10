using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(NoteSpawner))]
public class NoteSpawnerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        // デフォルトの描画（リストなど）をそのまま表示
        DrawDefaultInspector();

        NoteSpawner spawner = (NoteSpawner)target;

        GUILayout.Space(20);
        EditorGUILayout.LabelField("譜面作成 補助ツール", EditorStyles.boldLabel);

        if (spawner.beatmap == null)
            spawner.beatmap = new System.Collections.Generic.List<NoteData>();

        // +0.1秒追加 と +0.5秒追加
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("最後に +0.1秒 追加"))
        {
            AddNoteWithOffset(spawner, 0.1f);
        }
        if (GUILayout.Button("最後に +0.5秒 追加"))
        {
            AddNoteWithOffset(spawner, 0.5f);
        }
        EditorGUILayout.EndHorizontal();

        // コピー機能（同時押し）
        if (GUILayout.Button("最後のノーツを別レーンにコピー (同時押し)"))
        {
            if (spawner.beatmap.Count > 0)
            {
                NoteData lastNote = spawner.beatmap[spawner.beatmap.Count - 1];
                int newLane = (lastNote.laneIndex + 1) % 4; // 隣のレーンにずらす
                
                NoteData newNote = new NoteData
                {
                    spawnTime = lastNote.spawnTime,
                    laneIndex = newLane,
                    type = lastNote.type,
                    duration = lastNote.duration
                };
                
                Undo.RecordObject(spawner, "Copy Note");
                spawner.beatmap.Add(newNote);
                EditorUtility.SetDirty(spawner);
            }
        }

        // ソート機能
        if (GUILayout.Button("時間順にソート"))
        {
            Undo.RecordObject(spawner, "Sort Beatmap");
            spawner.beatmap.Sort((a, b) => a.spawnTime.CompareTo(b.spawnTime));
            EditorUtility.SetDirty(spawner);
        }

        GUILayout.Space(20);
        
        // 専用エディタを開くボタン
        GUI.backgroundColor = new Color(0.3f, 0.8f, 1f);
        if (GUILayout.Button("専用エディタ (Timeline) を開く", GUILayout.Height(40)))
        {
            BeatmapEditorWindow.ShowWindow(spawner);
        }
        GUI.backgroundColor = Color.white;
    }

    private void AddNoteWithOffset(NoteSpawner spawner, float offset)
    {
        float newTime = 0f;
        int lastLane = 0;
        NoteType lastType = NoteType.Normal;
        
        if (spawner.beatmap.Count > 0)
        {
            NoteData lastNote = spawner.beatmap[spawner.beatmap.Count - 1];
            newTime = lastNote.spawnTime + offset;
            lastLane = lastNote.laneIndex;
            lastType = lastNote.type;
        }

        NoteData newNote = new NoteData
        {
            spawnTime = newTime,
            laneIndex = lastLane,
            type = lastType,
            duration = 1.0f // ロングだった場合のデフォルト
        };

        Undo.RecordObject(spawner, "Add Note");
        spawner.beatmap.Add(newNote);
        EditorUtility.SetDirty(spawner);
    }
}
