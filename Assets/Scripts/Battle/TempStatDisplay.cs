using UnityEngine;
using TMPro;

public class TempStatDisplay : MonoBehaviour
{
    [SerializeField] float xOffset = 2f;
    [SerializeField] float fontSize = 36f;
    [SerializeField] Canvas battleCanvas;
    TextMeshProUGUI label;
    BattleScript player;
    Camera sceneCamera;
    private int healthOverNeg;
    public TMP_Text Label => label;
    public BattleScript Target => player;
    public bool IsReady => label != null && label.isActiveAndEnabled && label.canvas == battleCanvas &&
        battleCanvas != null && battleCanvas.gameObject.scene == gameObject.scene;

    void Awake() => player = GetComponent<BattleScript>();

    void Start()
    {
        battleCanvas = BattleHud.ResolveCanvas(this, battleCanvas);
        sceneCamera = Camera.main;
        if (battleCanvas == null) { Debug.LogError("No scene-owned canvas for player statistics.", this); enabled = false; return; }

        GameObject go = new GameObject($"{name}_Label", typeof(RectTransform));
        go.transform.SetParent(battleCanvas.transform, false);

        label = go.AddComponent<TextMeshProUGUI>();
        label.fontSize = fontSize;
        label.raycastTarget = false;
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
        if (label == null || player == null) return;
        label.text = $"Health: {player.health}/{player.maxHealth}\nDamage: {player.attackDmg}\nAttackSpd: Attack/{player.attackSpd} Secs\nElixir: {player.elixir:F1}/{player.maxElixir}\nShield: {player.shield}";
    }
    void LateUpdate() => BattleHud.PositionLabel(label, battleCanvas, sceneCamera, transform.position + Vector3.right * xOffset);
    void OnDestroy() { if (label != null) Destroy(label.gameObject); }
}
