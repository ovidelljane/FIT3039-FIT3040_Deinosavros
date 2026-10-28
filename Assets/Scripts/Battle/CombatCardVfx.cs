using System;
using System.Collections.Generic;
using UnityEngine;

// Presentation only. Effect and BattleScript remain the owners of combat state.
[DisallowMultipleComponent]
public sealed class CombatCardVfx : MonoBehaviour
{
    [Serializable]
    public sealed class Part
    {
        public ParticleSystem template;
        [Tooltip("Offset in actor-height units, measured from the body centre.")]
        public Vector3 offset;
        [Min(.001f)] public float scale = .2f;
        [Min(.01f)] public float playbackSpeed = 1;
        public bool loop;
    }

    [Serializable]
    public sealed class Binding
    {
        public string name;
        public StatType[] stats;
        public bool sustained;
        public Part[] parts;

        public bool Matches(StatType stat) => stats != null && Array.IndexOf(stats, stat) >= 0;
    }

    public Binding[] bindings;
    public Part attackImpact;
    [Min(1)] public int maximumBursts = 16;
    [Tooltip("Small camera-facing offset in actor-height units, preventing sprite overlap.")]
    [Range(0, .2f)] public float cameraOffset = .04f;

    private readonly List<Visual> visuals = new();
    private readonly Dictionary<BattleScript, Renderer> actors = new();
    private bool finished;
    private Camera viewCamera;
    public int ActiveVisualCount => visuals.Count;
    public int BurstCount { get; private set; }

    public bool IsShowing(BattleScript target, StatType stat)
    {
        foreach (var visual in visuals)
            if (!visual.stopping && visual.target == target && visual.binding != null && visual.binding.Matches(stat)) return true;
        return false;
    }

    private void OnEnable()
    {
        finished = false;
        CardEffectFeedback.Changed += OnEffect;
        BattleScript.OnAttackPerformed += OnAttack;
        BattleScript.OnBattleFinished += OnFinished;
    }

    private void Start()
    {
        viewCamera = Camera.main;
        foreach (var root in gameObject.scene.GetRootGameObjects())
            foreach (var actor in root.GetComponentsInChildren<BattleScript>(true))
                actors[actor] = actor.GetComponent<Renderer>();
        // Include effects installed by the encounter receiver before this component started.
        foreach (var effect in Effect.ActiveEffects)
            if (effect != null && effect.IsApplied && Find(effect.effectType)?.sustained == true)
                Show(effect.target, Find(effect.effectType));
    }

    private Binding Find(StatType stat)
    {
        if (bindings != null) foreach (var binding in bindings)
            if (binding != null && binding.Matches(stat)) return binding;
        return null;
    }

    private bool Accepts(BattleScript target) => !finished && target != null && target.isActiveAndEnabled &&
        target.health > 0 && target.gameObject.scene == gameObject.scene;

    private void OnEffect(CardEffectFeedback feedback)
    {
        if (feedback.Phase != CardFeedbackPhase.Applied || !Accepts(feedback.Target)) return;
        if ((feedback.Stat == StatType.Elixir || feedback.Stat == StatType.Shield) && feedback.After <= feedback.Before) return;
        Show(feedback.Target, Find(feedback.Stat));
    }

    private void OnAttack(BattleScript source, BattleScript target)
    {
        if (!Accepts(source) || !Accepts(target) || attackImpact == null || attackImpact.template == null) return;
        Create(target, null, new[] { attackImpact });
    }

    private void Show(BattleScript target, Binding binding)
    {
        if (!Accepts(target) || binding == null || binding.parts == null) return;
        if (binding.sustained)
            foreach (var visual in visuals)
                if (!visual.stopping && visual.target == target && visual.binding == binding) return;
        Create(target, binding, binding.parts);
    }

    private void Create(BattleScript target, Binding binding, Part[] parts)
    {
        bool sustained = binding != null && binding.sustained;
        if (!sustained)
        {
            int count = 0, oldest = -1;
            for (int i = 0; i < visuals.Count; i++)
                if (visuals[i].binding == null || !visuals[i].binding.sustained)
                { count++; if (oldest < 0) oldest = i; }
            if (count >= maximumBursts && oldest >= 0) Remove(oldest);
            BurstCount++;
        }
        if (!actors.ContainsKey(target)) actors[target] = target.GetComponent<Renderer>();
        var root = new GameObject(binding != null ? binding.name : "Attack");
        root.SetActive(false);
        root.transform.SetParent(transform, false);
        var systems = new List<ParticleSystem>();
        foreach (var part in parts)
        {
            if (part == null || part.template == null) continue;
            var system = Instantiate(part.template, root.transform, false);
            system.name = part.template.name;
            system.gameObject.SetActive(true);
            system.transform.localPosition = part.offset;
            system.transform.localScale = part.template.transform.localScale * part.scale;
            var main = system.main;
            main.playOnAwake = false;
            main.loop = sustained && part.loop;
            main.stopAction = ParticleSystemStopAction.None;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.simulationSpeed = part.playbackSpeed;
            systems.Add(system);
        }
        var visual = new Visual { target = target, binding = binding, root = root, systems = systems.ToArray() };
        visuals.Add(visual);
        Position(visual);
        root.SetActive(true);
        foreach (var system in visual.systems) system.Play(false);
    }

    private bool StillApplied(Visual visual)
    {
        if (!Accepts(visual.target)) return false;
        if (visual.binding.Matches(StatType.Shield)) return visual.target.shield > 0;
        foreach (var effect in Effect.ActiveEffects)
            if (effect != null && effect.IsApplied && effect.target == visual.target && visual.binding.Matches(effect.effectType)) return true;
        return false;
    }

    private void Position(Visual visual)
    {
        if (visual.target == null) return;
        var renderer = actors[visual.target];
        var bounds = renderer != null ? renderer.bounds : new Bounds(visual.target.transform.position, Vector3.one * 3);
        Vector3 towardCamera = viewCamera != null ? Vector3.ProjectOnPlane(-viewCamera.transform.forward, Vector3.up).normalized : Vector3.zero;
        visual.root.transform.SetPositionAndRotation(bounds.center + towardCamera * (bounds.size.y * cameraOffset), Quaternion.identity);
        visual.root.transform.localScale = Vector3.one * Mathf.Max(.25f, bounds.size.y);
    }

    private void LateUpdate()
    {
        if (finished) return;
        // Initial encounter shields (including sacrifice) may arrive without a card receipt.
        var shield = Find(StatType.Shield);
        if (shield != null) foreach (var actor in actors.Keys)
            if (Accepts(actor) && actor.shield > 0) Show(actor, shield);
        for (int i = visuals.Count - 1; i >= 0; i--)
        {
            var visual = visuals[i];
            if (visual.target == null || !visual.target.gameObject.activeInHierarchy) { Remove(i); continue; }
            Position(visual);
            if (!visual.stopping && visual.binding != null && visual.binding.sustained && !StillApplied(visual))
            {
                visual.stopping = true;
                foreach (var system in visual.systems) system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            bool alive = false;
            foreach (var system in visual.systems) if (system != null && system.IsAlive(false)) { alive = true; break; }
            if (!alive) Remove(i);
        }
    }

    private void Remove(int index)
    {
        var root = visuals[index].root;
        if (root != null) { root.SetActive(false); Destroy(root); }
        visuals.RemoveAt(index);
    }

    private void OnFinished(BattleScript player, bool victory)
    {
        if (player == null || player.gameObject.scene != gameObject.scene) return;
        finished = true;
        Clear();
    }

    private void Clear() { for (int i = visuals.Count - 1; i >= 0; i--) Remove(i); }

    private void OnDisable()
    {
        CardEffectFeedback.Changed -= OnEffect;
        BattleScript.OnAttackPerformed -= OnAttack;
        BattleScript.OnBattleFinished -= OnFinished;
        Clear();
    }

    private sealed class Visual
    {
        public BattleScript target;
        public Binding binding;
        public GameObject root;
        public ParticleSystem[] systems;
        public bool stopping;
    }
}
