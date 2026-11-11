using UnityEngine;

/// <summary>
/// Draws one layered archer: a body SpriteRenderer and a weapon SpriteRenderer (higher sorting
/// order, same transform) set from ONE frame index, so the two layers cannot drift apart.
/// Playback is a pure function of the elapsed time the owner passes to <see cref="Evaluate"/> —
/// the Loadouts screen keeps one shared clock and evaluates every archer with it; each archer
/// maps that time to a frame with its own frame count, fps and loop rule. A looping animation
/// wraps; a one-shot holds its last frame for <see cref="HoldSeconds"/> then replays, the same
/// behaviour as AnimationVersionPlayer.
/// </summary>
public class LoadoutPlayer : MonoBehaviour
{
    public const float HoldSeconds = 0.5f;

    // A clock stepped in exact 1/fps increments can land a hair under a frame boundary in float
    // arithmetic; this keeps "frame N at N/fps" exact.
    private const float FrameEpsilon = 0.001f;

    private SpriteRenderer _body;
    private SpriteRenderer _weapon;
    private Sprite[] _bodyFrames;
    private Sprite[] _weaponFrames;
    private int _frameCount;
    private int _fps = 1;
    private bool _loop;
    private float _elapsed;

    private void Awake()
    {
        _body = gameObject.AddComponent<SpriteRenderer>();
        var weaponGo = new GameObject("Weapon");
        weaponGo.transform.SetParent(transform, false);
        _weapon = weaponGo.AddComponent<SpriteRenderer>();
        _weapon.sortingOrder = _body.sortingOrder + 1;
    }

    /// <summary>Seconds one full play takes: a loop is its frames; a one-shot adds the hold.</summary>
    public static float PlayDuration(int frameCount, int fps, bool loop) =>
        frameCount / (float)Mathf.Max(1, fps) + (loop ? 0f : HoldSeconds);

    /// <summary>The 0-based frame the last evaluated time shows (the sheet's cell index); 0 when it has none.</summary>
    public int Frame => FrameAt(_elapsed, _frameCount, _fps, _loop);

    /// <summary>Seconds this archer's current animation takes to repeat; 0 when it has none.</summary>
    public float Period => _frameCount > 0 ? PlayDuration(_frameCount, _fps, _loop) : 0f;

    /// <summary>Which frame is showing <paramref name="elapsed"/> seconds after the start.</summary>
    public static int FrameAt(float elapsed, int frameCount, int fps, bool loop)
    {
        if (frameCount <= 0) return 0;
        int tick = Mathf.FloorToInt(Mathf.Max(0f, elapsed) * Mathf.Max(1, fps) + FrameEpsilon);
        if (loop) return tick % frameCount;

        float period = PlayDuration(frameCount, fps, false);
        float within = Mathf.Max(0f, elapsed) % period;
        return Mathf.Min(Mathf.FloorToInt(within * Mathf.Max(1, fps) + FrameEpsilon), frameCount - 1);
    }

    /// <summary>Hands this archer its frames; either layer may be null (a missing sheet draws
    /// nothing for that layer).</summary>
    public void Play(Sprite[] bodyFrames, Sprite[] weaponFrames, int frameCount, int fps, bool loop)
    {
        _bodyFrames = bodyFrames;
        _weaponFrames = weaponFrames;
        _frameCount = frameCount;
        _fps = Mathf.Max(1, fps);
        _loop = loop;
        Evaluate(0f);
    }

    /// <summary>Swaps the weapon layer's frames without touching the body or the clock; null
    /// means no bow. The weapon layer shows the frame for the last evaluated time at once, so
    /// no render in between can draw the body without its bow.</summary>
    public void SetWeaponFrames(Sprite[] weaponFrames)
    {
        _weaponFrames = weaponFrames;
        _weapon.sprite = SpriteAt(_weaponFrames, FrameAt(_elapsed, _frameCount, _fps, _loop));
    }

    /// <summary>Drops the frames so the sheets they came from can be unloaded.</summary>
    public void Clear()
    {
        _bodyFrames = null;
        _weaponFrames = null;
        _body.sprite = null;
        _weapon.sprite = null;
    }

    public void Evaluate(float elapsed)
    {
        _elapsed = elapsed;
        int frame = FrameAt(elapsed, _frameCount, _fps, _loop);
        _body.sprite = SpriteAt(_bodyFrames, frame);
        _weapon.sprite = SpriteAt(_weaponFrames, frame);
    }

    private static Sprite SpriteAt(Sprite[] frames, int index) =>
        frames != null && index < frames.Length ? frames[index] : null;
}
