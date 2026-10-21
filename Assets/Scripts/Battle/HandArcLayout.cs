using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Fans the hand along a circle that shares its centre with the bottom HUD arc, so the cards sit
// in the semicircle with their lower edges tucked behind the dome. Replaces the hand's
// HorizontalLayoutGroup; card order comes from DeckManager.Hand, not sibling order.
[RequireComponent(typeof(RectTransform))]
public sealed class HandArcLayout : MonoBehaviour
{
    [SerializeField] private DeckManager deck;
    [Tooltip("Circle centre shared with the bottom HUD (its Arc object).")]
    [SerializeField] private RectTransform arcCentre;
    [Tooltip("Distance from the circle centre to each card's centre.")]
    [SerializeField] private float radius = 950f;
    [Tooltip("Angle between neighbouring cards.")]
    [SerializeField] private float cardSpacingDegrees = 7.5f;
    [Tooltip("The whole hand never spreads wider than this.")]
    [SerializeField] private float maxSpreadDegrees = 36f;
    [SerializeField] private float moveSpeed = 12f;

    private readonly HashSet<BuffCards> placed = new();
    private readonly List<Transform> drawOrder = new();
    private RectTransform rect;

    private void Awake()
    {
        rect = (RectTransform)transform;
        // Positions are owned here; a layout group would fight every frame.
        if (TryGetComponent<HorizontalLayoutGroup>(out var row)) row.enabled = false;
    }

    private void LateUpdate()
    {
        if (deck == null || arcCentre == null) return;
        var hand = deck.Hand;
        int count = hand.Count;
        if (count == 0) { placed.Clear(); return; }
        placed.RemoveWhere(card => card == null);

        Vector2 centre = (Vector2)rect.InverseTransformPoint(arcCentre.position) - rect.rect.center;
        float spread = Mathf.Min(maxSpreadDegrees, cardSpacingDegrees * (count - 1));
        float blend = 1f - Mathf.Exp(-moveSpeed * Time.unscaledDeltaTime);
        BuffCards hovered = null;
        drawOrder.Clear();

        for (int i = 0; i < count; i++)
        {
            var card = hand[i];
            if (card == null) continue;
            var cardRect = (RectTransform)card.transform;
            float t = count > 1 ? (float)i / (count - 1) - .5f : 0f;
            float angle = -t * spread;
            Vector2 target = centre + Direction(angle) * radius;
            float roll = card.IsHovered ? 0f : angle;

            if (placed.Add(card))
            {
                // New cards slide in from the left end of the fan.
                cardRect.anchorMin = cardRect.anchorMax = cardRect.pivot = new Vector2(.5f, .5f);
                cardRect.anchoredPosition = centre + Direction(spread * .5f + 20f) * radius;
                cardRect.localRotation = Quaternion.Euler(0, 0, spread * .5f + 20f);
            }
            cardRect.anchoredPosition = Vector2.Lerp(cardRect.anchoredPosition, target, blend);
            cardRect.localRotation = Quaternion.Slerp(cardRect.localRotation, Quaternion.Euler(0, 0, roll), blend);

            if (card.IsHovered) hovered = card; else drawOrder.Add(cardRect);
        }
        // Left to right, with the hovered card drawn over its neighbours.
        if (hovered != null) drawOrder.Add(hovered.transform);
        for (int i = 0; i < drawOrder.Count; i++)
            if (rect.GetChild(i) != drawOrder[i]) drawOrder[i].SetSiblingIndex(i);
    }

    // Angle in degrees from straight up; positive leans left, matching a positive Z rotation.
    private static Vector2 Direction(float angle)
    {
        float radians = angle * Mathf.Deg2Rad;
        return new Vector2(-Mathf.Sin(radians), Mathf.Cos(radians));
    }
}
