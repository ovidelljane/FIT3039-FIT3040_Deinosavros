using System;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

public static class MapSurfaceTextureGenerator
{
    public const string Folder = "Assets/Art/MapEnvironment/Textures/Stylized";
    private const int Size = 2048;

    public static void Generate(bool force = false)
    {
        Directory.CreateDirectory(Folder);
        foreach (string kind in new[] { "Sandstone", "Earth", "Rock" })
        {
            if (!force && File.Exists($"{Folder}/{kind}_Normal.png") && File.Exists($"{Folder}/{kind}_Albedo.png")
                && File.Exists($"{Folder}/{kind}_Mask.png")) continue;
            float[] heights = new float[Size * Size];
            Color32[] albedo = new Color32[heights.Length];
            Color32[] mask = new Color32[heights.Length];
            Parallel.For(0, Size, y =>
            {
                for (int x = 0; x < Size; x++)
                {
                    float u = (float)x / Size, v = (float)y / Size;
                    float broad = Noise(u, v, 8);
                    float medium = Noise(u, v, 32);
                    float fine = Noise(u, v, 128);
                    float grain = (medium - 0.5f) * 0.035f + (fine - 0.5f) * 0.012f;
                    float seam = 0, variation = (broad - 0.5f) * 0.07f;
                    float h = broad * 0.008f + medium * 0.002f + fine * 0.0004f;
                    if (kind == "Sandstone")
                    {
                        float row = v * 6;
                        int rowIndex = (int)row;
                        float col = u * 4 + (rowIndex % 2) * 0.5f;
                        float fy = row - rowIndex;
                        float fx = col - (int)col;
                        float edge = Mathf.Min(Mathf.Min(fx, 1 - fx), Mathf.Min(fy, 1 - fy));
                        seam = 1 - Mathf.SmoothStep(0, 1, Mathf.Clamp01((edge - 0.012f) / 0.045f));
                        variation += (Hash((int)col % 4, rowIndex, 41) - 0.5f) * 0.10f;
                        h += 0.020f * (1 - seam);
                    }
                    else if (kind == "Rock")
                    {
                        float strata = Mathf.Abs(Mathf.Sin((v * 5 + broad * 0.13f) * Mathf.PI));
                        seam = 1 - Mathf.SmoothStep(0, 0.17f, strata);
                        h += broad * 0.015f - seam * 0.004f;
                    }
                    else
                    {
                        variation += (Noise(u, v, 4) - 0.5f) * 0.10f;
                    }
                    float tone = Mathf.Clamp01(0.87f + variation + grain - seam * 0.12f);
                    int index = y * Size + x;
                    heights[index] = h;
                    albedo[index] = new Color(tone, tone * 0.988f, tone * 0.968f, 1);
                    float roughness = (kind == "Earth" ? 0.90f : 0.82f) + (medium - 0.5f) * 0.08f;
                    mask[index] = new Color(1 - seam * 0.08f, roughness, broad, 1);
                }
            });
            Color32[] normals = new Color32[heights.Length];
            Parallel.For(0, Size, y =>
            {
                for (int x = 0; x < Size; x++)
                {
                    float dx = (heights[y * Size + (x + 1) % Size] - heights[y * Size + (x + Size - 1) % Size]) * Size * 0.5f;
                    float dy = (heights[((y + 1) % Size) * Size + x] - heights[((y + Size - 1) % Size) * Size + x]) * Size * 0.5f;
                    Vector3 normal = new Vector3(-dx, -dy, 1).normalized;
                    normals[y * Size + x] = new Color(normal.x * 0.5f + 0.5f, normal.y * 0.5f + 0.5f, normal.z * 0.5f + 0.5f, 1);
                }
            });
            Write(kind + "_Albedo", albedo, true, false);
            Write(kind + "_Mask", mask, false, false);
            Write(kind + "_Normal", normals, false, true);
        }
        AssetDatabase.Refresh();
    }

    private static void Write(string name, Color32[] pixels, bool srgb, bool normal)
    {
        string path = $"{Folder}/{name}.png";
        Texture2D texture = new(Size, Size, TextureFormat.RGBA32, false, !srgb);
        texture.SetPixels32(pixels);
        texture.Apply(false);
        File.WriteAllBytes(path, texture.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.sRGBTexture = srgb;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.filterMode = FilterMode.Trilinear;
        importer.mipmapEnabled = true;
        importer.anisoLevel = 4;
        importer.maxTextureSize = Size;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.SaveAndReimport();
    }

    private static float Noise(float u, float v, int period)
    {
        float x = u * period, y = v * period;
        int ix = (int)x, iy = (int)y;
        float fx = x - ix, fy = y - iy;
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);
        return Mathf.Lerp(Mathf.Lerp(Hash(ix % period, iy % period, 17), Hash((ix + 1) % period, iy % period, 17), fx),
            Mathf.Lerp(Hash(ix % period, (iy + 1) % period, 17), Hash((ix + 1) % period, (iy + 1) % period, 17), fx), fy);
    }

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
