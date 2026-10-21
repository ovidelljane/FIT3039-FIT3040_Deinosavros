using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class CardUseEffectAuthoring
{
    private const string ScenePath = "Assets/Scenes/Deinosavros.unity";
    private const string Output = "Library/CardDeparture";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/Battle/Apply Gold Leaf Card Departure")]
    public static void Apply()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Stop Play Mode before authoring card departure.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            Require(!SceneManager.GetSceneAt(i).isDirty, "Save your scene changes first.");
        var setup = EditorSceneManager.GetSceneManagerSetup();
        Directory.CreateDirectory(Output);
        if (!File.Exists(Output + "/Combat-before.unity")) File.Copy(ScenePath, Output + "/Combat-before.unity");
        try
        {
            var shader = Shader.Find("Deinosavros/UI/Card Departure");
            Require(shader != null && !ShaderUtil.ShaderHasError(shader), "The card departure shader must compile.");
            const string materialPath = "Assets/UI/CardDeparture.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "CardDeparture" };
                AssetDatabase.CreateAsset(material, materialPath);
            }
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var deck = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<DeckManager>(true)).Single();
            using var data = new SerializedObject(deck);
            var hand = (Transform)data.FindProperty("handContainer").objectReferenceValue;
            var existing = hand.parent.Find("CardUseFX");
            bool changed = existing == null;
            var root = existing as RectTransform;
            if (root == null)
            {
                root = (RectTransform)new GameObject("CardUseFX", typeof(RectTransform)).transform;
                root.SetParent(hand.parent, false); root.SetSiblingIndex(hand.GetSiblingIndex() + 1);
                root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
                root.offsetMin = root.offsetMax = Vector2.zero;
                root.gameObject.layer = hand.gameObject.layer;
                var group = root.gameObject.AddComponent<CanvasGroup>();
                group.interactable = false; group.blocksRaycasts = false;
            }
            var effect = root.GetComponent<CardUseEffect>();
            if (effect == null) { effect = root.gameObject.AddComponent<CardUseEffect>(); changed = true; }
            if (effect.cardTemplate == null)
            {
                var template = new GameObject("CardTemplate", typeof(RectTransform)).AddComponent<RawImage>();
                template.transform.SetParent(root, false); template.gameObject.layer = root.gameObject.layer;
                template.rectTransform.sizeDelta = new Vector2(246, 346);
                template.raycastTarget = false; template.material = material;
                template.gameObject.SetActive(false); effect.cardTemplate = template; changed = true;
            }
            if (data.FindProperty("cardUseEffect").objectReferenceValue != effect)
            {
                data.FindProperty("cardUseEffect").objectReferenceValue = effect;
                data.ApplyModifiedPropertiesWithoutUndo(); changed = true;
            }
            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                Require(EditorSceneManager.SaveScene(scene), "Card departure scene save failed.");
            }
            scene = EditorSceneManager.OpenScene(ScenePath);
            effect = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<CardUseEffect>(true)).Single();
            Require(effect.IsReady && !effect.cardTemplate.gameObject.activeSelf && !effect.cardTemplate.raycastTarget,
                "The saved scene must retain its non-interactive template and material.");
            AssetDatabase.SaveAssetIfDirty(material);
            File.WriteAllText(Output + "/authoring.txt", "PASS: Canvas/CardUseFX, editable timing and gold leaf settings, saved template and deck binding.\n");
        }
        finally { EditorSceneManager.RestoreSceneManagerSetup(setup); }
    }

    public static void ApplyTwiceAndVerify()
    {
        Apply(); string first = File.ReadAllText(ScenePath);
        Apply(); Require(File.ReadAllText(ScenePath) == first, "Reapplying must preserve the authored scene exactly.");
        RunIntegrationPlayVerification.RunCardDeparture();
    }

    public static IEnumerator Verify(Action<bool, string> check)
    {
        yield return Wait(() => Object.FindFirstObjectByType<MainMenuController>() != null);
        var menu = Object.FindFirstObjectByType<MainMenuController>();
        menu.GetType().GetMethod("StartRun", Private).Invoke(menu, null);
        yield return Wait(() => BattleHud.Find(SceneManager.GetActiveScene())?.Deck?.CanPlay == true);
        var hud = BattleHud.Find(SceneManager.GetActiveScene());
        var deck = hud.Deck; var player = deck.Player;
        var effect = hud.Canvas.GetComponentInChildren<CardUseEffect>();
        check(effect != null && effect.IsReady, "Combat resolves the saved card departure template.");
        check(!ShaderUtil.ShaderHasError(effect.cardTemplate.material.shader), "The Canvas dissolve shader compiles.");
        Time.timeScale = 0;
        try
        {
            var pointer = new PointerEventData(EventSystem.current);
            player.health = player.maxHealth; player.elixir = player.maxElixir;
            yield return null; Canvas.ForceUpdateCanvases();
            Capture(hud.Canvas, new Vector2Int(1920, 1080), "01-before");
            var card = deck.Hand.First(c => c.MeetsResourceRequirement);
            int initialHand = deck.Hand.Count;
            float resource = card.UsesHealthCost ? player.health : player.elixir;
            int cost = card.Cost; bool healthCost = card.UsesHealthCost;
            var remaining = deck.Hand.Where(c => c != card).ToArray();
            var timer = System.Diagnostics.Stopwatch.StartNew();
            card.OnPointerClick(pointer);
            timer.Stop();
            File.WriteAllText(Output + "/timing.txt", "Editor cold successful play including UI capture: " + timer.Elapsed.TotalMilliseconds.ToString("F2") + " ms\n");
            check(deck.Hand.Count == initialHand - 1 && effect.ActiveCount == 1,
                "The actual hand commits immediately while exactly one visual survives.");
            card.OnPointerClick(pointer);
            check(effect.ActiveCount == 1 && deck.Hand.Count == initialHand - 1, "Repeated click does not replay or consume twice.");
            check(Mathf.Approximately(healthCost ? player.health : player.elixir, resource - cost), "Animation does not change the exact cost.");
            yield return null; Canvas.ForceUpdateCanvases();
            var snapshots = effect.GetComponentsInChildren<RawImage>().Where(i => i.name.StartsWith("Card") && i.texture is RenderTexture).ToArray();
            check(snapshots.Length == 1 && snapshots[0].GetComponent<BuffCards>() == null &&
                !snapshots[0].raycastTarget, "The pooled face is presentation-only and does not intercept input.");
            SaveSnapshot((RenderTexture)snapshots[0].texture, check);
            Advance(effect, .06f); yield return null;
            Capture(hud.Canvas, new Vector2Int(1920, 1080), "02-lift");
            Advance(effect, .20f); yield return null; Canvas.ForceUpdateCanvases();
            foreach (var size in new[] { new Vector2Int(1920, 1080), new Vector2Int(2560, 1440), new Vector2Int(1920, 1200) })
                Capture(hud.Canvas, size, "03-dissolve");
            File.WriteAllText(Output + "/fragments.txt", string.Join("\n", effect.GetComponentsInChildren<CardUseFragments>(true).Select(f =>
                f.name + " active=" + f.isActiveAndEnabled + " visible=" + f.VisibleCount +
                " age=" + typeof(CardUseFragments).GetField("age", Private).GetValue(f) +
                " count=" + typeof(CardUseFragments).GetField("count", Private).GetValue(f) +
                " cull=" + f.canvasRenderer.cull + " rect=" + f.rectTransform.rect)));
            check(effect.GetComponentsInChildren<CardUseFragments>().Any(f => f.VisibleCount > 0), "Directional dissolve releases visible tapered foil and laurel leaves.");
            Advance(effect, .15f); yield return null;
            var withLeaves = Capture(hud.Canvas, new Vector2Int(1920, 1080), "03b-leaves");
            var activeFragments = effect.GetComponentsInChildren<CardUseFragments>();
            foreach (var fragments in activeFragments) fragments.enabled = false;
            var withoutLeaves = Capture(hud.Canvas, new Vector2Int(1920, 1080), "03c-without-leaves");
            foreach (var fragments in activeFragments) fragments.enabled = true;
            int leafPixels = withLeaves.Zip(withoutLeaves, (a, b) =>
                Mathf.Abs(a.r-b.r) + Mathf.Abs(a.g-b.g) + Mathf.Abs(a.b-b.b)).Count(d => d > 30);
            File.AppendAllText(Output + "/fragments.txt", "\nVisible leaf pixels: " + leafPixels + "\n" +
                string.Join("\n", activeFragments.Select(f => "origin=" + typeof(CardUseFragments).GetField("origin", Private).GetValue(f) +
                    " target=" + typeof(CardUseFragments).GetField("target", Private).GetValue(f))));
            check(leafPixels > 30, "Leaf geometry contributes visible pixels, not only populated meshes.");
            check(remaining.All(c => c != null && c.gameObject.activeInHierarchy), "Other cards remain intact and usable.");
            Advance(effect, effect.Duration); yield return null;
            check(effect.ActiveCount == 0, "The final transparent face and fragments return to the pool.");
            Capture(hud.Canvas, new Vector2Int(1920, 1080), "04-finished");

            // All authored card prefabs, including layered artwork and dynamic cost text.
            var definitions = AssetDatabase.FindAssets("t:CardDefinition", new[] { "Assets/Data/Cards" })
                .Select(g => AssetDatabase.LoadAssetAtPath<CardDefinition>(AssetDatabase.GUIDToAssetPath(g))).ToArray();
            foreach (var definition in definitions)
            {
                var view = AddTestCard(deck, definition); yield return null; Canvas.ForceUpdateCanvases();
                player.health = player.maxHealth; player.elixir = 1000;
                timer.Restart(); view.OnPointerClick(pointer); timer.Stop();
                File.AppendAllText(Output + "/timing.txt", definition.cardId + " warm successful play: " + timer.Elapsed.TotalMilliseconds.ToString("F2") + " ms\n");
                check(effect.ActiveCount == 1, "Authored card uses the same complete-face effect: " + definition.cardId);
                Advance(effect, effect.Duration + .1f); yield return null;
            }
            check(effect.GetComponentsInChildren<RawImage>(true).Length == effect.poolSize + 2,
                "Repeated use reuses a bounded pool instead of accumulating visuals.");

            var safeDefinition = definitions.First(d => d.BaseValues.Stat == StatType.Shield);
            var a = AddTestCard(deck, safeDefinition); var b = AddTestCard(deck, safeDefinition);
            yield return null; Canvas.ForceUpdateCanvases();
            player.elixir = 1000; a.OnPointerClick(pointer); b.OnPointerClick(pointer);
            check(effect.ActiveCount == 2, "Two rapid successful plays animate independently.");
            yield return Delay(.15f);
            check(effect.ActiveCount == 2, "Pause freezes the visual clock without touching gameplay state.");
            var denied = AddTestCard(deck, safeDefinition); yield return null;
            player.elixir = 0; int count = deck.Hand.Count;
            denied.OnPointerClick(pointer);
            check(effect.ActiveCount == 2 && deck.Hand.Count == count, "Insufficient resource creates no departure and removes no card.");
            effect.enabled = false;
            check(effect.ActiveCount == 0, "Disabling clears active presentation safely.");
            effect.enabled = true;
            player.elixir = 1000; denied.OnPointerClick(pointer);
            check(effect.ActiveCount == 1, "Re-enabling reuses the pool successfully.");
            deck.SetCombatEnabled(false);
            check(effect.ActiveCount == 0, "Combat completion clears presentation before the reward overlay.");
            check(effect.GetComponentsInChildren<Graphic>(true).All(g => !g.raycastTarget), "All departure graphics leave gameplay input unblocked.");
            yield return SceneManager.LoadSceneAsync("MainMenu");
            yield return null;
            check(Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).All(c => c.transform.parent == null || c.transform.parent.name != "Card Snapshot"),
                "Scene exit releases the offscreen camera and its canvas.");
        }
        finally { Time.timeScale = 1; }
    }

    private static BuffCards AddTestCard(DeckManager deck, CardDefinition definition)
    {
        var hand = (Transform)typeof(DeckManager).GetField("handContainer", Private).GetValue(deck);
        var card = Object.Instantiate(definition.combatPrefab, hand).GetComponent<BuffCards>();
        card.Initialize(deck, definition);
        ((List<BuffCards>)typeof(DeckManager).GetField("hand", Private).GetValue(deck)).Add(card);
        return card;
    }
    private static void Advance(CardUseEffect effect, float seconds) =>
        typeof(CardUseEffect).GetMethod("AdvanceVisuals", Private).Invoke(effect, new object[] { seconds });

    private static IEnumerator Wait(Func<bool> ready)
    {
        double deadline = EditorApplication.timeSinceStartup + 50;
        while (!ready()) { Require(EditorApplication.timeSinceStartup < deadline, "Card departure test timed out."); yield return null; }
    }
    private static IEnumerator Delay(float seconds)
    {
        double until = EditorApplication.timeSinceStartup + seconds;
        while (EditorApplication.timeSinceStartup < until) yield return null;
    }
    private static void SaveSnapshot(RenderTexture target, Action<bool, string> check)
    {
        var old = RenderTexture.active;
        var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        try
        {
            RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
            File.WriteAllBytes(Output + "/face-snapshot.png", image.EncodeToPNG());
            var pixels = image.GetPixels32();
            check(pixels.Count(p => p.a > 200) > pixels.Length * .2f && pixels.Count(p => p.a == 0) > pixels.Length * .02f,
                "The composed card snapshot contains both opaque artwork and transparent surroundings.");
        }
        finally { RenderTexture.active = old; Object.Destroy(image); }
    }
    private static Color32[] Capture(Canvas canvas, Vector2Int size, string label)
    {
        Directory.CreateDirectory(Output);
        var camera = Camera.main;
        var target = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGB32);
        var oldMode = canvas.renderMode; var oldCamera = canvas.worldCamera;
        float oldPlane = canvas.planeDistance, oldAspect = camera.aspect;
        var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active;
        Texture2D image = null;
        try
        {
            target.Create(); camera.targetTexture = target; camera.aspect = (float)size.x / size.y;
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera;
            canvas.planeDistance = camera.nearClipPlane + .5f; Canvas.ForceUpdateCanvases();
            var effect = canvas.GetComponentInChildren<CardUseEffect>();
            if (effect != null) Advance(effect, 0);
            Canvas.ForceUpdateCanvases();
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture.active = target;
            image = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0); image.Apply();
            File.WriteAllBytes(Output + "/" + label + "-" + size.x + "x" + size.y + ".png", image.EncodeToPNG());
            return image.GetPixels32();
        }
        finally
        {
            canvas.renderMode = oldMode; canvas.worldCamera = oldCamera; canvas.planeDistance = oldPlane;
            camera.targetTexture = oldTarget; camera.aspect = oldAspect; RenderTexture.active = oldActive;
            if (image != null) Object.Destroy(image); target.Release(); Object.Destroy(target);
            Canvas.ForceUpdateCanvases();
        }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
