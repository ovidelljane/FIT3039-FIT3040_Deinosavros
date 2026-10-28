using System.Linq;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

[TrackColor(.78f, .53f, .22f)]
[TrackClipType(typeof(SacrificeTimelineClip))]
[TrackBindingType(typeof(MapSacrificePanel))]
public sealed class SacrificeTimelineTrack : TrackAsset
{
    public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
    {
        var playable = ScriptPlayable<SacrificeTimelineMixer>.Create(graph, inputCount);
        var mixer = playable.GetBehaviour();
        mixer.director = graph.GetResolver() as PlayableDirector;
        var binding = mixer.director != null ? mixer.director.GetGenericBinding(this) : null;
        mixer.clips = mixer.director?.playableAsset is TimelineAsset timeline
            ? timeline.GetOutputTracks().OfType<SacrificeTimelineTrack>()
                .Where(track => !track.mutedInHierarchy && mixer.director.GetGenericBinding(track) == binding)
                .SelectMany(track => track.GetClips()).OrderBy(clip => clip.start).ToArray()
            : GetClips().OrderBy(clip => clip.start).ToArray();
        return playable;
    }

    public override void GatherProperties(PlayableDirector director, IPropertyCollector driver)
    {
        if (director.GetGenericBinding(this) is not MapSacrificePanel panel) return;
        driver.AddFromName(panel.overlay, "m_IsActive");
        driver.AddFromName(panel.confirm.gameObject, "m_IsActive");
        driver.AddFromName(panel.flightIcon.gameObject, "m_IsActive");
        driver.AddFromName(panel.cardGroup, "m_Alpha");
        driver.AddFromName(panel.illustration, "m_Sprite");
        driver.AddFromName(panel.illustration, "m_Material");
        driver.AddFromName(panel.cardName, "m_text");
        driver.AddFromName(panel.combatText, "m_text");
        driver.AddFromName(panel.feedbackBackdrop, "m_Color.a");
        driver.AddFromName(panel.flightIcon, "symbol");
        driver.AddFromName(panel.flightIcon, "m_Color");
        driver.AddFromName(panel.flightHeartCut, "m_Enabled");
        driver.AddFromComponent(panel.flightIcon.gameObject, panel.flightIcon.rectTransform);
        if (panel.playerSymbol != null)
            foreach (string name in new[] { "Core", "Halo", "Sigil" })
            {
                var target = panel.playerSymbol.transform.Find(name);
                if (target == null) continue;
                foreach (string axis in new[] { "x", "y", "z" }) driver.AddFromName(target, "m_LocalScale." + axis);
            }
    }
}

public sealed class SacrificeTimelineMixer : PlayableBehaviour
{
    public TimelineClip[] clips;
    public PlayableDirector director;
    private MapSacrificePanel previewPanel;

    public override void ProcessFrame(Playable playable, FrameData info, object playerData)
    {
        if (playerData is not MapSacrificePanel panel || director == null) return;
        if (!Application.isPlaying)
        {
            if (!panel.PrepareTimelinePreview()) return;
            previewPanel = panel;
        }
        else if (!panel.IsBusy) return;
        var frame = new SacrificeTimelineFrame { elapsed = (float)director.time, burnAge = -1, burnDuration = 1 };
        foreach (var clip in clips)
        {
            if (clip.asset is not SacrificeTimelineClip data || director.time < clip.start) continue;
            float normalized = Mathf.Clamp01((float)((director.time - clip.start) / System.Math.Max(.001, clip.duration)));
            float t = Mathf.Clamp01(data.progression.Evaluate(normalized));
            switch (data.phase)
            {
                case SacrificePhase.Dim: frame.dim = t; break;
                case SacrificePhase.Move: frame.move = t; break;
                case SacrificePhase.Expand: frame.expand = t; break;
                case SacrificePhase.Burn:
                    frame.burn = t; frame.burnAge = (float)(director.time - clip.start); frame.burnDuration = (float)clip.duration; break;
                case SacrificePhase.Transfer: frame.transfer = t; break;
                case SacrificePhase.Enhance: frame.enhance = t; break;
                case SacrificePhase.Brighten: frame.brighten = t; break;
            }
        }
        panel.ApplyTimelineFrame(frame);
    }

    public override void OnPlayableDestroy(Playable playable)
    {
        if (previewPanel != null) previewPanel.EndTimelinePreview();
    }
    public override void OnGraphStop(Playable playable)
    {
        if (!Application.isPlaying && previewPanel != null) previewPanel.EndTimelinePreview();
    }
}
