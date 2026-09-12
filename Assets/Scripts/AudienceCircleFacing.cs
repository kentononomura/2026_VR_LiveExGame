using UnityEngine;

/// <summary>Sets audience height, faces the stage, and synchronizes VAT animation to music.</summary>
[ExecuteAlways]
[RequireComponent(typeof(ParticleSystem))]
public sealed class AudienceCircleFacing : MonoBehaviour
{
    [Tooltip("観客の身長（m）。配置位置や列間隔は変えません。")]
    [Min(0.1f)] public float heightMeters = 1.7f;
    [Tooltip("観客の動作速度。1で従来の速度、1.25で25%速くなります。再生中も調整できます。")]
    [Range(0.1f, 4f)] public float animationSpeedMultiplier = 1.25f;
    [Tooltip("動作を変える平均間隔（秒）。観客ごとに±25%ずらします。")]
    [Min(1f)] public float motionInterval = 8f;
    [Tooltip("次の動作へ滑らかに切り替える時間（秒）。")]
    [Min(0.05f)] public float motionTransition = 1f;
    // AudienceB's undeformed submesh height; the overall bounds include animation motion.
    private const float ModelHeight = 1.455647f;
    private ParticleSystem particles;
    private ParticleSystem.Particle[] buffer;
    private StageDirector director;
    private ParticleSystemRenderer audienceRenderer;
    private Material originalMaterial;
    private Material playbackMaterial;
    private double animationTime;
    private float previousMusicSeconds;

    private void OnEnable()
    {
        particles = GetComponent<ParticleSystem>();
        animationTime = 0.0;
        previousMusicSeconds = 0f;
        if (Application.isPlaying)
        {
            director = FindAnyObjectByType<StageDirector>();
            audienceRenderer = GetComponent<ParticleSystemRenderer>();
            originalMaterial = audienceRenderer != null ? audienceRenderer.sharedMaterial : null;
            if (originalMaterial != null && originalMaterial.HasProperty("_ManualTime"))
            {
                playbackMaterial = new Material(originalMaterial);
                Shader mixedShader = Resources.Load<Shader>("TestSceneAudience/MixedAudience");
                if (mixedShader != null) playbackMaterial.shader = mixedShader;
                playbackMaterial.EnableKeyword("_TIMEUPDATEMODE_MANUAL");
                playbackMaterial.SetFloat("_TimeUpdateMode", 1f);
                playbackMaterial.SetFloat("_ManualTime", 0f);
                SetIndividualMotionTime(0f);
                for (int i = 1; i <= 4; i++) playbackMaterial.SetFloat("_Blend" + i, 0f);
                audienceRenderer.sharedMaterial = playbackMaterial;
            }
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.update += RefreshEditor;
#endif
    }

    private void OnDisable()
    {
        if (playbackMaterial != null)
        {
            if (audienceRenderer != null) audienceRenderer.sharedMaterial = originalMaterial;
            Destroy(playbackMaterial);
            playbackMaterial = null;
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.update -= RefreshEditor;
#endif
    }

#if UNITY_EDITOR
    private void RefreshEditor()
    {
        if (!Application.isPlaying) RefreshFacing();
    }
#endif

    private void LateUpdate()
    {
        RefreshFacing();
        if (Application.isPlaying && playbackMaterial != null && director != null &&
            director.TryGetAudienceMusicTime(out float seconds))
        {
            // Integrate audio time so changing speed does not jump to a different pose.
            // A backward seek/restart establishes a new phase from the audio position.
            float speed = playbackMaterial.GetFloat("_Speed") * Mathf.Clamp(animationSpeedMultiplier, 0.1f, 4f);
            if (seconds < previousMusicSeconds) animationTime = seconds * speed;
            else animationTime += (seconds - previousMusicSeconds) * speed;
            previousMusicSeconds = seconds;
            playbackMaterial.SetFloat("_ManualTime", (float)animationTime);
            playbackMaterial.SetFloat("_Blend1", Mathf.Clamp01(seconds / 0.3f));
            SetIndividualMotionTime(seconds);
        }
    }

    private void SetIndividualMotionTime(float seconds)
    {
        if (!playbackMaterial.HasProperty("_AudienceSeconds")) return;
        playbackMaterial.SetFloat("_AudienceSeconds", seconds);
        playbackMaterial.SetFloat("_MotionInterval", Mathf.Max(1f, motionInterval));
        playbackMaterial.SetFloat("_MotionTransition", Mathf.Max(0.05f, motionTransition));
    }

    public void RefreshFacing()
    {
        if (particles == null) particles = GetComponent<ParticleSystem>();
        if (particles == null || particles.main.simulationSpace != ParticleSystemSimulationSpace.Local) return;
        int count = particles.particleCount;
        if (count == 0) return;
        if (buffer == null || buffer.Length < count) buffer = new ParticleSystem.Particle[count];
        count = particles.GetParticles(buffer);
        bool changed = false;
        float size = Mathf.Max(0.1f, heightMeters) / ModelHeight;
        for (int i = 0; i < count; i++)
        {
            if ((buffer[i].startSize3D - Vector3.one * size).sqrMagnitude > 0.000001f)
            {
                buffer[i].startSize3D = Vector3.one * size;
                changed = true;
            }
            Vector3 position = buffer[i].position;
            if (position.x * position.x + position.z * position.z < 0.000001f) continue;
            float yaw = Mathf.Atan2(-position.x, -position.z) * Mathf.Rad2Deg;
            Vector3 current = buffer[i].rotation3D;
            if (Mathf.Abs(Mathf.DeltaAngle(current.y, yaw)) < 0.01f &&
                Mathf.Abs(Mathf.DeltaAngle(current.x, 0)) < 0.01f &&
                Mathf.Abs(Mathf.DeltaAngle(current.z, 0)) < 0.01f) continue;
            buffer[i].rotation3D = new Vector3(0f, yaw, 0f);
            changed = true;
        }
        if (changed) particles.SetParticles(buffer, count);
    }
}
