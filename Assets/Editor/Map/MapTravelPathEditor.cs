using System.Linq;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MapTravelPath))]
public sealed class MapTravelPathEditor:Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();var path=(MapTravelPath)target;
        EditorGUILayout.HelpBox("Points are environment-local floor positions. Amber line shows the floating core. Drag a point in Scene View to author a manual route; reimport preserves manual points.",MessageType.Info);
        if(GUILayout.Button("Check Road Height"))
        {
            int unsupported=0,embedded=0;
            foreach(var local in path.points)
            {
                var world=path.space.TransformPoint(local);
                if(!GroundHeight(path,world,out float y))unsupported++;
                else if(world.y+.55f<y+.1f)embedded++;
            }
            EditorUtility.DisplayDialog("Road height check",$"Samples: {path.points.Length}\nUncovered samples: {unsupported}\nEmbedded core samples: {embedded}","OK");
        }
    }
    private static bool GroundHeight(MapTravelPath path, Vector3 point, out float height)
    {
        height = float.NegativeInfinity;
        var surfaces = path.space.GetComponentsInChildren<MeshFilter>(true).Where(f =>
            f.name == "Island" || f.name.StartsWith("Platform_") || f == path.road ||
            f.name == "Stairs" || f.name == "Base_01" || f.name == "Landing");
        foreach (var filter in surfaces)
        {
            if (filter.sharedMesh == null) continue;
            var vertices = filter.sharedMesh.vertices;
            var triangles = filter.sharedMesh.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 a = filter.transform.TransformPoint(vertices[triangles[i]]);
                Vector3 b = filter.transform.TransformPoint(vertices[triangles[i + 1]]);
                Vector3 c = filter.transform.TransformPoint(vertices[triangles[i + 2]]);
                if (Vector3.Cross(b - a, c - a).normalized.y <= .35f) continue;
                float det = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                if (Mathf.Abs(det) < .000001f) continue;
                float u = ((b.z - c.z) * (point.x - c.x) + (c.x - b.x) * (point.z - c.z)) / det;
                float v = ((c.z - a.z) * (point.x - c.x) + (a.x - c.x) * (point.z - c.z)) / det;
                if (u < -.0001f || v < -.0001f || u + v > 1.0001f) continue;
                height = Mathf.Max(height, u * a.y + v * b.y + (1 - u - v) * c.y);
            }
        }
        return !float.IsNegativeInfinity(height);
    }
    private void OnSceneGUI()
    {
        var path=(MapTravelPath)target;if(path.space==null||path.points.Length==0)return;
        Handles.color=new Color(1,.72f,.30f);
        for(int i=0;i<path.points.Length;i++)
        {
            var world=path.space.TransformPoint(path.points[i]);
            if(i>0)Handles.DrawLine(path.space.TransformPoint(path.points[i-1])+Vector3.up*.55f,world+Vector3.up*.55f);
            float size=HandleUtility.GetHandleSize(world)*.026f;
            EditorGUI.BeginChangeCheck();var moved=Handles.FreeMoveHandle(world,size,Vector3.zero,Handles.SphereHandleCap);
            if(!EditorGUI.EndChangeCheck())continue;
            Undo.RecordObject(path,"Edit travel road point");path.points[i]=path.space.InverseTransformPoint(moved);path.manualControlPoints=true;
            path.Prepare();EditorUtility.SetDirty(path);
        }
    }
}
