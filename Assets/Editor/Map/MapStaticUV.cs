using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Native Lit materials need ordinary UVs. Keep them stable when the source FBX is reimported.
public sealed class MapStaticUV : AssetPostprocessor
{
    public const string Model = "Assets/Art/Map/Models/Environment.fbx";
    [Serializable] public sealed class Settings { public string version = "Map Lit UV 1"; public Entry[] meshes; }
    [Serializable] public sealed class Entry
    {
        public string name;
        public Matrix4x4 world;
        public float[] meters;
        public bool[] ground;
        public Vector4 groundRect;
    }

    private void OnPostprocessModel(GameObject root)
    {
        if (assetPath != Model || !assetImporter.userData.Contains("Map Lit UV 1")) return;
        var settings = JsonUtility.FromJson<Settings>(assetImporter.userData);
        foreach (var mesh in root.GetComponentsInChildren<MeshFilter>(true).Select(filter => filter.sharedMesh).Distinct())
        {
            var entry = settings.meshes.FirstOrDefault(candidate => candidate.name == mesh.name);
            if (entry == null) continue;
            var projected = Project(mesh,entry);
            EditorUtility.CopySerialized(projected,mesh);
            UnityEngine.Object.DestroyImmediate(projected);
        }
    }

    public static Mesh Project(Mesh source,Entry entry)
    {
        var vertices = source.vertices; var normals = source.normals; var colors = source.colors;
        var originals = new List<int>(); var uv = new List<Vector2>();
        var triangles = new List<int>[source.subMeshCount];
        var normalMatrix = entry.world.inverse.transpose;
        for (int slot = 0; slot < source.subMeshCount; slot++)
        {
            var indices = source.GetTriangles(slot); triangles[slot] = new List<int>();
            var remap = new Dictionary<(int,int),int>();
            for (int i = 0; i < indices.Length; i += 3)
            {
                Vector3 n = normalMatrix.MultiplyVector(normals[indices[i]] + normals[indices[i+1]] + normals[indices[i+2]]).normalized;
                var absolute = new Vector3(Mathf.Abs(n.x),Mathf.Abs(n.y),Mathf.Abs(n.z));
                int axis = absolute.x > absolute.y && absolute.x > absolute.z ? 0 : absolute.y > absolute.z ? 1 : 2;
                int projection = entry.ground[slot] ? 6 : entry.meters[slot] <= 0 ? 7 : axis*2+(n[axis] < 0 ? 1 : 0);
                for (int corner = 0; corner < 3; corner++)
                {
                    int index = indices[i+corner];
                    if (!remap.TryGetValue((index,projection),out int mapped))
                    {
                        mapped = originals.Count; originals.Add(index); remap[(index,projection)] = mapped;
                        Vector3 p = entry.world.MultiplyPoint3x4(vertices[index]);
                        Vector2 value;
                        if (projection == 6) value = new Vector2((p.x-entry.groundRect.x)/entry.groundRect.z,(p.z-entry.groundRect.y)/entry.groundRect.w);
                        else if (projection == 7) value = source.uv.Length == vertices.Length ? source.uv[index] : Vector2.zero;
                        else
                        {
                            float sign = projection%2 == 0 ? 1 : -1;
                            value = projection/2 == 0 ? new Vector2(-p.z*sign,p.y) : projection/2 == 1 ? new Vector2(p.x,-p.z*sign) : new Vector2(p.x*sign,p.y);
                            value /= Mathf.Max(.01f,entry.meters[slot]);
                        }
                        uv.Add(value);
                    }
                    triangles[slot].Add(mapped);
                }
            }
        }
        var result = new Mesh { name = source.name, indexFormat = originals.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        result.vertices = originals.Select(index => vertices[index]).ToArray();
        result.normals = originals.Select(index => normals[index]).ToArray();
        if (colors.Length == vertices.Length) result.colors = originals.Select(index => colors[index]).ToArray();
        result.SetUVs(0,uv);
        for (int channel = 1; channel < 8; channel++)
        {
            var data = new List<Vector4>(); source.GetUVs(channel,data);
            if (data.Count != vertices.Length) continue;
            int dimension = source.GetVertexAttributeDimension((VertexAttribute)((int)VertexAttribute.TexCoord0+channel));
            if (dimension == 2) result.SetUVs(channel,originals.Select(index => (Vector2)data[index]).ToList());
            else if (dimension == 3) result.SetUVs(channel,originals.Select(index => (Vector3)data[index]).ToList());
            else result.SetUVs(channel,originals.Select(index => data[index]).ToList());
        }
        result.subMeshCount = triangles.Length;
        for (int slot = 0; slot < triangles.Length; slot++) result.SetTriangles(triangles[slot],slot,false);
        result.bounds = source.bounds; result.RecalculateTangents();
        return result;
    }
}
