using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Deinosavros.MapReview;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Inert in normal play. Tests use an isolated pointer inside the opt-in player only.
public sealed partial class MapTravelHarness : MonoBehaviour
{
    private readonly List<string> report=new(), errors=new();
    private readonly List<float> frameTimes=new();
    private readonly List<Mouse> suspended=new();
    private readonly Vector3[] corners=new Vector3[4];
    private string output;
    private Mouse mouse;
    private InputSettings originalSettings, testSettings;
    private bool finished, sampling;
    private double watchdog;
    private MapGraphDefinition graph;
    private ProfilerRecorder gc;
    private long allocationBytes;
    private int maxParticles;
    private bool interceptReady;
    private float delayReady;
    private GameObject delayedPlayer;
    private int clipFrame;
    private double clipStart, nextClipFrame;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if(!Environment.GetCommandLineArgs().Contains("-travelValidation")||FindFirstObjectByType<MapTravelHarness>()!=null)return;
        var root=new GameObject("Travel verification");DontDestroyOnLoad(root);root.AddComponent<MapTravelHarness>();
    }
    private void Start()
    {
        output=Argument("-travelOutput",Path.Combine(Application.dataPath,"../TravelEvidence"));Directory.CreateDirectory(output);
        watchdog=Time.realtimeSinceStartupAsDouble+600;
        Application.runInBackground=true;Application.logMessageReceived+=Log;SceneManager.sceneLoaded+=SceneLoaded;
        QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;
        StartCoroutine(Guarded(Run()));
    }
    private void Update()
    {
        if(sampling)
        {
            frameTimes.Add(Time.unscaledDeltaTime*1000);
            if(gc.Valid&&gc.Count>0)allocationBytes+=Math.Max(0,gc.LastValue);
            var view=FindFirstObjectByType<MapTravelView>();if(view!=null)maxParticles=Math.Max(maxParticles,view.LiveParticles);
        }
        if(Time.realtimeSinceStartupAsDouble>watchdog){errors.Add("Travel verification timeout.");Finish(1);}
    }
    private void Log(string message,string stack,LogType type)
    {if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message+"\n"+stack);}
    private void SceneLoaded(Scene scene,LoadSceneMode mode)
    {
        EncounterSceneLoaded(scene);
        if(!interceptReady||scene.name!="Deinosavros")return;
        interceptReady=false;
        var candidate=MapTravelCoordinator.FindReadyPlayer(scene.name);
        delayedPlayer=candidate!=null?candidate.gameObject:null;
        if(delayedPlayer!=null)delayedPlayer.SetActive(false);
        if(delayReady>0)StartCoroutine(ReleasePlayer(delayReady));
    }
    private IEnumerator ReleasePlayer(float seconds)
    {yield return new WaitForSecondsRealtime(seconds);if(delayedPlayer!=null)delayedPlayer.SetActive(true);}
    private IEnumerator Guarded(IEnumerator task)
    {
        var stack=new Stack<IEnumerator>();stack.Push(task);
        while(stack.Count>0)
        {
            object item;
            try
            {
                if(!stack.Peek().MoveNext()){(stack.Pop() as IDisposable)?.Dispose();continue;}
                item=stack.Peek().Current;
            }
            catch(Exception ex){errors.Add(ex.ToString());Finish(1);yield break;}
            if(item is IEnumerator nested)stack.Push(nested);else yield return item;
        }
        Finish(errors.Count==0?0:1);
    }
    private IEnumerator Run()
    {
        SetupPointer();Move(new Vector2(2,2));Screen.SetResolution(1280,720,FullScreenMode.Windowed);
        yield return new WaitForSecondsRealtime(2);Screen.SetResolution(1920,1080,FullScreenMode.Windowed);
        yield return new WaitForSecondsRealtime(4);
        var ui=FindFirstObjectByType<MapController>();Check(ui!=null&&ui.TravelView!=null,"Formal Map uses its production controller and Travel view");
        graph=Read<MapGraphDefinition>(ui,"routeGraph");
        report.Add("GPU: "+SystemInfo.graphicsDeviceName+"; CPU: "+SystemInfo.processorType);
        report.Add("Development standalone player; uncapped; process-only synthetic input, no OS pointer operations.");
        Check(ui.Nodes.Length==14&&ui.TravelView.paths.Length==19,"Formal Map contains fourteen nodes and nineteen authored roads");
        Check(FindFirstObjectByType<MapReviewController>()==null,"Review UI does not replace the formal card layout");
        Check(ui.Session.Progress.CompletedCount==0&&ui.Nodes.Count(n=>n.IsAvailable)==1,"Only the start node is initially available");
        if(Environment.GetCommandLineArgs().Contains("-encounterValidation"))
        { yield return EncounterTypeChecks(ui); yield break; }
        if(Environment.GetCommandLineArgs().Contains("-battleUIValidation"))
        { yield return BattleUiChecks(ui); yield break; }
        foreach(var size in new[]{new Vector2Int(1920,1080),new Vector2Int(2560,1440),new Vector2Int(1920,1200)})
        {
            Screen.SetResolution(size.x,size.y,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(1.5f);
            Move(new Vector2(2,2));yield return null;ValidateFrame(ui,size);
            yield return Capture($"01-{size.x}x{size.y}-idle");
            Move(ui.TravelView.IconScreenRect("level_01_01").center);yield return new WaitForSecondsRealtime(.3f);
            Check(ui.NodeInteraction.DetailVisible,"Reference-scaled icons remain hoverable at "+size);
            var detail=ui.NodeInteraction.DetailPanel;detail.GetWorldCorners(corners);
            Check(corners[0].x>=0&&corners[2].x<=Screen.width&&corners[0].y>Screen.height*.26f&&corners[2].y<Screen.height-45*Screen.height/1080f,
                "Level information stays above the cards and below the top bar at "+size);
            yield return Capture($"01-{size.x}x{size.y}-hover",false);
        }
        Screen.SetResolution(1920,1080,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(3);
        if(Environment.GetCommandLineArgs().Contains("-travelCaptureOnly"))
        {
            AdvanceFixtureTo("level_01_01",ui);yield return null;
            Move(ui.TravelView.IconScreenRect("level_02_03").center);yield return new WaitForSecondsRealtime(.3f);
            yield return Capture("02-route-preview", false);yield return VisualRoutes(ui,true);yield break;
        }
        yield return InputChecks(ui);
        yield return VisualRoutes(ui,false);
        ui.Session.BeginNewRun();ui.TravelView.RestorePosition(ui.Session.Progress.CurrentNodeId);yield return null;
        string id=ui.Session.Progress.RunId;
        yield return Click(ui.TravelView.IconScreenRect("level_01_01").center);
        var firstTicket=ui.Session.Progress.CurrentEncounter;
        ui.SelectNode(ui.Nodes.Single(n=>n.NodeId=="level_01_01"));
        Check(ui.Session.Progress.Phase==MapProgressPhase.Traveling&&!ui.CanInteract&&ui.Session.Progress.CurrentEncounter==firstTicket,"A single icon click starts exactly one departure and freezes map/card interaction");
        yield return Wait(()=>SceneManager.GetActiveScene().name=="Deinosavros"&&ui.Session.Progress.Phase==MapProgressPhase.InEncounter,15,"Initial ignition reaches the ready battle receiver");
        var session=RunSession.Instance;
        Check(session.Progress.CurrentNodeId=="level_01_01"&&session.Progress.RunId==id,"Initial acknowledgement retains the adventure and start location");
        Check(FindObjectsByType<MapController>(FindObjectsSortMode.None).Length==0,"The old map controller is released only after battle activation");
        yield return VictoryAndReturn("level_01_01",3);
        ui=FindFirstObjectByType<MapController>();
        Move(ui.TravelView.IconScreenRect("level_02_03").center);yield return new WaitForSecondsRealtime(.3f);
        yield return Capture("04-live-route-preview",false);
        yield return Click(ui.TravelView.IconScreenRect("level_02_03").center);
        yield return Wait(()=>SceneManager.GetActiveScene().name=="Deinosavros"&&session.Progress.Phase==MapProgressPhase.InEncounter,15,"Ordinary route movement enters the real battle");
        Check(session.Progress.CurrentNodeId=="level_02_03"&&session.CurrentEncounterNodeId=="level_02_03"&&!session.CurrentEncounterIsBoss,"Node identity reaches the battle session without guessing distance");
        yield return VictoryAndReturn("level_02_03",2);
        ui=FindFirstObjectByType<MapController>();yield return FailureChecks(ui);
        ui=FindFirstObjectByType<MapController>();ui.Session.BeginNewRun();AdvanceFixtureTo("level_05_03",ui);yield return null;
        Move(ui.TravelView.IconScreenRect("level_06_01").center);yield return new WaitForSecondsRealtime(.3f);
        yield return Capture("07-boss-preview",false);
        yield return Click(ui.TravelView.IconScreenRect("level_06_01").center);
        yield return Wait(()=>SceneManager.GetActiveScene().name=="Deinosavros"&&session.Progress.Phase==MapProgressPhase.InEncounter,15,"Boss rises through the portal and enters the receiver");
        Check(session.CurrentEncounterIsBoss&&session.CurrentEncounterNodeId=="level_06_01","The existing battle receives the Boss flag and node ID");
        yield return VictoryAndReturn("level_06_01",0);
        ui=FindFirstObjectByType<MapController>();Check(ui.EnterButton.interactable&&session.Progress.Phase==MapProgressPhase.Won,"Boss victory shows the new adventure action");
        yield return Capture("08-boss-completed");ui.EnterButton.onClick.Invoke();yield return null;
        Check(session.Progress.Phase==MapProgressPhase.OnMap&&session.Progress.CompletedCount==0&&!session.SacrificeUsed,"Start New Run resets route, offering budget and deck");
        yield return Click(Camera.main.WorldToScreenPoint(ui.Nodes.Single(n=>n.NodeId=="level_01_01").InteractionCollider.bounds.center));
        yield return Wait(()=>session.Progress.Phase==MapProgressPhase.InEncounter,15,"Defeat test encounter begins");
        MapTravelCoordinator.FindReadyPlayer("Deinosavros").health=0;
        yield return Wait(()=>SceneManager.GetActiveScene().name=="Map"&&session.Progress.Phase==MapProgressPhase.Lost,12,"Player defeat ends the run and returns to Map exactly once");
        yield return new WaitForSecondsRealtime(.5f);ui=FindFirstObjectByType<MapController>();Check(ui.EnterButton.interactable,"Defeat exposes Start New Run without changing card layout");
        yield return Capture("09-defeated");ui.EnterButton.onClick.Invoke();yield return null;
        Check(string.IsNullOrEmpty(session.GetComponent<MapTravelCoordinator>().LastNotice),"New adventure clears the previous result notice across map reloads");
        yield return Benchmark(ui);
        yield return Capture("10-final-map");
    }
    private void ValidateFrame(MapController ui,Vector2Int size)
    {
        Check(Screen.width==size.x&&Screen.height==size.y,$"Resolution {size.x}x{size.y} is active");
        var camera=Camera.main;var core=ui.TravelView.transform.Find("Core");
        float width=Vector2.Distance(camera.WorldToScreenPoint(core.position-core.right*core.localScale.x*.5f),camera.WorldToScreenPoint(core.position+core.right*core.localScale.x*.5f));
        float height=Vector2.Distance(camera.WorldToScreenPoint(core.position-core.up*core.localScale.y*.5f),camera.WorldToScreenPoint(core.position+core.up*core.localScale.y*.5f));
        float reference=Screen.height/1080f;
        Check(Mathf.Abs(width-ui.TravelView.profile.corePixels*reference)<1.5f&&Mathf.Abs(height-ui.TravelView.profile.coreHeightPixels*reference)<1.5f,"Fire pawn scales with UI reference height and preserves its tall silhouette");
        Check(width/reference>=52&&height/reference>=70,"Player silhouette is substantially larger than the previous twenty-pixel token");
        var info=ui.TravelView.profile.nodeInformation;
        Check(info!=null&&ui.Nodes.All(n=>info.Find(n.NodeId)!=null),"All fourteen nodes have explicit editable encounter information");
        Check(!ui.EnterButton.gameObject.activeSelf,"The normal map has no second departure confirmation button");
        var framing=camera.GetComponent<MapCameraFraming>();
        foreach(var site in ui.TravelView.sites)
        {
            Vector3 p=camera.WorldToViewportPoint(ui.TravelView.environment.TransformPoint(site.point));
            Check(p.z>0&&p.x>0&&p.x<1&&p.y>framing.SafeArea.yMin&&p.y<framing.SafeArea.yMax,"Every node remains inside the existing UI-safe composition: "+site.nodeId);
        }
        Check(!framing.subjects.Any(r=>r!=null&&r.transform.IsChildOf(ui.TravelView.transform)),"Travel effects do not change camera subject framing");
    }
    private IEnumerator BattleUiChecks(MapController ui)
    {
        var session=ui.Session;
        Set(session,"playerHealth",73);Set(session,"playerMaxHealth",112);Set(session,"playerDamage",13);
        Set(session,"playerAttackSpeed",2.75f);Set(session,"playerShield",7);Set(session,"playerElixir",11.5f);Set(session,"playerMaxElixir",15f);
        yield return Click(ui.TravelView.IconScreenRect("level_01_01").center);
        yield return Wait(()=>session.Progress.Phase==MapProgressPhase.InEncounter&&!session.GetComponent<MapTravelCoordinator>().IsBusy,15,"Battle waits for both the active player and its complete HUD");
        yield return new WaitForSecondsRealtime(.3f);
        var player=MapTravelCoordinator.FindReadyPlayer("Deinosavros");var hud=BattleHud.Find(player.gameObject.scene);
        Check(player.health==73&&player.maxHealth==112&&player.attackDmg==13&&Mathf.Approximately(player.attackSpd,2.75f)&&player.shield==7&&Mathf.Approximately(player.elixir,11.5f)&&Mathf.Approximately(player.maxElixir,15),"HP, damage, fractional attack interval, shield and Elixir are applied before play");
        yield return Capture("battle-initial-frame");
        ValidateBattleHud(hud,session);
        var stats=player.GetComponent<TempStatDisplay>();
        Check(stats.Label.text.Contains("73/112")&&stats.Label.text.Contains("2.75")&&stats.Label.text.Contains("11.5/15"),"The visible statistics show transferred data rather than battle prefab defaults");
        foreach(var size in new[]{new Vector2Int(1920,1080),new Vector2Int(2560,1440),new Vector2Int(1920,1200)})
        {
            Screen.SetResolution(size.x,size.y,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(1);
            ValidateBattleHud(hud,session);yield return Capture($"battle-{size.x}x{size.y}-ready");
        }
        Screen.SetResolution(1920,1080,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(1);
        var shield=hud.Deck.Hand.Single(c=>c.Definition.cardId=="solar_shield");
        int oldShield=player.shield;float oldElixir=player.elixir;float shieldAmount=Read<float>(shield,"amount");int shieldCost=Read<int>(shield,"elixirCost");
        yield return Click(Center((RectTransform)shield.transform));yield return null;
        Check(player.shield==oldShield+(int)shieldAmount&&Mathf.Approximately(player.elixir,oldElixir-shieldCost)&&hud.Deck.Hand.Count==3,"A real hand click applies shield, pays its existing cost and removes only the played view");
        yield return Capture("battle-shield-played");
        var fleet=hud.Deck.Hand.Single(c=>c.Definition.cardId=="fleet_footwork");float speed=player.attackSpd,amount=Read<float>(fleet,"amount");
        yield return Click(Center((RectTransform)fleet.transform));
        var effect=FindObjectsByType<Effect>(FindObjectsSortMode.None).Single(e=>e.target==player&&e.effectType==StatType.AttackSpeed);
        Check(effect.target==player,"Timed front-card effects target the active player, never the disabled shadow");
        var startButton=FindObjectsByType<Button>(FindObjectsSortMode.None).Single(b=>b.name=="Start");
        yield return Click(Center((RectTransform)startButton.transform));
        yield return new WaitForSecondsRealtime(.4f);
        Check(Mathf.Approximately(player.attackSpd,speed-amount),"The existing timed attack-speed effect still uses the unchanged combat formula");
        yield return Wait(()=>effect==null,5,"The existing timed effect expires normally");yield return null;
        Check(Mathf.Approximately(player.attackSpd,speed),"Attack interval returns to its fractional value after the timed effect");
        Check(hud.Deck.Hand.Count==4&&session.GetActiveDeck().Count==4,"Played cards redraw on the existing tick delay without deleting the adventure deck");
        yield return Capture("battle-cards-redrawn");
        yield return VictoryAndReturn("level_01_01",3);
        ui=FindFirstObjectByType<MapController>();
        Check(Mathf.Approximately(session.PlayerAttackSpeed,2.75f),"Map return preserves the actual fractional attack interval instead of writing one");
        var status=FindFirstObjectByType<MapPlayerStatusPanel>();
        Check(Read<TMPro.TMP_Text>(status,"attackSpeedValue").text=="2.75s","The unchanged Map status panel displays fractional seconds correctly");
        int health=session.PlayerHealth,maxHealth=session.PlayerMaxHealth,damage=session.PlayerDamage,storedShield=session.PlayerShield;
        float interval=session.PlayerAttackSpeed,elixir=session.PlayerElixir,maxElixir=session.PlayerMaxElixir;
        var offering=session.GetActiveDeck().Single(c=>c.cardId=="tidal_wave");
        Check(session.TrySacrifice(offering),"One explicit offering is staged before the next battle");
        yield return Click(ui.TravelView.IconScreenRect("level_02_03").center);
        yield return Wait(()=>session.Progress.Phase==MapProgressPhase.InEncounter&&!session.GetComponent<MapTravelCoordinator>().IsBusy,15,"The next level initializes a fresh hand and state HUD");
        yield return new WaitForSecondsRealtime(.3f);
        player=MapTravelCoordinator.FindReadyPlayer("Deinosavros");hud=BattleHud.Find(player.gameObject.scene);ValidateBattleHud(hud,session);
        Check(player.health==health&&player.maxHealth==maxHealth&&player.attackDmg==damage&&player.shield==storedShield&&Mathf.Approximately(player.attackSpd,interval)&&Mathf.Approximately(player.elixir,elixir)&&Mathf.Approximately(player.maxElixir,maxElixir),"The next encounter inherits all returned player values exactly");
        Check(hud.Deck.Hand.Count==3&&hud.Deck.Hand.All(c=>c.Definition!=offering),"Only the sacrificed definition is absent from the next battle hand");
        foreach(var enemy in FindObjectsByType<BattleScript>(FindObjectsSortMode.None).Where(a=>a.isActiveAndEnabled&&a.CompareTag("Enemy")))
            Check(enemy.health==Mathf.Max(1,Mathf.CeilToInt(enemy.maxHealth*offering.overworldEffect.magnitude/100f)),"The existing enemy-health offering is applied after readiness: "+enemy.name);
        string encounter=session.Progress.CurrentEncounter.EncounterId;
        Check(!session.GetComponent<BattleRunBridge>().InitializeConfirmedEncounter(encounter,player),"Duplicate readiness cannot reset HP or apply the offering twice");
        yield return Capture("battle-next-level-offering");
        yield return VictoryAndReturn("level_02_03",2);
        ui=FindFirstObjectByType<MapController>();yield return FailureChecks(ui);
        yield return Capture("battle-ui-final-return");
    }
    private void ValidateBattleHud(BattleHud hud,RunSession session)
    {
        Check(hud!=null&&hud.Canvas.isActiveAndEnabled&&hud.gameObject.scene.name=="Deinosavros","The HUD is owned by the battle scene and stays active after transition");
        Check(hud.Deck.IsReady&&hud.Deck.Hand.Count==Mathf.Min(5,session.GetActiveDeck().Count),"The hand contains the expected active run cards");
        Check(hud.Deck.TryInitialize(session,hud.Deck.Player)&&hud.Deck.Hand.Count==Mathf.Min(5,session.GetActiveDeck().Count),"Repeated hand initialization cannot duplicate cards");
        foreach(var card in hud.Deck.Hand)
        {
            Check(card!=null&&card.gameObject.activeInHierarchy&&card.Target==hud.Deck.Player&&card.GetComponentInParent<Canvas>()==hud.Canvas,"Each live card is visible in the combat canvas and bound to the real player");
            Check(session.ContainsCard(card.Definition.cardId),"Each live card belongs to the unsacrificed deck");
            var center=Center((RectTransform)card.transform);
            Check(center.x>0&&center.x<Screen.width&&center.y>0&&center.y<Screen.height,"A card click target is inside the game view");
        }
        var labels=FindObjectsByType<HealthLabel>(FindObjectsSortMode.None).Where(l=>l.isActiveAndEnabled).ToArray();
        Check(labels.Length==4,"Player and all three active enemies have health displays");
        foreach(var label in labels)
        {
            Check(label.IsReady&&label.Label.canvas==hud.Canvas&&!string.IsNullOrEmpty(label.Label.text),"A health label is on the battle canvas, not the fading curtain: "+label.name);
            ValidateTextVisible(label.Label);
        }
        var stats=FindObjectsByType<TempStatDisplay>(FindObjectsSortMode.None).Single(s=>s.isActiveAndEnabled);
        Check(stats.IsReady&&stats.Label.canvas==hud.Canvas,"The player attribute block has an explicit battle canvas");ValidateTextVisible(stats.Label);
        var transition=session.transform.Find("Transition");
        Check(transition!=null&&!transition.gameObject.activeSelf&&transition.GetComponentsInChildren<TMPro.TMP_Text>(true).Length==0,"The hidden transition layer owns no combat text");
        Check(FindObjectsByType<DeckManager>(FindObjectsSortMode.None).Length==1,"There is exactly one battle deck manager");
    }
    private void ValidateTextVisible(TMPro.TMP_Text text)
    {
        text.ForceMeshUpdate();bool any=false;
        foreach(var character in text.textInfo.characterInfo.Take(text.textInfo.characterCount).Where(c=>c.isVisible))
        {
            var center=RectTransformUtility.WorldToScreenPoint(null,text.transform.TransformPoint((character.bottomLeft+character.topRight)*.5f));
            if(center.x<0||center.x>Screen.width||center.y<0||center.y>Screen.height)
                throw new InvalidOperationException("Status glyph outside the frame: "+text.text+" at "+center);
            any=true;
        }
        Check(any,"The status label contains rendered glyphs");
    }
    private static void Set(object target,string field,object value)=>target.GetType().GetField(field,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(target,value);
    private IEnumerator InputChecks(MapController ui)
    {
        var start=ui.Nodes.Single(n=>n.NodeId=="level_01_01");
        Vector2 point=ui.TravelView.IconScreenRect(start.NodeId).center;
        Move(point);yield return new WaitForSecondsRealtime(.07f);
        Check(ui.NodeInteraction.HoveredNodeId==start.NodeId&&!ui.NodeInteraction.DetailVisible,"Hover identifies the level before the detail delay");
        yield return new WaitForSecondsRealtime(.22f);
        Check(ui.NodeInteraction.DetailVisible&&ui.TravelView.PreviewNodeId==start.NodeId,"Hover alone shows encounter details and highlights the matching route");
        Check(ui.Session.Progress.Phase==MapProgressPhase.OnMap&&!ui.TravelView.IsMoving&&ui.SelectedNodeId==null,"Hover never commits a destination or moves the player");
        for(int i=0;i<15;i++){yield return new WaitForSecondsRealtime(.025f);Check(ui.NodeInteraction.DetailVisible,"Stationary hover stays stable");}
        yield return Capture("02-start-tooltip",false);
        Move(Center(ui.NodeInteraction.DetailPanel));yield return new WaitForSecondsRealtime(.3f);
        Check(ui.NodeInteraction.DetailVisible&&ui.NodeInteraction.HoveredNodeId==start.NodeId,"Moving into the information panel retains the same preview");
        yield return Click(Center(ui.NodeInteraction.DetailPanel));
        Check(ui.Session.Progress.Phase==MapProgressPhase.OnMap,"Clicking information does not fall through to the map");
        Move(point,1);yield return null;yield return null;Move(new Vector2(2,2));yield return null;yield return null;
        Check(ui.Session.Progress.Phase==MapProgressPhase.OnMap,"Dragging off a level cancels the click before departure");
        var card=FindObjectsByType<MapCardView>(FindObjectsSortMode.None).First(c=>c.Definition!=null);
        Vector2 cardPoint=Center((RectTransform)card.transform);
        Check(ui.BlocksMapPointer(cardPoint),"The overlapping card UI blocks map selection");
        yield return Click(cardPoint);Check(ui.Session.Progress.Phase==MapProgressPhase.OnMap,"A UI click does not start map travel");
        ui.RequestSacrifice(card);Check(!ui.CanInteract,"Offering confirmation freezes background interaction");
        ui.SelectNode(start);Check(ui.Session.Progress.Phase==MapProgressPhase.OnMap,"Background departure is rejected while offering confirmation is open");
        Read<Button>(ui,"cancelSacrificeButton").onClick.Invoke();Move(new Vector2(2,2));yield return null;
        Check(ui.CanInteract&&!ui.Session.SacrificeUsed,"Cancel restores interaction without spending an offering");
        AdvanceFixtureTo(start.NodeId,ui);yield return null;yield return null;
        string[] choices={"level_02_01","level_02_03","level_02_02"};
        Vector3 origin=ui.TravelView.FloorPosition;
        foreach(string choice in choices)
        {
            Move(ui.TravelView.IconScreenRect(choice).center);yield return new WaitForSecondsRealtime(.25f);
            Check(ui.NodeInteraction.HoveredNodeId==choice&&ui.NodeInteraction.DetailVisible&&ui.TravelView.PreviewNodeId==choice,"Sweeping across available icons changes only that level's preview: "+choice);
            Check(Vector3.Distance(origin,ui.TravelView.FloorPosition)<.001f,"Changing previews leaves the player on its current platform");
        }
        yield return Capture("03-choice-tooltip",false);
        Move(ui.TravelView.IconScreenRect("level_04_01").center);yield return new WaitForSecondsRealtime(.3f);
        Check(!ui.NodeInteraction.DetailVisible&&ui.Session.Progress.Phase==MapProgressPhase.OnMap,"Locked future levels do not preview or depart");
        yield return Click(ui.TravelView.IconScreenRect("level_04_01").center);
        Check(ui.Session.Progress.Phase==MapProgressPhase.OnMap,"Locked icon clicks are ignored");
        Move(new Vector2(2,2));ui.Session.BeginNewRun();ui.TravelView.RestorePosition(start.NodeId);yield return null;
    }
    private IEnumerator VisualRoutes(MapController ui,bool captureOnly)
    {
        foreach(var path in ui.TravelView.paths)
        {
            AdvanceFixtureTo(path.fromNodeId,ui);yield return null;
            ui.TravelView.RestorePosition(path.fromNodeId);ui.TravelView.Preview(path.toNodeId);yield return new WaitForSecondsRealtime(.28f);
            if(path.name=="R01"||path.name=="R18")yield return Capture(path.name+"-before");
            Check(ui.Session.TryBeginTravel(path.toNodeId,out var ticket),"An authored edge starts: "+path.name);
            var animation=ui.TravelView.Animate(ticket);float closest=1000;bool middle=false,arrived=false,absorbing=false;
            bool clip=path.name=="R01";
            if(clip){clipFrame=0;clipStart=Time.realtimeSinceStartupAsDouble;nextClipFrame=clipStart;Directory.CreateDirectory(output+"/Clip");}
            while(animation.MoveNext())
            {
                yield return animation.Current;
                closest=Mathf.Min(closest,Vector3.Distance(ui.TravelView.FloorPosition,path.Sample(1)));
                if(clip&&Time.realtimeSinceStartupAsDouble>=nextClipFrame)
                {yield return CaptureClipFrame();nextClipFrame=Time.realtimeSinceStartupAsDouble+.005;}
                float distance=Vector3.Distance(ui.TravelView.FloorPosition,path.Sample(.5f));
                if(!middle&&distance<.25f)
                {middle=true;if(path.name=="R01"||path.name=="R18")yield return Capture(path.name+"-moving");}
                if(!arrived&&closest<.02f)
                {arrived=true;if(path.name=="R01"||path.toNodeId=="level_06_01")yield return Capture(path.name+"-arrival");}
                if(ticket.IsBoss&&!absorbing&&arrived&&Vector3.Distance(ui.TravelView.CorePosition,ui.TravelView.FloorPosition)>1.15f)
                {absorbing=true;if(path.name=="R18")yield return Capture("R18-portal-absorb");}
                CheckParticleCap(ui.TravelView);
            }
            if(clip)File.WriteAllText(output+"/Clip/timing.txt",$"Frames {clipFrame}; duration {Time.realtimeSinceStartupAsDouble-clipStart:F6}; approximate fps {clipFrame/(Time.realtimeSinceStartupAsDouble-clipStart):F6}.");
            Check(closest<.02f,"Route reaches its saved floor landing: "+path.name);
            if(ticket.IsBoss)Check(Vector3.Distance(ui.TravelView.CorePosition,ui.TravelView.environment.TransformPoint(ui.TravelView.portalFocus))<.02f,"Boss route ends at the portal center: "+path.name);
            Check(ui.Session.CancelPreparedTravel(ticket.EncounterId),"Visual sample cancels without committing a gameplay location");
            ui.TravelView.RestorePosition(path.fromNodeId);
            if(captureOnly&&path.name=="R18")break;
        }
        report.Add("Visual traversal covered the saved road samples, including all three Boss stair routes.");Flush();
    }
    private void CheckParticleCap(MapTravelView view)
    {if(view.LiveParticles>96)throw new InvalidOperationException("Particle cap exceeded.");}
    private void AdvanceFixtureTo(string id,MapController ui)
    {
        var session=ui.Session;session.BeginNewRun();
        var route=new List<string>();
        bool Find(string current)
        {
            route.Add(current);if(current==id)return true;
            foreach(var next in graph.Find(current).next)if(Find(next))return true;
            route.RemoveAt(route.Count-1);return false;
        }
        Check(Find("level_01_01"),"Fixture target is reachable: "+id);
        foreach(string node in route)
        {
            Check(session.TryBeginTravel(node,out var ticket)&&session.Progress.MarkLoading(ticket.EncounterId)&&session.ConfirmEncounterStarted(ticket.EncounterId)&&
                FinishFixture(session,ticket,ui.EncounterRouting),"Fixture advances through explicit edges: "+node);
        }
        ui.TravelView.RestorePosition(id);
    }
    private IEnumerator VictoryAndReturn(string node,int available)
    {
        var session=RunSession.Instance;string encounter=session.Progress.CurrentEncounter.EncounterId;
        var opponents=GameObject.FindGameObjectsWithTag("Enemy");Check(opponents.Length>0,"The real encounter contains enemies");
        foreach(var enemy in opponents)enemy.GetComponent<BattleScript>().health=0;
        FindFirstObjectByType<TimeTickSystem>().StartTimer();
        yield return Wait(()=>session.Progress.CurrentEncounter==null,4,"The existing victory event completes exactly one encounter");
        Check(!session.CompleteEncounter(encounter,true),"Repeated victory return cannot advance twice");
        var reward=FindFirstObjectByType<RewardScreenController>();
        if(reward!=null)Read<Button>(reward,"skipButton").onClick.Invoke();
        else report.Add("No reward receiver is attached in the current battle scene; the map-side result bridge owns the return without creating rewards.");
        yield return Wait(()=>SceneManager.GetActiveScene().name=="Map"&&FindFirstObjectByType<MapController>()!=null,12,"The victory return loads Map without bypassing an existing reward receiver");
        yield return new WaitForSecondsRealtime(.8f);var ui=FindFirstObjectByType<MapController>();
        Check(ui.Session==session&&session.Progress.CurrentNodeId==node,"Victory return restores the exact completed node");
        Check(ui.Nodes.Count(n=>n.IsAvailable)==available,"Only the completed node's explicit next layer opens");
        Check(Vector3.Distance(ui.TravelView.FloorPosition,ui.TravelView.SitePoint(node))<.001f,"The fire seed reappears at the completed platform");
        Check(FindObjectsByType<RunSession>(FindObjectsSortMode.None).Length==1&&FindObjectsByType<MapTravelCoordinator>(FindObjectsSortMode.None).Length==1,"Scene changes retain one session and one travel coordinator");
        if(node=="level_02_03")yield return Capture("05-victory-return");
    }
    private IEnumerator FailureChecks(MapController ui)
    {
        var session=ui.Session;var card=session.GetActiveDeck().First();
        Check(session.TrySacrifice(card),"An offering can be staged before a failed departure");
        var expected=Read<PendingEncounterModifier>(session,"pendingModifier");int activeCards=session.GetActiveDeck().Count;string origin=session.Progress.CurrentNodeId;
        var destination=ui.Nodes.Single(n=>n.NodeId=="level_03_02");
        var coordinator=MapTravelCoordinator.Ensure(session);
        string originalScene=Read<string>(ui,"battleSceneName");
        string routedScene=ui.EncounterRouting != null ? ui.EncounterRouting.battleScene : null;
        if(ui.EncounterRouting!=null)ui.EncounterRouting.battleScene="MissingTravelScene";
        typeof(MapController).GetField("battleSceneName",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(ui,"MissingTravelScene");
        ui.SelectNode(destination);
        Check(session.Progress.Phase==MapProgressPhase.OnMap&&!coordinator.IsBusy,"Missing encounter scene fails before departure");
        typeof(MapController).GetField("battleSceneName",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(ui,originalScene);
        if(ui.EncounterRouting!=null)ui.EncounterRouting.battleScene=routedScene;
        Check(session.Progress.CurrentNodeId==origin&&Read<PendingEncounterModifier>(session,"pendingModifier").isValid&&session.GetActiveDeck().Count==activeCards,"Missing-scene failure preserves origin and the offering");
        interceptReady=true;delayReady=0;ui.SelectNode(destination);
        yield return Wait(()=>SceneManager.GetActiveScene().name=="Deinosavros",12,"Timeout test reaches the hidden receiver");
        yield return Wait(()=>SceneManager.GetActiveScene().name=="Map"&&session.Progress.Phase==MapProgressPhase.OnMap,12,"Five-second unready receiver recovers to Map");
        yield return new WaitForSecondsRealtime(.7f);ui=FindFirstObjectByType<MapController>();
        Check(session.Progress.CurrentNodeId==origin&&session.SacrificeUsed&&Read<PendingEncounterModifier>(session,"pendingModifier").isValid&&session.GetActiveDeck().Count==activeCards,"Readiness failure neither commits movement nor consumes the staged offering");
        Check(ui.CanInteract,"Failed-entry return restores card and map interaction");yield return Capture("06-ready-timeout-recovered");
        interceptReady=true;delayReady=1.2f;ui.SelectNode(ui.Nodes.Single(n=>n.NodeId=="level_03_02"));
        yield return Wait(()=>SceneManager.GetActiveScene().name=="Deinosavros",12,"Delayed receiver scene activates");
        yield return new WaitForSecondsRealtime(.4f);
        Check(session.Progress.Phase==MapProgressPhase.Loading&&session.Progress.CurrentNodeId==origin&&Read<PendingEncounterModifier>(session,"pendingModifier").isValid,"sceneLoaded does not commit or spend effects before receiver initialization");
        yield return Wait(()=>session.Progress.Phase==MapProgressPhase.InEncounter,5,"Delayed valid receiver acknowledges after initialization");
        Check(!Read<PendingEncounterModifier>(session,"pendingModifier").isValid,"The staged offering is consumed once after readiness");
        var player=MapTravelCoordinator.FindReadyPlayer("Deinosavros");var encounter=session.Progress.CurrentEncounter.EncounterId;
        Check(!session.GetComponent<BattleRunBridge>().InitializeConfirmedEncounter(encounter,player),"Duplicate receiver initialization cannot repeat an offering effect");
        report.Add("Existing offering formula retained: "+expected.effectType+", magnitude "+expected.magnitude+".");
        yield return VictoryAndReturn("level_03_02",2);
        Check(!session.SacrificeUsed,"A normal victory restores one offering allowance");
    }
    private IEnumerator Benchmark(MapController ui)
    {
        Screen.SetResolution(1920,1080,FullScreenMode.Windowed);Move(new Vector2(2,2));yield return new WaitForSecondsRealtime(3);
        AdvanceFixtureTo("level_01_01",ui);yield return null;
        var path=ui.TravelView.FindPath("level_01_01","level_02_03");
        gc=ProfilerRecorder.StartNew(ProfilerCategory.Memory,"GC Allocated In Frame",1);
        frameTimes.Clear();allocationBytes=0;maxParticles=0;sampling=true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        var measuredView=ui.TravelView;
        long updateBytes=measuredView.MeasuredUpdateBytes;
        int updateCount=measuredView.MeasuredUpdateCount;
        measuredView.MeasureUpdateAllocations=true;
#endif
        float seconds=float.Parse(Argument("-travelBenchmarkSeconds","30"),CultureInfo.InvariantCulture);double start=Time.realtimeSinceStartupAsDouble;
        while(Time.realtimeSinceStartupAsDouble-start<seconds)
        {
            ui.TravelView.RestorePosition(path.fromNodeId);ui.TravelView.Preview(path.toNodeId);
            if(!ui.Session.TryBeginTravel(path.toNodeId,out var ticket))throw new InvalidOperationException("Benchmark departure failed.");
            yield return ui.TravelView.Animate(ticket);ui.Session.CancelPreparedTravel(ticket.EncounterId);yield return null;
        }
        sampling=false;gc.Dispose();Check(frameTimes.Count>0,"Movement benchmark recorded rendered standalone frames");
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        measuredView.MeasureUpdateAllocations=false;
        long viewBytes=measuredView.MeasuredUpdateBytes-updateBytes;
        int viewUpdates=measuredView.MeasuredUpdateCount-updateCount;
        report.Add($"Isolated Travel LateUpdate: {viewUpdates} warmed calls, {viewBytes} managed bytes; excludes setup and per-departure coroutine creation.");
        Check(viewUpdates>100&&viewBytes==0,"Pooled Travel per-frame updates do not allocate managed memory after warm-up");
#endif
        float[] sorted=frameTimes.OrderBy(t=>t).ToArray();float P(float p)=>sorted[Mathf.Clamp(Mathf.CeilToInt(sorted.Length*p)-1,0,sorted.Length-1)];
        report.Add($"Movement benchmark: {Time.realtimeSinceStartupAsDouble-start:F2} seconds, {sorted.Length} frames; mean {frameTimes.Average():F3} ms ({1000/frameTimes.Average():F1} FPS), median {P(.5f):F3}, p95 {P(.95f):F3}, p99 {P(.99f):F3}, max {sorted[^1]:F3}; above16.67ms {sorted.Count(f=>f>16.667f)}.");
        report.Add($"Observed process GC allocation {allocationBytes} bytes; peak live motes {maxParticles}. This includes existing scene/UI behavior, not just Travel.");
        File.WriteAllLines(output+"/movement-frame-times.csv",frameTimes.Select(t=>t.ToString("R",CultureInfo.InvariantCulture)));
        Check(maxParticles<=96,"Movement respects the peak mote budget");
        ui.Session.BeginNewRun();ui.TravelView.RestorePosition(ui.Session.Progress.CurrentNodeId);yield return new WaitForSecondsRealtime(.6f);
    }
    private IEnumerator Wait(Func<bool> condition,float timeout,string label)
    {
        double end=Time.realtimeSinceStartupAsDouble+timeout;
        while(!condition()&&Time.realtimeSinceStartupAsDouble<end)yield return null;
        Check(condition(),label);
    }
    private IEnumerator Capture(string name,bool parkPointer=true)
    {
        if(parkPointer)Move(new Vector2(2,2));yield return new WaitForEndOfFrame();
        var image=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(output+"/"+name+".png",image.EncodeToPNG());Destroy(image);
        report.Add($"Captured: {name} ({Screen.width}x{Screen.height})");Flush();
    }
    private IEnumerator CaptureClipFrame()
    {
        yield return new WaitForEndOfFrame();var image=ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes(output+"/Clip/frame-"+(clipFrame++).ToString("D4")+".jpg",image.EncodeToJPG(94));Destroy(image);
    }
    private void SetupPointer()
    {
        originalSettings=InputSystem.settings;testSettings=Instantiate(originalSettings);testSettings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;InputSystem.settings=testSettings;
        foreach(var other in InputSystem.devices.OfType<Mouse>().Where(m=>m.enabled).ToArray()){suspended.Add(other);InputSystem.DisableDevice(other,keepSendingEvents:true);}
        InputSystem.onEvent+=FilterPointer;mouse=InputSystem.AddDevice<Mouse>("TravelTestPointer");mouse.MakeCurrent();
    }
    private void FilterPointer(InputEventPtr inputEvent,InputDevice device)
    {if(device is Mouse&&device!=mouse)inputEvent.handled=true;}
    private void Move(Vector2 point,ushort buttons=0)=>InputSystem.QueueStateEvent(mouse,new MouseState{position=point,buttons=buttons});
    private IEnumerator Click(Vector2 point)
    {
        Move(point);yield return null;yield return null;Move(point,1);yield return null;yield return null;Move(point);yield return null;yield return null;
        Check(Vector2.Distance(mouse.position.ReadValue(),point)<.1f,"The isolated pointer event stream delivers its click position");
    }
    private Vector2 Center(RectTransform rect){rect.GetWorldCorners(corners);return RectTransformUtility.WorldToScreenPoint(null,(corners[0]+corners[2])*.5f);}
    private static T Read<T>(object target,string field)=>(T)target.GetType().GetField(field,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(target);
    private static string Argument(string key,string fallback){var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,key);return index>=0&&index+1<args.Length?args[index+1]:fallback;}
    private void Check(bool condition,string message){if(!condition)throw new InvalidOperationException("FAILED: "+message);report.Add("PASS: "+message);}
    private void Flush()=>File.WriteAllLines(output+"/verification.txt",report.Concat(new[]{"Runtime errors: "+errors.Count}).Concat(errors));
    private void Finish(int code)
    {
        if(finished)return;finished=true;sampling=false;gc.Dispose();Flush();
        Application.logMessageReceived-=Log;SceneManager.sceneLoaded-=SceneLoaded;InputSystem.onEvent-=FilterPointer;
        if(mouse!=null)InputSystem.RemoveDevice(mouse);foreach(var device in suspended)if(device.added)InputSystem.EnableDevice(device);
        if(originalSettings!=null)InputSystem.settings=originalSettings;if(testSettings!=null)Destroy(testSettings);
        Application.Quit(code);enabled=false;
    }
}
