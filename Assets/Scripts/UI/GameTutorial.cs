using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// What the tutorial says and points at. It runs once per run: on the first combat, then on the first
// map screen after that combat is won. Skipping either one skips both for the rest of the run.
public static class GameTutorial
{
    private const float MapIconSpotlightScale = 2f;
    private static string combatShownRun, mapShownRun, skippedRun;

    public static IEnumerator PlayFirstCombat(BattleHud hud)
    {
        MapProgressState progress = RunSession.Instance != null ? RunSession.Instance.Progress : null;
        if (progress?.CurrentEncounter == null || !progress.CurrentEncounter.IsInitial) yield break;
        if (!Claim(progress, ref combatShownRun)) yield break;
        yield return TutorialOverlay.Play(hud.gameObject.scene, CombatSteps(hud), "Fight!", () => skippedRun = progress.RunId);
    }

    public static IEnumerator PlayFirstMap(MapController map)
    {
        MapProgressState progress = map.Session != null ? map.Session.Progress : null;
        if (progress == null || progress.CompletedCount != 1) yield break;
        // Also waits out the fade in from the reward screen.
        yield return null;
        while (!map.CanInteract) yield return null;
        if (!Claim(progress, ref mapShownRun)) yield break;
        yield return TutorialOverlay.Play(map.gameObject.scene, MapSteps(map), "Got it", () => skippedRun = progress.RunId);
    }

    private static bool Claim(MapProgressState progress, ref string shownRun)
    {
        if (progress.RunId == skippedRun || progress.RunId == shownRun) return false;
#if UNITY_EDITOR
        if (progress.TestStartNodeId != null) return false;
#endif
        shownRun = progress.RunId;
        return true;
    }

    private static List<TutorialStep> CombatSteps(BattleHud hud)
    {
        var bottom = hud.Canvas.GetComponentInChildren<CombatBottomHud>();
        return new List<TutorialStep>
        {
            new("Your Enemies",
                "Enemies attack on their own whenever their timer runs out. Once the countdown ends, the fight runs in real time.",
                rects => AddFighters(rects, hud, false)),
            new("You",
                "You attack automatically too, so your timer shows when your next hit lands.",
                rects => AddFighters(rects, hud, true)),
            new("Health",
                "This arc is your health. It carries over between battles, and if it runs out, your run is over. Shield absorbs damage first, but it doesn't last beyond the battle.",
                rects => AddHealth(rects, bottom)),
            new("Elixir",
                "Most cards cost Elixir, which refills over time. A few cost health instead, so check before you play them.",
                rects => AddElixir(rects, bottom)),
            new("Your Hand",
                "Hover over a card to read what it does, then click to play it. You'll draw a new card shortly after.",
                rects => AddHand(rects, hud)),
            new("Draw and Discard",
                "Cards you play go to the discard pile. When the draw pile is empty, the discard pile is shuffled back into it.",
                rects => AddEach(rects, hud.Canvas.GetComponentsInChildren<CardPileDisplay>())),
        };
    }

    private static List<TutorialStep> MapSteps(MapController map)
    {
        return new List<TutorialStep>
        {
            new("Your Status",
                "Your health and stats are shown here. Health you lose in battle stays lost, so keep an eye on it.",
                rects => Add(rects, map.PlayerStatusArea)),
            new("Your Deck",
                "These are the cards you take into battle. Cards you win as rewards join them here.",
                rects => AddDeck(rects, map)),
            new("Sacrifice",
                "Click a card to see its offering. Sacrificing removes the card for the rest of the run and gives you a bonus for the next battle. You can sacrifice one card before each battle.",
                rects => AddDeck(rects, map)),
            new("Deck Capacity",
                "Every card takes up capacity. If a new card won't fit, you'll have to give one up to keep it.",
                rects => Add(rects, map.CapacityArea)),
            new("Choose Your Path",
                "These are the places you can go next. Hover over one to see what's there, then click to travel. There's no turning back.",
                rects => AddAvailableNodes(rects, map)),
        };
    }

    // One spotlight around every matching fighter, since separate ones overlap when enemies stand close.
    private static void AddFighters(List<Rect> rects, BattleHud hud, bool player)
    {
        bool found = false;
        Rect group = default;
        foreach (var timer in hud.Canvas.GetComponentsInChildren<AttackCountdownView>())
        {
            BattleScript actor = timer.Actor;
            if (actor == null || !actor.isActiveAndEnabled || actor.health <= 0 || actor.CompareTag("Player") != player) continue;
            // Each timer sits inside the fighter's status panel, which floats above their model.
            Rect area = Union(ScreenRect(timer.transform.parent), ScreenRect(timer.transform));
            if (TryBodyRect(actor, out Rect bodyRect)) area = Union(area, bodyRect);
            group = found ? Union(group, area) : area;
            found = true;
        }
        if (found) rects.Add(group);
    }

    // Same filter as CombatBottomHud's head point: the visible body, without effects and particles.
    private static bool TryBodyRect(BattleScript actor, out Rect result)
    {
        result = default;
        Camera view = Camera.main;
        if (view == null) return false;
        bool found = false;
        foreach (var body in actor.GetComponentsInChildren<Renderer>())
        {
            if (!body.enabled || !body.gameObject.activeInHierarchy ||
                body is ParticleSystemRenderer || body is TrailRenderer || body is LineRenderer) continue;
            Bounds bounds = body.bounds;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 offset = Vector3.Scale(bounds.extents, new Vector3(
                    (corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                Vector3 point = view.WorldToScreenPoint(bounds.center + offset);
                if (point.z <= 0) continue;
                var pixel = new Rect(point.x, point.y, 0, 0);
                result = found ? Union(result, pixel) : pixel;
                found = true;
            }
        }
        return found;
    }

    private static void AddHealth(List<Rect> rects, CombatBottomHud bottom)
    {
        if (bottom == null) return;
        RectTransform centre = bottom.ArcCentre;
        Vector3 start = centre.TransformPoint(CombatBottomHud.OnArc(0));
        Vector3 end = centre.TransformPoint(CombatBottomHud.OnArc(1));
        Vector3 middle = centre.TransformPoint(CombatBottomHud.OnArc(.5f));
        var arc = Rect.MinMaxRect(Mathf.Min(start.x, end.x), Mathf.Min(start.y, end.y), Mathf.Max(start.x, end.x), middle.y);
        // Wide enough for the band itself and the icon on its left end.
        float reach = 60 * centre.lossyScale.x;
        arc = Rect.MinMaxRect(arc.xMin - reach, arc.yMin - reach, arc.xMax + reach, arc.yMax + reach);
        rects.Add(Union(arc, ScreenRect(bottom.HealthReadout)));
    }

    private static void AddElixir(List<Rect> rects, CombatBottomHud bottom)
    {
        if (bottom == null) return;
        Rect area = ScreenRect(bottom.ElixirBar);
        Transform icon = bottom.ElixirIcon;
        rects.Add(icon != null ? Union(area, ScreenRect(icon)) : area);
    }

    private static void AddHand(List<Rect> rects, BattleHud hud)
    {
        bool found = false;
        Rect area = default;
        foreach (BuffCards card in hud.Deck.Hand)
        {
            Rect cardRect = ScreenRect(card.transform);
            area = found ? Union(area, cardRect) : cardRect;
            found = true;
        }
        if (found) rects.Add(area);
    }

    private static void AddDeck(List<Rect> rects, MapController map)
    {
        bool found = false;
        Rect area = default;
        foreach (var card in map.DeckArea.GetComponentsInChildren<MapCardView>())
        {
            Rect cardRect = ScreenRect(card.transform);
            area = found ? Union(area, cardRect) : cardRect;
            found = true;
        }
        if (found) rects.Add(area);
    }

    private static void AddAvailableNodes(List<Rect> rects, MapController map)
    {
        foreach (MapEncounterNode node in map.Nodes)
        {
            if (node == null || !node.IsAvailable) continue;
            Rect icon = map.TravelView.IconScreenRect(node.NodeId);
            if (icon.width <= 0) continue;
            Vector2 size = icon.size * MapIconSpotlightScale;
            rects.Add(new Rect(icon.center - size * .5f, size));
        }
    }

    private static void AddEach<T>(List<Rect> rects, T[] targets) where T : Component
    {
        foreach (T target in targets) rects.Add(ScreenRect(target.transform));
    }

    private static void Add(List<Rect> rects, Transform target)
    {
        if (target != null) rects.Add(ScreenRect(target));
    }

    private static readonly Vector3[] Corners = new Vector3[4];

    // Both gameplay canvases are Screen Space Overlay, where world positions are screen pixels.
    private static Rect ScreenRect(Transform target)
    {
        ((RectTransform)target).GetWorldCorners(Corners);
        float minX = Corners[0].x, minY = Corners[0].y, maxX = minX, maxY = minY;
        foreach (Vector3 corner in Corners)
        {
            minX = Mathf.Min(minX, corner.x); minY = Mathf.Min(minY, corner.y);
            maxX = Mathf.Max(maxX, corner.x); maxY = Mathf.Max(maxY, corner.y);
        }
        return Rect.MinMaxRect(minX, minY, maxX, maxY);
    }

    private static Rect Union(Rect a, Rect b) => Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin),
        Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));
}
