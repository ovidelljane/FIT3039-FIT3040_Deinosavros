using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class RewardScreenController : MonoBehaviour
{
    [SerializeField] private GameObject rewardPanel;
    [SerializeField] private Transform rewardCardContainer;
    [SerializeField] private GameObject rewardCardPrefab;
    [SerializeField] private CardPool cardPool;
    [SerializeField] private Button skipButton;
    [SerializeField] private int rewardOptionCount = 3;
    [SerializeField] private string mapSceneName = "Map";

    private void OnEnable()
    {
        BattleScript.OnAllEnemiesDefeated += HandleVictory;
    }

    private void OnDisable()
    {
        BattleScript.OnAllEnemiesDefeated -= HandleVictory;
    }

    private void Start()
    {
        if (rewardPanel != null) rewardPanel.SetActive(false);
        if (skipButton != null) skipButton.onClick.AddListener(ReturnToMap);
    }

    private void HandleVictory()
    {
        Time.timeScale = 0f;
        rewardPanel.SetActive(true);

        foreach (CardDefinition offer in cardPool.GetRandomCards(rewardOptionCount))
        {
            GameObject instance = Instantiate(rewardCardPrefab, rewardCardContainer);
            RewardCardView view = instance.GetComponent<RewardCardView>();
            if (view != null) view.Initialize(offer, ChooseCard);
        }
    }

    private void ChooseCard(CardDefinition definition)
    {
        RunSession.Instance.AddCard(definition);
        ReturnToMap();
    }

    private void ReturnToMap()
    {
        Time.timeScale = 1f;
        SceneManager.LoadSceneAsync(mapSceneName);
    }
}
