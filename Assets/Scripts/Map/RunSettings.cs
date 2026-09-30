using UnityEngine;
using UnityEngine.SceneManagement;

// The inspector edits one shared asset instead of independent copies in each scene.
public sealed class RunSettings : MonoBehaviour
{
    public RunBalance configuration;
    public static RunBalance ForScene(Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            var settings = root.GetComponentInChildren<RunSettings>(true);
            if (settings != null && settings.configuration != null) return settings.configuration;
        }
        return RunBalance.Default;
    }
}
