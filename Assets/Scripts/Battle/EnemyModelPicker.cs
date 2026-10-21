using System.Collections.Generic;
using UnityEngine;

// Gives each enemy a random portrait when combat loads. Only the texture and quad width change;
// the enemy and shadow materials (and their mirroring) are shared.
public sealed class EnemyModelPicker : MonoBehaviour
{
    [SerializeField] private Texture2D[] enemyModels;
    [Tooltip("Size multiplier for enemies, applied on top of their scene scale.")]
    [SerializeField, Min(.1f)] private float enemyScale = 1f;

    private static readonly int MainTexture = Shader.PropertyToID("_MainTex");

    private void Awake()
    {
        var pool = new List<Texture2D>();
        foreach (var root in gameObject.scene.GetRootGameObjects())
            foreach (var actor in root.GetComponentsInChildren<BattleScript>())
            {
                if (!actor.isActiveAndEnabled || !actor.CompareTag("Enemy") || actor.GetComponent<BossCombat>() != null) continue;
                // Draw without repeats until every model has been used once.
                if (pool.Count == 0 && enemyModels != null)
                    foreach (var model in enemyModels) if (model != null) pool.Add(model);
                if (pool.Count == 0) return;
                int pick = Random.Range(0, pool.Count);
                Apply(actor, pool[pick], enemyScale);
                pool.RemoveAt(pick);
            }
    }

    private static void Apply(BattleScript enemy, Texture2D model, float scale)
    {
        var block = new MaterialPropertyBlock();
        // The body and its shadow copy show the same portrait. Other feedback properties are preserved.
        foreach (var body in enemy.GetComponentsInChildren<Renderer>())
        {
            body.GetPropertyBlock(block);
            block.SetTexture(MainTexture, model);
            body.SetPropertyBlock(block);
        }
        // Keep the authored height and fit the width to the new portrait's proportions.
        Vector3 size = enemy.transform.localScale;
        float height = Mathf.Abs(size.y) * scale, aspect = (float)model.width / Mathf.Max(1, model.height);
        enemy.transform.localScale = new Vector3(Mathf.Sign(size.x) * height * aspect, Mathf.Sign(size.y) * height,
            Mathf.Sign(size.z) * Mathf.Abs(size.z) * scale);
        // Grow upward from the feet rather than sinking into the arena floor.
        enemy.transform.position += enemy.transform.up * (Mathf.Sign(size.y) * (height - Mathf.Abs(size.y)) * .5f);
    }
}
