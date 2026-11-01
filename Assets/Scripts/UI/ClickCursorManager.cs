using UnityEngine;
using UnityEngine.InputSystem;

// Claw hardware cursor for every scene; swaps to the click frame while the left button is held.
public sealed class ClickCursorManager : MonoBehaviour
{
    // On-screen cursor size in pixels (Windows' default arrow is 32).
    private const int Size = 80;
    // Claw tip within the baked Resources/Cursors textures, as a fraction of their size.
    private static readonly Vector2 Hotspot = new Vector2(0.0426f, 0.4359f);

    private static ClickCursorManager instance;
    private Texture2D defaultCursor, clickingCursor;
    private bool pressed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (instance == null) new GameObject("Cursor").AddComponent<ClickCursorManager>();
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        DontDestroyOnLoad(gameObject);
        defaultCursor = Resize(Resources.Load<Texture2D>("Cursors/ClawDefault"));
        clickingCursor = Resize(Resources.Load<Texture2D>("Cursors/ClawClick"));
        Apply(defaultCursor);
    }

    private void Update()
    {
        bool down = Mouse.current != null && Mouse.current.leftButton.isPressed;
        if (down == pressed) return;
        pressed = down;
        Apply(pressed ? clickingCursor : defaultCursor);
    }

    // Hardware cursors need an uncompressed RGBA32 texture at the exact display size.
    private static Texture2D Resize(Texture2D source)
    {
        if (source == null) return null;
        var target = RenderTexture.GetTemporary(Size, Size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var previous = RenderTexture.active;
        Graphics.Blit(source, target);
        RenderTexture.active = target;
        var cursor = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = source.name };
        cursor.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
        cursor.Apply();
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(target);
        return cursor;
    }

    private static void Apply(Texture2D cursor)
    {
        if (cursor != null) Cursor.SetCursor(cursor, Hotspot * Size, CursorMode.Auto);
    }

    private void OnDestroy()
    {
        if (instance != this) return;
        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        instance = null;
    }
}
