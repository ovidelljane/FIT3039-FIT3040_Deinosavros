using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class ResourceBarView : MonoBehaviour
{
    [Header("Scene References")]
    [SerializeField] private Image fill, preview, marker;
    [SerializeField] private TMP_Text hint, number;
    [SerializeField] private string resource;
    [Header("Cost Preview")]
    [SerializeField] private Color affordableColor = new Color(1f, .91f, .74f);
    [SerializeField] private Color unaffordableColor = new Color(1f, .27f, .21f);
    [SerializeField] private Color normalHintColor = new Color(1f, .91f, .74f);
    [SerializeField] private Color warningHintColor = new Color(1f, .47f, .35f);
    public Image Fill { get => fill; private set => fill = value; }
    public Image Preview { get => preview; private set => preview = value; }
    public TMP_Text Hint { get => hint; private set => hint = value; }
    public bool IsReady => Fill != null && Preview != null && Hint != null && number != null && marker != null;
    private float previousValue = float.NaN, previousMaximum;
    private int previousCost = -1;
    private bool previousAffordable, previousActive;

    public static ResourceBarView Create(Transform parent,string resource,StatusSymbol symbol,Color tint,bool compact)
    {
        var root = PlayerStatusView.Rect(resource,parent);
        var layout = root.gameObject.AddComponent<LayoutElement>(); layout.preferredHeight = compact ? 42 : 62;
        layout.minHeight = layout.preferredHeight;
        var view = root.gameObject.AddComponent<ResourceBarView>(); view.resource = resource;
        var icon = PlayerStatusView.Icon(root,symbol,tint);
        PlayerStatusView.Place(icon.rectTransform,new Vector2(0,1),new Vector2(11,-10),new Vector2(22,22));
        var label = PlayerStatusView.Text(root,"Label",resource.ToUpperInvariant(),14,GameFontRole.Heading);
        label.alignment = TextAlignmentOptions.Left;
        PlayerStatusView.Place(label.rectTransform,new Vector2(0,1),new Vector2(28,-10),new Vector2(90,20),new Vector2(0,.5f));
        view.number = PlayerStatusView.Text(root,"Value","",compact ? 19 : 20,GameFontRole.Numeric);
        view.number.alignment = TextAlignmentOptions.Right;
        PlayerStatusView.Place(view.number.rectTransform,new Vector2(1,1),new Vector2(0,-10),new Vector2(94,22),new Vector2(1,.5f));
        var track = PlayerStatusView.Box("Track",root,new Color(.08f,.065f,.06f));
        track.rectTransform.anchorMin = new Vector2(0,1); track.rectTransform.anchorMax = Vector2.one;
        track.rectTransform.pivot = new Vector2(.5f,1); track.rectTransform.anchoredPosition = new Vector2(0,-25);
        track.rectTransform.sizeDelta = new Vector2(0,compact ? 14 : 19);
        var edge = track.gameObject.AddComponent<Outline>(); edge.effectColor = PlayerStatusView.Gold; edge.effectDistance = new Vector2(1,-1);
        view.Fill = PlayerStatusView.Box("Fill",track.transform,tint); Stretch(view.Fill.rectTransform,0,1);
        var gleam = PlayerStatusView.Box("Gleam",view.Fill.transform,new Color(1,1,1,.16f));
        gleam.rectTransform.anchorMin = new Vector2(0,.7f); gleam.rectTransform.anchorMax = Vector2.one;
        gleam.rectTransform.offsetMin = gleam.rectTransform.offsetMax = Vector2.zero;
        view.Preview = PlayerStatusView.Box("CostPreview",track.transform,PlayerStatusView.Cream);
        view.marker = PlayerStatusView.Box("AfterCost",track.transform,PlayerStatusView.Ink);
        view.marker.rectTransform.sizeDelta = new Vector2(2,compact ? 18 : 23);
        view.Hint = PlayerStatusView.Text(root,"CostHint","",15,GameFontRole.Body);
        view.Hint.alignment = TextAlignmentOptions.Right;
        view.Hint.rectTransform.anchorMin = Vector2.zero; view.Hint.rectTransform.anchorMax = new Vector2(1,0);
        view.Hint.rectTransform.pivot = new Vector2(.5f,0); view.Hint.rectTransform.sizeDelta = new Vector2(0,18);
        view.Hint.gameObject.SetActive(!compact);
        view.Preview.gameObject.SetActive(false); view.marker.gameObject.SetActive(false);
        return view;
    }
    public void SetValue(float current,float maximum,int cost=0,bool preview=false,bool affordable=true)
    {
        if (!IsReady) return;
        current = Mathf.Clamp(current,0,Mathf.Max(0,maximum));
        float end = maximum > 0 ? current/maximum : 0;
        Stretch(Fill.rectTransform,0,end);
        float start = maximum > 0 ? Mathf.Max(0,current-cost)/maximum : 0;
        bool visible = preview && cost > 0 && end > start;
        Preview.gameObject.SetActive(visible); marker.gameObject.SetActive(visible);
        if (visible)
        {
            Stretch(Preview.rectTransform,start,end);
            marker.rectTransform.anchorMin = marker.rectTransform.anchorMax = new Vector2(start,.5f);
            marker.rectTransform.anchoredPosition = Vector2.zero;
            Color tint = affordable ? affordableColor : unaffordableColor;
            tint.a = .5f + .2f*Mathf.Sin(Time.unscaledTime*4f); Preview.color = tint;
        }
        if (current == previousValue && maximum == previousMaximum && cost == previousCost &&
            preview == previousActive && affordable == previousAffordable) return;
        previousValue=current; previousMaximum=maximum; previousCost=cost; previousActive=preview; previousAffordable=affordable;
        number.text = resource == "Health" ? $"{current:0} / {maximum:0}" : $"{current:0.0} / {maximum:0}";
        Hint.color = affordable ? normalHintColor : warningHintColor;
        Hint.text = !preview ? "" : cost == 0 ? "No cost" : !affordable && current < cost
            ? $"Need {cost}  |  Short {cost-current:0.0}"
            : $"-{cost} {resource}  |  {Mathf.Max(0,current-cost):0.#} remaining";
    }
    private static void Stretch(RectTransform rect,float left,float right)
    {
        rect.anchorMin=new Vector2(left,0); rect.anchorMax=new Vector2(right,1);
        rect.offsetMin=rect.offsetMax=Vector2.zero;
    }
}
