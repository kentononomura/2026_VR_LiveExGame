using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// XROrigin をシーン遷移時に破棄せず維持するスクリプト。
/// Quest Link 環境では、XROrigin が破棄・再生成されるとトラッキングが切断され
/// 「視界が固定される」「ペンライトが見えなくなる」等の不具合が発生する。
/// このスクリプトにより XR リグ全体（カメラ、コントローラー、TrackedPoseDriver 等）を
/// シーン間で維持し、トラッキングの断絶を防ぐ。
/// </summary>
public class XROriginPersistence : MonoBehaviour
{
    private static XROriginPersistence instance;

    /// <summary>
    /// ゲーム起動時に自動実行。XROrigin を探して永続化スクリプトをアタッチする。
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoAttach()
    {
        if (instance != null) return;

        var xrOrigin = FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
        if (xrOrigin != null && xrOrigin.GetComponent<XROriginPersistence>() == null)
        {
            xrOrigin.gameObject.AddComponent<XROriginPersistence>();
        }

        SceneManager.sceneLoaded += OnSceneLoadedStatic;
    }

    private static void OnSceneLoadedStatic(Scene scene, LoadSceneMode mode)
    {
        if (instance != null) return;

        var xrOrigin = FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
        if (xrOrigin != null && xrOrigin.GetComponent<XROriginPersistence>() == null)
        {
            xrOrigin.gameObject.AddComponent<XROriginPersistence>();
        }
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.Log($"[XROriginPersistence] 既存の永続 XR リグが存在するため、複製 '{gameObject.name}' を破棄します。");
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        ConfigureFloorTrackingOrigin();
        Debug.Log($"[XROriginPersistence] '{gameObject.name}' を永続化しました。シーン遷移後もトラッキングを維持します。");

        SceneManager.sceneLoaded += OnSceneLoadedInstance;
    }

    /// <summary>
    /// Quest実機とQuest LinkでHMD・コントローラーの高さ基準を床へ統一する。
    /// Device基準用の固定身長オフセットと実機の床基準が混在することを防ぐ。
    /// </summary>
    private void ConfigureFloorTrackingOrigin()
    {
        var xrOrigin = GetComponent<Unity.XR.CoreUtils.XROrigin>();
        if (xrOrigin == null) return;

        xrOrigin.RequestedTrackingOriginMode =
            Unity.XR.CoreUtils.XROrigin.TrackingOriginMode.Floor;
        xrOrigin.CameraYOffset = 0f;

        if (xrOrigin.CameraFloorOffsetObject != null)
        {
            Transform offsetTransform = xrOrigin.CameraFloorOffsetObject.transform;
            Vector3 localPosition = offsetTransform.localPosition;
            localPosition.y = 0f;
            offsetTransform.localPosition = localPosition;
        }

        Debug.Log("[XROriginPersistence] Tracking OriginをFloorへ統一し、固定Camera Y Offsetを無効化しました。");
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            SceneManager.sceneLoaded -= OnSceneLoadedInstance;
            instance = null;
        }
    }

    private void OnSceneLoadedInstance(Scene scene, LoadSceneMode mode)
    {
        Debug.Log($"[XROriginPersistence] シーン '{scene.name}' がロードされました。");

        Vector3 targetPos = transform.position; // デフォルトは現在の位置
        Quaternion targetRot = transform.rotation;
        bool foundDuplicate = false;

        // 新しいシーンに含まれる重複 XROrigin を検索
        var allXROrigins = FindObjectsByType<Unity.XR.CoreUtils.XROrigin>(FindObjectsInactive.Include);
        foreach (var xrOrigin in allXROrigins)
        {
            if (xrOrigin.gameObject == gameObject) continue;

            // 破棄される重複リグが配置されていた「シーン固有の正しい座標」を記憶
            targetPos = xrOrigin.transform.position;
            targetRot = xrOrigin.transform.rotation;
            foundDuplicate = true;

            // 破棄前に、重複リグが持つシーン固有の子オブジェクトを永続リグに移植する
            MigrateSceneSpecificChildren(xrOrigin.gameObject);

            Debug.Log($"[XROriginPersistence] 重複する XROrigin '{xrOrigin.gameObject.name}' を破棄し、その座標({targetPos})を引き継ぎます。");
            // Destroyはフレーム末まで遅延するため、先に無効化して重複カメラ・入力・描画を即座に止める。
            xrOrigin.gameObject.SetActive(false);
            Destroy(xrOrigin.gameObject);
        }

        // シーンごとの位置・設定を適用
        ApplySceneSettings(scene.name, targetPos, targetRot, foundDuplicate);

        // 重複するXRリグ（カメラ）を破棄したため、シーン内のCanvasのEvent Cameraへの参照が外れてUIが押せなくなる問題の修正
        RepairCanvasCameras();
    }

    /// <summary>
    /// シーン内のすべてのワールドスペースキャンバスを探し、永続リグのカメラをイベントカメラとして再設定する
    /// </summary>
    private void RepairCanvasCameras()
    {
        Camera mainCam = Camera.main;
        if (mainCam == null) return;

        Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include);
        foreach (var canvas in canvases)
        {
            if (canvas.renderMode == RenderMode.WorldSpace)
            {
                canvas.worldCamera = mainCam;
            }
        }
        Debug.Log("[XROriginPersistence] シーン内の全Canvasの Event Camera を再設定しました。");
    }

    /// <summary>
    /// 破棄される重複リグから、永続リグに存在しないコンポーネントや子オブジェクトを移植する。
    /// 例: VRPhoneCamera、Saber、各種ペンライト設定など。
    /// </summary>
    private void MigrateSceneSpecificChildren(GameObject duplicateRig)
    {
        // VRPhoneCamera の移植
        var srcPhoneCamera = duplicateRig.GetComponentInChildren<VRPhoneCamera>(true);
        var dstPhoneCamera = gameObject.GetComponentInChildren<VRPhoneCamera>(true);
        if (srcPhoneCamera != null && dstPhoneCamera == null)
        {
            // VRPhoneCamera のゲームオブジェクト全体を永続リグの同じ親の下に移動
            Transform targetParent = FindMatchingParent(srcPhoneCamera.transform, duplicateRig.transform);
            if (targetParent != null)
            {
                srcPhoneCamera.transform.SetParent(targetParent, false);
                Debug.Log("[XROriginPersistence] VRPhoneCamera を永続リグに移植しました。");
            }
        }

        // Saber の移植（各コントローラーに付いているペンライト）
        var srcSabers = duplicateRig.GetComponentsInChildren<Saber>(true);
        var dstSabers = gameObject.GetComponentsInChildren<Saber>(true);
        if (srcSabers.Length > 0 && dstSabers.Length == 0)
        {
            foreach (var saber in srcSabers)
            {
                Transform targetParent = FindMatchingParent(saber.transform, duplicateRig.transform);
                if (targetParent != null)
                {
                    saber.transform.SetParent(targetParent, false);
                    Debug.Log($"[XROriginPersistence] Saber '{saber.gameObject.name}' を永続リグに移植しました。");
                }
            }
        }
    }

    /// <summary>
    /// srcChild の親の名前を辿って、永続リグ内の同名の親を見つける。
    /// 例: duplicateRig/Camera Offset/Right Hand Controller/VRPhoneCamera
    ///  → persistentRig/Camera Offset/Right Hand Controller を返す
    /// </summary>
    private Transform FindMatchingParent(Transform srcChild, Transform duplicateRoot)
    {
        // srcChild の親の相対パスを取得
        Transform srcParent = srcChild.parent;
        if (srcParent == null || srcParent == duplicateRoot) return transform;

        // 親の名前チェーンを構築
        var pathParts = new System.Collections.Generic.List<string>();
        Transform current = srcParent;
        while (current != null && current != duplicateRoot)
        {
            pathParts.Insert(0, current.name);
            current = current.parent;
        }

        // 永続リグ内で同じパスを辿る
        Transform target = transform;
        foreach (var part in pathParts)
        {
            Transform found = target.Find(part);
            if (found == null)
            {
                Debug.LogWarning($"[XROriginPersistence] 永続リグ内にパス '{part}' が見つかりません。直接の子として配置します。");
                return target;
            }
            target = found;
        }
        return target;
    }

    /// <summary>
    /// シーンごとのXRリグの位置と設定を適用する。
    /// </summary>
    private void ApplySceneSettings(string sceneName, Vector3 targetPos, Quaternion targetRot, bool foundDuplicate)
    {
        switch (sceneName)
        {
            case "TestScene":
                // TestScene: ステージ正面 Z=4 の位置
                transform.position = new Vector3(0f, transform.position.y, 4f);
                transform.rotation = Quaternion.Euler(0f, 180f, 0f);

                SetPhoneCameraActive(true);
                SetRayInteractorsActive(false);
                
                // TestSceneでのSaber制御はTestSceneVoiceManagerが行うため、ここでは特に弄らない
                Debug.Log($"[XROriginPersistence] TestScene: Position=({transform.position})");
                break;

            case "VRPhotoResultTest":
                // ResultScene: 重複リグがあればその座標、なければ原点
                transform.position = foundDuplicate ? new Vector3(targetPos.x, transform.position.y, targetPos.z) : new Vector3(0f, transform.position.y, 0f);
                transform.rotation = foundDuplicate ? targetRot : Quaternion.identity;

                SetPhoneCameraActive(false);
                
                // リザルトシーンではUI操作のためにRay（レーザー）を有効化
                SetRayInteractorsActive(true);
                
                // TestSceneで非表示にされたSaber（ペンライト）を復活させる
                RestoreSabers();

                Debug.Log($"[XROriginPersistence] VRPhotoResultTest: Position=({transform.position})");
                break;

            case "TitleScene":
                // TitleScene: 配置されていたXR Originの座標をそのまま引き継ぐ（Y座標はトラッキング維持のためそのまま）
                if (foundDuplicate)
                {
                    transform.position = new Vector3(targetPos.x, transform.position.y, targetPos.z);
                    transform.rotation = targetRot;
                }
                else
                {
                    // フォールバック
                    transform.position = new Vector3(0f, transform.position.y, 0f);
                    transform.rotation = Quaternion.identity;
                }
                
                SetPhoneCameraActive(false);
                SetRayInteractorsActive(true); // タイトルもUI操作があるので一応有効に
                // TestSceneでは右手ペンライトを隠すため、タイトルへ戻った時は両手とも復元する。
                RestoreSabers();
                
                Debug.Log($"[XROriginPersistence] TitleScene: Position=({transform.position})");
                break;

            default:
                // その他のシーン
                if (foundDuplicate)
                {
                    transform.position = new Vector3(targetPos.x, transform.position.y, targetPos.z);
                    transform.rotation = targetRot;
                }
                else
                {
                    transform.position = new Vector3(0f, transform.position.y, 0f);
                    transform.rotation = Quaternion.identity;
                }
                Debug.Log($"[XROriginPersistence] {sceneName}: Position=({transform.position})");
                break;
        }
    }

    private void SetPhoneCameraActive(bool isActive)
    {
        var phoneCamera = GetComponentInChildren<VRPhoneCamera>(true);
        if (phoneCamera != null)
        {
            phoneCamera.gameObject.SetActive(isActive);
            Debug.Log($"[XROriginPersistence] VRPhoneCamera を {(isActive ? "有効" : "無効")} にしました。");
        }
    }

    private void SetRayInteractorsActive(bool isActive)
    {
        StartCoroutine(SetRayInteractorsActiveRoutine(isActive));
    }

    private System.Collections.IEnumerator SetRayInteractorsActiveRoutine(bool isActive)
    {
        // 新しいシーンのEventSystemやXRUIInputModuleが完全に初期化(Start)されるのを待つため、1フレーム遅延させる
        yield return null;

        // コントローラーに付いているXRRayInteractorとLineRendererを切り替える
        var interactors = GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor>(true);
        foreach (var interactor in interactors)
        {
            if (isActive)
            {
                // 新しいシーンのEventSystem(XRUIInputModule)に確実に再登録させるため、一度無効にしてから有効化する
                interactor.enabled = false;
                interactor.enabled = true;
            }
            else
            {
                interactor.enabled = false;
            }
            
            // RayInteractorがアタッチされているオブジェクトのLineRendererも連動させる
            var lineRenderer = interactor.GetComponent<LineRenderer>();
            if (lineRenderer != null)
            {
                lineRenderer.enabled = isActive;
            }
            
            var lineVisual = interactor.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.XRInteractorLineVisual>();
            if (lineVisual != null)
            {
                lineVisual.enabled = isActive;
            }
        }
        Debug.Log($"[XROriginPersistence] XRRayInteractor (レーザーポインター) を {(isActive ? "有効(再登録完了)" : "無効")} にしました。");
    }

    private void RestoreSabers()
    {
        var sabers = GetComponentsInChildren<Saber>(true);
        foreach (var saber in sabers)
        {
            saber.enabled = true; // TestSceneでfalseにされたSaberスクリプトを復帰
            
            // TestSceneVoiceManager で非表示にされた子オブジェクト（SaberVisual等）を復帰
            foreach (Transform child in saber.transform)
            {
                if (child.name == "SaberVisual" || child.name == "HitboxVisual" || child.name == "PenlightMeterCanvas")
                {
                    child.gameObject.SetActive(true);
                }
            }
        }
        Debug.Log("[XROriginPersistence] 非表示にされていたSaber(ペンライト)を復元しました。");
    }
}
