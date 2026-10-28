using UnityEngine;
using UnityEngine.EventSystems;

// Only the detail card owns this hover region, not the surrounding status and warning UI.
[DisallowMultipleComponent]
public sealed class MapCardDetailPointer : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public MapSacrificePanel owner;
    public void OnPointerEnter(PointerEventData eventData) => owner?.OnPointerEnter(eventData);
    public void OnPointerExit(PointerEventData eventData) => owner?.OnPointerExit(eventData);
}
