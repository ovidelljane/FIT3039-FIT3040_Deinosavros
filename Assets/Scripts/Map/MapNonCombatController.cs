using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Scene-owned presentation. RunSession alone owns recovery and reward transactions.
public sealed class MapNonCombatController : MonoBehaviour
{
    public MapEncounterKind kind;
    public MapEncounterRouting routing;
    public TMP_FontAsset font;
    public Texture2D symbols;
    public IMapOpportunityContentProvider ContentProvider { get; set; } = new EmptyMapOpportunityContent();
    public Button ContinueButton { get; private set; }
    public RectTransform Panel { get; private set; }
    public bool IsReady { get; private set; }
    public bool IsActivated { get; private set; }
    public string EncounterId { get; private set; }
    public MapNonCombatReceipt Receipt => session?.GetNonCombatReceipt(EncounterId);
    private RunSession session;
    private TMP_Text heading, body, value, detail, capacity, continueText;
    private Image healthFill;
    private RectTransform choices;
    private static readonly Color Cream = new(1f, .9f, .72f), Gold = new(.82f, .63f, .34f),
        Ink = new(.1f, .073f, .053f), Green = new(.48f, .69f, .43f);

    private void Start()
    {
        BuildInterface(); IsReady = font != null && routing != null && symbols != null;
        session = RunSession.Instance;
        var ticket = session?.Progress?.CurrentEncounter;
        if (ticket != null && ticket.Kind == kind && session.Progress.Phase == MapProgressPhase.InEncounter)
        {
            if (TryPrepare(session, ticket)) ActivateEncounter();
        }
        else if (ticket == null && session?.LastNonCombatReceipt is MapNonCombatReceipt previous && previous.Kind == kind &&
            previous.Completed && previous.RunId == session.Progress.RunId && previous.NodeId == session.Progress.CurrentNodeId &&
            session.Progress.Phase == MapProgressPhase.OnMap)
        {
            EncounterId = previous.EncounterId; IsActivated = true; DisplayReceipt(previous, 1);
            ShowReturnError("Visit complete. Return to map.");
        }
        else if (ticket == null || ticket.Kind != kind)
        {
            value.text = "";
            body.text = "No active encounter.";
            detail.text = "";
            continueText.text = "Return to Map"; ContinueButton.interactable = true;
            ContinueButton.onClick.RemoveAllListeners();
            ContinueButton.onClick.AddListener(() =>
            {
                if (Application.CanStreamedLevelBeLoaded("Map")) SceneManager.LoadSceneAsync("Map");
                else ShowReturnError("The map scene is unavailable.");
            });
        }
    }
    public static MapNonCombatController Find(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded) return null;
        foreach (var root in scene.GetRootGameObjects())
        {
            var receiver = root.GetComponentInChildren<MapNonCombatController>();
            if (receiver != null && receiver.isActiveAndEnabled) return receiver;
        }
        return null;
    }
    public bool TryPrepare(RunSession owner, MapTravelTicket ticket)
    {
        if (!IsReady || !isActiveAndEnabled || owner == null || ticket == null || ticket.IsCombat || ticket.Kind != kind ||
            ticket.RunId != owner.Progress?.RunId || owner.Progress.CurrentEncounter != ticket || owner.PlayerHealth <= 0 || owner.PlayerMaxHealth <= 0) return false;
        session = owner; EncounterId = ticket.EncounterId;
        if (kind == MapEncounterKind.Opportunity && session.GetNonCombatReceipt(EncounterId) == null)
        {
            var offers = ContentProvider?.GetOffers(ticket, routing.cardPool);
            if (!session.TryPrepareOpportunity(EncounterId, routing.cardPool, offers)) return false;
        }
        return true;
    }
    public void ActivateEncounter()
    {
        if (IsActivated || session?.Progress?.Phase != MapProgressPhase.InEncounter ||
            session.Progress.CurrentEncounter?.EncounterId != EncounterId) return;
        if (kind == MapEncounterKind.Recovery && !session.TryResolveRecovery(EncounterId, out _)) return;
        IsActivated = true;
        if (kind == MapEncounterKind.Recovery) StartCoroutine(RevealRecovery());
        else { DisplayReceipt(Receipt, 1); ShowOffers(); ContinueButton.interactable = true; }
    }
    private IEnumerator RevealRecovery()
    {
        for (float t = 0; t < .6f; t += Time.unscaledDeltaTime)
        { DisplayReceipt(Receipt, Mathf.SmoothStep(0, 1, t / .6f)); yield return null; }
        DisplayReceipt(Receipt, 1); ContinueButton.interactable = true;
    }
    private void DisplayReceipt(MapNonCombatReceipt receipt, float progress)
    {
        if (receipt == null) return;
        capacity.text = $"DECK CAPACITY  {session.DeckCapacity} / {RunSession.CapacityLimit}";
        if (kind == MapEncounterKind.Recovery)
        {
            int shown = Mathf.RoundToInt(Mathf.Lerp(receipt.HealthBefore, receipt.HealthAfter, progress));
            value.text = $"{shown} <size=30>/ {receipt.MaxHealth} HP</size>";
            healthFill.rectTransform.anchorMax = new Vector2((float)shown / Mathf.Max(1, receipt.MaxHealth), 1);
            body.text = receipt.Recovered == 0 ? "Full health" : $"+{receipt.Recovered} HP";
            detail.text = "";
        }
        else
        {
            value.text = receipt.GrantedCard != null ? "CARD ACQUIRED" : "";
            body.text = receipt.GrantedCard != null ? receipt.GrantedCard.definition.displayName :
                receipt.Offers.Count == 0 ? "No cards available yet." : "Choose 1 card.";
            detail.text = "";
        }
    }
    private void ShowOffers()
    {
        var receipt = Receipt;
        if (receipt == null || receipt.Offers.Count == 0 || receipt.GrantedCard != null) return;
        choices.gameObject.SetActive(true); continueText.text = "Skip";
        detail.rectTransform.anchoredPosition = new Vector2(0, -215);
        detail.rectTransform.sizeDelta = new Vector2(830, 55);
        capacity.rectTransform.anchoredPosition = new Vector2(0, -258);
        int count = receipt.Offers.Count;
        for (int i = 0; i < count; i++)
        {
            var definition = receipt.Offers[i];
            var button = ButtonAt("Choice", choices, new Vector2((i - (count - 1) * .5f) * 245, 0), new Vector2(230, 76),
                definition.displayName + "\nCapacity " + definition.capacityCost, out _);
            button.onClick.AddListener(() =>
            {
                if (!session.TryClaimOpportunityCard(EncounterId, definition, out string reason)) { detail.text = reason; return; }
                choices.gameObject.SetActive(false); continueText.text = "Continue"; DisplayReceipt(Receipt, 1);
            });
        }
    }
    private void Continue()
    {
        if (!IsActivated || session == null) return;
        if (MapTravelCoordinator.Ensure(session).ContinueNonCombat(this, EncounterId)) ContinueButton.interactable = false;
    }
    public void ShowReturnError(string message)
    { detail.text = message; continueText.text = "Retry Return"; ContinueButton.interactable = true; }

    private void BuildInterface()
    {
        var ui = new GameObject("UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        ui.transform.SetParent(transform, false);
        ui.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = ui.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
        if (EventSystem.current == null)
        {
            var input = new GameObject("Input", typeof(EventSystem), typeof(InputSystemUIInputModule));
            input.transform.SetParent(transform, false); input.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }
        var background = Box("Background", ui.transform, Vector2.zero, Vector2.zero, new Color(.075f, .085f, .073f));
        Stretch(background.rectTransform);
        Box("Band", ui.transform, new Vector2(0, 0), new Vector2(2400, 480), new Color(.095f, .108f, .09f));
        var panelImage = Box("Panel", ui.transform, Vector2.zero, new Vector2(1000, 760), Ink);
        Panel = panelImage.rectTransform;
        var edge = panelImage.gameObject.AddComponent<Outline>(); edge.effectColor = Gold; edge.effectDistance = new Vector2(2, -2);
        Box("Rule", Panel, new Vector2(0, 170), new Vector2(740, 2), Gold);
        var iconObject = new GameObject("Icon", typeof(RectTransform), typeof(RawImage));
        var icon = iconObject.GetComponent<RawImage>(); icon.transform.SetParent(Panel, false); icon.texture = symbols; icon.raycastTarget = false;
        var tile = MapTravelView.AtlasRect(kind == MapEncounterKind.Recovery ? 11 : 10); icon.uvRect = new Rect(tile.x, tile.y, tile.z, tile.w);
        icon.rectTransform.anchoredPosition = new Vector2(0, 280); icon.rectTransform.sizeDelta = new Vector2(96, 96);
        heading = Text("Title", Panel, kind == MapEncounterKind.Recovery ? "RECOVERY" : "OPPORTUNITY", new Vector2(0, 204), new Vector2(850, 58), 36, Cream);
        value = Text("Value", Panel, "Loading...", new Vector2(0, 100), new Vector2(850, 76), kind == MapEncounterKind.Recovery ? 54 : 27, kind == MapEncounterKind.Recovery ? Green : Gold);
        var bar = Box("Health", Panel, new Vector2(0, 28), new Vector2(620, 14), new Color(.2f, .22f, .17f));
        healthFill = Box("Fill", bar.transform, Vector2.zero, new Vector2(620, 14), Green);
        // A plain rectangular image uses anchors for fill; no editor-only UI resources are required.
        Stretch(healthFill.rectTransform);
        bar.gameObject.SetActive(kind == MapEncounterKind.Recovery);
        body = Text("Body", Panel, "", new Vector2(0, -45), new Vector2(840, 92), 27, Cream);
        detail = Text("Detail", Panel, "", new Vector2(0, -145), new Vector2(830, 90), 19, new Color(.77f, .73f, .64f));
        capacity = Text("Capacity", Panel, "", new Vector2(0, -230), new Vector2(800, 35), 18, Gold);
        capacity.gameObject.SetActive(kind == MapEncounterKind.Opportunity);
        var choicesObject = new GameObject("Choices", typeof(RectTransform)); choices = (RectTransform)choicesObject.transform;
        choices.SetParent(Panel, false); choices.anchoredPosition = new Vector2(0, -140); choices.sizeDelta = new Vector2(820, 85); choices.gameObject.SetActive(false);
        ContinueButton = ButtonAt("Continue", Panel, new Vector2(0, -305), new Vector2(340, 64), "Continue", out continueText);
        ContinueButton.onClick.AddListener(Continue); ContinueButton.interactable = false;
    }
    private static void Stretch(RectTransform rect)
    { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
    private static Image Box(string name, Transform parent, Vector2 position, Vector2 size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>(); image.color = color; image.raycastTarget = false;
        image.rectTransform.sizeDelta = size; image.rectTransform.anchoredPosition = position; return image;
    }
    private TMP_Text Text(string name, Transform parent, string content, Vector2 position, Vector2 size, float fontSize, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)); go.transform.SetParent(parent, false);
        var text = go.GetComponent<TextMeshProUGUI>(); text.font = font; text.text = content; text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.Center; text.color = color; text.raycastTarget = false;
        text.rectTransform.sizeDelta = size; text.rectTransform.anchoredPosition = position; return text;
    }
    private Button ButtonAt(string name, Transform parent, Vector2 position, Vector2 size, string content, out TMP_Text label)
    {
        var image = Box(name, parent, position, size, new Color(.29f, .21f, .12f)); image.raycastTarget = true;
        var outline = image.gameObject.AddComponent<Outline>(); outline.effectColor = Gold; outline.effectDistance = new Vector2(1, -1);
        var button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image;
        label = Text("Label", image.transform, content, Vector2.zero, size - new Vector2(16, 8), 23, Cream); return button;
    }
}
