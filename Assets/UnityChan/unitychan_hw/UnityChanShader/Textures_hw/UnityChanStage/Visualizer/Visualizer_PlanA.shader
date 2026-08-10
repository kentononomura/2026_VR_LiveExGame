Shader "Custom/Visualizer_PlanA"
{
	Properties
	{
		_ReflectionTex ("Base (RGB)", 2D) = "black" {}
		_Spectra ("Spectra", Vector) = (0, 0, 0, 0)

		_Center ("Center", Vector) = (0.0, 0.0, 0.0)
		_RingSrtide ("Stride", Float) = 0.2
		_RingThicknessMin ("ThicknessMin", Float) = 0.1
		_RingThicknessMax ("ThicknessMax", Float) = 0.5
		_RingEmission ("RingEmission", Float) = 10.0
		_RingSpeedMin ("RingSpeedMin", Float) = 0.2
		_RingSpeedMax ("RingSpeedMin", Float) = 0.5
		_GridColor ("GridColor", Vector) = (0.2, 0.3, 0.5)
		_GridEmission ("GridEmission", Float) = 8.0
		_ReflectionStrength ("ReflectionStrength", Float) = 0.2
	}
	SubShader
	{
		Tags { "RenderPipeline" = "UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }

		Pass
		{
			Name "ForwardLit"
			Tags { "LightMode" = "UniversalForward" }
			Cull Off

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 3.0

			#pragma multi_compile_instancing

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			struct Attributes
			{
				float4 positionOS   : POSITION;
				float3 normalOS     : NORMAL;
				float2 uv           : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionCS   : SV_POSITION;
				float3 positionWS   : TEXCOORD0;
				float4 screenPos    : TEXCOORD1;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
				float4 _Spectra;
				float3 _Center;
				float _RingSrtide;
				float _RingThicknessMin;
				float _RingThicknessMax;
				float _RingEmission;
				float _RingSpeedMin;
				float _RingSpeedMax;
				float4 _GridColor;
				float _GridEmission;
				float _ReflectionStrength;
			CBUFFER_END

			float iq_rand(float p)
			{
				return frac(sin(p) * 43758.5453);
			}

			float _gl_mod(float a, float b) { return a - b * floor(a / b); }
			float2 _gl_mod(float2 a, float2 b) { return a - b * floor(a / b); }
			float3 _gl_mod(float3 a, float3 b) { return a - b * floor(a / b); }

			float Rings(float3 pos)
			{
				float pi = 3.14159;
				float2 wpos = pos.xz;

				float stride = _RingSrtide;
				float strine_half = stride * 0.5;
				float thickness = 1.0 - (_RingThicknessMin + length(_Spectra.xyz) * (_RingThicknessMax - _RingThicknessMin));
				float distance = abs(length(wpos) - _Time.y * 0.1);
				float fra = _gl_mod(distance, stride);
				float cycle = floor((distance) / stride);

				float c = strine_half - abs(fra - strine_half) - strine_half * thickness;
				c = max(c * (1.0 / (strine_half * thickness)), 0.0);

				float rs = iq_rand(cycle * cycle);
				float r = iq_rand(cycle) + _Time.y * (_RingSpeedMin + (_RingSpeedMax - _RingSpeedMin) * rs);

				float angle = atan2(wpos.y, wpos.x) / pi * 0.5 + 0.5; // 0.0-1.0
				float a = 1.0 - _gl_mod(angle + r, 1.0);
				a = max(a - 0.7, 0.0) * c;
				return a;
			}

			float Grid(float3 pos)
			{
				float grid_size = 0.4;
				float line_thickness = 0.015;

				float2 m = _gl_mod(abs(pos.xz * sign(pos.xz)), grid_size);
				if (m.x - line_thickness < 0.0 || m.y - line_thickness < 0.0) {
					return 1.0;
				}
				return 0.0;
			}

			float Circle(float3 pos)
			{
				float o_radius = 5.0;
				float i_radius = 4.0;
				float d = length(pos.xz);
				float c = max(o_radius - (o_radius - _gl_mod(d - _Time.y * 1.5, o_radius)) - i_radius, 0.0);
				return c;
			}

			float Hex(float2 p, float2 h)
			{
				float2 q = abs(p);
				return max(q.x - h.y, max(q.x + q.y * 0.57735, q.y * 1.1547) - h.x);
			}

			float HexGrid(float3 p)
			{
				float scale = 1.2;
				float2 grid = float2(0.692, 0.4) * scale;
				float radius = 0.22 * scale;

				float2 p1 = _gl_mod(p.xz, grid) - grid * 0.5;
				float c1 = Hex(p1, radius);

				float2 p2 = _gl_mod(p.xz + grid * 0.5, grid) - grid * 0.5;
				float c2 = Hex(p2, radius);
				return min(c1, c2);
			}

			Varyings vert(Attributes input)
			{
				Varyings output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
				output.positionCS = vertexInput.positionCS;
				output.positionWS = vertexInput.positionWS;
				output.screenPos = ComputeScreenPos(vertexInput.positionCS);
				return output;
			}

			half4 frag(Varyings input) : SV_Target
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

				float3 center = input.positionWS - _Center;
				float trails = Rings(center);
				float grid_d = HexGrid(center);
				float grid = grid_d > 0.0 ? 1.0 : 0.0;
				float circle = Circle(center);

				// Deep, elegant glossy black base color
				float3 baseColor = float3(0.015, 0.015, 0.018);

				float3 emission = baseColor;
				emission += trails * (0.5 + _Spectra.xyz * _RingEmission);
				emission += _GridColor.xyz * (grid * circle) * _GridEmission;

				return half4(emission, 1.0);
			}
			ENDHLSL
		}
	}
	FallBack "Diffuse"
}
