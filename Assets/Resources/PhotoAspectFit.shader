Shader "PhotoViewer/AspectFit"
{
    Properties
    {
        [MainTexture] _BaseMap("Photo", 2D) = "black" {}
        _BackgroundColor("Background", Color) = (0, 0, 0, 1)
        _PhotoAspect("Photo Aspect", Float) = 1
        _ScreenAspect("Screen Aspect", Float) = 1.333333
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "PhotoAspectFit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BackgroundColor;
                float _PhotoAspect;
                float _ScreenAspect;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 fittedUV = input.uv;
                float photoAspect = max(_PhotoAspect, 0.0001);
                float screenAspect = max(_ScreenAspect, 0.0001);

                if (photoAspect > screenAspect)
                {
                    float imageHeight = screenAspect / photoAspect;
                    fittedUV.y = (input.uv.y - 0.5) / imageHeight + 0.5;
                }
                else
                {
                    float imageWidth = photoAspect / screenAspect;
                    fittedUV.x = (input.uv.x - 0.5) / imageWidth + 0.5;
                }

                float inside =
                    step(0.0, fittedUV.x) * step(fittedUV.x, 1.0) *
                    step(0.0, fittedUV.y) * step(fittedUV.y, 1.0);
                half4 photoColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, fittedUV);
                return lerp(_BackgroundColor, photoColor, inside);
            }
            ENDHLSL
        }
    }
}
