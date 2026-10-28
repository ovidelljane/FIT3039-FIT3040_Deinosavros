Shader "Deinosavros/Map Camera Clouds"
{
    Properties
    {
        _CloudColor("Color (Alpha = Opacity)", Color) = (.82,.82,.79,.14)
        _CloudScale("Cloud Scale", Float) = 3.4
        _Flow("Flow", Vector) = (.024,.006,0,0)
        _CenterClarity("Clear Center", Range(0,1)) = .8
        [HideInInspector] _Aspect("Aspect", Float) = 1.777778
    }
    SubShader
    {
        Tags { "Queue"="Transparent+100" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Art/Map/Shaders/CloudNoise.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _CloudColor, _Flow;
                float _CloudScale, _CenterClarity, _Aspect;
            CBUFFER_END
            float _MapCloudTime;
            struct Input { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct Output { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };
            Output Vert(Input v)
            {
                Output o; o.positionCS=TransformObjectToHClip(v.positionOS.xyz); o.uv=v.uv; return o;
            }
            half4 Frag(Output i):SV_Target
            {
                float2 uv=i.uv-.5;
                float2 drifting=uv-_Flow.xy*_MapCloudTime;
                drifting.x*=_Aspect;
                float noise=MapCloudShape(drifting*max(.1,_CloudScale));
                float cloud=smoothstep(.34,.70,noise);
                float clearCenter=smoothstep(.12,.65,length(uv*float2(_Aspect*.72,1)));
                float edge=smoothstep(0,.035,min(min(i.uv.x,1-i.uv.x),min(i.uv.y,1-i.uv.y)));
                float alpha=cloud*_CloudColor.a*lerp(1,clearCenter,_CenterClarity)*edge;
                return half4(_CloudColor.rgb*(.82+.18*noise),alpha);
            }
            ENDHLSL
        }
    }
}