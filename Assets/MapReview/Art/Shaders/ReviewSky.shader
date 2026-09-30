Shader "Deinosavros/Map Review/Sky"
{
    Properties
    {
        _TopColor("Top Color", Color) = (0.025, 0.055, 0.105, 1)
        _HorizonColor("Horizon Color", Color) = (0.18, 0.23, 0.30, 1)
        _BottomColor("Bottom Color", Color) = (0.035, 0.05, 0.075, 1)
        _CloudColor("Cloud Color", Color) = (0.28, 0.34, 0.42, 0.22)
        _HorizonSharpness("Horizon Sharpness", Range(1, 16)) = 6
        _CloudScale("Cloud Scale", Range(0.1, 8)) = 1.7
        _CloudStrength("Cloud Strength", Range(0, 1)) = 0.32
        _CloudSpeed("Cloud Speed", Vector) = (0.0025, -0.0015, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Background"
            "RenderType" = "Background"
            "RenderPipeline" = "UniversalPipeline"
            "PreviewType" = "Skybox"
        }

        Pass
        {
            Name "Sky"
            Cull Off
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _TopColor;
                float4 _HorizonColor;
                float4 _BottomColor;
                float4 _CloudColor;
                float _HorizonSharpness;
                float _CloudScale;
                float _CloudStrength;
                float4 _CloudSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction : TEXCOORD0;
            };

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
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
                float value = 0.0;
                float amplitude = 0.55;
                value += ValueNoise(p) * amplitude;
                p = p * 2.03 + 7.17;
                amplitude *= 0.5;
                value += ValueNoise(p) * amplitude;
                p = p * 2.01 - 3.91;
                amplitude *= 0.5;
                value += ValueNoise(p) * amplitude;
                return value;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.direction = input.positionOS.xyz;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 direction = normalize(input.direction);
                float upperBlend = saturate(direction.y * 1.65);
                float lowerBlend = saturate(direction.y * 2.4 + 1.0);
                float3 lowerColor = lerp(_BottomColor.rgb, _HorizonColor.rgb, lowerBlend);
                float3 color = lerp(lowerColor, _TopColor.rgb, upperBlend);

                float horizon = exp(-abs(direction.y) * _HorizonSharpness);
                color = lerp(color, _HorizonColor.rgb, horizon * 0.32);

                float denominator = max(0.22, abs(direction.y) + 0.22);
                float2 cloudUv = direction.xz / denominator;
                cloudUv = cloudUv * _CloudScale + _Time.y * _CloudSpeed.xy;
                float cloud = Fbm(cloudUv);
                float broadCloud = Fbm(cloudUv * 0.43 - _Time.y * _CloudSpeed.yx * 0.35 + 9.3);
                cloud = saturate(cloud * 0.72 + broadCloud * 0.46);
                cloud = smoothstep(0.36, 0.72, cloud) * _CloudStrength;
                cloud *= lerp(0.45, 1.0, saturate(1.0 - abs(direction.y)));
                color = lerp(color, _CloudColor.rgb, cloud * _CloudColor.a);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
