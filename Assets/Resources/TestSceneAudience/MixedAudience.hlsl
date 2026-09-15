#ifndef PARTICLE_MESH_AUDIENCE_URP_INCLUDED
#define PARTICLE_MESH_AUDIENCE_URP_INCLUDED

// 独自のインスタンシング用のデータ構造を定義する
// 全てのincludeの前に定義が必要
#define UNITY_PARTICLE_INSTANCE_DATA MyParticleInstanceData
#define UNITY_PARTICLE_INSTANCE_DATA_NO_ANIM_FRAME
struct MyParticleInstanceData
{
    float3x4 transform;
    uint color;
    float4 random0;
};

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#if defined(LOD_FADE_CROSSFADE)
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
#endif

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ParticlesInstancing.hlsl"

TEXTURE2D(_PositionTexture);
SAMPLER(sampler_PositionTexture);
float4 _PositionTexture_TexelSize;

TEXTURE2D(_NormalTexture);
SAMPLER(sampler_NormalTexture);

TEXTURE2D(_TangentTexture);
SAMPLER(sampler_TangentTexture);

TEXTURE2D(_ColorTexture);
SAMPLER(sampler_ColorTexture);

TEXTURE2D(_MatCapTexture);
SAMPLER(sampler_MatCapTexture);


CBUFFER_START(UnityPerMaterial)
    uniform float _AudienceSeconds;
    uniform float _MotionInterval;
    uniform float _MotionTransition;
    uniform float _ManualTime;
    uniform float _Framecount;
    uniform float _Speed;

    uniform float _RandomDelay;
    uniform float _Blend1;
    uniform float _Blend2;
    uniform float _Blend3;
    uniform float _Blend4;

    uniform float3 _FogColorTop;
    uniform float3 _FogColorBottom;
    uniform float _FogStart;
    uniform float _FogEnd;
    uniform float _FogHeightStart;
    uniform float _FogHeightEnd;

    uniform float3 _TintColor;
    uniform float3 _PenLightColor;
    uniform float3 _MatCapColor;

    #if defined(PARTICLE_MESH_AUDIENCE_URP_SCENE_SELECTION_PASS)
        uniform float _ObjectId;
        uniform float _PassValue;
    #endif
    #if defined(PARTICLE_MESH_AUDIENCE_URP_PICKING)
        uniform float4 _SelectionID;
    #endif
CBUFFER_END

struct Attributes
{
    float4 positionOS : POSITION;
    float4 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
    float4 color : COLOR;
#if defined(UNITY_PARTICLE_INSTANCING_ENABLED)
    float2 uv0 : TEXCOORD0;
    float2 uv1 : TEXCOORD1;
#else
    float4 uv0 : TEXCOORD0;
#endif
    float4 random0 : TEXCOORD2;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

inline float remap(float value, float inputMin, float inputMax, float outputMin, float outputMax)
{
    return outputMin + (value - inputMin) * (outputMax - outputMin) / (inputMax - inputMin);
}

#if defined(PARTICLE_MESH_AUDIENCE_URP_FORWARD)
struct Varyings
{
    float4 positionCS : SV_POSITION;
    float4 color1 : COLOR;
    float4 color2 : TEXCOORD0;
    float2 uv0 : TEXCOORD1;
    float3 normalVS : TEXCOORD2;
    float3 fogParam : TEXCOORD3;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};
#elif defined(PARTICLE_MESH_AUDIENCE_URP_DEPTH_NORMALS_ONLY)
struct Varyings
{
    float4 positionCS   : SV_POSITION;
    float3 normalWS     : TEXCOORD1;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};
#elif defined(PARTICLE_MESH_AUDIENCE_URP_DEPTH_ONLY) || \
      defined(PARTICLE_MESH_AUDIENCE_URP_SCENE_SELECTION_PASS) || \
      defined(PARTICLE_MESH_AUDIENCE_URP_PICKING) || \
      defined(PARTICLE_MESH_AUDIENCE_URP_SHADOW_CASTER)
struct Varyings
{
    float4 positionCS   : SV_POSITION;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};
#endif

inline half4 GetParticleColor(half4 color)
{
#if defined(UNITY_PARTICLE_INSTANCING_ENABLED)
#if !defined(UNITY_PARTICLE_INSTANCE_DATA_NO_COLOR)
    UNITY_PARTICLE_INSTANCE_DATA data = unity_ParticleInstanceData[unity_InstanceID];
    color = lerp(half4(1.0, 1.0, 1.0, 1.0), color, unity_ParticleUseMeshColors);
    color *= half4(UnpackFromR8G8B8A8(data.color));
#endif
#endif
    return color;
}

// Preserve the original sequential blend, including its epsilon-clamped
// startup/end-point behavior. Skip texture fetches only for EXACT zero weights.
// The atlas contains five equal clips separated by 1/64-wide gaps.
float3 SampleAudienceMotion(TEXTURE2D_PARAM(animationTexture, animationSampler),
    float2 vertexUV, float phase, float baseWeight, float4 clipWeights)
{
    float3 result = 0.0;
    float atlasScale = _PositionTexture_TexelSize.x * (_Framecount - 1.0);
    float sampleWeights[5] = { baseWeight, clipWeights.x, clipWeights.y, clipWeights.z, clipWeights.w };
    [unroll]
    for (int clip = 0; clip < 5; ++clip)
    {
        float weight = sampleWeights[clip];
        UNITY_BRANCH
        if (weight > 0.0)
        {
            float frame = phase * 0.1875 + (float)clip * 0.203125;
            float2 uv = vertexUV + float2(atlasScale * frame, 0.0);
            result += SAMPLE_TEXTURE2D_LOD(animationTexture, animationSampler, uv, 0.0).rgb * weight;
        }
    }
    return result;
}

Varyings vert(Attributes input)
{
    Varyings output = (Varyings)0;

    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

#if defined(_TIMEUPDATEMODE_MANUAL)
    float t = _ManualTime;
#else
    #if defined(APPLY_JUMP_DOUBLE_SPEED)
        float speed = 1.0;
        speed = lerp(1.0, 2.0, step(0.999, _Blend3));
        float t = _Time.y * _Speed * speed;
    #else
        float t = _Time.y * _Speed;
    #endif
#endif

#if defined(UNITY_PARTICLE_INSTANCING_ENABLED)
    UNITY_PARTICLE_INSTANCE_DATA data = unity_ParticleInstanceData[unity_InstanceID];
    t += data.random0.x * _RandomDelay;
    float seed = data.random0.x;
#else
    t += input.random0.x * _RandomDelay;
    float seed = input.random0.x;
#endif

    // Stable per-particle choices; audio time drives phase, so pausing freezes
    // both the motion and its transition. Every next choice differs from the last.
    float4 randoms = frac(seed * float4(127.1, 311.7, 74.7, 269.5));
    float period = max(1.0, _MotionInterval) * lerp(0.75, 1.25, randoms.x);
    float clock = max(0.0, _AudienceSeconds) + randoms.y * period;
    float cycle = floor(clock / period);
    float stride = randoms.z < 0.5 ? 1.0 : 3.0;
    float current = fmod(floor(randoms.w * 4.0) + cycle * stride, 4.0);
    float previous = fmod(current + 4.0 - stride, 4.0);
    float transition = smoothstep(0.0, max(0.05, min(_MotionTransition, period * 0.45)), fmod(clock, period));
    float4 choices = float4(0, 1, 2, 3);
    float4 weights = lerp(1.0 - step(0.5, abs(choices - previous)),
                          1.0 - step(0.5, abs(choices - current)), transition);
    weights *= smoothstep(0.0, 0.3, max(0.0, _AudienceSeconds));
    // Convert independent clip weights to the asset's sequential blend factors.
    float blend4 = weights.w;
    float blend3 = saturate(weights.z / max(0.00001, 1.0 - weights.w));
    float blend2 = saturate(weights.y / max(0.00001, 1.0 - weights.w - weights.z));
    float blend1 = saturate(weights.x / max(0.00001, 1.0 - weights.w - weights.z - weights.y));

    // Expand the existing lerp chain into contribution weights. Keeping the
    // original factors preserves rounding near transitions and the startup fade.
    float4 clipWeights;
    clipWeights.w = blend4;
    clipWeights.z = blend3 * (1.0 - blend4);
    clipWeights.y = blend2 * (1.0 - blend3) * (1.0 - blend4);
    clipWeights.x = blend1 * (1.0 - blend2) * (1.0 - blend3) * (1.0 - blend4);
    float baseWeight = (1.0 - blend1) * (1.0 - blend2) * (1.0 - blend3) * (1.0 - blend4);

    t = frac(t);
#if defined(UNITY_PARTICLE_INSTANCING_ENABLED)
    float2 vertexUV = input.uv1.xy;
#else
    float2 vertexUV = input.uv0.zw;
#endif
    float4 positionOS = input.positionOS;
    positionOS.xyz += SampleAudienceMotion(TEXTURE2D_ARGS(_PositionTexture, sampler_PositionTexture),
        vertexUV, t, baseWeight, clipWeights);

#if defined(PARTICLE_MESH_AUDIENCE_URP_DEPTH_NORMALS_ONLY) || (defined(PARTICLE_MESH_AUDIENCE_URP_FORWARD) && defined(APPLY_MATCAP))
    float4 normalOS = input.normalOS;
    // Retain the atlas sampler used by the original shader for all three maps.
    normalOS.xyz = SampleAudienceMotion(TEXTURE2D_ARGS(_NormalTexture, sampler_PositionTexture),
        vertexUV, t, baseWeight, clipWeights);
#endif

// UniversalForwardパス
#if defined(PARTICLE_MESH_AUDIENCE_URP_FORWARD)
    VertexPositionInputs vertexInput = GetVertexPositionInputs(positionOS.xyz);
    output.positionCS = vertexInput.positionCS;

    output.uv0 = input.uv0.xy;

    output.color1 = GetParticleColor(input.color);
    output.color2 = input.color;

    #if defined(APPLY_MATCAP)
        float3 normalWS = TransformObjectToWorldNormal(normalOS.xyz);
        output.normalVS = mul((float3x3)UNITY_MATRIX_V, normalWS);
    #endif

    #if defined(_FOGMODE_USER)
        output.fogParam.x = -mul(UNITY_MATRIX_MV, positionOS).z;
        output.fogParam.y = positionOS.y;
    #endif
// DpethNormalsOnlyパス
#elif defined(PARTICLE_MESH_AUDIENCE_URP_DEPTH_NORMALS_ONLY)
    float4 tangentOS = input.tangentOS;
    tangentOS.xyz = SampleAudienceMotion(TEXTURE2D_ARGS(_TangentTexture, sampler_PositionTexture),
        vertexUV, t, baseWeight, clipWeights);

    VertexPositionInputs vertexInput = GetVertexPositionInputs(positionOS.xyz);
    output.positionCS = vertexInput.positionCS;

    VertexNormalInputs normalInput = GetVertexNormalInputs(normalOS.xyz, tangentOS);
    output.normalWS = NormalizeNormalPerVertex(normalInput.normalWS);

#elif defined(PARTICLE_MESH_AUDIENCE_URP_DEPTH_ONLY) || \
      defined(PARTICLE_MESH_AUDIENCE_URP_SCENE_SELECTION_PASS) || \
      defined(PARTICLE_MESH_AUDIENCE_URP_PICKING) || \
      defined(PARTICLE_MESH_AUDIENCE_URP_SHADOW_CASTER)
    VertexPositionInputs vertexInput = GetVertexPositionInputs(positionOS.xyz);
    output.positionCS = vertexInput.positionCS;
#else
#endif

    return output;
}

#endif // PARTICLE_MESH_AUDIENCE_URP_INCLUDED

