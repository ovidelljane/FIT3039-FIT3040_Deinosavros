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
    private TMP_Text heading, category, body, instruction;
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
        heading.text = info != null && !string.IsNullOrWhiteSpace(info.title) ? info.title : "Level " + hovered.Level;
        category.text = (info != null ? info.KindLabel : "ENCOUNTER") + "  /  AVAILABLE";
        body.text = info != null ? info.summary : "Select this destination to continue your adventure.";
        if (info != null && !string.IsNullOrWhiteSpace(info.enemies)) body.text += "\n\nEnemies: " + info.enemies;
        if (info != null && !string.IsNullOrWhiteSpace(info.rewards)) body.text += "\n\nRewards: " + info.rewards;
        instruction.text = "Click the level to travel";
        float textHeight = Mathf.Clamp(body.GetPreferredValues(body.text, 264, 0).y, 46, 260);
        body.rectTransform.sizeDelta = new Vector2(264, textHeight);
        panel.sizeDelta = new Vector2(300, 124 + textHeight);
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
        var root = new GameObject("NodeInfo", typeof(RectTransform), typeof(Image), typeof(Outline));
        panel = (RectTransform)root.transform; panel.SetParent(canvas.transform, false);
        panel.anchorMin = panel.anchorMax = new Vector2(.5f, .5f); panel.pivot = new Vector2(0, 1);
        root.GetComponent<Image>().color = new Color(.13f, .085f, .052f, .97f);
        var border = root.GetComponent<Outline>(); border.effectColor = new Color(.77f, .56f, .29f, 1); border.effectDistance = new Vector2(1, -1);
        category = Text("Type", 14, new Vector2(18, -14), 264, 20, new Color(.88f, .69f, .41f));
        heading = Text("Title", 24, new Vector2(18, -37), 264, 32, new Color(1, .91f, .72f));
        heading.fontStyle = FontStyles.Bold;
        body = Text("Content", 18, new Vector2(18, -77), 264, 100, new Color(.9f, .85f, .74f));
        instruction = Text("Hint", 14, Vector2.zero, 264, 22, new Color(.91f, .71f, .39f));
        instruction.rectTransform.anchorMin = instruction.rectTransform.anchorMax = new Vector2(0, 0);
        instruction.rectTransform.pivot = Vector2.zero; instruction.rectTransform.anchoredPosition = new Vector2(18, 12);
        panel.gameObject.SetActive(false);
    }
    private TMP_Text Text(string name, float size, Vector2 position, float width, float height, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        var text = go.GetComponent<TextMeshProUGUI>(); text.transform.SetParent(panel, false);
        var rect = text.rectTransform; rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = position; rect.sizeDelta = new Vector2(width, height);
        text.font = owner.EnterButton.GetComponentInChildren<TMP_Text>(true).font;
        text.fontSize = size; text.color = color; text.raycastTarget = false; text.richText = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        return text;
    }
    private void OnDestroy() { if (panel != null) Destroy(panel.gameObject); }
}
