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
    [Header("Scene Status")]
    [SerializeField] private RectTransform statusRoot;
    [SerializeField] private PlayerStatusView status;
    public Canvas Canvas => targetCanvas;
    public DeckManager Deck => deck;
    public bool CountdownArmed { get; private set; }
    public PlayerStatusView Status => status;
    public RectTransform StatusRoot => statusRoot;
    private BattleScript boundPlayer;

    private void Start()
    {
        if (Status == null || !Status.IsReady)
            Debug.LogError("Assign the scene-owned player status references on BattleHud.", this);
    }

    private void LateUpdate()
    {
        if (deck != null && deck.Player != null && boundPlayer != deck.Player) EnsureStatus(deck.Player);
    }
    private void EnsureStatus(BattleScript player)
    {
        if (Status == null || !Status.IsReady || boundPlayer == player) return;
        Status.Bind(player);
        boundPlayer = player;
    }

    public bool TryPrepare(RunSession session, BattleScript player)
    {
        if (targetCanvas == null || !targetCanvas.isActiveAndEnabled || player == null ||
            player.gameObject.scene != gameObject.scene || !player.isActiveAndEnabled ||
            deck == null || clock == null || countdown == null || Status == null || !Status.IsReady ||
            !Status.isActiveAndEnabled || Status.gameObject.scene != gameObject.scene) return false;
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
