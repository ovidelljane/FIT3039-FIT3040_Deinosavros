Shader "Deinosavros/UI/Card Departure"
{
    Properties
    {
        [PerRendererData] _MainTex("Card Snapshot", 2D) = "white" {}
        _Color("Tint", Color) = (1,1,1,1)
        _Gold("Old Gold", Color) = (.91,.69,.32,1)
        _Highlight("Cream Gold", Color) = (1,.90,.68,1)
        _Progress("Dissolve", Range(0,1)) = 0
        _EdgeWidth("Edge Width", Range(.002,.08)) = .018
        _EdgeStrength("Edge Light", Range(0,2)) = .65
        _Seed("Variation", Float) = 0
        _Direction("Direction", Float) = 1
        _Glint("Activation Glint", Float) = 0
        [HideInInspector] _ParticleMode("Particle Mode", Float) = 0
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
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
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
            float4 _ClipRect;
            half4 _Color, _Gold, _Highlight;
            float _Progress, _EdgeWidth, _EdgeStrength, _Seed, _Direction, _Glint, _ParticleMode;
            int _UIVertexColorAlwaysGammaSpace;
            struct Input { float4 vertex : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Output { float4 position : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; float2 local : TEXCOORD1; };
            Output Vert(Input input)
            {
                Output o;
                o.position = UnityObjectToClipPos(input.vertex);
                if (_UIVertexColorAlwaysGammaSpace && !IsGammaSpace()) input.color.rgb = UIGammaToLinear(input.color.rgb);
                o.color = input.color * _Color;
                o.uv = input.uv; o.local = input.vertex.xy;
                return o;
            }
            float Hash(float2 p)
            {
                float3 q = frac(float3(p.xyx) * .1031);
                q += dot(q, q.yzx + 33.33);
                return frac((q.x + q.y) * q.z);
            }
            float Noise(float2 p)
            {
                float2 cell = floor(p), f = frac(p);
                f = f * f * f * (f * (f * 6 - 15) + 10);
                return lerp(lerp(Hash(cell), Hash(cell + float2(1,0)), f.x),
                    lerp(Hash(cell + float2(0,1)), Hash(cell + 1), f.x), f.y);
            }
            half4 Frag(Output i) : SV_Target
            {
                half4 result;
                if (_ParticleMode > .5)
                {
                    float edge = smoothstep(0,.14,min(i.uv.x,1-i.uv.x));
                    float ridge = 1 - smoothstep(.03,.35,abs(i.uv.y-.52));
                    float outline = smoothstep(0,.16,min(i.uv.y,1-i.uv.y));
                    result.a = i.color.a * edge;
                    half3 foil = i.color.rgb * lerp(.68,1.15,ridge);
                    result.rgb = lerp(foil * .28, foil, outline) * result.a;
                }
                else
                {
                    float2 uv = i.uv;
                    float flow = Noise(uv * float2(5.3,4.7) + _Seed);
                    float detail = Noise(uv * 14 + flow * 2 + _Seed);
                    float x = _Direction >= 0 ? uv.x : 1-uv.x;
                    float field = .65 * (1-uv.y) + .35 * x + (flow-.5)*.16 + (detail-.5)*.025;
                    float threshold = lerp(-.13,1.13,_Progress);
                    float width = max(fwidth(field), .002);
                    float coverage = smoothstep(threshold-width,threshold+width,field);
                    float rim = (1-smoothstep(_EdgeWidth,_EdgeWidth+width*2,field-threshold)) * coverage;
                    half4 card = tex2D(_MainTex, uv);
                    // The UI snapshot is composited on transparent black (premultiplied RGB).
                    result.a = card.a * i.color.a * coverage;
                    result.rgb = card.rgb * i.color.rgb * i.color.a * coverage;
                    half3 gold = lerp(_Gold.rgb,_Highlight.rgb,flow);
                    result.rgb = lerp(result.rgb, gold * result.a, saturate(rim * _EdgeStrength));
                    result.rgb += gold * result.a * _Glint * pow(saturate(flow),3);
                }
                #ifdef UNITY_UI_CLIP_RECT
                result *= UnityGet2DClipping(i.local, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(result.a-.001);
                #endif
                return result;
            }
            ENDCG
        }
    }
}
