using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// All visuals and button events are authored in the scene prefab, never generated here.
[DefaultExecutionOrder(-100)]
public sealed class PauseMenu : MonoBehaviour
{
    [SerializeField] private GameObject overlay, menu, confirmation;
    [SerializeField] private UnityEngine.UI.Button openButton, resumeButton, backButton, leaveButton;
    [SerializeField] private TMP_Text confirmationTitle, confirmationBody, leaveLabel;
    private static PauseMenu owner;
    private MapController map;
    private float previousTimeScale;
    private GameObject previousSelection;
    private bool leaving, quit, combatScene;
    public static bool IsPaused => owner != null;
    public static float PresentationDeltaTime => IsPaused ? 0 : Time.unscaledDeltaTime;
    public bool IsOpen => owner == this;
    public bool CanOpen => isActiveAndEnabled && !leaving && !IsPaused && Time.timeScale > 0 &&
        gameObject.scene == SceneManager.GetActiveScene() && !combatScene &&
        RunSession.Instance?.GetComponent<MapTravelCoordinator>()?.IsBusy != true &&
        (map == null || map.SacrificePanel?.IsBlocking != true);

    private void Awake()
    {
        combatScene = BattleHud.Find(gameObject.scene) != null;
        foreach (var root in gameObject.scene.GetRootGameObjects())
            if ((map = root.GetComponentInChildren<MapController>()) != null) break;
        overlay.SetActive(false);
        confirmation.SetActive(false);
    }

    private void OnEnable() => SceneManager.activeSceneChanged += SceneChanged;
    private void OnDisable()
    {
        SceneManager.activeSceneChanged -= SceneChanged;
        ReleasePause();
        if (overlay != null) overlay.SetActive(false);
    }
    private void SceneChanged(Scene previous, Scene current)
    {
        if (current != gameObject.scene) Resume();
    }

    private void Update()
    {
        openButton.interactable = IsOpen || CanOpen;
        bool escape = Keyboard.current?.escapeKey.wasPressedThisFrame == true;
        bool start = Gamepad.current?.startButton.wasPressedThisFrame == true;
        bool back = Gamepad.current?.buttonEast.wasPressedThisFrame == true;
        if (leaving) return;
        if (IsOpen && (escape || start || back))
        {
            if (confirmation.activeSelf) Back(); else Resume();
        }
        else if ((escape || start) && map?.SacrificePanel?.IsOpen != true &&
                 map?.SacrificePanel?.IsDepartureWarningOpen != true) Open();
    }

    public void Open()
    {
        if (!CanOpen) return;
        previousTimeScale = Time.timeScale;
        previousSelection = EventSystem.current?.currentSelectedGameObject;
        map?.NodeInteraction?.Close();
        map?.SacrificePanel?.HideImmediate();
        owner = this;
        Time.timeScale = 0;
        transform.SetAsLastSibling();
        menu.SetActive(true); confirmation.SetActive(false); overlay.SetActive(true);
        EventSystem.current?.SetSelectedGameObject(resumeButton.gameObject);
    }

    public void Resume()
    {
        if (!IsOpen || leaving) return;
        ReleasePause();
        overlay.SetActive(false);
        map?.ReleaseSacrificeInput();
        EventSystem.current?.SetSelectedGameObject(previousSelection != null && previousSelection.activeInHierarchy
            ? previousSelection : null);
    }

    private void ReleasePause()
    {
        if (!IsOpen) return;
        owner = null;
        Time.timeScale = previousTimeScale;
    }

    public void RequestMainMenu() => ShowConfirmation(false);
    public void RequestQuit() => ShowConfirmation(true);
    private void ShowConfirmation(bool quitting)
    {
        if (!IsOpen || leaving) return;
        quit = quitting;
        confirmationTitle.text = quit ? "QUIT GAME?" : "LEAVE THIS RUN?";
        confirmationBody.text = "Current run progress will be lost.";
        leaveLabel.text = quit ? "Quit Game" : "Main Menu";
        leaveButton.interactable = quit || Application.CanStreamedLevelBeLoaded("MainMenu");
        menu.SetActive(false); confirmation.SetActive(true);
        EventSystem.current?.SetSelectedGameObject(backButton.gameObject);
    }

    public void Back()
    {
        if (!IsOpen || leaving) return;
        confirmation.SetActive(false); menu.SetActive(true);
        EventSystem.current?.SetSelectedGameObject(resumeButton.gameObject);
    }

    public void ConfirmLeave()
    {
        if (!IsOpen || leaving || (!quit && !Application.CanStreamedLevelBeLoaded("MainMenu"))) return;
        leaving = true;
        ReleasePause();
        EventSystem.current?.SetSelectedGameObject(null);
        if (quit)
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
        else if (RunSession.Instance != null) MapTravelCoordinator.Ensure(RunSession.Instance).ReturnToMainMenu();
        else SceneManager.LoadSceneAsync("MainMenu");
    }
}
