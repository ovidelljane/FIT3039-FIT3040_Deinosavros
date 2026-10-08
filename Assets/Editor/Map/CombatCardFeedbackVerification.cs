using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// All mutations are confined to a disposable Play Mode session.
public static class CombatCardFeedbackVerification
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private).Invoke(target, args);
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
    private static IEnumerator Wait(Func<bool> predicate)
    {
        double end = EditorApplication.timeSinceStartup + 50;
        while (!predicate())
        {
            if (EditorApplication.timeSinceStartup > end) throw new InvalidOperationException("Card feedback verification timed out.");
            yield return null;
        }
    }
    private static IEnumerator Delay(float seconds)
    {
        double end = EditorApplication.timeSinceStartup + seconds;
        while (EditorApplication.timeSinceStartup < end) yield return null;
    }
    private static void ClearEffects()
    {
        foreach (var effect in Effect.ActiveEffects.ToArray())
        { effect.enabled = false; Object.Destroy(effect.gameObject); }
    }
    private static Effect AddEffect(BattleScript actor, StatType type, float amount, float duration)
    {
        var effect = new GameObject("Verification Effect").AddComponent<Effect>();
        effect.SetValues(type, actor, amount, duration); return effect;
    }
    private static BuffCards Card(DeckManager deck, CardDefinition definition)
    {
        var card = Object.Instantiate(definition.combatPrefab, deck.Hand[0].transform.parent).GetComponent<BuffCards>();
        card.Initialize(deck, definition);
        ((List<BuffCards>)typeof(DeckManager).GetField("hand", Private).GetValue(deck)).Add(card);
        return card;
    }
    private static CardBuffBadge Badge(CombatCardFeedback feedback, BattleScript actor, StatType stat) =>
        feedback.targets.Single(t => t.actor == actor).badges.Single(b => b.stat == stat);
    private static bool Popup(CombatCardFeedback feedback, string text) =>
        feedback.popupLayer.GetComponentsInChildren<TMP_Text>().Any(t => t.text == text);

    public static IEnumerator Run(Action<bool, string> check)
    {
        yield return Wait(() => Object.FindFirstObjectByType<MainMenuController>() != null);
        Call(Object.FindFirstObjectByType<MainMenuController>(), "StartRun");
        yield return Wait(() => BattleHud.Find(SceneManager.GetActiveScene())?.Deck?.CanPlay == true);
        var hud = BattleHud.Find(SceneManager.GetActiveScene()); var deck = hud.Deck; var player = deck.Player;
        var feedback = Object.FindFirstObjectByType<CombatCardFeedback>(); var clock = TimeTickSystem.Active;
        var enemies = BattleScript.FindFighters("Enemy");
        check(feedback != null && feedback.IsReady, "Formal combat has saved feedback references.");
        check(feedback.targets.Length == 4 && feedback.targets.Sum(t => t.badges.Length) == 7, "Seven bounded, scene-owned buff slots.");
        var authoredBadges = feedback.targets.SelectMany(t => t.badges).ToArray();
        var badgePositions = authoredBadges.Select(b => ((RectTransform)b.transform).anchoredPosition).ToArray();
        var badgeSizes = authoredBadges.Select(b => ((RectTransform)b.transform).sizeDelta).ToArray();
        check(authoredBadges.All(b => b.visibility.alpha == 0 && !b.hitArea.raycastTarget && b.seconds.text == "" && b.stacks.text == ""),
            "Saved editor previews are hidden before any real effect is applied.");
        check(authoredBadges.All(b => ((RectTransform)b.transform).sizeDelta == new Vector2(52, 52) &&
            b.seconds.fontSize == 24 && b.stacks.fontSize == 20), "Larger authored sizes and fonts survive runtime typography initialization.");
        foreach (var enemy in enemies) { enemy.health = enemy.maxHealth = 10000; enemy.attackDmg = 0; enemy.attackSpd = 10000; }
        var receipts = new List<CardEffectFeedback>();
        void Receive(CardEffectFeedback receipt) => receipts.Add(receipt);
        CardEffectFeedback.Changed += Receive;
        var pointer = new PointerEventData(EventSystem.current);
        var definitions = AssetDatabase.LoadAssetAtPath<CardPool>("Assets/Data/CardPool.asset").Cards;
        Color originalDamage = hud.Status.ValueLabel(StatType.Damage).color;
        try
        {
            foreach (var definition in definitions)
            {
                ClearEffects();
                player.health = 80; player.maxHealth = 100; player.elixir = player.maxElixir = 10;
                player.attackDmg = 6; player.attackSpd = 5; player.elixirRegen = 0; player.hitsPerAttack = 1; player.shield = 0;
                var card = Card(deck, definition); yield return null;
                var values = card.Values;
                float before = CardEffectFeedback.Read(player, values.Stat);
                float hp = player.health, energy = player.elixir;
                receipts.Clear(); int handCount = deck.Hand.Count;
                Time.timeScale = 0;
                card.OnPointerEnter(pointer);
                card.OnPointerClick(pointer); card.OnPointerClick(pointer);
                check(deck.Hand.Count == handCount - 1, "One discard on repeated click: " + definition.cardId);
                check(receipts.Count(r => r.Phase == CardFeedbackPhase.Cost) == (values.Cost > 0 ? 1 : 0), "One cost receipt: " + definition.cardId);
                var applied = receipts.Where(r => r.Phase == CardFeedbackPhase.Applied).ToArray();
                check(applied.Length == (values.Stat == StatType.DamageAllEnemies ? 0 : values.Stat == StatType.EnemySlow ? 3 : 1),
                    "One application per target; AoE reuses damage feedback: " + definition.cardId);
                check(hud.Status.PreviewCard == null, "Playing clears the prior hover preview.");
                if (values.Stat == StatType.Elixir)
                    check(player.health == hp - values.Cost && player.elixir == 10 && Popup(feedback, "Full Elixir"),
                        "Health cost and capped Elixir gain stay separate.");
                else check(Mathf.Approximately(player.elixir, energy - values.Cost), "The exact Elixir cost is retained.");
                if (values.Stat == StatType.AttackSpeed)
                    check(Mathf.Approximately(player.attackSpd, before - values.Amount), "Feedback leaves interval arithmetic unchanged.");
                else if (values.Stat == StatType.Damage || values.Stat == StatType.ExtraHits || values.Stat == StatType.ElixirRegen || values.Stat == StatType.Shield)
                    check(Mathf.Approximately(CardEffectFeedback.Read(player, values.Stat), before + values.Amount), "Feedback does not apply the bonus twice.");
                yield return null;
                if (values.Duration > 0 && values.Stat != StatType.DamageAllEnemies)
                {
                    var badge = Badge(feedback, values.Stat == StatType.EnemySlow ? enemies[0] : player, values.Stat);
                    check(badge.StackCount == 1 && badge.RemainingSeconds > 0, "The real effect powers its ring: " + definition.cardId);
                }
                Time.timeScale = 1; yield return Delay(.28f);
                if (values.Stat == StatType.Damage)
                    check(hud.Status.ValueLabel(StatType.Damage).text == player.attackDmg.ToString() &&
                        hud.Status.ValueLabel(StatType.Damage).color == feedback.activeColor, "Damage settles to the true value and stays blue.");
                check(feedback.ActivePopupCount <= feedback.poolSize, "The floating-number pool remains bounded.");
            }

            ClearEffects(); player.attackDmg = 6; player.attackSpd = 5; player.elixirRegen = 0;
            yield return Delay(.9f);
            var first = AddEffect(player, StatType.Damage, 3, 2);
            var second = AddEffect(player, StatType.Damage, 7, 4);
            yield return Delay(.28f);
            var damageBadge = Badge(feedback, player, StatType.Damage);
            check(player.attackDmg == 16 && damageBadge.StackCount == 2 && damageBadge.stacks.text == "x2", "Stacked damage remains additive with one grouped icon.");
            check(Mathf.Abs(damageBadge.RemainingSeconds - first.RemainingSeconds) < .11f, "The ring selects the next expiration.");
            Canvas.ForceUpdateCanvases();
            var badgePointer = new PointerEventData(EventSystem.current)
            {
                position = RectTransformUtility.WorldToScreenPoint(hud.Canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : hud.Canvas.worldCamera,
                    damageBadge.transform.position)
            };
            var badgeHits = new List<RaycastResult>(); EventSystem.current.RaycastAll(badgePointer, badgeHits);
            check(badgeHits.Count > 0 && badgeHits[0].gameObject.GetComponentInParent<CardBuffBadge>() == damageBadge,
                "Only the active badge overrides the non-interactive status root for hover.");
            damageBadge.OnPointerEnter(pointer);
            check(feedback.tooltip.gameObject.activeSelf && feedback.tooltipText.text.Contains("+3 damage") &&
                feedback.tooltipText.text.Contains("+7 damage"), "Hover lists independent amounts and timers.");
            damageBadge.OnPointerExit(pointer);
            float remaining = first.RemainingSeconds; float ring = damageBadge.ring.remaining;
            Time.timeScale = 0; yield return Delay(.3f);
            check(first.RemainingSeconds == remaining && damageBadge.ring.remaining == ring, "Pause freezes effect time and ring together.");
            Time.timeScale = 1;
            Call(clock, "Advance", 2f); yield return Delay(.25f);
            check(player.attackDmg == 13 && damageBadge.StackCount == 1 && second.IsApplied, "First expiry removes only its own contribution.");
            Call(clock, "Advance", 2f); yield return Delay(.3f);
            check(player.attackDmg == 6 && damageBadge.StackCount == 0 && hud.Status.ValueLabel(StatType.Damage).color == originalDamage,
                "Last expiry restores the remaining true value and authored color.");

            player.attackSpd = 5; yield return Delay(.25f);
            var speed = AddEffect(player, StatType.AttackSpeed, 3, 6); yield return Delay(.28f);
            check(Popup(feedback, "-3s Interval") && hud.Status.ValueLabel(StatType.AttackSpeed).text == "2s" && player.attackSpd == 2,
                "Five-second to two-second interval shows -3s Interval and 2s without changing combat arithmetic.");
            var speedBadge = Badge(feedback, player, StatType.AttackSpeed); speedBadge.OnPointerEnter(pointer);
            check(feedback.tooltipText.text.Contains("Interval -3s"), "The speed tooltip retains the card's interval unit.");
            speedBadge.OnPointerExit(pointer);
            clock.StopTimer(); remaining = speed.RemainingSeconds; yield return Delay(.2f);
            check(speed.RemainingSeconds == remaining, "Stopping the simulation also pauses timers.");
            clock.StartTimer();
            var stackedSpeed = AddEffect(player, StatType.AttackSpeed, .5f, 12); yield return Delay(.28f);
            check(hud.Status.ValueLabel(StatType.AttackSpeed).text == "1.5s" && player.attackSpd == 1.5f,
                "Stacked speed effects retain fractional seconds per attack.");
            Call(clock, "Advance", 6f); yield return Delay(.28f);
            check(!speed.IsApplied && stackedSpeed.IsApplied && player.attackSpd == 4.5f &&
                hud.Status.ValueLabel(StatType.AttackSpeed).text == "4.5s", "Partial expiry displays the remaining live interval.");
            Call(clock, "Advance", 6f); yield return Delay(.28f);
            check(player.attackSpd == 5 && hud.Status.ValueLabel(StatType.AttackSpeed).text == "5s",
                "Final expiry returns to five seconds per attack, not attacks per second.");
            ClearEffects();

            foreach (int health in new[] { 70, 98, 100 })
            {
                var heal = Card(deck, definitions.First(d => d.BaseValues.Stat == StatType.Shield));
                Set(heal, "stat", StatType.Heal); Set(heal, "amount", 15f); Set(heal, "elixirCost", 0);
                yield return null; player.health = health; player.maxHealth = 100;
                receipts.Clear(); heal.OnPointerClick(pointer);
                int gain = Mathf.Min(15, 100 - health);
                check(player.health == health + gain && receipts.Single(r => r.Phase == CardFeedbackPhase.Applied).After - health == gain,
                    "Healing uses the actual clamped gain at " + health + " HP.");
                check(Popup(feedback, gain > 0 ? "+" + gain + " HP" : "Full HP"), "Healing feedback matches its actual outcome.");
                yield return Delay(.3f);
            }
            var insufficient = Card(deck, definitions.First(d => d.BaseValues.Cost > 0 && d.BaseValues.Stat == StatType.Damage));
            yield return null; player.elixir = 0; receipts.Clear();
            insufficient.OnPointerClick(pointer);
            check(receipts.Count == 0 && deck.Hand.Contains(insufficient), "Unaffordable plays do not emit success or consume a card.");
            player.elixir = 10; insufficient.OnPointerEnter(pointer); yield return null;
            check(hud.Status.Elixir.Preview.gameObject.activeSelf, "Cost hover remains independent of feedback bands.");
            insufficient.OnPointerExit(pointer);

            var shieldCard = Card(deck, definitions.First(d => d.BaseValues.Stat == StatType.Shield));
            yield return null; player.elixir = 10; player.shield = 0; int hpBefore = player.health;
            shieldCard.OnPointerClick(pointer); int shieldGranted = player.shield;
            player.TakeDamage(2, enemies[0]); yield return Delay(.28f);
            check(player.health == hpBefore && player.shield == shieldGranted - 2 && hud.Status.ValueLabel(StatType.Shield).text == player.shield.ToString(),
                "Hits during the shield animation retarget to the true remaining shield.");

            ClearEffects(); int original = player.attackDmg; int notifications = feedback.NotificationCount;
            var removed = AddEffect(player, StatType.Damage, 2, 6); int afterApplied = feedback.NotificationCount;
            removed.enabled = false; Object.Destroy(removed.gameObject);
            check(player.attackDmg == original && afterApplied == notifications + 1 && feedback.NotificationCount == afterApplied,
                "Cleanup removes the bonus once without an expiry presentation.");
            feedback.enabled = false;
            check(hud.Status.ValueLabel(StatType.Damage).text == player.attackDmg.ToString(), "Disabling during a tween restores the exact live label.");
            check(hud.Status.ValueLabel(StatType.AttackSpeed).text == hud.Status.FormatAttackSpeed(player.attackSpd),
                "Disabling feedback preserves the interval unit.");
            feedback.enabled = true;
            notifications = feedback.NotificationCount;
            AddEffect(player, StatType.Damage, 2, 6);
            check(feedback.NotificationCount == notifications + 1, "Re-enabling does not duplicate event subscriptions.");
            ClearEffects();

            // A controlled presentation sample, using the actual game camera and complete UI.
            player.health = 72; player.maxHealth = 100; player.elixir = 6; player.maxElixir = 10;
            player.attackDmg = 9; player.attackSpd = 5; player.shield = 5;
            foreach (var enemy in enemies) { enemy.health = 70; enemy.maxHealth = 100; }
            AddEffect(player, StatType.AttackSpeed, 3, 6); AddEffect(player, StatType.ExtraHits, 2, 6);
            AddEffect(player, StatType.ElixirRegen, 1, 6);
            foreach (var enemy in enemies) AddEffect(enemy, StatType.EnemySlow, 2, 6);
            yield return Delay(.85f);
            AddEffect(player, StatType.Damage, 4, 6); yield return Delay(.24f);
            Time.timeScale = 0;
            foreach (var size in new[] { new Vector2Int(1920, 1080), new Vector2Int(2560, 1440), new Vector2Int(1920, 1200) })
            {
                Capture(hud.Canvas, Camera.main, size, "active", check);
                check(feedback.popupLayer.GetComponentsInChildren<Graphic>().All(g => !g.raycastTarget), "Floating values never intercept input.");
                check(feedback.targets.SelectMany(t => t.badges).All(b => b.StackCount == 1 && b.ring.canvasRenderer.GetMesh().vertexCount > 0),
                    "All active rings have visible geometry at " + size);
            }
            damageBadge.OnPointerEnter(pointer); Capture(hud.Canvas, Camera.main, new Vector2Int(1920, 1080), "tooltip");
            damageBadge.OnPointerExit(pointer);
            Time.timeScale = 1; ClearEffects(); yield return Delay(.9f);
            check(feedback.ActivePopupCount == 0 && feedback.targets.SelectMany(t => t.badges).All(b => b.StackCount == 0),
                "All transient UI settles after effects are cleared.");
            check(feedback.targets.SelectMany(t => t.badges).SequenceEqual(authoredBadges) &&
                hud.StatusRoot.GetComponentsInChildren<CardBuffBadge>(true).Length == authoredBadges.Length,
                "Runtime reuses the seven hierarchy objects without creating countdown copies.");
            check(authoredBadges.Select((b, i) => ((RectTransform)b.transform).anchoredPosition == badgePositions[i] &&
                ((RectTransform)b.transform).sizeDelta == badgeSizes[i]).All(value => value),
                "Effects, expiry and re-enabling never overwrite the authored countdown layout.");

            player.shield = 0; player.TakeDamage(player.health + 1, enemies[0]); yield return null;
            check(feedback.Ended && feedback.ActivePopupCount == 0 && !feedback.tooltip.gameObject.activeSelf,
                "Battle end clears card presentation without altering the result.");
            ClearEffects();
            SceneManager.LoadSceneAsync("MainMenu");
            yield return Wait(() => Object.FindFirstObjectByType<MainMenuController>() != null);
            check(Effect.ActiveEffects.Count == 0, "Leaving combat clears the effect registry.");
            Call(Object.FindFirstObjectByType<MainMenuController>(), "StartRun");
            yield return Wait(() => BattleHud.Find(SceneManager.GetActiveScene())?.Deck?.CanPlay == true);
            feedback = Object.FindFirstObjectByType<CombatCardFeedback>();
            check(feedback.ActivePopupCount == 0 && feedback.NotificationCount == 0 &&
                feedback.targets.SelectMany(t => t.badges).All(b => b.StackCount == 0), "A fresh combat has no stale popups, buffs or event notifications.");
            check(feedback.popupLayer.childCount == feedback.poolSize + 1, "Reload creates one bounded pool from its saved template.");
        }
        finally { CardEffectFeedback.Changed -= Receive; Time.timeScale = 1; ClearEffects(); }
    }

    private static void Capture(Canvas canvas, Camera camera, Vector2Int size, string label, Action<bool, string> check = null)
    {
        Directory.CreateDirectory("Library/CardFeedback");
        var target = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGB32);
        var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active;
        var oldMode = canvas.renderMode; var oldCamera = canvas.worldCamera;
        float oldPlane = canvas.planeDistance, oldAspect = camera.aspect;
        Texture2D image = null;
        try
        {
            target.Create(); camera.targetTexture = target; camera.aspect = (float)size.x / size.y;
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera;
            canvas.planeDistance = camera.nearClipPlane + .5f; Canvas.ForceUpdateCanvases();
            if (check != null)
            {
                var speedValue = canvas.GetComponentInChildren<PlayerStatusView>(true).ValueLabel(StatType.AttackSpeed);
                speedValue.ForceMeshUpdate();
                check(speedValue.text == "2s" && !speedValue.isTextOverflowing,
                    "The interval value remains readable at " + size + ": " + speedValue.text +
                    " / preferred " + speedValue.preferredWidth + "x" + speedValue.preferredHeight +
                    " / rect " + speedValue.rectTransform.rect.size);
                foreach (var badge in canvas.GetComponentsInChildren<CardBuffBadge>(true))
                {
                    Rect circle = ScreenRect((RectTransform)badge.transform, camera);
                    Rect text = ScreenRect(badge.seconds.rectTransform, camera);
                    Rect panel = ScreenRect((RectTransform)badge.transform.parent.parent, camera);
                    check(circle.xMin >= 0 && circle.xMax <= size.x && circle.yMin >= 0 && circle.yMax <= size.y &&
                        text.xMin >= 0 && text.xMax <= size.x && text.yMin >= 0 && text.yMax <= size.y,
                        "Countdown ring and seconds fit the viewport: " + badge.actor.name + " / " + badge.stat + " / " + size);
                    check(!circle.Overlaps(panel) && !text.Overlaps(panel) && !circle.Overlaps(text),
                        "The enlarged countdown does not cover its health panel or its seconds: " + badge.stat);
                    badge.seconds.ForceMeshUpdate(); badge.stacks.ForceMeshUpdate();
                    check(!badge.seconds.isTextOverflowing && (string.IsNullOrEmpty(badge.stacks.text) || !badge.stacks.isTextOverflowing),
                        "Larger countdown text is not clipped: " + badge.stat + " / seconds " + badge.seconds.preferredHeight +
                        " / stacks " + badge.stacks.preferredHeight);
                }
            }
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture.active = target; image = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0); image.Apply();
            File.WriteAllBytes("Library/CardFeedback/" + label + "-" + size.x + "x" + size.y + ".png", image.EncodeToPNG());
        }
        finally
        {
            canvas.renderMode = oldMode; canvas.worldCamera = oldCamera; canvas.planeDistance = oldPlane;
            camera.targetTexture = oldTarget; camera.aspect = oldAspect; RenderTexture.active = oldActive;
            if (image != null) Object.DestroyImmediate(image); target.Release(); Object.DestroyImmediate(target);
            Canvas.ForceUpdateCanvases();
        }
    }

    private static Rect ScreenRect(RectTransform rect, Camera camera)
    {
        var corners = new Vector3[4]; rect.GetWorldCorners(corners);
        Vector2 min = new(float.PositiveInfinity, float.PositiveInfinity), max = new(float.NegativeInfinity, float.NegativeInfinity);
        foreach (var corner in corners)
        {
            Vector2 point = RectTransformUtility.WorldToScreenPoint(camera, corner);
            min = Vector2.Min(min, point); max = Vector2.Max(max, point);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }
}
