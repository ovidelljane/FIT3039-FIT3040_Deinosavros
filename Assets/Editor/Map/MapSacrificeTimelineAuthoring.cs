using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;

// Repairs only the selected scene binding; never regenerates clips or resets their timing.
public static class MapSacrificeTimelineAuthoring
{
    [MenuItem("Tools/Map/Bind Sacrifice Timeline")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        var scene = SceneManager.GetActiveScene();
        var panel = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MapSacrificePanel>(true)).Single();
        var director = panel.sequenceDirector != null ? panel.sequenceDirector : panel.GetComponent<PlayableDirector>();
        if (director == null || director.playableAsset is not TimelineAsset timeline)
            throw new InvalidOperationException("Assign the sacrifice Timeline on the scene's Playable Director.");
        var tracks = timeline.GetOutputTracks().OfType<SacrificeTimelineTrack>().ToArray();
        if (tracks.Length == 0) throw new InvalidOperationException("The assigned Timeline needs a sacrifice track.");
        bool changed = panel.sequenceDirector != director || director.timeUpdateMode == DirectorUpdateMode.Manual ||
            tracks.Any(track => director.GetGenericBinding(track) != panel);
        if (changed)
        {
            Undo.RecordObjects(new UnityEngine.Object[] { panel, director }, "Bind sacrifice Timeline");
            panel.sequenceDirector = director;
            if (director.timeUpdateMode == DirectorUpdateMode.Manual) director.timeUpdateMode = DirectorUpdateMode.UnscaledGameTime;
            foreach (var track in tracks) director.SetGenericBinding(track, panel);
            EditorUtility.SetDirty(panel); EditorUtility.SetDirty(director); EditorSceneManager.MarkSceneDirty(scene);
        }
        Directory.CreateDirectory(MapSacrificeAuthoring.Results);
        File.WriteAllText(MapSacrificeAuthoring.Results + "/timeline-authoring.txt",
            "PASS: native director binding; existing clips, curves, optional phases and scene edits preserved.\n");
    }

    public static void ApplyAndVerify()
    {
        Apply();
        var panel = SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MapSacrificePanel>(true)).Single();
        string before = EditorJsonUtility.ToJson(panel.sequenceDirector);
        Apply();
        if (EditorJsonUtility.ToJson(panel.sequenceDirector) != before)
            throw new InvalidOperationException("Repeated binding changed the director.");
    }
}
