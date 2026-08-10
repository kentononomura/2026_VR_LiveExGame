using UnityEngine;
using UnityEditor;

public class FixNotePrefab
{
    [MenuItem("Tools/Fix Note Prefab")]
    public static void FixPrefab()
    {
        // 1. 空のルートオブジェクトを作成
        GameObject root = new GameObject("Note");
        BoxCollider col = root.AddComponent<BoxCollider>();
        col.isTrigger = true;
        col.size = new Vector3(0.5f, 0.5f, 0.5f);
        
        Rigidbody rb = root.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        Note noteScript = root.AddComponent<Note>();

        // 2. 子にVisual（見た目）を作成
        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visual.name = "Visual";
        visual.transform.SetParent(root.transform);
        visual.transform.localPosition = Vector3.zero;
        Object.DestroyImmediate(visual.GetComponent<Collider>()); // 物理判定は親で行うため削除

        noteScript.visual = visual.transform;

        // 3. プレハブとして保存
        string path = "Assets/Note.prefab";
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);

        // 4. シーン内のNoteSpawnerに自動セット
        NoteSpawner spawner = Object.FindAnyObjectByType<NoteSpawner>();
        if (spawner != null)
        {
            spawner.notePrefab = prefab;
            EditorUtility.SetDirty(spawner);
            Debug.Log("<color=green>Noteプレハブの生成と自動セットが完了しました！</color>");
        }
        else
        {
            Debug.LogWarning("NoteSpawnerが見つかりませんでしたが、AssetsフォルダにNote.prefabを作成しました。");
        }
    }
}
