using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Deinosavros.MapReview
{
    public sealed class MapEncounterReviewController : MonoBehaviour
    {
        public MapGraphDefinition graph;
        public CardDefinition[] definitions;
        public string mapScene = "MapReview";
        private MapRunHost host;
        private TMP_Text summary, message;
        private MapEncounterRequest request;
        private int wave, rewardIndex;
        private string lastSpawnId;
        public MapEncounterRequest Request => request;
        public bool Running => host != null && host.State.Phase == MapRunPhase.InEncounter && host.Simulation != null;
        public string LastMessage => message != null ? message.text : "";
        private void Start()
        {
            host=MapRunHost.Ensure(graph,definitions);
            request=host.State.CurrentEncounter;
            host.State.Changed += Refresh;
            var canvas=new GameObject("Encounter Review Canvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
            canvas.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
            var scaler=canvas.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
            if(EventSystem.current==null)new GameObject("Review Events",typeof(EventSystem),typeof(InputSystemUIInputModule)).GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            var panel=MapReviewController.Rect("Receiver Panel",canvas.transform,new Vector2(.5f,.5f),new Vector2(.5f,.5f),Vector2.zero,new Vector2(1500,850));
            panel.gameObject.AddComponent<Image>().color=new Color(.10f,.075f,.05f,.98f);
            MapReviewController.Label("Title",panel,"ENCOUNTER RECEIVER / MAP-SIDE TEST ONLY",new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(0,-50),new Vector2(1400,65),32);
            summary=MapReviewController.Label("State",panel,"",new Vector2(.5f,.66f),new Vector2(.5f,.66f),Vector2.zero,new Vector2(1400,360),24);
            message=MapReviewController.Label("Message",panel,"Preparation may remain delayed. Effects are not committed until readiness is acknowledged.",new Vector2(.5f,.37f),new Vector2(.5f,.37f),Vector2.zero,new Vector2(1380,75),21);
            string[] labels={"Acknowledge start","Simulate load failure","Spawn later wave","Repeat enemy spawn","Take 8 damage","Spend 2 Elixir","Add reward card","Victory + return","Defeat + return","Victory, stay for tests","Repeat last result","Return to map"};
            Action[] actions={Acknowledge,FailLoad,Spawn,RepeatSpawn,Damage,Spend,Reward,()=>Complete(true),()=>Complete(false),()=>CompleteWithoutReturning(true),Repeat,Return};
            for(int i=0;i<labels.Length;i++)
            {
                int index=i;
                MapReviewController.Button("Action"+i,panel,labels[i],new Vector2(.125f+(i%4)*.25f,.25f-(i/4)*.1f),Vector2.zero,new Vector2(310,65),actions[index]);
            }
            Refresh();
        }
        private void OnDestroy() { if(host!=null)host.State.Changed-=Refresh; }
        public void Acknowledge()
        {
            bool started=request!=null&&host.State.AcknowledgeEncounterStarted(request.EncounterId);
            if(started)
            {
                host.Simulation=new MapEncounterSimulation(request);
                lastSpawnId="wave-0";
                wave=0;
                host.Simulation.SpawnEnemy(lastSpawnId,101);
            }
            message.text=started?"Started. Offering committed once.":"Duplicate, missing or expired start was ignored.";Refresh();
        }
        public void FailLoad()
        {
            if(request!=null&&host.State.CancelPreparedEncounter(request.EncounterId)){host.Simulation=null;Return();}
            else message.text="Only an unstarted preparation can fail or be cancelled.";
        }
        public void Spawn()
        {
            if(!RequireRunning())return;
            do { lastSpawnId="wave-"+(++wave); } while(host.Simulation.Enemies.ContainsKey(lastSpawnId));
            int health=host.Simulation.SpawnEnemy(lastSpawnId,5);
            message.text=$"Later enemy {lastSpawnId} entered at {health} HP from 5 initial HP.";
            Refresh();
        }
        public void RepeatSpawn()
        {
            if(!RequireRunning())return;
            if(string.IsNullOrEmpty(lastSpawnId)){message.text="Spawn an enemy first.";return;}
            int health=host.Simulation.SpawnEnemy(lastSpawnId,999);
            message.text=$"Repeated spawn {lastSpawnId}: still {health} HP. The effect was not applied twice.";
            Refresh();
        }
        public void Damage()
        {
            if(!RequireRunning())return;
            host.Simulation.TakeDamage(8);
            message.text="Applied 8 damage: temporary shield first, persistent shield second, then HP.";
            Refresh();
        }
        public void Spend()
        {
            if(!RequireRunning())return;
            message.text=host.Simulation.SpendElixir(2)?"Spent 2 Elixir.":"Insufficient Elixir; nothing was consumed.";
            Refresh();
        }
        public void Reward()
        {
            if(!RequireRunning())return;
            var valid=definitions?.Where(definition=>definition!=null).ToArray();
            if(valid==null||valid.Length==0){message.text="No reward definitions are configured.";return;}
            var added=host.State.AddCard(valid[(rewardIndex++)%valid.Length]);
            message.text=added!=null?"A separate reward instance was added; no capacity rule was changed.":"This run cannot accept a reward in its current phase.";
            Refresh();
        }
        private bool RequireRunning()
        {
            if(Running)return true;
            message.text="Acknowledge a prepared encounter before using this action.";
            return false;
        }
        public void Complete(bool victory) => Finish(victory,true);
        public void CompleteWithoutReturning(bool victory) => Finish(victory,false);
        private void Finish(bool victory,bool returnToMap)
        {
            if(!RequireRunning())return;
            var result=host.Simulation.Result(victory);
            if(host.State.CompleteEncounter(result))
            {
                host.LastResult=result;
                host.Simulation=null;
                message.text="Result accepted once. Temporary effects were removed from the returned state.";
                Refresh();
                if(returnToMap)Return();
            }
            else message.text="The result was rejected without modifying the run.";
        }
        public void Repeat(){message.text="Repeated result accepted: "+host.State.CompleteEncounter(host.LastResult);Refresh();}
        public void Return()
        {
            if(host.State.Phase==MapRunPhase.InEncounter){message.text="Finish the active encounter before returning.";return;}
            if(host.State.Phase==MapRunPhase.Prepared&&request!=null)host.State.CancelPreparedEncounter(request.EncounterId);
            try { SceneManager.LoadScene(mapScene); }
            catch(Exception exception)
            {
                message.text="The map scene could not load. The current run remains available.";
                Debug.LogWarning("Review map load failed: "+exception.Message);
            }
        }
        private void Refresh()
        {
            if(summary==null||host==null)return;
            var p=host.Simulation?.PersistentPlayer??host.State.Player;
            string enemies=host.Simulation==null?"None":string.Join(", ",host.Simulation.Enemies.Select(e=>$"{e.Key}: {e.Value} HP"));
            summary.text=$"Phase: {host.State.Phase}\nNode: {request?.NodeId??"No prepared encounter"}\nHP {p.Health}/{p.MaxHealth}  |  Damage {p.Damage}  |  Base interval {p.AttackInterval:0.##} s\nPersistent shield {p.Shield}  |  Temporary shield {host.Simulation?.TemporaryShield??0}\nEffective interval {host.Simulation?.EffectiveAttackInterval??p.AttackInterval:0.##} s\nElixir {p.Elixir:0.#}/{p.MaxElixir:0.#}\nEnemies: {enemies}\nActive cards: {host.State.Cards.Count(c=>!c.Sacrificed)}\nOffering: {request?.Effect.Description??"None"}";
        }
    }
}
