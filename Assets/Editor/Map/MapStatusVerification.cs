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

// Only mutates a disposable Play Mode session, never the saved player state.
public static class MapStatusVerification
{
    public static IEnumerator Run(Action<bool, string> check)
    {
        var menu = Object.FindFirstObjectByType<MainMenuController>();
        menu.GetType().GetMethod("StartRun", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(menu, null);
        yield return Until(() => SceneManager.GetActiveScene().name == "Deinosavros" && TimeTickSystem.Active?.IsStarted == true);
        var combat = BattleHud.Find(SceneManager.GetActiveScene()).Status;
        check(combat.FormatAttackSpeed(5) == "5s", "Combat and Map share the seconds-per-attack display.");
        foreach (var enemy in BattleScript.FindFighters("Enemy")) enemy.TakeDamage(10000);
        yield return Until(() => Object.FindFirstObjectByType<RewardScreenController>()?.IsPresented == true);
        Field<Button>(Object.FindFirstObjectByType<RewardScreenController>(), "skipButton").onClick.Invoke();
        yield return ReadyMap(); yield return null;
        var map = Object.FindFirstObjectByType<MapController>();
        var session = map.Session;
        var panel = Object.FindFirstObjectByType<MapPlayerStatusPanel>();
        var view = panel.View;
        check(view != null && view.IsReady && Field<MapPlayerStatusPanel>(map, "playerStatusPanel") == panel,
            "The Map controller binds the saved status panel.");
        check(panel.transform.parent.name == "CardBar" && panel.name == "Status" && view.name == "Player",
            "The editable panel remains at MapCanvas/CardBar/Status/Player.");
        CheckValues(view, session, check);
        int count = panel.GetComponentsInChildren<Transform>(true).Length;
        var rects = panel.GetComponentsInChildren<RectTransform>(true);
        var poses = rects.Select(r => (r.anchorMin, r.anchorMax, r.anchoredPosition, r.sizeDelta)).ToArray();
        panel.Initialize(session); panel.Initialize(session); yield return null;
        check(panel.View == view && panel.GetComponentsInChildren<Transform>(true).Length == count &&
            Object.FindObjectsByType<PlayerStatusView>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == 1,
            "Repeated initialization reuses the same scene objects and references.");
        for (int i = 0; i < rects.Length; i++)
            check(poses[i] == (rects[i].anchorMin, rects[i].anchorMax, rects[i].anchoredPosition, rects[i].sizeDelta),
                "Binding preserves the authored layout: " + rects[i].name);
        var number = view.ValueLabel(StatType.AttackSpeed);
        float fontSize = number.fontSize; var style = number.fontStyle; var color = number.color; var font = number.font;
        number.fontSize += 1; number.fontStyle = FontStyles.Bold; number.color = Color.cyan;
        GameFonts.ApplyHierarchy(map.GetComponentInParent<Transform>());
        GameFonts.ApplyHierarchy(panel.GetComponentInParent<Canvas>().transform);
        check(number.font == font && number.fontSize == fontSize + 1 && number.fontStyle == FontStyles.Bold && number.color == Color.cyan,
            "Global typography preserves Inspector-authored Map status styles.");
        number.fontSize = fontSize; number.fontStyle = style; number.color = color;

        float savedInterval = session.PlayerAttackSpeed;
        var intervalField = typeof(RunSession).GetField("playerAttackSpeed", BindingFlags.Instance | BindingFlags.NonPublic);
        foreach (float interval in new[] { 5f, 2f, 1.25f, .5f })
        {
            intervalField.SetValue(session, interval); yield return null; yield return null;
            check(number.text == view.FormatAttackSpeed(interval), "Map speed follows the live interval " + interval);
            check(session.PlayerAttackSpeed == interval, "The UI does not change the actual attack interval.");
        }
        intervalField.SetValue(session, savedInterval);
        panel.gameObject.SetActive(false); panel.gameObject.SetActive(true); panel.Initialize(session);
        yield return null; CheckValues(view, session, check);
        check(panel.View == view && panel.GetComponentsInChildren<Transform>(true).Length == count,
            "Re-enabling the saved panel does not construct another status UI.");
        var canvas = panel.GetComponentInParent<Canvas>().rootCanvas;
        Capture(panel, canvas, Camera.main, check);
        foreach (var text in panel.GetComponentsInChildren<TMP_Text>(true))
            check(AssetDatabase.Contains(text.font), "The saved font is retained after scene initialization: " + text.name);
        int hp = session.PlayerHealth; string runId = session.Progress.RunId;
        SceneManager.LoadScene("Map"); yield return ReadyMap(); yield return null;
        panel = Object.FindFirstObjectByType<MapPlayerStatusPanel>();
        check(RunSession.Instance == session && session.Progress.RunId == runId && session.PlayerHealth == hp,
            "Map reload preserves the same run and player state.");
        check(Object.FindObjectsByType<MapPlayerStatusPanel>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == 1,
            "Map reload restores exactly one authored panel.");
        CheckValues(panel.View, session, check);
    }

    private static void CheckValues(PlayerStatusView view, RunSession session, Action<bool, string> check)
    {
        check(view.Health.Number.text == $"{session.PlayerHealth:0} / {session.PlayerMaxHealth:0}", "Map health shows current and maximum health.");
        check(view.Elixir.Number.text == $"{session.PlayerElixir:0.0} / {session.PlayerMaxElixir:0}", "Map Elixir shows current and maximum resource.");
        check(view.ValueLabel(StatType.Damage).text == session.PlayerDamage.ToString(), "Map damage tracks the session.");
        check(view.ValueLabel(StatType.Shield).text == Mathf.Max(0, session.PlayerShield).ToString(), "Map shield tracks the session.");
        check(view.ValueLabel(StatType.AttackSpeed).text == view.FormatAttackSpeed(session.PlayerAttackSpeed), "Map attack speed uses the configured explicit units.");
    }

    private static void Capture(MapPlayerStatusPanel panel, Canvas canvas, Camera camera, Action<bool, string> check)
    {
        var scaler = canvas.GetComponent<CanvasScaler>(); var rect = (RectTransform)canvas.transform;
        var mode = canvas.renderMode; var assignedCamera = canvas.worldCamera; bool scaleEnabled = scaler.enabled;
        var position = rect.localPosition; var rotation = rect.localRotation; var localScale = rect.localScale; var delta = rect.sizeDelta;
        float scale = canvas.scaleFactor, aspect = camera.aspect; var target = camera.targetTexture; var active = RenderTexture.active;
        try
        {
            scaler.enabled = false; canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = camera; canvas.scaleFactor = 1;
            foreach (var size in new[] { new Vector2Int(1920,1080), new Vector2Int(2560,1440), new Vector2Int(1920,1200) })
            {
                camera.aspect = (float)size.x / size.y;
                float uiScale = Mathf.Sqrt(size.x / 1920f * size.y / 1080f);
                float height = size.y / uiScale;
                rect.sizeDelta = new Vector2(size.x / uiScale, height);
                float distance = camera.nearClipPlane + 1;
                rect.position = camera.transform.position + camera.transform.forward * distance; rect.rotation = camera.transform.rotation;
                rect.localScale = Vector3.one * (2 * distance * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * .5f) / height);
                Canvas.ForceUpdateCanvases();
                var corners = new Vector3[4]; ((RectTransform)panel.transform).GetWorldCorners(corners);
                foreach (var point in corners)
                {
                    var v = camera.WorldToViewportPoint(point);
                    check(v.x >= 0 && v.x < .15f && v.y >= 0 && v.y <= .26f,
                        "Status stays inside the left card-bar area at " + size);
                }
                foreach (var text in panel.GetComponentsInChildren<TMP_Text>())
                {
                    text.ForceMeshUpdate();
                    check(!text.isTextOverflowing, "Map status text fits at " + size + ": " + text.name);
                }
                var rt = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGB32); Texture2D image = null;
                try
                {
                    rt.Create(); camera.targetTexture = rt;
                    RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
                    RenderTexture.active = rt; image = new Texture2D(size.x,size.y,TextureFormat.RGB24,false);
                    image.ReadPixels(new Rect(0,0,size.x,size.y),0,0); image.Apply();
                    Directory.CreateDirectory(MapStatusSceneAuthoring.Results);
                    File.WriteAllBytes(MapStatusSceneAuthoring.Results + "/status-" + size.x + "x" + size.y + ".png", image.EncodeToPNG());
                }
                finally { camera.targetTexture = target; RenderTexture.active = active; if (image != null) Object.DestroyImmediate(image); rt.Release(); Object.DestroyImmediate(rt); }
            }
        }
        finally
        {
            camera.aspect = aspect; canvas.renderMode = mode; canvas.worldCamera = assignedCamera; canvas.scaleFactor = scale;
            rect.localPosition = position; rect.localRotation = rotation; rect.localScale = localScale; rect.sizeDelta = delta;
            scaler.enabled = scaleEnabled; Canvas.ForceUpdateCanvases();
        }
    }

    private static IEnumerator ReadyMap() => Until(() => SceneManager.GetActiveScene().name == "Map" && Object.FindFirstObjectByType<MapController>()?.CanInteract == true);
    private static IEnumerator Until(Func<bool> condition)
    { float end = Time.realtimeSinceStartup + 60; while (!condition()) { if (Time.realtimeSinceStartup > end) throw new TimeoutException("Map status verification timed out."); yield return null; } }
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
}
