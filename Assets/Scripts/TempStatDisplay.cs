using UnityEngine;
using TMPro;

public class TempStatDisplay : MonoBehaviour
{
    [SerializeField] float xOffset = 2f;
    [SerializeField] float fontSize = 36f;
    TextMeshProUGUI label;
    BattleScript player;
    private int healthOverNeg;

    void Awake() => player = GetComponent<BattleScript>();

    void Start()
    {
        Canvas canvas = FindObjectOfType<Canvas>();

        GameObject go = new GameObject($"{name}_Label", typeof(RectTransform));
        go.transform.SetParent(canvas.transform, false);

        label = go.AddComponent<TextMeshProUGUI>();
        label.fontSize = fontSize;
        label.alignment = TextAlignmentOptions.Right;
        label.enableWordWrapping = false;

        Vector3 vp = Camera.main.WorldToViewportPoint(transform.position + Vector3.right * xOffset);
        RectTransform rt = label.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(vp.x, vp.y);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.sizeDelta = new Vector2(300, 60);
        rt.anchoredPosition = Vector2.zero;
    }

    void Update()
    {
        label.text = $"Health: {player.health}/{player.maxHealth}\nDamage: {player.attackDmg}\nAttackSpd: Attack/{player.attackSpd} Secs\nElixir: {player.elixir:F1}/{player.maxElixir}\nShield: {player.shield}";
    }
}