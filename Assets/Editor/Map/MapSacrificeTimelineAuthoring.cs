using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;

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
}
