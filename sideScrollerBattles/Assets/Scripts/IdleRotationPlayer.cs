using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives the "Idle rotation" entry: loops idle v9, and every
/// <see cref="fidgetIntervalSecondsRange"/> (real seconds, random) cuts to one of
/// <see cref="_fidgetEntries"/> (never the same one twice in a row) and back — crossfading both
/// ways over <see cref="crossfadeFrameCount"/> frames via two SpriteRenderers so the cut never
/// pops, per AnimationVersionEntry.matchedIdleFrames (precomputed at import time; see
/// Assets/Editor/FidgetIdleMatcher.cs — this class does no image comparison of its own):
///
/// - Idle keeps looping normally. Once a fidget is due, it waits — still looping — until the
///   current idle frame is one of that fidget's precomputed best-matching frames, then starts the
///   entry crossfade from there. Idle is never seen to jump.
/// - The entry crossfade fades the idle layer out and the fidget layer in (fidget playing from its
///   own frame 0) over crossfadeFrameCount frames, then the fidget plays through on its own.
/// - crossfadeFrameCount frames before the fidget's last frame, the exit crossfade begins: the
///   idle layer is snapped to the single best-matching idle frame (matchedIdleFrames[0]) and faded
///   back in while the fidget plays out and fades out. The idle jump here is hidden by the fidget
///   still dominating the blend early on.
///
/// <see cref="SetFidgetIntervalForDebug"/> exists purely so a verification capture doesn't have to
/// wait out the full 5-15s real-time range per cut.
/// </summary>
public class IdleRotationPlayer : MonoBehaviour
{
    private enum State { Idle, CrossfadeIn, Fidget, CrossfadeOut }

    [SerializeField] private SpriteRenderer baseRenderer;
    [SerializeField] private SpriteRenderer overlayRenderer;
    [SerializeField] private Text playingLabel;

    [Tooltip("Random real-seconds range between fidgets while idling.")]
    [SerializeField] private Vector2 fidgetIntervalSecondsRange = new Vector2(5f, 15f);
    [SerializeField] private int crossfadeFrameCount = 6;
    [Tooltip("Frame rate the crossfade duration (crossfadeFrameCount / this) is measured against — the fidgets' own fps, since the crossfade sits at each fidget's own start/end.")]
    [SerializeField] private float crossfadeFps = 24f;

    private AnimationVersionEntry _idleEntry;
    private AnimationVersionEntry[] _fidgetEntries;

    private State _state;
    private int _idleFrameIndex;
    private float _idleFrameTimer;
    private int _fidgetFrameIndex;
    private float _fidgetFrameTimer;
    private int _activeFidgetIndex;
    private int _lastFidgetIndex = -1;
    private float _fidgetDueTimer;
    private bool _fidgetPending;
    private float _crossfadeTimer;

    // Guards Update(): false until Begin() runs, and again once Stop() is called — otherwise a
    // leftover Update() tick (e.g. the one still in flight when the screen navigates away to a
    // normal version) reads _idleEntry before it's ever been set.
    private bool _active;

    /// <summary>Starts (or restarts) the rotation from a fresh idle loop.</summary>
    public void Begin(AnimationVersionEntry idleEntry, AnimationVersionEntry[] fidgetEntries)
    {
        _idleEntry = idleEntry;
        _fidgetEntries = fidgetEntries;
        _idleFrameIndex = 0;
        _idleFrameTimer = 0f;
        _lastFidgetIndex = -1;
        _state = State.Idle;
        ScheduleNextFidget();

        baseRenderer.enabled = true;
        baseRenderer.color = Color.white;
        baseRenderer.sprite = _idleEntry.frames[0];
        overlayRenderer.enabled = false;
        overlayRenderer.color = new Color(1f, 1f, 1f, 0f);
        SetLabel("idle");
        _active = true;
    }

    /// <summary>Hides both layers and stops ticking — called when the screen navigates away to a
    /// normal version.</summary>
    public void Stop()
    {
        _active = false;
        baseRenderer.enabled = false;
        overlayRenderer.enabled = false;
        SetLabel("");
    }

    /// <summary>Debug-only hook so a verification capture doesn't have to wait out the real 5-15s
    /// range per cut — see class doc.</summary>
    public void SetFidgetIntervalForDebug(float minSeconds, float maxSeconds)
    {
        fidgetIntervalSecondsRange = new Vector2(minSeconds, maxSeconds);
    }

    private void Update()
    {
        if (!_active) return;
        switch (_state)
        {
            case State.Idle: UpdateIdle(); break;
            case State.CrossfadeIn: UpdateCrossfadeIn(); break;
            case State.Fidget: UpdateFidget(); break;
            case State.CrossfadeOut: UpdateCrossfadeOut(); break;
        }
    }

    private void UpdateIdle()
    {
        AdvanceIdleFrame();

        if (!_fidgetPending)
        {
            _fidgetDueTimer -= Time.deltaTime;
            if (_fidgetDueTimer <= 0f)
            {
                _activeFidgetIndex = PickNextFidget();
                _fidgetPending = true;
            }
            return;
        }

        AnimationVersionEntry fidget = _fidgetEntries[_activeFidgetIndex];
        if (System.Array.IndexOf(fidget.matchedIdleFrames, _idleFrameIndex) >= 0)
        {
            StartCrossfadeIn(fidget);
        }
    }

    private int PickNextFidget()
    {
        if (_fidgetEntries.Length <= 1) return 0;
        int choice;
        do { choice = Random.Range(0, _fidgetEntries.Length); } while (choice == _lastFidgetIndex);
        return choice;
    }

    private void StartCrossfadeIn(AnimationVersionEntry fidget)
    {
        _state = State.CrossfadeIn;
        _crossfadeTimer = 0f;
        _fidgetFrameIndex = 0;
        _fidgetFrameTimer = 0f;
        _lastFidgetIndex = _activeFidgetIndex;
        _fidgetPending = false;

        overlayRenderer.enabled = true;
        overlayRenderer.sprite = fidget.frames[0];
        overlayRenderer.color = new Color(1f, 1f, 1f, 0f);
        SetLabel("→ " + FidgetLabel(fidget));

        float seam = DistanceForIdleFrame(fidget, _idleFrameIndex);
        Debug.Log(
            $"IdleRotationPlayer: entry crossfade — idle frame {_idleFrameIndex} -> {fidget.action} v{fidget.version} " +
            $"frame 0, seam distance {seam:F4}");
    }

    private void UpdateCrossfadeIn()
    {
        AdvanceIdleFrame();
        AnimationVersionEntry fidget = _fidgetEntries[_activeFidgetIndex];
        AdvanceFidgetFrame(fidget);

        _crossfadeTimer += Time.deltaTime;
        float t = Mathf.Clamp01(_crossfadeTimer / (crossfadeFrameCount / crossfadeFps));
        baseRenderer.color = new Color(1f, 1f, 1f, 1f - t);
        overlayRenderer.color = new Color(1f, 1f, 1f, t);

        if (t >= 1f)
        {
            _state = State.Fidget;
            baseRenderer.enabled = false;
            overlayRenderer.color = Color.white;
            SetLabel(FidgetLabel(fidget));
        }
    }

    private void UpdateFidget()
    {
        AnimationVersionEntry fidget = _fidgetEntries[_activeFidgetIndex];
        int lastIndex = fidget.frames.Length - 1;
        int crossfadeStartIndex = Mathf.Max(0, lastIndex - crossfadeFrameCount);

        if (_fidgetFrameIndex >= crossfadeStartIndex)
        {
            StartCrossfadeOut(fidget);
            return;
        }
        AdvanceFidgetFrame(fidget);
    }

    private void StartCrossfadeOut(AnimationVersionEntry fidget)
    {
        _state = State.CrossfadeOut;
        _crossfadeTimer = 0f;

        _idleFrameIndex = fidget.matchedIdleFrames[0];
        _idleFrameTimer = 0f;
        baseRenderer.enabled = true;
        baseRenderer.sprite = _idleEntry.frames[_idleFrameIndex];
        baseRenderer.color = new Color(1f, 1f, 1f, 0f);
        overlayRenderer.color = Color.white;
        SetLabel("→ idle");

        float seam = fidget.matchedIdleFrameDistances[0];
        Debug.Log(
            $"IdleRotationPlayer: exit crossfade — {fidget.action} v{fidget.version} last frame -> idle frame " +
            $"{_idleFrameIndex}, seam distance {seam:F4}");
    }

    private void UpdateCrossfadeOut()
    {
        AdvanceIdleFrame();
        AdvanceFidgetFrame(_fidgetEntries[_activeFidgetIndex]);

        _crossfadeTimer += Time.deltaTime;
        float t = Mathf.Clamp01(_crossfadeTimer / (crossfadeFrameCount / crossfadeFps));
        baseRenderer.color = new Color(1f, 1f, 1f, t);
        overlayRenderer.color = new Color(1f, 1f, 1f, 1f - t);

        if (t >= 1f)
        {
            _state = State.Idle;
            overlayRenderer.enabled = false;
            baseRenderer.color = Color.white;
            ScheduleNextFidget();
            SetLabel("idle");
        }
    }

    private void AdvanceIdleFrame()
    {
        _idleFrameTimer += Time.deltaTime;
        float frameSeconds = 1f / _idleEntry.fps;
        while (_idleFrameTimer >= frameSeconds)
        {
            _idleFrameTimer -= frameSeconds;
            _idleFrameIndex = (_idleFrameIndex + 1) % _idleEntry.frames.Length;
        }
        baseRenderer.sprite = _idleEntry.frames[_idleFrameIndex];
    }

    /// <summary>Steps toward the fidget's last frame and holds there — never loops; the state
    /// machine itself decides when to leave the last frame (via the exit crossfade).</summary>
    private void AdvanceFidgetFrame(AnimationVersionEntry fidget)
    {
        _fidgetFrameTimer += Time.deltaTime;
        float frameSeconds = 1f / fidget.fps;
        while (_fidgetFrameTimer >= frameSeconds)
        {
            _fidgetFrameTimer -= frameSeconds;
            if (_fidgetFrameIndex < fidget.frames.Length - 1) _fidgetFrameIndex++;
        }
        overlayRenderer.sprite = fidget.frames[_fidgetFrameIndex];
    }

    private void ScheduleNextFidget()
    {
        _fidgetDueTimer = Random.Range(fidgetIntervalSecondsRange.x, fidgetIntervalSecondsRange.y);
        _fidgetPending = false;
    }

    private static float DistanceForIdleFrame(AnimationVersionEntry fidget, int idleFrameIndex)
    {
        int position = System.Array.IndexOf(fidget.matchedIdleFrames, idleFrameIndex);
        return position >= 0 ? fidget.matchedIdleFrameDistances[position] : -1f;
    }

    private static string FidgetLabel(AnimationVersionEntry fidget) =>
        fidget.action.StartsWith("fidget_") ? fidget.action.Substring("fidget_".Length) : fidget.action;

    private void SetLabel(string text)
    {
        if (playingLabel != null) playingLabel.text = text;
    }
}
