using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class MainMenuController : MonoBehaviour
{
    [SerializeField] private string firstBattleSceneName = "Deinosavros";
    [SerializeField] private CardDefinition[] startingDeck;
    [SerializeField] private Button startButton;
    private bool loading;

    private void Start()
    {
        Time.timeScale = 1f;
        startButton.onClick.AddListener(StartRun);
    }

    private void StartRun()
    {
        if (loading) return;
        loading = true;
        startButton.interactable = false;
        RunSession.StartNewRun(startingDeck);
        SceneManager.LoadSceneAsync(firstBattleSceneName);
    }
}
