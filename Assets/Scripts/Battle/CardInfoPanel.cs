using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Shows the hovered battle card's details on the right side of the screen.
public sealed class CardInfoPanel : MonoBehaviour
{
    private static CardInfoPanel instance;

    private TextMeshProUGUI titleText;
    private TextMeshProUGUI costText;
    private TextMeshProUGUI effectText;
    private BuffCards shownCard;
    private CardEffectValues lastValues;

    private void LateUpdate()
    {
        if (shownCard == null || !shownCard.isActiveAndEnabled) { gameObject.SetActive(false); return; }
        var values = shownCard.Values;
        if (values.Amount == lastValues.Amount && values.Duration == lastValues.Duration && values.Cost == lastValues.Cost && values.Stat == lastValues.Stat) return;
        lastValues = values;
        costText.text = $"Cost: {shownCard.Cost} {(shownCard.UsesHealthCost ? "HP" : "Elixir")}";
        effectText.text = GameFonts.FormatEffect(shownCard.Description);
    }

    public static void Show(BuffCards card, Canvas canvas, string title, int elixirCost, string effect)
    {
        if (instance == null) instance = Create(canvas);
        instance.shownCard = card;
        instance.lastValues = card.Values;
        instance.titleText.text = title;
        instance.costText.text = $"Cost: {elixirCost} {(card.UsesHealthCost ? "HP" : "Elixir")}";
        instance.effectText.text = GameFonts.FormatEffect(effect);
        instance.gameObject.SetActive(true);
        instance.transform.SetAsLastSibling();
    }

    public static void Hide(BuffCards card)
    {
        if (instance == null || instance.shownCard != card) return;
        instance.shownCard = null;
        instance.gameObject.SetActive(false);
    }

    private static CardInfoPanel Create(Canvas canvas)
    {
        var panelObject = new GameObject("CardInfoPanel", typeof(RectTransform));
        panelObject.transform.SetParent(canvas.transform, false);

        RectTransform rect = (RectTransform)panelObject.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0.5f);
        rect.anchoredPosition = new Vector2(-40f, 0f);
        rect.sizeDelta = new Vector2(360f, 0f);

        // The panel must not block raycasts, or it would steal hover from the cards.
        Image background = panelObject.AddComponent<Image>();
        background.color = PlayerStatusView.Ink;
        background.raycastTarget = false;
        var edge = panelObject.AddComponent<Outline>(); edge.effectColor = PlayerStatusView.Gold; edge.effectDistance = new Vector2(1,-1);

        VerticalLayoutGroup layout = panelObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(24, 24, 20, 20);
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;

        panelObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        CardInfoPanel panel = panelObject.AddComponent<CardInfoPanel>();
        panel.titleText = CreateText(panelObject.transform, 28f, FontStyles.Bold, PlayerStatusView.Cream);
        panel.costText = CreateText(panelObject.transform, 25f, FontStyles.Normal, PlayerStatusView.Gold);
        panel.effectText = CreateText(panelObject.transform, 28f, FontStyles.Normal, PlayerStatusView.Cream);
        panelObject.SetActive(false);
        return panel;
    }

    private static TextMeshProUGUI CreateText(Transform parent, float fontSize, FontStyles style, Color color)
    {
        var textObject = new GameObject("Text", typeof(RectTransform));
        textObject.transform.SetParent(parent, false);
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        GameFonts.Apply(text, style == FontStyles.Bold ? GameFontRole.Heading : GameFontRole.Body);
        text.color = color;
        text.raycastTarget = false;
        return text;
    }
}
