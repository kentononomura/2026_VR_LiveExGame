using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TestScene専用のペンライト応援演出と、ハート到達後の一回限りの倍率を管理します。
/// </summary>
public sealed class PenlightHeartFeature : MonoBehaviour
{
    public const string FlyingHeartLayerName = "PenlightHeart";

    private const string HeartSpriteResourcePath = "VoiceFeedback/heart-particle";
    private const string SparkleTextureResourcePath = "VoiceFeedback/sparkle-particle";
    private const string ParticleMaterialResourcePath = "VoiceFeedback/particle-material";

    [Header("References (Optional)")]
    [SerializeField] private PenlightGaugeController leftPenlight;
    [SerializeField] private Transform penlightTipAnchor;
    [SerializeField] private Animator characterAnimator;
    [SerializeField] private Transform characterRoot;
    [SerializeField] private Vector3 chestLocalOffset = new Vector3(0f, 0.08f, 0.05f);

    [Header("Swing Activity")]
    [Tooltip("最後の有効ゲージ加算後も、振りが継続中とみなす時間です。")]
    [Min(0.05f)]
    [SerializeField] private float swingActivityGrace = 0.30f;

    [Tooltip("振り続けている間にハートを生成する間隔です。")]
    [Min(0.05f)]
    [SerializeField] private float heartSpawnInterval = 0.45f;

    [Header("Penlight Trail")]
    [Min(0.01f)]
    [SerializeField] private float trailTime = 0.20f;
    [Min(0.001f)]
    [SerializeField] private float trailStartWidth = 0.035f;
    [Min(0.001f)]
    [SerializeField] private float trailEndWidth = 0.008f;

    [Header("Flying Heart")]
    [Min(0.1f)]
    [SerializeField] private float heartFlightDuration = 0.80f;
    [Min(0.01f)]
    [SerializeField] private float heartSize = 0.16f;
    [Min(0f)]
    [SerializeField] private float heartArcHeight = 0.35f;
    [Min(0f)]
    [SerializeField] private float heartArcSide = 0.12f;
    [Range(1, 24)]
    [SerializeField] private int maxConcurrentHearts = 8;

    [Header("Heart Bonus")]
    [Min(1f)]
    [SerializeField] private float heartBonusMultiplier = 1.5f;
    [Min(0.1f)]
    [SerializeField] private float heartBonusDuration = 3f;

    [Header("Debug")]
    [SerializeField] private bool enableDebugLog;

    private sealed class FlyingHeart
    {
        public GameObject GameObject;
        public Transform Transform;
        public SpriteRenderer Renderer;
        public bool Active;
        public Vector3 StartPosition;
        public float Elapsed;
        public float SideSign;
    }

    private readonly List<FlyingHeart> heartPool = new List<FlyingHeart>();
    private Transform resolvedTipAnchor;
    private Transform resolvedChestAnchor;
    private GameObject visualRoot;
    private TrailRenderer trail;
    private ParticleSystem penlightSparkles;
    private ParticleSystem arrivalParticles;
    private Sprite heartSprite;
    private Material trailMaterial;
    private Material sparkleMaterial;
    private float lastValidSwingTime = float.NegativeInfinity;
    private float nextHeartSpawnTime;
    private bool ownsTipAnchor;
    private bool ownsTrail;
    private bool swingVisualsActive;
    private bool heartBonusActive;
    private float heartBonusExpiresAt;
    private bool isSubscribed;

    public bool IsHeartBonusActive =>
        heartBonusActive && Time.time <= heartBonusExpiresAt;

    public float HeartBonusRemaining => IsHeartBonusActive
        ? Mathf.Max(0f, heartBonusExpiresAt - Time.time)
        : 0f;

    public void Configure(
        PenlightGaugeController requestedPenlight,
        Animator requestedAnimator,
        Transform requestedCharacterRoot)
    {
        if (leftPenlight != requestedPenlight)
        {
            UnsubscribeFromPenlight();
            DestroyOwnedTipVisuals();
            leftPenlight = requestedPenlight;
            resolvedTipAnchor = null;
        }

        if (characterAnimator != requestedAnimator || characterRoot != requestedCharacterRoot)
        {
            characterAnimator = requestedAnimator;
            characterRoot = requestedCharacterRoot;
            resolvedChestAnchor = null;
        }

        SubscribeToPenlight();
        EnsureVisuals();
    }

    public bool TryConsumeHeartBonus(out float multiplier)
    {
        ExpireHeartBonusIfNeeded();
        if (!heartBonusActive)
        {
            multiplier = 1f;
            return false;
        }

        multiplier = Mathf.Max(1f, heartBonusMultiplier);
        heartBonusActive = false;

        if (enableDebugLog)
        {
            Debug.Log($"[PenlightHeart] ハートボーナスを消費しました: x{multiplier:F1}");
        }
        return true;
    }

    private void OnValidate()
    {
        swingActivityGrace = Mathf.Max(0.05f, swingActivityGrace);
        heartSpawnInterval = Mathf.Max(0.05f, heartSpawnInterval);
        trailTime = Mathf.Max(0.01f, trailTime);
        trailStartWidth = Mathf.Max(0.001f, trailStartWidth);
        trailEndWidth = Mathf.Max(0.001f, trailEndWidth);
        heartFlightDuration = Mathf.Max(0.1f, heartFlightDuration);
        heartSize = Mathf.Max(0.01f, heartSize);
        maxConcurrentHearts = Mathf.Clamp(maxConcurrentHearts, 1, 24);
        heartBonusMultiplier = Mathf.Max(1f, heartBonusMultiplier);
        heartBonusDuration = Mathf.Max(0.1f, heartBonusDuration);
    }

    private void OnEnable()
    {
        SubscribeToPenlight();
    }

    private void OnDisable()
    {
        UnsubscribeFromPenlight();
        SetSwingVisuals(false);
        DeactivateAllHearts();
        heartBonusActive = false;
    }

    private void Update()
    {
        if (leftPenlight != null && resolvedTipAnchor == null)
        {
            EnsureVisuals();
        }

        bool swingActive =
            leftPenlight != null &&
            Time.time - lastValidSwingTime <= swingActivityGrace;

        if (swingActive)
        {
            SetSwingVisuals(true);
            UpdateVisualColor();
            if (Time.time >= nextHeartSpawnTime)
            {
                SpawnHeart();
                nextHeartSpawnTime = Time.time + heartSpawnInterval;
            }
        }
        else
        {
            SetSwingVisuals(false);
        }

        UpdateFlyingHearts();
        ExpireHeartBonusIfNeeded();
    }

    private void OnValidSwingGaugeAdded(float amount)
    {
        bool wasActive = Time.time - lastValidSwingTime <= swingActivityGrace;
        lastValidSwingTime = Time.time;

        if (!wasActive)
        {
            EnsureVisuals();
            SetSwingVisuals(true);
            UpdateVisualColor();
            if (Time.time >= nextHeartSpawnTime)
            {
                SpawnHeart();
                nextHeartSpawnTime = Time.time + heartSpawnInterval;
            }
        }
    }

    private void SubscribeToPenlight()
    {
        if (!isActiveAndEnabled || isSubscribed || leftPenlight == null) return;
        leftPenlight.ValidSwingGaugeAdded += OnValidSwingGaugeAdded;
        isSubscribed = true;
    }

    private void UnsubscribeFromPenlight()
    {
        if (!isSubscribed) return;
        if (leftPenlight != null)
        {
            leftPenlight.ValidSwingGaugeAdded -= OnValidSwingGaugeAdded;
        }
        isSubscribed = false;
    }

    private void EnsureVisuals()
    {
        if (leftPenlight == null) return;

        if (visualRoot == null)
        {
            visualRoot = new GameObject("Penlight Heart Feature Visuals");
            visualRoot.transform.SetParent(transform, false);
        }

        ResolveTipAnchor();
        ResolveChestAnchor();

        if (heartSprite == null)
        {
            heartSprite = Resources.Load<Sprite>(HeartSpriteResourcePath);
            if (heartSprite == null)
            {
                Debug.LogWarning("[PenlightHeart] ハートSpriteを読み込めないため、飛行ハートを表示できません。");
            }
        }

        if (trail == null && resolvedTipAnchor != null)
        {
            trail = resolvedTipAnchor.gameObject.GetComponent<TrailRenderer>();
            if (trail == null)
            {
                trail = resolvedTipAnchor.gameObject.AddComponent<TrailRenderer>();
                ownsTrail = true;
            }
            trail.time = trailTime;
            trail.startWidth = trailStartWidth;
            trail.endWidth = trailEndWidth;
            trail.minVertexDistance = 0.01f;
            trail.alignment = LineAlignment.View;
            trail.textureMode = LineTextureMode.Stretch;
            trail.emitting = false;
            if (trailMaterial == null) trailMaterial = CreateTrailMaterial();
            if (trailMaterial != null) trail.material = trailMaterial;
            trail.Clear();
        }

        if (penlightSparkles == null && resolvedTipAnchor != null)
        {
            penlightSparkles = CreateSparkleSystem(
                "Penlight Swing Sparkles",
                resolvedTipAnchor,
                true,
                21f,
                32);
        }

        if (arrivalParticles == null)
        {
            arrivalParticles = CreateSparkleSystem(
                "Penlight Heart Arrival",
                visualRoot.transform,
                false,
                0f,
                24);
        }

        while (heartPool.Count < maxConcurrentHearts && heartSprite != null)
        {
            heartPool.Add(CreateFlyingHeart(heartPool.Count));
        }
    }

    private void ResolveTipAnchor()
    {
        if (penlightTipAnchor != null)
        {
            resolvedTipAnchor = penlightTipAnchor;
            return;
        }
        if (resolvedTipAnchor != null || leftPenlight == null) return;

        // Saber.Start がモデルを生成するまで待つ。先に固定位置を確定しない。
        Transform visual = leftPenlight.transform.Find("SaberVisual");
        if (visual == null) return;

        Transform anchorParent = visual;
        Vector3 anchorPosition = Vector3.zero;
        Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
        bool foundLight = false;
        foreach (Renderer renderer in renderers)
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == null ||
                    materials[i].name.IndexOf("light", System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                // 発光部と持ち手が同じMeshでも、Lightマテリアルの範囲だけを使う。
                Bounds lightBounds = renderer.localBounds;
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh :
                    (renderer as SkinnedMeshRenderer)?.sharedMesh;
                if (mesh != null && i < mesh.subMeshCount)
                {
                    Bounds subMeshBounds = mesh.GetSubMesh(i).bounds;
                    if (subMeshBounds.size.sqrMagnitude > 0f)
                        lightBounds = subMeshBounds;
                }

                anchorParent = renderer.transform;
                anchorPosition = lightBounds.center;
                foundLight = true;
                break;
            }
            if (foundLight) break;
        }

        // 標準Cylinderや発光マテリアルがないモデルも、実際の表示位置から出す。
        if (!foundLight && renderers.Length > 0)
        {
            anchorParent = renderers[0].transform;
            anchorPosition = renderers[0].localBounds.center;
        }

        GameObject tipObject = new GameObject("Penlight Effect Tip");
        resolvedTipAnchor = tipObject.transform;
        ownsTipAnchor = true;
        resolvedTipAnchor.SetParent(anchorParent, false);
        resolvedTipAnchor.localPosition = anchorPosition;
        resolvedTipAnchor.localRotation = Quaternion.identity;
    }

    private void ResolveChestAnchor()
    {
        if (resolvedChestAnchor != null) return;
        if (characterAnimator != null && characterAnimator.isHuman)
        {
            resolvedChestAnchor = characterAnimator.GetBoneTransform(HumanBodyBones.UpperChest);
            if (resolvedChestAnchor == null)
            {
                resolvedChestAnchor = characterAnimator.GetBoneTransform(HumanBodyBones.Chest);
            }
            if (resolvedChestAnchor == null)
            {
                resolvedChestAnchor = characterAnimator.GetBoneTransform(HumanBodyBones.Head);
            }
        }
        if (resolvedChestAnchor == null) resolvedChestAnchor = characterRoot;
    }

    private Vector3 GetTargetPosition()
    {
        ResolveChestAnchor();
        if (resolvedChestAnchor == null) return Vector3.zero;

        Transform offsetSpace = characterRoot != null ? characterRoot : resolvedChestAnchor;
        return resolvedChestAnchor.position + offsetSpace.TransformVector(chestLocalOffset);
    }

    private bool HasTarget()
    {
        ResolveChestAnchor();
        return resolvedChestAnchor != null;
    }

    private void SetSwingVisuals(bool active)
    {
        if (swingVisualsActive == active) return;
        swingVisualsActive = active;

        if (trail != null)
        {
            trail.time = trailTime;
            trail.startWidth = trailStartWidth;
            trail.endWidth = trailEndWidth;
            trail.emitting = active;
            if (active) trail.Clear();
        }

        if (penlightSparkles != null)
        {
            if (active) penlightSparkles.Play(true);
            else penlightSparkles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }

    private void UpdateVisualColor()
    {
        if (leftPenlight == null) return;
        Color color = leftPenlight.CurrentColor;

        if (trail != null)
        {
            trail.startColor = new Color(color.r, color.g, color.b, 0.95f);
            trail.endColor = new Color(color.r, color.g, color.b, 0f);
        }
        if (penlightSparkles != null)
        {
            ParticleSystem.MainModule main = penlightSparkles.main;
            main.startColor = new ParticleSystem.MinMaxGradient(color, Color.white);
        }
    }

    private void SpawnHeart()
    {
        EnsureVisuals();
        if (resolvedTipAnchor == null || !HasTarget() || heartSprite == null) return;

        FlyingHeart heart = null;
        foreach (FlyingHeart candidate in heartPool)
        {
            if (!candidate.Active)
            {
                heart = candidate;
                break;
            }
        }
        if (heart == null) return;

        heart.Active = true;
        heart.Elapsed = 0f;
        heart.StartPosition = resolvedTipAnchor.position;
        heart.SideSign = Random.value < 0.5f ? -1f : 1f;
        // 元画像が通常のピンク色を持つため、レベル色を掛けず白Tintでそのまま表示する。
        heart.Renderer.color = Color.white;
        heart.Transform.position = heart.StartPosition;
        heart.Transform.localScale = Vector3.one * GetHeartSpriteScale(heartSize);
        heart.GameObject.SetActive(true);
    }

    private FlyingHeart CreateFlyingHeart(int index)
    {
        GameObject heartObject = new GameObject($"Flying Penlight Heart {index + 1}");
        int heartLayer = LayerMask.NameToLayer(FlyingHeartLayerName);
        if (heartLayer >= 0) heartObject.layer = heartLayer;
        heartObject.transform.SetParent(visualRoot.transform, false);
        SpriteRenderer renderer = heartObject.AddComponent<SpriteRenderer>();
        renderer.sprite = heartSprite;
        renderer.sortingOrder = 15;
        heartObject.SetActive(false);

        return new FlyingHeart
        {
            GameObject = heartObject,
            Transform = heartObject.transform,
            Renderer = renderer
        };
    }

    private void UpdateFlyingHearts()
    {
        Camera targetCamera = Camera.main;
        foreach (FlyingHeart heart in heartPool)
        {
            if (!heart.Active) continue;
            if (!HasTarget())
            {
                DeactivateHeart(heart);
                continue;
            }

            heart.Elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(heart.Elapsed / Mathf.Max(0.1f, heartFlightDuration));
            float easedProgress = progress * progress * (3f - 2f * progress);
            Vector3 target = GetTargetPosition();
            Vector3 cameraRight = targetCamera != null ? targetCamera.transform.right : Vector3.right;
            Vector3 control = (heart.StartPosition + target) * 0.5f +
                Vector3.up * heartArcHeight +
                cameraRight * heartArcSide * heart.SideSign;
            float inverse = 1f - easedProgress;
            heart.Transform.position =
                inverse * inverse * heart.StartPosition +
                2f * inverse * easedProgress * control +
                easedProgress * easedProgress * target;

            if (targetCamera != null)
            {
                heart.Transform.rotation = targetCamera.transform.rotation;
            }
            float pulse = 1f + Mathf.Sin(heart.Elapsed * 11f) * 0.08f;
            heart.Transform.localScale =
                Vector3.one * GetHeartSpriteScale(heartSize) * pulse;

            if (progress >= 1f)
            {
                ArriveHeart(heart, target);
            }
        }
    }

    private void ArriveHeart(FlyingHeart heart, Vector3 position)
    {
        Color color = heart.Renderer.color;
        DeactivateHeart(heart);
        ActivateHeartBonus();

        if (arrivalParticles != null)
        {
            arrivalParticles.transform.position = position;
            ParticleSystem.MainModule main = arrivalParticles.main;
            main.startColor = new ParticleSystem.MinMaxGradient(color, Color.white);
            arrivalParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            arrivalParticles.Play(true);
        }
    }

    private void ActivateHeartBonus()
    {
        heartBonusActive = true;
        heartBonusExpiresAt = Time.time + heartBonusDuration;
        if (enableDebugLog)
        {
            Debug.Log($"[PenlightHeart] ハート到達。ボーナス x{heartBonusMultiplier:F1} を {heartBonusDuration:F1}秒有効化しました。");
        }
    }

    private void ExpireHeartBonusIfNeeded()
    {
        if (!heartBonusActive || Time.time <= heartBonusExpiresAt) return;
        heartBonusActive = false;
        if (enableDebugLog) Debug.Log("[PenlightHeart] ハートボーナスが時間切れになりました。");
    }

    private float GetHeartSpriteScale(float requestedWorldSize)
    {
        if (heartSprite == null) return requestedWorldSize;
        Vector2 spriteSize = heartSprite.bounds.size;
        float largestSide = Mathf.Max(spriteSize.x, spriteSize.y);
        return largestSide > 0.0001f
            ? requestedWorldSize / largestSide
            : requestedWorldSize;
    }

    private ParticleSystem CreateSparkleSystem(
        string objectName,
        Transform parent,
        bool loop,
        float rate,
        int maxParticles)
    {
        GameObject particleObject = new GameObject(objectName);
        particleObject.transform.SetParent(parent, false);
        ParticleSystem particles = particleObject.AddComponent<ParticleSystem>();
        // AddComponent starts playback on an active object; stop before changing duration.
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = particles.main;
        main.loop = loop;
        main.playOnAwake = false;
        main.duration = 0.6f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.20f, 0.45f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.03f, 0.16f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.07f);
        main.maxParticles = maxParticles;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        // 発光Meshの子に置いても、モデルの拡縮で粒子の大きさを変えない。
        main.scalingMode = ParticleSystemScalingMode.Local;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = rate;
        if (!loop)
        {
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)10) });
        }

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = loop ? 0.025f : 0.12f;

        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = particles.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f)
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.15f),
                new GradientAlphaKey(0f, 1f)
            });
        colorOverLifetime.color = gradient;

        ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.alignment = ParticleSystemRenderSpace.View;
        if (sparkleMaterial == null) sparkleMaterial = CreateParticleMaterial();
        if (sparkleMaterial != null) renderer.material = sparkleMaterial;

        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        return particles;
    }

    private Material CreateParticleMaterial()
    {
        Material template = Resources.Load<Material>(ParticleMaterialResourcePath);
        Material material = template != null ? new Material(template) : null;
        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) return null;
            material = new Material(shader);
        }

        Texture2D sparkleTexture = Resources.Load<Texture2D>(SparkleTextureResourcePath);
        material.name = "Penlight Heart Sparkle Material";
        material.mainTexture = sparkleTexture;
        if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", sparkleTexture);
        return material;
    }

    private static Material CreateTrailMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader == null) return null;

        Material material = new Material(shader)
        {
            name = "Penlight Heart Trail Material"
        };
        return material;
    }

    private static void DeactivateHeart(FlyingHeart heart)
    {
        heart.Active = false;
        heart.GameObject.SetActive(false);
    }

    private void DeactivateAllHearts()
    {
        foreach (FlyingHeart heart in heartPool)
        {
            if (heart.Active) DeactivateHeart(heart);
        }
    }

    private void DestroyOwnedTipVisuals()
    {
        if (ownsTipAnchor && resolvedTipAnchor != null)
        {
            Destroy(resolvedTipAnchor.gameObject);
        }
        else
        {
            if (ownsTrail && trail != null) Destroy(trail);
            if (penlightSparkles != null) Destroy(penlightSparkles.gameObject);
        }

        ownsTipAnchor = false;
        ownsTrail = false;
        resolvedTipAnchor = null;
        trail = null;
        penlightSparkles = null;
    }

    private void OnDestroy()
    {
        UnsubscribeFromPenlight();
        DestroyOwnedTipVisuals();
        if (trailMaterial != null) Destroy(trailMaterial);
        if (sparkleMaterial != null) Destroy(sparkleMaterial);
    }
}
