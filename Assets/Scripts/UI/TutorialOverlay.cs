using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public readonly struct TutorialStep
{
    public readonly string Title, Body;
    // Fills the list with the screen-pixel rects to spotlight. Called every frame, so targets may move.
    public readonly Action<List<Rect>> Targets;

    public TutorialStep(string title, string body, Action<List<Rect>> targets)
    {
        Title = title; Body = body; Targets = targets;
    }
}

// Pauses the game and walks through steps, dimming everything except each step's targets.
// Built at runtime and owned by the scene it is shown in.
public sealed class TutorialOverlay : MonoBehaviour
{
    private const int SortingOrder = 31000; // Above every scene canvas, below the transition curtain.
    private const float BoxWidth = 540, ScreenMargin = 24, BoxGap = 20, HolePadding = 10, FadeSeconds = .2f;

    private static TutorialOverlay active;
    public static bool IsOpen => active != null;

    private IReadOnlyList<TutorialStep> steps;
    private string finishLabel;
    private Action onSkipped;
    private int stepIndex;
    private float previousTimeScale;
    private bool finished, layoutDirty;
    private RectTransform canvasRect, box;
    private CanvasGroup group;
    private TutorialDim dim;
    private TMP_Text title, body, counter, nextLabel;
    private readonly List<Rect> screenRects = new(), localRects = new();

    // Runs until the player finishes or skips. Reports through onSkipped so a skip can end later sections too.
    public static IEnumerator Play(Scene scene, IReadOnlyList<TutorialStep> steps, string finishLabel, Action onSkipped)
    {
        if (steps == null || steps.Count == 0 || IsOpen) yield break;
        TutorialOverlay overlay = Create(scene, steps, finishLabel, onSkipped);
        while (overlay != null && !overlay.finished) yield return null;
    }

    private static TutorialOverlay Create(Scene scene, IReadOnlyList<TutorialStep> steps, string finishLabel, Action onSkipped)
    {
        var root = new GameObject("Tutorial", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
            typeof(GraphicRaycaster), typeof(CanvasGroup));
        SceneManager.MoveGameObjectToScene(root, scene);
        root.layer = LayerMask.NameToLayer("UI");

        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = .5f;

        var overlay = root.AddComponent<TutorialOverlay>();
        overlay.steps = steps;
        overlay.finishLabel = finishLabel;
        overlay.onSkipped = onSkipped;
        overlay.Build();
        overlay.ShowStep(0);
        overlay.Pause();
        return overlay;
    }

    private void Build()
    {
        canvasRect = (RectTransform)transform;
        group = GetComponent<CanvasGroup>();
        group.alpha = 0;

        var dimRect = PlayerStatusView.Rect("Dim", transform);
        dimRect.anchorMin = Vector2.zero; dimRect.anchorMax = Vector2.one;
        dimRect.offsetMin = dimRect.offsetMax = Vector2.zero;
        dim = dimRect.gameObject.AddComponent<TutorialDim>();
        dim.color = new Color(0, 0, 0, .74f);

        Image panel = PlayerStatusView.Box("Textbox", transform, PlayerStatusView.Ink);
        box = panel.rectTransform;
        box.anchorMin = box.anchorMax = new Vector2(.5f, .5f);
        box.sizeDelta = new Vector2(BoxWidth, 0);
        var edge = panel.gameObject.AddComponent<Outline>();
        edge.effectColor = PlayerStatusView.Gold; edge.effectDistance = new Vector2(2, -2);
        var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(28, 28, 22, 22);
        layout.spacing = 12;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandHeight = false;
        panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        title = BoxText("Title", 32, GameFontRole.Heading, PlayerStatusView.Gold);
        body = BoxText("Body", 28, GameFontRole.Body, PlayerStatusView.Cream);

        var footer = PlayerStatusView.Rect("Footer", box);
        var row = footer.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.spacing = 12;
        row.childAlignment = TextAnchor.MiddleLeft;
        row.childControlWidth = row.childControlHeight = true;
        row.childForceExpandWidth = false; row.childForceExpandHeight = false;
        counter = PlayerStatusView.Text(footer, "Counter", "", 24, GameFontRole.Body);
        counter.color = PlayerStatusView.Gold;
        counter.alignment = TextAlignmentOptions.Left;
        counter.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        CreateButton(footer, "Skip", new Color(.17f, .125f, .09f), Skip);
        nextLabel = CreateButton(footer, "Next", new Color(.42f, .29f, .13f), Next);
    }

    private TMP_Text BoxText(string name, float size, GameFontRole role, Color tint)
    {
        TMP_Text text = PlayerStatusView.Text(box, name, "", size, role);
        text.color = tint;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.textWrappingMode = TextWrappingModes.Normal;
        return text;
    }

    private static TMP_Text CreateButton(Transform parent, string label, Color fill, UnityEngine.Events.UnityAction click)
    {
        Image image = PlayerStatusView.Box(label + "Button", parent, fill);
        image.raycastTarget = true;
        var element = image.gameObject.AddComponent<LayoutElement>();
        element.minWidth = element.preferredWidth = 130;
        element.minHeight = element.preferredHeight = 50;
        var outline = image.gameObject.AddComponent<Outline>();
        outline.effectColor = PlayerStatusView.Gold; outline.effectDistance = new Vector2(1, -1);
        var button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(click);

        TMP_Text text = PlayerStatusView.Text(image.transform, "Label", label, 24, GameFontRole.Body);
        text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
        return text;
    }

    private void Pause()
    {
        active = this;
        previousTimeScale = Time.timeScale;
        Time.timeScale = 0;
    }

    private void Release()
    {
        if (active != this) return;
        active = null;
        Time.timeScale = previousTimeScale;
    }

    private void OnDestroy() => Release();

    private void ShowStep(int index)
    {
        stepIndex = index;
        title.text = steps[index].Title;
        body.text = steps[index].Body;
        counter.text = $"{index + 1} / {steps.Count}";
        nextLabel.text = index == steps.Count - 1 ? finishLabel : "Next";
        layoutDirty = true;
    }

    private void Next()
    {
        if (finished) return;
        if (stepIndex + 1 < steps.Count) ShowStep(stepIndex + 1);
        else Finish();
    }

    private void Skip()
    {
        if (finished) return;
        onSkipped?.Invoke();
        Finish();
    }

    private void Finish()
    {
        finished = true;
        Release();
        gameObject.SetActive(false);
        Destroy(gameObject);
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && (keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame)) Next();
    }

    // Late, so spotlights use positions the HUD and hand layout settled on this frame.
    private void LateUpdate()
    {
        group.alpha = Mathf.MoveTowards(group.alpha, 1, Time.unscaledDeltaTime / FadeSeconds);
        if (layoutDirty)
        {
            layoutDirty = false;
            LayoutRebuilder.ForceRebuildLayoutImmediate(box);
        }

        screenRects.Clear();
        steps[stepIndex].Targets?.Invoke(screenRects);
        localRects.Clear();
        Rect area = canvasRect.rect;
        foreach (Rect screenRect in screenRects)
        {
            Rect local = ToLocal(screenRect);
            local = Rect.MinMaxRect(Mathf.Max(area.xMin, local.xMin - HolePadding), Mathf.Max(area.yMin, local.yMin - HolePadding),
                Mathf.Min(area.xMax, local.xMax + HolePadding), Mathf.Min(area.yMax, local.yMax + HolePadding));
            if (local.width > 0 && local.height > 0) localRects.Add(local);
        }
        dim.SetHoles(localRects);
        PlaceBox(area);
    }

    private Rect ToLocal(Rect screen)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen.min, null, out Vector2 min);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen.max, null, out Vector2 max);
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    // Tries each side of the spotlights in turn, and centres the box when nothing is spotlighted or no side fits.
    private void PlaceBox(Rect area)
    {
        Vector2 half = box.rect.size * .5f;
        float minX = area.xMin + ScreenMargin + half.x, maxX = area.xMax - ScreenMargin - half.x;
        float minY = area.yMin + ScreenMargin + half.y, maxY = area.yMax - ScreenMargin - half.y;
        Vector2 position = area.center;

        if (localRects.Count > 0)
        {
            Rect spot = localRects[0];
            foreach (Rect rect in localRects)
                spot = Rect.MinMaxRect(Mathf.Min(spot.xMin, rect.xMin), Mathf.Min(spot.yMin, rect.yMin),
                    Mathf.Max(spot.xMax, rect.xMax), Mathf.Max(spot.yMax, rect.yMax));

            float above = spot.yMax + BoxGap + half.y, below = spot.yMin - BoxGap - half.y;
            float right = spot.xMax + BoxGap + half.x, left = spot.xMin - BoxGap - half.x;
            float alongX = Mathf.Clamp(spot.center.x, minX, maxX), alongY = Mathf.Clamp(spot.center.y, minY, maxY);
            if (above <= maxY) position = new Vector2(alongX, above);
            else if (below >= minY) position = new Vector2(alongX, below);
            else if (right <= maxX) position = new Vector2(right, alongY);
            else if (left >= minX) position = new Vector2(left, alongY);
        }

        box.localPosition = position;
    }
}
