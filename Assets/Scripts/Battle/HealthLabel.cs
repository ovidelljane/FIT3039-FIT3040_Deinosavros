using UnityEngine;
using TMPro;

public class HealthLabel : MonoBehaviour
{
    [SerializeField] float yOffset = 2f;
    [SerializeField] float fontSize = 36f;
    [SerializeField] string displayName = "Enemy";
    [SerializeField] Canvas battleCanvas;
    TextMeshProUGUI label;
    BattleScript hp;
    Camera sceneCamera;
    private int healthOverNeg;
    public TMP_Text Label => label;
    public BattleScript Target => hp;
    public bool IsReady => label != null && label.isActiveAndEnabled && label.canvas == battleCanvas &&
        battleCanvas != null && battleCanvas.gameObject.scene == gameObject.scene;

    void Awake() => hp = GetComponent<BattleScript>();

    void Start()
    {
        battleCanvas = BattleHud.ResolveCanvas(this, battleCanvas);
        sceneCamera = Camera.main;
        if (battleCanvas == null) { Debug.LogError("No scene-owned canvas for the health label.", this); enabled = false; return; }

        GameObject go = new GameObject($"{name}_Label", typeof(RectTransform));
        go.transform.SetParent(battleCanvas.transform, false);

        label = go.AddComponent<TextMeshProUGUI>();
        label.fontSize = fontSize;
        label.raycastTarget = false;
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
        if (label == null || hp == null) return;
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
    void LateUpdate() => BattleHud.PositionLabel(label, battleCanvas, sceneCamera, transform.position + Vector3.up * yOffset);
    void OnDestroy() { if (label != null) Destroy(label.gameObject); }
}
