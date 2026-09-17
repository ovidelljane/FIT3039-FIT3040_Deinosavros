Shader "Deinosavros/Map Camera Clouds"
{
    Properties
    {
        _CloudColor("Cloud Color", Color) = (0.82, 0.82, 0.79, 1)
        _ShadowColor("Shadow Color", Color) = (0.30, 0.34, 0.38, 1)
        _Opacity("Opacity", Range(0, 0.5)) = 0.26
        _Coverage("Coverage", Range(0.3, 0.8)) = 0.56
        _Softness("Edge Softness", Range(0.02, 0.35)) = 0.09
        _PrimaryScale("Primary Scale", Range(0.2, 5)) = 1.35
        _DetailScale("Detail Scale", Range(1, 12)) = 4.8
        _PrimarySpeed("Primary Speed", Vector) = (0.016, 0.0015, 0, 0)
        _DetailSpeed("Detail Speed", Vector) = (-0.008, 0.003, 0, 0)
        _CenterClarity("Center Clarity", Range(0, 1)) = 0.25
        _Aspect("Aspect", Float) = 1.777778
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent+100"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "CameraClouds"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _CloudColor;
                float4 _ShadowColor;
                float4 _PrimarySpeed;
                float4 _DetailSpeed;
                float _Opacity;
                float _Coverage;
                float _Softness;
                float _PrimaryScale;
                float _DetailScale;
                float _CenterClarity;
                float _Aspect;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            float Hash21(float2 value)
            {
                value = frac(value * float2(123.34, 345.45));
                value += dot(value, value + 34.345);
                return frac(value.x * value.y);
            }

            float ValueNoise(float2 value)
            {
                float2 cell = floor(value);
                float2 local = frac(value);
                local = local * local * (3.0 - 2.0 * local);

                float bottomLeft = Hash21(cell);
                float bottomRight = Hash21(cell + float2(1.0, 0.0));
                float topLeft = Hash21(cell + float2(0.0, 1.0));
                float topRight = Hash21(cell + float2(1.0, 1.0));
                return lerp(
                    lerp(bottomLeft, bottomRight, local.x),
                    lerp(topLeft, topRight, local.x),
                    local.y);
            }

            float Fbm(float2 value)
            {
                float noise = ValueNoise(value) * 0.52;
                value = value * 2.03 + 7.17;
                noise += ValueNoise(value) * 0.27;
                value = value * 2.01 - 3.91;
                noise += ValueNoise(value) * 0.14;
                value = value * 2.04 + 5.37;
                noise += ValueNoise(value) * 0.07;
                return noise;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 centeredUv = input.uv - 0.5;
                centeredUv.x *= _Aspect;

                float2 primaryUv = centeredUv * _PrimaryScale + _Time.y * _PrimarySpeed.xy;
                float2 detailUv = centeredUv * _DetailScale + _Time.y * _DetailSpeed.xy + 11.7;
                float primaryNoise = Fbm(primaryUv);
                float detailNoise = Fbm(detailUv);
                float warpedNoise = Fbm(primaryUv * 0.56 - detailNoise * 0.38 + 19.3);
                float density = saturate(primaryNoise * 0.58 + warpedNoise * 0.30 + detailNoise * 0.18);
                float cloudMask = smoothstep(_Coverage - _Softness, _Coverage + _Softness, density);

                float centerDistance = length(float2(centeredUv.x * 0.72, centeredUv.y));
                float centerFade = smoothstep(0.10, 0.62, centerDistance);
                cloudMask *= lerp(1.0, centerFade, _CenterClarity);

                float edgeFeather = smoothstep(0.0, 0.045, input.uv.x)
                    * smoothstep(0.0, 0.045, input.uv.y)
                    * smoothstep(0.0, 0.045, 1.0 - input.uv.x)
                    * smoothstep(0.0, 0.045, 1.0 - input.uv.y);

                float lighting = saturate(detailNoise * 1.2 + primaryNoise * 0.35);
                float3 color = lerp(_ShadowColor.rgb, _CloudColor.rgb, lighting);
                float alpha = cloudMask * _Opacity * edgeFeather;
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
