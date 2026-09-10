using UnityEngine;
using TMPro;

public class HealthLabel : MonoBehaviour
{
    [SerializeField] float yOffset = 2f;
    [SerializeField] float fontSize = 36f;
    [SerializeField] string displayName = "Enemy";
    TextMeshProUGUI label;
    BattleScript hp;
    private int healthOverNeg;

    void Awake() => hp = GetComponent<BattleScript>();

    void Start()
    {
        Canvas canvas = FindObjectOfType<Canvas>();

        GameObject go = new GameObject($"{name}_Label", typeof(RectTransform));
        go.transform.SetParent(canvas.transform, false);

        label = go.AddComponent<TextMeshProUGUI>();
        label.fontSize = fontSize;
        label.alignment = TextAlignmentOptions.Bottom;
        label.enableWordWrapping = false;

        Vector3 vp = Camera.main.WorldToViewportPoint(transform.position + Vector3.up * yOffset);
        RectTransform rt = label.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(vp.x, vp.y);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.sizeDelta = new Vector2(300, 60);
        rt.anchoredPosition = Vector2.zero;
    }

    void Update()
    {
        if (hp.health < 0)
        {
            healthOverNeg = 0;
        }
        else
        {
            healthOverNeg = hp.health;
        }
        label.text = $"{displayName}: {healthOverNeg}/{hp.maxHealth}";
    }
}