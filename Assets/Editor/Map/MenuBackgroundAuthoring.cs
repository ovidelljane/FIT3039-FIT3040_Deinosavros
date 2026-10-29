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

    [MenuItem("Tools/UI/Author Result Text")]
    public static void AuthorResultText()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode,"Stop Play Mode before authoring result text.");
        const string path = "Assets/Scenes/Deinosavros.unity";
        var scene = SceneManager.GetSceneByPath(path);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
        try
        {
            var controller = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<RewardScreenController>(true)).Single();
            using var data = new SerializedObject(controller);
            if (data.FindProperty("victory").FindPropertyRelative("root").objectReferenceValue != null) return;
            Directory.CreateDirectory(Results);
            if (!File.Exists(Results+"/Combat-before-result-text.unity")) File.Copy(path,Results+"/Combat-before-result-text.unity");
            var panel = ((GameObject)data.FindProperty("rewardPanel").objectReferenceValue).transform;
            var originalTitle = panel.Find("Title").GetComponent<TMP_Text>();
            var button = (Button)data.FindProperty("skipButton").objectReferenceValue;
            var originalLabel = button.GetComponentInChildren<TMP_Text>(true);
            var headingFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Resources/Fonts/Cinzel-Black SDF.asset");
            var bodyFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Resources/Fonts/Alegreya SDF.asset");
            var accent = data.FindProperty("accentColor").colorValue;
            Undo.RegisterFullObjectHierarchyUndo(panel.gameObject,"Author result menu text");
            RectTransform Group(string name)
            {
                var rect = (RectTransform)new GameObject(name,typeof(RectTransform)).transform;
                rect.gameObject.layer = panel.gameObject.layer; rect.SetParent(panel,false);
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.sizeDelta = Vector2.zero;
                return rect;
            }
            void Anchors(RectTransform rect,Vector2 min,Vector2 max)
            { rect.anchorMin=min; rect.anchorMax=max; rect.offsetMin=rect.offsetMax=Vector2.zero; }
            TMP_Text Body(Transform parent,string name,string content)
            {
                var rect = (RectTransform)new GameObject(name,typeof(RectTransform),typeof(TextMeshProUGUI)).transform;
                rect.gameObject.layer=panel.gameObject.layer; rect.SetParent(parent,false);
                Anchors(rect,new Vector2(.07f,.78f),new Vector2(.93f,.85f));
                var text=rect.GetComponent<TMP_Text>(); text.text=content; text.font=bodyFont;
                text.fontSize=text.fontSizeMax=30; text.fontSizeMin=23; text.enableAutoSizing=true;
                text.alignment=TextAlignmentOptions.Center; text.raycastTarget=false; text.color=new Color(.95f,.88f,.7f);
                return text;
            }
            void View(string property,string name,string heading,string description,bool reuse)
            {
                var root=Group(name);
                var title=reuse ? originalTitle : UnityEngine.Object.Instantiate(originalTitle,root,false);
                title.transform.SetParent(root,false); title.name="Title";
                title.text=heading; title.font=headingFont; title.fontSize=title.fontSizeMax=56; title.enableAutoSizing=false;
                title.color=accent; title.raycastTarget=false;
                Anchors(title.rectTransform,new Vector2(.06f,.85f),new Vector2(.94f,.96f));
                var message=Body(root,"Message",description);
                var view=data.FindProperty(property);
                view.FindPropertyRelative("root").objectReferenceValue=root.gameObject;
                view.FindPropertyRelative("title").objectReferenceValue=title;
                view.FindPropertyRelative("message").objectReferenceValue=message;
                root.gameObject.SetActive(reuse);
            }
            string currentHeading=originalTitle.text;
            if (string.IsNullOrWhiteSpace(currentHeading) || currentHeading=="Choose a Card") currentHeading="Victory";
            View("victory","Victory",currentHeading,"Choose one card - Deck capacity {capacity}/{limit}",true);
            View("defeat","Defeat","Defeat","This adventure has ended",false);
            View("replacement","Replacement","Replace a Card","{card} - Capacity {capacity}/{limit}",false);
            var error=Body(panel,"ReturnError","Your reward is saved. Retry returning to the map.");
            error.gameObject.SetActive(false); data.FindProperty("returnError").objectReferenceValue=error;
            originalLabel.name="VictoryLabel"; originalLabel.font=headingFont;
            originalLabel.fontSize=originalLabel.fontSizeMax=25; originalLabel.fontSizeMin=19;
            originalLabel.enableAutoSizing=true; originalLabel.color=accent;
            string[] properties={"victoryButtonLabel","defeatButtonLabel","replacementButtonLabel","retryButtonLabel"};
            string[] names={"VictoryLabel","DefeatLabel","ReplacementLabel","RetryLabel"};
            string[] captions={"Skip","Return to Menu","Cancel Replacement","Retry Return"};
            for(int i=0;i<properties.Length;i++)
            {
                var label=i==0 ? originalLabel : UnityEngine.Object.Instantiate(originalLabel,button.transform,false);
                label.name=names[i]; label.text=captions[i]; label.gameObject.SetActive(i==0);
                data.FindProperty(properties[i]).objectReferenceValue=label;
            }
            var buttonRect=(RectTransform)button.transform;
            buttonRect.anchorMin=buttonRect.anchorMax=new Vector2(.5f,.08f);
            buttonRect.anchoredPosition=Vector2.zero; buttonRect.sizeDelta=new Vector2(380,72);
            button.GetComponent<Image>().color=data.FindProperty("cardColor").colorValue;
            var outline=button.GetComponent<Outline>(); if(outline==null) outline=button.gameObject.AddComponent<Outline>();
            outline.effectColor=accent; outline.effectDistance=new Vector2(1.5f,-1.5f);
            Anchors((RectTransform)data.FindProperty("rewardCardContainer").objectReferenceValue,new Vector2(.04f,.18f),new Vector2(.96f,.77f));
            data.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(scene);
            Require(EditorSceneManager.SaveScene(scene),"Could not save scene-authored result text.");
            File.WriteAllText(Results+"/result-text-bindings.txt","PASS: Victory, Defeat, Replacement, error and button text are authored in the Combat hierarchy.\n");
        }
        finally { if(opened) EditorSceneManager.CloseScene(scene,true); }
    }

    [MenuItem("Tools/UI/Verify Result Background")]
    public static void VerifyResultBackground()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode,"Run result background checks in Edit Mode.");
        var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/Deinosavros.unity");
        try
        {
            var roots = scene.GetRootGameObjects();
            var controller = roots.SelectMany(root => root.GetComponentsInChildren<RewardScreenController>(true)).Single();
            using var data = new SerializedObject(controller);
            var panel = (GameObject)data.FindProperty("rewardPanel").objectReferenceValue;
            var image = panel.GetComponent<Image>();
            var expected = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/ITCH_BACKDROP.png");
            Require(expected != null && image.sprite == expected && image.color.a == 1,"Victory and Defeat must share the Opportunity backdrop.");
            Require(image.color == data.FindProperty("backgroundColor").colorValue,"Runtime presentation must preserve the authored background tint.");
            var camera = roots.SelectMany(root => root.GetComponentsInChildren<Camera>(true)).Single(c => c.CompareTag("MainCamera"));
            camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
            var canvas = panel.GetComponentInParent<Canvas>();
            foreach (var root in roots)
                if (!camera.transform.IsChildOf(root.transform) && !canvas.transform.IsChildOf(root.transform)) root.SetActive(false);
            var branch = panel.transform;
            while (branch.parent != null && branch.parent != canvas.transform) branch = branch.parent;
            foreach (Transform child in canvas.transform) if (child != branch) child.gameObject.SetActive(false);
            canvas.GetComponent<CanvasScaler>().enabled = false;
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.scaleFactor = 1;
            ((RectTransform)canvas.transform).pivot = new Vector2(.5f,.5f);
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(1920,1080);
            panel.SetActive(true);
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var victory=typeof(RewardScreenController).GetField("victory",flags).GetValue(controller);
            typeof(RewardScreenController).GetMethod("PreparePresentation",flags).Invoke(controller,new[] { victory });
            var container = (Transform)data.FindProperty("rewardCardContainer").objectReferenceValue;
            var prefab = (GameObject)data.FindProperty("rewardCardPrefab").objectReferenceValue;
            var pool = (CardPool)data.FindProperty("cardPool").objectReferenceValue;
            foreach (var card in pool.Cards.Take(3))
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);
                instance.transform.SetParent(container,false);
                instance.GetComponent<RewardCardView>().Initialize(card,_ => {});
            }
            typeof(RewardScreenController).GetMethod("UpdatePresentationLayout",flags).Invoke(controller,null);
            Canvas.ForceUpdateCanvases();
            Require(container.GetComponentsInChildren<RewardCardView>().Length == 3 &&
                container.GetComponentsInChildren<RewardCardView>().All(view => ((RectTransform)view.transform).rect.height > 100),
                "The background preview must include three visible reward cards.");
            MapOpportunityVerification.CaptureCanvas(canvas,camera,"result-victory-background");
            foreach (Transform child in container.Cast<Transform>().ToArray()) UnityEngine.Object.DestroyImmediate(child.gameObject);
            var defeat=typeof(RewardScreenController).GetField("defeat",flags).GetValue(controller);
            typeof(RewardScreenController).GetMethod("PreparePresentation",flags).Invoke(controller,new[] { defeat });
            typeof(RewardScreenController).GetMethod("SetSkip",flags).Invoke(controller,new object[] {
                data.FindProperty("defeatButtonLabel").objectReferenceValue,new UnityEngine.Events.UnityAction(() => {}) });
            MapOpportunityVerification.CaptureCanvas(canvas,camera,"result-defeat-background");
            Directory.CreateDirectory(Results);
            File.WriteAllText(Results+"/result-background.txt","PASS: shared authored background survives real presentation initialization; Victory and Defeat captured at three resolutions.\n");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    [MenuItem("Tools/UI/Verify Result Text Editing")]
    public static void VerifyResultTextEditing()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode,"Run result text checks in Edit Mode.");
        var scene=EditorSceneManager.OpenPreviewScene("Assets/Scenes/Deinosavros.unity");
        int checks=0;
        void Check(bool value,string reason) { checks++; Require(value,reason); }
        try
        {
            var controller=scene.GetRootGameObjects().SelectMany(root=>root.GetComponentsInChildren<RewardScreenController>(true)).Single();
            const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var type=typeof(RewardScreenController);
            var win=(RewardScreenController.ResultTextView)type.GetField("victory",flags).GetValue(controller);
            var lose=(RewardScreenController.ResultTextView)type.GetField("defeat",flags).GetValue(controller);
            var replace=(RewardScreenController.ResultTextView)type.GetField("replacement",flags).GetValue(controller);
            var winButton=(TMP_Text)type.GetField("victoryButtonLabel",flags).GetValue(controller);
            var loseButton=(TMP_Text)type.GetField("defeatButtonLabel",flags).GetValue(controller);
            var retryButton=(TMP_Text)type.GetField("retryButtonLabel",flags).GetValue(controller);
            var error=(TMP_Text)type.GetField("returnError",flags).GetValue(controller);
            Check(win.root!=null && lose.root!=null && replace.root!=null,"Every result uses authored scene text.");
            win.title.text="Custom victory"; win.message.text="Custom reward {capacity}/{limit}";
            lose.title.text="Custom defeat"; lose.message.text="Custom ending";
            winButton.text="Keep going"; loseButton.text="Back home"; retryButton.text="Try again";
            error.text="Custom return failure";
            win.title.fontSize=63; win.title.color=Color.cyan; win.title.fontStyle=FontStyles.Italic;
            win.title.rectTransform.anchoredPosition+=new Vector2(17,-11);
            var position=win.title.rectTransform.anchoredPosition;
            var prepare=type.GetMethod("PreparePresentation",flags);
            prepare.Invoke(controller,new object[]{win});
            Check(win.title.text=="Custom victory" && win.message.text.Contains("Custom reward"),"Victory uses editable title and description.");
            Check(win.FormatMessage(17,30)=="Custom reward 17/30","Capacity tokens remain linked to real values.");
            Check(win.title.fontSize==63 && win.title.color==Color.cyan && win.title.fontStyle==FontStyles.Italic &&
                win.title.rectTransform.anchoredPosition==position,"Presentation does not reset scene text styling or placement.");
            typeof(GameFonts).GetMethod("StyleScene",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)
                .Invoke(null,new object[]{scene,LoadSceneMode.Single});
            Check(win.title.fontSize==63 && win.title.fontStyle==FontStyles.Italic,"Global scene typography preserves result text edits.");
            bool returned=false;
            controller.PresentDefeat(()=>returned=true);
            Check(lose.root.activeSelf && !win.root.activeSelf && lose.title.text=="Custom defeat" && lose.message.text=="Custom ending",
                "The real defeat entry preserves authored copy and switches only the result root.");
            Check(loseButton.gameObject.activeSelf && loseButton.text=="Back home","Defeat uses its own editable button label.");
            using(var data=new SerializedObject(controller))
                ((Button)data.FindProperty("skipButton").objectReferenceValue).onClick.Invoke();
            Check(returned,"The configured defeat label keeps the existing return action.");
            prepare.Invoke(controller,new object[]{replace});
            type.GetMethod("CancelReplacement",flags).Invoke(controller,null);
            Check(win.root.activeSelf && win.title.text=="Custom victory" && winButton.text=="Keep going" && winButton.gameObject.activeSelf,
                "Cancelling replacement restores authored victory content without a literal title reset.");
            Check(win.message.text.Contains("Custom reward"),"Returning to rewards keeps the editable description template.");
            controller.ShowReturnError(null);
            Check(error.gameObject.activeSelf && error.text=="Custom return failure" && retryButton.gameObject.activeSelf && retryButton.text=="Try again",
                "Return failure and retry copy are also authored in the hierarchy.");
            Directory.CreateDirectory(Results);
            File.WriteAllText(Results+"/result-text-editing.txt","PASS: "+checks+" checks; editable titles, messages, labels, dynamic tokens, typography, cancellation and existing button actions.\n");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

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
