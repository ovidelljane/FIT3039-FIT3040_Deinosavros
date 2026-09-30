using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Deinosavros.MapReview
{
    [Serializable]
    public sealed class MapReviewCardArt
    {
        public CardDefinition definition;
        public GameObject front;
        public GameObject back;
        public Sprite faceArtwork;
        public UnityEngine.Rect frontContentBounds;
        public UnityEngine.Rect backContentBounds;
    }

    public sealed class MapReviewController : MonoBehaviour
    {
        public MapGraphDefinition graph;
        public CardDefinition[] startingDeck;
        public MapReviewCardArt[] artwork;
        public Material stateOutlineMaterial;
        public string encounterScene = "MapEncounterReview";
        private MapRunHost host;
        private RectTransform canvas, bar, detail, modal, deckRoot;
        private TMP_Text status, header, pageText, detailTitle, detailBody, notice, modalBody;
        private Button enterButton, sacrificeButton;
        private readonly List<MapReviewCardView> views = new();
        private readonly List<RaycastResult> uiHits = new();
        private readonly Vector3[] rectCorners = new Vector3[4];
        private MapReviewNode[] nodes = Array.Empty<MapReviewNode>();
        private MapReviewCardView detailOwner;
        private MapRunCard detailCard, confirmCard;
        private float closeAt;
        private int page;
        private string selected;
        private bool refreshPending, loadingReceiver;
        private string loadNotice;
        public bool ModalOpen => modal != null && modal.gameObject.activeSelf;
        public MapRunState State => host != null ? host.State : null;
        public RectTransform CardBar => bar;
        public RectTransform TopBar { get; private set; }
        public RectTransform DetailPanel => detail;
        public bool DetailVisible => detail != null && detail.gameObject.activeSelf;
        public RectTransform DetailRect => detail;
        public RectTransform ModalRect => modal;
        public IReadOnlyList<MapReviewCardView> CardViews => views;
        public string ActiveDetailInstanceId => detailCard?.InstanceId;
        public string PendingSacrificeInstanceId => confirmCard?.InstanceId;
        public string SelectedNodeId => selected;
        public int CurrentPage => page;
        public const float CardStride = 120;
        public const float CardHitWidth = 140;
        public static readonly Color Gold = new(.72f, .53f, .27f, 1);
        private static readonly Color Paper = new(.97f, .88f, .7f, 1);
        private static readonly Color PanelColor = new(.105f, .074f, .053f, .97f);

        private void Start()
        {
            host = MapRunHost.Ensure(graph, startingDeck);
            BuildUi(); host.State.Changed += QueueRefresh; Refresh();
            nodes = FindObjectsByType<MapReviewNode>(FindObjectsSortMode.None);
            foreach (var node in nodes) node.Bind(this);
        }
        private void OnDestroy() { if (host != null) host.State.Changed -= QueueRefresh; }
        private void QueueRefresh() => refreshPending = true;
        private void Update()
        {
            if (refreshPending) { refreshPending = false; Refresh(); }
            if (ModalOpen || Mouse.current == null) return;
            Vector2 position = Mouse.current.position.ReadValue();
            if (detailOwner != null)
            {
                bool withinGroup = detailOwner.ContainsScreenPoint(position) ||
                    RectTransformUtility.RectangleContainsScreenPoint(detail, position) || IsInDetailBridge(position);
                if (withinGroup) closeAt = 0;
                else if (closeAt <= 0) closeAt = Time.unscaledTime + .24f;
                else if (Time.unscaledTime >= closeAt) CloseDetail();
            }
            if (Mouse.current.leftButton.wasPressedThisFrame) SelectNodeAtPointer(position);
        }

        public bool BlocksMapPointer(Vector2 position)
        {
            if (ModalOpen || loadingReceiver || host == null || host.State.Phase != MapRunPhase.OnMap || IsInDetailBridge(position)) return true;
            if (EventSystem.current == null) return false;
            uiHits.Clear();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = position }, uiHits);
            // Use this pointer's current location rather than the event system's previous frame cache.
            return uiHits.Any(hit => hit.module is GraphicRaycaster);
        }

        public MapReviewCardView CardAtScreenPoint(Vector2 position)
        {
            if(ModalOpen||loadingReceiver)return null;
            if(DetailVisible&&RectTransformUtility.RectangleContainsScreenPoint(detail,position))return null;
            // Draw order and hit ownership stay right-over-left, independent of animated face bounds.
            for(int index=views.Count-1;index>=0;index--)
                if(views[index]!=null&&views[index].isActiveAndEnabled&&views[index].ContainsScreenPoint(position))return views[index];
            return null;
        }

        private void SelectNodeAtPointer(Vector2 position)
        {
            if (BlocksMapPointer(position) || Camera.main == null) return;
            Ray ray = Camera.main.ScreenPointToRay(position);
            float closest = float.PositiveInfinity;
            MapReviewNode selectedHit = null;
            foreach (var node in nodes)
            {
                if (node == null || !node.isActiveAndEnabled || node.interactionCollider == null ||
                    host.State.NodeState(node.nodeId) != MapNodeState.Available) continue;
                if (node.interactionCollider.Raycast(ray, out var hit, 500) && hit.distance < closest)
                {
                    closest = hit.distance;
                    selectedHit = node;
                }
            }
            if (selectedHit != null) SelectNode(selectedHit.nodeId);
        }

        private UnityEngine.Rect ScreenRect(RectTransform target)
        {
            target.GetWorldCorners(rectCorners);
            Vector2 min = RectTransformUtility.WorldToScreenPoint(null, rectCorners[0]);
            Vector2 max = RectTransformUtility.WorldToScreenPoint(null, rectCorners[2]);
            return UnityEngine.Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private bool IsInDetailBridge(Vector2 position)
        {
            if (detailOwner == null || detail == null || !detail.gameObject.activeSelf) return false;
            var cardBounds = ScreenRect(detailOwner.HitRect);
            var detailBounds = ScreenRect(detail);
            float minX = Mathf.Max(cardBounds.xMin, detailBounds.xMin);
            float maxX = Mathf.Min(cardBounds.xMax, detailBounds.xMax);
            if (maxX <= minX || detailBounds.yMin < cardBounds.yMax) return false;
            return UnityEngine.Rect.MinMaxRect(minX, cardBounds.yMax - 2, maxX, detailBounds.yMin + 2).Contains(position);
        }
        public static RectTransform Rect(string name, Transform parent, Vector2 min, Vector2 max, Vector2 position, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false); rect.anchorMin = min; rect.anchorMax = max;
            rect.anchoredPosition = position; rect.sizeDelta = size; return rect;
        }
        public static TMP_Text Label(string name, Transform parent, string text, Vector2 min, Vector2 max, Vector2 position, Vector2 size, float fontSize = 22)
        {
            var rect = Rect(name, parent, min, max, position, size);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>(); label.text = text; label.fontSize = fontSize;
            label.color = Paper; label.alignment = TextAlignmentOptions.Center; label.raycastTarget = false;
            label.enableAutoSizing = false; return label;
        }
        public static Button Button(string name, Transform parent, string text, Vector2 anchor, Vector2 position, Vector2 size, Action action)
        {
            var rect = Rect(name, parent, anchor, anchor, position, size);
            var image = rect.gameObject.AddComponent<Image>(); image.color = new Color(.26f, .17f, .09f, 1);
            var outline = rect.gameObject.AddComponent<Outline>(); outline.effectColor = Gold; outline.effectDistance = new Vector2(1.3f, -1.3f);
            var button = rect.gameObject.AddComponent<Button>(); button.onClick.AddListener(() => action());
            Label("Label", rect, text, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 19);
            return button;
        }
        private static void Panel(RectTransform rect, Color color)
        {
            rect.gameObject.AddComponent<Image>().color = color;
            var edge = rect.gameObject.AddComponent<Outline>(); edge.effectColor = Gold; edge.effectDistance = new Vector2(1, -1);
        }
        private void BuildUi()
        {
            if (EventSystem.current == null)
            {
                var events = new GameObject("Review Event System", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
            canvas = new GameObject("Map Review Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)).GetComponent<RectTransform>();
            var c = canvas.GetComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 100;
            var scaler = canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
            TopBar = Rect("TopStatusBar", canvas, new Vector2(0, 1), Vector2.one, new Vector2(0, -29), new Vector2(0, 58)); Panel(TopBar, PanelColor);
            header = Label("RunStatus", TopBar, "MAP REVIEW", Vector2.zero, Vector2.one, new Vector2(-90, 0), new Vector2(-250, 0));
            Button("NewRun", TopBar, "New adventure", new Vector2(1,.5f), new Vector2(-105,0), new Vector2(185,36), BeginNewAdventure);
            bar = Rect("CardBar", canvas, Vector2.zero, new Vector2(1, .26f), Vector2.zero, Vector2.zero); Panel(bar, PanelColor);
            status = Label("PersistentPlayerStatus", bar, "", new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(127, 8), new Vector2(210, 245), 21);
            status.alignment = TextAlignmentOptions.TopLeft;
            deckRoot = Rect("FiveCardPage", bar, new Vector2(.15f,0), new Vector2(.86f,1), Vector2.zero, Vector2.zero);
            Button("PreviousPage", bar, "<", new Vector2(.137f,.5f), Vector2.zero, new Vector2(36,56), () => ChangePage(-1));
            Button("NextPage", bar, ">", new Vector2(.875f,.5f), Vector2.zero, new Vector2(36,56), () => ChangePage(1));
            pageText = Label("Page", bar, "", new Vector2(.93f,.68f), new Vector2(.93f,.68f), Vector2.zero, new Vector2(195,88), 18);
            enterButton = Button("EnterEncounter", bar, "Choose a node", new Vector2(.935f,.35f), Vector2.zero, new Vector2(192,56), EnterEncounter);
            notice = Label("PendingPreview", bar, "", new Vector2(.53f,1), new Vector2(.53f,1), new Vector2(0,-15), new Vector2(1230,28), 16);
            detail = Rect("CardDetailPanel", canvas, new Vector2(.5f,.26f), new Vector2(.5f,.26f), new Vector2(0,112), new Vector2(700,214)); Panel(detail, PanelColor);
            detailTitle = Label("CardName", detail, "", new Vector2(.5f,1), new Vector2(.5f,1), new Vector2(0,-32), new Vector2(650,40), 26);
            detailBody = Label("OfferingDescription", detail, "", new Vector2(.5f,.5f), new Vector2(.5f,.5f), new Vector2(-94,-5), new Vector2(435,104), 19);
            sacrificeButton = Button("Offer", detail, "Sacrifice", new Vector2(1,.35f), new Vector2(-100,0), new Vector2(158,46), () => RequestSacrifice(detailCard));
            detail.gameObject.SetActive(false);
            modal = Rect("ConfirmationBlocker", canvas, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero); Panel(modal, new Color(0,0,0,.68f));
            var dialog = Rect("ConfirmOffering", modal, new Vector2(.5f,.5f), new Vector2(.5f,.5f), Vector2.zero, new Vector2(740,380)); Panel(dialog, PanelColor);
            modalBody = Label("ConfirmationText", dialog, "", Vector2.zero, Vector2.one, new Vector2(0,40), new Vector2(-72,-145), 24);
            Button("Confirm", dialog, "Confirm offering", new Vector2(.7f,0), new Vector2(0,55), new Vector2(235,52), ConfirmSacrifice);
            Button("Cancel", dialog, "Keep card", new Vector2(.3f,0), new Vector2(0,55), new Vector2(210,52), CancelSacrifice);
            modal.gameObject.SetActive(false);
        }
        private void Refresh()
        {
            if (canvas == null) return;
            var state = host.State; var p = state.Player;
            status.text = $"<color=#C9A565>EXPLORER</color>\nHP   {p.Health} / {p.MaxHealth}\nDamage   {p.Damage}\nAttack   {1 / p.AttackInterval:0.##} /s\nShield   {p.Shield}\nElixir   {p.Elixir:0.#} / {p.MaxElixir:0.#}";
            string phase=state.Phase switch
            {
                MapRunPhase.OnMap=>"Choose a route",
                MapRunPhase.Prepared=>"Preparing encounter",
                MapRunPhase.InEncounter=>"In encounter",
                MapRunPhase.Won=>"Adventure complete",
                MapRunPhase.Lost=>"Adventure ended",
                _=>"Adventure"
            };
            header.text = $"ANCIENT SANCTUARY    /    {phase}    /    {(state.SacrificeUsed ? "Offering used" : "One offering available")}";
            notice.text = !string.IsNullOrEmpty(loadNotice) ? loadNotice : state.PendingEffect.IsValid ? "NEXT ENCOUNTER: " + state.PendingEffect.Description : "Hover to turn a card. An offering removes that instance until this adventure ends.";
            var deck = state.Cards.Where(c => !c.Sacrificed).ToArray(); int pages = Mathf.Max(1, Mathf.CeilToInt(deck.Length/5f)); page = Mathf.Clamp(page, 0, pages-1);
            pageText.text = $"Page {page+1} / {pages}\nCapacity {state.ActiveCapacity} / 20" + (state.ActiveCapacity > 20 ? "\n<color=#F3AB6C>Over capacity</color>" : "");
            CloseDetail();
            foreach (var view in views) if (view != null) { view.gameObject.SetActive(false); Destroy(view.gameObject); }
            views.Clear();
            for (int i=0;i<5 && page*5+i<deck.Length;i++)
            {
                var card = deck[page*5+i];
                var rect = Rect("Card_"+card.InstanceId, deckRoot, new Vector2(0,.48f), new Vector2(0,.48f), new Vector2(CardHitWidth*.5f+i*CardStride,0), new Vector2(CardHitWidth,226));
                var view=rect.gameObject.AddComponent<MapReviewCardView>();
                var art=artwork?.FirstOrDefault(a=>a != null && a.definition==card.Definition);
                view.Initialize(this,card,art?.front,art?.back,art?.faceArtwork,art?.frontContentBounds??default,art?.backContentBounds??default); views.Add(view);
            }
            if (selected != null && state.NodeState(selected) != MapNodeState.Available) selected = null;
            enterButton.interactable = selected != null && state.Phase == MapRunPhase.OnMap;
            enterButton.GetComponentInChildren<TMP_Text>().text = selected == null ? "Choose a node" : "Begin encounter";
        }
        public void BeginNewAdventure()
        {
            if (ModalOpen || loadingReceiver || host == null || host.State.Phase == MapRunPhase.InEncounter) return;
            host.Simulation = null;
            host.LastResult = null;
            selected = null;
            page = 0;
            loadNotice = null;
            host.State.BeginNewRun(graph, startingDeck);
        }
        public void ChangePage(int delta) { if (ModalOpen || loadingReceiver) return; page += delta; Refresh(); }
        public bool IsDetailOwner(MapReviewCardView view) => detailOwner == view && detail != null && detail.gameObject.activeSelf;
        public void CardEntered(MapReviewCardView view)
        {
            if (detailOwner != view) CloseDetail(); else closeAt = 0;
        }
        public void CardExited(MapReviewCardView view) { if (detailOwner == view) closeAt = Time.unscaledTime + .24f; }
        public void ShowDetail(MapReviewCardView view, MapRunCard card)
        {
            if (ModalOpen || loadingReceiver || view == null || card == null || card.Sacrificed || (detailOwner == view && detail.gameObject.activeSelf)) return;
            detailOwner = view; detailCard = card; closeAt = 0; detail.gameObject.SetActive(true);
            Canvas.ForceUpdateCanvases();
            float cardCenter = canvas.InverseTransformPoint(view.HitRect.position).x;
            float halfAvailable = Mathf.Max(0, (canvas.rect.width - detail.rect.width) * .5f - 16);
            detail.anchoredPosition = new Vector2(Mathf.Clamp(cardCenter, -halfAvailable, halfAvailable), detail.anchoredPosition.y);
            detailTitle.text = card.Definition.displayName;
            var effect = new MapSacrificeEffect(card.InstanceId, card.Definition.overworldEffect.effectType);
            detailBody.text = effect.Description + $"\nCurrent attack interval: {host.State.Player.AttackInterval:0.##} s / attack.";
            sacrificeButton.interactable = !host.State.SacrificeUsed && host.State.Phase == MapRunPhase.OnMap;
        }
        private void CloseDetail() { detailOwner = null; detailCard = null; closeAt = 0; if (detail != null) detail.gameObject.SetActive(false); }
        public void RequestSacrifice(MapRunCard card)
        {
            if (ModalOpen || loadingReceiver || host == null || card == null || card.Sacrificed || host.State.SacrificeUsed || host.State.Phase != MapRunPhase.OnMap ||
                !host.State.Cards.Any(current => current.InstanceId == card.InstanceId && ReferenceEquals(current, card))) return;
            confirmCard = card;
            modalBody.text = $"Sacrifice {card.Definition.displayName}?\n\nThis specific card is removed for the rest of this adventure.\n\n" + new MapSacrificeEffect(card.InstanceId,card.Definition.overworldEffect.effectType).Description;
            modal.gameObject.SetActive(true);
        }
        public void ConfirmSacrifice()
        {
            if (!ModalOpen || confirmCard == null) return;
            string id = confirmCard.InstanceId; confirmCard = null;
            host.State.TrySacrifice(id); modal.gameObject.SetActive(false); CloseDetail();
        }
        public void CancelSacrifice()
        {
            confirmCard = null;
            if (modal != null) modal.gameObject.SetActive(false);
        }
        public void SelectNode(string id)
        {
            if (ModalOpen || loadingReceiver || host == null || host.State.Phase != MapRunPhase.OnMap || host.State.NodeState(id) != MapNodeState.Available) return;
            selected = id; enterButton.interactable = true; enterButton.GetComponentInChildren<TMP_Text>().text = "Begin encounter";
        }
        public bool IsSelected(string id) => selected == id;
        public void EnterEncounter()
        {
            if (ModalOpen || loadingReceiver || selected == null || !host.State.TryPrepareEncounter(selected, out var request)) return;
            loadNotice = null;
            loadingReceiver = true;
            CloseDetail();
            StartCoroutine(LoadReceiver(request));
        }
        private IEnumerator LoadReceiver(MapEncounterRequest request)
        {
            AsyncOperation load = null;
            try { load = SceneManager.LoadSceneAsync(encounterScene); }
            catch (Exception exception) { Debug.LogWarning("Review receiver load failed: " + exception.Message); }
            if (load == null)
            {
                loadingReceiver = false;
                loadNotice = "The receiver could not load. Your offering is preserved; choose the node to retry.";
                host.State.CancelPreparedEncounter(request.EncounterId);
                yield break;
            }
            while (!load.isDone) yield return null;
        }
    }
}
