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
#else
    t += input.random0.x * _RandomDelay;
#endif

    t = frac(t);
    const float t0 = remap(t, 0.0, 1.0, 0.0, 0.1875);
    const float t1 = remap(t, 0.0, 1.0, 0.203125, 0.390625);
    const float t2 = remap(t, 0.0, 1.0, 0.40625, 0.59375);
    const float t3 = remap(t, 0.0, 1.0, 0.609375, 0.796875);
    const float t4 = remap(t, 0.0, 1.0, 0.8125, 1.0);

    float4 positionOS = input.positionOS;
    float4 normalOS = input.normalOS;

    const float fameCount = _Framecount - 1.0;
    const float2 offsetUV0 = float2((_PositionTexture_TexelSize.x * fameCount * t0), 0.0);
    const float2 offsetUV1 = float2((_PositionTexture_TexelSize.x * fameCount * t1), 0.0);
    const float2 offsetUV2 = float2((_PositionTexture_TexelSize.x * fameCount * t2), 0.0);
    const float2 offsetUV3 = float2((_PositionTexture_TexelSize.x * fameCount * t3), 0.0);
    const float2 offsetUV4 = float2((_PositionTexture_TexelSize.x * fameCount * t4), 0.0);
#if defined(UNITY_PARTICLE_INSTANCING_ENABLED)
    const float2 uv0 = (offsetUV0 + input.uv1.xy);
    const float2 uv1 = (offsetUV1 + input.uv1.xy);
    const float2 uv2 = (offsetUV2 + input.uv1.xy);
    const float2 uv3 = (offsetUV3 + input.uv1.xy);
    const float2 uv4 = (offsetUV4 + input.uv1.xy);
#else
    const float2 uv0 = (offsetUV0 + input.uv0.zw);
    const float2 uv1 = (offsetUV1 + input.uv0.zw);
    const float2 uv2 = (offsetUV2 + input.uv0.zw);
    const float2 uv3 = (offsetUV3 + input.uv0.zw);
    const float2 uv4 = (offsetUV4 + input.uv0.zw);
#endif

    const float3 offsetPosition0 = SAMPLE_TEXTURE2D_LOD(_PositionTexture, sampler_PositionTexture, uv0, 0.0).rgb;
    const float3 offsetPosition1 = SAMPLE_TEXTURE2D_LOD(_PositionTexture, sampler_PositionTexture, uv1, 0.0).rgb;
    const float3 offsetPosition2 = SAMPLE_TEXTURE2D_LOD(_PositionTexture, sampler_PositionTexture, uv2, 0.0).rgb;
    const float3 offsetPosition3 = SAMPLE_TEXTURE2D_LOD(_PositionTexture, sampler_PositionTexture, uv3, 0.0).rgb;
    const float3 offsetPosition4 = SAMPLE_TEXTURE2D_LOD(_PositionTexture, sampler_PositionTexture, uv4, 0.0).rgb;
    const float3 vertex01 = lerp(offsetPosition0, offsetPosition1, _Blend1);
    const float3 vertex12 = lerp(vertex01, offsetPosition2, _Blend2);
    const float3 vertex23 = lerp(vertex12, offsetPosition3, _Blend3);
    const float3 vertex34 = lerp(vertex23, offsetPosition4, _Blend4);
    positionOS.xyz += vertex34;

    const float3 normal0 = SAMPLE_TEXTURE2D_LOD(_NormalTexture, sampler_PositionTexture, uv0, 0.0).rgb;
    const float3 normal1 = SAMPLE_TEXTURE2D_LOD(_NormalTexture, sampler_PositionTexture, uv1, 0.0).rgb;
    const float3 normal2 = SAMPLE_TEXTURE2D_LOD(_NormalTexture, sampler_PositionTexture, uv2, 0.0).rgb;
    const float3 normal3 = SAMPLE_TEXTURE2D_LOD(_NormalTexture, sampler_PositionTexture, uv3, 0.0).rgb;
    const float3 normal4 = SAMPLE_TEXTURE2D_LOD(_NormalTexture, sampler_PositionTexture, uv4, 0.0).rgb;
    const float3 normal01 = lerp(normal0, normal1, _Blend1);
    const float3 normal12 = lerp(normal01, normal2, _Blend2);
    const float3 normal23 = lerp(normal12, normal3, _Blend3);
    const float3 normal34 = lerp(normal23, normal4, _Blend4);
    normalOS.xyz = normal34;

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
    const float3 tangent0 = SAMPLE_TEXTURE2D_LOD(_TangentTexture, sampler_PositionTexture, uv0, 0.0).rgb;
    const float3 tangent1 = SAMPLE_TEXTURE2D_LOD(_TangentTexture, sampler_PositionTexture, uv1, 0.0).rgb;
    const float3 tangent2 = SAMPLE_TEXTURE2D_LOD(_TangentTexture, sampler_PositionTexture, uv2, 0.0).rgb;
    const float3 tangent3 = SAMPLE_TEXTURE2D_LOD(_TangentTexture, sampler_PositionTexture, uv3, 0.0).rgb;
    const float3 tangent4 = SAMPLE_TEXTURE2D_LOD(_TangentTexture, sampler_PositionTexture, uv4, 0.0).rgb;
    const float3 tangent01 = lerp(tangent0, tangent1, _Blend1);
    const float3 tangent12 = lerp(tangent01, tangent2, _Blend2);
    const float3 tangent23 = lerp(tangent12, tangent3, _Blend3);
    const float3 tangent34 = lerp(tangent23, tangent4, _Blend4);
    float4 tangentOS = input.tangentOS;
    tangentOS.xyz = tangent34;

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