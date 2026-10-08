using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public enum GameFontRole { Heading, Body, Numeric }

// Builds TextMeshPro font assets at runtime from fonts in Resources/Fonts.
public static class GameFonts
{
    private static TMP_FontAsset cinzelBlack;
    private static TMP_FontAsset bodyFont;

    public static TMP_FontAsset CinzelBlack
    {
        get
        {
            if (cinzelBlack == null)
            {
                Font font = Resources.Load<Font>("Fonts/Cinzel-Black");
                if (font != null) cinzelBlack = TMP_FontAsset.CreateFontAsset(font);
            }
            return cinzelBlack;
        }
    }

    public static TMP_FontAsset Body
    {
        get
        {
            if (bodyFont == null)
            {
                var source = Resources.Load<Font>("Fonts/Alegreya");
                if (source != null) bodyFont = TMP_FontAsset.CreateFontAsset(source);
            }
            return bodyFont;
        }
    }
    public static void Apply(TMP_Text text, GameFontRole role = GameFontRole.Heading)
    {
        if (text == null) return;
        var font = role == GameFontRole.Heading ? CinzelBlack : Body;
        if (font != null) text.font = font;
        text.fontStyle = text.name == "CostText" ? FontStyles.Bold : FontStyles.Normal;
        text.characterSpacing = role == GameFontRole.Heading ? 1.2f : 0f;
    }
    public static void ApplyHierarchy(Transform root, Transform preserveRoot = null)
    {
        foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if (preserveRoot != null && text.transform.IsChildOf(preserveRoot)) continue;
            if (text.GetComponentInParent<MapSacrificePanel>(true) != null ||
                text.GetComponentInParent<MapPlayerStatusPanel>(true) != null ||
                text.GetComponentInParent<MapCardView>(true)?.PreservesBackTypography(text.transform) == true) continue;
            string label = text.name.ToLowerInvariant();
            var role = label.Contains("title") || label.Contains("name") || text.GetComponentInParent<Button>() != null
                ? GameFontRole.Heading : GameFontRole.Body;
            Apply(text, role);
        }
    }
    // Keep authored effect values intact while allowing a natural word break.
    public static string FormatEffect(string text) => text?.Replace("AttackSpeed", "Attack Speed") ?? "";
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InstallSceneTypography()
    {
        SceneManager.sceneLoaded -= StyleScene;
        SceneManager.sceneLoaded += StyleScene;
    }
    private static void StyleScene(Scene scene, LoadSceneMode mode)
    {
        // Encounter pages keep their scene-authored font and typography settings.
        if (scene.name != "MainMenu" && scene.name != "Map" && scene.name != "Deinosavros") return;
        Transform preserveRoot = scene.name == "Deinosavros" ? BattleHud.Find(scene)?.StatusRoot : null;
        foreach (var root in scene.GetRootGameObjects()) ApplyHierarchy(root.transform, preserveRoot);
    }
}
