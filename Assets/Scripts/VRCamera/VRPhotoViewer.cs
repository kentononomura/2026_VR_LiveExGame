using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class VRPhotoViewer : MonoBehaviour
{
    [Header("Default Settings")]
    [SerializeField] private Texture2D noPhotoTexture;

    private Renderer screenRenderer;
    private List<Texture2D> photos;
    private int currentPhotoIndex = 0;

    private InputAction nextAction;
    private InputAction prevAction;

    private void Awake()
    {
        screenRenderer = GetComponent<Renderer>();
    }

    private void Start()
    {
        // Fade in when scene starts
        if (VRScreenFader.Instance != null)
        {
            VRScreenFader.Instance.FadeIn(1.0f, null);
        }

        photos = PhotoGalleryManager.GetPhotos();
        UpdateScreenTexture();
    }

    private bool isRightTriggerDown = false;
    private bool isLeftTriggerDown = false;

    private void OnEnable()
    {
        // Setup direct path bindings for triggers (reading as float axis)
        nextAction = new InputAction(
            name: "NextPhoto",
            type: InputActionType.Value,
            expectedControlType: "Axis",
            binding: "<XRController>{RightHand}/trigger"
        );
        nextAction.Enable();
        nextAction.performed += OnNextPressed;
        nextAction.canceled += OnNextPressed;

        prevAction = new InputAction(
            name: "PrevPhoto",
            type: InputActionType.Value,
            expectedControlType: "Axis",
            binding: "<XRController>{LeftHand}/trigger"
        );
        prevAction.Enable();
        prevAction.performed += OnPrevPressed;
        prevAction.canceled += OnPrevPressed;
    }

    private void OnDisable()
    {
        if (nextAction != null)
        {
            nextAction.performed -= OnNextPressed;
            nextAction.canceled -= OnNextPressed;
            nextAction.Disable();
        }

        if (prevAction != null)
        {
            prevAction.performed -= OnPrevPressed;
            prevAction.canceled -= OnPrevPressed;
            prevAction.Disable();
        }
    }

    private void OnNextPressed(InputAction.CallbackContext context)
    {
        float val = context.ReadValue<float>();
        if (val >= 0.8f)
        {
            if (!isRightTriggerDown)
            {
                isRightTriggerDown = true;
                if (photos == null || photos.Count <= 1) return;
                currentPhotoIndex = (currentPhotoIndex + 1) % photos.Count;
                UpdateScreenTexture();
            }
        }
        else if (val < 0.2f)
        {
            isRightTriggerDown = false;
        }
    }

    private void OnPrevPressed(InputAction.CallbackContext context)
    {
        float val = context.ReadValue<float>();
        if (val >= 0.8f)
        {
            if (!isLeftTriggerDown)
            {
                isLeftTriggerDown = true;
                if (photos == null || photos.Count <= 1) return;
                if (currentPhotoIndex == 0) return;
                currentPhotoIndex--;
                UpdateScreenTexture();
            }
        }
        else if (val < 0.2f)
        {
            isLeftTriggerDown = false;
        }
    }

    private void UpdateScreenTexture()
    {
        if (screenRenderer == null) return;

        Texture2D textureToApply = noPhotoTexture;

        if (photos != null && photos.Count > 0 && currentPhotoIndex < photos.Count)
        {
            var p = photos[currentPhotoIndex];
            if (p != null) textureToApply = p;
        }

        if (textureToApply != null)
        {
            // Apply to both URP standard (_BaseMap) and legacy shader (_MainTex) slots
            screenRenderer.material.SetTexture("_BaseMap", textureToApply);
            screenRenderer.material.SetTexture("_MainTex", textureToApply);
        }
    }
}
