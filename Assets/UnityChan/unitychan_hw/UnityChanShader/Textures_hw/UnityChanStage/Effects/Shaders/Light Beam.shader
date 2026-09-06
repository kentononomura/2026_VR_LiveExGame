Shader "Custom/Light Beam"
{
    Properties
    {
        _Color("Base Color", Color) = (1, 1, 1, 1)
        _MainTex("Gradient Texture", 2D) = "white" {}
        _NoiseTex1("Noise Texture 1", 2D) = "white" {}
        _NoiseTex2("Noise Texture 2", 2D) = "white" {}
        _NoiseScale("Noise Scale", Vector) = (1, 1, 1, 1)
        _NoiseSpeed("Noise Speed", Vector) = (0.1, 0.1, 0.1, 0.1)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_NoiseTex1);
            SAMPLER(sampler_NoiseTex1);
            TEXTURE2D(_NoiseTex2);
            SAMPLER(sampler_NoiseTex2);

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float4 _MainTex_ST;
                float4 _NoiseScale;
                float4 _NoiseSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 noiseUv1 : TEXCOORD1;
                float2 noiseUv2 : TEXCOORD2;
                float3 positionWS : TEXCOORD3;
                half3 normalWS : TEXCOORD4;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.noiseUv1 = positionInputs.positionWS.xy * _NoiseScale.xy + _NoiseSpeed.xy * _Time.y;
                output.noiseUv2 = positionInputs.positionWS.xy * _NoiseScale.zw + _NoiseSpeed.zw * _Time.y;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 cameraDirection = normalize(input.positionWS - GetCameraPositionWS());
                half falloff = max(abs(dot(cameraDirection, normalize(input.normalWS))) - 0.4h, 0.0h);
                falloff = falloff * falloff * 5.0h;
                half gradient = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a;
                half noise1 = SAMPLE_TEXTURE2D(_NoiseTex1, sampler_NoiseTex1, input.noiseUv1).r;
                half noise2 = SAMPLE_TEXTURE2D(_NoiseTex2, sampler_NoiseTex2, input.noiseUv2).r;
                return half4(_Color.rgb, _Color.a * gradient * noise1 * noise2 * falloff);
            }
            ENDHLSL
        }
    }
}
