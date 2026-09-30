Shader "Deinosavros/Combat/Clouds"
{
    Properties
    {
        [MainTexture] _BaseMap("Original Sky", 2D) = "white" {}
        [MainColor] _BaseColor("Tint", Color) = (1,1,1,1)
        _DriftSpeed("Horizontal Drift", Range(-.03,.03)) = .007
        _Billow("Cloud Billow", Range(0,.02)) = .003
        [HideInInspector] _PreviewTime("Preview Time", Float) = -1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-20" "RenderType"="Transparent" }
        Pass
        {
            Name "CloudDrift"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                float _DriftSpeed, _Billow, _PreviewTime;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(Attributes v)
            {
                Varyings o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = TRANSFORM_TEX(v.uv,_BaseMap); return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                float time = _PreviewTime >= 0 ? _PreviewTime : _Time.y;
                float2 uv = i.uv;
                float billow = sin(uv.x * 9 + time * .19) * sin(uv.y * 6 - time * .13);
                uv.x += time * _DriftSpeed * lerp(.65,1,saturate(uv.y)) + billow * _Billow;
                // Mirrored extension is continuous even when the source illustration is not tileable.
                uv.x = 1 - abs(frac(uv.x * .5) * 2 - 1);
                uv.y = clamp(uv.y + billow * _Billow * sin(saturate(uv.y) * PI), .002, .998);
                return SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,uv) * _BaseColor;
            }
            ENDHLSL
        }
    }
}
