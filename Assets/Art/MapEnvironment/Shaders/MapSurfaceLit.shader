Shader "Deinosavros/Map Surface Lit"
{
    Properties
    {
        _BaseColor("Surface Color", Color) = (0.725, 0.675, 0.553, 1)
        _BaseMap("Albedo", 2D) = "white" {}
        [Normal] _NormalMap("Normal", 2D) = "bump" {}
        _MaskMap("AO Roughness Moss", 2D) = "white" {}
        _MossColor("Moss Color", Color) = (0.37, 0.44, 0.19, 1)
        _TileMeters("Tile Size in Meters", Float) = 3.2
        _Smoothness("Smoothness", Range(0,1)) = 0.18
        _DetailStrength("Normal Strength", Range(0,1)) = 0.32
        _MossAmount("Moss Coverage", Range(0,1)) = 0
        _MacroVariation("Broad Color Variation", Range(0,0.3)) = 0.06
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Cull [_Cull]
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
        TEXTURE2D(_MaskMap); SAMPLER(sampler_MaskMap);
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _MossColor;
            float _TileMeters;
            half _Smoothness;
            half _DetailStrength;
            half _MossAmount;
            half _MacroVariation;
            half _Cull;
        CBUFFER_END
        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            half3 normalWS : TEXCOORD1;
            half fog : TEXCOORD2;
            half3 vertexLighting : TEXCOORD3;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };
        Varyings BasicVert(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
            output.positionCS = TransformWorldToHClip(output.positionWS);
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            output.fog = ComputeFogFactor(output.positionCS.z);
            return output;
        }
        float3 Weights(float3 n)
        {
            float3 weights = pow(abs(n), 4);
            return weights / max(dot(weights, 1), 0.0001);
        }
        half3 SurfaceNormal(float3 p, half3 n)
        {
            float3 uv = p / max(_TileMeters, 0.01);
            half3 weights = Weights(n);
            half3 nx = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv.zy), _DetailStrength);
            half3 ny = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv.xz), _DetailStrength);
            half3 nz = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv.xy), _DetailStrength);
            half3 detail = half3(0, nx.y, nx.x) * weights.x
                + half3(ny.x, 0, ny.y) * weights.y + half3(nz.x, nz.y, 0) * weights.z;
            return normalize(n + detail - n * dot(n, detail));
        }
        ENDHLSL
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            Varyings Vert(Attributes input)
            {
                Varyings output = BasicVert(input);
                output.vertexLighting = VertexLighting(output.positionWS, output.normalWS);
                return output;
            }
            half4 Frag(Varyings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 n = normalize(input.normalWS) * IS_FRONT_VFACE(face, 1, -1);
                float3 uv = input.positionWS / max(_TileMeters, 0.01);
                half3 weights = Weights(n);
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv.zy).rgb * weights.x
                    + SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv.xz).rgb * weights.y
                    + SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv.xy).rgb * weights.z;
                half3 mask = SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, uv.zy).rgb * weights.x
                    + SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, uv.xz).rgb * weights.y
                    + SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, uv.xy).rgb * weights.z;
                half macro = SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, input.positionWS.xz * 0.043).b;
                half moss = smoothstep(0.27, 0.75, macro) * _MossAmount * saturate(n.y * 1.5);
                albedo *= lerp(_BaseColor.rgb, _MossColor.rgb, moss);
                albedo *= 1 + (macro - 0.5) * _MacroVariation;
                InputData lighting = (InputData)0;
                lighting.positionWS = input.positionWS;
                lighting.positionCS = input.positionCS;
                lighting.normalWS = SurfaceNormal(input.positionWS, n);
                lighting.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                lighting.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                lighting.fogCoord = input.fog;
                lighting.vertexLighting = input.vertexLighting;
                lighting.bakedGI = SampleSH(lighting.normalWS);
                lighting.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                lighting.shadowMask = half4(1,1,1,1);
                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                surface.alpha = 1;
                surface.metallic = 0;
                surface.specular = half3(0.04,0.04,0.04);
                surface.smoothness = saturate(_Smoothness + (0.82 - mask.g) * 0.4);
                surface.normalTS = half3(0,0,1);
                surface.occlusion = lerp(1, mask.r, 0.35);
                half4 color = UniversalFragmentPBR(lighting, surface);
                color.rgb = MixFog(color.rgb, input.fog);
                return color;
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            float3 _LightPosition;
            Varyings ShadowVert(Attributes input)
            {
                Varyings output = BasicVert(input);
                float3 direction = _LightDirection;
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    direction = normalize(_LightPosition - output.positionWS);
                #endif
                output.positionCS = TransformWorldToHClip(ApplyShadowBias(output.positionWS, output.normalWS, direction));
                #if UNITY_REVERSED_Z
                    output.positionCS.z = min(output.positionCS.z, UNITY_NEAR_CLIP_VALUE * output.positionCS.w);
                #else
                    output.positionCS.z = max(output.positionCS.z, UNITY_NEAR_CLIP_VALUE * output.positionCS.w);
                #endif
                return output;
            }
            half4 ShadowFrag(Varyings input) : SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex BasicVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            half DepthFrag(Varyings input) : SV_Target { return input.positionCS.z; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex BasicVert
            #pragma fragment NormalFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            half4 NormalFrag(Varyings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half3 n = SurfaceNormal(input.positionWS, normalize(input.normalWS) * IS_FRONT_VFACE(face, 1, -1));
                #if defined(_GBUFFER_NORMALS_OCT)
                    return half4(PackFloat2To888(saturate(PackNormalOctQuadEncode(n) * 0.5 + 0.5)), 0);
                #else
                    return half4(n, 0);
                #endif
            }
            ENDHLSL
        }
    }
}
