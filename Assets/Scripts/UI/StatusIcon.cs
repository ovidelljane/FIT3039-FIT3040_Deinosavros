using UnityEngine;
using UnityEngine.UI;

public enum StatusSymbol { Health, Elixir, Shield, Speed, Damage }

// Original vector silhouettes; no external texture dependencies.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class StatusIcon : MaskableGraphic
{
    public StatusSymbol symbol;
    private static readonly Vector2[] Heart = { new(0,.22f), new(.15f,.38f), new(.32f,.35f), new(.43f,.2f),
        new(.4f,.02f), new(.24f,-.18f), new(0,-.4f), new(-.24f,-.18f), new(-.4f,.02f), new(-.43f,.2f), new(-.32f,.35f), new(-.15f,.38f) };
    private static readonly Vector2[] Flask = { new(-.16f,.4f), new(.16f,.4f), new(.16f,.12f), new(.37f,-.18f),
        new(.32f,-.38f), new(-.32f,-.38f), new(-.37f,-.18f), new(-.16f,.12f) };
    private static readonly Vector2[] Shield = { new(0,.42f), new(.37f,.25f), new(.29f,-.15f), new(0,-.43f), new(-.29f,-.15f), new(-.37f,.25f) };
    private static readonly Vector2[] Wing = { new(-.35f,-.34f), new(-.24f,.08f), new(.35f,.4f), new(.29f,.12f),
        new(.05f,.04f), new(.28f,0), new(.13f,-.18f), new(-.04f,-.21f), new(.06f,-.31f) };
    private static readonly Vector2[] Blade = { new(-.07f,-.2f), new(-.07f,.24f), new(0,.45f), new(.07f,.24f), new(.07f,-.2f) };
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Vector2[] shape = symbol == StatusSymbol.Health ? Heart : symbol == StatusSymbol.Elixir ? Flask :
            symbol == StatusSymbol.Shield ? Shield : symbol == StatusSymbol.Speed ? Wing : Blade;
        Polygon(vh,shape,1,new Color(.09f,.06f,.035f,color.a)); Polygon(vh,shape,.83f,color);
        if (symbol == StatusSymbol.Damage)
        { Line(vh,new(-.22f,-.15f),new(.22f,-.15f),.065f,color); Line(vh,new(0,-.18f),new(0,-.4f),.075f,color); }
        if (symbol == StatusSymbol.Elixir) Line(vh,new(-.17f,-.1f),new(.17f,-.1f),.04f,new Color(1,.91f,.74f));
        if (symbol == StatusSymbol.Speed) Line(vh,new(-.22f,-.23f),new(.24f,.24f),.035f,new Color(1,.91f,.74f));
    }
    private Vector3 Point(Vector2 p) => (Vector3)(rectTransform.rect.center + p * Mathf.Min(rectTransform.rect.width,rectTransform.rect.height));
    private void Polygon(VertexHelper vh,Vector2[] points,float scale,Color tint)
    {
        int start = vh.currentVertCount;
        vh.AddVert(Point(new Vector2(0,-.03f)*scale),tint,Vector2.zero);
        foreach (var p in points) vh.AddVert(Point(p*scale),tint,Vector2.zero);
        for (int i=0;i<points.Length;i++) vh.AddTriangle(start,start+i+1,start+(i+1)%points.Length+1);
    }
    private void Line(VertexHelper vh,Vector2 a,Vector2 b,float width,Color tint)
    {
        Vector2 n = new Vector2(-(b-a).y,(b-a).x).normalized * width * .5f;
        int start = vh.currentVertCount;
        vh.AddVert(Point(a+n),tint,Vector2.zero); vh.AddVert(Point(b+n),tint,Vector2.zero);
        vh.AddVert(Point(b-n),tint,Vector2.zero); vh.AddVert(Point(a-n),tint,Vector2.zero);
        vh.AddTriangle(start,start+1,start+2); vh.AddTriangle(start,start+2,start+3);
    }
}
