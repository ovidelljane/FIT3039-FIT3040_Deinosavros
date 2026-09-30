Shader "Deinosavros/Map Review/Portal Veil"
{
    Properties
    {
        _Color("Amber Light",Color)=(1,.25,.045,1)
        _Opacity("Veil Opacity",Range(0,1))=.22
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Opacity;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION;float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0; };
            Varyings Vert(Attributes input)
            { Varyings o;o.positionCS=TransformObjectToHClip(input.positionOS.xyz);o.uv=input.uv;return o; }
            half4 Frag(Varyings input):SV_Target
            {
                float2 p=input.uv*2-1;float radius=length(p);
                float swirl=sin(radius*22-atan2(p.y,p.x)*2-_Time.y*.65)*.5+.5;
                float rim=smoothstep(.64,.95,radius)*(1-smoothstep(.98,1.05,radius));
                float density=lerp(.12,.42,swirl)+rim*.5;
                return half4(_Color.rgb*(.65+rim*1.7),_Opacity*density);
            }
            ENDHLSL
        }
    }
}
