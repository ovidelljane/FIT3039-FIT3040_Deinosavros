using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Explicit, disposable visual regression check. Never saves gameplay scenes.
public static class MapCardArtworkVerification
{
    public static void StartBatch()
    {
        var fleet = Card("FleetFootwork");
        var illustrationRegion = new Rect(.126f, .12f, .748f, .68f);
        if (fleet.frontArtworkRegion != illustrationRegion)
        {
            fleet.frontArtworkRegion = illustrationRegion;
            EditorUtility.SetDirty(fleet);
            AssetDatabase.SaveAssetIfDirty(fleet);
        }
        foreach (string name in new[] { "TidalWave", "UncertainFates" })
        {
            var definition = Card(name);
            if (definition.frontArtwork == null) throw new InvalidOperationException("Missing imported illustration: " + name);
            string path = AssetDatabase.GetAssetPath(definition.combatPrefab);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var artwork = Field<Image>(root.GetComponent<BuffCards>(), "artworkImage");
                artwork.sprite = definition.frontArtwork;
                artwork.enabled = true;
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        RunIntegrationPlayVerification.RunCardArtwork();
    }

    public static IEnumerator Run(Action<bool, string> check)
    {
        var menu = Object.FindFirstObjectByType<MainMenuController>();
        menu.GetType().GetMethod("StartRun", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(menu, null);
        float deadline = Time.realtimeSinceStartup + 45f;
        while (SceneManager.GetActiveScene().name != "Deinosavros" || TimeTickSystem.Active?.IsStarted != true)
        {
            if (Time.realtimeSinceStartup > deadline) throw new InvalidOperationException("Initial battle did not become ready.");
            yield return null;
        }
        foreach (var enemy in BattleScript.FindFighters("Enemy")) enemy.TakeDamage(10000);
        while (Object.FindFirstObjectByType<RewardScreenController>()?.IsPresented != true)
        {
            if (Time.realtimeSinceStartup > deadline) throw new InvalidOperationException("Victory did not become ready.");
            yield return null;
        }
        Field<Button>(Object.FindFirstObjectByType<RewardScreenController>(), "skipButton").onClick.Invoke();
        deadline = Time.realtimeSinceStartup + 45f;
        while (SceneManager.GetActiveScene().name != "Map" || Object.FindFirstObjectByType<MapController>()?.CanInteract != true)
        {
            if (Time.realtimeSinceStartup > deadline) throw new InvalidOperationException("Map did not become ready.");
            yield return null;
        }
        yield return null;
        var map = Object.FindObjectsByType<MapController>(FindObjectsSortMode.None)
            .First(m => m.gameObject.scene == SceneManager.GetActiveScene());
        var template = Field<MapCardView[]>(map, "prebuiltCardViews").First(v => v.gameObject.activeInHierarchy);
        var deck = Field<Transform>(map, "deckContainer");
        var layout = deck.GetComponent<HorizontalLayoutGroup>();
        float spacing = layout.spacing;
        var order = deck.GetComponentsInChildren<MapCardView>().Select(v => v.InstanceId).ToArray();
        var definitions = AssetDatabase.FindAssets("t:CardDefinition", new[] { "Assets/Data/Cards" })
            .Select(g => AssetDatabase.LoadAssetAtPath<CardDefinition>(AssetDatabase.GUIDToAssetPath(g)))
            .OrderBy(c => c.displayName).ToArray();
        check(definitions.Length == 11 && definitions.All(c => c.FrontIllustration != null), "All eleven cards have illustrations.");
        check(Card("TidalWave").frontArtwork.name == "TIDAL_WAVE" && Card("UncertainFates").frontArtwork.name == "GOLDEN_CHARIOT",
            "The two newly imported illustrations are bound to the intended definitions.");
        var fleet = Card("FleetFootwork");
        check(fleet.FrontIllustration != fleet.frontArtwork && fleet.FrontIllustration == fleet.FrontIllustration &&
            fleet.FrontIllustration.rect.width < fleet.frontArtwork.rect.width,
            "The legacy framed illustration is cropped once and cached, without changing its source texture: " + fleet.frontArtworkRegion);

        var board = new GameObject("Card Artwork Check", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        var canvas = board.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 30000;
        var scaler = board.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
        var background = new GameObject("Background", typeof(RectTransform), typeof(Image));
        background.transform.SetParent(board.transform, false);
        var backgroundRect = (RectTransform)background.transform;
        backgroundRect.anchorMin = Vector2.zero; backgroundRect.anchorMax = Vector2.one;
        backgroundRect.sizeDelta = Vector2.zero;
        background.GetComponent<Image>().color = new Color(.12f, .10f, .08f, 1f);
        background.GetComponent<Image>().raycastTarget = false;
        var views = new MapCardView[definitions.Length];
        try
        {
            for (int i = 0; i < definitions.Length; i++)
            {
                var definition = definitions[i];
                var view = definition.mapPrefab != null
                    ? Object.Instantiate(definition.mapPrefab, board.transform).GetComponent<MapCardView>()
                    : Object.Instantiate(template, board.transform);
                views[i] = view; view.Initialize(map, definition, "art-check-" + i);
                var rect = (RectTransform)view.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f); rect.pivot = new Vector2(.5f, .5f);
                rect.sizeDelta = new Vector2(150, 250); rect.localScale = Vector3.one * 1.25f;
                rect.anchoredPosition = new Vector2((i % 6 - 2.5f) * 300, i < 6 ? 250 : -250);
                var front = Field<GameObject>(view, "combatFrontInstance");
                var combat = front.GetComponent<BuffCards>();
                check(Field<Image>(combat, "artworkImage").sprite == definition.FrontIllustration &&
                    Field<Image>(combat, "borderImage").sprite == definition.frontBorder,
                    definition.name + ": one illustration beneath the intended frame.");
                view.Initialize(map, definition, "art-check-" + i);
                yield return null;
                check(Field<GameObject>(view, "backFace").GetComponentsInChildren<Image>(true)
                    .Count(image => image.enabled && image.gameObject.activeSelf && image.sprite == definition.backArtwork) == 1,
                    definition.name + ": repeated binding keeps one active back frame.");
            }
            yield return Delay(.2f);
            MapOpportunityVerification.CaptureCanvas(canvas, Camera.main, "card-fronts");
            Canvas.ForceUpdateCanvases();
            foreach (var view in views)
            {
                var point = RectTransformUtility.WorldToScreenPoint(null, view.transform.position);
                view.OnPointerEnter(new PointerEventData(EventSystem.current) { position = point });
            }
            yield return Delay(.8f);
            var reference = Field<Image>(views[0], "backArtwork");
            var referenceBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(views[0].transform, reference.transform);
            var overflow = new System.Collections.Generic.List<string>();
            foreach (var view in views)
            {
                var back = Field<Image>(view, "backArtwork");
                check(Field<GameObject>(view, "backFace").activeSelf && !Field<GameObject>(view, "frontFace").activeSelf,
                    view.Definition.name + ": hover shows only the back.");
                check(back.rectTransform.rect == reference.rectTransform.rect && back.preserveAspect &&
                    back.rectTransform.localScale == Vector3.one && back.rectTransform.anchoredPosition == Vector2.zero,
                    view.Definition.name + ": identical frame size, centering and undistorted aspect.");
                var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(view.transform, back.transform);
                check(Vector3.Distance(bounds.size, referenceBounds.size) < .01f &&
                    Vector3.Distance(bounds.center, referenceBounds.center) < .01f,
                    view.Definition.name + ": inherited parent transforms do not change the displayed back size.");
                var title = Field<TMPro.TMP_Text>(view, "titleText"); title.ForceMeshUpdate();
                check(!title.isTextOverflowing && title.textInfo.lineCount <= 2, view.Definition.name + ": title fits within two lines.");
                foreach (var label in Field<GameObject>(view, "backFace").GetComponentsInChildren<TMPro.TMP_Text>())
                {
                    label.ForceMeshUpdate();
                    if (label.isTextOverflowing) overflow.Add(view.Definition.name + ": " + label.name +
                        " (" + label.preferredHeight + "/" + label.rectTransform.rect.height + ")");
                }
                check(Field<Button>(view, "sacrificeButton") == null && Field<TMPro.TMP_Text>(view, "effectText").gameObject.activeSelf,
                    view.Definition.name + ": the back shows the benefit and opens a stable inspector instead of a tiny sacrifice button.");
            }
            MapOpportunityVerification.CaptureCanvas(canvas, Camera.main, "card-backs");
            check(overflow.Count == 0, "All offering back labels fit: " + string.Join("; ", overflow));
            foreach (var view in views) view.OnPointerExit(null);
            yield return Delay(.5f);
            check(views.All(v => Field<GameObject>(v, "frontFace").activeSelf), "Every card returns to its front after hover.");
            // Reverse a partial flip to exercise the existing hit-area/animation separation.
            var last = views.Last();
            last.OnPointerEnter(new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(null, last.transform.position) });
            yield return Delay(.1f); last.OnPointerExit(null); yield return Delay(.4f);
            check(Field<GameObject>(last, "frontFace").activeSelf, "A partial flip reverses without sticking on the back.");
        }
        finally { Object.Destroy(board); }
        yield return null;
        check(layout.spacing == spacing && deck.GetComponentsInChildren<MapCardView>().Select(v => v.InstanceId).SequenceEqual(order),
            "The real Map deck keeps its order, overlap and instance IDs.");
        // Exercise the same illustration binding in combat and rewards without playing a card.
        var rewardPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/RewardCardPrefab.prefab");
        foreach (var definition in definitions)
        {
            var combat = Object.Instantiate(definition.combatPrefab).GetComponent<BuffCards>();
            combat.Initialize(null, definition);
            check(Field<Image>(combat, "artworkImage").sprite == definition.FrontIllustration,
                definition.name + ": combat uses the corrected illustration.");
            Object.Destroy(combat.gameObject);
            var reward = Object.Instantiate(rewardPrefab).GetComponent<RewardCardView>(); reward.Initialize(definition, null);
            check(Field<Image>(reward, "artwork").sprite == definition.FrontIllustration,
                definition.name + ": rewards use the corrected illustration.");
            Object.Destroy(reward.gameObject);
        }
    }

    private static CardDefinition Card(string name) => AssetDatabase.LoadAssetAtPath<CardDefinition>("Assets/Data/Cards/" + name + ".asset");
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);
    private static IEnumerator Delay(float seconds)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < until) yield return null;
    }
}
