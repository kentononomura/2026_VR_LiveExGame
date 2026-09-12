Shader "TestScene/Particles/MixedAudience"
{ 
    Properties
    {
        // Project variant of ShirayuriMeshibe's audience shader.
        [Header(IndividualMotion)]
        _AudienceSeconds("Music seconds", Float) = 0
        _MotionInterval("Motion interval seconds", Float) = 8
        _MotionTransition("Motion transition seconds", Float) = 1
        [Header(AnimationTime)]
        [KeywordEnum(Auto, Manual)] _TimeUpdateMode("Time Update Mode", Float) = 0
        _ManualTime ("ManualTime", Float) = 0

        [Header(VertexAnimationTexture)]
        _Framecount("Frame count", Float) = 240
        [NoScaleOffset] _PositionTexture ("Position Texture", 2D) = "white" {}
        [NoScaleOffset] _NormalTexture("Normal Texture", 2D) = "white" {}
        [NoScaleOffset] _TangentTexture("Tangent Texture", 2D) = "white" {}
        _Speed("Speed(Base 60BPM)", Float) = 1

        [Header(BlendAnimation)]
        _RandomDelay("RandomDelay ", Range(0 , 1)) = 0
        _Blend1("Blend Swing", Range(0 , 1)) = 0
        _Blend2("Blend Raise", Range(0 , 1)) = 0
        _Blend3("Blend Jump", Range(0 , 1)) = 0
        _Blend4("Blend Ripple", Range(0 , 1)) = 0
        [Toggle(APPLY_JUMP_DOUBLE_SPEED)] _ApplyJumpDoubleSpeed("Apply Double Jump Speed", Float) = 0

        [Header(Fog)]
        [KeywordEnum(None, User)] _FogMode("Fog Mode", Float) = 0
        _FogColorTop ("Fog Color(TOP)", Color) = (1.0, 1.0, 1.0, 1.0)
        _FogColorBottom ("Fog Color(BOTTOM)", Color) = (1.0, 1.0, 1.0, 1.0)
        _FogStart("Fog Start(Z)", Float) = 0.5
        _FogEnd("Fog End(Z)", Float) = 10
        _FogHeightStart("Fog Height Start(Local Y)", Float) = 0.5
        _FogHeightEnd("Fog Height End(Local Y)", Float) = 1.5

        [Header(ObjectSettings)]
        [NoScaleOffset] _ColorTexture("Color Texture", 2D) = "white" {}
        _TintColor ("Tint Color", Color) = (1.0, 1.0, 1.0, 1.0)
        [HDR] _PenLightColor ("PenLight Color", Color) = (1.0, 1.0, 1.0, 1.0)

        [Toggle(APPLY_MATCAP)] _ApplyMatCap("Apply MatCap", Float) = 0
        [NoScaleOffset] _MatCapTexture("MatCap Texture",2D) = "black" {}
        [HDR] _MatCapColor ("MatCap Color", Color) = (1.0, 1.0, 1.0, 1.0)

        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
    }

    // URP
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
            "PerformanceChecks" = "False"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ForwardLit"
            Tags
            {
                "LightMode" = "UniversalForward"
            }

            Cull [_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile_local _ _TIMEUPDATEMODE_MANUAL
            #pragma multi_compile_local _ APPLY_JUMP_DOUBLE_SPEED
            #pragma multi_compile_local _ APPLY_MATCAP
            #pragma multi_compile_local _ _FOGMODE_USER

            #pragma multi_compile_instancing
            #pragma instancing_options procedural:ParticleInstancingSetup
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #define PARTICLE_MESH_AUDIENCE_URP_FORWARD
            #include "MixedAudience.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ParticlesInstancing.hlsl"

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                const float4 c0 = SAMPLE_TEXTURE2D(_ColorTexture, sampler_ColorTexture, input.uv0);
                // const float3 tintColor = UNITY_ACCESS_INSTANCED_PROP(Props, _TintColor);
                float3 c1 = lerp(c0.rgb * _TintColor, float3(0.0, 0.0, 0.0), input.color2.r);

#if defined(_FOGMODE_USER)
                const float3 fogColor = lerp(_FogColorBottom, _FogColorTop, smoothstep(_FogHeightStart, _FogHeightEnd, input.fogParam.y));
                c1 = lerp(c1, fogColor, smoothstep(_FogStart, _FogEnd, input.fogParam.x));
#endif

#if defined(APPLY_MATCAP)
                const float2 uvMatcap = input.normalVS.xy * 0.5 + 0.5;
                const float4 cMatcap = SAMPLE_TEXTURE2D(_MatCapTexture, sampler_MatCapTexture, uvMatcap);
                // const float3 matCapColor = UNITY_ACCESS_INSTANCED_PROP(Props, _MatCapColor);
                c1 += cMatcap.rgb * _MatCapColor;
#endif

                // Apply PenLight Color
                // const float3 penlightColor = UNITY_ACCESS_INSTANCED_PROP(Props, _PenLightColor);
                c1 += input.color1.rgb * _PenLightColor * c0.rgb;
                c1 = max(0.0, c1); // Flashing対応

                float4 f = float4(c1, 1.0);
                return f;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags
            {
                "LightMode" = "DepthOnly"
            }

            ZWrite On
            ColorMask R
            Cull[_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma shader_feature_local_fragment _SURFACE_TYPE_TRANSPARENT
            #pragma multi_compile_local _ _TIMEUPDATEMODE_MANUAL
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _ALPHAMODULATE_ON

            #pragma multi_compile_fog
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
            #pragma multi_compile _ DEBUG_DISPLAY
            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            #pragma multi_compile_instancing
            #pragma instancing_options procedural:ParticleInstancingSetup
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #define PARTICLE_MESH_AUDIENCE_URP_DEPTH_ONLY
            #include "MixedAudience.hlsl"

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

#if defined(LOD_FADE_CROSSFADE)
                LODFadeCrossFade(input.positionCS);
#endif

                return input.positionCS.z;
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormalsOnly"
            Tags
            {
                "LightMode" = "DepthNormalsOnly"
            }

            ZWrite On
            Cull[_Cull]

            HLSLPROGRAM
            #pragma exclude_renderers gles3 glcore
            #pragma target 4.5

            #pragma vertex vert
            #pragma fragment frag

            // #pragma shader_feature_local _ALPHATEST_ON

            #pragma multi_compile_local _ _TIMEUPDATEMODE_MANUAL
            #pragma multi_compile_local _ APPLY_JUMP_DOUBLE_SPEED

            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT // forward-only variant
            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            #pragma multi_compile_instancing
            #pragma instancing_options procedural:ParticleInstancingSetup
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            // #include_with_pragmas "Packages/com.unity.render-pipelines.universal/Shaders/Particles/ParticlesUnlitInput.hlsl"

            #define PARTICLE_MESH_AUDIENCE_URP_DEPTH_NORMALS_ONLY
            #include "MixedAudience.hlsl"

            void frag(
                Varyings input
                , out half4 outNormalWS : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
                , out float4 outRenderingLayers : SV_Target1
#endif
                )
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

// #if defined(_ALPHATEST_ON)
                // Alpha(SampleAlbedoAlpha(input.uv, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap)).a, _BaseColor, _Cutoff);
// #endif

#if defined(LOD_FADE_CROSSFADE)
                LODFadeCrossFade(input.positionCS);
#endif

                outNormalWS = half4(NormalizeNormalPerPixel(input.normalWS), 0.0);

#ifdef _WRITE_RENDERING_LAYERS
                uint renderingLayers = GetMeshRenderingLayer();
                outRenderingLayers = float4(EncodeMeshRenderingLayer(renderingLayers), 0, 0, 0);
#endif
            }
            ENDHLSL
        }

        Pass
        {
            Tags
            {
                "LightMode" = "SceneSelectionPass"
            }

            BlendOp Add
            Blend One Zero
            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma exclude_renderers gles3 glcore
            #pragma target 4.5

            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile_local _ _TIMEUPDATEMODE_MANUAL
            #pragma multi_compile_local _ APPLY_JUMP_DOUBLE_SPEED

            #pragma multi_compile_instancing
            #pragma instancing_options procedural:ParticleInstancingSetup
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #define PARTICLE_MESH_AUDIENCE_URP_SCENE_SELECTION_PASS
            #include "MixedAudience.hlsl"

            half4 frag(Varyings input) : SV_Target
            {
                return float4(_ObjectId, _PassValue, 1.0, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Tags
            {
                "LightMode" = "Picking"
            }

            BlendOp Add
            Blend One Zero
            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile_local _ _TIMEUPDATEMODE_MANUAL
            #pragma multi_compile_local _ APPLY_JUMP_DOUBLE_SPEED

            #pragma multi_compile_instancing
            #pragma instancing_options procedural:ParticleInstancingSetup
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #define PARTICLE_MESH_AUDIENCE_URP_PICKING
            #include "MixedAudience.hlsl"
            
            half4 frag(Varyings input) : SV_Target
            {
                return _SelectionID;
            }
            
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags
            {
                "LightMode" = "ShadowCaster"
            }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma multi_compile_local _ _TIMEUPDATEMODE_MANUAL
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE

            #define PARTICLE_MESH_AUDIENCE_URP_SHADOW_CASTER
            #include "MixedAudience.hlsl"

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

#if defined(LOD_FADE_CROSSFADE)
                LODFadeCrossFade(input.positionCS);
#endif

                return 0;
            }
            ENDHLSL
        }
    }
}
