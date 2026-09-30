using UnityEngine;

namespace Deinosavros.MapReview
{
    public sealed class MapReviewEnvironmentSource : MonoBehaviour
    {
        [HideInInspector] public string modelAssetPath;
        [HideInInspector] public bool representativeSample;
    }
}
