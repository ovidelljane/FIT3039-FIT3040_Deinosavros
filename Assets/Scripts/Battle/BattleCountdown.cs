using System.Collections;
using TMPro;
using UnityEngine;

// Counts down in the middle of the screen, then starts the battle clock.
[RequireComponent(typeof(TimeTickSystem))]
public sealed class BattleCountdown : MonoBehaviour
{
    [SerializeField] private RectTransform canvas;
    [SerializeField] private int countFrom = 5;
    [SerializeField] private float fontSize = 220f;

    private IEnumerator Start()
    {
        TextMeshProUGUI text = CreateText();
        for (int number = countFrom; number > 0; number--)
        {
            text.text = number.ToString();
            for (float elapsed = 0f; elapsed < 1f; elapsed += Time.deltaTime)
            {
                // Each number pops in large, settles, then fades just before the next one.
                text.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.5f, 1f, Mathf.Clamp01(elapsed * 4f));
                text.alpha = 1f - Mathf.Clamp01((elapsed - 0.7f) / 0.3f);
                yield return null;
            }
        }
        Destroy(text.gameObject);
        GetComponent<TimeTickSystem>().StartTimer();
    }

    private TextMeshProUGUI CreateText()
    {
        var textObject = new GameObject("Countdown", typeof(RectTransform));
        textObject.transform.SetParent(canvas, false);
        RectTransform rect = (RectTransform)textObject.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(400f, 300f);

        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        GameFonts.Apply(text);
        text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.Center;
        text.color = new Color(0.95f, 0.88f, 0.7f);
        text.outlineWidth = 0.2f;
        text.outlineColor = new Color32(20, 14, 10, 255);
        text.raycastTarget = false;
        return text;
    }
}
