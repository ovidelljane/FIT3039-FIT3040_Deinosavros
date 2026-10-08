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

public static class CombatAttackCountdownVerification
{
    public static IEnumerator Run(Action<bool, string> check)
    {
        var menu = Object.FindFirstObjectByType<MainMenuController>();
        menu.GetType().GetMethod("StartRun", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(menu, null);
        yield return Wait(() => SceneManager.GetActiveScene().name == "Deinosavros" && BattleHud.Find(SceneManager.GetActiveScene())?.CountdownArmed == true);
        var views = Object.FindObjectsByType<AttackCountdownView>(FindObjectsSortMode.None);
        check(views.Length == 4 && views.All(v => v.IsReady), "All four fighters use complete authored countdowns.");
        var clock = TimeTickSystem.Active; var hud = BattleHud.Find(SceneManager.GetActiveScene());
        var player = BattleHud.FindPlayer(SceneManager.GetActiveScene());
        var playerView = views.Single(v => v.Actor == player);
        var enemyView = views.First(v => v.Actor.CompareTag("Enemy"));
        float interval = player.attackSpd; int damage = player.attackDmg;
        yield return null;
        check(!clock.IsStarted && Mathf.Approximately(playerView.RemainingSeconds, interval), "The timer waits at the full interval before combat starts.");
        yield return Wait(() => clock.IsStarted); yield return Delay(.35f);
        check(playerView.RemainingSeconds < interval && playerView.RemainingSeconds > 0, "The real battle clock decreases the countdown.");
        foreach (var view in views) CheckClock(view, clock, check);
        Time.timeScale = 0; yield return null;
        float paused = playerView.RemainingSeconds;
        var pausedText = Field<TMP_Text>(playerView, "seconds").text;
        yield return Delay(.25f);
        check(playerView.RemainingSeconds == paused && Field<TMP_Text>(playerView, "seconds").text == pausedText,
            "Pausing freezes both the line and the remaining-time text.");
        Time.timeScale = 1;

        int attacks = 0;
        Action<BattleScript, BattleScript> count = (actor, target) => { if (actor == player) attacks++; };
        BattleScript.OnAttackPerformed += count;
        try
        {
            // Shorter intervals use the same accumulated real attack time, without a UI-side reset.
            player.attackSpd = 1.25f; yield return null; yield return null;
            CheckClock(playerView, clock, check);
            yield return Wait(() => attacks > 0); yield return null;
            check(playerView.RemainingSeconds > .8f && playerView.RemainingSeconds <= 1.25f, "An actual attack resets the countdown to its current interval.");
            var elapsed = player.SecondsUntilNextAttack;
            playerView.enabled = false; playerView.enabled = true; yield return null;
            check(player.SecondsUntilNextAttack <= elapsed && playerView.IsReady, "Re-enabling the view never resets the actor clock.");
            var enemy = enemyView.Actor; float oldEnemyInterval = enemy.attackSpd;
            enemy.attackSpd += 2; yield return null; yield return null;
            CheckClock(enemyView, clock, check);
            check(enemyView.RemainingSeconds > 2, "Enemy slow immediately extends its displayed attack countdown.");
            enemy.attackSpd = oldEnemyInterval;
            player.attackSpd = interval; yield return null; yield return null;
            check(player.attackDmg == damage, "The timer does not change attack damage or combat formulas.");
            foreach (var enemyActor in views.Where(v => v.Actor.CompareTag("Enemy")).Select(v => v.Actor))
            {
                var effect = new GameObject("Verification Slow").AddComponent<Effect>();
                effect.SetValues(StatType.EnemySlow, enemyActor, 2, 8);
            }
            yield return Delay(.25f);
            Capture(hud.Canvas, Camera.main, views, check);
            var victims = BattleScript.FindFighters("Enemy"); victims[0].TakeDamage(10000); yield return null; yield return null;
            check(Field<CanvasGroup>(views.Single(v => v.Actor == victims[0]), "visibility").alpha == 0,
                "A dead enemy cannot display another upcoming attack.");
            foreach (var remainingEnemy in victims.Skip(1)) remainingEnemy.TakeDamage(10000);
            yield return Wait(() => Object.FindFirstObjectByType<RewardScreenController>()?.IsPresented == true); yield return null;
            check(views.All(v => Field<CanvasGroup>(v, "visibility").alpha == 0), "Victory removes all upcoming-attack timers.");
        }
        finally { BattleScript.OnAttackPerformed -= count; Time.timeScale = 1; }
    }

    private static void CheckClock(AttackCountdownView view, TimeTickSystem clock, Action<bool, string> check)
    {
        float expected = Mathf.Max(0, view.Actor.SecondsUntilNextAttack - clock.SecondsSinceLastTick);
        check(Mathf.Abs(view.RemainingSeconds - expected) <= clock.TickInterval + .03f, "Countdown follows the simulation for " + view.Actor.name);
        var fill = Field<Image>(view, "fill");
        check(Mathf.Abs(fill.rectTransform.anchorMax.x - view.RemainingSeconds / view.Actor.AttackIntervalSeconds) < .001f,
            "The line and number represent the same remaining interval.");
    }

    public static void Capture(Canvas canvas, Camera camera, AttackCountdownView[] views, Action<bool, string> check)
    {
        foreach (var size in new[] { new Vector2Int(1920,1080), new Vector2Int(2560,1440), new Vector2Int(1920,1200) })
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
                foreach (var view in views)
                {
                    var corners = new Vector3[4]; ((RectTransform)view.transform).GetWorldCorners(corners);
                    foreach (var corner in corners)
                    {
                        var vp = camera.WorldToViewportPoint(corner);
                        check(vp.x > 0 && vp.x < 1 && vp.y > .26f && vp.y < 1, "Attack timer stays clear of the hand and screen edges at " + size);
                    }
                    foreach (var text in view.GetComponentsInChildren<TMP_Text>())
                    { text.ForceMeshUpdate(); check(!text.isTextOverflowing, "Attack countdown text fits: " + text.text); }
                    var area = ViewportBounds((RectTransform)view.transform, camera);
                    foreach (var other in views)
                    {
                        check(!area.Overlaps(ViewportBounds((RectTransform)other.transform.parent, camera)),
                            "Attack timers do not overlap any existing status panel at " + size);
                        if (view != other) check(!area.Overlaps(ViewportBounds((RectTransform)other.transform, camera)),
                            "Attack timers do not overlap each other at " + size);
                    }
                    foreach (var badge in canvas.GetComponentsInChildren<CardBuffBadge>())
                    {
                        check(!area.Overlaps(ViewportBounds((RectTransform)badge.transform, camera)), "Attack timer avoids buff rings.");
                        if (badge.StackCount > 0) check(!area.Overlaps(ViewportBounds(badge.seconds.rectTransform, camera)), "Attack timer avoids buff seconds.");
                    }
                }
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
                RenderTexture.active = rt; image = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0); image.Apply();
                Directory.CreateDirectory(CombatAttackCountdownAuthoring.Results);
                File.WriteAllBytes(CombatAttackCountdownAuthoring.Results + "/combat-" + size.x + "x" + size.y + ".png", image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = target; camera.aspect = aspect; RenderTexture.active = active;
                canvas.renderMode = mode; canvas.worldCamera = previousCamera; canvas.planeDistance = plane; Canvas.ForceUpdateCanvases();
                if (image != null) Object.DestroyImmediate(image); rt.Release(); Object.DestroyImmediate(rt);
            }
        }
    }

    private static Rect ViewportBounds(RectTransform rect, Camera camera)
    {
        var corners = new Vector3[4]; rect.GetWorldCorners(corners);
        var a = camera.WorldToViewportPoint(corners[0]); var b = camera.WorldToViewportPoint(corners[2]);
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
    }

    private static IEnumerator Wait(Func<bool> ready)
    { float limit = Time.realtimeSinceStartup + 50; while (!ready()) { if (Time.realtimeSinceStartup > limit) throw new TimeoutException("Attack timer check timed out."); yield return null; } }
    private static IEnumerator Delay(float seconds) { float limit = Time.realtimeSinceStartup + seconds; while (Time.realtimeSinceStartup < limit) yield return null; }
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(value);
}
