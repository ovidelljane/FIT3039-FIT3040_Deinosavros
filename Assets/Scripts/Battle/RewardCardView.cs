using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class RewardCardView : MonoBehaviour
{
    [SerializeField] private Image artwork;
    [SerializeField] private Image border;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text effectText;
    [SerializeField] private Button chooseButton;

    private CardDefinition definition;
    private Action<CardDefinition> onChosen;

    public void Initialize(CardDefinition cardDefinition, Action<CardDefinition> onChosenCallback)
    {
        definition = cardDefinition;
        onChosen = onChosenCallback;

        if (artwork != null) artwork.sprite = definition.frontArtwork;
        if (border != null) border.sprite = definition.frontBorder;
        if (nameText != null) nameText.text = definition.displayName;
        if (effectText != null) effectText.text = definition.combatEffectText;

        chooseButton.onClick.RemoveAllListeners();
        chooseButton.onClick.AddListener(() => onChosen?.Invoke(definition));
    }
}
