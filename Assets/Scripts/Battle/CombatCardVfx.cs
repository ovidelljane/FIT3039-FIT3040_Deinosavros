using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Presentation only. Effect and BattleScript remain the owners of combat state.
[DisallowMultipleComponent]
public sealed class CombatCardVfx : MonoBehaviour
{
    [Serializable]
    public sealed class Part
    {
        public ParticleSystem template;
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

    [Serializable]
    private struct JudgementTargetEffect
    {
        public BattleScript target;
        public Transform effect;
    }

    public Binding[] bindings;
    public Part attackImpact;
    [Min(1)] public int maximumBursts = 16;

    [Header("Judgement Day")]
    [SerializeField, Tooltip("Optional replacement for the arena and character effects. Add a Particle System effect under this slot to use it.")]
    private Transform judgementEffectSlot;
    [SerializeField, Tooltip("Align the slot origin with the combatant hit by Judgement Day. Disable for a fixed arena effect.")]
    private bool placeJudgementOnTarget = true;
    [SerializeField] private Transform judgementArenaEffect;
    [SerializeField] private JudgementTargetEffect[] judgementTargetEffects = Array.Empty<JudgementTargetEffect>();

    private readonly HashSet<BattleScript> actors = new();
    private bool finished;
    private readonly List<SceneParticle> sceneParticles = new();
    private readonly List<SceneVisual> sceneVisuals = new();
    private readonly HashSet<string> missingSceneParts = new();
    private bool sceneReady;
    private Binding areaDamageBinding;
    private Binding thunderboltBinding;
    private Coroutine thunderboltLighting;
    private Light thunderboltSpot;
    private float originalSpotIntensity;
    private Binding healingBinding;
    private readonly Binding hunterBinding = new() { name = "Hunter", stats = new[] { StatType.HuntWeakestEnemy } };
    private readonly Dictionary<BattleScript, float> delayedShields = new();
    private Coroutine judgementVignette;
    private int vignetteVersion;
    private readonly Dictionary<Vignette, VignetteState> vignetteOriginals = new();
    private readonly Binding judgementGlobal = new() { name = "Judgement Global", stats = new[] { StatType.JudgementDay } };
    private readonly Binding judgementCharacter = new() { name = "Judgement Character", stats = new[] { StatType.JudgementDay } };
    private Part[] judgementEffectParts = Array.Empty<Part>();
    public int ActiveVisualCount => sceneVisuals.Count;
    public int BurstCount { get; private set; }

    public bool IsShowing(BattleScript target, StatType stat)
    {
        foreach (var visual in sceneVisuals)
            if (visual.target == target && visual.binding != null && visual.binding.Matches(stat)) return true;
        return false;
    }

    private void OnEnable()
    {
        finished = false;
        CardEffectFeedback.Changed += OnEffect;
        BattleScript.OnAttackPerformed += OnAttack;
        BattleScript.OnBattleFinished += OnFinished;
    }

    private void Awake()
    {
        PrepareSceneParticles();
    }

    private void Start()
    {
        // Include effects installed by the encounter receiver before this component started.
        foreach (var effect in Effect.ActiveEffects)
            if (effect != null && effect.IsApplied && effect.effectType != StatType.FrequencySlow && Find(effect.effectType)?.sustained == true)
                Show(effect.target, Find(effect.effectType));
    }

    private Binding Find(StatType stat)
    {
        if (bindings != null) foreach (var binding in bindings)
            if (binding != null && binding.Matches(stat)) return binding;
        return stat == StatType.Heal ? healingBinding : null;
    }

    private bool Accepts(BattleScript target) => !finished && target != null && target.isActiveAndEnabled &&
        target.health > 0 && target.gameObject.scene == gameObject.scene;

    private void OnEffect(CardEffectFeedback feedback)
    {
        if (feedback.Phase != CardFeedbackPhase.Applied || !Accepts(feedback.Target)) return;
        if (feedback.Stat == StatType.DamageAllEnemies && feedback.SourceCard != null && feedback.SourceCard.cardId == "thunderbolt")
        {
            Show(feedback.Target, thunderboltBinding);
            PlayThunderboltLighting();
            return;
        }
        if (feedback.Stat == StatType.FrequencySlow) return; // Judgement uses its own selected-target visuals.
        if (feedback.Stat == StatType.HuntWeakestEnemy)
        {
            string groupName = feedback.Target.name switch
            {
                "Enemy" => "HunterParticle3",
                "Enemy (1)" => "HunterParticle2",
                _ => "HunterParticle"
            };
            var group = transform.Find(groupName);
            if (group != null) ShowScene(feedback.Target, hunterBinding, GroupParts(group), group);
            return;
        }
        if (feedback.Stat == StatType.JudgementDay)
        {
            if (judgementVignette != null) StopCoroutine(judgementVignette);
            judgementVignette = StartCoroutine(TransitionJudgementVignette(++vignetteVersion));
            PlayJudgementParticles(feedback.Target);
            return;
        }
        if ((feedback.Stat == StatType.Elixir || feedback.Stat == StatType.Shield) && feedback.After <= feedback.Before) return;
        var binding = Find(feedback.Stat);
        if (feedback.Stat == StatType.Shield && binding != null)
        {
            if (feedback.ParticleDelay > 0)
                delayedShields[feedback.Target] = Time.time + feedback.ParticleDelay;
            else
                PlayShield(feedback.Target, binding);
            return;
        }
        // Instant area damage reuses the authored water without becoming a lasting stat buff.
        if (binding == null && feedback.Stat == StatType.DamageAllEnemies)
        {
            var water = transform.Find("AttackEnemyParticle");
            if (water != null)
            {
                areaDamageBinding ??= new Binding
                {
                    name = "AreaDamageWater", stats = new[] { StatType.DamageAllEnemies }, sustained = false
                };
                areaDamageBinding.parts = GroupParts(water);
                binding = areaDamageBinding;
            }
        }
        Show(feedback.Target, binding);
    }

    private void PlayThunderboltLighting()
    {
        if (thunderboltLighting == null)
        {
            var camera = Camera.main;
            if (camera == null || camera.gameObject.scene != gameObject.scene) return;
            thunderboltSpot = camera.transform.Find("Spot Light")?.GetComponent<Light>();
            if (thunderboltSpot == null || thunderboltSpot.type != LightType.Spot) return;
            originalSpotIntensity = thunderboltSpot.intensity;
        }
        else StopCoroutine(thunderboltLighting);
        thunderboltLighting = StartCoroutine(AnimateThunderboltLighting());
    }

    private IEnumerator AnimateThunderboltLighting()
    {
        const float fade = .2f;
        const float restoreAt = 1.3f;
        float startTime = Time.time;
        float startIntensity = thunderboltSpot.intensity;
        while (thunderboltSpot != null)
        {
            float elapsed = Time.time - startTime;
            if (elapsed >= restoreAt + fade) break;
            thunderboltSpot.intensity = elapsed < restoreAt
                ? Mathf.SmoothStep(startIntensity, 10000f, elapsed / fade)
                : Mathf.SmoothStep(10000f, originalSpotIntensity, (elapsed - restoreAt) / fade);
            yield return null;
        }
        RestoreThunderboltLighting();
    }

    private void RestoreThunderboltLighting()
    {
        if (thunderboltSpot != null) thunderboltSpot.intensity = originalSpotIntensity;
        thunderboltSpot = null;
        thunderboltLighting = null;
    }

    private static Part[] GroupParts(Transform group)
    {
        var parts = new List<Part>();
        foreach (var system in group.GetComponentsInChildren<ParticleSystem>(true))
        {
            bool nested = false;
            for (var parent = system.transform.parent; parent != null && parent != group.parent; parent = parent.parent)
                if (parent.GetComponent<ParticleSystem>() != null) { nested = true; break; }
            if (!nested) parts.Add(new Part { template = system });
        }
        return parts.ToArray();
    }

    private void PlayJudgementParticles(BattleScript target)
    {
        for (int i = sceneVisuals.Count - 1; i >= 0; i--)
            if (sceneVisuals[i].binding == judgementGlobal || sceneVisuals[i].binding == judgementCharacter) RemoveScene(i);

        if (judgementEffectSlot != null && judgementEffectParts.Length > 0)
        {
            ShowScene(target, judgementCharacter, judgementEffectParts, judgementEffectSlot);
            return;
        }

        if (judgementArenaEffect != null)
            ShowScene(target, judgementGlobal, GroupParts(judgementArenaEffect), judgementArenaEffect);
        foreach (var targetEffect in judgementTargetEffects)
        {
            if (targetEffect.target != target || targetEffect.effect == null) continue;
            ShowScene(target, judgementCharacter, GroupParts(targetEffect.effect), targetEffect.effect);
            break;
        }
    }

    private IEnumerator TransitionJudgementVignette(int version)
    {
        var targets = new List<(Vignette effect, float intensity, float smoothness)>();
        foreach (var root in gameObject.scene.GetRootGameObjects())
            foreach (var volume in root.GetComponentsInChildren<Volume>())
            {
                if (!volume.isActiveAndEnabled || !volume.isGlobal || volume.name != "Global Volume") continue;
                // profile is the runtime copy; never edit the shared profile asset.
                var profile = volume.profile;
                if (!profile.TryGet<Vignette>(out var vignette)) vignette = profile.Add<Vignette>();
                if (!vignetteOriginals.ContainsKey(vignette))
                    vignetteOriginals.Add(vignette, new VignetteState
                    {
                        intensity = vignette.intensity.value, smoothness = vignette.smoothness.value,
                        active = vignette.active, intensityOverride = vignette.intensity.overrideState,
                        smoothnessOverride = vignette.smoothness.overrideState
                    });
                vignette.active = true;
                vignette.intensity.overrideState = true;
                vignette.smoothness.overrideState = true;
                targets.Add((vignette, vignette.intensity.value, vignette.smoothness.value));
            }
        if (targets.Count == 0)
        {
            Debug.LogWarning("Judgement Day could not find an active Global Volume in this scene.", this);
            yield break;
        }
        const float duration = .3f;
        float elapsed = 0;
        while (elapsed < duration)
        {
            if (version != vignetteVersion) yield break;
            elapsed += Time.deltaTime;
            float blend = Mathf.SmoothStep(0, 1, Mathf.Clamp01(elapsed / duration));
            foreach (var target in targets)
            {
                if (target.effect == null) continue;
                target.effect.intensity.value = Mathf.Lerp(target.intensity, .5f, blend);
                target.effect.smoothness.value = Mathf.Lerp(target.smoothness, 1, blend);
            }
            yield return null;
        }
        // Count the hold from card play, including the initial fade-in.
        yield return new WaitForSeconds(Mathf.Max(0, 1.5f - elapsed));
        if (version != vignetteVersion) yield break;
        // Retain this transition's original values rather than looking them up each frame:
        // battle cleanup or a newer cast can clear the shared restoration dictionary.
        var restoring = new List<(Vignette effect, float intensity, float smoothness, VignetteState original)>();
        foreach (var original in vignetteOriginals)
            if (original.Key != null)
                restoring.Add((original.Key, original.Key.intensity.value, original.Key.smoothness.value, original.Value));
        elapsed = 0;
        while (elapsed < duration)
        {
            if (version != vignetteVersion) yield break;
            elapsed += Time.deltaTime;
            float blend = Mathf.SmoothStep(0, 1, Mathf.Clamp01(elapsed / duration));
            foreach (var target in restoring)
            {
                if (target.effect == null) continue;
                var original = target.original;
                target.effect.intensity.value = Mathf.Lerp(target.intensity, original.intensity, blend);
                target.effect.smoothness.value = Mathf.Lerp(target.smoothness, original.smoothness, blend);
            }
            yield return null;
        }
        if (version != vignetteVersion) yield break;
        RestoreJudgementVignette();
        judgementVignette = null;
    }

    private void RestoreJudgementVignette()
    {
        foreach (var entry in vignetteOriginals)
        {
            if (entry.Key == null) continue;
            entry.Key.intensity.value = entry.Value.intensity;
            entry.Key.smoothness.value = entry.Value.smoothness;
            entry.Key.intensity.overrideState = entry.Value.intensityOverride;
            entry.Key.smoothness.overrideState = entry.Value.smoothnessOverride;
            entry.Key.active = entry.Value.active;
        }
        vignetteOriginals.Clear();
    }

    private sealed class VignetteState
    {
        public float intensity, smoothness;
        public bool active, intensityOverride, smoothnessOverride;
    }

    private void OnAttack(BattleScript source, BattleScript target)
    {
        if (!Accepts(source) || !Accepts(target) || attackImpact == null || attackImpact.template == null) return;
        ShowScene(target, null, new[] { attackImpact });
    }

    private void Show(BattleScript target, Binding binding)
    {
        if (!Accepts(target) || binding == null || binding.parts == null) return;
        ShowScene(target, binding, binding.parts);
    }

    private bool StillApplied(BattleScript target, Binding binding)
    {
        if (!Accepts(target)) return false;
        if (binding.Matches(StatType.Shield)) return target.shield > 0;
        foreach (var effect in Effect.ActiveEffects)
            if (effect != null && effect.IsApplied && effect.target == target && binding.Matches(effect.effectType)) return true;
        return false;
    }

    private void PlayShield(BattleScript target, Binding binding)
    {
        var existing = sceneVisuals.Find(v => v.target == target && v.binding == binding);
        if (existing == null) Show(target, binding);
        else foreach (var part in existing.parts) ReplaySceneParticle(part, binding.sustained);
    }

    private void LateUpdate()
    {
        if (finished)
        {
            TickSceneParticles();
            return;
        }
        // Initial encounter shields (including sacrifice) may arrive without a card receipt.
        var shield = Find(StatType.Shield);
        if (shield != null) foreach (var actor in actors)
        {
            if (actor == null) continue;
            if (delayedShields.TryGetValue(actor, out float playAt))
            {
                if (Time.time < playAt) continue;
                delayedShields.Remove(actor);
                if (!Accepts(actor)) continue;
                if (actor.shield > 0) PlayShield(actor, shield);
                else
                {
                    // A spent shield still gets its delayed visual as a one-shot burst.
                    var burst = new Binding { name = shield.name, stats = shield.stats, parts = shield.parts };
                    Show(actor, burst);
                }
            }
            else if (Accepts(actor) && actor.shield > 0) Show(actor, shield);
        }
        TickSceneParticles();
    }

    private void OnFinished(BattleScript player, bool victory)
    {
        if (player == null || player.gameObject.scene != gameObject.scene) return;
        finished = true;
        Clear(keepJudgementBurst: true);
    }

    private void Clear(bool keepJudgementBurst = false)
    {
        if (thunderboltLighting != null) StopCoroutine(thunderboltLighting);
        RestoreThunderboltLighting();
        delayedShields.Clear();
        ++vignetteVersion;
        if (judgementVignette != null) StopCoroutine(judgementVignette);
        judgementVignette = null;
        RestoreJudgementVignette();
        for (int i = sceneVisuals.Count - 1; i >= 0; i--)
        {
            var binding = sceneVisuals[i].binding;
            if (!keepJudgementBurst || (binding != judgementGlobal && binding != judgementCharacter)) RemoveScene(i);
        }
    }

    // Scene sources are a fixed pool. Never create, destroy, reparent or rescale them.
    private void PrepareSceneParticles()
    {
        if (sceneReady) return;
        sceneReady = true;
        foreach (var root in gameObject.scene.GetRootGameObjects())
            foreach (var actor in root.GetComponentsInChildren<BattleScript>(true))
                if (actor.enabled) actors.Add(actor);

        var parts = new List<Part>();
        if (bindings != null) foreach (var binding in bindings)
            if (binding?.parts != null) parts.AddRange(SceneParts(binding, binding.parts));
        if (attackImpact != null) parts.Add(attackImpact);
        var lightning = transform.Find("LightningParticle");
        if (lightning != null)
        {
            thunderboltBinding = new Binding { name = "Thunderbolt", stats = new[] { StatType.DamageAllEnemies }, parts = GroupParts(lightning) };
            parts.AddRange(thunderboltBinding.parts);
        }
        var healing = transform.Find("HealingParticle");
        if (healing != null)
        {
            healingBinding = new Binding { name = "Healing", stats = new[] { StatType.Heal }, parts = GroupParts(healing) };
            parts.AddRange(healingBinding.parts);
        }
        foreach (Transform group in transform)
            if (group.name == "AttackEnemyParticle" || IsHunterGroup(group.name))
                parts.AddRange(GroupParts(group));
        var stagedJudgementGroups = new HashSet<Transform>();
        if (judgementArenaEffect != null) stagedJudgementGroups.Add(judgementArenaEffect);
        foreach (var targetEffect in judgementTargetEffects)
            if (targetEffect.effect != null) stagedJudgementGroups.Add(targetEffect.effect);
        foreach (var group in stagedJudgementGroups) parts.AddRange(GroupParts(group));
        if (judgementEffectSlot != null && judgementEffectSlot.parent == transform)
        {
            judgementEffectParts = GroupParts(judgementEffectSlot);
            parts.AddRange(judgementEffectParts);
        }
        var candidates = GetComponentsInChildren<ParticleSystem>(true);
        foreach (var part in parts)
        {
            if (part?.template == null) continue;
            var sourceRoot = SceneRoot(part.template.transform);
            if (sourceRoot == null) continue;
            foreach (var system in candidates)
            {
                var effectRoot = SceneRoot(system.transform);
                if (effectRoot == null || Family(effectRoot.name) != Family(sourceRoot.name) ||
                    RelativePath(system.transform, effectRoot) != RelativePath(part.template.transform, sourceRoot)) continue;
                if (sceneParticles.Exists(p => p.system == system)) continue;
                BattleScript home = null;
                float closest = float.PositiveInfinity;
                foreach (var actor in actors)
                {
                    if (effectRoot.name == "HunterParticle" && actor.name != "Enemy (2)") continue;
                    var distance = Vector3.ProjectOnPlane(system.transform.position - actor.transform.position, Vector3.up).sqrMagnitude;
                    if (distance < closest) { closest = distance; home = actor; }
                }
                var systems = system.GetComponentsInChildren<ParticleSystem>(true);
                foreach (var child in systems)
                {
                    var main = child.main;
                    main.playOnAwake = false;
                    main.stopAction = ParticleSystemStopAction.None;
                    child.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
                sceneParticles.Add(new SceneParticle
                {
                    system = system, systems = systems, root = effectRoot,
                    family = Family(effectRoot.name), path = RelativePath(system.transform, effectRoot),
                    position = system.transform.position, rotation = system.transform.rotation,
                    loops = Array.ConvertAll(systems, p => p.main.loop),
                    stagedJudgementPosition = stagedJudgementGroups.Contains(effectRoot),
                    homePosition = effectRoot == judgementEffectSlot ? effectRoot.position :
                        (home != null ? home.transform.position : system.transform.position)
                });
            }
        }
        foreach (var particle in sceneParticles) particle.root.gameObject.SetActive(false);
    }

    private Transform SceneRoot(Transform child)
    {
        while (child != null && child.parent != transform) child = child.parent;
        return child;
    }

    private static string Family(string name)
    {
        int suffix = name.LastIndexOf(" (");
        return suffix >= 0 && name.EndsWith(")") ? name.Substring(0, suffix) : name;
    }

    private static string RelativePath(Transform child, Transform root)
    {
        string path = child.name;
        while (child != root && child.parent != null)
        {
            child = child.parent;
            if (child != root) path = child.name + "/" + path;
        }
        return path;
    }

    private static bool IsBuffBinding(Binding binding) => binding != null &&
        (binding.Matches(StatType.Damage) || binding.Matches(StatType.AttackSpeed) || binding.Matches(StatType.CardMomentum) || binding.Matches(StatType.ExtraHits) || binding.Matches(StatType.Cataclysm));

    // Buff cards play the whole authored BuffParticle group, including newly added children.
    private Part[] SceneParts(Binding binding, Part[] parts)
    {
        if (!IsBuffBinding(binding)) return parts;
        var expanded = new List<Part>();
        var roots = new HashSet<Transform>();
        foreach (var part in parts)
        {
            if (part?.template == null) continue;
            var root = SceneRoot(part.template.transform);
            if (root == null || !roots.Add(root)) continue;
            foreach (var system in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                // A parent ParticleSystem already includes its nested particle systems.
                bool nested = false;
                for (var parent = system.transform.parent; parent != null && parent != root.parent; parent = parent.parent)
                    if (parent.GetComponent<ParticleSystem>() != null) { nested = true; break; }
                if (!nested) expanded.Add(new Part { template = system });
            }
        }
        return expanded.ToArray();
    }

    private void ShowScene(BattleScript target, Binding binding, Part[] parts, Transform exactRoot = null)
    {
        PrepareSceneParticles();
        parts = SceneParts(binding, parts);
        bool sustained = binding != null && binding.sustained;
        if (sustained)
        {
            var existing = sceneVisuals.Find(v => v.target == target && v.binding == binding);
            if (existing != null)
            {
                if (IsBuffBinding(binding)) foreach (var part in existing.parts) ReplaySceneParticle(part, true);
                return;
            }
        }
        if (!sustained)
        {
            // Replay the same target's burst instead of overlapping copies.
            for (int i = sceneVisuals.Count - 1; i >= 0; i--)
                if (sceneVisuals[i].target == target && sceneVisuals[i].binding == binding) RemoveScene(i);
            int count = 0, oldest = -1;
            for (int i = 0; i < sceneVisuals.Count; i++)
                if (sceneVisuals[i].binding == null || !sceneVisuals[i].binding.sustained)
                { count++; if (oldest < 0) oldest = i; }
            if (count >= maximumBursts && oldest >= 0) RemoveScene(oldest);
        }
        var visual = new SceneVisual { target = target, binding = binding };
        foreach (var part in parts)
        {
            if (part?.template == null) continue;
            var sourceRoot = SceneRoot(part.template.transform);
            if (sourceRoot == null) continue;
            string family = Family(sourceRoot.name), path = RelativePath(part.template.transform, sourceRoot);
            SceneParticle chosen = null;
            float closest = float.PositiveInfinity;
            foreach (var candidate in sceneParticles)
            {
                if ((exactRoot != null && candidate.root != exactRoot) || candidate.family != family || candidate.path != path ||
                    (candidate.users > 0 && candidate.target != target)) continue;
                float distance = Vector3.ProjectOnPlane(candidate.homePosition - target.transform.position, Vector3.up).sqrMagnitude;
                if (distance < closest) { closest = distance; chosen = candidate; }
            }
            if (chosen == null)
            {
                if (missingSceneParts.Add(family + "/" + target.name))
                    Debug.LogWarning("No free scene VFX for " + family + " on " + target.name + ". Add another authored instance if simultaneous playback is needed.", this);
                continue;
            }
            visual.parts.Add(chosen);
            chosen.users++;
            if (chosen.users > 1)
            {
                if (IsBuffBinding(binding)) ReplaySceneParticle(chosen, sustained);
                continue; // Shared visuals stop only after their last user.
            }
            chosen.target = target;
            PositionScene(chosen);
            chosen.root.gameObject.SetActive(true);
            chosen.system.gameObject.SetActive(true);
            ReplaySceneParticle(chosen, sustained);
        }
        if (visual.parts.Count == 0) return;
        sceneVisuals.Add(visual);
        if (!sustained) BurstCount++;
    }

    private static bool IsHunterGroup(string name) => name is "HunterParticle" or "HunterParticle2" or "HunterParticle3";

    private void PositionScene(SceneParticle particle)
    {
        if (particle.root == judgementEffectSlot)
        {
            if (!placeJudgementOnTarget) return;
        }
        else
        {
            if (IsHunterGroup(particle.family) && (particle.target == null || !particle.target.TryGetComponent<BossCombat>(out _))) return;
            // These effects use their authored scene positions.
            if (particle.stagedJudgementPosition || particle.family == "AttackEnemyParticle" ||
                particle.family == "BuffParticle" || particle.family == "LightningParticle") return;
        }
        if (particle.target != null)
            particle.system.transform.SetPositionAndRotation(
                particle.position + particle.target.transform.position - particle.homePosition, particle.rotation);
    }

    private static void ReplaySceneParticle(SceneParticle particle, bool sustained)
    {
        for (int i = 0; i < particle.systems.Length; i++)
        {
            var system = particle.systems[i];
            var main = system.main;
            main.loop = sustained && particle.loops[i];
            system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        foreach (var system in particle.systems)
            if (system.gameObject.activeInHierarchy) system.Play(false);
    }

    private void TickSceneParticles()
    {
        for (int i = sceneVisuals.Count - 1; i >= 0; i--)
        {
            var visual = sceneVisuals[i];
            bool sustained = visual.binding != null && visual.binding.sustained;
            bool finishesAfterHit = visual.binding == judgementGlobal || visual.binding == judgementCharacter || visual.binding == hunterBinding;
            if ((!finishesAfterHit && !Accepts(visual.target)) || (sustained && !StillApplied(visual.target, visual.binding)))
            { RemoveScene(i); continue; }
            bool alive = false;
            foreach (var part in visual.parts)
            {
                PositionScene(part);
                foreach (var system in part.systems)
                    if (system != null && system.IsAlive(false)) alive = true;
            }
            if (!sustained && !alive) RemoveScene(i);
        }
    }

    private void RemoveScene(int index)
    {
        var visual = sceneVisuals[index];
        sceneVisuals.RemoveAt(index);
        foreach (var part in visual.parts)
        {
            if (--part.users > 0) continue;
            foreach (var system in part.systems)
                if (system != null) system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            part.system.gameObject.SetActive(false);
            part.system.transform.SetPositionAndRotation(part.position, part.rotation);
            part.target = null;
            bool rootInUse = sceneParticles.Exists(p => p.root == part.root && p.users > 0);
            if (!rootInUse) part.root.gameObject.SetActive(false);
        }
    }

    private sealed class SceneParticle
    {
        public ParticleSystem system;
        public ParticleSystem[] systems;
        public bool[] loops;
        public bool stagedJudgementPosition;
        public Transform root;
        public string family, path;
        public Vector3 position, homePosition;
        public Quaternion rotation;
        public BattleScript target;
        public int users;
    }

    private sealed class SceneVisual
    {
        public BattleScript target;
        public Binding binding;
        public readonly List<SceneParticle> parts = new();
    }

    private void OnDisable()
    {
        CardEffectFeedback.Changed -= OnEffect;
        BattleScript.OnAttackPerformed -= OnAttack;
        BattleScript.OnBattleFinished -= OnFinished;
        Clear();
    }

}

// ============================================================================
// 旧版备份：运行时复制特效（全部为注释，不参与编译和运行）。
// 包含复制、按角色尺寸缩放、偏移、播放速度及销毁逻辑。
// 当前上方的代码使用场景实例。下方保存的是完整旧版，方便检索与对照。
// 若恢复旧模式，请替换当前类并核对近期卡牌触发修复；不要直接取消全部注释，
// 否则会出现重复类定义。旧版不包含后续 Thunderbolt 水特效修复。
// ============================================================================
// using System;
// using System.Collections.Generic;
// using UnityEngine;
//
// // Presentation only. Effect and BattleScript remain the owners of combat state.
// [DisallowMultipleComponent]
// public sealed class CombatCardVfx : MonoBehaviour
// {
//     [Serializable]
//     public sealed class Part
//     {
//         public ParticleSystem template;
//         [Tooltip("Offset in actor-height units, measured from the body centre.")]
//         public Vector3 offset;
//         [Min(.001f)] public float scale = .2f;
//         [Min(.01f)] public float playbackSpeed = 1;
//         public bool loop;
//     }
//
//     [Serializable]
//     public sealed class Binding
//     {
//         public string name;
//         public StatType[] stats;
//         public bool sustained;
//         public Part[] parts;
//
//         public bool Matches(StatType stat) => stats != null && Array.IndexOf(stats, stat) >= 0;
//     }
//
//     public Binding[] bindings;
//     public Part attackImpact;
//     [Min(1)] public int maximumBursts = 16;
//     [Tooltip("Small camera-facing offset in actor-height units, preventing sprite overlap.")]
//     [Range(0, .2f)] public float cameraOffset = .04f;
//
//     private readonly List<Visual> visuals = new();
//     private readonly Dictionary<BattleScript, Renderer> actors = new();
//     private bool finished;
//     private Camera viewCamera;
//     public int ActiveVisualCount => visuals.Count;
//     public int BurstCount { get; private set; }
//
//     public bool IsShowing(BattleScript target, StatType stat)
//     {
//         foreach (var visual in visuals)
//             if (!visual.stopping && visual.target == target && visual.binding != null && visual.binding.Matches(stat)) return true;
//         return false;
//     }
//
//     private void OnEnable()
//     {
//         finished = false;
//         CardEffectFeedback.Changed += OnEffect;
//         BattleScript.OnAttackPerformed += OnAttack;
//         BattleScript.OnBattleFinished += OnFinished;
//     }
//
//     private void Start()
//     {
//         viewCamera = Camera.main;
//         foreach (var root in gameObject.scene.GetRootGameObjects())
//             foreach (var actor in root.GetComponentsInChildren<BattleScript>(true))
//                 actors[actor] = actor.GetComponent<Renderer>();
//         // Include effects installed by the encounter receiver before this component started.
//         foreach (var effect in Effect.ActiveEffects)
//             if (effect != null && effect.IsApplied && Find(effect.effectType)?.sustained == true)
//                 Show(effect.target, Find(effect.effectType));
//     }
//
//     private Binding Find(StatType stat)
//     {
//         if (bindings != null) foreach (var binding in bindings)
//             if (binding != null && binding.Matches(stat)) return binding;
//         return null;
//     }
//
//     private bool Accepts(BattleScript target) => !finished && target != null && target.isActiveAndEnabled &&
//         target.health > 0 && target.gameObject.scene == gameObject.scene;
//
//     private void OnEffect(CardEffectFeedback feedback)
//     {
//         if (feedback.Phase != CardFeedbackPhase.Applied || !Accepts(feedback.Target)) return;
//         if ((feedback.Stat == StatType.Elixir || feedback.Stat == StatType.Shield) && feedback.After <= feedback.Before) return;
//         Show(feedback.Target, Find(feedback.Stat));
//     }
//
//     private void OnAttack(BattleScript source, BattleScript target)
//     {
//         if (!Accepts(source) || !Accepts(target) || attackImpact == null || attackImpact.template == null) return;
//         Create(target, null, new[] { attackImpact });
//     }
//
//     private void Show(BattleScript target, Binding binding)
//     {
//         if (!Accepts(target) || binding == null || binding.parts == null) return;
//         if (binding.sustained)
//             foreach (var visual in visuals)
//                 if (!visual.stopping && visual.target == target && visual.binding == binding) return;
//         Create(target, binding, binding.parts);
//     }
//
//     private void Create(BattleScript target, Binding binding, Part[] parts)
//     {
//         bool sustained = binding != null && binding.sustained;
//         if (!sustained)
//         {
//             int count = 0, oldest = -1;
//             for (int i = 0; i < visuals.Count; i++)
//                 if (visuals[i].binding == null || !visuals[i].binding.sustained)
//                 { count++; if (oldest < 0) oldest = i; }
//             if (count >= maximumBursts && oldest >= 0) Remove(oldest);
//             BurstCount++;
//         }
//         if (!actors.ContainsKey(target)) actors[target] = target.GetComponent<Renderer>();
//         var root = new GameObject(binding != null ? binding.name : "Attack");
//         root.SetActive(false);
//         root.transform.SetParent(transform, false);
//         var systems = new List<ParticleSystem>();
//         foreach (var part in parts)
//         {
//             if (part == null || part.template == null) continue;
//             var system = Instantiate(part.template, root.transform, false);
//             system.name = part.template.name;
//             system.gameObject.SetActive(true);
//             system.transform.localPosition = part.offset;
//             system.transform.localScale = part.template.transform.localScale * part.scale;
//             var main = system.main;
//             main.playOnAwake = false;
//             main.loop = sustained && part.loop;
//             main.stopAction = ParticleSystemStopAction.None;
//             main.scalingMode = ParticleSystemScalingMode.Hierarchy;
//             main.simulationSpace = ParticleSystemSimulationSpace.Local;
//             main.simulationSpeed = part.playbackSpeed;
//             systems.Add(system);
//         }
//         var visual = new Visual { target = target, binding = binding, root = root, systems = systems.ToArray() };
//         visuals.Add(visual);
//         Position(visual);
//         root.SetActive(true);
//         foreach (var system in visual.systems) system.Play(false);
//     }
//
//     private bool StillApplied(Visual visual)
//     {
//         if (!Accepts(visual.target)) return false;
//         if (visual.binding.Matches(StatType.Shield)) return visual.target.shield > 0;
//         foreach (var effect in Effect.ActiveEffects)
//             if (effect != null && effect.IsApplied && effect.target == visual.target && visual.binding.Matches(effect.effectType)) return true;
//         return false;
//     }
//
//     private void Position(Visual visual)
//     {
//         if (visual.target == null) return;
//         var renderer = actors[visual.target];
//         var bounds = renderer != null ? renderer.bounds : new Bounds(visual.target.transform.position, Vector3.one * 3);
//         Vector3 towardCamera = viewCamera != null ? Vector3.ProjectOnPlane(-viewCamera.transform.forward, Vector3.up).normalized : Vector3.zero;
//         visual.root.transform.SetPositionAndRotation(bounds.center + towardCamera * (bounds.size.y * cameraOffset), Quaternion.identity);
//         visual.root.transform.localScale = Vector3.one * Mathf.Max(.25f, bounds.size.y);
//     }
//
//     private void LateUpdate()
//     {
//         if (finished) return;
//         // Initial encounter shields (including sacrifice) may arrive without a card receipt.
//         var shield = Find(StatType.Shield);
//         if (shield != null) foreach (var actor in actors.Keys)
//             if (Accepts(actor) && actor.shield > 0) Show(actor, shield);
//         for (int i = visuals.Count - 1; i >= 0; i--)
//         {
//             var visual = visuals[i];
//             if (visual.target == null || !visual.target.gameObject.activeInHierarchy) { Remove(i); continue; }
//             Position(visual);
//             if (!visual.stopping && visual.binding != null && visual.binding.sustained && !StillApplied(visual))
//             {
//                 visual.stopping = true;
//                 foreach (var system in visual.systems) system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
//             }
//             bool alive = false;
//             foreach (var system in visual.systems) if (system != null && system.IsAlive(false)) { alive = true; break; }
//             if (!alive) Remove(i);
//         }
//     }
//
//     private void Remove(int index)
//     {
//         var root = visuals[index].root;
//         if (root != null) { root.SetActive(false); Destroy(root); }
//         visuals.RemoveAt(index);
//     }
//
//     private void OnFinished(BattleScript player, bool victory)
//     {
//         if (player == null || player.gameObject.scene != gameObject.scene) return;
//         finished = true;
//         Clear();
//     }
//
//     private void Clear() { for (int i = visuals.Count - 1; i >= 0; i--) Remove(i); }
//
//     private void OnDisable()
//     {
//         CardEffectFeedback.Changed -= OnEffect;
//         BattleScript.OnAttackPerformed -= OnAttack;
//         BattleScript.OnBattleFinished -= OnFinished;
//         Clear();
//     }
//
//     private sealed class Visual
//     {
//         public BattleScript target;
//         public Binding binding;
//         public GameObject root;
//         public ParticleSystem[] systems;
//         public bool stopping;
//     }
// }
