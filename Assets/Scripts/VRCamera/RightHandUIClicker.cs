using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// XR Interaction Toolkitのバージョン差異を吸収するためにリフレクションを使用するか、
// コンポーネントを直接取得して処理します。
public class RightHandUIClicker : MonoBehaviour
{
#if ENABLE_INPUT_SYSTEM
    [Tooltip("UIをクリックするための入力アクション（デフォルトは右手Aボタン）")]
    public InputAction clickAction = new InputAction("Click", InputActionType.Button, "<XRController>{RightHand}/primaryButton");

    void OnEnable()
    {
        clickAction.Enable();
        clickAction.performed += OnClickPerformed;
    }

    void OnDisable()
    {
        clickAction.performed -= OnClickPerformed;
        clickAction.Disable();
    }

    private void OnClickPerformed(InputAction.CallbackContext ctx)
    {
        // XRRayInteractorコンポーネントを取得
        UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor rayInteractor = null;
        var comps = GetComponents<MonoBehaviour>();
        foreach (var comp in comps)
        {
            if (comp != null && comp.GetType().Name == "XRRayInteractor")
            {
                rayInteractor = comp as UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor;
                break;
            }
        }

        if (rayInteractor == null) return;

        // TryGetCurrentUIRaycastResult を直接呼び出す
        if (rayInteractor.TryGetCurrentUIRaycastResult(out RaycastResult result))
        {
            if (result.gameObject != null)
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
#endif
}
