Shader "Deinosavros/Combat/Portrait"
{
    Properties
    {
        [MainTexture] _MainTex("Portrait", 2D) = "white" {}
        [MainColor] _Color("Tint", Color) = (1,1,1,1)
        _OutlineColor("Outline", Color) = (.10,.045,.025,1)
        _OutlinePixels("Outline Pixels", Range(0,3)) = 1
        _HitColor("Impact Tint", Color) = (1,.87,.62,1)
        _HitBlend("Impact", Range(0,1)) = 0
        _Defeated("Defeated", Range(0,1)) = 0
        _LightingStrength("Scene Lighting", Range(0,1)) = 1
        _AmbientStrength("Ambient Strength", Range(0,2)) = 1
        _DiffuseWrap("Soft Diffuse Wrap", Range(0,.5)) = .25
        _ReceiveShadows("Receive Shadows", Range(0,1)) = 1
        [ToggleUI] _CastShadows("Cast Silhouette Shadows", Float) = 1
        _ShadowCutoff("Shadow Alpha Cutoff", Range(.05,.95)) = .35
        [HideInInspector] _WorldOffset("World Offset", Vector) = (0,0,0,0)
        [Toggle] _ZWrite("Depth Write", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST, _MainTex_TexelSize;
            half4 _Color, _OutlineColor, _HitColor;
            float4 _WorldOffset;
            float _OutlinePixels, _HitBlend, _Defeated, _ZWrite;
            float _LightingStrength, _AmbientStrength, _DiffuseWrap, _ReceiveShadows;
            float _CastShadows, _ShadowCutoff;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "Portrait"
            Tags { "LightMode"="UniversalForwardOnly" }
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite [_ZWrite]
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #define _SURFACE_TYPE_TRANSPARENT 1
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
            };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz) + _WorldOffset.xyz;
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            half3 PortraitDiffuse(Light light, half3 normalWS)
            {
                #if defined(_LIGHT_LAYERS)
                    if (!IsMatchingLightLayer(light.layerMask, GetMeshRenderingLayer())) return 0;
                #endif
                half diffuse = saturate((dot(normalWS, light.direction) + _DiffuseWrap) / (1 + _DiffuseWrap));
                half shadow = lerp(1, light.shadowAttenuation, _ReceiveShadows);
                return light.color * (diffuse * light.distanceAttenuation * shadow);
            }

            half3 PortraitLighting(Varyings i)
            {
                // The portraits are two-sided planes. Light the visible side, including
                // mirrored enemies, without replacing the painted shading with gloss.
                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.normalWS = NormalizeNormalPerPixel(i.normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                inputData.normalWS *= dot(inputData.normalWS, inputData.viewDirectionWS) < 0 ? -1 : 1;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                half3 illumination = SampleSH(inputData.normalWS) * _AmbientStrength;
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(i.positionWS), i.positionWS, half4(1,1,1,1));
                illumination += PortraitDiffuse(mainLight, inputData.normalWS);
                #if defined(_ADDITIONAL_LIGHTS) || defined(_ADDITIONAL_LIGHTS_VERTEX)
                    #if USE_CLUSTER_LIGHT_LOOP
                        UNITY_LOOP for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); ++lightIndex)
                        {
                            CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
                            Light light = GetAdditionalLight(lightIndex, i.positionWS, half4(1,1,1,1));
                            illumination += PortraitDiffuse(light, inputData.normalWS);
                        }
                    #endif
                    uint lightCount = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(lightCount)
                        Light light = GetAdditionalLight(lightIndex, i.positionWS, half4(1,1,1,1));
                        illumination += PortraitDiffuse(light, inputData.normalWS);
                    LIGHT_LOOP_END
                #endif
                return illumination;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                half4 art = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                float2 d = _MainTex_TexelSize.xy * _OutlinePixels;
                half edge = max(max(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + float2(d.x,0)).a,
                    SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv - float2(d.x,0)).a),
                    max(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + float2(0,d.y)).a,
                    SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv - float2(0,d.y)).a));
                half alpha = max(art.a, edge * _OutlineColor.a) * _Color.a;
                clip(alpha - .008);
                half3 rgb = lerp(_OutlineColor.rgb, art.rgb * _Color.rgb, smoothstep(.05,.8,art.a));
                // Shadow-copy materials opt out. Impact tint is applied after lighting
                // so the existing hit cue remains readable even on the unlit side.
                if (_LightingStrength > .001)
                    rgb *= lerp(half3(1,1,1), PortraitLighting(i), _LightingStrength);
                rgb = lerp(rgb, _HitColor.rgb, saturate(_HitBlend) * smoothstep(.1,.8,art.a));
                half gray = dot(rgb, half3(.25,.6,.15));
                rgb = lerp(rgb, gray * half3(.56,.48,.43), _Defeated);
                return half4(rgb, alpha * (1 - _Defeated * .25));
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            float3 _LightPosition;
            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };
            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };
            ShadowVaryings ShadowVert(ShadowAttributes input)
            {
                ShadowVaryings output;
                // Idle animation already deforms the shared mesh. Match the forward
                // pass's recoil/lunge and current frame instead of casting a static card.
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz) + _WorldOffset.xyz;
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                // Mirrored, two-sided planes need a normal facing the shadow light.
                normalWS *= dot(normalWS, lightDirectionWS) < 0 ? -1 : 1;
                output.positionCS = ApplyShadowClamping(TransformWorldToHClip(
                    ApplyShadowBias(positionWS, normalWS, lightDirectionWS)));
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }
            half4 ShadowFrag(ShadowVaryings input) : SV_Target
            {
                clip(_CastShadows - .5);
                half alpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a * _Color.a;
                // Cast only the painted silhouette, not its transparent rectangular plane
                // or the cosmetic outline. Defeated bodies remain visible, so keep their shadow.
                clip(alpha - _ShadowCutoff);
                return 0;
            }
            ENDHLSL
        }
    }
}
