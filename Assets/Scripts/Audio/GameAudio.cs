using UnityEngine;
using UnityEngine.SceneManagement;

// Owns playback independently of cards, RunSession, and scene transitions.
public sealed class GameAudio : MonoBehaviour
{
    private static GameAudio instance;
    [SerializeField] private GameAudioProfile profile;
    private AudioSource musicSource, cardSource;
    private AudioListener fallbackListener;
    private bool inGameScene;
    public int CardSoundCount { get; private set; }
    public AudioClip LastCardClip { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap() => Ensure();

    public static GameAudio Ensure()
    {
        if (instance == null) new GameObject("Audio").AddComponent<GameAudio>();
        return instance;
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        DontDestroyOnLoad(gameObject);
        profile = Resources.Load<GameAudioProfile>("GameAudio");
        musicSource = Source("Music", true);
        musicSource.priority = 128;
        musicSource.volume = 0;
        cardSource = Source("Cards", false);
        cardSource.priority = 32;
        var listener = new GameObject("Listener");
        listener.SetActive(false);
        listener.transform.SetParent(transform, false);
        fallbackListener = listener.AddComponent<AudioListener>();
        SceneManager.sceneLoaded += SceneLoaded;
        SceneManager.activeSceneChanged += ActiveSceneChanged;
        ApplyScene(SceneManager.GetActiveScene());
    }

    private AudioSource Source(string label, bool loop)
    {
        var child = new GameObject(label);
        child.transform.SetParent(transform, false);
        var source = child.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = loop;
        source.spatialBlend = 0;
        source.dopplerLevel = 0;
        return source;
    }

    private void SceneLoaded(Scene scene, LoadSceneMode mode) => ApplyScene(SceneManager.GetActiveScene());
    private void ActiveSceneChanged(Scene previous, Scene current) => ApplyScene(current);

    private void ApplyScene(Scene scene)
    {
        if (profile == null) profile = Resources.Load<GameAudioProfile>("GameAudio");
        inGameScene = GameAudioProfile.IsGameScene(scene.name);
        // Event pages have no scene listener; never compete with a scene's own listener.
        bool sceneHasListener = false;
        foreach (var listener in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
            if (listener != fallbackListener && listener.isActiveAndEnabled) { sceneHasListener = true; break; }
        fallbackListener.gameObject.SetActive(inGameScene && !sceneHasListener);
        if (inGameScene && profile != null && profile.music != null && !musicSource.isPlaying)
        {
            musicSource.clip = profile.music;
            musicSource.Play();
        }
    }

    private void Update()
    {
        if (profile == null) return;
        float target = inGameScene ? profile.musicVolume : 0;
        float rate = Mathf.Max(.01f, profile.musicVolume) / Mathf.Max(.1f, profile.musicFadeSeconds);
        musicSource.volume = Mathf.MoveTowards(musicSource.volume, target, Time.unscaledDeltaTime * rate);
        if (!inGameScene && musicSource.volume <= 0 && musicSource.isPlaying) musicSource.Stop();
    }

    public static void PlayCard(CardDefinition definition, AudioClip fallback = null)
    {
        var audio = Ensure();
        var settings = audio.profile;
        var clip = definition != null && settings != null ? settings.CardClip(definition.cardType) : null;
        float gain = clip != null ? settings.CardGain(definition.cardType) : 1;
        if (clip == null) clip = fallback;
        if (clip == null) return;
        audio.cardSource.PlayOneShot(clip, gain * (settings != null ? settings.cardVolume : 1));
        audio.LastCardClip = clip;
        audio.CardSoundCount++;
    }

    private void OnDestroy()
    {
        if (instance != this) return;
        SceneManager.sceneLoaded -= SceneLoaded;
        SceneManager.activeSceneChanged -= ActiveSceneChanged;
        instance = null;
    }
}
