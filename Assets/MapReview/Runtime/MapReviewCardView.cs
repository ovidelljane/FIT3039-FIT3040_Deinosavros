using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Sprites;
using UnityEngine.UI;

namespace Deinosavros.MapReview
{
    public sealed class MapReviewCardView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler, IPointerClickHandler
    {
        public const float FlipDuration = .28f;
        public const float DetailDelay = .15f;
        private RectTransform visual;
        private GameObject front, back;
        private MapReviewController owner;
        private MapRunCard card;
        private bool inside;
        private Vector2 pointer, tilt;
        private float progress, settled;
        public string InstanceId => card?.InstanceId;
        public float FlipProgress => progress;
        public bool IsBackVisible => progress >= .5f;
        public bool IsPointerInside => inside;
        public RectTransform HitRect => (RectTransform)transform;
        public RectTransform VisualRect => visual;
        public Vector2 FrontVisibleSize { get; private set; }
        public Vector2 BackVisibleSize { get; private set; }

        public void Initialize(MapReviewController controller, MapRunCard instance, GameObject frontPrefab, GameObject backPrefab, Sprite frontFaceArtwork = null,
            UnityEngine.Rect frontContentBounds=default,UnityEngine.Rect backContentBounds=default)
        {
            owner = controller; card = instance;
            var hit = GetComponent<Image>() ?? gameObject.AddComponent<Image>(); hit.color = Color.clear; hit.raycastTarget = true;
            visual = MapReviewController.Rect("Animated visual", transform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(162, 222));
            front = MakeFace("Front", frontPrefab, instance.Definition.frontArtwork, frontFaceArtwork, frontContentBounds, false, out var frontSize);
            back = MakeFace("Back", backPrefab, instance.Definition.backArtwork, null, backContentBounds, true, out var backSize);
            FrontVisibleSize=frontSize;BackVisibleSize=backSize;
            back.SetActive(false);
            var title=MapReviewController.Label("Instance card name",transform,instance.Definition.displayName,new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,-11),new Vector2(MapReviewController.CardStride-4,20),12);
            title.overflowMode=TextOverflowModes.Ellipsis;
        }
        private GameObject MakeFace(string name, GameObject prefab, Sprite border, Sprite illustration, UnityEngine.Rect contentBounds, bool reverse, out Vector2 visibleSize)
        {
            var root = MapReviewController.Rect(name, visual, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, visual.sizeDelta);
            if(illustration!=null)
            {
                FitOriginalSprite("Original illustrated face",root,illustration,contentBounds,out visibleSize);
                return root.gameObject;
            }
            var palette=prefab!=null?prefab.GetComponent<Image>():null;
            if(reverse&&prefab!=null)
            {
                var originalBackground=prefab.transform.Find("EffectBackground");
                if(originalBackground!=null)palette=originalBackground.GetComponent<Image>();
            }
            visibleSize=visual.sizeDelta;
            RectTransform borderRect=null;
            if(border!=null)borderRect=FitOriginalSprite("Original border artwork",root,border,contentBounds,out visibleSize);
            var fill=MapReviewController.Rect("Original palette fill",root,new Vector2(.5f,.5f),new Vector2(.5f,.5f),new Vector2(0,-2),new Vector2(visibleSize.x*.84f,visibleSize.y*.82f));
            var background=fill.gameObject.AddComponent<Image>();
            background.color=palette!=null?palette.color:new Color(.13f,.10f,.08f);
            background.raycastTarget=false;fill.SetAsFirstSibling();
            if(borderRect!=null)borderRect.SetAsLastSibling();
            if(!reverse)
            {
                // This card has no illustrated face asset: retain its existing border and actual combat text.
                MapReviewController.Label("Bound combat effect",root,card.Definition.combatEffectText,new Vector2(.5f,.5f),new Vector2(.5f,.5f),Vector2.zero,new Vector2(visibleSize.x*.77f,95),21);
            }
            else
            {
                float width=visibleSize.x*.77f;
                MapReviewController.Label("Offering title",root,card.Definition.displayName,new Vector2(.5f,.5f),new Vector2(.5f,.5f),new Vector2(0,46),new Vector2(width,43),15);
                string effect=card.Definition.overworldEffect.effectType switch
                {
                    OverworldEffectType.ReduceEnemyStartingHealth=>"Enemies enter at\n75% HP",
                    OverworldEffectType.ImprovePlayerAttackSpeed=>"-1 second\nattack interval",
                    OverworldEffectType.GrantStartingShield=>"+5 temporary\nshield",
                    OverworldEffectType.IncreaseStartingElixir=>"+3 maximum\nElixir",
                    _=>"Offering"
                };
                MapReviewController.Label("Offering summary",root,effect,new Vector2(.5f,.5f),new Vector2(.5f,.5f),new Vector2(0,-7),new Vector2(width,58),17);
                bool permanent=card.Definition.overworldEffect.effectType==OverworldEffectType.IncreaseStartingElixir;
                MapReviewController.Label("Offering duration",root,permanent?"MAXIMUM: THIS RUN":"NEXT ENCOUNTER",new Vector2(.5f,.5f),new Vector2(.5f,.5f),new Vector2(0,-55),new Vector2(width,28),10);
                MapReviewController.Label("Offering note",root,permanent?"Restore 3 on start":"Offer once before battle",new Vector2(.5f,.5f),new Vector2(.5f,.5f),new Vector2(0,-78),new Vector2(width,22),9);
            }
            return root.gameObject;
        }
        private RectTransform FitOriginalSprite(string name,RectTransform parent,Sprite sprite,UnityEngine.Rect contentBounds,out Vector2 visibleSize)
        {
            Vector2 sourceSize=sprite.rect.size;
            Vector4 padding=DataUtility.GetPadding(sprite);
            var content=UnityEngine.Rect.MinMaxRect(padding.x,padding.y,sourceSize.x-padding.z,sourceSize.y-padding.w);
            if(contentBounds.width>0&&contentBounds.height>0)
            {
                // Bounds describe the authored card body, excluding transparent margin and isolated export marks.
                content=UnityEngine.Rect.MinMaxRect(Mathf.Max(0,contentBounds.xMin*sourceSize.x-1),Mathf.Max(0,contentBounds.yMin*sourceSize.y-1),
                    Mathf.Min(sourceSize.x,contentBounds.xMax*sourceSize.x+1),Mathf.Min(sourceSize.y,contentBounds.yMax*sourceSize.y+1));
            }
            float scale=Mathf.Min(visual.rect.width/Mathf.Max(1,content.width),visual.rect.height/Mathf.Max(1,content.height));
            visibleSize=content.size*scale;
            var viewport=MapReviewController.Rect(name,parent,new Vector2(.5f,.5f),new Vector2(.5f,.5f),Vector2.zero,visibleSize);
            viewport.gameObject.AddComponent<RectMask2D>();
            var rect=MapReviewController.Rect("Unmodified source sprite",viewport,new Vector2(.5f,.5f),new Vector2(.5f,.5f),(sourceSize*.5f-content.center)*scale,sourceSize*scale);
            var image=rect.gameObject.AddComponent<Image>();image.sprite=sprite;image.preserveAspect=true;image.raycastTarget=false;
            return viewport;
        }
        public bool ContainsScreenPoint(Vector2 screenPoint) => RectTransformUtility.RectangleContainsScreenPoint(HitRect, screenPoint);
        private void Update()
        {
            if (owner == null || visual == null || owner.ModalOpen) return;
            // Reconcile after modal dismissal and page changes even without a new pointer event.
            if (Mouse.current != null)
            {
                Vector2 position = Mouse.current.position.ReadValue();
                SetInside(owner.CardAtScreenPoint(position)==this);
                if (inside) UpdatePointer(position, null);
            }
            bool active = inside || owner.IsDetailOwner(this);
            float dt = Time.unscaledDeltaTime;
            float previousProgress = progress;
            progress = Mathf.MoveTowards(progress, active ? 1 : 0, dt / FlipDuration);
            bool backVisible = IsBackVisible;
            if (front.activeSelf == backVisible) front.SetActive(!backVisible);
            if (back.activeSelf != backVisible) back.SetActive(backVisible);
            float angle = Mathf.SmoothStep(0, 180, progress);
            // Swap at the edge and keep the newly visible face readable, never mirrored.
            if (backVisible) angle -= 180;
            Vector2 targetTilt = inside ? new Vector2(-pointer.y * 3, pointer.x * 6) : Vector2.zero;
            tilt = Vector2.Lerp(tilt, targetTilt, 1 - Mathf.Exp(-18 * dt));
            float edgeTilt = Mathf.Abs(Mathf.Cos(angle * Mathf.Deg2Rad));
            visual.localRotation = Quaternion.Euler(tilt.x * edgeTilt, angle + tilt.y * edgeTilt, 0);
            visual.localScale = Vector3.Lerp(visual.localScale, Vector3.one * (active ? 1.04f : 1), 1 - Mathf.Exp(-16 * dt));
            if (inside && Mathf.Approximately(progress, 1))
            {
                // Only the remainder after reaching the back counts toward the settled dwell.
                settled += Mathf.Max(0, dt - (1 - previousProgress) * FlipDuration);
                if (settled >= DetailDelay) owner.ShowDetail(this, card);
            }
            else settled = 0;
        }
        private void SetInside(bool value)
        {
            if (inside == value) return;
            inside = value;
            if (inside) owner.CardEntered(this);
            else { settled = 0; owner.CardExited(this); }
        }
        private void UpdatePointer(Vector2 position, Camera eventCamera)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(HitRect, position, eventCamera, out var local);
            local -= HitRect.rect.center;
            pointer = new Vector2(Mathf.Clamp(local.x / Mathf.Max(1, HitRect.rect.width * .5f), -1, 1), Mathf.Clamp(local.y / Mathf.Max(1, HitRect.rect.height * .5f), -1, 1));
        }
        public void OnPointerEnter(PointerEventData e) { if (owner == null || owner.ModalOpen) return; SetInside(owner.CardAtScreenPoint(e.position)==this); OnPointerMove(e); }
        public void OnPointerExit(PointerEventData e) { if (owner != null && !owner.ModalOpen) SetInside(owner.CardAtScreenPoint(e.position)==this); }
        public void OnPointerMove(PointerEventData e)
        {
            if (owner != null && !owner.ModalOpen && owner.CardAtScreenPoint(e.position)==this) UpdatePointer(e.position, e.enterEventCamera);
        }
        public void OnPointerClick(PointerEventData e) { if (owner != null && !owner.ModalOpen && e.button == PointerEventData.InputButton.Left && owner.CardAtScreenPoint(e.position)==this) owner.RequestSacrifice(card); }
    }
}
