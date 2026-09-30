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
            half4 Frag(Varyings input):SV_Target
            {
                float2 uv = input.uv;
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
