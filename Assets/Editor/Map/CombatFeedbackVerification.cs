using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class CombatFeedbackVerification
{
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Call(object target, string name, params object[] args) => target.GetType().GetMethod(name,Private).Invoke(target,args);
    private static IEnumerator Wait(Func<bool> ready)
    {
        double end = EditorApplication.timeSinceStartup + 50;
        while (!ready())
        {
            if (EditorApplication.timeSinceStartup > end) throw new InvalidOperationException("Combat feedback verification timed out.");
            yield return null;
        }
    }

    private static IEnumerator Delay(float seconds, bool realtime = false)
    {
        double now() => realtime ? EditorApplication.timeSinceStartup : Time.timeAsDouble;
        double end = now() + seconds;
        while (now() < end) yield return null;
    }

    public static IEnumerator Run(Action<bool,string> check)
    {
        yield return Wait(() => Object.FindFirstObjectByType<MainMenuController>() != null);
        Call(Object.FindFirstObjectByType<MainMenuController>(), "StartRun");
        yield return Wait(() => BattleHud.Find(SceneManager.GetActiveScene())?.Deck?.CanPlay == true);
        var feedback = Object.FindFirstObjectByType<CombatFeedback>();
        check(typeof(BattleScript).GetMethod("TakeDamage", new[] { typeof(int) }) != null,
            "The original one-argument damage entry remains available for UnityEvents and existing callers.");
        check(feedback != null && CombatFeedback.IsPresent(SceneManager.GetActiveScene()), "Formal combat contains its own presentation root.");
        var hud = BattleHud.Find(SceneManager.GetActiveScene()); var player = hud.Deck.Player;
        var enemies = BattleScript.FindFighters("Enemy"); var enemy = enemies[0];
        TimeTickSystem.Active.StopTimer();
        foreach (var target in enemies) target.maxHealth = target.health = 200;
        var origins = new[] { player }.Concat(enemies).ToDictionary(a => a, a => a.transform.position);
        var renderer = enemy.GetComponent<Renderer>(); var block = new MaterialPropertyBlock();
        check(renderer.sharedMaterial.shader.name == "Deinosavros/Combat/Portrait" &&
            player.GetComponent<Renderer>().sharedMaterial.shader == renderer.sharedMaterial.shader, "Both teams use the combat portrait shader.");
        check(!ShaderUtil.ShaderHasError(renderer.sharedMaterial.shader), "Portrait shader compiles without errors.");
        yield return VerifyLighting(feedback, new[] { player }.Concat(enemies).ToArray(), check);
        // Let capture/first-use shader compilation settle before timing a short hit cue.
        yield return Delay(.2f, true);
        int before = feedback.DamageEventCount;
        int playerHealthBeforeHit = player.health;
        enemy.shield = 5; enemy.TakeDamage(12, player); player.shield = 0; player.TakeDamage(4, enemy);
        yield return null;
        check(enemy.health == 193 && enemy.shield == 0 && player.health == playerHealthBeforeHit - 4, "Feedback does not change health or shield arithmetic.");
        var numbers = feedback.canvas.transform.Find("Impacts").GetComponentsInChildren<TMP_Text>();
        check(numbers.Any(t => t.text == "-7") && numbers.Any(t => t.text == "5") && numbers.Any(t => t.text == "-4") &&
            feedback.canvas.transform.Find("Impacts").GetComponentsInChildren<StatusIcon>().Length == 1,
            "Both sides display actual health loss; absorbed damage has a separate shield icon and number.");
        check(numbers.All(t => !t.raycastTarget) && !feedback.canvas.transform.Find("Impacts").GetComponent<CanvasGroup>().blocksRaycasts,
            "Presentation never intercepts card or button input.");
        var layer = feedback.canvas.transform.Find("Impacts");
        check(feedback.canvas.transform.Cast<Transform>().Where(t => t.name.EndsWith("_Status"))
            .All(t => t.GetSiblingIndex() < layer.GetSiblingIndex()) &&
            layer.GetSiblingIndex() < feedback.canvas.transform.Find("HandContainer").GetSiblingIndex(),
            "Damage numbers stay readable above enemy status bars without covering the card layer.");
        renderer.GetPropertyBlock(block);
        check(block.GetFloat("_HitBlend") > 0, "A landed hit activates the flash.");
        Call(player, "Attack");
        yield return Delay(.04f);
        renderer.GetPropertyBlock(block);
        check(block.GetVector("_WorldOffset").sqrMagnitude > 0, "Recoil bends only the render position.");
        var playerRenderer = player.GetComponent<Renderer>(); playerRenderer.GetPropertyBlock(block);
        check(block.GetVector("_WorldOffset").sqrMagnitude > 0, "An attack lunges toward its target.");
        foreach (var pair in origins) check(pair.Key.transform.position == pair.Value, "Gameplay position stays fixed: " + pair.Key.name);
        Time.timeScale = 0;
        try
        {
            var offset = block.GetVector("_WorldOffset");
            yield return Delay(.12f, true);
            playerRenderer.GetPropertyBlock(block);
            check(block.GetVector("_WorldOffset") == offset, "Combat pause also pauses impact motion.");
            var loop = player.GetComponent<PlayerIdleLoop>();
            Call(loop, "ApplyPose", 2.05f);
            playerRenderer.GetPropertyBlock(block);
            check(block.GetTexture("_MainTex") != null && block.GetVector("_WorldOffset") == offset,
                "Player frame changes preserve the impact property block.");
            foreach (var size in new[] { new Vector2Int(1920,1080), new Vector2Int(2560,1440), new Vector2Int(1920,1200) })
            {
                var pixels = Capture(feedback.canvas,feedback.worldCamera,size,"impact");
                check(pixels.Length == size.x * size.y, "Damage presentation renders at " + size);
            }
            var sky = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Single(r => r.name == "SkyPic");
            var original = sky.sharedMaterial;
            var preview = new Material(original); sky.sharedMaterial = preview;
            try
            {
                check(preview.shader.name == "Deinosavros/Combat/Clouds" && !ShaderUtil.ShaderHasError(preview.shader), "The existing sky uses its compiled cloud shader.");
                preview.SetFloat("_PreviewTime",0);
                var a = Capture(feedback.canvas,feedback.worldCamera,new Vector2Int(1920,1080),"cloud-a");
                preview.SetFloat("_PreviewTime",8);
                var b = Capture(feedback.canvas,feedback.worldCamera,new Vector2Int(1920,1080),"cloud-b");
                int changed = 0;
                for (int i = 0; i < a.Length; i++)
                    if (Math.Abs(a[i].r-b[i].r) + Math.Abs(a[i].g-b[i].g) + Math.Abs(a[i].b-b[i].b) > 12) changed++;
                check(changed > 1500, "Cloud movement visibly changes pixels, not only a material parameter: " + changed);
            }
            finally { sky.sharedMaterial = original; Object.Destroy(preview); }
        }
        finally { Time.timeScale = 1; }
        yield return Delay(1.2f);
        renderer.GetPropertyBlock(block);
        check(block.GetFloat("_HitBlend") == 0 && block.GetVector("_WorldOffset").sqrMagnitude < .00001f && feedback.ActiveNumberCount == 0,
            "All transient effects settle without drift or lingering numbers.");
        enemy.shield = 30; int hp = enemy.health;
        enemy.TakeDamage(3, player); yield return null;
        check(enemy.health == hp && enemy.shield == 27 && feedback.ActiveNumberCount == 1, "Fully absorbed hits display one shield number and leave HP unchanged.");
        before = feedback.DamageEventCount;
        enemy.TakeDamage(0,player);
        check(feedback.DamageEventCount == before, "Zero damage produces no false hit feedback.");
        enemy.health = 200; enemy.shield = 0;
        for (int i = 0; i < 80; i++) enemy.TakeDamage(1,player);
        yield return null;
        check(feedback.ActiveNumberCount <= feedback.numberPoolSize && feedback.canvas.transform.Find("Impacts").childCount == feedback.numberPoolSize,
            "Multi-hit bursts recycle a bounded number pool instead of instantiating more objects.");
        enemy.health = 3; enemy.TakeDamage(20,player); yield return null;
        check(!enemy.enabled && enemy.health == 0 && feedback.DamageEventCount > before, "Lethal hits still present feedback after the fighter disables.");
        renderer.GetPropertyBlock(block);
        check(block.GetFloat("_Defeated") > 0, "Defeated sprites use a subdued material instead of a solid red repaint.");
        yield return Delay(1.2f);
        Capture(feedback.canvas,feedback.worldCamera,new Vector2Int(1920,1080),"settled");
        feedback.enabled = false; renderer.GetPropertyBlock(block);
        check(block.GetFloat("_HitBlend") == 0 && block.GetVector("_WorldOffset").sqrMagnitude == 0,
            "Disabling feedback restores its owned shader properties.");
    }

    private static IEnumerator VerifyLighting(CombatFeedback feedback, BattleScript[] actors, Action<bool, string> check)
    {
        var camera = feedback.worldCamera;
        var size = new Vector2Int(1920, 1080);
        var bodies = actors.Select(a => a.GetComponent<Renderer>()).ToArray();
        var properties = bodies.Select(r => { var b = new MaterialPropertyBlock(); r.GetPropertyBlock(b); return b; }).ToArray();
        var renderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        int isolatedLayer = Enumerable.Range(8, 24).First(layer => renderers.All(r => r.gameObject.layer != layer));
        var originalLayers = bodies.Select(r => r.gameObject.layer).ToArray();
        int cameraMask = camera.cullingMask;
        var lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
        var lightStates = lights.Select(l => l.enabled).ToArray();
        var cameraData = camera.GetUniversalAdditionalCameraData();
        bool post = cameraData.renderPostProcessing, canvasEnabled = feedback.canvas.enabled;
        var flags = camera.clearFlags; var background = camera.backgroundColor;
        float timeScale = Time.timeScale, aspect = camera.aspect;
        Light fixture = null;
        try
        {
            Time.timeScale = 0;
            check(bodies.All(r => r.sharedMaterial.GetFloat("_LightingStrength") == 1), "Both teams opt into scene lighting.");
            foreach (var actor in actors)
                check(actor.GetComponentsInChildren<Renderer>().Where(r => r != actor.GetComponent<Renderer>())
                    .All(r => r.sharedMaterial.GetFloat("_LightingStrength") == 0), "Existing shadow copies remain unlit: " + actor.name);
            for (int i = 0; i < bodies.Length; i++)
            {
                var unlit = new MaterialPropertyBlock(); bodies[i].GetPropertyBlock(unlit);
                unlit.SetFloat("_LightingStrength", 0); bodies[i].SetPropertyBlock(unlit);
            }
            Capture(feedback.canvas, camera, size, "lighting-before");
            for (int i = 0; i < bodies.Length; i++) bodies[i].SetPropertyBlock(properties[i]);
            Capture(feedback.canvas, camera, size, "lighting-after");

            // Isolate the portraits so changed environment pixels cannot give a false pass.
            foreach (var body in bodies) body.gameObject.layer = isolatedLayer;
            camera.cullingMask = 1 << isolatedLayer;
            foreach (var light in lights) light.enabled = false;
            feedback.canvas.enabled = false; cameraData.renderPostProcessing = false;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            camera.aspect = (float)size.x / size.y;
            fixture = new GameObject("Portrait Light Check").AddComponent<Light>();
            fixture.type = LightType.Directional;
            fixture.transform.rotation = camera.transform.rotation;
            fixture.intensity = 1.5f; fixture.color = new Color(1f, .22f, .08f);
            yield return Delay(.1f, true);
            var warm = Capture(feedback.canvas, camera, size, "lighting-test-warm");
            fixture.color = new Color(.08f, .25f, 1f);
            yield return Delay(.1f, true);
            var cool = Capture(feedback.canvas, camera, size, "lighting-test-cool");
            foreach (var body in bodies)
                check(ChangedPixels(warm, cool, body, camera, size) > 100, "Directional light color changes the actual portrait pixels: " + body.name);
            fixture.enabled = false;
            yield return Delay(.1f, true);
            var ambient = Capture(feedback.canvas, camera, size, "lighting-test-ambient");
            var center = bodies.Aggregate(Vector3.zero, (p, r) => p + r.bounds.center) / bodies.Length;
            fixture.type = LightType.Point; fixture.range = 100;
            fixture.transform.position = center - camera.transform.forward * 9;
            fixture.intensity = 250; fixture.color = new Color(1f, .72f, .35f); fixture.enabled = true;
            yield return Delay(.1f, true);
            var point = Capture(feedback.canvas, camera, size, "lighting-test-point");
            foreach (var body in bodies)
                check(ChangedPixels(ambient, point, body, camera, size) > 100, "Additional point lighting reaches the portrait: " + body.name);
            fixture.type = LightType.Spot; fixture.spotAngle = 130; fixture.innerSpotAngle = 100;
            fixture.transform.LookAt(center); fixture.intensity = 1000;
            yield return Delay(.1f, true);
            var spot = Capture(feedback.canvas, camera, size, "lighting-test-spot");
            foreach (var body in bodies)
                check(ChangedPixels(ambient, spot, body, camera, size) > 100, "Spot lighting reaches the portrait: " + body.name);
            check(!ShaderUtil.ShaderHasError(bodies[0].sharedMaterial.shader), "Active directional and clustered additional-light shader variants compile.");
        }
        finally
        {
            if (fixture != null) Object.DestroyImmediate(fixture.gameObject);
            for (int i = 0; i < bodies.Length; i++) bodies[i].SetPropertyBlock(properties[i]);
            for (int i = 0; i < bodies.Length; i++) bodies[i].gameObject.layer = originalLayers[i];
            camera.cullingMask = cameraMask;
            for (int i = 0; i < lights.Length; i++) lights[i].enabled = lightStates[i];
            feedback.canvas.enabled = canvasEnabled; cameraData.renderPostProcessing = post;
            camera.clearFlags = flags; camera.backgroundColor = background; camera.aspect = aspect;
            Time.timeScale = timeScale;
        }
    }

    private static int ChangedPixels(Color32[] a, Color32[] b, Renderer renderer, Camera camera, Vector2Int size)
    {
        var bounds = renderer.bounds; var min = Vector2.one; var max = Vector2.zero;
        for (int i = 0; i < 8; i++)
        {
            var corner = bounds.center + Vector3.Scale(bounds.extents,
                new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            Vector2 point = camera.WorldToViewportPoint(corner);
            min = Vector2.Min(min, point); max = Vector2.Max(max, point);
        }
        int changed = 0;
        for (int y = Mathf.Clamp((int)(min.y * size.y), 0, size.y); y < Mathf.Clamp((int)(max.y * size.y), 0, size.y); y++)
            for (int x = Mathf.Clamp((int)(min.x * size.x), 0, size.x); x < Mathf.Clamp((int)(max.x * size.x), 0, size.x); x++)
            {
                int i = y * size.x + x;
                if (Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b) > 20) changed++;
            }
        return changed;
    }

    private static Color32[] Capture(Canvas canvas, Camera camera, Vector2Int size, string label)
    {
        Directory.CreateDirectory("Library/CombatFeedback");
        var target = new RenderTexture(size.x,size.y,24,RenderTextureFormat.ARGB32);
        var previousTarget = camera.targetTexture; var previousActive = RenderTexture.active;
        var previousMode = canvas.renderMode; var previousCamera = canvas.worldCamera;
        float plane = canvas.planeDistance, aspect = camera.aspect;
        Texture2D image = null;
        try
        {
            target.Create(); camera.targetTexture = target; camera.aspect = (float)size.x/size.y;
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera;
            canvas.planeDistance = camera.nearClipPlane + .5f; Canvas.ForceUpdateCanvases();
            RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture.active = target; image = new Texture2D(size.x,size.y,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,size.x,size.y),0,0); image.Apply();
            File.WriteAllBytes("Library/CombatFeedback/"+label+"-"+size.x+"x"+size.y+".png",image.EncodeToPNG());
            return image.GetPixels32();
        }
        finally
        {
            canvas.renderMode = previousMode; canvas.worldCamera = previousCamera; canvas.planeDistance = plane;
            camera.targetTexture = previousTarget; camera.aspect = aspect; RenderTexture.active = previousActive;
            if (image != null) Object.DestroyImmediate(image); target.Release(); Object.DestroyImmediate(target);
            Canvas.ForceUpdateCanvases();
        }
    }
}
