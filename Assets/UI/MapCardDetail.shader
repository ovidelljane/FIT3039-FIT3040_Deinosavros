Shader "Deinosavros/UI/Map Card Detail"
{
    Properties
    {
        [PerRendererData] _MainTex("Artwork", 2D) = "white" {}
        _CornerRadius("Corner Radius (pixels)", Float) = 48
        _ShadeHeight("Black Gradient Height", Range(0,1)) = .56
        _ShadeSoftness("Black Gradient Softness", Range(0,1)) = .25
        _ShadeOpacity("Black Gradient Opacity", Range(0,1)) = .88
        [HideInInspector] _ArtworkUV("Artwork UV", Vector) = (0,0,1,1)
        [HideInInspector] _SourceUV("Source Sprite UV", Vector) = (0,0,1,1)
        [HideInInspector] _PanelRect("Panel Rect", Vector) = (-220,-348,440,696)
        [HideInInspector] _Mirror("Mirror Artwork", Float) = 0
        [HideInInspector] _StencilComp("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip("Alpha Clip", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "CanUseSpriteAtlas"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            sampler2D _MainTex;
            float4 _ClipRect, _ArtworkUV, _SourceUV, _PanelRect;
            float _CornerRadius, _ShadeHeight, _ShadeSoftness, _ShadeOpacity, _Mirror;
            int _UIVertexColorAlwaysGammaSpace;
            struct Input { float4 vertex : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Output { float4 position : SV_POSITION; half4 color : COLOR; float2 local : TEXCOORD0; float2 uv : TEXCOORD1; };
            Output Vert(Input v)
            {
                Output o; o.position = UnityObjectToClipPos(v.vertex); o.local = v.vertex.xy; o.uv = v.uv;
                if (_UIVertexColorAlwaysGammaSpace && !IsGammaSpace()) v.color.rgb = UIGammaToLinear(v.color.rgb);
                o.color = v.color; return o;
            }
            half4 Frag(Output i) : SV_Target
            {
                float2 size = max(_PanelRect.zw, 1);
                float2 uv = (i.uv - _SourceUV.xy) / max(_SourceUV.zw,.00001);
                float2 source = uv;
                source.x = lerp(source.x, 1-source.x, _Mirror);
                half4 color = tex2D(_MainTex, _ArtworkUV.xy + source * _ArtworkUV.zw) * i.color;
                float solidHeight = _ShadeHeight * (1-saturate(_ShadeSoftness));
                float shade = (1-smoothstep(solidHeight,max(solidHeight+.0001,_ShadeHeight),uv.y)) * step(.0001,_ShadeHeight);
                float mask = saturate(shade*_ShadeOpacity);
                float alpha = lerp(color.a,i.color.a,mask);
                color.rgb *= (1-mask)*color.a/max(alpha,.00001);
                color.a = alpha;
                float radius = clamp(_CornerRadius, 0, min(size.x,size.y)*.5);
                float2 q = abs((uv-.5)*size) - (size*.5-radius);
                float distance = length(max(q,0)) + min(max(q.x,q.y),0) - radius;
                color.a *= saturate(.5-distance/max(fwidth(distance),.75));
                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(i.local,_ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a-.001);
                #endif
                return color;
            }
            ENDCG
        }
    }
}
