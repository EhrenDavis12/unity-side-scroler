using UnityEngine;

/// <summary>
/// Sizes the orthographic camera so a 1x1-unit sprite cell "contain"-fits, centred, within the
/// top half of the screen, feet at world y=0. At the reference case (screen wide enough,
/// relative to a half-screen box) camera size 1 gives that exactly; a narrower screen grows
/// the size so the cell shrinks to fit the available width instead of overflowing it.
/// </summary>
[RequireComponent(typeof(Camera))]
public class ShowcaseCameraFit : MonoBehaviour
{
    private const float CellSize = 1f; // one 256px cell at 256 pixels-per-unit

    private Camera _camera;
    private int _lastWidth;
    private int _lastHeight;

    private void Awake()
    {
        _camera = GetComponent<Camera>();
        _camera.orthographic = true;
        Fit();
    }

    private void Update()
    {
        if (Screen.width != _lastWidth || Screen.height != _lastHeight) Fit();
    }

    private void Fit()
    {
        _lastWidth = Screen.width;
        _lastHeight = Screen.height;
        float aspect = _lastHeight > 0 ? (float)_lastWidth / _lastHeight : 1f;

        // Top-half box, in world units, at ortho size S: width = 2*S*aspect, height = S.
        // Height alone (S = CellSize) is exact whenever the resulting width already covers
        // the cell; otherwise grow S so the width does too, which letterboxes vertically.
        float size = CellSize;
        if (2f * size * aspect < CellSize) size = CellSize / (2f * aspect);
        _camera.orthographicSize = size;

        // Feet at y = 0: camera centred on y = 0 shows [-size, size], so the top half of the
        // view is exactly [0, size].
        transform.position = new Vector3(transform.position.x, 0f, transform.position.z);
    }
}
