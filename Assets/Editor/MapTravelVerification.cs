using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class MapTravelVerification
{
    [MenuItem("Tools/Map/Verify Travel Reapply And Reload")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play Mode before verifying saved travel bindings.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Save your scene changes before running travel reload verification.");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/Map.unity")
            throw new InvalidOperationException("Open the formal Map before running travel verification.");
        MapTravelBuild.ValidateScene(scene);
        string before = Snapshot(scene, "before");
        MapTravelBuild.Install();
        Require(Snapshot(scene, "repeat-one") == before, "The first repeat application changed protected scene data or authored routes.");
        MapTravelBuild.Install();
        Require(Snapshot(scene, "repeat-two") == before, "The second repeat application changed protected scene data or authored routes.");
        scene = EditorSceneManager.OpenScene(scene.path, OpenSceneMode.Single);
        MapTravelBuild.ValidateScene(scene);
        MapTravelBuild.ValidateOriginalSnapshot(scene);
        Require(Snapshot(scene, "reload") == before, "Save and reload changed scene layout, road geometry or travel references.");
        Directory.CreateDirectory(MapTravelBuild.Evidence);
        File.WriteAllText(MapTravelBuild.Evidence + "/repeat-reload.txt",
            "PASS: two consecutive installations and a saved scene reload preserve all node/collider payloads, UI rectangles, " +
            "environment transforms, short hierarchy names, nineteen local paths, asset GUIDs and the two Boss road vertex/index streams. " +
            "Fourteen nodes, nineteen routes and one Travel root remain. No missing script components or material references.\n" +
            "Scene: " + scene.path + "\nSnapshot SHA256: " + before);
    }

    private static string Snapshot(Scene scene, string stage)
    {
        var text = new StringBuilder();
        var transforms = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
            .OrderBy(Path, StringComparer.Ordinal).ToArray();
        foreach (var item in transforms)
        {
            text.AppendLine(Path(item));
            // Layout groups recompute driven RectTransform values after OnEnable.
            // Compare their persisted payload, not transient calculated positions.
            if (item is RectTransform rectangle) text.AppendLine(EditorJsonUtility.ToJson(rectangle));
            else { Append(text, item.localPosition); Append(text, item.localRotation); Append(text, item.localScale); }
            text.AppendLine(item.gameObject.activeSelf.ToString());
            foreach (var component in item.GetComponents<Component>())
            {
                Require(component != null, "A missing script component exists at " + Path(item));
                text.AppendLine(component.GetType().FullName);
            }
            foreach (var collider in item.GetComponents<Collider>()) AppendSerialized(text, collider);
            foreach (var filter in item.GetComponents<MeshFilter>()) text.AppendLine(Reference(filter.sharedMesh));
            foreach (var renderer in item.GetComponents<Renderer>())
            {
                text.AppendLine(renderer.enabled.ToString());
                foreach (var material in renderer.sharedMaterials)
                {
                    Require(material != null, "A material reference is missing at " + Path(item));
                    text.AppendLine(Reference(material));
                }
            }
        }
        var components = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Component>(true)).ToArray();
        foreach (var node in components.OfType<MapEncounterNode>().OrderBy(n => n.NodeId, StringComparer.Ordinal)) AppendSerialized(text, node);
        foreach (var controller in components.OfType<MapController>()) AppendSerialized(text, controller);
        foreach (var view in components.OfType<MapTravelView>()) AppendSerialized(text, view);
        foreach (var path in components.OfType<MapTravelPath>().OrderBy(p => p.name, StringComparer.Ordinal))
        {
            AppendSerialized(text, path);
            if (path.overrideRoadMesh != null) text.AppendLine(MeshHash(path.overrideRoadMesh));
        }
        File.WriteAllText(MapTravelBuild.Evidence + "/snapshot-" + stage + ".txt", text.ToString());
        using var digest = SHA256.Create();
        return Convert.ToBase64String(digest.ComputeHash(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private static void AppendSerialized(StringBuilder target, Object value)
    {
        using var serialized = new SerializedObject(value);
        var property = serialized.GetIterator();
        bool enterChildren = true;
        while (property.Next(enterChildren))
        {
            // Native object-reference children expose transient runtime entity IDs.
            enterChildren = property.propertyType != SerializedPropertyType.ObjectReference;
            target.Append(property.propertyPath).Append(':');
            switch (property.propertyType)
            {
                case SerializedPropertyType.ObjectReference: target.Append(Reference(property.objectReferenceValue)); break;
                case SerializedPropertyType.Integer: target.Append(property.longValue); break;
                case SerializedPropertyType.Boolean: target.Append(property.boolValue); break;
                case SerializedPropertyType.Float: target.Append(property.doubleValue.ToString("R", CultureInfo.InvariantCulture)); break;
                case SerializedPropertyType.String: target.Append(property.stringValue); break;
                case SerializedPropertyType.Enum: target.Append(property.enumValueIndex); break;
                case SerializedPropertyType.Vector2: Append(target, property.vector2Value); break;
                case SerializedPropertyType.Vector3: Append(target, property.vector3Value); break;
                case SerializedPropertyType.Vector4: Append(target, property.vector4Value); break;
                case SerializedPropertyType.Quaternion: Append(target, property.quaternionValue); break;
                case SerializedPropertyType.Color: Append(target, (Vector4)property.colorValue); break;
                case SerializedPropertyType.ArraySize: target.Append(property.intValue); break;
            }
            target.AppendLine();
        }
    }
    private static string Reference(Object value)
    {
        if (value == null) return "null";
        if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out string guid, out long id) && !string.IsNullOrEmpty(guid))
            return guid + ":" + id.ToString(CultureInfo.InvariantCulture);
        if (value is Component component) return Path(component.transform) + ":" + component.GetType().FullName;
        if (value is GameObject gameObject) return Path(gameObject.transform);
        return value.GetType().FullName + ":" + value.name;
    }
    private static string MeshHash(Mesh mesh)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write(mesh.vertexCount); writer.Write(mesh.subMeshCount);
            foreach (var point in mesh.vertices) { writer.Write(point.x); writer.Write(point.y); writer.Write(point.z); }
            foreach (var normal in mesh.normals) { writer.Write(normal.x); writer.Write(normal.y); writer.Write(normal.z); }
            foreach (var uv in mesh.uv) { writer.Write(uv.x); writer.Write(uv.y); }
            for (int i = 0; i < mesh.subMeshCount; i++) foreach (int index in mesh.GetIndices(i)) writer.Write(index);
        }
        using var digest = SHA256.Create();
        return Convert.ToBase64String(digest.ComputeHash(stream.ToArray()));
    }
    private static string Path(Transform value) => value.parent == null ? value.name : Path(value.parent) + "/" + value.name;
    private static void Append(StringBuilder text, Vector4 value)
    {
        text.Append(value.x.ToString("R", CultureInfo.InvariantCulture)).Append(',')
            .Append(value.y.ToString("R", CultureInfo.InvariantCulture)).Append(',')
            .Append(value.z.ToString("R", CultureInfo.InvariantCulture)).Append(',')
            .Append(value.w.ToString("R", CultureInfo.InvariantCulture)).AppendLine();
    }
    private static void Append(StringBuilder text, Quaternion value) => Append(text, new Vector4(value.x, value.y, value.z, value.w));
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
