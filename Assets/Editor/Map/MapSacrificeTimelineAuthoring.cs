using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

// Native tracks own every animation curve. This helper only binds and prepares card artwork.
[InitializeOnLoad]
public static class MapSacrificeTimelineAuthoring
{
    private static MapSacrificePanel preview;
    static MapSacrificeTimelineAuthoring() => EditorApplication.update += Preview;

    private static void Preview()
    {
        if (Application.isPlaying) return;
        var director = TimelineEditor.inspectedDirector;
        var panel = director != null ? director.GetComponent<MapSacrificePanel>() : null;
        bool inspecting = panel != null && AnimationMode.InAnimationMode() && director.playableGraph.IsValid();
        if (preview != null && (!inspecting || preview != panel))
        { preview.EndTimelinePreview(); preview = null; }
        if (!inspecting || panel.IsTimelinePreview) return;
        if (panel.PrepareTimelinePreview())
        { preview = panel; director.Evaluate(); panel.burnEffect.RefreshPresentation(); }
    }

    [MenuItem("Tools/Map/Bind Sacrifice Timeline")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        var scene = SceneManager.GetActiveScene();
        var panel = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MapSacrificePanel>(true)).Single();
        var director = panel.sequenceDirector;
        if (director == null || director.playableAsset is not TimelineAsset timeline)
            throw new InvalidOperationException("Assign the sacrifice Timeline on the scene's Playable Director.");
        foreach (var track in timeline.GetOutputTracks())
        {
            Object target = track.name switch
            {
                "Screen" => panel.feedbackBackdrop.GetComponent<Animator>(),
                "Card" => panel.burnEffect.cardTemplate.GetComponent<Animator>(),
                "Burn" => panel.burnEffect.GetComponent<Animator>(),
                "Transfer" => panel.flightIcon.transform.parent.GetComponent<Animator>(),
                "Player" => panel.playerSymbol.GetComponent<Animator>(),
                "Fire" => panel.sequenceDirector.GetComponent<AudioSource>(),
                _ => director.GetGenericBinding(track)
            };
            if (target == null || director.GetGenericBinding(track) == target) continue;
            Undo.RecordObject(director, "Bind sacrifice animation");
            director.SetGenericBinding(track, target);
            EditorUtility.SetDirty(director);
            EditorSceneManager.MarkSceneDirty(scene);
        }
    }

    public static void ApplyAndVerify()
    {
        Apply();
        var panel = SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MapSacrificePanel>(true)).Single();
        string before = EditorJsonUtility.ToJson(panel.sequenceDirector);
        Apply();
        if (EditorJsonUtility.ToJson(panel.sequenceDirector) != before)
            throw new InvalidOperationException("Repeated binding changed the native director.");
    }
}
