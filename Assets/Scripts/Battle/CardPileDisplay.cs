using TMPro;
using UnityEngine;
using UnityEngine.UI;

// A simple rounded card-stack shape with a caption and card count, anchored to a bottom corner.
public sealed class CardPileDisplay : MonoBehaviour
{
    private const int SpriteSize = 64;
    private const int CornerRadius = 16;

    private static Sprite roundedSprite;

    private TextMeshProUGUI countText;

    public static CardPileDisplay Create(Transform parent, int siblingIndex, string caption, bool leftCorner)
    {
        var pileObject = new GameObject($"{caption}Pile", typeof(RectTransform));
        pileObject.transform.SetParent(parent, false);
        pileObject.transform.SetSiblingIndex(siblingIndex);

        RectTransform rect = (RectTransform)pileObject.transform;
        Vector2 corner = leftCorner ? new Vector2(0f, 0f) : new Vector2(1f, 0f);
        rect.anchorMin = rect.anchorMax = rect.pivot = corner;
        rect.anchoredPosition = new Vector2(leftCorner ? 50f : -50f, 40f);
        rect.sizeDelta = new Vector2(120f, 170f);

        // A second card peeking out behind the first makes it read as a stack.
        CreateCard(pileObject.transform, new Vector2(8f, 8f), new Color(0.07f, 0.06f, 0.09f), new Color(0.55f, 0.42f, 0.2f));
        CreateCard(pileObject.transform, Vector2.zero, new Color(0.13f, 0.11f, 0.16f), new Color(0.85f, 0.66f, 0.3f));

        CardPileDisplay pile = pileObject.AddComponent<CardPileDisplay>();
        pile.countText = CreateText(pileObject.transform, 56f);
        TextMeshProUGUI captionText = CreateText(pileObject.transform, 22f);
        captionText.text = caption.ToUpperInvariant();
        RectTransform captionRect = captionText.rectTransform;
        captionRect.anchorMin = new Vector2(0f, 1f);
        captionRect.anchorMax = new Vector2(1f, 1f);
        captionRect.pivot = new Vector2(0.5f, 0f);
        captionRect.anchoredPosition = new Vector2(0f, 14f);
        captionRect.sizeDelta = new Vector2(40f, 30f);
        return pile;
    }

    public void SetCount(int count)
    {
        countText.text = count.ToString();
    }

    private static void CreateCard(Transform parent, Vector2 offset, Color fill, Color outline)
    {
        Image outlineImage = CreateRounded(parent, "Card", outline);
        RectTransform outlineRect = outlineImage.rectTransform;
        outlineRect.anchorMin = Vector2.zero;
        outlineRect.anchorMax = Vector2.one;
        outlineRect.offsetMin = offset;
        outlineRect.offsetMax = offset;

        Image fillImage = CreateRounded(outlineImage.transform, "Fill", fill);
        RectTransform fillRect = fillImage.rectTransform;
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = new Vector2(4f, 4f);
        fillRect.offsetMax = new Vector2(-4f, -4f);
    }

    private static Image CreateRounded(Transform parent, string objectName, Color color)
    {
        var imageObject = new GameObject(objectName, typeof(RectTransform));
        imageObject.transform.SetParent(parent, false);
        Image image = imageObject.AddComponent<Image>();
        image.sprite = RoundedSprite();
        image.type = Image.Type.Sliced;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static TextMeshProUGUI CreateText(Transform parent, float fontSize)
    {
        var textObject = new GameObject("Text", typeof(RectTransform));
        textObject.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)textObject.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;

        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        GameFonts.Apply(text);
        text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.Center;
        text.color = new Color(0.95f, 0.88f, 0.7f);
        text.raycastTarget = false;
        return text;
    }

    // White rounded square, 9-sliced so the corners keep their radius at any size.
    private static Sprite RoundedSprite()
    {
        if (roundedSprite != null) return roundedSprite;

        var texture = new Texture2D(SpriteSize, SpriteSize, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        var pixels = new Color32[SpriteSize * SpriteSize];
        float max = SpriteSize - CornerRadius;
        for (int y = 0; y < SpriteSize; y++)
        {
            for (int x = 0; x < SpriteSize; x++)
            {
                float px = x + 0.5f;
                float py = y + 0.5f;
                float dx = Mathf.Max(CornerRadius - px, px - max, 0f);
                float dy = Mathf.Max(CornerRadius - py, py - max, 0f);
                float alpha = Mathf.Clamp01(CornerRadius - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                pixels[y * SpriteSize + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply();

        roundedSprite = Sprite.Create(texture, new Rect(0f, 0f, SpriteSize, SpriteSize), new Vector2(0.5f, 0.5f),
            100f, 0, SpriteMeshType.FullRect, new Vector4(CornerRadius, CornerRadius, CornerRadius, CornerRadius));
        return roundedSprite;
    }
}
