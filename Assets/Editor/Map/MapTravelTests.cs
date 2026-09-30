using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Deinosavros.MapReview;
using UnityEditor;
using UnityEngine;

public static class MapTravelTests
{
    private static int assertions;
    [MenuItem("Tools/Map/Verify Travel State")]
    public static void Run()
    {
        assertions = 0;
        var graph = AssetDatabase.LoadAssetAtPath<MapGraphDefinition>("Assets/Art/Map/Settings/Graph.asset");
        graph.Validate(); var completePaths = new List<string[]>();
        void Visit(string id, List<string> route)
        {
            route.Add(id); var node = graph.Find(id);
            if (node.boss) completePaths.Add(route.ToArray());
            else foreach (var next in node.next) Visit(next,new List<string>(route));
        }
        Visit(graph.nodes.Single(n=>n.layer==1).id,new List<string>());
        var covered = new HashSet<string>();
        foreach (var route in completePaths)
        {
            var state = new MapProgressState(graph); string stale = null;
            Check(route.Length==6,"Every route has five normal encounters and a Boss.");
            for (int i=0;i<route.Length;i++)
            {
                string original=state.CurrentNodeId;
                Check(state.TryBeginTravel(route[i],out var ticket),"Available departure accepted.");
                Check(!state.TryBeginTravel(route[i],out _),"Duplicate departure rejected.");
                Check(state.CurrentNodeId==original,"Movement does not commit location.");
                Check(!state.ConfirmEncounterStarted(ticket.EncounterId),"Arrival required before acknowledgement.");
                Check(!state.CompleteEncounter(ticket.EncounterId,true),"Prepared result rejected.");
                Check(state.MarkLoading(ticket.EncounterId),"Arrival accepted once.");
                Check(!state.MarkLoading(ticket.EncounterId),"Duplicate arrival rejected.");
                Check(state.ConfirmEncounterStarted(ticket.EncounterId),"Ready receiver commits location.");
                Check(state.CurrentNodeId==route[i],"Committed target matches receiver.");
                Check(!state.ConfirmEncounterStarted(ticket.EncounterId),"Duplicate acknowledgement rejected.");
                Check(!state.CancelPreparedTravel(ticket.EncounterId),"Started encounter cannot be rolled back.");
                if(stale!=null)Check(!state.CompleteEncounter(stale,true),"Stale result rejected.");
                Check(state.CompleteEncounter(ticket.EncounterId,true),"Victory accepted.");
                Check(!state.CompleteEncounter(ticket.EncounterId,true),"Duplicate result rejected.");
                Check(state.NodeState(route[i])==MapLocationState.Completed,"Victory marks completion.");
                if(i>0){covered.Add(route[i-1]+">"+route[i]);Check(state.HasTraveled(route[i-1],route[i]),"Traversed road retained.");}
                if(i<route.Length-1)
                {
                    var expected=new HashSet<string>(graph.Find(route[i]).next);
                    foreach(var n in graph.nodes)
                        Check((state.NodeState(n.id)==MapLocationState.Available)==expected.Contains(n.id),"Only explicit next nodes are available.");
                    Check(!state.TryBeginTravel(route[i],out _),"Completed node cannot be entered again.");
                }
                stale=ticket.EncounterId;
            }
            Check(state.Phase==MapProgressPhase.Won,"Boss victory ends the run.");
            Check(!state.TryBeginTravel(route[0],out _),"Finished run blocks travel.");
            string oldRun=state.RunId;state.BeginNewRun(graph);
            Check(state.RunId!=oldRun&&state.CompletedCount==0,"New adventure clears progression and changes identity.");
            Check(!state.CompleteEncounter(stale,true),"Previous adventure result rejected.");
        }
        Check(covered.Count==19,"All nineteen edges covered.");
        var retry=new MapProgressState(graph);string origin=retry.CurrentNodeId;
        Check(retry.TryBeginTravel(origin,out var canceled),"Initial departure accepted.");
        Check(retry.CancelPreparedTravel(canceled.EncounterId),"Preload failure cancels once.");
        Check(retry.CurrentNodeId==origin&&retry.NodeState(origin)==MapLocationState.Available,"Cancellation restores the exact origin.");
        Check(!retry.CancelPreparedTravel(canceled.EncounterId),"Duplicate cancellation ignored.");
        Check(retry.TryBeginTravel(origin,out var second),"Retry accepted.");
        Check(!retry.MarkLoading(canceled.EncounterId),"Old attempt cannot acknowledge retry.");
        Check(retry.MarkLoading(second.EncounterId)&&retry.ConfirmEncounterStarted(second.EncounterId),"Retry can start.");
        Check(retry.CompleteEncounter(second.EncounterId,false)&&retry.Phase==MapProgressPhase.Lost,"Defeat ends the adventure.");
        Check(!retry.TryBeginTravel(origin,out _),"Defeated run cannot move.");
        float previous=0;
        for(int i=0;i<=100;i++)
        {
            float value=MapTravelPath.EaseDistance(i/100f);
            Check(value>=previous&&value>=0&&value<=1.00001f,"Motion distance is monotonic and bounded.");previous=value;
        }
        Check(Mathf.Abs(MapTravelPath.EaseDistance(0))<.00001f&&Mathf.Abs(MapTravelPath.EaseDistance(1)-1)<.00001f,"Motion starts and ends exactly.");
        Directory.CreateDirectory("Library/MapTests");
        File.WriteAllText("Library/MapTests/state-tests.txt",$"PASS: {assertions} assertions; {completePaths.Count} complete paths; {covered.Count} edges; duplicate input, cancellation, delayed start, stale results, defeat and reset.");
    }
    private static void Check(bool condition,string message){assertions++;if(!condition)throw new InvalidOperationException(message);}
}
