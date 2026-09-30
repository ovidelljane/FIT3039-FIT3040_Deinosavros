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
                if(!MapTravelBuild.GroundHeight(path,world,out float y))unsupported++;
                else if(world.y+.55f<y+.1f)embedded++;
            }
            EditorUtility.DisplayDialog("Road height check",$"Samples: {path.points.Length}\nUncovered samples: {unsupported}\nEmbedded core samples: {embedded}","OK");
        }
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
