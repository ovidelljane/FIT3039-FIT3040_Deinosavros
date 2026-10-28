Shader "Deinosavros/UI/Menu Embers"
{
    Properties
    {
        [PerRendererData] _MainTex("Original Artwork", 2D) = "white" {}
        _Color("Tint", Color) = (1,1,1,1)
        _FlowSpeed("Flow Speed", Range(0,3)) = 0.18
        _Distortion("Heat Distortion", Range(0,0.15)) = 0.009
        _FireStrength("Warm Firelight", Range(0,3)) = 0.38
        _EmberStrength("Drifting Embers", Range(0,5)) = 0.6
        _WarmColor("Fire Color", Color) = (1,0.42,0.08,1)
        _EmberColor("Ember Color", Color) = (1,0.78,0.34,1)
        [HideInInspector] _PreviewTime("Preview Time", Float) = -1
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
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "CanUseSpriteAtlas"="False" }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "WatercolorFire"
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            half4 _Color, _WarmColor, _EmberColor, _TextureSampleAdd;
            float4 _ClipRect;
            float _UIMaskSoftnessX, _UIMaskSoftnessY;
            int _UIVertexColorAlwaysGammaSpace;
            float _FlowSpeed, _Distortion, _FireStrength, _EmberStrength, _PreviewTime;

            struct Attributes
            {
                float4 vertex : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 position : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 mask : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.position = UnityObjectToClipPos(input.vertex);
                if (_UIVertexColorAlwaysGammaSpace && !IsGammaSpace()) input.color.rgb = UIGammaToLinear(input.color.rgb);
                output.color = input.color * _Color;
                output.uv = input.uv;
                float2 pixelSize = output.position.w;
                pixelSize /= abs(float2(_ScreenParams.x * UNITY_MATRIX_P[0][0], _ScreenParams.y * UNITY_MATRIX_P[1][1]));
                float4 rect = clamp(_ClipRect, -2e10, 2e10);
                output.mask = float4(input.vertex.xy * 2 - rect.xy - rect.zw,
                    .25 / (.25 * float2(_UIMaskSoftnessX, _UIMaskSoftnessY) + abs(pixelSize)));
                return output;
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
                // Quintic interpolation keeps the heat field continuous across cells.
                f = f * f * f * (f * (f * 6 - 15) + 10);
                return lerp(lerp(Hash(cell), Hash(cell + float2(1,0)), f.x),
                    lerp(Hash(cell + float2(0,1)), Hash(cell + 1), f.x), f.y);
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                float time = (_PreviewTime >= 0 ? _PreviewTime : _Time.y) * _FlowSpeed;
                float sides = smoothstep(.2, .48, abs(uv.x - .5));
                float bottom = 1 - smoothstep(.02, .5, uv.y);
                float quiet = 1 - smoothstep(.18, .52, length((uv - float2(.5,.59)) * float2(1.4,1.6)));
                float activity = max(sides, bottom * .85) * (1 - quiet * .97);
                float noiseA = Noise(uv * float2(5,3) - float2(time * .21, time));
                float noiseB = Noise(uv * float2(8,4) + float2(noiseA * 1.8, -time * 1.3));
                float fine = Noise(uv * float2(13,6) + float2(noiseB, -time * 1.7));

                // Anchor the image edges instead of scrolling or repeating non-tileable artwork.
                float border = smoothstep(0, .035, min(min(uv.x, 1-uv.x), min(uv.y, 1-uv.y)));
                float2 flow = float2((noiseA-.5)*1.3 + (noiseB-.5)*.5, (noiseB-.5)*.7);
                float2 warped = uv + flow * _Distortion * activity * border;
                warped = clamp(warped, _MainTex_TexelSize.xy * .5, 1 - _MainTex_TexelSize.xy * .5);
                half4 color = tex2D(_MainTex, warped) + _TextureSampleAdd;

                float ribbons = pow(saturate(1 - abs(sin((uv.x * 7 + noiseA * 1.6 + noiseB * .7) * 3.14159))), 3);
                float fire = ribbons * smoothstep(.18,.82, noiseB*.7 + fine*.3);
                float heat = activity * _FireStrength;
                // Warm pigment and restrained reflected light preserve the paper's color and grain.
                color.rgb *= 1 + heat * (noiseA-.5) * .25;
                color.rgb = lerp(color.rgb, color.rgb * .7 + _WarmColor.rgb * .3, heat * fire);
                color.rgb += _WarmColor.rgb * heat * fire * .16;

                float2 grid = uv * float2(22,12) - float2(time*.14, time*.9);
                float2 cell = floor(grid);
                float seed = Hash(cell + 37);
                float2 center = float2(.2 + Hash(cell)*.6, .2 + Hash(cell+19)*.6);
                center.x += sin(time*1.6 + seed*24)*.09;
                float distance = length((frac(grid)-center) * float2(1,1.45));
                float radius = lerp(.022,.044,seed);
                float aa = max(fwidth(distance), .003);
                float spark = (1-smoothstep(radius-aa,radius+aa,distance)) * step(.88,seed);
                float glow = exp2(-distance*distance*180) * .12 * step(.88,seed);
                float life = .65 + .35*sin(time*2 + seed*39);
                color.rgb += _EmberColor.rgb * (spark + glow) * activity * _EmberStrength * life;
                color *= input.color;
                #ifdef UNITY_UI_CLIP_RECT
                    half2 mask = saturate((_ClipRect.zw - _ClipRect.xy - abs(input.mask.xy)) * input.mask.zw);
                    color.a *= mask.x * mask.y;
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                    clip(color.a - .001);
                #endif
                return color;
            }
            ENDCG
        }
    }
}
