using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// The approved Unity-only override bends the two outer approaches into the existing stairs.
// Vertices and indices come from the original road; no extra slabs or scene objects are added.
public static class MapTravelBossRoads
{
    public static Vector3 StairEntry(MeshFilter stairs,Vector3 landing,Vector3 direction)
    {
        float nearest=float.PositiveInfinity;
        foreach(var vertex in stairs.sharedMesh.vertices)
            nearest=Mathf.Min(nearest,Vector3.Dot(stairs.transform.TransformPoint(vertex)-landing,direction));
        var entry=landing+direction*(nearest-.10f);entry.y=landing.y;return entry;
    }
    public static Vector3[] Apply(MapTravelPath path,Vector3 start,Vector3 landing,Vector3 centralStart,MeshFilter stairs,Func<Vector3,float> ground)
    {
        if(path.sourceRoadMesh==null||path.road.sharedMesh!=path.overrideRoadMesh)path.sourceRoadMesh=path.road.sharedMesh;
        var original=path.sourceRoadMesh;var mesh=Object.Instantiate(original);mesh.name=path.name+" Stair Approach";
        Vector3 originalDirection=landing-start;originalDirection.y=0;originalDirection.Normalize();
        Vector3 axis=landing-centralStart;axis.y=0;axis.Normalize();
        Vector3 lateral=Vector3.Cross(Vector3.up,originalDirection).normalized;
        float minimum=float.PositiveInfinity,maximum=float.NegativeInfinity;
        var source=original.vertices;
        foreach(var vertex in source)
        {
            float t=Vector3.Dot(path.road.transform.TransformPoint(vertex)-start,originalDirection);
            minimum=Mathf.Min(minimum,t);maximum=Mathf.Max(maximum,t);
        }
        Vector3 first=start+originalDirection*minimum,entry=StairEntry(stairs,landing,axis);
        first.y=start.y;
        float kept=Mathf.Min(.85f,(maximum-minimum)*.22f);
        Vector3 bend=first+originalDirection*kept;
        float side=Mathf.Sign(Vector3.Dot(start-centralStart,Vector3.Cross(Vector3.up,axis)));
        Vector3 a=bend+originalDirection*.58f;
        Vector3 b=entry-axis*.78f+Vector3.Cross(Vector3.up,axis)*side*.16f;
        Vector3 Curve(float t)
        {
            float distance=t*(maximum-minimum);
            if(distance<=kept)return first+originalDirection*distance;
            float u=Mathf.InverseLerp(kept,maximum-minimum,distance),v=1-u;
            return v*v*v*bend+3*v*v*u*a+3*v*u*u*b+u*u*u*entry;
        }
        Vector3 Tangent(float t)
        {
            var d=Curve(Mathf.Min(1,t+.002f))-Curve(Mathf.Max(0,t-.002f));d.y=0;return d.normalized;
        }
        float top=float.NegativeInfinity;
        foreach(var vertex in source)top=Mathf.Max(top,path.road.transform.TransformPoint(vertex).y);
        for(int i=0;i<source.Length;i++)
        {
            Vector3 old=path.road.transform.TransformPoint(source[i]);
            float t=Mathf.InverseLerp(minimum,maximum,Vector3.Dot(old-start,originalDirection));
            float width=Vector3.Dot(old-start,lateral);
            Vector3 center=Curve(t),sideways=Vector3.Cross(Vector3.up,Tangent(t)).normalized;
            Vector3 point=center+sideways*width;
            point.y=ground(point)+.055f+(old.y-top);
            source[i]=path.road.transform.InverseTransformPoint(point);
        }
        mesh.vertices=source;mesh.RecalculateNormals();mesh.RecalculateBounds();
        Directory.CreateDirectory(MapTravelArt.Root);
        string asset=MapTravelArt.Root+"/"+path.name+"Road.asset";
        var saved=AssetDatabase.LoadAssetAtPath<Mesh>(asset);
        if(saved==null){saved=mesh;AssetDatabase.CreateAsset(saved,asset);}
        else{EditorUtility.CopySerialized(mesh,saved);Object.DestroyImmediate(mesh);}
        EditorUtility.SetDirty(saved);AssetDatabase.SaveAssetIfDirty(saved);
        path.overrideRoadMesh=saved;path.road.sharedMesh=saved;EditorUtility.SetDirty(path.road);
        var guide=new System.Collections.Generic.List<Vector3>{start};
        int count=Mathf.Max(16,Mathf.CeilToInt((maximum-minimum)/.20f));
        for(int i=0;i<=count;i++)guide.Add(Curve(i/(float)count));
        guide.Add(landing);
        return guide.ToArray();
    }
}
