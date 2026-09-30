using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class RewardCardView : MonoBehaviour
{
    [SerializeField] private Image artwork;
    [SerializeField] private Image border;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text effectText;
    [SerializeField] private Button chooseButton;

    private CardDefinition definition;
    private Action<CardDefinition> onChosen;

    public void Initialize(CardDefinition cardDefinition, Action<CardDefinition> onChosenCallback, bool preserveTypography = false)
    {
        definition = cardDefinition;
        onChosen = onChosenCallback;
        if (!preserveTypography)
        {
            GameFonts.Apply(nameText, GameFontRole.Heading);
            GameFonts.Apply(effectText, GameFontRole.Body);
        }

        if (artwork != null) artwork.sprite = definition.FrontIllustration;
        if (border != null) border.sprite = definition.frontBorder;
        if (nameText != null) nameText.text = definition.displayName;
        if (effectText != null) effectText.text = GameFonts.FormatEffect(definition.GetCombatDescription()) +
            $"\nCapacity: {definition.capacityCost}";

        chooseButton.onClick.RemoveAllListeners();
        chooseButton.onClick.AddListener(() => onChosen?.Invoke(definition));
    }

    // Opt-in for the battle reward screen; opportunity cards keep their authored layout.
    public void SetPresentation(Vector2 size, Color surface, Color accent)
    {
        ((RectTransform)transform).sizeDelta = size;
        var background = GetComponent<Image>();
        if (background != null) background.color = surface;
        var outline = GetComponent<Outline>();
        if (outline == null) outline = gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(accent.r, accent.g, accent.b, .65f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);

        float height = size.y * .64f;
        var faceSize = new Vector2(height * 120f / 170f, height);
        var center = new Vector2(0, -14 - height * .5f);
        SetRect(border.rectTransform, center, faceSize);
        SetRect((RectTransform)artwork.transform.parent, center,
            new Vector2(faceSize.x * 76f / 120f, faceSize.y * 129f / 170f));
        var artRect = artwork.rectTransform;
        artRect.anchorMin = artRect.anchorMax = new Vector2(.5f, .5f);
        artRect.anchoredPosition = Vector2.zero;
        artRect.sizeDelta = new Vector2(faceSize.x * 95f / 120f, faceSize.y * 134f / 170f);
        artwork.preserveAspect = border.preserveAspect = true;
        float scale = Mathf.Min(1f, size.y / 600f);
        SetText(nameText, new Vector2(.04f, .23f), new Vector2(.96f, .32f), 29 * scale, accent);
        SetText(effectText, new Vector2(.06f, .035f), new Vector2(.94f, .22f), 28 * scale,
            new Color(.94f, .9f, .81f));
        var colors = chooseButton.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = colors.selectedColor = new Color(1.22f, 1.19f, 1.12f);
        colors.pressedColor = new Color(.85f, .8f, .72f);
        chooseButton.colors = colors;
    }

    private static void SetRect(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1);
        rect.anchoredPosition = position; rect.sizeDelta = size;
    }

    private static void SetText(TMP_Text text, Vector2 min, Vector2 max, float size, Color color)
    {
        var rect = text.rectTransform;
        rect.anchorMin = min; rect.anchorMax = max;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        text.fontSize = text.fontSizeMax = size; text.fontSizeMin = size * .78f;
        text.enableAutoSizing = true; text.color = color; text.raycastTarget = false;
        text.alignment = TextAlignmentOptions.Center;
    }
}
