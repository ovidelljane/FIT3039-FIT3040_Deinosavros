using System;
using System.IO;
using UnityEditor;
using UnityEngine;

// Original authored symbols. No external images, stamps or texture sources are used.
public static class MapTravelArt
{
    public const string Root = "Assets/Art/Map";
    private const int Cell = 256, Width = Cell * 4, Height = Cell * 4;
    private static readonly Color Ink = new(.18f, .09f, .045f), Gold = new(.906f, .714f, .416f),
        Pale = new(1, .902f, .682f), Shadow = new(.65f, .38f, .14f);
    private static Color[] pixels;

    public static MapTravelProfile CreateAssets()
    {
        Directory.CreateDirectory(Root + "/Textures");
        Directory.CreateDirectory(Root + "/Models");
        Directory.CreateDirectory(Root + "/Settings");
        Directory.CreateDirectory(Root + "/Materials");
        string image = Root + "/Textures/TravelSymbols.png";
        var existingImporter = AssetImporter.GetAtPath(image) as TextureImporter;
        if (!File.Exists(image) || existingImporter == null || existingImporter.userData != "MapTravelSymbols v4")
        {
            pixels = new Color[Width * Height];
            DrawCore(); DrawLeaf(); DrawDiamond(); DrawMeander(); DrawSeal(); DrawCompleted(); DrawHalo(); DrawPassed();
            DrawEncounterIcons();
            var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            texture.SetPixels(pixels); texture.Apply(); File.WriteAllBytes(image, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture); pixels = null;
        }
        AssetDatabase.ImportAsset(image, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(image);
        importer.textureType = TextureImporterType.Default; importer.alphaIsTransparency = true; importer.sRGBTexture = true;
        importer.mipmapEnabled = true; importer.wrapMode = TextureWrapMode.Clamp; importer.filterMode = FilterMode.Trilinear;
        importer.textureCompression = TextureImporterCompression.Uncompressed; importer.maxTextureSize = 1024; importer.SaveAndReimport();
        importer.userData = "MapTravelSymbols v4"; importer.SaveAndReimport();
        var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(image);
        var shader = Shader.Find("Map/Travel Symbols");
        if (shader == null) throw new InvalidOperationException("Travel shader has not compiled.");
        Material symbols = Material("FireSeed", shader, atlas, 0, 1.05f, .06f);
        Material halo = Material("Halo", shader, atlas, 6, 1, .06f); halo.SetFloat("_Opacity", .19f);
        Material sigil = Material("Sigil", shader, atlas, 4, 1, 0); sigil.SetFloat("_Opacity", .35f);
        string quadPath = Root + "/Models/Quad.asset";
        var quad = AssetDatabase.LoadAssetAtPath<Mesh>(quadPath);
        if (quad == null)
        {
            quad = new Mesh { name = "Travel Quad", vertices = new[] { new Vector3(-.5f, -.5f, 0), new Vector3(.5f, -.5f, 0), new Vector3(.5f, .5f, 0), new Vector3(-.5f, .5f, 0) },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up },
                colors = new[] { Color.white, Color.white, Color.white, Color.white }, triangles = new[] { 0, 1, 2, 0, 2, 3 } };
            quad.RecalculateBounds(); AssetDatabase.CreateAsset(quad, quadPath);
        }
        string profilePath = Root + "/Settings/Travel.asset";
        var profile = AssetDatabase.LoadAssetAtPath<MapTravelProfile>(profilePath);
        if (profile == null) { profile = ScriptableObject.CreateInstance<MapTravelProfile>(); AssetDatabase.CreateAsset(profile, profilePath); }
        profile.symbolMaterial = symbols; profile.glowMaterial = halo; profile.quadMesh = quad;
        profile.nodeInformation = CreateNodeInformation();
        EditorUtility.SetDirty(profile); EditorUtility.SetDirty(symbols); EditorUtility.SetDirty(halo); EditorUtility.SetDirty(sigil);
        AssetDatabase.SaveAssetIfDirty(profile); AssetDatabase.SaveAssetIfDirty(symbols); AssetDatabase.SaveAssetIfDirty(halo); AssetDatabase.SaveAssetIfDirty(sigil);
        return profile;
    }
    private static MapNodeCatalog CreateNodeInformation()
    {
        string path = Root + "/Settings/Nodes.asset";
        var catalog = AssetDatabase.LoadAssetAtPath<MapNodeCatalog>(path);
        if (catalog != null) return catalog;
        catalog = ScriptableObject.CreateInstance<MapNodeCatalog>();
        var graph = AssetDatabase.LoadAssetAtPath<Deinosavros.MapReview.MapGraphDefinition>(Root + "/Settings/Graph.asset");
        var entries = new System.Collections.Generic.List<MapNodeInfo>();
        foreach (var node in graph.nodes)
        {
            entries.Add(new MapNodeInfo {
                nodeId = node.id, kind = node.boss ? MapEncounterKind.Boss : MapEncounterKind.Battle,
                title = node.boss ? "The Sanctuary" : node.layer == 1 ? "Level 1 - The Entrance" : "Level " + node.layer + " - Path " + int.Parse(node.id.Substring(node.id.Length - 2)),
                summary = node.boss ? "The final destination. Enter the portal to face this adventure's last encounter." : "Defeat the enemies to complete this level and open its connected paths.",
                enemies = "", rewards = ""
            });
        }
        catalog.nodes = entries.ToArray(); AssetDatabase.CreateAsset(catalog, path); AssetDatabase.SaveAssetIfDirty(catalog);
        return catalog;
    }
    private static Material Material(string name, Shader shader, Texture2D atlas, int glyph, float emission, float softness)
    {
        string path = Root + "/Materials/" + name + ".mat"; var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null) { material = new Material(shader) { name = name }; AssetDatabase.CreateAsset(material, path); }
        material.shader = shader; material.SetTexture("_BaseMap", atlas); material.SetVector("_AtlasRect", MapTravelView.AtlasRect(glyph));
        material.SetColor("_Tint", Color.white); material.SetFloat("_Opacity", 1); material.SetFloat("_Emission", emission);
        material.SetFloat("_SoftDepth", softness); material.SetFloat("_Mode", 0); return material;
    }
    private static void DrawCore()
    {
        Vector2[] border = { new(.57f,.95f), new(.59f,.77f), new(.76f,.84f), new(.72f,.65f), new(.88f,.51f), new(.86f,.32f),
            new(.67f,.14f), new(.49f,.055f), new(.25f,.2f), new(.12f,.39f), new(.18f,.56f), new(.32f,.71f), new(.37f,.58f), new(.47f,.79f) };
        Polygon(0, border, Gold); Stroke(0, border, Ink, .043f, true);
        Polygon(0, new[] { new Vector2(.31f,.64f), new Vector2(.23f,.41f), new Vector2(.37f,.24f), new Vector2(.51f,.13f), new Vector2(.5f,.075f), new Vector2(.27f,.22f), new Vector2(.15f,.39f) }, Shadow);
        Vector2[] flame = { new(.54f,.77f), new(.52f,.58f), new(.64f,.65f), new(.62f,.49f), new(.73f,.39f), new(.67f,.26f), new(.5f,.18f),
            new(.36f,.29f), new(.32f,.4f), new(.4f,.52f), new(.43f,.42f) };
        Polygon(0, flame, Pale);
        Vector2[] gem = { new(.5f,.46f), new(.62f,.33f), new(.5f,.2f), new(.39f,.33f) };
        Polygon(0, gem, Gold); Stroke(0, gem, Ink, .024f, true);
        Stroke(0, new[] { new Vector2(.69f,.22f), new Vector2(.78f,.33f), new Vector2(.8f,.45f) }, Pale, .025f, false);
    }
    private static void DrawEncounterIcons()
    {
        var frame = new[] { new Vector2(.3f,.94f), new Vector2(.72f,.94f), new Vector2(.94f,.7f), new Vector2(.94f,.3f),
            new Vector2(.7f,.065f), new Vector2(.3f,.065f), new Vector2(.06f,.3f), new Vector2(.06f,.7f) };
        for (int glyph = 8; glyph <= 11; glyph++)
        {
            Polygon(glyph, frame, Ink); Stroke(glyph, frame, Pale, .055f, true);
            Vector2[] inset = new Vector2[frame.Length];
            for (int i = 0; i < frame.Length; i++) inset[i] = (frame[i] - Vector2.one * .5f) * .82f + Vector2.one * .5f;
            Stroke(glyph, inset, Gold, .018f, true);
        }
        Vector2[] sword = { new(.28f,.25f), new(.36f,.22f), new(.78f,.72f), new(.78f,.8f), new(.7f,.79f) };
        Polygon(8, sword, Pale);
        for (int i = 0; i < sword.Length; i++) sword[i].x = 1 - sword[i].x;
        Polygon(8, sword, Gold);
        Stroke(8, new[] { new Vector2(.2f,.43f), new Vector2(.44f,.22f) }, Gold, .05f, false);
        Stroke(8, new[] { new Vector2(.56f,.22f), new Vector2(.8f,.43f) }, Pale, .05f, false);
        Polygon(9, new[] { new Vector2(.26f,.35f), new Vector2(.2f,.69f), new Vector2(.39f,.58f), new Vector2(.51f,.79f), new Vector2(.62f,.58f), new Vector2(.81f,.69f), new Vector2(.74f,.35f) }, Gold);
        Stroke(9, new[] { new Vector2(.29f,.26f), new Vector2(.73f,.26f) }, Pale, .055f, false);
        Polygon(9, new[] { new Vector2(.51f,.6f), new Vector2(.6f,.49f), new Vector2(.51f,.39f), new Vector2(.42f,.49f) }, Ink);
        var card = new[] { new Vector2(.27f,.24f), new Vector2(.58f,.18f), new Vector2(.68f,.71f), new Vector2(.37f,.77f) };
        Polygon(10, card, Gold); Stroke(10, card, Pale, .026f, true);
        Stroke(10, new[] { new Vector2(.39f,.27f), new Vector2(.68f,.27f), new Vector2(.68f,.65f) }, Gold, .035f, false);
        Polygon(10, new[] { new Vector2(.72f,.84f), new Vector2(.76f,.74f), new Vector2(.86f,.7f), new Vector2(.76f,.66f), new Vector2(.72f,.56f), new Vector2(.68f,.66f), new Vector2(.58f,.7f), new Vector2(.68f,.74f) }, Pale);
        Polygon(10, new[] { new Vector2(.47f,.6f), new Vector2(.55f,.48f), new Vector2(.46f,.36f), new Vector2(.39f,.48f) }, Ink);
        var heart = new[] { new Vector2(.5f,.32f), new Vector2(.25f,.55f), new Vector2(.23f,.68f), new Vector2(.3f,.78f), new Vector2(.41f,.79f), new Vector2(.5f,.7f), new Vector2(.59f,.79f), new Vector2(.7f,.78f), new Vector2(.77f,.68f), new Vector2(.75f,.55f) };
        Polygon(11, heart, new Color(.58f,.78f,.47f)); Stroke(11, heart, Pale, .025f, true);
        Stroke(11, new[] { new Vector2(.25f,.4f), new Vector2(.38f,.24f), new Vector2(.65f,.2f) }, Gold, .03f, false);
        Polygon(11, new[] { new Vector2(.37f,.26f), new Vector2(.23f,.28f), new Vector2(.2f,.41f), new Vector2(.34f,.36f) }, Gold);
        Polygon(11, new[] { new Vector2(.49f,.23f), new Vector2(.61f,.33f), new Vector2(.74f,.29f), new Vector2(.62f,.21f) }, Gold);
    }
    private static void DrawLeaf()
    {
        Vector2[] outline = { new(.22f,.16f), new(.27f,.48f), new(.4f,.74f), new(.79f,.91f), new(.76f,.62f), new(.63f,.36f), new(.37f,.21f) };
        Polygon(1, outline, Gold); Stroke(1, outline, Ink, .027f, true);
        Stroke(1, new[] { new Vector2(.22f,.13f), new Vector2(.48f,.48f), new Vector2(.75f,.86f) }, Pale, .026f, false);
        Stroke(1, new[] { new Vector2(.39f,.4f), new Vector2(.35f,.6f) }, Shadow, .016f, false);
        Stroke(1, new[] { new Vector2(.54f,.56f), new Vector2(.7f,.61f) }, Shadow, .016f, false);
    }
    private static void DrawDiamond()
    {
        Vector2[] p = { new(.5f,.84f), new(.8f,.5f), new(.5f,.15f), new(.19f,.5f) };
        Stroke(2, p, Ink, .067f, true); Stroke(2, p, Gold, .037f, true);
        Polygon(2, new[] { new Vector2(.5f,.63f), new Vector2(.62f,.5f), new Vector2(.5f,.36f), new Vector2(.37f,.5f) }, Pale);
        for (int i = 0; i < 4; i++) { float a = i * Mathf.PI * .5f; Dot(2, new Vector2(.5f,.5f) + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * .44f, .025f, Gold); }
    }
    private static void DrawMeander()
    {
        Vector2[] p = { new(0,.48f), new(.17f,.48f), new(.17f,.73f), new(.62f,.73f), new(.62f,.32f), new(.36f,.32f), new(.36f,.53f), new(.46f,.53f), new(.46f,.42f), new(.54f,.42f), new(.54f,.61f), new(.25f,.61f), new(.25f,.43f), new(.86f,.43f), new(.86f,.55f), new(1,.55f) };
        Stroke(3, p, Ink, .14f, false); Stroke(3, p, Gold, .075f, false);
    }
    private static void DrawSeal()
    {
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI / 4;
            Vector2 P(float angle, float radius) => new Vector2(.5f,.5f) + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            var p = new[] { P(a+.06f,.39f), P(a+.28f,.39f), P(a+.28f,.3f), P(a+.5f,.3f), P(a+.5f,.4f), P(a+.65f,.4f) };
            Stroke(4, p, Ink, .07f, false); Stroke(4, p, Gold, .043f, false);
        }
    }
    private static void DrawCompleted()
    {
        Vector2[] p = { new(.5f,.78f), new(.75f,.5f), new(.5f,.21f), new(.23f,.5f) };
        Polygon(5, p, Gold); Stroke(5, p, Ink, .028f, true);
        Stroke(5, new[] { new Vector2(.34f,.5f), new Vector2(.47f,.37f), new Vector2(.66f,.62f) }, Pale, .065f, false);
        for (int i = 0; i < 10; i++)
        { float a = (i + .5f) * Mathf.PI * 2 / 10; Dot(5, new Vector2(.5f,.5f) + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * .39f, .016f, Gold); }
    }
    private static void DrawHalo()
    {
        for (int y = 0; y < Cell; y++) for (int x = 0; x < Cell; x++)
        {
            float r = Vector2.Distance(new Vector2(x/(float)Cell,y/(float)Cell), new Vector2(.5f,.5f)) / .5f;
            if (r >= 1) continue;
            Color c = Gold; c.a = Mathf.Pow(1 - r * r, 3) * .62f; Put(6, x, y, c);
        }
    }
    private static void DrawPassed()
    {
        Stroke(7, new[] { new Vector2(.33f,.65f), new Vector2(.65f,.34f) }, Ink, .045f, false);
        Stroke(7, new[] { new Vector2(.33f,.34f), new Vector2(.64f,.66f) }, Gold, .024f, false);
    }
    private static void Polygon(int glyph, Vector2[] shape, Color color)
    {
        for (int y = 0; y < Cell; y++) for (int x = 0; x < Cell; x++)
        {
            Vector2 p = new((x+.5f)/Cell, (y+.5f)/Cell); bool inside = false;
            for (int i = 0, j = shape.Length - 1; i < shape.Length; j = i++)
                if ((shape[i].y > p.y) != (shape[j].y > p.y) && p.x < (shape[j].x-shape[i].x)*(p.y-shape[i].y)/(shape[j].y-shape[i].y)+shape[i].x) inside = !inside;
            if (inside) Put(glyph, x, y, color);
        }
    }
    private static void Stroke(int glyph, Vector2[] points, Color color, float width, bool closed)
    {
        int count = closed ? points.Length : points.Length - 1;
        for (int i = 0; i < count; i++)
        {
            Vector2 a = points[i], b = points[(i + 1) % points.Length]; float length = Vector2.Distance(a,b);
            int steps = Mathf.Max(2, Mathf.CeilToInt(length * Cell * 1.4f));
            for (int j = 0; j <= steps; j++) Dot(glyph, Vector2.Lerp(a,b,j/(float)steps), width * .5f, color);
        }
    }
    private static void Dot(int glyph, Vector2 p, float radius, Color color)
    {
        int minX = Mathf.Max(0, Mathf.FloorToInt((p.x-radius)*Cell)-1), maxX = Mathf.Min(Cell-1,Mathf.CeilToInt((p.x+radius)*Cell)+1);
        int minY = Mathf.Max(0, Mathf.FloorToInt((p.y-radius)*Cell)-1), maxY = Mathf.Min(Cell-1,Mathf.CeilToInt((p.y+radius)*Cell)+1);
        for (int y=minY;y<=maxY;y++) for(int x=minX;x<=maxX;x++)
        {
            float distance = Vector2.Distance(new Vector2((x+.5f)/Cell,(y+.5f)/Cell),p);
            Color c = color; c.a *= Mathf.Clamp01((radius-distance)*Cell+.5f); if(c.a>0) Put(glyph,x,y,c);
        }
    }
    private static void Put(int glyph, int x, int y, Color c)
    {
        int index = ((glyph/4)*Cell+y)*Width+(glyph%4)*Cell+x;
        Color old = pixels[index]; float alpha = c.a + old.a*(1-c.a);
        pixels[index] = alpha <= 0 ? Color.clear : new Color((c.r*c.a+old.r*old.a*(1-c.a))/alpha,
            (c.g*c.a+old.g*old.a*(1-c.a))/alpha,(c.b*c.a+old.b*old.a*(1-c.a))/alpha,alpha);
    }
}
