Shader "Deinosavros/Map Mist Surface"
{
    Properties
    {
        _BaseColor("Color (Alpha = Opacity)", Color) = (.46,.51,.5,.94)
        _CloudSize("Cloud Size (meters)", Range(1,80)) = 12
        _Speed("Flow Speed (meters/sec)", Range(0,4)) = .65
        _Direction("Flow Direction (degrees)", Range(0,360)) = 15
        _Ceiling("Top Height", Float) = -2
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off ZTest LEqual Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Assets/Art/Map/Shaders/CloudNoise.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _CloudSize, _Speed, _Direction, _Ceiling;
            CBUFFER_END
            float _MapCloudTime;
            struct Input { float4 positionOS:POSITION; };
            struct Output { float4 positionCS:SV_POSITION; float3 world:TEXCOORD0; float fog:TEXCOORD1; };
            Output Vert(Input v)
            {
                Output o; o.world=TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS=TransformWorldToHClip(o.world); o.fog=ComputeFogFactor(o.positionCS.z); return o;
            }
            half4 Frag(Output i):SV_Target
            {
                float angle=radians(_Direction);
                float2 flow=float2(cos(angle),sin(angle))*_Speed*_MapCloudTime;
                float noise=MapCloudShape((i.world.xz-flow)/max(1,_CloudSize));
                float cloud=smoothstep(.25,.72,noise);
                float sceneDepth=LinearEyeDepth(SampleSceneDepth(i.positionCS.xy/_ScaledScreenParams.xy),_ZBufferParams);
                float surfaceDepth=-TransformWorldToView(i.world).z;
                float contact=saturate((sceneDepth-surfaceDepth)*.25);
                float height=saturate((_Ceiling-i.world.y)*.5);
                half3 color=MixFog(_BaseColor.rgb*(.65+.35*noise),i.fog);
                return half4(color,(.72+.28*cloud)*_BaseColor.a*contact*height);
            }
            ENDHLSL
        }
    }
}
