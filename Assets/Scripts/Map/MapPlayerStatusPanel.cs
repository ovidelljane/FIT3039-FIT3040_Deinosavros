using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class MapPlayerStatusPanel : MonoBehaviour
{
    private static readonly Color PanelColor = new(0.055f, 0.045f, 0.04f, 0.94f);
    private static readonly Color BorderColor = new(0.52f, 0.34f, 0.18f, 0.9f);
    private static readonly Color LabelColor = new(0.78f, 0.72f, 0.64f, 1f);
    private static readonly Color ValueColor = new(1f, 0.91f, 0.76f, 1f);

    private RunSession runSession;
    private TMP_Text healthValue;
    private TMP_Text damageValue;
    private TMP_Text attackSpeedValue;
    private TMP_Text shieldValue;
    private TMP_Text elixirValue;

    private int displayedHealth = int.MinValue;
    private int displayedMaxHealth = int.MinValue;
    private int displayedDamage = int.MinValue;
    private int displayedAttackSpeed = int.MinValue;
    private int displayedShield = int.MinValue;
    private float displayedElixir = float.NaN;
    private float displayedMaxElixir = float.NaN;

    public static MapPlayerStatusPanel Create(Transform parent)
    {
        GameObject panelObject = new("PlayerStatusPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline));
        RectTransform panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.SetParent(parent, false);
        panelRect.anchorMin = new Vector2(0f, 0f);
        panelRect.anchorMax = new Vector2(0f, 1f);
        panelRect.pivot = new Vector2(0f, 0.5f);
        panelRect.offsetMin = new Vector2(18f, 18f);
        panelRect.offsetMax = new Vector2(225f, -18f);

        Image panelImage = panelObject.GetComponent<Image>();
        panelImage.color = PanelColor;
        panelImage.raycastTarget = false;

        Outline outline = panelObject.GetComponent<Outline>();
        outline.effectColor = BorderColor;
        outline.effectDistance = new Vector2(1.5f, -1.5f);
        outline.useGraphicAlpha = true;

        MapPlayerStatusPanel panel = panelObject.AddComponent<MapPlayerStatusPanel>();
        panel.BuildContents();
        return panel;
    }

    public void Initialize(RunSession session)
    {
        runSession = session;
        RefreshValues(true);
    }

    private void Update()
    {
        RefreshValues(false);
    }

    private void BuildContents()
    {
        RectTransform contentRect = CreateRect("Content", transform);
        contentRect.anchorMin = Vector2.zero;
        contentRect.anchorMax = Vector2.one;
        contentRect.offsetMin = new Vector2(12f, 10f);
        contentRect.offsetMax = new Vector2(-12f, -10f);

        VerticalLayoutGroup layout = contentRect.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(0, 0, 0, 0);
        layout.spacing = 3f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        TMP_Text title = CreateText("Title", contentRect, "CURRENT STATUS", 16f, TextAlignmentOptions.Center, ValueColor);
        SetPreferredHeight(title.gameObject, 25f);

        Image divider = CreateRect("Divider", contentRect).gameObject.AddComponent<Image>();
        divider.color = BorderColor;
        divider.raycastTarget = false;
        SetPreferredHeight(divider.gameObject, 1f);

        healthValue = CreateStatRow(contentRect, "HP", new Color(0.86f, 0.25f, 0.20f, 1f));
        damageValue = CreateStatRow(contentRect, "DAMAGE", new Color(0.92f, 0.49f, 0.20f, 1f));
        attackSpeedValue = CreateStatRow(contentRect, "ATTACK SPD", new Color(0.92f, 0.76f, 0.25f, 1f));
        shieldValue = CreateStatRow(contentRect, "SHIELD", new Color(0.28f, 0.62f, 0.92f, 1f));
        elixirValue = CreateStatRow(contentRect, "ELIXIR", new Color(0.67f, 0.38f, 0.90f, 1f));
    }

    private TMP_Text CreateStatRow(RectTransform parent, string label, Color accentColor)
    {
        RectTransform rowRect = CreateRect(label + "Row", parent);
        SetPreferredHeight(rowRect.gameObject, 25f);

        HorizontalLayoutGroup rowLayout = rowRect.gameObject.AddComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 7f;
        rowLayout.childAlignment = TextAnchor.MiddleLeft;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = true;

        Image accent = CreateRect("Accent", rowRect).gameObject.AddComponent<Image>();
        accent.color = accentColor;
        accent.raycastTarget = false;
        SetLayoutWidth(accent.gameObject, 4f);

        TMP_Text labelText = CreateText("Label", rowRect, label, 12f, TextAlignmentOptions.MidlineLeft, LabelColor);
        SetLayoutWidth(labelText.gameObject, 78f);

        TMP_Text valueText = CreateText("Value", rowRect, "--", 13f, TextAlignmentOptions.MidlineRight, ValueColor);
        LayoutElement valueLayout = valueText.gameObject.AddComponent<LayoutElement>();
        valueLayout.flexibleWidth = 1f;
        return valueText;
    }

    private void RefreshValues(bool force)
    {
        if (runSession == null || healthValue == null)
        {
            return;
        }

        if (force || displayedHealth != runSession.PlayerHealth || displayedMaxHealth != runSession.PlayerMaxHealth)
        {
            displayedHealth = runSession.PlayerHealth;
            displayedMaxHealth = runSession.PlayerMaxHealth;
            healthValue.text = $"{displayedHealth} / {displayedMaxHealth}";
        }
        if (force || displayedDamage != runSession.PlayerDamage)
        {
            displayedDamage = runSession.PlayerDamage;
            damageValue.text = displayedDamage.ToString();
        }
        if (force || displayedAttackSpeed != runSession.PlayerAttackSpeed)
        {
            displayedAttackSpeed = runSession.PlayerAttackSpeed;
            attackSpeedValue.text = $"{displayedAttackSpeed}s";
        }
        if (force || displayedShield != runSession.PlayerShield)
        {
            displayedShield = runSession.PlayerShield;
            shieldValue.text = displayedShield.ToString();
        }
        if (force || !Mathf.Approximately(displayedElixir, runSession.PlayerElixir) || !Mathf.Approximately(displayedMaxElixir, runSession.PlayerMaxElixir))
        {
            displayedElixir = runSession.PlayerElixir;
            displayedMaxElixir = runSession.PlayerMaxElixir;
            elixirValue.text = $"{displayedElixir:0.0} / {displayedMaxElixir:0.0}";
        }
    }

    private static RectTransform CreateRect(string objectName, Transform parent)
    {
        GameObject child = new(objectName, typeof(RectTransform));
        RectTransform rect = child.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.localScale = Vector3.one;
        return rect;
    }

    private static TMP_Text CreateText(string objectName, Transform parent, string content, float fontSize, TextAlignmentOptions alignment, Color color)
    {
        RectTransform rect = CreateRect(objectName, parent);
        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.text = content;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        return text;
    }

    private static void SetPreferredHeight(GameObject target, float height)
    {
        LayoutElement layout = target.AddComponent<LayoutElement>();
        layout.preferredHeight = height;
        layout.minHeight = height;
    }

    private static void SetLayoutWidth(GameObject target, float width)
    {
        LayoutElement layout = target.AddComponent<LayoutElement>();
        layout.preferredWidth = width;
        layout.minWidth = width;
    }
}
