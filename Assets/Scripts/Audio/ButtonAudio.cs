using UnityEngine;
using UnityEngine.EventSystems;

// Plays on press so disabling a button during its click cannot cut off feedback.
[DisallowMultipleComponent, RequireComponent(typeof(UnityEngine.UI.Button))]
public sealed class ButtonAudio : MonoBehaviour, IPointerDownHandler, ISubmitHandler
{
    [SerializeField] private GameSound sound = GameSound.MenuClick;
    [SerializeField, Range(0, 2)] private float volume = 1;
    private UnityEngine.UI.Button button;

    private void Awake() => button = GetComponent<UnityEngine.UI.Button>();

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left) Play();
    }

    public void OnSubmit(BaseEventData eventData) => Play();

    private void Play()
    {
        if (button != null && button.IsActive() && button.IsInteractable()) GameAudio.Play(sound, volume);
    }
}
