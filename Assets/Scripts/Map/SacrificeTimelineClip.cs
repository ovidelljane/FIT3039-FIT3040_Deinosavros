using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

public enum SacrificePhase { Dim, Move, Expand, Burn, Transfer, Enhance, Brighten }

// Each clip holds its completed progress, so seeking never depends on playback history.
public sealed class SacrificeTimelineClip : PlayableAsset, ITimelineClipAsset
{
    public SacrificePhase phase;
    public AnimationCurve progression = AnimationCurve.EaseInOut(0, 0, 1, 1);
    public ClipCaps clipCaps => ClipCaps.None;
    public override Playable CreatePlayable(PlayableGraph graph, GameObject owner) =>
        Playable.Create(graph);
}

public struct SacrificeTimelineFrame
{
    public float elapsed, dim, move, expand, burn, transfer, enhance, brighten, burnAge, burnDuration;
}
