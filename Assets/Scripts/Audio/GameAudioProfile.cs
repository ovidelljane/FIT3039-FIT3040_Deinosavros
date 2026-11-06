using UnityEngine;

public enum GameSound { CardFlick, CardFlip, MenuClick, Error }

[CreateAssetMenu(menuName = "Deinosavros/Game Audio", fileName = "GameAudio")]
public sealed class GameAudioProfile : ScriptableObject
{
    [Header("Card Effects")]
    public AudioClip attack;
    public AudioClip buff;
    public AudioClip debuff;
    public AudioClip defense;
    [Range(0, 1)] public float cardVolume = 1f;
    [Range(0, 2)] public float attackGain = 1f;
    [Range(0, 2)] public float buffGain = .85f;
    [Range(0, 2)] public float debuffGain = .7f;
    [Range(0, 2)] public float defenseGain = 1.7f;

    [Header("Interaction Sounds")]
    public AudioClip cardFlick;
    public AudioClip cardFlip;
    public AudioClip menuClick;
    public AudioClip error;
    [Range(0, 1)] public float interactionVolume = 1f;
    [Range(0, 2)] public float cardFlickGain = 1.6f;
    [Range(0, 2)] public float cardFlipGain = 1.6f;
    [Range(0, 2)] public float menuClickGain = 1.6f;
    [Range(0, 2)] public float errorGain = 1f;

    [Header("Continuous Music")]
    public AudioClip music;
    [Range(0, 1)] public float musicVolume = .1f;
    [Min(.1f)] public float musicFadeSeconds = .8f;

    public AudioClip CardClip(MapCardType kind) => kind switch
    {
        MapCardType.Attack => attack,
        MapCardType.Buff or MapCardType.Elixir => buff,
        MapCardType.Debuff => debuff,
        MapCardType.Defense => defense,
        _ => null
    };

    public float CardGain(MapCardType kind) => kind switch
    {
        MapCardType.Attack => attackGain,
        MapCardType.Buff or MapCardType.Elixir => buffGain,
        MapCardType.Debuff => debuffGain,
        MapCardType.Defense => defenseGain,
        _ => 1f
    };

    public AudioClip InteractionClip(GameSound cue) => cue switch
    {
        GameSound.CardFlick => cardFlick,
        GameSound.CardFlip => cardFlip,
        GameSound.MenuClick => menuClick,
        GameSound.Error => error,
        _ => null
    };

    public float InteractionGain(GameSound cue) => cue switch
    {
        GameSound.CardFlick => cardFlickGain,
        GameSound.CardFlip => cardFlipGain,
        GameSound.MenuClick => menuClickGain,
        GameSound.Error => errorGain,
        _ => 1f
    };

    public static bool IsGameScene(string scene) => scene == "MainMenu" || scene == "Map" ||
        scene == "Deinosavros" || scene == "MapOpportunity" || scene == "MapRecovery";
}
