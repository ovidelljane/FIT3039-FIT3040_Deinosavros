using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// One authored slot per effect type. Each underlying Effect keeps its own lifetime.
public sealed class CardBuffBadge : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public CombatCardFeedback owner;
    public BattleScript actor;
    public StatType stat;
    public CardBuffRing ring;
    public TMP_Text seconds, stacks;
    public CanvasGroup visibility;
    public Image hitArea;
    public int StackCount { get; private set; }
    public float RemainingSeconds { get; private set; }
    private int displayedTenth = -1, displayedStacks = -1;
    private float opacity;

    private void OnEnable()
    {
        // Saved scene previews remain editable; gameplay starts with no invented buffs.
        if (Application.isPlaying && owner != null) Clear();
    }

    public void Tick(float delta, bool ended)
    {
        int count = 0;
        float nearest = float.PositiveInfinity, duration = 1;
        var effects = Effect.ActiveEffects;
        if (!ended && actor != null && actor.health > 0 && actor.isActiveAndEnabled)
            for (int i = 0; i < effects.Count; i++)
            {
                var effect = effects[i];
                if (Matches(effect))
                {
                    count++;
                    if (effect.RemainingSeconds < nearest)
                    { nearest = effect.RemainingSeconds; duration = effect.TotalSeconds; }
                }
            }
        StackCount = count; RemainingSeconds = count > 0 ? nearest : 0;
        opacity = Mathf.MoveTowards(opacity, count > 0 ? 1 : 0, delta / .2f);
        if (count > 0 && opacity == 0) opacity = .01f;
        visibility.alpha = opacity;
        visibility.blocksRaycasts = count > 0;
        hitArea.raycastTarget = count > 0;
        float fill = count > 0 ? Mathf.Clamp01(nearest / Mathf.Max(.01f, duration)) : 0;
        if (!Mathf.Approximately(ring.remaining, fill)) { ring.remaining = fill; ring.SetVerticesDirty(); }
        int tenth = Mathf.CeilToInt(RemainingSeconds * 10);
        if (tenth != displayedTenth)
        { displayedTenth = tenth; seconds.text = count > 0 ? (tenth / 10f).ToString("0.0") : ""; }
        if (count != displayedStacks)
        { displayedStacks = count; stacks.text = count > 1 ? "x" + count : ""; }
        if (count == 0) owner.HideTooltip(this);
    }

    public bool Matches(Effect effect) => effect != null && effect.IsApplied && effect.target == actor && effect.effectType == stat;
    public void OnPointerEnter(PointerEventData data) { if (StackCount > 0) owner.ShowTooltip(this); }
    public void OnPointerExit(PointerEventData data) => owner.HideTooltip(this);
    public void Clear()
    {
        StackCount = 0; RemainingSeconds = 0; opacity = 0;
        visibility.alpha = 0; visibility.blocksRaycasts = false; hitArea.raycastTarget = false;
        seconds.text = stacks.text = ""; displayedStacks = displayedTenth = -1;
        owner.HideTooltip(this);
    }
    private void OnDisable() { if (Application.isPlaying && owner != null) Clear(); }
}
