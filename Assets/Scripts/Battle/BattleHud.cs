using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

// Explicit scene-owned HUD. Persistent transition canvases can never own combat UI.
public sealed class BattleHud : MonoBehaviour
{
    [SerializeField] private Canvas targetCanvas;
    [SerializeField] private DeckManager deck;
    [SerializeField] private HealthLabel[] healthLabels;
    [SerializeField] private TempStatDisplay playerStats;
    public Canvas Canvas => targetCanvas;
    public DeckManager Deck => deck;

    public bool TryPrepare(RunSession session, BattleScript player)
    {
        if (targetCanvas == null || !targetCanvas.isActiveAndEnabled || player == null ||
            player.gameObject.scene != gameObject.scene || !player.isActiveAndEnabled || deck == null) return false;
        if (!deck.TryInitialize(session, player) || !deck.IsReady) return false;
        if (playerStats == null || !playerStats.IsReady || playerStats.Target != player) return false;
        if (healthLabels == null || healthLabels.Length == 0) return false;
        foreach (var label in healthLabels)
            if (label == null || (label.isActiveAndEnabled && !label.IsReady)) return false;
        return true;
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
        // Compatibility for older standalone scenes, strictly inside the actor's scene.
        foreach (var root in scene.GetRootGameObjects())
            foreach (var canvas in root.GetComponentsInChildren<Canvas>())
                if (canvas.isRootCanvas && canvas.isActiveAndEnabled) return canvas;
        return null;
    }
    public static void PositionLabel(TMP_Text label, Canvas canvas, Camera camera, Vector3 worldAnchor)
    {
        if (label == null || canvas == null || camera == null) return;
        Vector3 viewport = camera.WorldToViewportPoint(worldAnchor);
        var rect = label.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(viewport.x, viewport.y);
        rect.anchoredPosition = Vector2.zero;
        label.ForceMeshUpdate();
        var bounds = label.textBounds;
        var uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        Vector2 min = RectTransformUtility.WorldToScreenPoint(uiCamera, rect.TransformPoint(bounds.min));
        Vector2 max = RectTransformUtility.WorldToScreenPoint(uiCamera, rect.TransformPoint(bounds.max));
        float margin = 12 * canvas.scaleFactor;
        Vector2 shift = new(min.x < margin ? margin - min.x : max.x > Screen.width - margin ? Screen.width - margin - max.x : 0,
            min.y < margin ? margin - min.y : max.y > Screen.height - margin ? Screen.height - margin - max.y : 0);
        rect.anchoredPosition += shift / Mathf.Max(.01f, canvas.scaleFactor);
    }
}
