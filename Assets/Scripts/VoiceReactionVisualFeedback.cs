using System;
using System.Collections.Generic;
using UnityEngine;

public enum VoiceReactionEffectStyle
{
    Default,
    HeartAndSparkle,
    HeartOnly,
    SparkleOnly,
    Custom,
    None
}

[Serializable]
public sealed class VoiceCommandEffectSetting
{
    [Tooltip("KeywordReaction.commandId と一致させる論理コマンドIDです。")]
    public string commandId;

    [Tooltip("このコマンドで再生する演出です。Defaultなら共通設定を使用します。")]
    public VoiceReactionEffectStyle effectStyle = VoiceReactionEffectStyle.Default;

    [Tooltip("Effect StyleがCustomの場合に再生するParticle System Prefabです。")]
    public ParticleSystem customEffectPrefab;

    [Tooltip("共通設定とは異なる発生位置と大きさを使用します。")]
    public bool overridePlacement;

    public Vector3 localOffset = new Vector3(0f, 0.25f, 0.05f);

    [Min(0.01f)]
    public float scale = 1f;
}

/// <summary>
/// VoicePoint成立後のリアクション開始を、ユニティちゃん周辺の視覚演出で通知します。
/// 組み込みのハートとキラキラは実行時に一度だけ作成し、以後は再利用します。
/// </summary>
public sealed class VoiceReactionVisualFeedback : MonoBehaviour
{
    private const string HeartTextureResourcePath = "VoiceFeedback/heart-particle";
    private const string SparkleTextureResourcePath = "VoiceFeedback/sparkle-particle";
    private const string ParticleMaterialResourcePath = "VoiceFeedback/particle-material";

    [Header("Default Effect")]
    [SerializeField] private VoiceReactionEffectStyle defaultEffectStyle =
        VoiceReactionEffectStyle.HeartAndSparkle;

    [Tooltip("HumanoidのUpperChestまたはChestを基準にした発生位置です。")]
    [SerializeField] private Vector3 defaultLocalOffset = new Vector3(0f, 0.25f, 0.05f);

    [Min(0.01f)]
    [SerializeField] private float defaultScale = 1f;

    [Header("Per Command Overrides")]
    [SerializeField] private List<VoiceCommandEffectSetting> commandEffects =
        new List<VoiceCommandEffectSetting>
        {
            new VoiceCommandEffectSetting { commandId = "LookAt" },
            new VoiceCommandEffectSetting { commandId = "Wave" },
            new VoiceCommandEffectSetting { commandId = "Cute" },
            new VoiceCommandEffectSetting { commandId = "UnityChanCall" }
        };

    private ParticleSystem heartParticles;
    private ParticleSystem sparkleParticles;
    private Material heartMaterial;
    private Material sparkleMaterial;
    private readonly Dictionary<ParticleSystem, ParticleSystem> customInstances =
        new Dictionary<ParticleSystem, ParticleSystem>();

    public void Play(string commandId, Animator characterAnimator, Transform characterRoot)
    {
        VoiceCommandEffectSetting setting = FindSetting(commandId);
        VoiceReactionEffectStyle style = setting != null
            ? setting.effectStyle
            : VoiceReactionEffectStyle.Default;
        if (style == VoiceReactionEffectStyle.Default) style = defaultEffectStyle;
        if (style == VoiceReactionEffectStyle.None) return;

        Transform anchor = ResolveAnchor(characterAnimator, characterRoot);
        if (anchor == null)
        {
            Debug.LogWarning("[VoiceFeedback] エフェクトの発生基準となるTransformを取得できません。");
            return;
        }

        Vector3 localOffset = setting != null && setting.overridePlacement
            ? setting.localOffset
            : defaultLocalOffset;
        float effectScale = setting != null && setting.overridePlacement
            ? Mathf.Max(0.01f, setting.scale)
            : Mathf.Max(0.01f, defaultScale);
        Vector3 worldPosition = anchor.position +
            (characterRoot != null ? characterRoot.TransformVector(localOffset) : localOffset);

        if (style == VoiceReactionEffectStyle.Custom)
        {
            PlayCustom(setting, worldPosition, effectScale);
            return;
        }

        EnsureBuiltInEffects();
        if (style == VoiceReactionEffectStyle.HeartAndSparkle ||
            style == VoiceReactionEffectStyle.HeartOnly)
        {
            RestartParticles(heartParticles, worldPosition, effectScale);
        }

        if (style == VoiceReactionEffectStyle.HeartAndSparkle ||
            style == VoiceReactionEffectStyle.SparkleOnly)
        {
            RestartParticles(sparkleParticles, worldPosition, effectScale);
        }
    }

    private VoiceCommandEffectSetting FindSetting(string commandId)
    {
        if (string.IsNullOrWhiteSpace(commandId) || commandEffects == null) return null;

        foreach (VoiceCommandEffectSetting setting in commandEffects)
        {
            if (setting != null && string.Equals(
                    setting.commandId,
                    commandId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return setting;
            }
        }

        return null;
    }

    private static Transform ResolveAnchor(Animator animator, Transform fallback)
    {
        if (animator != null && animator.isHuman)
        {
            Transform anchor = animator.GetBoneTransform(HumanBodyBones.UpperChest);
            if (anchor == null) anchor = animator.GetBoneTransform(HumanBodyBones.Chest);
            if (anchor == null) anchor = animator.GetBoneTransform(HumanBodyBones.Head);
            if (anchor != null) return anchor;
        }

        return fallback;
    }

    private void EnsureBuiltInEffects()
    {
        if (heartParticles == null)
        {
            Texture2D texture = Resources.Load<Texture2D>(HeartTextureResourcePath);
            heartMaterial = CreateParticleMaterial(texture, "VoiceFeedback Heart Material");
            heartParticles = CreateHeartParticles(heartMaterial);
        }

        if (sparkleParticles == null)
        {
            Texture2D texture = Resources.Load<Texture2D>(SparkleTextureResourcePath);
            sparkleMaterial = CreateParticleMaterial(texture, "VoiceFeedback Sparkle Material");
            sparkleParticles = CreateSparkleParticles(sparkleMaterial);
        }
    }

    private Material CreateParticleMaterial(Texture2D texture, string materialName)
    {
        if (texture == null)
        {
            Debug.LogWarning($"[VoiceFeedback] Particle textureをResourcesから読み込めません: {materialName}");
        }

        Material template = Resources.Load<Material>(ParticleMaterialResourcePath);
        Material material;
        if (template != null)
        {
            material = new Material(template);
        }
        else
        {
            Shader fallbackShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (fallbackShader == null) fallbackShader = Shader.Find("Sprites/Default");
            if (fallbackShader == null)
            {
                Debug.LogError("[VoiceFeedback] Particle Shaderを取得できません。");
                return null;
            }

            Debug.LogWarning("[VoiceFeedback] Particle MaterialをResourcesから読み込めないため、フォールバックShaderを使用します。");
            material = new Material(fallbackShader);
        }

        material.name = materialName;
        material.mainTexture = texture;
        if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
        return material;
    }

    private ParticleSystem CreateHeartParticles(Material material)
    {
        ParticleSystem particles = CreateParticleSystem("VoiceFeedback Hearts", material);
        ParticleSystem.MainModule main = particles.main;
        main.duration = 1.2f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.15f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.09f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.09f, 0.15f);
        main.startRotation = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)7) });

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.18f;
        shape.radiusThickness = 1f;

        ParticleSystem.VelocityOverLifetimeModule velocity = particles.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f);
        velocity.y = new ParticleSystem.MinMaxCurve(0.22f, 0.38f);
        velocity.z = new ParticleSystem.MinMaxCurve(-0.04f, 0.04f);

        ConfigureFadeAndSize(particles, 0.55f);
        return particles;
    }

    private ParticleSystem CreateSparkleParticles(Material material)
    {
        ParticleSystem particles = CreateParticleSystem("VoiceFeedback Sparkles", material);
        ParticleSystem.MainModule main = particles.main;
        main.duration = 0.9f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.75f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.18f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)12) });

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.28f;
        shape.radiusThickness = 1f;

        ParticleSystem.RotationOverLifetimeModule rotation = particles.rotationOverLifetime;
        rotation.enabled = true;
        rotation.z = new ParticleSystem.MinMaxCurve(-1.5f, 1.5f);

        ConfigureFadeAndSize(particles, 0.35f);
        return particles;
    }

    private ParticleSystem CreateParticleSystem(string objectName, Material material)
    {
        GameObject effectObject = new GameObject(objectName);
        effectObject.transform.SetParent(transform, false);

        ParticleSystem particles = effectObject.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = particles.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = 24;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = 0f;

        ParticleSystemRenderer particleRenderer = particles.GetComponent<ParticleSystemRenderer>();
        particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        particleRenderer.alignment = ParticleSystemRenderSpace.View;
        particleRenderer.sortMode = ParticleSystemSortMode.YoungestInFront;
        particleRenderer.material = material;
        particleRenderer.maxParticleSize = 0.25f;

        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        return particles;
    }

    private static void ConfigureFadeAndSize(ParticleSystem particles, float peakTime)
    {
        Gradient colorGradient = new Gradient();
        colorGradient.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f)
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.08f),
                new GradientAlphaKey(1f, 0.65f),
                new GradientAlphaKey(0f, 1f)
            });
        ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
        color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(colorGradient);

        AnimationCurve sizeCurve = new AnimationCurve(
            new Keyframe(0f, 0.2f),
            new Keyframe(peakTime, 1f),
            new Keyframe(1f, 0.15f));
        ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);
    }

    private static void RestartParticles(ParticleSystem particles, Vector3 position, float scale)
    {
        if (particles == null) return;

        Transform effectTransform = particles.transform;
        effectTransform.position = position;
        effectTransform.rotation = Quaternion.identity;
        effectTransform.localScale = Vector3.one * scale;
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        particles.Play(true);
    }

    private void PlayCustom(VoiceCommandEffectSetting setting, Vector3 position, float scale)
    {
        if (setting == null || setting.customEffectPrefab == null)
        {
            Debug.LogWarning("[VoiceFeedback] Custom演出が選択されていますが、Prefabが設定されていません。");
            return;
        }

        if (!customInstances.TryGetValue(setting.customEffectPrefab, out ParticleSystem instance) ||
            instance == null)
        {
            instance = Instantiate(setting.customEffectPrefab, transform);
            instance.name = $"VoiceFeedback Custom ({setting.commandId})";
            customInstances[setting.customEffectPrefab] = instance;
        }

        RestartParticles(instance, position, scale);
    }

    private void OnDestroy()
    {
        if (heartMaterial != null) Destroy(heartMaterial);
        if (sparkleMaterial != null) Destroy(sparkleMaterial);
    }
}
