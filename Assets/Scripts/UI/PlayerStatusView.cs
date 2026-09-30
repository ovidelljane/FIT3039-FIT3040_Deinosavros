using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Presentation only. BattleScript and RunSession remain the sources of truth.
public sealed class PlayerStatusView : MonoBehaviour
{
    public static readonly Color Ink=new(.095f,.071f,.052f,.97f), Gold=new(.72f,.53f,.29f), Cream=new(1,.91f,.74f);
    public ResourceBarView Health { get; private set; }
    public ResourceBarView Elixir { get; private set; }
    public BuffCards PreviewCard { get; private set; }
    private BattleScript player;
    private RunSession session;
    private TMP_Text shield,speed,damage;
    private int oldShield=int.MinValue,oldDamage=int.MinValue;
    private float oldInterval=float.NaN;

    public static PlayerStatusView Create(Transform parent,bool compact)
    {
        var root=Rect("Status",parent);
        root.anchorMin=Vector2.zero; root.anchorMax=Vector2.one; root.offsetMin=root.offsetMax=Vector2.zero;
        var view=root.gameObject.AddComponent<PlayerStatusView>();
        var background=root.gameObject.AddComponent<Image>(); background.color=Ink; background.raycastTarget=false;
        var border=root.gameObject.AddComponent<Outline>(); border.effectColor=Gold; border.effectDistance=new Vector2(1,-1);
        var layout=root.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding=new RectOffset(compact?12:18,compact?12:18,10,10); layout.spacing=compact?3:6;
        layout.childControlHeight=layout.childControlWidth=true; layout.childForceExpandHeight=false;
        var title=Text(root,"Title",compact?"VITALS":"PLAYER",compact?17:20,GameFontRole.Heading);
        Height(title.rectTransform,compact?22:28);
        view.Health=ResourceBarView.Create(root,"Health",StatusSymbol.Health,new Color(.68f,.21f,.19f),compact);
        view.Elixir=ResourceBarView.Create(root,"Elixir",StatusSymbol.Elixir,new Color(.49f,.34f,.73f),compact);
        var stats=Rect("Attributes",root); Height(stats,compact?81:50);
        if (compact)
        {
            var rows=stats.gameObject.AddComponent<VerticalLayoutGroup>(); rows.spacing=2;
            rows.childControlHeight=rows.childControlWidth=true;
        }
        else
        {
            var rows=stats.gameObject.AddComponent<HorizontalLayoutGroup>(); rows.spacing=10;
            rows.childControlHeight=rows.childControlWidth=true;
        }
        view.shield=view.Stat(stats,"SHIELD",StatusSymbol.Shield,new Color(.52f,.74f,.83f),compact);
        view.speed=view.Stat(stats,"ATK / S",StatusSymbol.Speed,new Color(.91f,.76f,.41f),compact);
        view.damage=view.Stat(stats,"DAMAGE",StatusSymbol.Damage,new Color(.88f,.52f,.32f),compact);
        return view;
    }
    public void Bind(BattleScript source) { player=source; session=null; Refresh(); }
    public void Bind(RunSession source) { session=source; player=null; Refresh(); }
    public void ShowCost(BuffCards card) { PreviewCard=card; Refresh(); }
    public void ClearCost(BuffCards card) { if (PreviewCard==card) { PreviewCard=null; Refresh(); } }
    private void LateUpdate()=>Refresh();
    private void OnDisable() { PreviewCard=null; }
    private void Refresh()
    {
        if (player==null && session==null) return;
        bool combat=player!=null;
        float hp=combat?player.health:session.PlayerHealth, maxHp=combat?player.maxHealth:session.PlayerMaxHealth;
        float energy=combat?player.elixir:session.PlayerElixir, maxEnergy=combat?player.maxElixir:session.PlayerMaxElixir;
        bool preview=combat && hp>0 && PreviewCard!=null && PreviewCard.isActiveAndEnabled;
        int cost=preview?PreviewCard.Cost:0;
        bool healthCost=preview && PreviewCard.UsesHealthCost;
        bool affordable=!preview || PreviewCard.MeetsResourceRequirement;
        Health.SetValue(hp,maxHp,cost,healthCost,affordable && hp>cost);
        Elixir.SetValue(energy,maxEnergy,cost,preview && !healthCost,affordable);
        if (healthCost && !affordable) Health.Hint.text=$"Requires {cost} Health to play";
        else if (healthCost && hp <= cost) Health.Hint.text=$"-{cost} Health  |  Lethal cost";
        int shieldValue=combat?player.shield:session.PlayerShield, damageValue=combat?player.attackDmg:session.PlayerDamage;
        float interval=combat?player.attackSpd:session.PlayerAttackSpeed;
        if (shieldValue!=oldShield) { oldShield=shieldValue; shield.text=Mathf.Max(0,shieldValue).ToString(); }
        if (damageValue!=oldDamage) { oldDamage=damageValue; damage.text=damageValue.ToString(); }
        if (interval!=oldInterval) { oldInterval=interval; speed.text=$"{1f/Mathf.Max(.01f,interval):0.00}"; }
    }
    private TMP_Text Stat(Transform parent,string title,StatusSymbol symbol,Color tint,bool compact)
    {
        var row=Rect(title,parent); Height(row,compact?25:50); row.GetComponent<LayoutElement>().flexibleWidth=1;
        var icon=Icon(row,symbol,tint);
        Place(icon.rectTransform,compact?new Vector2(0,.5f):new Vector2(.25f,.68f),compact?new Vector2(11,0):Vector2.zero,new Vector2(24,24));
        var label=Text(row,"Label",title,13,GameFontRole.Heading);
        Place(label.rectTransform,compact?new Vector2(0,.5f):new Vector2(.5f,0),compact?new Vector2(29,0):new Vector2(0,8),
            new Vector2(compact?100:105,18),compact?new Vector2(0,.5f):new Vector2(.5f,.5f));
        label.alignment=compact?TextAlignmentOptions.Left:TextAlignmentOptions.Center;
        var number=Text(row,"Value","--",compact?22:26,GameFontRole.Numeric);
        Place(number.rectTransform,compact?new Vector2(1,.5f):new Vector2(.7f,.68f),Vector2.zero,
            new Vector2(compact?60:58,30),compact?new Vector2(1,.5f):new Vector2(.5f,.5f));
        number.alignment=compact?TextAlignmentOptions.Right:TextAlignmentOptions.Center; return number;
    }
    public static RectTransform Rect(string name,Transform parent)
    {
        var root=(RectTransform)new GameObject(name,typeof(RectTransform)).transform;
        root.SetParent(parent,false); root.gameObject.layer=parent.gameObject.layer; return root;
    }
    public static Image Box(string name,Transform parent,Color color)
    {
        var image=Rect(name,parent).gameObject.AddComponent<Image>(); image.color=color; image.raycastTarget=false; return image;
    }
    public static TMP_Text Text(Transform parent,string name,string content,float size,GameFontRole role)
    {
        var text=Rect(name,parent).gameObject.AddComponent<TextMeshProUGUI>(); GameFonts.Apply(text,role);
        text.text=content; text.fontSize=size; text.color=Cream; text.raycastTarget=false;
        text.alignment=TextAlignmentOptions.Center; text.textWrappingMode=TextWrappingModes.NoWrap; return text;
    }
    public static StatusIcon Icon(Transform parent,StatusSymbol symbol,Color color)
    {
        var icon=Rect("Icon",parent).gameObject.AddComponent<StatusIcon>(); icon.symbol=symbol; icon.color=color; icon.raycastTarget=false; return icon;
    }
    public static void Place(RectTransform rect,Vector2 anchor,Vector2 position,Vector2 size,Vector2? pivot=null)
    {
        rect.anchorMin=rect.anchorMax=anchor; rect.pivot=pivot??new Vector2(.5f,.5f); rect.anchoredPosition=position; rect.sizeDelta=size;
    }
    private static void Height(RectTransform rect,float height)
    {
        var layout=rect.gameObject.AddComponent<LayoutElement>(); layout.preferredHeight=layout.minHeight=height;
    }
}
