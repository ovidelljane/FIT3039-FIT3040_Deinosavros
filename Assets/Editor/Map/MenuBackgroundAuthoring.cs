using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Uses the supplied layered artwork; no generated textures or runtime UI construction.
public static class MenuBackgroundAuthoring
{
    private const string ScenePath = "Assets/Scenes/MainMenu.unity";
    private const string Results = "Library/MenuBackground";
    private const string EmberMaterial = "Assets/UI/MenuEmbers.mat";
    private static readonly string[] Artwork = { "Assets/UI/Untitled_Artwork.png", "Assets/UI/IMG_1602.png", "Assets/UI/IMG_1603.png" };

    [MenuItem("Tools/UI/Apply Animated Menu Background")]
    public static void ApplyEmbers()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Stop Play Mode before updating the menu material.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            Require(!SceneManager.GetSceneAt(i).isDirty, "Save your scene changes before updating the menu material.");
        var setup = EditorSceneManager.GetSceneManagerSetup();
        Directory.CreateDirectory(Results);
        try
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/UI/MenuEmbers.shader");
            Require(shader != null && !ShaderUtil.ShaderHasError(shader), "The menu ember shader must compile.");
            var material = AssetDatabase.LoadAssetAtPath<Material>(EmberMaterial);
            if (material == null)
            {
                material = new Material(shader) { name = "MenuEmbers" };
                AssetDatabase.CreateAsset(material, EmberMaterial); AssetDatabase.SaveAssets();
            }
            Require(material.shader == shader && material.GetFloat("_PreviewTime") < 0,
                "Use the live menu shader clock in the saved material.");
            string backup = Results + "/Menu-before-embers.unity";
            if (!File.Exists(backup)) File.Copy(ScenePath, backup);
            string snapshot = null;
            for (int pass = 0; pass < 2; pass++)
            {
                var scene = EditorSceneManager.OpenScene(ScenePath);
                var background = MenuCanvas(scene).transform.Find("Background").GetComponent<Image>();
                if (background.material != material)
                {
                    Capture(scene, new Vector2Int(1920,1080), "embers-before");
                    background.material = material;
                    EditorSceneManager.MarkSceneDirty(scene);
                    Require(EditorSceneManager.SaveScene(scene), "Could not save the menu material binding.");
                }
                string saved = File.ReadAllText(ScenePath);
                if (pass == 0) snapshot = saved;
                else Require(saved == snapshot, "Reapplying the shader cannot change menu objects or layout.");
            }
            var reopened = EditorSceneManager.OpenScene(ScenePath);
            Validate(reopened);
            var image = MenuCanvas(reopened).transform.Find("Background").GetComponent<Image>();
            Require(image.material == material && !image.raycastTarget, "The background must retain its material and ignore clicks.");
            Require(MenuCanvas(reopened).GetComponentsInChildren<Graphic>(true).Count(g => g.material == material) == 1,
                "Only the background artwork may use the effect, not characters, buttons or text.");
            VerifyEmberFrames(reopened, image);
            File.WriteAllText(Results + "/embers-authoring.txt",
                "PASS: background-only material, saved and reopened, exact repeat application, original art retained, three resolutions and changing shader pixels.\n");
        }
        finally
        {
            if (setup.Any(s => s.isLoaded)) EditorSceneManager.RestoreSceneManagerSetup(setup);
            else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
    }

    private static Canvas MenuCanvas(Scene scene) => scene.GetRootGameObjects()
        .SelectMany(r => r.GetComponentsInChildren<Canvas>(true)).Single(c => c.isRootCanvas);

    private static void VerifyEmberFrames(Scene scene, Image background)
    {
        var original = background.material;
        var preview = new Material(original);
        var size = new Vector2Int(1920,1080);
        try
        {
            background.material = preview;
            preview.SetFloat("_PreviewTime", 0);
            var a = Capture(scene, size, "embers-00");
            preview.SetFloat("_PreviewTime", 6);
            var b = Capture(scene, size, "embers-06");
            Require(ChangedPixels(a,b) > 10000, "The effect must visibly flow, not just change a property.");
            int centerChanges = 0;
            for (int y = 560; y < 750; y++) for (int x = 820; x < 1100; x++)
                if (Difference(a[y*1920+x], b[y*1920+x]) > 12) centerChanges++;
            Require(centerChanges < 500, "Keep the central title region calm.");
            preview.SetFloat("_PreviewTime", 18); Capture(scene, size, "embers-18");
            foreach (var resolution in new[] { new Vector2Int(2560,1440), new Vector2Int(1920,1200) })
                Capture(scene, resolution, "embers");
            Require(!ShaderUtil.ShaderHasError(preview.shader), "All rendered UI shader variants must compile.");

            preview.SetFloat("_Distortion",0); preview.SetFloat("_FireStrength",0); preview.SetFloat("_EmberStrength",0);
            var disabled = Capture(scene, size, "embers-disabled");
            background.material = null;
            var plain = Capture(scene, size, "embers-original");
            Require(ChangedPixels(disabled,plain) < 100, "Zero effect strengths must preserve the original artwork and UI colors.");
        }
        finally { background.material = original; UnityEngine.Object.DestroyImmediate(preview); }
    }

    public static IEnumerator VerifyRuntime(Action<bool,string> check)
    {
        var scene = SceneManager.GetActiveScene();
        var canvas = MenuCanvas(scene);
        var background = canvas.transform.Find("Background").GetComponent<Image>();
        check(background.material == AssetDatabase.LoadAssetAtPath<Material>(EmberMaterial) &&
            background.material.GetFloat("_PreviewTime") < 0, "The saved background uses the live shader clock.");
        var a = Capture(scene, new Vector2Int(1920,1080), "embers-live-a");
        double until = EditorApplication.timeSinceStartup + 2;
        while (EditorApplication.timeSinceStartup < until) yield return null;
        var b = Capture(scene, new Vector2Int(1920,1080), "embers-live-b");
        check(ChangedPixels(a,b) > 10000, "Real Play Mode time animates the background without a runtime UI builder.");
        check(!ShaderUtil.ShaderHasError(background.material.shader), "The runtime shader compiles without errors.");
        foreach (var button in canvas.GetComponentsInChildren<Button>())
        {
            var pointer = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(null,button.transform.position) };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer,hits);
            check(hits.Count > 0 && hits[0].gameObject.GetComponentInParent<Button>() == button,
                "The animated background does not block the menu action: " + button.name);
        }
        check(canvas.transform.Find("Background").childCount == 3 &&
            canvas.GetComponentsInChildren<Graphic>(true).Count(g => g.material == background.material) == 1,
            "The title and two character layers remain independent and unwarped.");
    }
    private static int Difference(Color32 a,Color32 b) => Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b);
    private static int ChangedPixels(Color32[] a,Color32[] b)
    {
        int changed = 0;
        for (int i=0;i<a.Length;i++) if (Difference(a[i],b[i]) > 12) changed++;
        return changed;
    }

    [MenuItem("Tools/UI/Apply Layered Menu Background")]
    public static void Apply()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Stop Play Mode before updating the menu.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            Require(!SceneManager.GetSceneAt(i).isDirty, "Save your scene changes before updating the menu.");
        Directory.CreateDirectory(Results);
        var setup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            foreach (string path in Artwork)
            {
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                Require(importer != null, "Missing supplied artwork: " + path);
                if (importer.textureType == TextureImporterType.Sprite && importer.spriteImportMode == SpriteImportMode.Single && importer.alphaIsTransparency) continue;
                importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true; importer.mipmapEnabled = false; importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var canvas = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Canvas>(true)).Single(c => c.isRootCanvas);
            var background = canvas.transform.Find("Background").GetComponent<Image>();
            Require(background != null, "Keep the existing menu background container.");
            if (!File.Exists(Results + "/Menu-before-background.unity")) File.Copy(ScenePath, Results + "/Menu-before-background.unity");
            bool changed = false;
            var paper = AssetDatabase.LoadAssetAtPath<Sprite>(Artwork[0]);
            Require(paper != null, "The background must import as a single UI sprite.");
            if (background.sprite != paper) { background.sprite = paper; background.color = Color.white; changed = true; }
            if (background.raycastTarget) { background.raycastTarget = false; changed = true; }
            if (background.transform.Find("Left") == null)
            {
                AddCharacter(background.transform, "Left", Artwork[1], new Vector2(0, 0), new Vector2(190, 460), 680, false);
                changed = true;
            }
            else if (((RectTransform)background.transform.Find("Left")).anchoredPosition == new Vector2(190, 510))
            { ((RectTransform)background.transform.Find("Left")).anchoredPosition = new Vector2(190, 460); changed = true; }
            if (background.transform.Find("Right") == null)
            {
                AddCharacter(background.transform, "Right", Artwork[2], new Vector2(1, 0), new Vector2(-180, 580), 850, true);
                changed = true;
            }
            if (background.transform.Find("Title") == null)
            {
                var title = PlayerStatusView.Rect("Title", background.transform).gameObject.AddComponent<TextMeshProUGUI>();
                title.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Resources/Fonts/Cinzel-Black SDF.asset");
                title.text = "Dein\u00f3savros"; title.fontSize = 90; title.alignment = TextAlignmentOptions.Center;
                title.color = new Color(.68f, .34f, .07f); title.raycastTarget = false; title.textWrappingMode = TextWrappingModes.NoWrap;
                PlayerStatusView.Place(title.rectTransform, new Vector2(.5f, .5f), new Vector2(0, 120), new Vector2(850, 150));
                var shadow = title.gameObject.AddComponent<Shadow>(); shadow.effectColor = new Color(.22f, .13f, .045f, .65f);
                shadow.effectDistance = new Vector2(3, -4); changed = true;
            }
            if (changed) { Canvas.ForceUpdateCanvases(); EditorSceneManager.MarkSceneDirty(scene); Require(EditorSceneManager.SaveScene(scene), "Could not save the menu artwork."); }
            scene = EditorSceneManager.OpenScene(ScenePath); Validate(scene);
            File.WriteAllText(Results + "/authoring.txt", "PASS: supplied background and two transparent character sprites saved in Canvas/Background. Existing buttons and actions preserved.\n");
            Capture(scene);
        }
        finally
        {
            if (setup.Any(s => s.isLoaded)) EditorSceneManager.RestoreSceneManagerSetup(setup);
            else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
    }

    public static void ApplyAndVerify()
    {
        Apply(); string snapshot = File.ReadAllText(ScenePath); Apply();
        Require(snapshot == File.ReadAllText(ScenePath), "Repeated application changed the menu layout.");
        File.AppendAllText(Results + "/authoring.txt", "PASS: repeated application preserves exact scene bytes.\n");
    }

    private static void AddCharacter(Transform parent, string name, string path, Vector2 anchor, Vector2 position, float width, bool mirror)
    {
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path); Require(sprite != null, "Missing menu character sprite.");
        var image = PlayerStatusView.Box(name, parent, Color.white); image.sprite = sprite; image.preserveAspect = true;
        PlayerStatusView.Place(image.rectTransform, anchor, position, new Vector2(width, width * sprite.rect.height / sprite.rect.width));
        if (mirror) image.rectTransform.localScale = new Vector3(-1, 1, 1);
    }

    private static void Validate(Scene scene)
    {
        var canvas = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Canvas>(true)).Single(c => c.isRootCanvas);
        var background = canvas.transform.Find("Background");
        Require(background.GetSiblingIndex() == 0, "The artwork must stay behind all menu buttons.");
        foreach (var image in background.GetComponentsInChildren<Image>(true))
            Require(image.sprite != null && AssetDatabase.Contains(image.sprite) && !image.raycastTarget, "Every decorative menu layer must have an imported sprite and ignore input.");
        Require(background.childCount == 3 && background.Find("Title").GetComponent<TMP_Text>().text == "Dein\u00f3savros", "Keep one title and two independent character layers.");
        Require(canvas.GetComponentsInChildren<Button>(true).Length == 3, "Do not alter the three existing menu actions.");
    }

    private static void Capture(Scene scene)
    {
        foreach (var size in new[] { new Vector2Int(1920,1080), new Vector2Int(2560,1440), new Vector2Int(1920,1200) })
            Capture(scene,size,"menu");
    }
    private static Color32[] Capture(Scene scene, Vector2Int size, string label)
    {
        Directory.CreateDirectory(Results);
        var canvas = MenuCanvas(scene);
        var camera = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>(true)).Single(c => c.CompareTag("MainCamera"));
        {
            var rt = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGB32);
            var target = camera.targetTexture; var active = RenderTexture.active; float aspect = camera.aspect;
            var mode = canvas.renderMode; var previousCamera = canvas.worldCamera; float plane = canvas.planeDistance;
            Texture2D image = null;
            try
            {
                rt.Create(); camera.targetTexture = rt; camera.aspect = (float)size.x / size.y;
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = camera.nearClipPlane + .5f;
                Canvas.ForceUpdateCanvases();
                var title = canvas.transform.Find("Background/Title").GetComponent<TMP_Text>(); title.ForceMeshUpdate();
                Require(!title.isTextOverflowing, "The menu title must fit at " + size);
                foreach (var button in canvas.GetComponentsInChildren<Button>())
                {
                    var corners = new Vector3[4]; ((RectTransform)button.transform).GetWorldCorners(corners);
                    foreach (var corner in corners)
                    { var vp = camera.WorldToViewportPoint(corner); Require(vp.x >= 0 && vp.x <= 1 && vp.y >= 0 && vp.y <= 1, "Keep menu actions inside the viewport."); }
                }
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
                RenderTexture.active = rt; image = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0,0,size.x,size.y), 0, 0); image.Apply();
                File.WriteAllBytes(Results + "/" + label + "-" + size.x + "x" + size.y + ".png", image.EncodeToPNG());
                return image.GetPixels32();
            }
            finally
            {
                camera.targetTexture = target; camera.aspect = aspect; RenderTexture.active = active;
                canvas.renderMode = mode; canvas.worldCamera = previousCamera; canvas.planeDistance = plane; Canvas.ForceUpdateCanvases();
                if (image != null) UnityEngine.Object.DestroyImmediate(image); rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
            }
        }
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
