using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Explicit, scene-local portrait shadow authoring. Never overwrites unsaved scenes.
public static class CombatShadowSetup
{
    public const string Output = "Library/CombatShadows";
    private const string ScenePath = "Assets/Scenes/Deinosavros.unity";

    public static void Inspect()
    {
        RequireCleanEditor();
        var previous = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            var scene = EditorSceneManager.OpenScene(ScenePath);
            Directory.CreateDirectory(Output);
            var report = new StringBuilder();
            foreach (var actor in Components<BattleScript>(scene))
            {
                report.AppendLine($"ACTOR {actor.name}: {actor.transform.position}");
                foreach (var renderer in actor.GetComponentsInChildren<MeshRenderer>(true))
                    report.AppendLine($"  {renderer.name}: {renderer.bounds}, cast={renderer.shadowCastingMode}, material={renderer.sharedMaterial.name}");
                foreach (var hit in Physics.RaycastAll(actor.transform.position + Vector3.up * 10, Vector3.down, 30))
                    report.AppendLine($"  BELOW {hit.collider.name}: {hit.point}");
            }
            foreach (var light in Components<Light>(scene))
                report.AppendLine($"LIGHT {light.name}: {light.type}, world={light.transform.position}, forward={light.transform.forward}, shadows={light.shadows}");
            foreach (var renderer in Components<MeshRenderer>(scene).Where(r => r.bounds.Contains(new Vector3(0, 0, 0)) || r.name.Contains("Ground") || r.name.Contains("Arena")))
                report.AppendLine($"GROUND {renderer.name}: {renderer.bounds}, {string.Join(", ", renderer.sharedMaterials.Select(m => m != null ? m.name + " / " + m.shader.name : "missing"))}");
            File.WriteAllText(Output + "/inventory.txt", report.ToString());
            Capture(Components<Camera>(scene).First(c => c.CompareTag("MainCamera")), null, "before");
        }
        finally { EditorSceneManager.RestoreSceneManagerSetup(previous); }
    }

    [MenuItem("Tools/Battle/Apply Portrait Shadows")]
    public static void Apply()
    {
        RequireCleanEditor();
        Directory.CreateDirectory(Output);
        var previous = EditorSceneManager.GetSceneManagerSetup();
        if (!File.Exists(Output + "/Deinosavros.before.unity"))
            File.Copy(ScenePath, Output + "/Deinosavros.before.unity", false);
        try
        {
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var actors = Components<BattleScript>(scene).Where(a => a.isActiveAndEnabled).ToArray();
            if (actors.Length != 4) throw new InvalidOperationException("Expected the existing four combat portraits.");
            foreach (var actor in actors)
            {
                var body = actor.GetComponent<MeshRenderer>();
                var material = body.sharedMaterial;
                if (material == null || material.shader.name != "Deinosavros/Combat/Portrait" || material.FindPass("ShadowCaster") < 0)
                    throw new InvalidOperationException("The portrait shadow caster must compile before applying.");
                body.shadowCastingMode = ShadowCastingMode.TwoSided;
                body.receiveShadows = true;
                material.SetFloat("_ShadowCutoff", .35f);
                material.SetFloat("_CastShadows", 1);
                material.DisableKeyword("_CASTSHADOWS_ON");
                EditorUtility.SetDirty(material);
                // Keep the meshes and animation references, but retire the coplanar dark copies.
                foreach (var copy in actor.GetComponentsInChildren<MeshRenderer>(true).Where(r => r != body &&
                    (r.sharedMaterial?.name == "CombatPlayerShadow" || r.sharedMaterial?.name == "CombatEnemyShadow")))
                {
                    copy.enabled = false;
                    copy.shadowCastingMode = ShadowCastingMode.Off;
                    copy.receiveShadows = false;
                    copy.sharedMaterial.SetFloat("_CastShadows", 0);
                    copy.sharedMaterial.DisableKeyword("_CASTSHADOWS_ON");
                    EditorUtility.SetDirty(copy.sharedMaterial);
                }
            }
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            scene = EditorSceneManager.OpenScene(ScenePath);
            foreach (var actor in Components<BattleScript>(scene).Where(a => a.isActiveAndEnabled))
            {
                var body = actor.GetComponent<MeshRenderer>();
                if (body.shadowCastingMode != ShadowCastingMode.TwoSided || body.sharedMaterial.GetFloat("_CastShadows") != 1)
                    throw new InvalidOperationException("Portrait casting was not persisted.");
                foreach (var copy in actor.GetComponentsInChildren<MeshRenderer>(true).Where(r => r != body))
                    if (copy.enabled || copy.shadowCastingMode != ShadowCastingMode.Off || copy.sharedMaterial.GetFloat("_CastShadows") != 0)
                        throw new InvalidOperationException("A retired shadow copy is still casting.");
            }
            if (ShaderUtil.ShaderHasError(Shader.Find("Deinosavros/Combat/Portrait")))
                throw new InvalidOperationException("Portrait shader compilation failed.");
            File.WriteAllText(Output + "/apply.txt", "PASS: Four two-sided silhouette casters; old dark copies disabled; scene saved and reopened.\n");
        }
        finally { EditorSceneManager.RestoreSceneManagerSetup(previous); }
    }

    private static void RequireCleanEditor()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save your scene changes first.");
    }

    public static T[] Components<T>(Scene scene) where T : Component => scene.GetRootGameObjects()
        .SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();

    public static Color32[] Capture(Camera camera, Canvas canvas, string label, int width = 1920, int height = 1080)
    {
        Directory.CreateDirectory(Output);
        var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        var previousTarget = camera.targetTexture;
        var previousActive = RenderTexture.active;
        var mode = canvas != null ? canvas.renderMode : RenderMode.ScreenSpaceOverlay;
        var previousCamera = canvas != null ? canvas.worldCamera : null;
        float plane = canvas != null ? canvas.planeDistance : 0, aspect = camera.aspect;
        Texture2D image = null;
        try
        {
            target.Create(); camera.targetTexture = target; camera.aspect = (float)width / height;
            if (canvas != null)
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera;
                canvas.planeDistance = camera.nearClipPlane + .5f;
                Canvas.ForceUpdateCanvases();
            }
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture.active = target;
            image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
            File.WriteAllBytes(Output + "/" + label + ".png", image.EncodeToPNG());
            return image.GetPixels32();
        }
        finally
        {
            if (canvas != null)
            {
                canvas.renderMode = mode; canvas.worldCamera = previousCamera; canvas.planeDistance = plane;
                Canvas.ForceUpdateCanvases();
            }
            camera.targetTexture = previousTarget; camera.aspect = aspect; RenderTexture.active = previousActive;
            if (image != null) Object.DestroyImmediate(image);
            target.Release(); Object.DestroyImmediate(target);
        }
    }
}

public static class CombatShadowVerification
{
    private static IEnumerator Wait(Func<bool> ready)
    {
        double limit = EditorApplication.timeSinceStartup + 50;
        while (!ready())
        {
            if (EditorApplication.timeSinceStartup > limit) throw new InvalidOperationException("Portrait shadow verification timed out.");
            yield return null;
        }
    }

    public static IEnumerator Run(Action<bool, string> check)
    {
        yield return Wait(() => Object.FindFirstObjectByType<MainMenuController>() != null);
        typeof(MainMenuController).GetMethod("StartRun", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Invoke(Object.FindFirstObjectByType<MainMenuController>(), null);
        yield return Wait(() => BattleHud.Find(SceneManager.GetActiveScene())?.Deck?.CanPlay == true);
        TimeTickSystem.Active.StopTimer();
        var scene = SceneManager.GetActiveScene();
        var actors = CombatShadowSetup.Components<BattleScript>(scene).Where(a => a.isActiveAndEnabled).ToArray();
        var bodies = actors.Select(a => a.GetComponent<MeshRenderer>()).ToArray();
        var camera = Camera.main;
        var canvas = BattleHud.Find(scene).Canvas;
        var originalBlocks = bodies.Select(r => { var block = new MaterialPropertyBlock(); r.GetPropertyBlock(block); return block; }).ToArray();
        float timeScale = Time.timeScale;
        Time.timeScale = 0;
        var report = new StringBuilder();
        var clear = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        clear.SetPixels(new[] { Color.clear, Color.clear, Color.clear, Color.clear }); clear.Apply();
        try
        {
            check(actors.Length == 4, "One player and three enemies retain their original combat components.");
            check(!ShaderUtil.ShaderHasError(bodies[0].sharedMaterial.shader), "Forward and silhouette caster shader passes compile.");
            foreach (var actor in actors)
            {
                var body = actor.GetComponent<MeshRenderer>();
                check(body.shadowCastingMode == ShadowCastingMode.TwoSided && body.sharedMaterial.FindPass("ShadowCaster") >= 0,
                    "Two-sided portrait casting survives scene reload: " + actor.name);
                check(actor.GetComponentsInChildren<MeshRenderer>(true).Where(r => r != body)
                    .All(r => !r.enabled && r.shadowCastingMode == ShadowCastingMode.Off), "Old coplanar shadows are disabled: " + actor.name);
                check(actor.GetComponentsInChildren<MeshFilter>(true).All(m => m.sharedMesh == actor.GetComponent<MeshFilter>().sharedMesh),
                    "Idle animation still owns the existing synchronized meshes: " + actor.name);
            }

            foreach (var body in bodies) body.shadowCastingMode = ShadowCastingMode.Off;
            var without = CombatShadowSetup.Capture(camera, canvas, "game-before");
            foreach (var body in bodies) body.shadowCastingMode = ShadowCastingMode.TwoSided;
            var with = CombatShadowSetup.Capture(camera, canvas, "game-after");
            check(Changed(without, with) > 1000, "Shadows are visible with the actual portraits and full combat UI present.");

            // Hide the portraits to measure only light falling on the actual environment.
            foreach (var body in bodies) body.enabled = false;
            var empty = CombatShadowSetup.Capture(camera, canvas, "ground-only");
            for (int i = 0; i < bodies.Length; i++)
            {
                var body = bodies[i];
                body.enabled = true; body.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                var cast = CombatShadowSetup.Capture(camera, canvas, "shadow-" + i);
                int changed = Changed(empty, cast);
                report.AppendLine(body.name + " ground shadow changed pixels: " + changed);
                check(changed > 150, "The portrait visibly shadows the real ground: " + body.name + " (" + changed + " pixels)");
                var block = new MaterialPropertyBlock(); body.GetPropertyBlock(block);
                block.SetTexture("_MainTex", clear); body.SetPropertyBlock(block);
                var transparent = CombatShadowSetup.Capture(camera, canvas, "transparent-" + i);
                int transparentPixels = Changed(empty, transparent);
                check(transparentPixels < 40, "Transparent areas do not cast a rectangular plane: " + body.name);
                body.SetPropertyBlock(originalBlocks[i]);
                body.GetPropertyBlock(block); block.SetVector("_WorldOffset", new Vector4(.45f, 0, 0, 0)); body.SetPropertyBlock(block);
                var moved = CombatShadowSetup.Capture(camera, canvas, "offset-" + i);
                check(Changed(cast, moved) > 80, "Attack and recoil offsets move the silhouette shadow: " + body.name);
                body.SetPropertyBlock(originalBlocks[i]);
                body.enabled = false;
            }
            foreach (var body in bodies) { body.enabled = true; body.shadowCastingMode = ShadowCastingMode.TwoSided; }
            CombatShadowSetup.Capture(camera, canvas, "game-2560x1440", 2560, 1440);
            CombatShadowSetup.Capture(camera, canvas, "game-1920x1200", 1920, 1200);
            check(!ShaderUtil.ShaderHasError(bodies[0].sharedMaterial.shader), "Rendered punctual-shadow shader variants compile.");
            File.WriteAllText(CombatShadowSetup.Output + "/verification.txt", "PASS\n" + report);
        }
        finally
        {
            for (int i = 0; i < bodies.Length; i++)
            {
                bodies[i].enabled = true; bodies[i].shadowCastingMode = ShadowCastingMode.TwoSided;
                bodies[i].SetPropertyBlock(originalBlocks[i]);
            }
            Object.DestroyImmediate(clear);
            Time.timeScale = timeScale;
        }
    }

    private static int Changed(Color32[] a, Color32[] b)
    {
        int count = 0;
        for (int i = 0; i < a.Length; i++)
            if (Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b) > 18) count++;
        return count;
    }
}
