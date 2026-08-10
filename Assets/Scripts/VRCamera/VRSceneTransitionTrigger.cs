using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class VRSceneTransitionTrigger : MonoBehaviour
{
    [SerializeField] private string targetSceneName = "VRPhotoResultTest";
    [SerializeField] private float fadeDuration = 1.0f;

    private InputAction xButtonAction;
    private bool isTransitioning = false;

    private void OnEnable()
    {
        // Setup direct path binding for Left Hand X button (primaryButton)
        xButtonAction = new InputAction(
            name: "LeftHandXButton",
            type: InputActionType.Button,
            binding: "<XRController>{LeftHand}/primaryButton"
        );
        xButtonAction.Enable();
        xButtonAction.performed += OnXButtonPressed;
    }

    private void OnDisable()
    {
        if (xButtonAction != null)
        {
            xButtonAction.performed -= OnXButtonPressed;
            xButtonAction.Disable();
        }
    }

    private void OnXButtonPressed(InputAction.CallbackContext context)
    {
        if (isTransitioning) return;
        isTransitioning = true;

        if (VRScreenFader.Instance != null)
        {
            VRScreenFader.Instance.FadeOut(fadeDuration, () =>
            {
                SceneManager.LoadScene(targetSceneName);
            });
        }
        else
        {
            // Fallback if fader is missing in scene
            SceneManager.LoadScene(targetSceneName);
        }
    }
}
