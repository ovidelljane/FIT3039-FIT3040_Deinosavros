using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class HealthLabel : MonoBehaviour
{
    [SerializeField] float yOffset = 2f;
    [SerializeField] float fontSize = 36f;
    [SerializeField] string displayName = "Enemy";
    [SerializeField] Canvas battleCanvas;
    TextMeshProUGUI label;
    BattleScript hp;
    private RectTransform enemyPanel;
    private Image enemyFill;
    private TMP_Text attackText;
    private CanvasGroup visibility;
    private Camera worldCamera;
    private int shownHealth = int.MinValue, shownMaximum = int.MinValue, shownAttack = int.MinValue;
    public bool IsReady => label != null && label.isActiveAndEnabled && battleCanvas != null &&
        label.canvas == battleCanvas && battleCanvas.gameObject.scene == gameObject.scene;

    void Awake() => hp = GetComponent<BattleScript>();

    void Start()
    {
        Canvas canvas = BattleHud.ResolveCanvas(this, battleCanvas);
        battleCanvas = canvas;
        if (canvas == null) { Debug.LogError("No scene-owned canvas for the health label.", this); return; }

        if (CompareTag("Enemy"))
        {
            BuildEnemyStatus(canvas);
            RefreshEnemy();
            FollowEnemy();
            return;
        }

        GameObject go = new GameObject($"{name}_Label", typeof(RectTransform));
        go.transform.SetParent(canvas.transform, false);

        label = go.AddComponent<TextMeshProUGUI>();
        GameFonts.Apply(label, GameFontRole.Numeric);
        label.color = PlayerStatusView.Cream;
        label.raycastTarget = false;
        label.fontSize = fontSize;
        label.alignment = TextAlignmentOptions.Bottom;
        label.enableWordWrapping = false;

        Vector3 vp = Camera.main != null
            ? Camera.main.WorldToViewportPoint(transform.position + Vector3.up * yOffset)
            : new Vector3(.5f, .5f, 1);
        RectTransform rt = label.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(vp.x, vp.y);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.sizeDelta = new Vector2(300, 60);
        rt.anchoredPosition = Vector2.zero;
    }

    private void BuildEnemyStatus(Canvas canvas)
    {
        enemyPanel = PlayerStatusView.Rect(name + "_Status", canvas.transform);
        enemyPanel.pivot = new Vector2(.5f, 0);
        enemyPanel.sizeDelta = new Vector2(144, 54);
        enemyPanel.SetAsFirstSibling();
        var background = enemyPanel.gameObject.AddComponent<Image>();
        background.color = PlayerStatusView.Ink;
        background.raycastTarget = false;
        var edge = enemyPanel.gameObject.AddComponent<Outline>();
        edge.effectColor = PlayerStatusView.Gold;
        edge.effectDistance = new Vector2(1, -1);
        visibility = enemyPanel.gameObject.AddComponent<CanvasGroup>();
        visibility.interactable = visibility.blocksRaycasts = false;

        var title = PlayerStatusView.Text(enemyPanel, "Name", displayName, 15, GameFontRole.Heading);
        title.alignment = TextAlignmentOptions.Left;
        title.enableAutoSizing = true;
        title.fontSizeMin = 11;
        title.fontSizeMax = 15;
        PlayerStatusView.Place(title.rectTransform, new Vector2(0,1), new Vector2(8,-14), new Vector2(78,22), new Vector2(0,.5f));
        var icon = PlayerStatusView.Icon(enemyPanel, StatusSymbol.Damage, new Color(.88f,.52f,.32f));
        PlayerStatusView.Place(icon.rectTransform, Vector2.one, new Vector2(-44,-14), new Vector2(20,20));
        attackText = PlayerStatusView.Text(enemyPanel, "Attack", "", 22, GameFontRole.Numeric);
        PlayerStatusView.Place(attackText.rectTransform, Vector2.one, new Vector2(-19,-14), new Vector2(30,26));

        var track = PlayerStatusView.Box("Health", enemyPanel, new Color(.08f,.065f,.06f));
        PlayerStatusView.Place(track.rectTransform, new Vector2(.5f,0), new Vector2(0,8), new Vector2(124,16), new Vector2(.5f,0));
        var trackEdge = track.gameObject.AddComponent<Outline>();
        trackEdge.effectColor = PlayerStatusView.Gold;
        trackEdge.effectDistance = new Vector2(1,-1);
        enemyFill = PlayerStatusView.Box("Fill", track.transform, new Color(.68f,.21f,.19f));
        enemyFill.rectTransform.anchorMin = Vector2.zero;
        enemyFill.rectTransform.anchorMax = Vector2.one;
        enemyFill.rectTransform.offsetMin = enemyFill.rectTransform.offsetMax = Vector2.zero;
        label = (TextMeshProUGUI)PlayerStatusView.Text(track.transform, "Value", "", 18, GameFontRole.Numeric);
        PlayerStatusView.Place(label.rectTransform, new Vector2(.5f,.5f), Vector2.zero, new Vector2(124,22));
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
        visibility.alpha = viewport.z > 0 && viewport.x >= 0 && viewport.x <= 1 && viewport.y >= 0 && viewport.y <= 1 ? 1 : 0;
        enemyPanel.anchorMin = enemyPanel.anchorMax = new Vector2(viewport.x, viewport.y);
        enemyPanel.anchoredPosition = Vector2.zero;
    }

    void LateUpdate()
    {
        if (label == null || hp == null) return;
        if (enemyPanel != null) { RefreshEnemy(); FollowEnemy(); }
        else label.text = $"{displayName}: {Mathf.Max(0, hp.health)}/{hp.maxHealth}";
    }

    void OnEnable() { if (enemyPanel != null) enemyPanel.gameObject.SetActive(true); }
    void OnDisable() { if (enemyPanel != null) enemyPanel.gameObject.SetActive(false); }
    void OnDestroy()
    {
        if (enemyPanel != null) Destroy(enemyPanel.gameObject);
        else if (label != null) Destroy(label.gameObject);
    }
}
