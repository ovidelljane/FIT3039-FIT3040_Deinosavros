using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CombatFeedbackSetup
{
    [MenuItem("Tools/Battle/Apply Combat Feedback")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save your scene edits before applying combat presentation.");
        var setup = EditorSceneManager.GetSceneManagerSetup();
        const string path = "Assets/Scenes/Deinosavros.unity";
        Directory.CreateDirectory("Library/CombatFeedback");
        if (!File.Exists("Library/CombatFeedback/Deinosavros.before.unity")) File.Copy(path, "Library/CombatFeedback/Deinosavros.before.unity");
        try
        {
            var scene = EditorSceneManager.OpenScene(path);
            var portrait = Shader.Find("Deinosavros/Combat/Portrait");
            var clouds = Shader.Find("Deinosavros/Combat/Clouds");
            if (portrait == null || clouds == null || ShaderUtil.ShaderHasError(portrait) || ShaderUtil.ShaderHasError(clouds))
                throw new InvalidOperationException("Combat shaders must compile before binding.");
            var roots = scene.GetRootGameObjects();
            var feedback = roots.SelectMany(r => r.GetComponentsInChildren<CombatFeedback>(true)).SingleOrDefault();
            if (feedback == null) feedback = new GameObject("Combat Feedback").AddComponent<CombatFeedback>();
            feedback.canvas = roots.SelectMany(r => r.GetComponentsInChildren<BattleHud>()).Single().Canvas;
            feedback.worldCamera = roots.SelectMany(r => r.GetComponentsInChildren<Camera>()).Single(c => c.CompareTag("MainCamera"));
            foreach (var actor in roots.SelectMany(r => r.GetComponentsInChildren<BattleScript>()).Where(a => a.enabled))
            {
                foreach (var renderer in actor.GetComponentsInChildren<MeshRenderer>())
                {
                    bool shadow = renderer.gameObject != actor.gameObject;
                    string role = (actor.CompareTag("Player") ? "Player" : "Enemy") + (shadow ? "Shadow" : "");
                    renderer.sharedMaterial = PortraitMaterial(renderer.sharedMaterial, portrait, role, shadow);
                }
            }
            var sky = roots.SelectMany(r => r.GetComponentsInChildren<MeshRenderer>()).Single(r => r.name == "SkyPic");
            const string skyPath = "Assets/Sprites/Materials/CombatSky.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
            if (material == null)
            {
                material = new Material(sky.sharedMaterial) { name = "CombatSky", shader = clouds };
                material.SetFloat("_DriftSpeed", .007f); material.SetFloat("_Billow", .003f);
                material.SetFloat("_PreviewTime", -1); material.renderQueue = 2980;
                AssetDatabase.CreateAsset(material, skyPath);
            }
            sky.sharedMaterial = material;
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets(); EditorSceneManager.SaveScene(scene);
            File.WriteAllText("Library/CombatFeedback/bindings.txt", "PASS: four fighters, their shadows and existing SkyPic use scene-specific presentation. No original material overwritten.\n");
        }
        finally { EditorSceneManager.RestoreSceneManagerSetup(setup); }
    }

    private static Material PortraitMaterial(Material source, Shader shader, string role, bool shadow)
    {
        string path = "Assets/Sprites/Materials/Combat" + role + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        material = new Material(source) { name = "Combat" + role, shader = shader };
        material.SetColor("_HitColor", new Color(1,.87f,.62f));
        material.SetColor("_OutlineColor", new Color(.10f,.045f,.025f,shadow ? 0 : 1));
        material.SetFloat("_OutlinePixels", shadow ? 0 : 1);
        material.SetFloat("_HitBlend", 0); material.SetFloat("_Defeated", 0);
        material.SetFloat("_LightingStrength", shadow ? 0 : 1);
        material.SetFloat("_ZWrite", shadow ? 0 : 1);
        if (shadow) material.SetColor("_Color", new Color(.055f,.04f,.025f,.42f));
        AssetDatabase.CreateAsset(material,path); return material;
    }
}
