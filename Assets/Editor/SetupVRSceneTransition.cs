using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public class SetupVRSceneTransition : Editor
{
    [MenuItem("Tools/Setup VR Photo Transition & Screen")]
    public static void Setup()
    {
        // Save current changes to avoid losing work
        EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();

        SetupTestScene();
        SetupResultScene();
        
        Debug.Log("VR Photo Transition & Screen setup completed successfully!");
    }

    private static void SetupTestScene()
    {
        // 1. Open TestScene
        string scenePath = "Assets/Scenes/TestScene.unity";
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

        // 2. Find "App Config"
        var configObj = GameObject.Find("App Config");
        if (configObj == null)
        {
            configObj = new GameObject("App Config");
        }

        // Add VRSceneTransitionTrigger if not present
        if (configObj.GetComponent<VRSceneTransitionTrigger>() == null)
        {
            configObj.AddComponent<VRSceneTransitionTrigger>();
        }

        // Add VRScreenFader if not present
        if (configObj.GetComponent<VRScreenFader>() == null)
        {
            configObj.AddComponent<VRScreenFader>();
        }

        EditorSceneManager.SaveScene(scene);
        Debug.Log("TestScene configured successfully.");
    }

    private static void SetupResultScene()
    {
        // 1. Open VRPhotoResultTest scene
        string scenePath = "Assets/Scenes/VRPhotoResultTest.unity";
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

        // 2. Destroy the existing XROriginVR first to ensure we start clean and don't keep a broken instance
        var oldRig = GameObject.Find("XROriginVR");
        if (oldRig != null)
        {
            DestroyImmediate(oldRig);
            Debug.Log("Destroyed old XROriginVR to ensure clean instantiation.");
        }

        // 3. Remove standalone non-VR cameras (cameras that are not part of the XROriginVR camera rig)
        var cameras = GameObject.FindObjectsByType<Camera>(FindObjectsInactive.Include);
        foreach (var cam in cameras)
        {
            // Destroy only if it is not part of the VR rig (check root name)
            if (cam.transform.root.name != "XROriginVR")
            {
                DestroyImmediate(cam.gameObject);
                Debug.Log("Removed standalone camera: " + cam.name);
            }
        }

        // 4. Load and instantiate a fresh XROriginVR prefab
        var xrRigName = "XROriginVR";
        string prefabPath = "Assets/VRCameraAssets/XROriginVR.prefab";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab != null)
        {
            var xrRig = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            xrRig.name = xrRigName;
            xrRig.transform.position = Vector3.zero;
            xrRig.transform.rotation = Quaternion.identity;
            Debug.Log("Instantiated fresh XROriginVR prefab.");

            // Deactivate VRPhoneCamera to prevent trigger conflicts
            var phoneCamera = xrRig.GetComponentInChildren<VRPhoneCamera>(true);
            if (phoneCamera != null)
            {
                phoneCamera.gameObject.SetActive(false);
                Debug.Log("Deactivated VRPhoneCamera in VRPhotoResultTest.");
            }
        }
        else
        {
            Debug.LogError("Could not find XROriginVR prefab at " + prefabPath);
        }

        // 5. Create or find the large screen
        var screenName = "PhotoScreen";
        var screenObj = GameObject.Find(screenName);
        if (screenObj == null)
        {
            screenObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
            screenObj.name = screenName;
        }

        // Configure transform: Y rotation set to 0, placed in front of player (Z=3)
        screenObj.transform.position = new Vector3(0f, 1.5f, 3f);
        screenObj.transform.rotation = Quaternion.Euler(0f, 0f, 0f);
        screenObj.transform.localScale = new Vector3(4f, 3f, 1f);

        // Add VRPhotoViewer if not present
        var viewer = screenObj.GetComponent<VRPhotoViewer>();
        if (viewer == null)
        {
            screenObj.AddComponent<VRPhotoViewer>();
        }

        // Remove collider
        var col = screenObj.GetComponent<Collider>();
        if (col != null)
        {
            DestroyImmediate(col);
        }

        EditorSceneManager.SaveScene(scene);
        Debug.Log("VRPhotoResultTest configured successfully with fresh XR Origin.");

        // Reload TestScene so the editor returns to the starting scene
        EditorSceneManager.OpenScene("Assets/Scenes/TestScene.unity", OpenSceneMode.Single);
    }
}
