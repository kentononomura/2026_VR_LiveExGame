using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class VRPauseMenu : MonoBehaviour
{
    private static VRPauseMenu instance;
    private GameObject menuUI;
    private bool isPaused = false;
    private float previousTimeScale = 1f;

#if ENABLE_INPUT_SYSTEM
    private InputAction pauseAction;
    private InputAction leftTriggerAction;
    private InputAction rightTriggerAction;
#endif

    private UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor leftRay;
    private UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor rightRay;

    private System.Collections.Generic.Dictionary<GameObject, bool> rayGoStates = new System.Collections.Generic.Dictionary<GameObject, bool>();
    private System.Collections.Generic.Dictionary<UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor, bool> rayStates = new System.Collections.Generic.Dictionary<UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor, bool>();
    private System.Collections.Generic.Dictionary<UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor, LayerMask> rayMasks = new System.Collections.Generic.Dictionary<UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor, LayerMask>();
    private System.Collections.Generic.Dictionary<UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor, float> rayDistances = new System.Collections.Generic.Dictionary<UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor, float>();
    private System.Collections.Generic.Dictionary<Collider, bool> originalColliderStates = new System.Collections.Generic.Dictionary<Collider, bool>();
    private System.Collections.Generic.Dictionary<CharacterController, bool> originalCCStates = new System.Collections.Generic.Dictionary<CharacterController, bool>();

    public static bool IsGamePaused()
    {
        return instance != null && instance.isPaused;
    }

    // This ensures the manager automatically spawns when the game starts, without modifying prefabs.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        if (instance == null)
        {
            GameObject obj = new GameObject("VRPauseMenuManager");
            instance = obj.AddComponent<VRPauseMenu>();
            DontDestroyOnLoad(obj);
        }
    }

    private void Awake()
    {
#if ENABLE_INPUT_SYSTEM
        // Right hand secondary button (B button)
        pauseAction = new InputAction("Pause", InputActionType.Button, "<XRController>{RightHand}/secondaryButton");
        pauseAction.performed += OnPauseActionPerformed;
        pauseAction.Enable();

        leftTriggerAction = new InputAction("LeftTriggerPause", InputActionType.Button, "<XRController>{LeftHand}/triggerPressed");
        leftTriggerAction.Enable();

        rightTriggerAction = new InputAction("RightTriggerPause", InputActionType.Button, "<XRController>{RightHand}/triggerPressed");
        rightTriggerAction.Enable();
#endif
        SceneManager.sceneLoaded += OnSceneLoaded;
        
        // Always instantiate a clean runtime UI instance
        CreateMenuUI();
        
        // Clean up any manually placed scene duplicates immediately
        CleanSceneDuplicates();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Always force time scale and audio back to active on scene load to prevent freeze states
        Time.timeScale = 1f;
        AudioListener.pause = false;
        isPaused = false;

        // Clean up duplicates on every scene load
        CleanSceneDuplicates();
    }

    private void TemporarilyIgnoreHeldObjects(bool ignore)
    {
        if (ignore)
        {
            originalColliderStates.Clear();
            originalCCStates.Clear();
            var xrOrigin = FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (xrOrigin != null)
            {
                // Walk up to the absolute root of the player rig to find parent colliders/CharacterControllers
                Transform root = xrOrigin.transform;
                while (root.parent != null)
                {
                    root = root.parent;
                }

                // 1. Disable all colliders in the player hierarchy (phone, hands, penlights)
                var colliders = root.GetComponentsInChildren<Collider>(true);
                foreach (var col in colliders)
                {
                    if (col.enabled)
                    {
                        originalColliderStates[col] = col.enabled;
                        col.enabled = false;
                    }
                }

                // 2. Disable all CharacterControllers in the player hierarchy (which don't inherit from Collider)
                var ccs = root.GetComponentsInChildren<CharacterController>(true);
                foreach (var cc in ccs)
                {
                    if (cc.enabled)
                    {
                        originalCCStates[cc] = cc.enabled;
                        cc.enabled = false;
                    }
                }
            }
        }
        else
        {
            foreach (var kvp in originalColliderStates)
            {
                if (kvp.Key != null)
                {
                    kvp.Key.enabled = kvp.Value;
                }
            }
            originalColliderStates.Clear();

            foreach (var kvp in originalCCStates)
            {
                if (kvp.Key != null)
                {
                    kvp.Key.enabled = kvp.Value;
                }
            }
            originalCCStates.Clear();
        }
    }

    private void CleanSceneDuplicates()
    {
        // Find all canvases in the scene (including inactive ones)
        var canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var c in canvases)
        {
            GameObject go = c.gameObject;
            // If it's not our managed menuUI and its name matches the prefab, destroy it!
            if (go != menuUI && (go.name == "VRPauseMenuPrefab" || go.name == "PauseMenuCanvas" || go.name.Contains("VRPauseMenuPrefab")))
            {
                Destroy(go);
                Debug.Log("[VRPauseMenu] Cleaned up duplicate pause UI: " + go.name);
            }
        }
    }

    private void OnDestroy()
    {
#if ENABLE_INPUT_SYSTEM
        if (pauseAction != null)
        {
            pauseAction.performed -= OnPauseActionPerformed;
            pauseAction.Disable();
        }
        if (leftTriggerAction != null) leftTriggerAction.Disable();
        if (rightTriggerAction != null) rightTriggerAction.Disable();
#endif
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

#if ENABLE_INPUT_SYSTEM
    private void OnPauseActionPerformed(InputAction.CallbackContext context)
    {
        // Only allow pausing in TestScene and ResultScene (VRPhotoResultTest)
        string currentScene = SceneManager.GetActiveScene().name;
        if (currentScene != "TestScene" && currentScene != "VRPhotoResultTest")
        {
            return;
        }

        TogglePause();
    }
#endif

    private void TogglePause()
    {
        EnsureEventSystem();

        if (isPaused)
        {
            ResumeGame();
        }
        else
        {
            PauseGame();
        }
    }

    private void EnsureEventSystem()
    {
        EventSystem es = FindAnyObjectByType<EventSystem>();
        if (es == null)
        {
            GameObject esObj = new GameObject("EventSystem");
            es = esObj.AddComponent<EventSystem>();
        }

        // Check if the EventSystem has XRUIInputModule. If not, add it.
        var xrInput = es.GetComponent<UnityEngine.XR.Interaction.Toolkit.UI.XRUIInputModule>();
        if (xrInput == null)
        {
            // Also disable StandaloneInputModule to prevent conflicts in VR
            var standalone = es.GetComponent<StandaloneInputModule>();
            if (standalone != null) standalone.enabled = false;

            // Disable standard InputSystemUIInputModule if present to prevent conflict
            var inputSystemUI = es.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            if (inputSystemUI != null) inputSystemUI.enabled = false;

            es.gameObject.AddComponent<UnityEngine.XR.Interaction.Toolkit.UI.XRUIInputModule>();
            Debug.Log("VRPauseMenu: Configured EventSystem with XRUIInputModule.");
        }
    }

    private void FindRays()
    {
        leftRay = null;
        rightRay = null;
        var rays = FindObjectsByType<UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var r in rays)
        {
            // Check self name, parent name, and grandparent name to see if it's Left/Right hand
            string selfName = r.gameObject.name.ToLower();
            string parentName = r.transform.parent != null ? r.transform.parent.name.ToLower() : "";
            string grandParentName = (r.transform.parent != null && r.transform.parent.parent != null) ? r.transform.parent.parent.name.ToLower() : "";

            bool isLeft = selfName.Contains("left") || parentName.Contains("left") || grandParentName.Contains("left");
            bool isRight = selfName.Contains("right") || parentName.Contains("right") || grandParentName.Contains("right");

            if (isLeft) leftRay = r;
            if (isRight) rightRay = r;
        }
        Debug.Log($"[VRPauseMenu] FindRays - Left: {(leftRay != null ? leftRay.name : "null")}, Right: {(rightRay != null ? rightRay.name : "null")}");
    }

    private Camera GetVRCamera()
    {
        var xrOrigin = FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
        if (xrOrigin != null && xrOrigin.Camera != null)
        {
            return xrOrigin.Camera;
        }
        
        // Fallback: Find MainCamera that isn't the smartphone camera
        Camera[] cams = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var c in cams)
        {
            if (c.CompareTag("MainCamera") && c.name != "ViewfinderCamera" && !c.name.Contains("Phone"))
            {
                return c;
            }
        }

        return Camera.main;
    }

    private void ToggleRayInteractors(bool forceEnable)
    {
        if (forceEnable)
        {
            rayStates.Clear();

            var rays = FindObjectsByType<UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var ray in rays)
            {
                rayStates[ray] = ray.enabled;

                ray.enabled = true;
                
                // Save original mask and distance, then set mask to target ONLY the UI layer
                rayMasks[ray] = ray.raycastMask;
                rayDistances[ray] = ray.maxRaycastDistance;

                ray.maxRaycastDistance = 100f;
                ray.raycastMask = LayerMask.GetMask("UI"); // Ignore all physics layers, only raycast the UI layer

                var lineVisual = ray.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.XRInteractorLineVisual>();
                if (lineVisual != null) lineVisual.enabled = true;
                var lineRenderer = ray.GetComponent<LineRenderer>();
                if (lineRenderer != null) lineRenderer.enabled = true;
            }
        }
        else
        {
            string currentScene = SceneManager.GetActiveScene().name;
            
            foreach (var kvp in rayStates)
            {
                if (kvp.Key != null)
                {
                    if (currentScene == "TestScene")
                    {
                        // In TestScene, completely disable the ray components, leaving the hand GameObject active
                        kvp.Key.enabled = false;
                        var lineVisual = kvp.Key.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.XRInteractorLineVisual>();
                        if (lineVisual != null) lineVisual.enabled = false;
                        var lineRenderer = kvp.Key.GetComponent<LineRenderer>();
                        if (lineRenderer != null) lineRenderer.enabled = false;
                    }
                    else
                    {
                        // Restore original states for other scenes
                        kvp.Key.enabled = kvp.Value;
                        if (rayMasks.TryGetValue(kvp.Key, out LayerMask wasMask))
                        {
                            kvp.Key.raycastMask = wasMask;
                        }
                        if (rayDistances.TryGetValue(kvp.Key, out float wasDist))
                        {
                            kvp.Key.maxRaycastDistance = wasDist;
                        }

                        var lineVisual = kvp.Key.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.XRInteractorLineVisual>();
                        if (lineVisual != null) lineVisual.enabled = kvp.Value;
                        var lineRenderer = kvp.Key.GetComponent<LineRenderer>();
                        if (lineRenderer != null) lineRenderer.enabled = kvp.Value;
                    }
                }
            }
            rayStates.Clear();
            rayMasks.Clear();
            rayDistances.Clear();
        }
    }

    private void PauseGame()
    {
        isPaused = true;
        previousTimeScale = Time.timeScale;
        Time.timeScale = 0.0001f;
        AudioListener.pause = true;

        if (menuUI != null)
        {
            // Set the menu UI and all its children to the UI layer (Layer 5)
            int uiLayer = LayerMask.NameToLayer("UI");
            foreach (Transform t in menuUI.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.layer = uiLayer;
            }
            menuUI.layer = uiLayer;

            Camera cam = GetVRCamera();
            if (cam != null)
            {
                Canvas canvas = menuUI.GetComponent<Canvas>();
                if (canvas != null)
                {
                    canvas.worldCamera = cam;
                }
                // Spawn the menu at 1.0m using the true VR eye camera
                menuUI.transform.position = cam.transform.position + cam.transform.forward * 1.0f;
                menuUI.transform.rotation = Quaternion.LookRotation(menuUI.transform.position - cam.transform.position);
                menuUI.transform.localScale = new Vector3(0.0012f, 0.0012f, 0.0012f);
            }

            // Sync volume slider before showing
            Slider volumeSlider = menuUI.transform.Find("Panel/Slider").GetComponent<Slider>();
            if (volumeSlider != null)
            {
                volumeSlider.value = AudioListener.volume;
            }

            menuUI.SetActive(true);
            Debug.Log("[VRPauseMenu] Pause menu set to active.");
        }

        ToggleRayInteractors(true);
        TemporarilyIgnoreHeldObjects(true);
        FindRays();
    }

    private void ResumeGame()
    {
        isPaused = false;
        Time.timeScale = previousTimeScale > 0f ? previousTimeScale : 1f;
        AudioListener.pause = false;

        if (menuUI != null)
        {
            menuUI.SetActive(false);
            Debug.Log("[VRPauseMenu] Pause menu set to inactive.");
        }

        ToggleRayInteractors(false);
        TemporarilyIgnoreHeldObjects(false);
    }

    private void Update()
    {
        if (!isPaused) return;

#if ENABLE_INPUT_SYSTEM
        // Handle trigger clicks programmatically to bypass any broken native UI Select setups
        if (leftTriggerAction != null)
        {
            if (leftTriggerAction.IsPressed())
            {
                HandleRayInteraction(leftRay, leftTriggerAction.WasPressedThisFrame());
            }
        }
        if (rightTriggerAction != null)
        {
            if (rightTriggerAction.IsPressed())
            {
                HandleRayInteraction(rightRay, rightTriggerAction.WasPressedThisFrame());
            }
        }
#endif
    }

    private void HandleRayInteraction(UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor ray, bool isNewPress)
    {
        if (ray == null || !ray.enabled || !ray.gameObject.activeInHierarchy) return;

        if (ray.TryGetCurrentUIRaycastResult(out RaycastResult result))
        {
            if (result.gameObject != null)
            {
                // Handle Slider
                Slider slider = result.gameObject.GetComponentInParent<Slider>();
                if (slider != null && slider.interactable)
                {
                    Canvas canvas = menuUI.GetComponent<Canvas>();
                    if (canvas != null && canvas.worldCamera != null)
                    {
                        RectTransform sliderRt = slider.GetComponent<RectTransform>();
                        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(sliderRt, result.screenPosition, canvas.worldCamera, out Vector2 localPoint))
                        {
                            float width = sliderRt.rect.width;
                            float normalizedValue = Mathf.Clamp01((localPoint.x + width / 2f) / width);
                            slider.value = Mathf.Lerp(slider.minValue, slider.maxValue, normalizedValue);
                        }
                    }
                    return;
                }

                // Handle Button
                if (isNewPress)
                {
                    Button btn = result.gameObject.GetComponentInParent<Button>();
                    if (btn != null && btn.interactable)
                    {
                        var pointerEventData = new PointerEventData(EventSystem.current);
                        ExecuteEvents.Execute(btn.gameObject, pointerEventData, ExecuteEvents.pointerClickHandler);
                        ExecuteEvents.Execute(btn.gameObject, pointerEventData, ExecuteEvents.submitHandler);
                    }
                }
            }
        }
    }

    private void CreateMenuUI()
    {
        GameObject prefab = Resources.Load<GameObject>("VRPauseMenuPrefab");
        if (prefab == null)
        {
            Debug.LogError("[VRPauseMenu] VRPauseMenuPrefab could not be loaded from Resources!");
            return;
        }

        menuUI = Instantiate(prefab);
        menuUI.transform.SetParent(this.transform);

        // Ensure default local transform settings
        menuUI.transform.localPosition = Vector3.zero;
        menuUI.transform.localRotation = Quaternion.identity;

        // Bind events dynamically to the prefab's elements
        Slider volumeSlider = menuUI.transform.Find("Panel/Slider").GetComponent<Slider>();
        if (volumeSlider != null)
        {
            volumeSlider.value = AudioListener.volume;
            volumeSlider.onValueChanged.AddListener((val) => { AudioListener.volume = val; });
        }

        Button resumeBtn = menuUI.transform.Find("Panel/ResumeBtn").GetComponent<Button>();
        if (resumeBtn != null)
        {
            resumeBtn.onClick.AddListener(ResumeGame);
        }

        Button titleBtn = menuUI.transform.Find("Panel/TitleBtn").GetComponent<Button>();
        if (titleBtn != null)
        {
            titleBtn.onClick.AddListener(() =>
            {
                ResumeGame();
                if (VRScreenFader.Instance != null)
                {
                    VRScreenFader.Instance.FadeOut(1.0f, () =>
                    {
                        SceneManager.LoadScene("TitleScene");
                    });
                }
                else
                {
                    SceneManager.LoadScene("TitleScene");
                }
            });
        }

        Button quitBtn = menuUI.transform.Find("Panel/QuitBtn").GetComponent<Button>();
        if (quitBtn != null)
        {
            quitBtn.onClick.AddListener(() =>
            {
                Application.Quit();
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#endif
            });
        }

        menuUI.SetActive(false);
    }
}
