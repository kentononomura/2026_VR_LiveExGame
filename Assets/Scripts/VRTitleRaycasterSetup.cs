using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.UI;

public class VRTitleRaycasterSetup : MonoBehaviour
{
    [Header("Line Visual Settings")]
    [Tooltip("レーザーの太さ")]
    public float lineWidth = 0.015f;
    [Tooltip("レーザーの色（不透明〜半透明赤のグラデーション）")]
    public Color laserColor = Color.red;

    void Start()
    {
        // 1. 本編用のSaber（剣）がタイトル画面に出てこないように非表示にする
        DisableSabersInTitle();

        // 2. 左右のVRコントローラーにレーザーポインター（Ray Interactor）を追加・有効化する
        SetupRayInteractors();

        // 3. CanvasにVR用UIレイスキャスター（UIポインター判定）を追加する
        SetupCanvasRaycaster();

        // 4. EventSystemにVR用のUI入力モジュールを組み込む
        SetupEventSystem();
    }

    private void DisableSabersInTitle()
    {
        // シーン内のすべてのSaberコンポーネントを探して、ゲームオブジェクトごと非アクティブにする
        Saber[] sabers = FindObjectsByType<Saber>(FindObjectsInactive.Include);
        foreach (var saber in sabers)
        {
            saber.gameObject.SetActive(false);
            Debug.Log($"[VRSetup] タイトル画面のため、Saber '{saber.gameObject.name}' を非アクティブにしました。");
        }
    }

    private void SetupRayInteractors()
    {
        List<GameObject> controllers = new List<GameObject>();

        // UnityのXROriginを取得して、その配下からコントローラーのゲームオブジェクトを探す（バージョン依存を防ぐため名前とカメラ除外で特定）
        var xrOrigin = FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
        if (xrOrigin != null)
        {
            Transform offset = xrOrigin.CameraFloorOffsetObject != null 
                ? xrOrigin.CameraFloorOffsetObject.transform 
                : xrOrigin.transform;

            for (int i = 0; i < offset.childCount; i++)
            {
                Transform child = offset.GetChild(i);
                string nameLower = child.name.ToLower();
                if ((nameLower.Contains("hand") || nameLower.Contains("controller") || nameLower.Contains("left") || nameLower.Contains("right")) &&
                    child.GetComponent<Camera>() == null)
                {
                    controllers.Add(child.gameObject);
                }
            }
        }

        // もしXROriginから見つからなかった場合は、Saberコンポーネントの親オブジェクトを探索（フォールバック）
        if (controllers.Count == 0)
        {
            Saber[] sabers = FindObjectsByType<Saber>(FindObjectsInactive.Include);
            foreach (var s in sabers)
            {
                if (!controllers.Contains(s.gameObject))
                {
                    controllers.Add(s.gameObject);
                }
            }
        }

        if (controllers.Count == 0)
        {
            Debug.LogWarning("[VRSetup] コントローラーのゲームオブジェクトが特定できませんでした。");
            return;
        }

        // デフォルトのライン用マテリアル（シンプルな色表示用）を取得または作成
        Material lineMat = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended Premultiply"));

        foreach (var ctrlObj in controllers)
        {
            // --- 2a. XR Ray Interactor の追加・有効化 ---
            UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor rayInteractor = ctrlObj.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor>();
            if (rayInteractor == null)
            {
                rayInteractor = ctrlObj.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor>();
            }
            rayInteractor.enabled = true;
            rayInteractor.maxRaycastDistance = 100f; // 100m先まで届くように射程を設定
            rayInteractor.raycastMask = ~0; // 全てのレイヤーを対象にする
            
            // --- 2b. Line Renderer（線の描画）の追加・設定 ---
            LineRenderer lineRenderer = ctrlObj.GetComponent<LineRenderer>();
            if (lineRenderer == null)
            {
                lineRenderer = ctrlObj.AddComponent<LineRenderer>();
            }
            lineRenderer.enabled = true;
            lineRenderer.widthMultiplier = lineWidth;
            lineRenderer.useWorldSpace = true;
            lineRenderer.sharedMaterial = lineMat;

            // 赤から透明に消えていくグラデーション
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[] { new GradientColorKey(laserColor, 0.0f), new GradientColorKey(laserColor, 1.0f) },
                new GradientAlphaKey[] { new GradientAlphaKey(1.0f, 0.0f), new GradientAlphaKey(0.0f, 1.0f) }
            );
            lineRenderer.colorGradient = gradient;

            // --- 2c. XR Interactor Line Visual（レーザー表示制御）の追加 ---
            UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.XRInteractorLineVisual lineVisual = ctrlObj.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.XRInteractorLineVisual>();
            if (lineVisual == null)
            {
                lineVisual = ctrlObj.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.XRInteractorLineVisual>();
            }
            lineVisual.enabled = true;

            Debug.Log($"[VRSetup] コントローラー '{ctrlObj.name}' にレーザーポインター（Ray Interactor）をセットアップしました。");
        }
    }

    private void SetupCanvasRaycaster()
    {
        // シーン内のすべてのCanvasを対象にする
        Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include);
        foreach (var canvas in canvases)
        {
            // World Space キャンバスのみにVRレーザー判定を追加
            if (canvas.renderMode == RenderMode.WorldSpace)
            {
                // UIイベントを受け取るカメラを設定（これがないとWorld Space Canvasは入力に反応しません）
                Camera mainCam = Camera.main;
                if (mainCam != null)
                {
                    canvas.worldCamera = mainCam;
                }

                TrackedDeviceGraphicRaycaster raycaster = canvas.GetComponent<TrackedDeviceGraphicRaycaster>();
                if (raycaster == null)
                {
                    raycaster = canvas.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
                }
                raycaster.enabled = true;
                Debug.Log($"[VRSetup] キャンバス '{canvas.gameObject.name}' に Event Camera と TrackedDeviceGraphicRaycaster を設定しました。");
            }
        }
    }

    private void SetupEventSystem()
    {
        var allEventSystems = FindObjectsByType<EventSystem>(FindObjectsInactive.Include);
        EventSystem eventSystem = null;
        if (allEventSystems.Length > 0)
        {
            eventSystem = allEventSystems[0];
            foreach (var es in allEventSystems)
            {
                if (es != null && es.gameObject.activeInHierarchy)
                {
                    eventSystem = es;
                    break;
                }
            }
            foreach (var es in allEventSystems)
            {
                if (es != null && es != eventSystem)
                {
                    Destroy(es.gameObject);
                }
            }
        }
        else
        {
            GameObject esObj = new GameObject("EventSystem");
            eventSystem = esObj.AddComponent<EventSystem>();
        }

        // VR用の UI Input Module を追加
        XRUIInputModule xrInputModule = eventSystem.GetComponent<XRUIInputModule>();
        if (xrInputModule == null)
        {
            // 従来のキーボード・マウス用モジュールがあれば無効化する
            StandaloneInputModule standalone = eventSystem.GetComponent<StandaloneInputModule>();
            if (standalone != null)
            {
                standalone.enabled = false;
            }

            xrInputModule = eventSystem.gameObject.AddComponent<XRUIInputModule>();
        }
        xrInputModule.enabled = true;
        Debug.Log("[VRSetup] EventSystem に XRUIInputModule をセットアップしました。");
    }
}
