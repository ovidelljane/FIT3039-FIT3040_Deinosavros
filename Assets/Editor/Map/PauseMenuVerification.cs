using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Exercises only a disposable Play Mode run; never saves player or scene state.
public static class PauseMenuVerification
{
    public static IEnumerator Run(Action<bool, string> check)
    {
        yield return Until(() => Object.FindFirstObjectByType<MainMenuController>() != null);
        check(!PauseMenu.IsPaused && Object.FindFirstObjectByType<PauseMenu>() == null, "MainMenu starts without a pause overlay.");
        var menu = Object.FindFirstObjectByType<MainMenuController>();
        typeof(MainMenuController).GetMethod("StartRun", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(menu, null);
        yield return Until(() => TimeTickSystem.Active?.IsStarted == true);
        check(Object.FindFirstObjectByType<PauseMenu>() == null && !PauseMenu.IsPaused, "The first real battle has no pause menu.");
        foreach (var enemy in BattleScript.FindFighters("Enemy")) enemy.TakeDamage(10000);
        yield return Until(() => Object.FindFirstObjectByType<RewardScreenController>()?.IsPresented == true);
        Field<UnityEngine.UI.Button>(Object.FindFirstObjectByType<RewardScreenController>(), "skipButton").onClick.Invoke();
        yield return ReadyMap();
        yield return null;
        var map = Object.FindFirstObjectByType<MapController>();
        var session = map.Session;
        var pause = Object.FindFirstObjectByType<PauseMenu>();
        var nodes = map.Nodes.Select(n => (n.transform.position, n.transform.localScale)).ToArray();
        string node = session.Progress.CurrentNodeId, run = session.Progress.RunId;
        var deck = session.RunDeck.Select(c => c.instanceId).ToArray();
        var card = Object.FindObjectsByType<MapCardView>(FindObjectsSortMode.None).First();
        map.ShowCardDetail(card);
        check(map.SacrificePanel.IsOpen, "A card detail can be open before pausing.");
        Field<UnityEngine.UI.Button>(pause, "openButton").onClick.Invoke();
        yield return null;
        check(pause.IsOpen && PauseMenu.IsPaused && Time.timeScale == 0 && !map.CanInteract, "The authored button pauses and locks map interaction.");
        check(!map.SacrificePanel.IsOpen && Field<GameObject>(pause, "overlay").activeSelf, "Pausing closes transient details and shows the saved overlay.");
        float time = Time.time;
        float face = card.FlipProgress;
        map.SelectNode(map.Nodes.First(n => n.IsAvailable));
        yield return Wait(.25f);
        check(Time.time == time && card.FlipProgress == face && session.Progress.CurrentNodeId == node && session.Progress.Phase == MapProgressPhase.OnMap,
            "Paused time, cards and direct map selection cannot advance the run.");
        CheckRaycasts(pause, check);
        check(EventSystem.current.currentSelectedGameObject == Field<UnityEngine.UI.Button>(pause, "resumeButton").gameObject,
            "Resume receives keyboard and gamepad focus.");
        Capture(pause, "map-paused", check);
        pause.RequestMainMenu();
        check(Field<GameObject>(pause, "confirmation").activeSelf && pause.IsOpen, "Returning to the menu requires an explicit confirmation.");
        pause.Back(); pause.RequestQuit(); pause.Back();
        check(pause.IsOpen && Field<GameObject>(pause, "menu").activeSelf && Time.timeScale == 0, "Canceling either exit remains paused.");
        Field<UnityEngine.UI.Button>(pause, "resumeButton").onClick.Invoke();
        yield return null; yield return null;
        check(!PauseMenu.IsPaused && Time.timeScale == 1 && map.CanInteract, "Resume restores normal time and interaction.");
        check(session.Progress.RunId == run && session.RunDeck.Select(c => c.instanceId).SequenceEqual(deck) &&
              map.Nodes.Select(n => (n.transform.position, n.transform.localScale)).SequenceEqual(nodes), "Pause never changes cards, route, node transforms or scaling.");
        Time.timeScale = .5f; pause.Open(); pause.Open(); pause.Resume();
        check(Time.timeScale == .5f && !PauseMenu.IsPaused, "Repeated open restores the original time scale exactly once.");
        Time.timeScale = 1;
        pause.Open(); pause.enabled = false;
        check(!PauseMenu.IsPaused && Time.timeScale == 1 && !Field<GameObject>(pause, "overlay").activeSelf, "Disabling a paused component releases time and overlay.");
        pause.enabled = true;
        pause.Open(); SceneManager.LoadScene("Map");
        yield return ReadyMap();
        check(!PauseMenu.IsPaused && Time.timeScale == 1 && RunSession.Instance == session, "Reloading a paused scene releases pause and preserves the run.");

        // Use the actual transition and receiver for both non-combat pages.
        map = Object.FindFirstObjectByType<MapController>();
        bool started = map.TryStartTestEncounter("level_02_01", null, out string reason, MapOpportunityOutcome.Card);
        check(started, "Prepare an opportunity start in the disposable run: " + reason);
        pause = Object.FindFirstObjectByType<PauseMenu>();
        pause.Open();
        check(!pause.IsOpen, "Pause cannot interrupt travel or scene activation.");
        yield return Until(() => Object.FindFirstObjectByType<MapOpportunityController>()?.IsSpinning == true &&
                                !session.GetComponent<MapTravelCoordinator>().IsBusy);
        var opportunity = Object.FindFirstObjectByType<MapOpportunityController>();
        pause = Object.FindFirstObjectByType<PauseMenu>();
        pause.Open(); yield return null;
        var icon = Field<UnityEngine.UI.RawImage>(opportunity, "icon");
        var rotation = icon.transform.localRotation; var tile = icon.uvRect;
        yield return Wait(.5f);
        check(pause.IsOpen && opportunity.IsSpinning && icon.transform.localRotation == rotation && icon.uvRect == tile && !opportunity.IsRevealed,
            "Opportunity face rotation and reveal stop while paused.");
        Capture(pause, "opportunity-paused", check);
        pause.Resume(); yield return Until(() => opportunity.IsRevealed);
        check(opportunity.Receipt.Outcome == MapOpportunityOutcome.Card, "Resume keeps the original random result.");
        opportunity.SecondaryButton.onClick.Invoke();
        yield return ReadyMap();

        map = Object.FindFirstObjectByType<MapController>();
        started = map.TryStartTestEncounter("level_02_02", 40, out reason);
        check(started, "Prepare a damaged recovery start in the disposable run: " + reason);
        yield return Until(() => Object.FindFirstObjectByType<MapNonCombatController>()?.IsActivated == true &&
                                !session.GetComponent<MapTravelCoordinator>().IsBusy);
        var recovery = Object.FindFirstObjectByType<MapNonCombatController>();
        pause = Object.FindFirstObjectByType<PauseMenu>();
        pause.Open(); yield return null;
        var health = Field<TMP_Text>(recovery, "value");
        string before = health.text; int actualHealth = session.PlayerHealth;
        yield return Wait(.5f);
        check(pause.IsOpen && health.text == before && session.PlayerHealth == actualHealth, "Recovery presentation freezes without healing a second time.");
        Capture(pause, "recovery-paused", check);
        pause.Resume(); yield return Until(() => recovery.ContinueButton.interactable);
        check(session.PlayerHealth == actualHealth && actualHealth > 40, "Recovery resumes with the original one-time receipt.");
        pause.Open(); pause.RequestMainMenu(); pause.ConfirmLeave();
        yield return Until(() => Object.FindFirstObjectByType<MainMenuController>() != null && RunSession.Instance == null);
        check(Time.timeScale == 1 && !PauseMenu.IsPaused, "Returning from pause cleans up the run and restores time.");
        var mainMenu = Object.FindFirstObjectByType<MainMenuController>();
        typeof(MainMenuController).GetMethod("StartRun", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(mainMenu, null);
        yield return Until(() => TimeTickSystem.Active?.IsStarted == true);
        check(SceneManager.GetActiveScene().name == "Deinosavros" && Object.FindFirstObjectByType<PauseMenu>() == null && !PauseMenu.IsPaused && Time.timeScale == 1,
            "Real Combat begins normally without a pause menu or lingering pause state.");
        File.WriteAllText("Library/PauseMenu/runtime.txt", "PASS: saved buttons, input blocking, exact time restoration, opportunity/recovery pause, non-combat receipts, leave cleanup, Combat exclusion and three-resolution screenshots.\n");
    }

    private static void CheckRaycasts(PauseMenu pause, Action<bool, string> check)
    {
        var pointer = new PointerEventData(EventSystem.current) { position = new Vector2(Screen.width * .15f, Screen.height * .1f) };
        var hits = new System.Collections.Generic.List<RaycastResult>();
        EventSystem.current.RaycastAll(pointer, hits);
        check(hits.Count > 0 && hits[0].gameObject.transform.IsChildOf(pause.transform), "The dim layer intercepts clicks over background cards and the map.");
    }
    private static void Capture(PauseMenu pause, string label, Action<bool, string> check)
    {
        bool asynchronous = ShaderUtil.allowAsyncCompilation;
        ShaderUtil.allowAsyncCompilation = false;
        try
        {
            var canvas = pause.GetComponentInParent<Canvas>();
            if (SceneManager.GetActiveScene().name == "Map")
                MapSacrificeVerification.Capture(canvas, Camera.main, "pause-" + label, check);
            else
                MapOpportunityVerification.CaptureCanvas(canvas, Camera.main, "pause-" + label,
                    Field<GameObject>(pause, "menu").GetComponent<RectTransform>());
            foreach (var text in pause.GetComponentsInChildren<TMP_Text>().Where(t => !string.IsNullOrEmpty(t.text)))
            {
                text.ForceMeshUpdate();
                check(!text.isTextOverflowing && text.textInfo.characterCount > 0, "Pause text is legible and fits: " + label + "/" + text.name);
            }
        }
        finally { ShaderUtil.allowAsyncCompilation = asynchronous; }
    }
    private static T Field<T>(object target, string name) => (T)FindField(target.GetType(), name).GetValue(target);
    private static FieldInfo FindField(Type type, string name) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? (type.BaseType != null ? FindField(type.BaseType, name) : throw new MissingFieldException(name));
    private static IEnumerator Wait(float seconds)
    {
        double until = UnityEditor.EditorApplication.timeSinceStartup + seconds;
        while (UnityEditor.EditorApplication.timeSinceStartup < until) yield return null;
    }
    private static IEnumerator ReadyMap() => Until(() => Object.FindFirstObjectByType<MapController>() is MapController map &&
        map.NodeInteraction != null && map.CanInteract);
    private static IEnumerator Until(Func<bool> condition)
    {
        double until = UnityEditor.EditorApplication.timeSinceStartup + 30;
        while (!condition())
        {
            if (UnityEditor.EditorApplication.timeSinceStartup > until) throw new InvalidOperationException("Pause verification timed out in " + SceneManager.GetActiveScene().name);
            yield return null;
        }
    }
}
