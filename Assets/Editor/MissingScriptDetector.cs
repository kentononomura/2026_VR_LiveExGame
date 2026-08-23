using UnityEditor;
using UnityEngine;

public class MissingScriptDetector : Editor
{
    [MenuItem("Tools/Find Missing Scripts in Scene")]
    public static void FindMissingScripts()
    {
        int missingCount = 0;
        GameObject[] goList = GameObject.FindObjectsByType<GameObject>(FindObjectsInactive.Include);
        
        foreach (GameObject go in goList)
        {
            Component[] components = go.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] == null)
                {
                    Debug.LogError($"[Missing Script] Found missing script on GameObject: {GetGameObjectPath(go)}", go);
                    missingCount++;
                }
            }
        }
        
        Debug.Log($"[Missing Script Search Completed] Found {missingCount} missing script(s) in active scene.");
    }

    [MenuItem("Tools/Remove Missing Scripts in Scene")]
    public static void RemoveMissingScripts()
    {
        int removedCount = 0;
        GameObject[] goList = GameObject.FindObjectsByType<GameObject>(FindObjectsInactive.Include);
        
        foreach (GameObject go in goList)
        {
            int count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
            if (count > 0)
            {
                Undo.RegisterCompleteObjectUndo(go, "Remove Missing Scripts");
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
                Debug.Log($"[Missing Script] Removed {count} missing script(s) from GameObject: {GetGameObjectPath(go)}", go);
                removedCount += count;
            }
        }
        
        Debug.Log($"[Missing Script Cleanup Completed] Removed {removedCount} missing script(s) from the scene.");
    }

    private static string GetGameObjectPath(GameObject obj)
    {
        string path = "/" + obj.name;
        while (obj.transform.parent != null)
        {
            obj = obj.transform.parent.gameObject;
            path = "/" + obj.name + path;
        }
        return path;
    }
}
