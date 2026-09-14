using UnityEngine;

/// <summary>Persistent monitor position. Duplicating this object creates another display.</summary>
[DisallowMultipleComponent]
public sealed class VoiceBillboardPlacement : MonoBehaviour
{
    public VoiceCommandHUD source;
    private VoiceCommandHUD display;

    private void Start()
    {
        if (source != null) display = source.CreateReplica(transform);
    }

    private void OnDestroy()
    {
        if (display != null) Destroy(display.gameObject);
    }
}
