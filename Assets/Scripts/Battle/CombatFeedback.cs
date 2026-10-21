using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Visual feedback observes damage after calculation; it never changes combat time or stats.
[DefaultExecutionOrder(600)]
[DisallowMultipleComponent]
public sealed class CombatFeedback : MonoBehaviour
{
    public Canvas canvas;
    public Camera worldCamera;
    [Header("Damage Numbers")]
    [Range(8, 64)] public int numberPoolSize = 40;
    [Min(.2f)] public float numberLifetime = 1.05f;
    [Min(16)] public float numberSize = 44;
    public float numberRise = 90;
    public Color damageColor = new(1, .89f, .63f);
    public Color playerDamageColor = new(1, .4f, .32f);
    public Color shieldColor = new(.5f, .85f, 1);
    [Header("Impact")]
    [Range(.05f, .4f)] public float flashDuration = .17f;
    [Range(0, 1)] public float flashStrength = .72f;
    [Range(0, 1)] public float attackLunge = .32f;
    [Min(.05f)] public float attackDuration = .22f;
    [Range(0, 1)] public float impactStrength = .7f;
    public Color hitTint = new(1, .87f, .62f);
    [Header("Hit Recoil")]
    [Tooltip("Maximum visual displacement for hits of 10 damage or more. Gameplay positions stay fixed.")]
    [Range(0, .6f)] public float recoilDistance = .18f;
    [Tooltip("Fraction of maximum recoil for hits of 1 to 4 damage, including damage absorbed by shields.")]
    [Range(0, 1)] public float lightRecoilScale = .2f;
    [Tooltip("Fraction of maximum recoil for hits of 5 to 9 damage.")]
    [Range(0, 1)] public float mediumRecoilScale = .55f;
    [Tooltip("Scales all enemy hit recoil tiers, including Bosses. Does not affect player recoil or damage.")]
    [Range(0, 3)] public float enemyRecoilMultiplier = 1.4f;
    [Min(.05f)] public float recoilDuration = .24f;

    private static CombatFeedback active;
    private readonly Dictionary<BattleScript, ActorVisual> actors = new();
    private Popup[] pool;
    private RectTransform layer;
    private int nextNumber, sequence;
    public int ActiveNumberCount { get; private set; }
    public int DamageEventCount { get; private set; }
    public static bool IsPresent(Scene scene) => active != null && active.isActiveAndEnabled && active.gameObject.scene == scene;

    private void OnEnable()
    {
        active = this;
        BattleScript.OnDamageReceived += OnDamage;
        BattleScript.OnAttackPerformed += OnAttack;
    }

    private void Start()
    {
        if (canvas == null) canvas = BattleHud.Find(gameObject.scene)?.Canvas;
        if (worldCamera == null) worldCamera = Camera.main;
        if (canvas == null || worldCamera == null) { enabled = false; return; }
        layer = PlayerStatusView.Rect("Impacts", canvas.transform);
        layer.anchorMin = Vector2.zero; layer.anchorMax = Vector2.one;
        layer.offsetMin = layer.offsetMax = Vector2.zero;
        // Above world-space status labels, below the hand, details and victory panel.
        var hand = canvas.transform.Find("HandContainer");
        layer.SetSiblingIndex(hand != null ? hand.GetSiblingIndex() : 0);
        var group = layer.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = group.interactable = false;
        pool = new Popup[Mathf.Clamp(numberPoolSize, 8, 64)];
        for (int i = 0; i < pool.Length; i++)
        {
            var rect = PlayerStatusView.Rect("Hit" + (i + 1).ToString("00"), layer);
            rect.sizeDelta = new Vector2(230, 110);
            var text = PlayerStatusView.Text(rect, "Value", "", numberSize, GameFontRole.Numeric);
            text.fontStyle = FontStyles.Bold;
            text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
            text.outlineColor = new Color(.11f, .055f, .025f); text.outlineWidth = .18f;
            text.fontMaterial.EnableKeyword("OUTLINE_ON");
            var marker = PlayerStatusView.Icon(rect, StatusSymbol.Shield, shieldColor);
            PlayerStatusView.Place(marker.rectTransform, new Vector2(.5f,.5f), new Vector2(-36,0), new Vector2(22,24));
            var burst = PlayerStatusView.Rect("Burst", rect).gameObject.AddComponent<CombatImpactGraphic>();
            burst.rectTransform.sizeDelta = new Vector2(100, 100);
            burst.transform.SetAsFirstSibling(); burst.raycastTarget = false;
            pool[i] = new Popup { root = rect, text = text, burst = burst, marker = marker };
            rect.gameObject.SetActive(false);
        }
        foreach (var root in gameObject.scene.GetRootGameObjects())
            foreach (var actor in root.GetComponentsInChildren<BattleScript>())
                if (actor.isActiveAndEnabled) Get(actor);
    }

    private ActorVisual Get(BattleScript actor)
    {
        if (!actors.TryGetValue(actor, out var visual))
        { visual = new ActorVisual(actor); actors.Add(actor, visual); }
        return visual;
    }

    private void OnAttack(BattleScript attacker, BattleScript target)
    {
        if (attacker == null || target == null || attacker.gameObject.scene != gameObject.scene) return;
        var visual = Get(attacker);
        visual.attackTime = 0;
        visual.attackDirection = (target.transform.position - attacker.transform.position).normalized;
    }

    private void OnDamage(BattleScript victim, BattleScript source, int healthDamage, int blocked)
    {
        if (victim == null || victim.gameObject.scene != gameObject.scene || pool == null || (healthDamage <= 0 && blocked <= 0)) return;
        DamageEventCount++;
        var visual = Get(victim);
        bool player = victim.CompareTag("Player");
        visual.tint = blocked > 0 && healthDamage == 0 ? shieldColor : hitTint;
        Vector3 away = source != null ? victim.transform.position - source.transform.position : Vector3.zero;
        away = Vector3.ProjectOnPlane(away, Vector3.up);
        if (away.sqrMagnitude < .0001f)
        {
            away = worldCamera != null ? Vector3.ProjectOnPlane(worldCamera.transform.right, Vector3.up) : Vector3.right;
            if (away.sqrMagnitude < .0001f) away = Vector3.right;
            away *= player ? -1 : 1;
        }
        visual.Hit(this, away.normalized, Mathf.Max(0, healthDamage) + Mathf.Max(0, blocked));
        Vector3 point = victim.transform.position;
        if (healthDamage > 0) Show(point, healthDamage, false, player ? playerDamageColor : damageColor, healthDamage >= victim.maxHealth * .15f);
        if (blocked > 0) Show(point, blocked, true, shieldColor, false);
    }

    private void Show(Vector3 point, int value, bool block, Color tint, bool heavy)
    {
        var popup = pool[nextNumber]; nextNumber = (nextNumber + 1) % pool.Length;
        popup.world = point; popup.age = 0; popup.tint = tint;
        popup.lane = (sequence++ % 5 - 2) * 36;
        popup.block = block;
        popup.text.SetText(block ? "{0}" : "-{0}", value);
        popup.marker.gameObject.SetActive(block);
        popup.text.fontSize = numberSize * (block ? .72f : heavy ? 1.13f : 1);
        popup.root.gameObject.SetActive(true);
        UpdatePopup(popup, 0);
    }

    private void LateUpdate()
    {
        float delta = Time.deltaTime;
        foreach (var pair in actors) if (pair.Key != null) pair.Value.Tick(this, delta);
        ActiveNumberCount = 0;
        if (pool == null) return;
        foreach (var popup in pool)
        {
            if (!popup.root.gameObject.activeSelf) continue;
            popup.age += delta;
            if (popup.age >= Mathf.Max(.2f, numberLifetime)) { popup.root.gameObject.SetActive(false); continue; }
            ActiveNumberCount++; UpdatePopup(popup, popup.age / Mathf.Max(.2f, numberLifetime));
        }
    }

    private void UpdatePopup(Popup popup, float t)
    {
        Vector3 view = worldCamera.WorldToViewportPoint(popup.world);
        popup.root.anchorMin = popup.root.anchorMax = new Vector2(Mathf.Clamp(view.x, .06f, .94f), Mathf.Clamp(view.y, .28f, .9f));
        float rise = 1 - (1 - t) * (1 - t);
        popup.root.anchoredPosition = new Vector2(popup.lane * (1 + .2f * t), (popup.block ? -35 : -5) + numberRise * rise);
        popup.root.localScale = Vector3.one * (1 + .22f * Mathf.Sin(Mathf.Clamp01(t / .18f) * Mathf.PI));
        var tint = popup.tint; tint.a = view.z > 0 ? 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.55f, 1, t)) : 0;
        popup.text.color = tint;
        popup.marker.color = tint;
        popup.burst.progress = Mathf.Clamp01(t / .32f);
        tint.a *= impactStrength;
        popup.burst.color = tint; popup.burst.SetVerticesDirty();
    }

    private void OnDisable()
    {
        BattleScript.OnDamageReceived -= OnDamage;
        BattleScript.OnAttackPerformed -= OnAttack;
        foreach (var visual in actors.Values) visual.Reset();
        if (pool != null) foreach (var popup in pool) if (popup.root != null) popup.root.gameObject.SetActive(false);
        if (active == this) active = null;
    }
    private void OnDestroy() { if (layer != null) Destroy(layer.gameObject); }

    private sealed class Popup
    {
        public RectTransform root;
        public TMP_Text text;
        public CombatImpactGraphic burst;
        public StatusIcon marker;
        public Vector3 world;
        public float age, lane;
        public Color tint;
        public bool block;
    }

    private sealed class ActorVisual
    {
        private static readonly int Offset = Shader.PropertyToID("_WorldOffset"), Flash = Shader.PropertyToID("_HitBlend"),
            Tint = Shader.PropertyToID("_HitColor"), Defeated = Shader.PropertyToID("_Defeated");
        private readonly BattleScript actor;
        private readonly Renderer[] renderers;
        private readonly MaterialPropertyBlock block = new();
        public float hitTime = 10, attackTime = 10;
        private float hitDistance;
        public Vector3 hitDirection, attackDirection;
        public Color tint;
        public ActorVisual(BattleScript source) { actor = source; renderers = source.GetComponentsInChildren<Renderer>(); }
        public void Hit(CombatFeedback settings, Vector3 direction, int damage)
        {
            float light = Mathf.Clamp01(settings.lightRecoilScale);
            float medium = Mathf.Clamp(settings.mediumRecoilScale, light, 1);
            float distance = Mathf.Max(0, settings.recoilDistance) * (damage < 5 ? light : damage < 10 ? medium : 1);
            if (actor.CompareTag("Enemy")) distance *= Mathf.Max(0, settings.enemyRecoilMultiplier);
            // Simultaneous hits retain the stronger kick instead of accumulating displacement.
            hitDistance = hitTime < Mathf.Max(.05f, settings.recoilDuration) * .3f ? Mathf.Max(hitDistance, distance) : distance;
            hitDirection = direction;
            hitTime = 0;
        }
        private static float RecoilEnvelope(float t)
        {
            if (t <= 0 || t >= 1) return 0;
            const float peak = .18f;
            if (t <= peak) return Mathf.Sin(t / peak * Mathf.PI * .5f);
            float settle = (t - peak) / (1 - peak);
            // A fast backward kick followed by one small rebound, with an exact resting endpoint.
            return Mathf.Cos(settle * Mathf.PI * 1.5f) * (1 - settle) * (1 - settle);
        }
        public void Tick(CombatFeedback settings, float delta)
        {
            hitTime += delta; attackTime += delta;
            float hit = Mathf.Clamp01(hitTime / Mathf.Max(.05f, settings.recoilDuration));
            float attack = Mathf.Clamp01(attackTime / Mathf.Max(.05f, settings.attackDuration));
            Vector3 offset = hitDirection * (RecoilEnvelope(hit) * hitDistance) +
                attackDirection * (Mathf.Sin(attack * Mathf.PI) * settings.attackLunge);
            float flash = (1 - Mathf.Clamp01(hitTime / Mathf.Max(.05f, settings.flashDuration))) * settings.flashStrength;
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                renderer.GetPropertyBlock(block);
                block.SetVector(Offset, offset);
                block.SetFloat(Flash, renderer.gameObject == actor.gameObject ? flash : 0);
                block.SetColor(Tint, tint);
                block.SetFloat(Defeated, actor.health <= 0 ? .7f : 0);
                renderer.SetPropertyBlock(block);
            }
        }
        public void Reset()
        {
            hitTime = attackTime = 10;
            hitDistance = 0;
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                renderer.GetPropertyBlock(block);
                block.SetVector(Offset, Vector4.zero); block.SetFloat(Flash, 0); block.SetFloat(Defeated, 0);
                renderer.SetPropertyBlock(block);
            }
        }
    }
}

// Eight short, tapered shards. UI geometry keeps the effect original and texture-free.
public sealed class CombatImpactGraphic : MaskableGraphic
{
    public float progress;
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear(); if (progress >= 1) return;
        Color tint = color; tint.a *= 1 - progress;
        for (int i = 0; i < 8; i++)
        {
            float angle = (i + .2f) * Mathf.PI * .25f;
            var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            var side = new Vector2(-direction.y, direction.x);
            float radius = 14 + progress * 40, length = (1 - progress) * (i % 2 == 0 ? 17 : 10);
            int start = vh.currentVertCount;
            vh.AddVert(direction * radius, tint, Vector2.zero);
            vh.AddVert(direction * (radius + length * .4f) + side * 2.5f, tint, Vector2.zero);
            vh.AddVert(direction * (radius + length), tint, Vector2.zero);
            vh.AddVert(direction * (radius + length * .4f) - side * 2.5f, tint, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2); vh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
