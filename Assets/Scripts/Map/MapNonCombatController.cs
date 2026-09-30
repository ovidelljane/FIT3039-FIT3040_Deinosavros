using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Scene-owned presentation. RunSession alone owns recovery and reward transactions.
public class MapNonCombatController : MonoBehaviour
{
    public MapEncounterKind kind;
    public MapEncounterRouting routing;
    public TMP_FontAsset font;
    public Texture2D symbols;
    [Header("Scene Interface")]
    [SerializeField] private Button continueButton;
    [SerializeField] private RectTransform panel;
    [SerializeField] protected TMP_Text heading, body, value, detail, capacity, continueText;
    [SerializeField] protected Image healthFill;
    public Button ContinueButton => continueButton;
    public RectTransform Panel => panel;
    public bool IsReady { get; private set; }
    public bool IsActivated { get; protected set; }
    public string EncounterId { get; private set; }
    public MapNonCombatReceipt Receipt => session?.GetNonCombatReceipt(EncounterId);
    protected RunSession session;

    protected virtual void Start()
    {
        IsReady = BindInterface() && routing != null && symbols != null;
        if (!IsReady)
        {
            Debug.LogError("The encounter scene has missing interface bindings. Repair its Scene Interface references.", this);
            return;
        }
        ContinueButton.onClick.AddListener(Continue); ContinueButton.interactable = false;
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
    public virtual bool TryPrepare(RunSession owner, MapTravelTicket ticket)
    {
        if (!IsReady || !isActiveAndEnabled || owner == null || ticket == null || ticket.IsCombat || ticket.Kind != kind ||
            ticket.RunId != owner.Progress?.RunId || owner.Progress.CurrentEncounter != ticket || owner.PlayerHealth <= 0 || owner.PlayerMaxHealth <= 0) return false;
        session = owner; EncounterId = ticket.EncounterId;
        return true;
    }
    public virtual void ActivateEncounter()
    {
        if (IsActivated || session?.Progress?.Phase != MapProgressPhase.InEncounter ||
            session.Progress.CurrentEncounter?.EncounterId != EncounterId) return;
        if (kind == MapEncounterKind.Recovery && !session.TryResolveRecovery(EncounterId, out _)) return;
        IsActivated = true;
        if (kind == MapEncounterKind.Recovery) StartCoroutine(RevealRecovery());
        else { DisplayReceipt(Receipt, 1); ContinueButton.interactable = true; }
    }
    private IEnumerator RevealRecovery()
    {
        for (float t = 0; t < .6f; t += Time.unscaledDeltaTime)
        { DisplayReceipt(Receipt, Mathf.SmoothStep(0, 1, t / .6f)); yield return null; }
        DisplayReceipt(Receipt, 1); ContinueButton.interactable = true;
    }
    protected virtual void DisplayReceipt(MapNonCombatReceipt receipt, float progress)
    {
        if (receipt == null) return;
        capacity.text = $"DECK CAPACITY  {session.DeckCapacity} / {RunSession.CapacityLimit}";
        if (kind == MapEncounterKind.Recovery)
        {
            int shown = Mathf.RoundToInt(Mathf.Lerp(receipt.HealthBefore, receipt.HealthAfter, progress));
            value.text = $"{shown} <size=55%>/ {receipt.MaxHealth} HP</size>";
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
    protected virtual void Continue()
    {
        if (!IsActivated || session == null) return;
        if (MapTravelCoordinator.Ensure(session).ContinueNonCombat(this, EncounterId)) ContinueButton.interactable = false;
    }
    public virtual void ShowReturnError(string message)
    { detail.text = message; continueText.text = "Retry Return"; ContinueButton.interactable = true; }

    protected virtual bool BindInterface() => Panel != null && ContinueButton != null && heading != null &&
        body != null && value != null && detail != null && capacity != null && continueText != null && healthFill != null;
}
