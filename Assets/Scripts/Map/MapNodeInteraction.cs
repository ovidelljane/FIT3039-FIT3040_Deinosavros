using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// One stable picker owns both the platform and its icon. Animated visuals are never hit targets.
public sealed class MapNodeInteraction : MonoBehaviour
{
    private MapController owner;
    private MapTravelView view;
    private Camera sceneCamera;
    private Canvas canvas;
    private RectTransform panel;
    private TMP_Text heading;
    private MapEncounterNode hovered, pressed;
    private Vector2 pressPoint;
    private float hoverStarted, lastOnTarget;
    public string HoveredNodeId => hovered != null ? hovered.NodeId : null;
    public bool DetailVisible => panel != null && panel.gameObject.activeSelf;
    public RectTransform DetailPanel => panel;

    public void Initialize(MapController controller, MapTravelView travelView)
    {
        owner = controller; view = travelView; sceneCamera = Camera.main;
        canvas = owner.EnterButton.GetComponentInParent<Canvas>(true).rootCanvas;
        CreatePanel();
    }
    private void Update()
    {
        if (owner == null || view == null || sceneCamera == null || Mouse.current == null) return;
        if (!owner.CanInteract) { Close(); return; }
        Vector2 point = Mouse.current.position.ReadValue();
        bool inDetail = DetailVisible && RectTransformUtility.RectangleContainsScreenPoint(panel, point, UICamera);
        bool blocked = owner.BlocksMapPointer(point);
        MapEncounterNode hit = blocked ? null : Pick(point);
        if (hit != null)
        {
            lastOnTarget = Time.unscaledTime;
            if (hovered != hit) SetHover(hit);
        }
        else if (inDetail && hovered != null) lastOnTarget = Time.unscaledTime;
        else if ((blocked || Time.unscaledTime - lastOnTarget > view.profile.tooltipGrace) && hovered != null) SetHover(null);

        if (hovered != null && Time.unscaledTime - hoverStarted >= view.profile.tooltipDelay)
        {
            if (!DetailVisible) ShowDetail();
            PositionPanel();
        }
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            pressed = hit; pressPoint = point;
        }
        if (pressed != null && (hit != pressed || Vector2.Distance(point, pressPoint) > 10 * ReferenceScale)) pressed = null;
        if (Mouse.current.leftButton.wasReleasedThisFrame)
        {
            var target = pressed; pressed = null;
            if (target != null && target == hit && owner.CanInteract) owner.SelectNode(target);
        }
    }
    private Camera UICamera => canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
    private float ReferenceScale => Mathf.Max(.1f, Screen.height / 1080f);
    private MapEncounterNode Pick(Vector2 point)
    {
        MapEncounterNode best = null;
        float distance = float.MaxValue;
        // Fixed, unscaled icon rectangles avoid hover oscillation at the animated outline.
        foreach (var node in owner.Nodes)
        {
            if (node == null || !node.IsAvailable) continue;
            Rect rect = view.IconScreenRect(node.NodeId);
            if (!rect.Contains(point)) continue;
            float d = (rect.center - point).sqrMagnitude;
            if (d < distance) { best = node; distance = d; }
        }
        if (best != null) return best;
        Ray ray = sceneCamera.ScreenPointToRay(point); distance = float.MaxValue;
        foreach (var node in owner.Nodes)
        {
            if (node == null || !node.IsAvailable || node.InteractionCollider == null) continue;
            if (node.InteractionCollider.Raycast(ray, out var hit, sceneCamera.farClipPlane) && hit.distance < distance)
            { best = node; distance = hit.distance; }
        }
        return best;
    }
    private void SetHover(MapEncounterNode next)
    {
        if (hovered == next) return;
        hovered?.SetHovered(false); hovered = next; hovered?.SetHovered(true);
        hoverStarted = Time.unscaledTime;
        if (panel != null) panel.gameObject.SetActive(false);
        view.Preview(hovered != null ? hovered.NodeId : null);
    }
    public void Close()
    {
        pressed = null;
        if (hovered != null) SetHover(null);
        if (panel != null) panel.gameObject.SetActive(false);
    }
    private void ShowDetail()
    {
        var info = view.profile.nodeInformation != null ? view.profile.nodeInformation.Find(hovered.NodeId) : null;
        heading.text = (info != null ? info.kind : MapEncounterKind.Battle).ToString();
        float width = Mathf.Max(132, heading.GetPreferredValues(heading.text).x + 36);
        panel.sizeDelta = new Vector2(width, 56);
        heading.rectTransform.sizeDelta = new Vector2(width - 36, 32);
        panel.gameObject.SetActive(true); PositionPanel();
    }
    private void PositionPanel()
    {
        if (hovered == null) return;
        Rect icon = view.IconScreenRect(hovered.NodeId);
        float scale = canvas.scaleFactor, width = panel.rect.width * scale, height = panel.rect.height * scale;
        Rect safe = new(12 * scale, .26f * Screen.height + 12 * scale, Screen.width - 24 * scale, .69f * Screen.height - 24 * scale);
        var framing = sceneCamera.GetComponent<MapCameraFraming>();
        if (framing != null)
            safe = new Rect(12 * scale, framing.SafeArea.yMin * Screen.height, Screen.width - 24 * scale,
                (framing.SafeArea.yMax - framing.SafeArea.yMin) * Screen.height);
        float x = icon.xMax + 10 * scale;
        if (x + width > safe.xMax) x = icon.xMin - width - 10 * scale;
        Vector2 screenPoint = new(Mathf.Clamp(x, safe.xMin, Mathf.Max(safe.xMin, safe.xMax - width)),
            Mathf.Clamp(icon.yMax + 12 * scale, safe.yMin + height, Mathf.Max(safe.yMin + height, safe.yMax)));
        RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)canvas.transform, screenPoint, UICamera, out var local);
        panel.localPosition = local;
    }
    private void CreatePanel()
    {
        var root = new GameObject("NodeInfo", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Outline));
        panel = (RectTransform)root.transform; panel.SetParent(canvas.transform, false);
        panel.anchorMin = panel.anchorMax = new Vector2(.5f, .5f); panel.pivot = new Vector2(0, 1);
        root.GetComponent<UnityEngine.UI.Image>().color = new Color(.13f, .085f, .052f, .97f);
        var border = root.GetComponent<UnityEngine.UI.Outline>(); border.effectColor = new Color(.77f, .56f, .29f, 1); border.effectDistance = new Vector2(1, -1);
        heading = Text("Title", 24, new Vector2(18, -12), 264, 32, new Color(1, .91f, .72f));
        heading.alignment = TextAlignmentOptions.Center;
        GameFonts.Apply(heading, GameFontRole.Heading);
        panel.gameObject.SetActive(false);
    }
    private TMP_Text Text(string name, float size, Vector2 position, float width, float height, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        var text = go.GetComponent<TextMeshProUGUI>(); text.transform.SetParent(panel, false);
        var rect = text.rectTransform; rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = position; rect.sizeDelta = new Vector2(width, height);
        GameFonts.Apply(text, GameFontRole.Body);
        text.fontSize = size; text.color = color; text.raycastTarget = false; text.richText = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        return text;
    }
    private void OnDestroy() { if (panel != null) Destroy(panel.gameObject); }
}
