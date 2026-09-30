// Review-only original foliage atlas contract:
// _BaseMap is an sRGB RGBA atlas; alpha is clipped, never blended.
// _NormalMap is an imported tangent-space normal map; _RoughnessMap is linear red.
// Use white _BaseColor, _Cutoff=.35, _NormalStrength=.25 and _Smoothness=.15 for cards.
// _UseTextures=0 uses _BaseColor only, for geometric trunks and rachises.
// All connected plant materials must share identical _Wind* values, especially the
// WORLD-Y _WindRootHeight. No object-origin phase is used, so coincident attachment
// points on separate meshes receive the same displacement. A per-plant property block
// may supply a different world root height, but must be applied to every connected part.
Shader "Deinosavros/Map Review/Foliage Lit"
{
    Properties
    {
        [MainColor] _BaseColor("Atlas Tint / Untextured Color", Color) = (1, 1, 1, 1)
        [ToggleUI] _UseTextures("Use Original Foliage Atlas", Float) = 1
        [MainTexture] _BaseMap("Original Albedo and Coverage", 2D) = "white" {}
        [Normal] [NoScaleOffset] _NormalMap("Original Tangent Normal", 2D) = "bump" {}
        [NoScaleOffset] _RoughnessMap("Original Roughness (R)", 2D) = "white" {}
        _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.35
        _NormalStrength("Normal Strength", Range(0, 1)) = 0.25
        _Smoothness("Base Smoothness", Range(0, 0.4)) = 0.15
        _ReferenceRoughness("Texture Reference Roughness", Range(0.5, 1)) = 0.85
        _RoughnessVariation("Roughness Variation", Range(0, 1)) = 0.25
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 0
        _WindDirection("World Wind Direction", Vector) = (0.82, 0, 0.57, 0)
        _WindStrength("World Wind Displacement", Range(0, 0.15)) = 0.045
        _WindSpeed("Wind Speed", Range(0, 4)) = 0.8
        _WindSpatialScale("Wind Spatial Scale", Range(0.05, 2)) = 0.3
        _WindFlutter("Leaf Flutter", Range(0, 0.5)) = 0.18
        _WindRootHeight("Root Height (World Y)", Float) = 0
        _WindBendHeight("Bend Height (World Meters)", Range(0.1, 8)) = 1.5
    }
    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="TransparentCutout"
            "Queue"="AlphaTest"
            "UniversalMaterialType"="Lit"
            "IgnoreProjector"="True"
        }
        Cull [_Cull]
        ZWrite On
        Blend One Zero

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
        TEXTURE2D(_RoughnessMap); SAMPLER(sampler_RoughnessMap);
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            float4 _BaseMap_ST;
            float4 _WindDirection;
            half _UseTextures;
            half _Cutoff;
            half _NormalStrength;
            half _Smoothness;
            half _ReferenceRoughness;
            half _RoughnessVariation;
            half _Cull;
            float _WindStrength;
            float _WindSpeed;
            float _WindSpatialScale;
            float _WindFlutter;
            float _WindRootHeight;
            float _WindBendHeight;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float4 tangentOS : TANGENT;
            float2 uv : TEXCOORD0;
            float2 lightmapUV : TEXCOORD1;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            half3 normalWS : TEXCOORD1;
            half4 tangentWS : TEXCOORD2;
            float2 uv : TEXCOORD3;
            half4 fogAndVertexLight : TEXCOORD4;
            float2 lightmapUV : TEXCOORD5;
            half3 vertexSH : TEXCOORD6;
            float4 screenShadowCoord : TEXCOORD7;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        float3 WindPosition(float3 positionWS)
        {
            float2 direction = _WindDirection.xz;
            direction = dot(direction, direction) > 0.0001 ? normalize(direction) : float2(0.82, 0.57);
            float2 side = float2(-direction.y, direction.x);
            float weight = saturate((positionWS.y - _WindRootHeight) / max(_WindBendHeight, 0.01));
            weight = weight * weight * (3.0 - 2.0 * weight);
            float phase = dot(positionWS, float3(0.73, 0.19, 0.37)) * _WindSpatialScale;
            float time = _Time.y * _WindSpeed;
            float gust = 0.72 + 0.28 * sin(time * 0.29 + phase * 0.21);
            float sway = sin(phase + time) * gust;
            float flutter = sin(phase * 2.37 - time * 1.61) * _WindFlutter;
            float2 displacement = (direction * sway + side * flutter) * _WindStrength * weight;
            return positionWS + float3(displacement.x, 0, displacement.y);
        }

        Varyings FoliageVertex(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
            float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            float3 tangentWS = TransformObjectToWorldDir(input.tangentOS.xyz, false);
            float handedness = (input.tangentOS.w < 0 ? -1.0 : 1.0) * GetOddNegativeScale();
            tangentWS -= normalWS * dot(tangentWS, normalWS);
            if (dot(tangentWS, tangentWS) < 0.0001)
                tangentWS = cross(abs(normalWS.y) < 0.9 ? float3(0,1,0) : float3(0,0,1), normalWS);
            tangentWS = SafeNormalize(tangentWS);
            float3 bitangentWS = cross(normalWS, tangentWS) * handedness;
            output.positionWS = WindPosition(positionWS);
            // Use the same deformed basis in lighting and depth normals. This also
            // works for geometric axes with no usable imported UV tangent.
            float3 bentTangent = SafeNormalize(WindPosition(positionWS + tangentWS * 0.02) - output.positionWS);
            float3 bentBitangent = SafeNormalize(WindPosition(positionWS + bitangentWS * 0.02) - output.positionWS);
            output.normalWS = SafeNormalize(cross(bentTangent, bentBitangent)) * handedness;
            output.tangentWS = half4(SafeNormalize(bentTangent - output.normalWS * dot(bentTangent, output.normalWS)), handedness);
            output.positionCS = TransformWorldToHClip(output.positionWS);
            output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
            output.fogAndVertexLight = half4(ComputeFogFactor(output.positionCS.z),
                VertexLighting(output.positionWS, output.normalWS));
            OUTPUT_LIGHTMAP_UV(input.lightmapUV, unity_LightmapST, output.lightmapUV);
            output.vertexSH = SampleSHVertex(output.normalWS);
            output.screenShadowCoord = ComputeScreenPos(output.positionCS);
            return output;
        }

        half4 ReadBase(float2 uv)
        {
            half4 color = _BaseColor;
            UNITY_BRANCH
            if (_UseTextures > 0.5h) color *= SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);
            return color;
        }
        void ClipFoliage(float2 uv)
        {
            clip(ReadBase(uv).a - _Cutoff);
        }
        half3 ReadNormal(Varyings input, half faceSign)
        {
            half3 n = normalize(input.normalWS);
            half3 result = n;
            UNITY_BRANCH
            if (_UseTextures > 0.5h && _NormalStrength > 0.001h)
            {
                half3 tangent = normalize(input.tangentWS.xyz - n * dot(input.tangentWS.xyz, n));
                half3 bitangent = cross(n, tangent) * input.tangentWS.w;
                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, input.uv), _NormalStrength);
                result = normalize(tangent * normalTS.x + bitangent * normalTS.y + n * normalTS.z);
            }
            // Thin leaves light correctly from either side without mirrored card geometry.
            return result * faceSign;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex FoliageVertex
            #pragma fragment FoliageFragment
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            void FoliageFragment(Varyings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC,
                out half4 outColor : SV_Target0
                #ifdef _WRITE_RENDERING_LAYERS
                    , out uint outRenderingLayers : SV_Target1
                #endif
            )
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 albedo = ReadBase(input.uv);
                clip(albedo.a - _Cutoff);
                half smoothness = _Smoothness;
                UNITY_BRANCH
                if (_UseTextures > 0.5h)
                {
                    half roughness = SAMPLE_TEXTURE2D(_RoughnessMap, sampler_RoughnessMap, input.uv).r;
                    smoothness = saturate(_Smoothness + (_ReferenceRoughness - roughness) * _RoughnessVariation);
                }
                InputData lighting = (InputData)0;
                lighting.positionWS = input.positionWS;
                lighting.positionCS = input.positionCS;
                lighting.normalWS = ReadNormal(input, IS_FRONT_VFACE(face, 1.0h, -1.0h));
                lighting.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    lighting.shadowCoord = input.screenShadowCoord;
                #else
                    lighting.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #endif
                lighting.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1), input.fogAndVertexLight.x);
                lighting.vertexLighting = input.fogAndVertexLight.yzw;
                lighting.bakedGI = SAMPLE_GI(input.lightmapUV, input.vertexSH, lighting.normalWS);
                lighting.shadowMask = SAMPLE_SHADOWMASK(input.lightmapUV);
                lighting.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo.rgb;
                surface.alpha = 1;
                surface.metallic = 0;
                surface.specular = half3(0.04h, 0.04h, 0.04h);
                surface.smoothness = smoothness;
                surface.normalTS = half3(0,0,1);
                surface.occlusion = 1;
                outColor = UniversalFragmentPBR(lighting, surface);
                outColor.rgb = MixFog(outColor.rgb, lighting.fogCoord);
                outColor.a = 1;
                #ifdef _WRITE_RENDERING_LAYERS
                    outRenderingLayers = EncodeMeshRenderingLayer();
                #endif
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZTest LEqual
            ColorMask 0
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex FoliageShadowVertex
            #pragma fragment FoliageShadowFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection;
            float3 _LightPosition;
            Varyings FoliageShadowVertex(Attributes input)
            {
                Varyings output = FoliageVertex(input);
                float3 direction = _LightDirection;
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    direction = normalize(_LightPosition - output.positionWS);
                #endif
                output.positionCS = ApplyShadowClamping(TransformWorldToHClip(
                    ApplyShadowBias(output.positionWS, output.normalWS, direction)));
                return output;
            }
            half4 FoliageShadowFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                ClipFoliage(input.uv);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex FoliageVertex
            #pragma fragment FoliageDepthFragment
            #pragma multi_compile_instancing
            half FoliageDepthFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                ClipFoliage(input.uv);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex FoliageVertex
            #pragma fragment FoliageNormalsFragment
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            void FoliageNormalsFragment(Varyings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC,
                out half4 outNormalWS : SV_Target0
                #ifdef _WRITE_RENDERING_LAYERS
                    , out uint outRenderingLayers : SV_Target1
                #endif
            )
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                ClipFoliage(input.uv);
                half3 normalWS = ReadNormal(input, IS_FRONT_VFACE(face, 1.0h, -1.0h));
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 octNormal = PackNormalOctQuadEncode(normalWS);
                    outNormalWS = half4(PackFloat2To888(saturate(octNormal * 0.5 + 0.5)), 0);
                #else
                    outNormalWS = half4(normalWS, 0);
                #endif
                #ifdef _WRITE_RENDERING_LAYERS
                    outRenderingLayers = EncodeMeshRenderingLayer();
                #endif
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
