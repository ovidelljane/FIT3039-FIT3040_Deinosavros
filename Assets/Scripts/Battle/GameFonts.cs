using TMPro;
using UnityEngine;

// Builds TextMeshPro font assets at runtime from fonts in Resources/Fonts.
public static class GameFonts
{
    private static TMP_FontAsset cinzelBlack;

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

    public static void Apply(TMP_Text text)
    {
        if (CinzelBlack != null) text.font = CinzelBlack;
    }
}
