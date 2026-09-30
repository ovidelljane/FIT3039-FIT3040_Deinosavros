using System;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Deinosavros.MapReview.Editor
{
    /// <summary>
    /// Original deterministic 2048-pixel seamless surface maps, review assets only.
    /// No photograph, third-party texture, painted lighting, brick grid or horizontal strata.
    /// Surface Lit material contract:
    /// _UseTextures=0: _BaseColor is the absolute material color; textures are bypassed.
    /// _UseTextures=1: _BaseMap multiplies _BaseColor (normally white); _NormalMap is tangent-space;
    /// _RoughnessMap uses linear red. _Smoothness is the base, adjusted by
    /// (_ReferenceRoughness - sampledRoughness) * _RoughnessVariation.
    /// Limestone/Rock use smoothness .18, reference roughness .82, normal strength .30.
    /// Soil uses smoothness .10, reference roughness .90, normal strength .20.
    /// _TileMeters is a world-space repeat, default 3 meters; no mesh UVs are required.
    /// _MossAmount is opt-in per material. _MossHeightRange.xy specifies full/zero world Y,
    /// and _MossUpwardBias controls upward-facing preference. Do not remap foliage or portal energy.
    /// Authored ground: COLOR_0 RGB stores normalized soil/moss/exposed-rock weights, alpha 1.
    /// Terrain materials use white tint and _GroundBlendStrength=1,
    /// _SoilColor/_MossColor/_ExposedRockColor as absolute colors, _GroundDetailStrength=.6,
    /// tile 3 and normal strength .30. Top surfaces use Soil maps, smoothness .10,
    /// reference roughness .90 and _GroundReferenceColor=(.43,.38,.30).
    /// Masked cliff surfaces retain Rock maps, smoothness .18, reference roughness .82,
    /// and _GroundReferenceColor=(.51,.52,.49).
    /// Set _GroundReferenceColor using Material.SetColor with these sRGB values directly;
    /// it is a Color property, so do not pre-convert with Color.linear or Color.gamma.
    /// Ground mode replaces global noise/moss color placement with the interpolated vertex mask;
    /// a zero RGB mask falls back to soil. COLOR_0 must not be gamma converted.
    /// _GroundBlendStrength=0 preserves normal non-ground material behavior and requires no colors.
    /// Generate(true) regenerates the revised textures in place, retaining their existing GUIDs.
    /// Generate creates textures only. It does not change materials, scenes, profiles or render settings.
    /// </summary>
    public static class ReviewSurfaceTextures
    {
        public const string Folder = "Assets/MapReview/Art/Textures/OriginalSurfaces";
        public const int Size = 2048;
        public const string ShaderName = "Deinosavros/Map Review/Surface Lit";
        private enum SurfaceKind { Limestone, Soil, Rock }

        [MenuItem("Tools/Map Review/Generate Original Surface Textures")]
        public static void Generate() => Generate(false);

        public static void Generate(bool overwrite)
        {
            Directory.CreateDirectory(Folder);
            foreach (SurfaceKind kind in Enum.GetValues(typeof(SurfaceKind)))
            {
                string prefix = Folder + "/" + kind;
                if (!overwrite && File.Exists(prefix + "_Albedo.png") && File.Exists(prefix + "_Normal.png") &&
                    File.Exists(prefix + "_Roughness.png"))
                {
                    Configure(prefix + "_Albedo.png", true, false);
                    Configure(prefix + "_Normal.png", false, true);
                    Configure(prefix + "_Roughness.png", false, false);
                    continue;
                }
                GenerateSurface(kind, prefix);
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("Original review surface textures are ready. No scene or material was modified.");
        }

        private static void GenerateSurface(SurfaceKind kind, string prefix)
        {
            int count = Size * Size;
            var height = new float[count];
            var color = new Color32[count];
            var roughness = new Color32[count];
            var normal = new Color32[count];
            uint seed = 4709u + (uint)kind * 137u;
            Color baseColor = kind switch
            {
                SurfaceKind.Limestone => new Color(.67f, .64f, .57f),
                SurfaceKind.Soil => new Color(.43f, .38f, .30f),
                _ => new Color(.51f, .52f, .49f)
            };
            float baseRoughness = kind == SurfaceKind.Soil ? .90f : .82f;
            Parallel.For(0, Size, y =>
            {
                for (int x = 0; x < Size; x++)
                {
                    float u = (float)x / Size, v = (float)y / Size;
                    // Integer coordinate transforms and periodic displacement remain seamless.
                    float weather = Noise(u, v, 5, seed);
                    float warp = (Noise(u, v, 4, seed + 13) - .5f) * .035f;
                    float grain = Noise(u + warp, v - warp, 47, seed + 29);
                    float variation, relief, roughnessOffset;
                    if (kind == SurfaceKind.Limestone)
                    {
                        // Weathered shallow hollows interrupt wide, clean worn faces.
                        float pores = CellEdge(u + warp, v - warp, 14, seed + 41, out _);
                        float hollow = (1 - Smooth(.025f, .15f, pores)) * Smooth(.45f, .76f, weather);
                        float worn = Noise(u + v + warp, v - u, 11, seed + 57);
                        variation = (weather - .5f) * .025f + (worn - .5f) * .025f
                            + (grain - .5f) * .010f - hollow * .018f;
                        relief = worn * .006f - hollow * .007f + grain * .0007f;
                        roughnessOffset = (worn - .5f) * .035f + hollow * .04f;
                    }
                    else if (kind == SurfaceKind.Rock)
                    {
                        // Oblique interlocking weathered planes, not horizontal layer bands.
                        float edge = CellEdge(u + v + warp, v - u - warp, 5, seed + 73, out float cell);
                        float plane = Smooth(.015f, .30f, edge);
                        float erosion = (1 - Smooth(.018f, .11f, edge)) * Smooth(.32f, .75f, weather);
                        float mineral = Noise(u + v, v - u, 17, seed + 97);
                        variation = (cell - .5f) * .045f * plane + (mineral - .5f) * .022f
                            + (grain - .5f) * .009f - erosion * .016f;
                        // Normal relief is deliberately lower-frequency than mineral color.
                        // Two independent periodic warps prevent a shared diagonal grain direction.
                        float fractureU = u + (Noise(u + .31f, v - .12f, 3, seed + 151) - .5f) * .19f;
                        float fractureV = v + (Noise(u - .23f, v + .41f, 2, seed + 173) - .5f) * .17f;
                        float fractureEdge = CellEdge(fractureU, fractureV, 3, seed + 191, out _);
                        float fractureExtent = Smooth(.39f, .64f, Noise(u, v, 4, seed + 211));
                        float fracture = (1 - Smooth(.008f, .105f, fractureEdge)) * fractureExtent;
                        float broadFace = Noise(fractureU, fractureV, 3, seed + 233);
                        // Interrupted recesses sit between broad faces. No fine 47-cell grain,
                        // diagonal 17-cell detail or repeated raised cellular bevel in normals.
                        relief = broadFace * .004f - fracture * .011f;
                        roughnessOffset = (mineral - .5f) * .04f + erosion * .055f;
                    }
                    else
                    {
                        // Soft aggregates and interrupted sediment grain; no dark cloudy blotches.
                        float edge = CellEdge(u + warp, v - warp, 24, seed + 101, out float cell);
                        float aggregate = Smooth(.035f, .32f, edge);
                        float sediment = Noise(u + v + warp, v - u, 19, seed + 127);
                        variation = (aggregate - .35f) * .016f + (sediment - .5f) * .025f
                            + (grain - .5f) * .009f + (weather - .5f) * .008f;
                        relief = aggregate * (.0028f + cell * .0012f)
                            + sediment * .002f + grain * .0008f;
                        roughnessOffset = (sediment - .5f) * .025f + (1 - aggregate) * .015f;
                    }
                    int index = y * Size + x;
                    height[index] = relief;
                    color[index] = new Color(Mathf.Clamp01(baseColor.r + variation),
                        Mathf.Clamp01(baseColor.g + variation * .95f),
                        Mathf.Clamp01(baseColor.b + variation * .85f), 1);
                    float value = Mathf.Clamp(baseRoughness + roughnessOffset, .68f, .97f);
                    roughness[index] = new Color(value, value, value, 1);
                }
            });
            Parallel.For(0, Size, y =>
            {
                int previousY = (y + Size - 1) % Size, nextY = (y + 1) % Size;
                for (int x = 0; x < Size; x++)
                {
                    // Periodic derivatives preserve the same tangent field across every tile seam.
                    float dx = (height[y * Size + (x + 1) % Size] - height[y * Size + (x + Size - 1) % Size]) * Size * .5f;
                    float dy = (height[nextY * Size + x] - height[previousY * Size + x]) * Size * .5f;
                    // Bound steep outliers without flattening all the authored medium relief.
                    var slope = Vector2.ClampMagnitude(new Vector2(dx, dy), .8f);
                    var n = new Vector3(-slope.x, -slope.y, 1).normalized;
                    normal[y * Size + x] = new Color(n.x * .5f + .5f, n.y * .5f + .5f, n.z * .5f + .5f, 1);
                }
            });
            Write(prefix + "_Albedo.png", color, true, false);
            Write(prefix + "_Normal.png", normal, false, true);
            Write(prefix + "_Roughness.png", roughness, false, false);
        }

        private static void Write(string path, Color32[] pixels, bool srgb, bool normal)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false, !srgb);
            try
            {
                texture.SetPixels32(pixels);
                texture.Apply(false, false);
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
            Configure(path, srgb, normal);
        }

        private static void Configure(string path, bool srgb, bool normal)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.convertToNormalmap = false;
            importer.sRGBTexture = srgb;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear;
            importer.mipmapEnabled = true;
            importer.anisoLevel = 8;
            importer.maxTextureSize = Size;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.isReadable = false;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }

        private static float Smooth(float a, float b, float value)
        {
            float t = Mathf.Clamp01((value - a) / (b - a));
            return t * t * (3 - 2 * t);
        }

        private static float Noise(float u, float v, int period, uint seed)
        {
            float x = u * period, y = v * period;
            int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
            float fx = x - ix, fy = y - iy;
            // Quintic interpolation gives a smooth periodic height derivative, not grid ridges.
            fx = fx * fx * fx * (fx * (fx * 6 - 15) + 10);
            fy = fy * fy * fy * (fy * (fy * 6 - 15) + 10);
            return Mathf.Lerp(Mathf.Lerp(Hash(Wrap(ix, period), Wrap(iy, period), seed),
                    Hash(Wrap(ix + 1, period), Wrap(iy, period), seed), fx),
                Mathf.Lerp(Hash(Wrap(ix, period), Wrap(iy + 1, period), seed),
                    Hash(Wrap(ix + 1, period), Wrap(iy + 1, period), seed), fx), fy);
        }
        private static float CellEdge(float u, float v, int period, uint seed, out float cellValue)
        {
            float x = u * period, y = v * period;
            int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
            float first = float.MaxValue, second = float.MaxValue;
            cellValue = .5f;
            for (int oy = -1; oy <= 1; oy++)
                for (int ox = -1; ox <= 1; ox++)
                {
                    int cx = ix + ox, cy = iy + oy;
                    int wrappedX = Wrap(cx, period), wrappedY = Wrap(cy, period);
                    float px = cx + .2f + Hash(wrappedX, wrappedY, seed) * .6f;
                    float py = cy + .2f + Hash(wrappedX, wrappedY, seed + 19) * .6f;
                    float distance = (x - px) * (x - px) + (y - py) * (y - py);
                    if (distance < first)
                    {
                        second = first; first = distance;
                        cellValue = Hash(wrappedX, wrappedY, seed + 37);
                    }
                    else if (distance < second) second = distance;
                }
            return Mathf.Sqrt(second) - Mathf.Sqrt(first);
        }
        private static int Wrap(int value, int period) => (value % period + period) % period;
        private static float Hash(int x, int y, uint seed)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u + (uint)y * 668265263u + seed * 2246822519u;
                h = (h ^ (h >> 13)) * 1274126177u;
                return (h ^ (h >> 16)) / (float)uint.MaxValue;
            }
        }
    }
}
