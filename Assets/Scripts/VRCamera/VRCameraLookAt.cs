using UnityEngine;

[RequireComponent(typeof(Animator))]
public class VRCameraLookAt : MonoBehaviour
{
    private Animator anim;
    private Camera mainCam;

    [Tooltip("How strongly she looks at the camera (0 = not at all, 1 = fully)")]
    [Range(0f, 1f)]
    public float lookWeight = 0.8f;

    [Tooltip("How much the body turns (0 = only head/eyes, 1 = full body turn)")]
    [Range(0f, 1f)]
    public float bodyWeight = 0.1f;
    
    [Tooltip("How much the head turns")]
    [Range(0f, 1f)]
    public float headWeight = 0.8f;
    
    [Tooltip("How much the eyes turn")]
    [Range(0f, 1f)]
    public float eyesWeight = 1.0f;
    
    [Tooltip("Clamp weight (0 = unconstrained, 1 = constrained)")]
    [Range(0f, 1f)]
    public float clampWeight = 0.5f;

    void Start()
    {
        anim = GetComponent<Animator>();
        
        // Find the VR camera or Main Camera
        mainCam = Camera.main;
        if (mainCam == null)
        {
            var allCams = Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude);
            foreach (var c in allCams)
            {
                if (c.gameObject.name.Contains("Main Camera") || c.gameObject.name.Contains("VR"))
                {
                    mainCam = c;
                    break;
                }
            }
        }
    }

    void OnAnimatorIK(int layerIndex)
    {
        if (anim != null && mainCam != null && lookWeight > 0f)
        {
            // Only look if the camera is somewhat in front of her (not behind her)
            Vector3 camToHead = (mainCam.transform.position - transform.position).normalized;
            float dot = Vector3.Dot(transform.forward, camToHead);
            
            // If the camera is in front of her (dot > 0), look at it
            if (dot > 0f)
            {
                // Smoothly adjust weight based on how directly in front the camera is
                float currentWeight = lookWeight * Mathf.Clamp01(dot * 2f);
                
                anim.SetLookAtWeight(currentWeight, bodyWeight, headWeight, eyesWeight, clampWeight);
                anim.SetLookAtPosition(mainCam.transform.position);
            }
            else
            {
                anim.SetLookAtWeight(0f);
            }
        }
    }
}
