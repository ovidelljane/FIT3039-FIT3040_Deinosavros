Shader "Deinosavros/Map Review/Surface Lit"
{
    Properties
    {
        [MainColor] _BaseColor("Surface Color / Texture Tint", Color) = (0.63, 0.61, 0.55, 1)
        [ToggleUI] _UseTextures("Use Original Surface Textures", Float) = 0
        [MainTexture] [NoScaleOffset] _BaseMap("Original Albedo", 2D) = "white" {}
        [Normal] [NoScaleOffset] _NormalMap("Original Normal", 2D) = "bump" {}
        [NoScaleOffset] _RoughnessMap("Original Roughness (R)", 2D) = "white" {}
        _TileMeters("World Texture Repeat (Meters)", Range(0.25, 12)) = 3
        _NormalStrength("Surface Normal Strength", Range(0, 1)) = 0.3
        _Smoothness("Base Smoothness", Range(0, 0.5)) = 0.18
        _ReferenceRoughness("Texture Reference Roughness", Range(0.5, 1)) = 0.82
        _RoughnessVariation("Texture Roughness Influence", Range(0, 1)) = 1
        _MacroVariation("Broad Color Variation", Range(0, 0.25)) = 0.06
        _MacroMeters("Broad Patch Size (Meters)", Range(2, 30)) = 7
        _MossColor("Moss Color", Color) = (0.24, 0.30, 0.14, 1)
        _MossAmount("Intentional Moss Coverage", Range(0, 1)) = 0
        _MossHeightRange("Moss Height: Full Y, Zero Y", Vector) = (-1, 2, 0, 0)
        _MossUpwardBias("Prefer Upward Facing Moss", Range(0, 1)) = 0.75
        _GroundBlendStrength("Authored Terrain RGB Mask Blend", Range(0, 1)) = 0
        _SoilColor("Authored Terrain Soil Color", Color) = (0.43, 0.38, 0.30, 1)
        _ExposedRockColor("Authored Terrain Exposed Rock Color", Color) = (0.55, 0.51, 0.43, 1)
        _GroundReferenceColor("Terrain Texture Reference Albedo", Color) = (0.43, 0.38, 0.30, 1)
        _GroundDetailStrength("Authored Terrain Texture Variation", Range(0, 1)) = 0.6
        [ToggleUI] _LayeredGround("Independent Soil Moss Rock Surfaces", Float) = 0
        [NoScaleOffset] _MossMap("Moss Albedo", 2D) = "white" {}
        [Normal] [NoScaleOffset] _MossNormal("Moss Normal", 2D) = "bump" {}
        [NoScaleOffset] _MossSurface("Moss Roughness (R)", 2D) = "white" {}
        [NoScaleOffset] _RockMap("Exposed Rock Albedo", 2D) = "white" {}
        [Normal] [NoScaleOffset] _RockNormal("Exposed Rock Normal", 2D) = "bump" {}
        [NoScaleOffset] _RockSurface("Exposed Rock Roughness (R)", 2D) = "white" {}
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" "UniversalMaterialType"="Lit" }
        Cull [_Cull]
        ZWrite On

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
        TEXTURE2D(_RoughnessMap); SAMPLER(sampler_RoughnessMap);
        TEXTURE2D(_MossMap);
        TEXTURE2D(_MossNormal);
        TEXTURE2D(_MossSurface);
        TEXTURE2D(_RockMap);
        TEXTURE2D(_RockNormal);
        TEXTURE2D(_RockSurface);
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _MossColor;
            half4 _SoilColor;
            half4 _ExposedRockColor;
            half4 _GroundReferenceColor;
            float4 _MossHeightRange;
            float _TileMeters;
            float _MacroMeters;
            half _UseTextures;
            half _NormalStrength;
            half _Smoothness;
            half _ReferenceRoughness;
            half _RoughnessVariation;
            half _MacroVariation;
            half _MossAmount;
            half _MossUpwardBias;
            half _GroundBlendStrength;
            half _GroundDetailStrength;
            half _LayeredGround;
            half _Cull;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 lightmapUV : TEXCOORD1;
            half4 color : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            half3 normalWS : TEXCOORD1;
            half4 fogAndVertexLight : TEXCOORD2;
            float2 lightmapUV : TEXCOORD3;
            half3 vertexSH : TEXCOORD4;
            float4 screenShadowCoord : TEXCOORD5;
            half3 terrainMask : TEXCOORD6;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings SurfaceVertex(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
            output.positionWS = position.positionWS;
            output.positionCS = position.positionCS;
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            output.fogAndVertexLight = half4(ComputeFogFactor(position.positionCS.z),
                VertexLighting(position.positionWS, output.normalWS));
            OUTPUT_LIGHTMAP_UV(input.lightmapUV, unity_LightmapST, output.lightmapUV);
            output.vertexSH = SampleSHVertex(output.normalWS);
            output.screenShadowCoord = ComputeScreenPos(position.positionCS);
            // COLOR_0 is data, not albedo: R soil, G rooted moss, B exposed rock.
            output.terrainMask = input.color.rgb;
            return output;
        }

        half HashCell(float3 cell)
        {
            cell = frac(cell * 0.1031);
            cell += dot(cell, cell.yzx + 33.33);
            return frac((cell.x + cell.y) * cell.z);
        }
        half BroadNoise(float3 p)
        {
            float3 cell = floor(p);
            float3 t = frac(p);
            t = t * t * (3.0 - 2.0 * t);
            return lerp(lerp(lerp(HashCell(cell), HashCell(cell + float3(1,0,0)), t.x),
                lerp(HashCell(cell + float3(0,1,0)), HashCell(cell + float3(1,1,0)), t.x), t.y),
                lerp(lerp(HashCell(cell + float3(0,0,1)), HashCell(cell + float3(1,0,1)), t.x),
                lerp(HashCell(cell + float3(0,1,1)), HashCell(cell + float3(1,1,1)), t.x), t.y), t.z);
        }
        half3 ProjectionWeights(half3 normalWS)
        {
            half3 weights = abs(normalWS);
            weights *= weights;
            weights *= weights;
            return weights / max(weights.x + weights.y + weights.z, 0.0001h);
        }
        void ProjectionUV(float3 positionWS, half3 n, out float2 uvX, out float2 uvY,
            out float2 uvZ, out half3 orientation)
        {
            orientation = half3(n.x < 0 ? -1 : 1, n.y < 0 ? -1 : 1, n.z < 0 ? -1 : 1);
            float3 p = positionWS / max(_TileMeters, 0.01);
            // Each signed projection has a right-handed tangent basis on its axis.
            uvX = float2(-p.z * orientation.x, p.y);
            uvY = float2(p.x, -p.z * orientation.y);
            uvZ = float2(p.x * orientation.z, p.y);
        }
        half3 GroundWeights(half3 mask)
        {
            mask = max(mask, 0);
            half total = mask.x + mask.y + mask.z;
            return total > 0.0001h ? mask / max(total, 0.0001h) : half3(1, 0, 0);
        }
        half3 SurfaceNormal(float3 positionWS, half3 n, half3 terrainMask)
        {
            half3 result = n;
            UNITY_BRANCH
            if (_LayeredGround > 0.5h)
            {
                // Authored upper terrain uses world XZ, independent of the imported UVs.
                // Steep island walls use a separate triplanar rock material.
                float2 uv = positionWS.xz / max(_TileMeters, 0.01);
                half3 mask = GroundWeights(terrainMask);
                half3 soil = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv), _NormalStrength);
                half3 moss = UnpackNormalScale(SAMPLE_TEXTURE2D(_MossNormal, sampler_NormalMap, uv * 1.31), _NormalStrength);
                half3 rock = UnpackNormalScale(SAMPLE_TEXTURE2D(_RockNormal, sampler_NormalMap, uv * 0.73), _NormalStrength);
                half2 slope = soil.xy / max(soil.z, 0.2h) * mask.x
                    + moss.xy / max(moss.z, 0.2h) * mask.y
                    + rock.xy / max(rock.z, 0.2h) * mask.z;
                half3 gradient = half3(slope.x, 0, slope.y);
                return normalize(n + gradient - n * dot(n, gradient));
            }
            UNITY_BRANCH
            if (_UseTextures > 0.5h && _NormalStrength >= 0.001h)
            {
                float2 uvX = 0, uvY = 0, uvZ = 0;
                half3 orientation = 1;
                ProjectionUV(positionWS, n, uvX, uvY, uvZ, orientation);
                half3 weights = ProjectionWeights(n);
                half3 nx = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uvX), _NormalStrength);
                half3 ny = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uvY), _NormalStrength);
                half3 nz = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uvZ), _NormalStrength);
                half2 sx = nx.xy / max(nx.z, 0.2h);
                half2 sy = ny.xy / max(ny.z, 0.2h);
                half2 sz = nz.xy / max(nz.z, 0.2h);
                half3 gradient = half3(0, sx.y, -sx.x * orientation.x) * weights.x
                    + half3(sy.x, 0, -sy.y * orientation.y) * weights.y
                    + half3(sz.x * orientation.z, sz.y, 0) * weights.z;
                // Project detail into the actual surface tangent plane. A flat map leaves
                // the original geometric normal unchanged, including diagonal cliff faces.
                result = normalize(n + gradient - n * dot(n, gradient));
            }
            return result;
        }
        void ReadSurface(float3 positionWS, half3 geometricNormal, half3 terrainMask,
            out half3 albedo, out half smoothness)
        {
            albedo = _BaseColor.rgb;
            smoothness = _Smoothness;
            UNITY_BRANCH
            if (_LayeredGround > 0.5h)
            {
                float2 uv = positionWS.xz / max(_TileMeters, 0.01);
                half3 mask = GroundWeights(terrainMask);
                half3 soil = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb;
                half3 moss = SAMPLE_TEXTURE2D(_MossMap, sampler_BaseMap, uv * 1.31).rgb;
                half3 rock = SAMPLE_TEXTURE2D(_RockMap, sampler_BaseMap, uv * 0.73).rgb;
                albedo *= soil * mask.x + moss * mask.y + rock * mask.z;
                half roughness = SAMPLE_TEXTURE2D(_RoughnessMap, sampler_RoughnessMap, uv).r * mask.x
                    + SAMPLE_TEXTURE2D(_MossSurface, sampler_RoughnessMap, uv * 1.31).r * mask.y
                    + SAMPLE_TEXTURE2D(_RockSurface, sampler_RoughnessMap, uv * 0.73).r * mask.z;
                smoothness = clamp(1.0h - roughness, 0.04h, 0.22h);
                return;
            }
            half textureDetail = 1;
            UNITY_BRANCH
            if (_UseTextures > 0.5h)
            {
                float2 uvX = 0, uvY = 0, uvZ = 0;
                half3 orientation = 1;
                ProjectionUV(positionWS, geometricNormal, uvX, uvY, uvZ, orientation);
                half3 weights = ProjectionWeights(geometricNormal);
                half3 textureAlbedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvX).rgb * weights.x
                    + SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvY).rgb * weights.y
                    + SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvZ).rgb * weights.z;
                albedo *= textureAlbedo;
                // Ground mode uses the selected original surface map for neutral detail.
                // Unity converts the Color property into the same space as sampled albedo.
                // Region colors are absolute colors, not multiplied by dark soil or grey rock.
                half referenceLuminance = dot(_GroundReferenceColor.rgb,
                    half3(0.2126h, 0.7152h, 0.0722h));
                textureDetail = clamp(dot(textureAlbedo, half3(0.2126h, 0.7152h, 0.0722h))
                    / max(referenceLuminance, 0.001h), 0.72h, 1.28h);
                half roughness = SAMPLE_TEXTURE2D(_RoughnessMap, sampler_RoughnessMap, uvX).r * weights.x
                    + SAMPLE_TEXTURE2D(_RoughnessMap, sampler_RoughnessMap, uvY).r * weights.y
                    + SAMPLE_TEXTURE2D(_RoughnessMap, sampler_RoughnessMap, uvZ).r * weights.z;
                smoothness = saturate(_Smoothness + (_ReferenceRoughness - roughness) * _RoughnessVariation);
            }
            half unmaskedSmoothness = smoothness;
            half patch = BroadNoise(positionWS / max(_MacroMeters, 0.1) + float3(4.17, 8.53, 1.29));
            albedo *= 1.0h + (patch - 0.5h) * _MacroVariation;
            half heightMask = 1.0h - smoothstep(_MossHeightRange.x,
                max(_MossHeightRange.x + 0.01, _MossHeightRange.y), positionWS.y);
            half facing = lerp(1.0h, smoothstep(-0.05h, 0.8h, geometricNormal.y), _MossUpwardBias);
            half moss = _MossAmount * smoothstep(0.30h, 0.68h, patch) * heightMask * facing;
            albedo = lerp(albedo, _MossColor.rgb, moss);
            smoothness = lerp(smoothness, 0.10h, moss);
            UNITY_BRANCH
            if (_GroundBlendStrength > 0.001h)
            {
                half3 mask = max(terrainMask, 0);
                half total = mask.x + mask.y + mask.z;
                mask = total > 0.0001h ? mask / max(total, 0.0001h) : half3(1, 0, 0);
                half3 regionColor = _SoilColor.rgb * mask.x + _MossColor.rgb * mask.y
                    + _ExposedRockColor.rgb * mask.z;
                regionColor *= lerp(1.0h, textureDetail, _GroundDetailStrength);
                // Deliberately bypass the unmotivated broad moss/noise field in authored mode.
                albedo = lerp(albedo, regionColor, saturate(_GroundBlendStrength));
                half regionSmoothness = saturate(dot(mask, half3(0.10h, 0.08h, 0.18h))
                    + unmaskedSmoothness - _Smoothness);
                smoothness = lerp(smoothness, regionSmoothness, saturate(_GroundBlendStrength));
            }
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex SurfaceVertex
            #pragma fragment SurfaceFragment
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

            void SurfaceFragment(Varyings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC,
                out half4 outColor : SV_Target0
                #ifdef _WRITE_RENDERING_LAYERS
                    , out uint outRenderingLayers : SV_Target1
                #endif
            )
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 n = normalize(input.normalWS) * IS_FRONT_VFACE(face, 1.0h, -1.0h);
                half3 albedo;
                half smoothness;
                ReadSurface(input.positionWS, n, input.terrainMask, albedo, smoothness);
                InputData lighting = (InputData)0;
                lighting.positionWS = input.positionWS;
                lighting.positionCS = input.positionCS;
                lighting.normalWS = SurfaceNormal(input.positionWS, n, input.terrainMask);
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
                surface.albedo = albedo;
                surface.alpha = 1;
                surface.metallic = 0;
                surface.specular = half3(0.04h, 0.04h, 0.04h);
                surface.smoothness = smoothness;
                surface.normalTS = half3(0, 0, 1);
                surface.occlusion = 1;
                // No clear coat, sheen, baked dark crevices or emissive stone.
                outColor = UniversalFragmentPBR(lighting, surface);
                outColor.rgb = MixFog(outColor.rgb, lighting.fogCoord);
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
            #pragma vertex ReviewShadowVertex
            #pragma fragment ReviewShadowFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection;
            float3 _LightPosition;
            Varyings ReviewShadowVertex(Attributes input)
            {
                Varyings output = SurfaceVertex(input);
                float3 direction = _LightDirection;
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    direction = normalize(_LightPosition - output.positionWS);
                #endif
                output.positionCS = ApplyShadowClamping(TransformWorldToHClip(
                    ApplyShadowBias(output.positionWS, output.normalWS, direction)));
                return output;
            }
            half4 ReviewShadowFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
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
            #pragma vertex SurfaceVertex
            #pragma fragment ReviewDepthFragment
            #pragma multi_compile_instancing
            half ReviewDepthFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
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
            #pragma vertex SurfaceVertex
            #pragma fragment ReviewNormalsFragment
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            void ReviewNormalsFragment(Varyings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC,
                out half4 outNormalWS : SV_Target0
                #ifdef _WRITE_RENDERING_LAYERS
                    , out uint outRenderingLayers : SV_Target1
                #endif
            )
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 n = SurfaceNormal(input.positionWS,
                    normalize(input.normalWS) * IS_FRONT_VFACE(face, 1.0h, -1.0h), input.terrainMask);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 octNormal = PackNormalOctQuadEncode(n);
                    outNormalWS = half4(PackFloat2To888(saturate(octNormal * 0.5 + 0.5)), 0);
                #else
                    outNormalWS = half4(n, 0);
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
