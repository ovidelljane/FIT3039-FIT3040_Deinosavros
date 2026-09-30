using UnityEngine;

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

    public static bool IsGameScene(string scene) => scene == "MainMenu" || scene == "Map" ||
        scene == "Deinosavros" || scene == "MapOpportunity" || scene == "MapRecovery";
}
