using UnityEngine;

public sealed class MapPlayerStatusPanel : MonoBehaviour
{
    public PlayerStatusView View { get; private set; }
    public static MapPlayerStatusPanel Create(Transform parent)
    {
        var rect = PlayerStatusView.Rect("PlayerStatusPanel", parent);
        rect.anchorMin = Vector2.zero; rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, .5f);
        rect.offsetMin = new Vector2(18, 18); rect.offsetMax = new Vector2(225, -18);
        var panel = rect.gameObject.AddComponent<MapPlayerStatusPanel>();
        panel.View = PlayerStatusView.Create(rect, true);
        return panel;
    }
    public void Initialize(RunSession session)
    {
        if (View == null) View = PlayerStatusView.Create(transform, true);
        View.Bind(session);
    }
}
