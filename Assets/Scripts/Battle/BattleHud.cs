using UnityEngine;
using UnityEngine.SceneManagement;

// Scene-local readiness and combat input. The persistent transition canvas is never a HUD.
public sealed class BattleHud : MonoBehaviour
{
    [SerializeField] private Canvas targetCanvas;
    [SerializeField] private DeckManager deck;
    [SerializeField] private HealthLabel[] healthLabels;
    [SerializeField] private BattleCountdown countdown;
    [SerializeField] private TimeTickSystem clock;
    public Canvas Canvas => targetCanvas;
    public DeckManager Deck => deck;
    public bool CountdownArmed { get; private set; }
    public PlayerStatusView Status { get; private set; }

    private void LateUpdate()
    {
        if (Status == null && deck != null && deck.Player != null) EnsureStatus(deck.Player);
    }
    private void EnsureStatus(BattleScript player)
    {
        if (Status != null || targetCanvas == null) return;
        var rect = PlayerStatusView.Rect("PlayerStatus", targetCanvas.transform);
        PlayerStatusView.Place(rect, new Vector2(0,1), new Vector2(32,-32), new Vector2(380,250), new Vector2(0,1));
        rect.SetAsFirstSibling();
        Status = PlayerStatusView.Create(rect, false);
        Status.Bind(player);
    }

    public bool TryPrepare(RunSession session, BattleScript player)
    {
        if (targetCanvas == null || !targetCanvas.isActiveAndEnabled || player == null ||
            player.gameObject.scene != gameObject.scene || !player.isActiveAndEnabled ||
            deck == null || clock == null || countdown == null) return false;
        if (!deck.TryInitialize(session, player)) return false;
        EnsureStatus(player);
        if (healthLabels == null || healthLabels.Length == 0) return false;
        foreach (var label in healthLabels)
            if (label == null || (label.isActiveAndEnabled && !label.IsReady)) return false;
        return true;
    }

    public void ArmCountdown() => CountdownArmed = true;
    public void BeginCombat()
    {
        if (!CountdownArmed || deck.Player == null || deck.Player.health <= 0) return;
        deck.SetCombatEnabled(true);
        clock.StartTimer();
    }
    public void StopCombat()
    {
        CountdownArmed = false;
        deck?.SetCombatEnabled(false);
        clock?.StopTimer();
        if (deck != null) foreach (var card in deck.Hand) card.ResetHover();
    }
    public static BattleHud Find(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded) return null;
        foreach (var root in scene.GetRootGameObjects())
        {
            var hud = root.GetComponentInChildren<BattleHud>();
            if (hud != null && hud.isActiveAndEnabled) return hud;
        }
        return null;
    }
    public static BattleScript FindPlayer(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded) return null;
        foreach (var root in scene.GetRootGameObjects())
            foreach (var actor in root.GetComponentsInChildren<BattleScript>())
                if (actor.isActiveAndEnabled && actor.CompareTag("Player")) return actor;
        return null;
    }
    public static Canvas ResolveCanvas(Component owner, Canvas assigned)
    {
        var scene = owner.gameObject.scene;
        if (assigned != null && assigned.gameObject.scene == scene) return assigned;
        var hud = Find(scene);
        if (hud != null) return hud.Canvas;
        foreach (var root in scene.GetRootGameObjects())
            foreach (var canvas in root.GetComponentsInChildren<Canvas>())
                if (canvas.isRootCanvas && canvas.isActiveAndEnabled) return canvas;
        return null;
    }
}
