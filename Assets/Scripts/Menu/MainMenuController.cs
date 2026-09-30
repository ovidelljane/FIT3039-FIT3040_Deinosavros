using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Deinosavros.MapReview;

public sealed class MainMenuController : MonoBehaviour
{
    [SerializeField] private string firstBattleSceneName = "Deinosavros";
    [SerializeField] private CardDefinition[] startingDeck;
    [SerializeField] private Button startButton;
    [SerializeField] private MapGraphDefinition routeGraph;
    [SerializeField] private MapNodeCatalog nodeCatalog;
    private bool loading;

    private void Start()
    {
        Time.timeScale = 1f;
        startButton.onClick.AddListener(StartRun);
    }

    private void StartRun()
    {
        if (loading) return;
        var rules = RunSettings.ForScene(gameObject.scene);
        int capacity = 0;
        foreach (var card in startingDeck ?? System.Array.Empty<CardDefinition>())
        {
            if (card == null || card.capacityCost < 0) { Debug.LogWarning("The starting deck contains an invalid card.", this); return; }
            capacity += card.capacityCost;
        }
        if (capacity > Mathf.Max(1, rules.deckCapacity))
        { Debug.LogWarning("The starting deck exceeds the configured capacity. Adjust Run Settings or the starting deck.", this); return; }
        loading = true;
        startButton.interactable = false;
        if (routeGraph == null || nodeCatalog == null || !Application.CanStreamedLevelBeLoaded(firstBattleSceneName))
        {
            loading = false; startButton.interactable = true;
            Debug.LogError("The starting route or battle scene is unavailable.", this);
            return;
        }
        var session = RunSession.StartNewRun(startingDeck, routeGraph, nodeCatalog, rules);
        if (!MapTravelCoordinator.Ensure(session).BeginInitialEncounter(firstBattleSceneName))
        {
            Destroy(session.gameObject);
            loading = false; startButton.interactable = true;
        }
    }
}
