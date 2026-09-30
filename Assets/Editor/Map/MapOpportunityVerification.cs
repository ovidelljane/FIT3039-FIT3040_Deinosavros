using System;
using System.IO;
using System.Linq;
using Deinosavros.MapReview;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static class MapOpportunityVerification
{
    public const string ProfilePath = "Assets/Art/Map/Settings/Opportunity.asset";
    private static int checks;

    public static void ApplyAssets()
    {
        var profile = AssetDatabase.LoadAssetAtPath<MapOpportunityProfile>(ProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<MapOpportunityProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
        }
        Require(profile.IsValid, "Opportunity profile is valid.");
        var catalog = AssetDatabase.LoadAssetAtPath<MapNodeCatalog>("Assets/Art/Map/Settings/Nodes.asset");
        float total = profile.cardWeight + profile.recoveryWeight + profile.battleWeight;
        foreach (var node in catalog.nodes.Where(node => node.kind == MapEncounterKind.Opportunity))
            node.summary = $"Card {100 * profile.cardWeight / total:0}% | Heal {100 * profile.recoveryWeight / total:0}% | Battle {100 * profile.battleWeight / total:0}%";
        EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets();

        var scene = EditorSceneManager.OpenScene("Assets/Scenes/MapOpportunity.unity");
        var previous = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MapNonCombatController>(true)).Single();
        var receiver = previous as MapOpportunityController;
        if (receiver == null)
        {
            receiver = previous.gameObject.AddComponent<MapOpportunityController>();
            receiver.kind = MapEncounterKind.Opportunity; receiver.routing = previous.routing;
            receiver.font = previous.font; receiver.symbols = previous.symbols;
            Object.DestroyImmediate(previous);
        }
        receiver.profile = AssetDatabase.LoadAssetAtPath<MapOpportunityProfile>(ProfilePath);
        receiver.rewardCardPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/RewardCardPrefab.prefab");
        Require(receiver.profile != null && receiver.rewardCardPrefab != null, "Event scene asset bindings exist.");
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
    }

    [MenuItem("Tools/Map/Verify Opportunity Transactions")]
    public static void VerifyState()
    {
        checks = 0;
        var graph = AssetDatabase.LoadAssetAtPath<MapGraphDefinition>("Assets/Art/Map/Settings/Graph.asset");
        var catalog = AssetDatabase.LoadAssetAtPath<MapNodeCatalog>("Assets/Art/Map/Settings/Nodes.asset");
        var pool = AssetDatabase.LoadAssetAtPath<CardPool>("Assets/Data/CardPool.asset");
        var profile = AssetDatabase.LoadAssetAtPath<MapOpportunityProfile>(ProfilePath);
        var original = RunSession.Instance;
        Require(profile != null && profile.IsValid, "Authored profile exists.");
        var distribution = ScriptableObject.CreateInstance<MapOpportunityProfile>();
        for (int i = 0; i < 100; i++)
            Require(distribution.OutcomeAt(i / 100d) == (i < 40 ? MapOpportunityOutcome.Card :
                i < 60 ? MapOpportunityOutcome.Recovery : MapOpportunityOutcome.Battle), "40/20/40 interval boundaries.");
        Object.DestroyImmediate(distribution);

        var root = new GameObject("Opportunity Test") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        var session = root.AddComponent<RunSession>();
        var player = root.AddComponent<BattleScript>();
        session.InitializeProgress(graph, catalog);
        var card = pool.Cards.OrderByDescending(c => c.capacityCost).First();
        var rules = RunIntegrationTests.SetFixtureRules(session, 6 * card.capacityCost);
        try
        {
            for (int i = 0; i < session.DeckCapacityLimit / card.capacityCost; i++)
                Require(session.TryAddCard(card, out _), "Full-capacity duplicate-card baseline.");
            StartEntrance(session);
            session.SetNextOpportunityForTesting(MapOpportunityOutcome.Card);
            var ticket = EnterOpportunity(session, profile, pool);
            var receipt = session.GetNonCombatReceipt(ticket.EncounterId);
            var offer = receipt.Offers.Single();
            Require(session.NextOpportunityForTesting == null, "Forced result is consumed once.");
            Require(session.TryPrepareOpportunityEvent(ticket.EncounterId, profile, pool) && receipt.Offers.Single() == offer,
                "Repeated preparation retains the same card.");
            Require(!session.CompleteNonCombatEncounter(ticket.EncounterId), "Unresolved card cannot complete.");
            Require(!session.TryClaimOpportunityCard(ticket.EncounterId, null, out _) && !receipt.Resolved,
                "A null claim does not silently skip the reward.");
            Require(!session.TryResolveOpportunityReward(ticket.EncounterId, offer, null, out _), "Full capacity rejects direct claim.");
            string[] ids = session.RunDeck.Select(c => c.instanceId).ToArray();
            Require(!session.TryResolveOpportunityReward(ticket.EncounterId, offer, "missing", out _) &&
                ids.SequenceEqual(session.RunDeck.Select(c => c.instanceId)), "Failed replacement is atomic.");
            int expectedCapacity = session.DeckCapacity - card.capacityCost + offer.capacityCost;
            Require(session.TryResolveOpportunityReward(ticket.EncounterId, offer, ids[3], out _) && session.DeckCapacity == expectedCapacity &&
                !session.RunDeck.Any(c => c.instanceId == ids[3]) && ids.Where((_, i) => i != 3).All(id => session.RunDeck.Any(c => c.instanceId == id)),
                "Only the chosen same-name instance is replaced.");
            Require(!session.SacrificeUsed && !session.HasPendingModifier, "Replacement is not an offering.");
            Require(!session.TryResolveOpportunityReward(ticket.EncounterId, offer, ids[4], out _), "No duplicate claim.");
            Require(session.CompleteNonCombatEncounter(ticket.EncounterId) && !session.CompleteNonCombatEncounter(ticket.EncounterId), "One completion only.");

            StartEntrance(session);
            Require(session.TrySacrifice(card), "Offering before event.");
            session.SetNextOpportunityForTesting(MapOpportunityOutcome.Card);
            ticket = EnterOpportunity(session, profile, pool);
            receipt = session.GetNonCombatReceipt(ticket.EncounterId);
            expectedCapacity = session.DeckCapacity + receipt.Offers.Single().capacityCost;
            Require(session.TryClaimOpportunityCard(ticket.EncounterId, receipt.Offers.Single(), out _) && session.DeckCapacity == expectedCapacity,
                "A free capacity slot accepts one card.");
            Require(session.HasPendingModifier && session.SacrificeUsed, "Card reward preserves pending offering.");
            session.CompleteNonCombatEncounter(ticket.EncounterId);
            Require(!session.TryClaimOpportunityCard(ticket.EncounterId, offer, out _), "Expired reward rejected.");

            foreach (int health in new[] { 40, 97, 100 })
            {
                StartEntrance(session); player.maxHealth = 100; player.health = health; session.CapturePlayerStats(player);
                session.SetNextOpportunityForTesting(MapOpportunityOutcome.Recovery);
                ticket = EnterOpportunity(session, profile, pool);
                Require(!session.CompleteNonCombatEncounter(ticket.EncounterId), "Recovery must resolve first.");
                Require(session.TryResolveOpportunityRecovery(ticket.EncounterId) && session.PlayerHealth == Math.Min(100, health + 15), "Capped 15 percent healing.");
                Require(session.TryResolveOpportunityRecovery(ticket.EncounterId) && session.PlayerHealth == Math.Min(100, health + 15), "No repeated healing.");
                Require(!session.TryPrepareOpportunityBattle(ticket.EncounterId), "Recovery cannot become battle.");
                session.CompleteNonCombatEncounter(ticket.EncounterId);
            }
            // Non-round maxima exercise integer ceiling independently of the default 100 HP.
            player.maxHealth = 101;
            StartEntrance(session); player.health = 30; session.CapturePlayerStats(player);
            session.SetNextOpportunityForTesting(MapOpportunityOutcome.Recovery);
            ticket = EnterOpportunity(session, profile, pool); session.TryResolveOpportunityRecovery(ticket.EncounterId);
            Require(session.PlayerHealth == 46, "101 maximum HP rounds fifteen percent up to sixteen.");

            StartEntrance(session); player.health = 70; session.CapturePlayerStats(player);
            Require(session.TrySacrifice(card), "Battle-event pending offering.");
            session.SetNextOpportunityForTesting(MapOpportunityOutcome.Battle);
            ticket = EnterOpportunity(session, profile, pool);
            receipt = session.GetNonCombatReceipt(ticket.EncounterId);
            Require(!session.CompleteNonCombatEncounter(ticket.EncounterId) && !session.TryConsumePendingModifier(out _), "Ambush cannot skip or consume early.");
            Require(session.TryPrepareOpportunityBattle(ticket.EncounterId) && ticket.Kind == MapEncounterKind.Opportunity && ticket.IsCombat,
                "Receiver changes while node type stays Opportunity.");
            Require(!session.CancelPreparedTravel(ticket.EncounterId), "Child loading cannot roll back the committed map visit.");
            Require(session.CancelOpportunityBattle(ticket.EncounterId) && receipt.Outcome == MapOpportunityOutcome.Battle &&
                session.Progress.CurrentNodeId == ticket.NodeId && session.HasPendingModifier, "Failed battle preserves event and offering.");
            Require(session.TryPrepareOpportunityBattle(ticket.EncounterId) && session.ConfirmEncounterStarted(ticket.EncounterId), "Retry uses the same encounter.");
            Require(receipt.BattleState == MapOpportunityBattleState.Active && session.TryConsumePendingModifier(out _) &&
                !session.TryConsumePendingModifier(out _), "Battle consumes offering exactly once after readiness.");
            Require(session.TryPrepareBattleReward(ticket.EncounterId, pool, 3, out var rewards) && rewards.Count == 3, "Ambush receives normal victory offers.");
            Require(session.TryResolveBattleReward(ticket.EncounterId, null, null, out _) && session.CompleteEncounter(ticket.EncounterId, true), "Battle reward settles the same node.");
            Require(session.Progress.CompletedCount == 2 && session.Progress.CurrentNodeId == "level_02_01" &&
                session.Progress.NodeState("level_03_01") == MapLocationState.Available && !session.SacrificeUsed && receipt.Completed,
                "One layer advances and victory restores the offering allowance.");
            Require(!session.CompleteEncounter(ticket.EncounterId, true), "Duplicate victory rejected.");
            StartEntrance(session); session.SetNextOpportunityForTesting(MapOpportunityOutcome.Battle);
            ticket = EnterOpportunity(session, profile, pool); session.TryPrepareOpportunityBattle(ticket.EncounterId);
            session.ConfirmEncounterStarted(ticket.EncounterId); session.CompleteEncounter(ticket.EncounterId, false);
            Require(session.Progress.Phase == MapProgressPhase.Lost && session.Progress.CompletedCount == 1 &&
                session.GetNonCombatReceipt(ticket.EncounterId).BattleState == MapOpportunityBattleState.Lost, "Ambush defeat ends the run.");
            session.BeginNewRun();
            Require(session.GetNonCombatReceipt(ticket.EncounterId) == null && session.NextOpportunityForTesting == null, "New run clears results and override.");
            Require(RunSession.Instance == original, "Fixtures do not replace the live session.");
            Directory.CreateDirectory(RunIntegrationSetup.ResultDirectory);
            File.WriteAllText(RunIntegrationSetup.ResultDirectory + "/opportunity-state.txt", "PASS: " + checks + " opportunity transaction checks.\n");
        }
        finally { Object.DestroyImmediate(root); Object.DestroyImmediate(rules); }
    }

    private static void StartEntrance(RunSession session)
    {
        session.Progress.BeginNewRun(AssetDatabase.LoadAssetAtPath<MapGraphDefinition>("Assets/Art/Map/Settings/Graph.asset"),
            AssetDatabase.LoadAssetAtPath<MapNodeCatalog>("Assets/Art/Map/Settings/Nodes.asset"));
        Require(session.TryBeginTravel("level_01_01", out var start), "Entrance available.");
        session.Progress.MarkLoading(start.EncounterId); session.ConfirmEncounterStarted(start.EncounterId);
        session.CompleteEncounter(start.EncounterId, true);
    }
    private static MapTravelTicket EnterOpportunity(RunSession session, MapOpportunityProfile profile, CardPool pool)
    {
        Require(session.TryBeginTravel("level_02_01", out var ticket), "Opportunity available.");
        session.Progress.MarkLoading(ticket.EncounterId);
        Require(session.TryPrepareOpportunityEvent(ticket.EncounterId, profile, pool) && session.ConfirmEncounterStarted(ticket.EncounterId), "Event receiver prepared.");
        return ticket;
    }
    private static void Require(bool value, string message)
    { checks++; if (!value) throw new InvalidOperationException(message); }

    public static void CaptureLayouts(MapOpportunityController receiver, string label)
    {
        var canvas = receiver.Panel.GetComponentInParent<Canvas>();
        CaptureCanvas(canvas, Camera.main, "opportunity-" + label, receiver.Panel);
    }

    public static void CaptureCanvas(Canvas canvas, Camera camera, string label, RectTransform panel = null)
    {
        var scaler = canvas.GetComponent<CanvasScaler>();
        var previousMode = canvas.renderMode; var previousCamera = canvas.worldCamera;
        var previousTarget = camera.targetTexture; int previousMask = camera.cullingMask;
        float previousScale = canvas.scaleFactor; float previousPlane = canvas.planeDistance;
        float previousAspect = camera.aspect; var previousActive = RenderTexture.active;
        bool previousOrthographic = camera.orthographic; float previousSize = camera.orthographicSize;
        var canvasRect = (RectTransform)canvas.transform;
        var previousPosition = canvasRect.localPosition; var previousRotation = canvasRect.localRotation;
        var previousLocalScale = canvasRect.localScale; var previousDelta = canvasRect.sizeDelta;
        bool scalerEnabled = scaler.enabled;
        try
        {
            // Use the same reference-pixel layout in an offscreen orthographic projection.
            // Batch Mode does not update the native Game View display size.
            scaler.enabled = false; canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = camera; canvas.scaleFactor = 1;
            canvasRect.position = camera.transform.position + camera.transform.forward * 10;
            canvasRect.rotation = camera.transform.rotation; canvasRect.localScale = Vector3.one;
            camera.orthographic = true; camera.cullingMask = ~0;
            foreach (var size in new[] { new Vector2Int(1920, 1080), new Vector2Int(2560, 1440), new Vector2Int(1920, 1200) })
            {
                var target = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGB32);
                Texture2D image = null;
                try
                {
                    target.Create(); camera.targetTexture = target; camera.aspect = (float)size.x / size.y;
                    float scale = Mathf.Sqrt(size.x / 1920f * (size.y / 1080f));
                    canvasRect.sizeDelta = new Vector2(size.x / scale, size.y / scale);
                    camera.orthographicSize = size.y / scale * .5f;
                    Canvas.ForceUpdateCanvases();
                    var corners = new Vector3[4];
                    if (panel != null)
                    {
                        panel.GetWorldCorners(corners);
                        foreach (var corner in corners)
                        {
                            var view = camera.WorldToViewportPoint(corner);
                            Require(view.x >= 0 && view.x <= 1 && view.y >= 0 && view.y <= 1, "Event panel stays inside " + size + ": " + view);
                        }
                        foreach (var button in panel.GetComponentsInChildren<Button>())
                        {
                            if (!button.gameObject.activeInHierarchy || !button.interactable) continue;
                            // Replacement cards can scroll; persistent action buttons must remain fully visible.
                            if (button.GetComponentInParent<ScrollRect>() != null) continue;
                            button.GetComponent<RectTransform>().GetWorldCorners(corners);
                            foreach (var corner in corners)
                            {
                                var view = camera.WorldToViewportPoint(corner);
                                Require(view.x >= 0 && view.x <= 1 && view.y >= 0 && view.y <= 1, "Action stays inside " + size);
                            }
                        }
                    }
                    RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                    RenderTexture.active = target;
                    image = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
                    image.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0); image.Apply();
                    File.WriteAllBytes(RunIntegrationSetup.ResultDirectory + "/" + label + "-" + size.x + "x" + size.y + ".png", image.EncodeToPNG());
                }
                finally
                {
                    camera.targetTexture = previousTarget; RenderTexture.active = previousActive;
                    if (image != null) Object.DestroyImmediate(image);
                    target.Release(); Object.DestroyImmediate(target);
                }
            }
        }
        finally
        {
            canvas.renderMode = previousMode; canvas.worldCamera = previousCamera; canvas.planeDistance = previousPlane;
            canvasRect.localPosition = previousPosition; canvasRect.localRotation = previousRotation;
            canvasRect.localScale = previousLocalScale; canvasRect.sizeDelta = previousDelta;
            canvas.scaleFactor = previousScale; scaler.enabled = scalerEnabled;
            camera.targetTexture = previousTarget; camera.cullingMask = previousMask; camera.aspect = previousAspect;
            camera.orthographic = previousOrthographic; camera.orthographicSize = previousSize;
            RenderTexture.active = previousActive; Canvas.ForceUpdateCanvases();
        }
    }
}
