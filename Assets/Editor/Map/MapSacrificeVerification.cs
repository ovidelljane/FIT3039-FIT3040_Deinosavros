using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Disposable play-session verification, with captures preserving the actual map camera projection.
public static class MapSacrificeVerification
{
    public static IEnumerator Run(Action<bool, string> check)
    {
        var menu = Object.FindFirstObjectByType<MainMenuController>();
        menu.GetType().GetMethod("StartRun", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(menu, null);
        yield return Until(() => SceneManager.GetActiveScene().name == "Deinosavros" && TimeTickSystem.Active?.IsStarted == true);
        foreach (var enemy in BattleScript.FindFighters("Enemy")) enemy.TakeDamage(10000);
        yield return Until(() => Object.FindFirstObjectByType<RewardScreenController>()?.IsPresented == true);
        Field<Button>(Object.FindFirstObjectByType<RewardScreenController>(), "skipButton").onClick.Invoke();
        yield return Until(() => SceneManager.GetActiveScene().name == "Map" && Object.FindFirstObjectByType<MapController>()?.CanInteract == true);
        var map = Object.FindFirstObjectByType<MapController>(); var session = map.Session; var panel = map.SacrificePanel;
        var canvas = panel.GetComponentInParent<Canvas>().rootCanvas;
        var views = Object.FindObjectsByType<MapCardView>(FindObjectsSortMode.None);
        var speed = views.First(v => v.Definition.overworldEffect.effectType == OverworldEffectType.ImprovePlayerAttackSpeed);
        string id = speed.InstanceId;
        int capacity = session.DeckCapacity; float interval = session.PlayerAttackSpeed;
        var deck = Field<Transform>(map, "deckContainer"); var layout = deck.GetComponent<HorizontalLayoutGroup>(); float spacing = layout.spacing;
        var nodeTransforms = map.Nodes.Select(n => (n.NodeId, n.transform.position, n.transform.localScale)).ToArray();
        check(panel.IsReady && !panel.IsOpen, "The authored inspector is ready and initially hidden.");
        var pointer = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(null, speed.transform.position), button = PointerEventData.InputButton.Left };
        speed.OnPointerEnter(pointer); yield return Delay(.4f);
        check(Field<GameObject>(speed, "backFace").activeSelf && !panel.IsOpen, "Hover flips to a concise back without opening a modal.");
        Capture(canvas, Camera.main, "back", check);
        speed.OnPointerClick(pointer); yield return null;
        check(panel.IsOpen && !map.CanInteract && panel.SelectedInstanceId == id, "Click pins the exact instance and blocks map input.");
        check(!panel.combatText.gameObject.activeSelf && ((RectTransform)panel.panel.Find("Scope")).anchoredPosition.y == 31,
            "The detail card has no central text and its scope heading is lowered.");
        check(panel.confirm.interactable && panel.benefitDetails.text.Contains(MapSacrificePreview.Rate(interval)), "The preview includes the actual attack rate.");
        check(session.DeckCapacity == capacity && session.PlayerAttackSpeed == interval && !session.SacrificeUsed, "Inspecting has no gameplay side effects.");
        Capture(canvas, Camera.main, "offering", check);
        speed.OnPointerExit(pointer); yield return Delay(.35f);
        check(panel.IsOpen, "Leaving the card does not close the stable inspector.");
        panel.close.onClick.Invoke(); check(!panel.IsOpen && !map.CanInteract, "Close suppresses same-frame map clicks.");
        yield return Until(() => map.CanInteract);
        check(session.DeckCapacity == capacity && !session.HasPendingModifier, "Cancel does not sacrifice or prepare an effect.");
        speed.OnPointerEnter(pointer); yield return Delay(.08f); speed.OnPointerClick(pointer);
        check(panel.IsOpen && panel.SelectedInstanceId == id, "A click during a partial flip still selects the correct instance.");
        panel.Close(); yield return Until(() => map.CanInteract); yield return Delay(.4f);
        check(Field<GameObject>(speed, "frontFace").activeSelf, "Closing permits a smooth return to the front.");

        // Exercise every definition with a pure view clone; these previews cannot be committed.
        var definitions = AssetDatabase.FindAssets("t:CardDefinition", new[] { "Assets/Data/Cards" })
            .Select(g => AssetDatabase.LoadAssetAtPath<CardDefinition>(AssetDatabase.GUIDToAssetPath(g))).ToArray();
        var overflow = new System.Collections.Generic.List<string>();
        foreach (var definition in definitions)
        {
            var view = Object.Instantiate(speed, canvas.transform); view.Initialize(map, definition, "preview-only");
            ((RectTransform)view.transform).anchoredPosition = new(-4000, 0);
            var preview = MapSacrificePreview.For(session, definition, "preview-only");
            map.RequestSacrifice(view); yield return null; Canvas.ForceUpdateCanvases();
            check(panel.benefitValue.text == preview.Value && panel.frame.sprite == definition.frontBorder && panel.illustration.sprite == definition.FrontIllustration,
                definition.name + ": shared data and single front frame.");
            foreach (var label in panel.panel.GetComponentsInChildren<TMP_Text>())
            { label.ForceMeshUpdate(); if (label.isTextOverflowing) overflow.Add(definition.name + ": " + label.name + " (" + label.preferredHeight + "/" + label.rectTransform.rect.height + ")"); }
            check(!panel.confirm.interactable, definition.name + ": foreign instance cannot be sacrificed.");
            panel.Close(); Object.Destroy(view.gameObject); yield return Until(() => map.CanInteract);
        }
        check(overflow.Count == 0, "All eleven panels fit: " + string.Join("; ", overflow));

        check(session.TryAddCard(speed.Definition, out _), "A same-definition instance can be added for duplicate isolation testing.");
        yield return null; yield return null;
        var duplicate = session.RunDeck.Last(c => !c.sacrificed && c.definition == speed.Definition);
        views = Object.FindObjectsByType<MapCardView>(FindObjectsSortMode.None);
        var original = views.Single(v => v.InstanceId == id);
        capacity = session.DeckCapacity;
        map.RequestSacrifice(original); panel.Confirm(); panel.Confirm();
        check(session.RunDeck.Single(c => c.instanceId == id).sacrificed && !duplicate.sacrificed, "Only the selected same-name instance is removed.");
        check(session.DeckCapacity == capacity - original.Definition.capacityCost && session.HasPendingModifier, "A double click commits capacity and offering only once.");
        check(session.PlayerAttackSpeed == interval, "Preparing an offering does not apply it to the player early.");
        yield return Until(() => map.CanInteract);
        check(!panel.IsBusy && !panel.IsOpen && panel.statusText.text.StartsWith("Next battle:"), "Feedback ends with a persistent prepared-benefit indicator.");
        Capture(canvas, Camera.main, "prepared", check);
        var another = Object.FindObjectsByType<MapCardView>(FindObjectsSortMode.None).First();
        map.RequestSacrifice(another); yield return null;
        check(panel.IsOpen && !panel.confirm.interactable && panel.reason.text.Contains("Already sacrificed"), "Used sacrifices remain inspectable with an explicit unavailable reason.");
        Capture(canvas, Camera.main, "unavailable", check);
        panel.Close(); yield return Until(() => map.CanInteract);
        panel.statusButton.onClick.Invoke(); yield return null;
        check(panel.statusPopup.activeSelf && panel.statusDetails.text.Contains("Attack interval"), "Prepared benefit can be inspected without a second sacrifice.");
        var prepared = session.PendingModifier;
        var pendingField = typeof(RunSession).GetField("pendingModifier", BindingFlags.Instance | BindingFlags.NonPublic);
        try
        {
            foreach (var card in session.RunDeck.Select(c => c.definition).Distinct())
            {
                pendingField.SetValue(session, new PendingEncounterModifier { sourceCardId = card.cardId,
                    effectType = card.overworldEffect.effectType, magnitude = card.overworldEffect.magnitude, isValid = true });
                panel.RefreshStatus(); panel.statusDetails.ForceMeshUpdate();
                check(!panel.statusDetails.isTextOverflowing, card.name + ": pending summary fits.");
            }
        }
        finally { pendingField.SetValue(session, prepared); panel.RefreshStatus(); }
        Capture(canvas, Camera.main, "pending-details", check);
        panel.statusButton.onClick.Invoke();
        check(layout.spacing == spacing && nodeTransforms.SequenceEqual(map.Nodes.Select(n => (n.NodeId, n.transform.position, n.transform.localScale))), "Deck spacing and all node transforms remain unchanged.");
        check(map.TryRestartRunForTesting(), "A new run can reset after the inspector closes.");
        yield return null;
        check(!session.SacrificeUsed && !session.HasPendingModifier && !panel.IsOpen, "Restart clears offering UI and data.");
        yield return VerifyDeparture(map, check);
        Directory.CreateDirectory(MapSacrificeAuthoring.Results);
        File.WriteAllText(MapSacrificeAuthoring.Results + "/checks.txt", "PASS: all definitions, three resolutions, stable hover, cancellation, exact-instance removal, repeated commit, disabled state, pending summary and restart.\n");
    }

    private static IEnumerator VerifyDeparture(MapController map, Action<bool, string> check)
    {
        var session = map.Session; var panel = map.SacrificePanel;
        var definitions = AssetDatabase.LoadAssetAtPath<CardPool>("Assets/Data/CardPool.asset").Cards;
        var costThree = definitions.First(c => c.capacityCost == 3);
        var costTwo = definitions.First(c => c.capacityCost == 2);
        check(session.DeckCapacity == 22 && session.TryAddCard(costThree, out _) && session.DeckCapacity == 25,
            "The boundary fixture has exactly 25 capacity.");
        check(!map.NeedsSacrificeReminder(MapEncounterKind.Battle), "Exactly 25 never prompts.");
        check(session.TryAddCard(costTwo, out _) && session.DeckCapacity == 27, "The upper fixture has 27 capacity.");
        check(map.NeedsSacrificeReminder(MapEncounterKind.Battle) && map.NeedsSacrificeReminder(MapEncounterKind.Boss), "Battle and Boss warn above 25.");
        check(!map.NeedsSacrificeReminder(MapEncounterKind.Opportunity) && !map.NeedsSacrificeReminder(MapEncounterKind.Recovery), "Noncombat destinations do not warn.");
        yield return null;
        var start = map.Nodes.Single(n => n.NodeId == session.Progress.CurrentNodeId);
        string current = session.Progress.CurrentNodeId;
        map.SelectNode(start);
        check(panel.IsDepartureWarningOpen && !map.CanInteract && session.Progress.CurrentEncounter == null && session.Progress.CurrentNodeId == current,
            "The reminder blocks input before movement, a travel ticket or any resource changes.");
        map.SelectNode(start);
        check(session.Progress.CurrentEncounter == null, "Repeated map clicks cannot bypass the reminder.");
        Capture(panel.GetComponentInParent<Canvas>().rootCanvas, Camera.main, "departure-warning", check);
        panel.departureBack.onClick.Invoke(); yield return Until(() => map.CanInteract);
        check(!panel.IsDepartureWarningOpen && session.DeckCapacity == 27 && session.Progress.CurrentNodeId == current && !session.SacrificeUsed,
            "Returning from the reminder changes no run data.");
        var instance = session.RunDeck.Last(c => c.definition == costTwo && !c.sacrificed);
        check(session.TrySacrificeInstance(instance.instanceId) && session.TryAddCard(costTwo, out _), "The prepared-offering fixture remains above 25.");
        check(session.DeckCapacity == 27 && !map.NeedsSacrificeReminder(MapEncounterKind.Battle), "An already prepared offering suppresses the reminder.");
        check(map.TryRestartRunForTesting(), "The confirmed departure uses a fresh disposable run.");
        session.TryAddCard(costThree, out _); session.TryAddCard(costTwo, out _); yield return null;
        start = map.Nodes.Single(n => n.NodeId == session.Progress.CurrentNodeId);
        map.SelectNode(start); panel.departureContinue.onClick.Invoke(); panel.departureContinue.onClick.Invoke();
        string encounter = session.Progress.CurrentEncounter?.EncounterId;
        check(!string.IsNullOrEmpty(encounter) && !session.SacrificeUsed && !session.HasPendingModifier, "Confirming enters once without inventing a sacrifice.");
        yield return Until(() => SceneManager.GetActiveScene().name == "Deinosavros" && TimeTickSystem.Active?.IsStarted == true);
        check(session.Progress.CurrentEncounter.EncounterId == encounter, "Repeated confirmation creates only one encounter.");
        var card = Object.FindObjectsByType<BuffCards>(FindObjectsSortMode.None).First(c => c.isActiveAndEnabled);
        var label = Field<TextMeshProUGUI>(card, "costText"); label.ForceMeshUpdate();
        check((label.fontStyle & FontStyles.Bold) != 0 && label.fontSize > 22 && !label.isTextOverflowing, "Combat cost digits are enlarged, bold and remain readable.");
    }

    public static void Capture(Canvas canvas, Camera camera, string name, Action<bool, string> check)
    {
        var scaler = canvas.GetComponent<CanvasScaler>(); var rect = (RectTransform)canvas.transform;
        var mode = canvas.renderMode; var assignedCamera = canvas.worldCamera; bool scaleEnabled = scaler.enabled;
        var position = rect.localPosition; var rotation = rect.localRotation; var localScale = rect.localScale; var delta = rect.sizeDelta;
        float scale = canvas.scaleFactor, aspect = camera.aspect; var target = camera.targetTexture; var active = RenderTexture.active;
        try
        {
            scaler.enabled = false; canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = camera; canvas.scaleFactor = 1;
            foreach (var size in new[] { new Vector2Int(1920,1080), new Vector2Int(2560,1440), new Vector2Int(1920,1200) })
            {
                camera.aspect = (float)size.x / size.y;
                float uiScale = Mathf.Sqrt(size.x / 1920f * size.y / 1080f);
                float height = size.y / uiScale;
                rect.sizeDelta = new(size.x / uiScale, height);
                float distance = camera.nearClipPlane + 1;
                rect.position = camera.transform.position + camera.transform.forward * distance; rect.rotation = camera.transform.rotation;
                rect.localScale = Vector3.one * (2 * distance * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * .5f) / height);
                Canvas.ForceUpdateCanvases();
                var panel = canvas.GetComponentInChildren<MapSacrificePanel>(true);
                if (panel != null && panel.IsOpen)
                {
                    var corners = new Vector3[4]; panel.panel.GetWorldCorners(corners);
                    foreach (var p in corners) { var v = camera.WorldToViewportPoint(p); check(v.x >= 0 && v.x < .27f && v.y > .26f && v.y < .95f, "Offering stays left of the map and above the cards at " + size); }
                }
                if (panel != null && panel.IsDepartureWarningOpen)
                {
                    foreach (var label in panel.departurePanel.GetComponentsInChildren<TMP_Text>())
                    { label.ForceMeshUpdate(); check(!label.isTextOverflowing, "Departure reminder text fits at " + size); }
                }
                var rt = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGB32); Texture2D image = null;
                try
                {
                    rt.Create(); camera.targetTexture = rt;
                    RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
                    RenderTexture.active = rt; image = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
                    image.ReadPixels(new Rect(0,0,size.x,size.y),0,0); image.Apply();
                    Directory.CreateDirectory(MapSacrificeAuthoring.Results);
                    File.WriteAllBytes(MapSacrificeAuthoring.Results + "/" + name + "-" + size.x + "x" + size.y + ".png", image.EncodeToPNG());
                }
                finally { camera.targetTexture = target; RenderTexture.active = active; if (image != null) Object.DestroyImmediate(image); rt.Release(); Object.DestroyImmediate(rt); }
            }
        }
        finally
        {
            camera.aspect = aspect; canvas.renderMode = mode; canvas.worldCamera = assignedCamera; canvas.scaleFactor = scale;
            rect.localPosition = position; rect.localRotation = rotation; rect.localScale = localScale; rect.sizeDelta = delta;
            scaler.enabled = scaleEnabled; Canvas.ForceUpdateCanvases();
        }
    }
    private static IEnumerator Until(Func<bool> condition)
    { float end = Time.realtimeSinceStartup + 60; while (!condition()) { if (Time.realtimeSinceStartup > end) throw new TimeoutException("Sacrifice check timed out."); yield return null; } }
    private static IEnumerator Delay(float seconds) { float end = Time.realtimeSinceStartup + seconds; while (Time.realtimeSinceStartup < end) yield return null; }
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
}
