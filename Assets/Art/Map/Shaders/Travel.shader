Shader "Map/Travel Symbols"
{
    Properties
    {
        _BaseMap("Original Symbol Atlas", 2D) = "white" {}
        _AtlasRect("Atlas Rectangle", Vector) = (0,0,0.25,0.25)
        _Tint("Tint", Color) = (1,1,1,1)
        _CreamTint("Cream Tint", Color) = (1,1,1,1)
        _Opacity("Opacity", Range(0,1)) = 1
        _Emission("Emission", Range(0,3)) = 1
        _SoftDepth("Depth Softness", Float) = 0.06
        _Mode("Route Mode", Float) = 0
        _Head("Route Head", Float) = 0
        _Repeats("Route Repeats", Float) = 1
        [ToggleUI] _FlameMotion("Animate Fireseed Only", Float) = 0
        _FlameWarp("Flame Sway", Range(0,0.12)) = 0.075
        _FlameSpeed("Flame Flow Speed", Range(0,3)) = 1.15
        _FlameFlicker("Flame Brightness Variation", Range(0,0.2)) = 0.08
        [HideInInspector] _FlameTime("Unscaled Flame Clock", Float) = -1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _AtlasRect, _Tint, _CreamTint;
                float _Opacity, _Emission, _SoftDepth, _Mode, _Head, _Repeats;
                float _FlameMotion, _FlameWarp, _FlameSpeed, _FlameFlicker, _FlameTime;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float4 screen:TEXCOORD1; half4 color:COLOR; float fog:TEXCOORD2; float eye:TEXCOORD3; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = p.positionCS; output.screen = ComputeScreenPos(p.positionCS);
                output.uv = input.uv; output.color = input.color;
                output.fog = ComputeFogFactor(p.positionCS.z); output.eye = -p.positionVS.z;
                return output;
            }
            float FlameHash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }
            float FlameNoise(float2 p)
            {
                float2 cell = floor(p), f = frac(p);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(FlameHash(cell), FlameHash(cell + float2(1,0)), f.x),
                    lerp(FlameHash(cell + float2(0,1)), FlameHash(cell + 1), f.x), f.y);
            }
            float FlameWeight(float2 uv)
            {
                // Keep the authored diamond and the base steady, rather than waving the whole card.
                float diamond = abs(uv.x - .5) / .12 + abs(uv.y - .33) / .13;
                return smoothstep(.17, .86, uv.y) * smoothstep(.95, 1.6, diamond);
            }
            float2 FlameUV(float2 uv, float time)
            {
                float weight = FlameWeight(uv);
                float noise = FlameNoise(uv * float2(4,7) - float2(time * .19, time * 1.2));
                float sway = sin(uv.y * 7 - time * 5.2) * .57
                    + sin(uv.y * 14 - time * 7.7 + 1.7) * .28 + (noise * 2 - 1) * .15;
                uv.x -= sway * weight * _FlameWarp;
                float stretch = sin(time * 4.1 + .7) * .65 + sin(time * 6.3 + 2.1) * .35;
                uv.y -= stretch * weight * _FlameWarp * .28;
                return uv;
            }
            half4 Frag(Varyings input):SV_Target
            {
                float2 uv = input.uv;
                bool flame = _FlameMotion > .5 && _Mode < .5
                    && all(abs(_AtlasRect - float4(0,0,.25,.25)) < .0001);
                float flameTime = (_FlameTime >= 0 ? _FlameTime : _Time.y) * _FlameSpeed;
                if (flame) uv = FlameUV(uv, flameTime);
                float route = 1;
                if (_Mode > 0.5)
                {
                    uv.x = frac(uv.x * _Repeats);
                    if (_Mode > 1.5 && _Mode < 2.5)
                    {
                        float ahead = 1 - smoothstep(_Head + .035, _Head + .18, input.uv.x);
                        float wake = lerp(.18, 1, saturate(1 - (_Head - input.uv.x) * 4));
                        route = ahead * wake;
                    }
                }
                float2 atlas = _AtlasRect.xy + clamp(uv, .004, .996) * _AtlasRect.zw;
                half4 symbol = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, atlas);
                if (flame)
                {
                    // Advected warm brightness stays inside the existing painted silhouette.
                    // No white additive wash, alpha flicker or neighboring atlas-cell sampling.
                    float weight = FlameWeight(input.uv);
                    float flow = FlameNoise(uv * float2(5,10) - float2(flameTime * .13, flameTime * 1.45));
                    float ribbon = sin(uv.y * 20 - flameTime * 8 + flow * 3);
                    float flicker = sin(flameTime * 8.1) * .55 + sin(flameTime * 12.7 + .9) * .3
                        + sin(flameTime * 17.3 + 2.4) * .15;
                    float paintedFire = smoothstep(.09, .25, symbol.r);
                    symbol.rgb *= 1 + weight * paintedFire * (ribbon * .10 + flicker * _FlameFlicker);
                }
                if (_Mode > .5)
                {
                    // A readable continuous thread under the meander survives the game-camera scale.
                    float edge = abs(input.uv.y - .5);
                    float band = 1 - smoothstep(.28, .46, edge);
                    float middle = 1 - smoothstep(.10, .22, edge);
                    half3 thread = lerp(half3(.075, .035, .013), half3(.8, .46, .14), middle);
                    float fill = band * .8 * (1 - symbol.a);
                    float alpha = symbol.a + fill;
                    symbol.rgb = (symbol.rgb * symbol.a + thread * fill) / max(alpha, .001);
                    symbol.a = alpha;
                }
                half alpha = symbol.a * input.color.a * _Tint.a * _Opacity * route;
                if (_SoftDepth > .001)
                {
                    float depth = LinearEyeDepth(SampleSceneDepth(input.screen.xy / input.screen.w), _ZBufferParams);
                    alpha *= saturate((depth - input.eye) / _SoftDepth);
                }
                half cream = smoothstep(.68, .77, symbol.g) * smoothstep(.86, .96, symbol.r);
                half3 rgb = symbol.rgb * input.color.rgb * lerp(_Tint.rgb, _CreamTint.rgb, cream) * _Emission;
                rgb = MixFog(rgb, input.fog);
                return half4(rgb * alpha, alpha);
            }
            ENDHLSL
        }
    }
}
