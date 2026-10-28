Shader "Deinosavros/UI/Sacrifice Burn"
{
    Properties
    {
        [PerRendererData] _MainTex("Card Snapshot", 2D) = "white" {}
        [HideInInspector] _Progress("Burn Progress", Range(0,1)) = 0
        [HideInInspector] _Clock("Visual Time", Float) = 0
        [HideInInspector] _Seed("Variation", Float) = 0
        _CharWidth("Char Width", Range(.01,.25)) = .085
        _EdgeWidth("Ember Width", Range(.002,.06)) = .018
        _CharColor("Charcoal", Color) = (.075,.032,.018,1)
        _EmberColor("Ember", Color) = (1,.27,.035,1)
        _HotColor("Hot Edge", Color) = (1,.78,.32,1)
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
            Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp]
            ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask]
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
            half4 _CharColor, _EmberColor, _HotColor;
            float _Progress, _Clock, _Seed, _CharWidth, _EdgeWidth, _ParticleMode;
            int _UIVertexColorAlwaysGammaSpace;
            struct Input { float4 vertex : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; float2 kind : TEXCOORD1; };
            struct Output { float4 position : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; float2 local : TEXCOORD1; float kind : TEXCOORD2; };
            Output Vert(Input v)
            {
                Output o;
                o.position = UnityObjectToClipPos(v.vertex);
                if (_UIVertexColorAlwaysGammaSpace && !IsGammaSpace()) v.color.rgb = UIGammaToLinear(v.color.rgb);
                o.color = v.color; o.uv = v.uv; o.local = v.vertex.xy; o.kind = v.kind.x;
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
                float2 c = floor(p), f = frac(p);
                f = f*f*(3-2*f);
                return lerp(lerp(Hash(c),Hash(c+float2(1,0)),f.x),lerp(Hash(c+float2(0,1)),Hash(c+1),f.x),f.y);
            }
            half4 Frag(Output i) : SV_Target
            {
                half4 result;
                if (_ParticleMode > .5)
                {
                    float2 p = i.uv * 2 - 1;
                    float shape;
                    if (i.kind > 1.5)
                    {
                        // Low-opacity, irregular wisps rather than rectangular smoke cards.
                        float n = Noise(p*2.2 + float2(_Clock*.9,_Seed));
                        shape = pow(saturate(1-dot(p,p)),2) * (.35+.65*n);
                    }
                    else if (i.kind > .5)
                        shape = 1-smoothstep(.58,.94,abs(p.x+.22*p.y)+abs(p.y)*.75);
                    else shape = 1-smoothstep(.15,1,abs(p.x)*1.15+abs(p.y)*.85);
                    result.a = shape*i.color.a;
                    result.rgb = i.color.rgb*result.a;
                }
                else
                {
                    float2 uv = i.uv;
                    float broad = Noise(uv*float2(6,8)+_Seed);
                    float fine = Noise(uv*float2(27,35)+broad+_Seed);
                    float border = min(min(uv.x,1-uv.x),min(uv.y,1-uv.y));
                    float perimeter = border + (broad-.5)*.095 + (fine-.5)*.018;
                    // Interior pinholes join the advancing perimeter, not a uniform opacity fade.
                    float holes = .22 + Noise(uv*float2(4,6)+_Seed+12)*.40;
                    float field = min(perimeter,holes);
                    float threshold = lerp(-.09,.62,_Progress);
                    float d = field-threshold;
                    float aa = max(fwidth(field),.0015);
                    float cover = smoothstep(-aa,aa,d);
                    float charMask = 1-smoothstep(0,_CharWidth,d);
                    float rim = 1-smoothstep(_EdgeWidth*.2,_EdgeWidth,d);
                    float hot = 1-smoothstep(0,_EdgeWidth*.28,d);
                    float flicker = .78 + .22*sin(_Clock*23 + uv.y*44 + broad*9);
                    float2 curl = float2(sin(uv.y*18+_Clock*8),cos(uv.x*15-_Clock*5))*.006*charMask*_Progress;
                    half4 card = tex2D(_MainTex, saturate(uv+curl));
                    // The snapshot contains premultiplied card, frame and text together.
                    half3 original = card.rgb / max(card.a,.001);
                    half3 charred = _CharColor.rgb * (.75+fine*.55);
                    half3 surface = lerp(original,charred,charMask*.95);
                    surface = lerp(surface,_EmberColor.rgb,rim*flicker);
                    surface = lerp(surface,_HotColor.rgb,hot*.85);
                    result.a = card.a*i.color.a*cover;
                    result.rgb = surface*i.color.rgb*result.a;
                }
                #ifdef UNITY_UI_CLIP_RECT
                result *= UnityGet2DClipping(i.local,_ClipRect);
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
