using System.Globalization;
using UnityEngine;
using Deinosavros.MapReview;

// Read-only presentation of the existing offering rules. Never applies a modifier.
public sealed class MapSacrificePreview
{
    public string InstanceId, Title, Value, BackValue, Details, Limit, Compact, Reason;
    public CardDefinition Card;
    public StatusSymbol Symbol;
    public Color Accent;
    public int CapacityBefore, CapacityAfter, CapacityLimit, FreedCapacity;
    public bool CanConfirm;

    public static string Number(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    public static string Rate(float interval) => (1f / Mathf.Max(.01f, interval)).ToString("0.00", CultureInfo.InvariantCulture);

    public static MapSacrificePreview For(RunSession session, CardDefinition card, string instanceId = null, PendingEncounterModifier? prepared = null)
    {
        var view = new MapSacrificePreview { Card = card, InstanceId = instanceId };
        if (card == null) { view.Reason = "This card is no longer available."; return view; }
        var effect = card.overworldEffect;
        if (prepared.HasValue) { effect.effectType = prepared.Value.effectType; effect.magnitude = prepared.Value.magnitude; effect.triggerEvery = prepared.Value.triggerEvery; }
        float amount = effect.magnitude;
        string n = Number(amount);
        switch (effect.effectType)
        {
            case OverworldEffectType.DivinePunishment:
                view.Title = "Divine Punishment"; view.Value = n + " Divine Damage";
                view.Details = "Every " + Mathf.Max(1, effect.triggerEvery) + " damage instances you deal, strike a random enemy.";
                view.Limit = "Punishment does not charge itself. Next battle only.";
                view.Compact = n + " damage every " + Mathf.Max(1, effect.triggerEvery) + " hits";
                view.Symbol = StatusSymbol.Damage; view.Accent = new Color(.92f, .74f, .40f);
                break;
            case OverworldEffectType.ReduceEnemyAttackFrequency:
                view.Title = "Slowed Foes"; view.Value = "Enemy Attack Rate -" + n + "%";
                view.Details = "Enemies attack " + n + "% less frequently for the entire next battle.";
                view.Limit = "Includes the Boss. Does not carry into later battles.";
                view.Compact = "Enemy attack rate -" + n + "%";
                view.Symbol = StatusSymbol.Speed; view.Accent = new Color(.73f, .60f, .88f);
                break;
            case OverworldEffectType.ImprovePlayerAttackSpeed:
                float minimum = session != null ? session.MinimumOfferingAttackInterval : RunBalance.Default.minimumOfferingAttackInterval;
                float before = session != null ? session.PlayerAttackSpeed : 5f;
                float after = Mathf.Max(minimum, before - amount);
                view.Title = "Faster Attacks"; view.Value = "-" + n + "s Attack Interval";
                view.Details = "Attack interval   " + Number(before) + "s  \u2192  " + Number(after) + "s\n" +
                    "Attacks/sec         " + Rate(before) + "  \u2192  " + Rate(after);
                view.Limit = "Minimum interval: " + Number(minimum) + "s.";
                view.Compact = "-" + n + "s attack interval";
                view.Symbol = StatusSymbol.Speed; view.Accent = new Color(.91f, .73f, .40f);
                break;
            case OverworldEffectType.GrantStartingShield:
                view.Title = "Divine Protection"; view.Value = "+" + n + " Shield";
                view.Details = "Gain " + n + " Shield when the next battle begins.";
                view.Limit = "Unused Shield ends with the battle.";
                view.Compact = "+" + n + " Shield";
                view.Symbol = StatusSymbol.Shield; view.Accent = new Color(.56f, .79f, .83f);
                break;
            case OverworldEffectType.ReduceEnemyStartingHealth:
                view.Title = "Weakened Foes"; view.Value = "Enemies start at " + n + "% HP";
                view.Details = "Applied to enemies when battle begins.";
                view.Limit = "Rounded up; minimum 1 HP.";
                view.Compact = "Enemies start at " + n + "% HP";
                view.Symbol = StatusSymbol.Health; view.Accent = new Color(.82f, .48f, .43f);
                break;
            case OverworldEffectType.IncreaseStartingElixir:
                view.Title = "Elixir Reserve"; view.Value = "+" + n + " Maximum Elixir";
                view.Details = "Restore " + n + " Elixir at battle start.";
                view.Limit = "Next battle only. Restore up to the new maximum.";
                view.Compact = "+" + n + " max Elixir / restore " + n;
                view.Symbol = StatusSymbol.Elixir; view.Accent = new Color(.73f, .60f, .88f);
                break;
        }
        view.BackValue = effect.effectType switch
        {
            OverworldEffectType.IncreaseStartingElixir => "+" + n + " Max Elixir\nRestore " + n,
            OverworldEffectType.DivinePunishment => n + " Damage\nEvery " + Mathf.Max(1, effect.triggerEvery) + " hits",
            OverworldEffectType.ReduceEnemyAttackFrequency => "Enemy Attack Rate\n-" + n + "%",
            _ => view.Value
        };
        view.CapacityBefore = session != null ? session.DeckCapacity : 0;
        view.CapacityLimit = session != null ? session.DeckCapacityLimit : RunBalance.Default.deckCapacity;
        view.FreedCapacity = Mathf.Max(0, card.capacityCost);
        view.CapacityAfter = Mathf.Max(0, view.CapacityBefore - view.FreedCapacity);
        bool found = false;
        if (session != null)
            foreach (var instance in session.RunDeck)
                if (instance.instanceId == instanceId && instance.definition == card && !instance.sacrificed) { found = true; break; }
        view.Reason = !found ? "This card is no longer available." :
            session.Progress != null && session.Progress.Phase != MapProgressPhase.OnMap ? "Return to the map to sacrifice a card." :
            session.SacrificeUsed ? "Already sacrificed before this battle." : "";
        view.CanConfirm = string.IsNullOrEmpty(view.Reason);
        return view;
    }

    public static MapSacrificePreview Pending(RunSession session)
    {
        if (session == null || !session.HasPendingModifier) return null;
        foreach (var instance in session.RunDeck)
            if (instance.definition != null && instance.definition.cardId == session.PendingModifier.sourceCardId)
                return For(session, instance.definition, instance.instanceId, session.PendingModifier);
        return null;
    }
}
