Shader "Deinosavros/Map Mist Surface"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (0.05, 0.08, 0.11, 0.8)
        _HighlightColor("Highlight Color", Color) = (0.16, 0.21, 0.26, 1)
        _Density("Density", Range(0, 1.5)) = 0.8
        _Coverage("Coverage", Range(0, 1)) = 0.75
        _NoiseScale("Noise Scale", Range(0.001, 0.25)) = 0.035
        _EdgeSoftness("Edge Softness", Range(0.01, 0.5)) = 0.2
        _SoftDepth("Soft Depth", Range(0.05, 10)) = 2.5
        _Ceiling("World Height Ceiling", Float) = 1000
        _HeightFade("Height Fade", Float) = 2
        _BaseHeight("Base Fade Height", Float) = -10000
        _BaseFadeDistance("Base Fade Distance", Float) = 1
        _FlowA("Flow A", Vector) = (0.006, 0.002, 0, 0)
        _FlowB("Flow B", Vector) = (-0.003, 0.005, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "MistForward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _HighlightColor;
                float _Density;
                float _Coverage;
                float _NoiseScale;
                float _EdgeSoftness;
                float _SoftDepth;
                float _Ceiling;
                float _HeightFade;
                float _BaseHeight;
                float _BaseFadeDistance;
                float4 _FlowA;
                float4 _FlowB;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float fogFactor : TEXCOORD1;
            };

            float Hash21(float2 p)
            {
                p = frac(p * float2(127.1, 311.7));
                p += dot(p, p + 19.19);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 cell = floor(p);
                float2 local = frac(p);
                local = local * local * (3.0 - 2.0 * local);
                float a = Hash21(cell);
                float b = Hash21(cell + float2(1.0, 0.0));
                float c = Hash21(cell + float2(0.0, 1.0));
                float d = Hash21(cell + float2(1.0, 1.0));
                return lerp(lerp(a, b, local.x), lerp(c, d, local.x), local.y);
            }

            float Fbm(float2 p)
            {
                float value = ValueNoise(p) * 0.58;
                p = p * 2.07 + 5.13;
                value += ValueNoise(p) * 0.28;
                p = p * 2.01 - 2.71;
                value += ValueNoise(p) * 0.14;
                return value;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 baseUv = input.positionWS.xz * _NoiseScale;
                float noiseA = Fbm(baseUv + _Time.y * _FlowA.xy);
                float noiseB = Fbm(baseUv * 1.71 + _Time.y * _FlowB.xy + 13.7);
                float combinedNoise = saturate(noiseA * 0.68 + noiseB * 0.46);
                float threshold = 1.0 - _Coverage;
                float coverage = smoothstep(threshold - _EdgeSoftness, threshold + _EdgeSoftness, combinedNoise);

                float2 screenUv = input.positionCS.xy / _ScaledScreenParams.xy;
                float sceneRawDepth = SampleSceneDepth(screenUv);
                float sceneEyeDepth = LinearEyeDepth(sceneRawDepth, _ZBufferParams);
                float surfaceEyeDepth = -TransformWorldToView(input.positionWS).z;
                float depthFade = saturate((sceneEyeDepth - surfaceEyeDepth) / max(_SoftDepth, 0.001));

                float heightFade = saturate((_Ceiling - input.positionWS.y) / max(_HeightFade, 0.01));
                float baseFade = saturate((input.positionWS.y - _BaseHeight) / max(_BaseFadeDistance, 0.01));
                float alpha = coverage * _Density * _BaseColor.a * depthFade * heightFade * baseFade;
                float3 color = lerp(_BaseColor.rgb, _HighlightColor.rgb, saturate(combinedNoise * 1.2));
                color = MixFog(color, input.fogFactor);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
