using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class HealthLabel : MonoBehaviour
{
    [SerializeField] float yOffset = 2f;
    [SerializeField, HideInInspector] float fontSize = 36f;
    [SerializeField] string displayName = "Enemy";
    [SerializeField] Canvas battleCanvas;
    [Header("Scene Status")]
    [SerializeField] private TextMeshProUGUI label;
    [SerializeField] private RectTransform enemyPanel;
    [SerializeField] private Image enemyFill;
    [SerializeField] private TMP_Text attackText;
    [SerializeField] private CanvasGroup visibility;
    BattleScript hp;
    private Camera worldCamera;
    private float visibleAlpha = 1f;
    private int shownHealth = int.MinValue, shownMaximum = int.MinValue, shownAttack = int.MinValue;
    public bool HasSceneBindings => label != null && (!CompareTag("Enemy") ||
        (enemyPanel != null && enemyFill != null && attackText != null && visibility != null));
    public bool IsReady => HasSceneBindings && label.isActiveAndEnabled && battleCanvas != null &&
        label.canvas == battleCanvas && battleCanvas.gameObject.scene == gameObject.scene;

    void Awake()
    {
        hp = GetComponent<BattleScript>();
        if (visibility != null) visibleAlpha = visibility.alpha;
    }

    void Start()
    {
        Canvas canvas = BattleHud.ResolveCanvas(this, battleCanvas);
        battleCanvas = canvas;
        if (canvas == null || !HasSceneBindings)
        {
            Debug.LogError("Assign the scene-owned health label and status references on this actor.", this);
            return;
        }

        if (CompareTag("Enemy"))
        {
            RefreshEnemy();
            FollowEnemy();
            return;
        }

        Vector3 vp = Camera.main != null
            ? Camera.main.WorldToViewportPoint(transform.position + Vector3.up * yOffset)
            : new Vector3(.5f, .5f, 1);
        RectTransform rt = label.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(vp.x, vp.y);
    }

    private void RefreshEnemy()
    {
        if (hp == null) return;
        int maximum = Mathf.Max(0, hp.maxHealth);
        int current = Mathf.Clamp(hp.health, 0, maximum);
        if (current != shownHealth || maximum != shownMaximum)
        {
            shownHealth = current;
            shownMaximum = maximum;
            enemyFill.rectTransform.anchorMax = new Vector2(maximum > 0 ? (float)current / maximum : 0, 1);
            label.text = $"{current} / {maximum}";
        }
        if (shownAttack != hp.attackDmg)
        {
            shownAttack = hp.attackDmg;
            // Per-hit attack, not attack speed or multi-hit total damage.
            attackText.text = Mathf.Max(0, hp.attackDmg).ToString();
        }
    }

    private void FollowEnemy()
    {
        if (worldCamera == null) worldCamera = Camera.main;
        if (worldCamera == null) { visibility.alpha = 0; return; }
        Vector3 viewport = worldCamera.WorldToViewportPoint(transform.position + Vector3.up * yOffset);
        visibility.alpha = viewport.z > 0 && viewport.x >= 0 && viewport.x <= 1 && viewport.y >= 0 && viewport.y <= 1 ? visibleAlpha : 0;
        enemyPanel.anchorMin = enemyPanel.anchorMax = new Vector2(viewport.x, viewport.y);
    }

    void LateUpdate()
    {
        if (!HasSceneBindings || hp == null) return;
        if (enemyPanel != null) { RefreshEnemy(); FollowEnemy(); }
        else label.text = $"{displayName}: {Mathf.Max(0, hp.health)}/{hp.maxHealth}";
    }

    private void SetVisible(bool visible)
    {
        if (enemyPanel != null) enemyPanel.gameObject.SetActive(visible);
        else if (label != null) label.gameObject.SetActive(visible);
    }
    void OnEnable() => SetVisible(true);
    void OnDisable() => SetVisible(false);
    void OnDestroy() => SetVisible(false);
}
