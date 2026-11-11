using UnityEngine;

/// <summary>
/// Steps a SpriteRenderer through one AnimationVersionEntry's frames at its own fps, for the
/// Animation Versions comparison screen — no Animator, no AnimationClips, since every version
/// already carries everything needed to play it directly. A looping entry wraps back to frame 0;
/// a one-shot plays once, holds its last frame for <see cref="holdSeconds"/>, then replays from
/// frame 0 — repeating forever, since this is a review loop, not gameplay. Calling
/// <see cref="Play"/> again (a version switch) always restarts from frame 0.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class AnimationVersionPlayer : MonoBehaviour
{
    [Tooltip("How long a one-shot holds its last frame before replaying.")]
    [SerializeField] private float holdSeconds = 0.5f;

    private SpriteRenderer _spriteRenderer;
    private Sprite[] _frames;
    private int _fps;
    private bool _loop;

    private int _frameIndex;
    private float _frameTimer;
    private bool _holding;
    private float _holdTimer;

    private void Awake()
    {
        _spriteRenderer = GetComponent<SpriteRenderer>();
    }

    /// <summary>Starts playing this entry from frame 0. An entry with no frames (the pending
    /// placeholder) stops and hides playback instead — see <see cref="Stop"/>.</summary>
    public void Play(AnimationVersionEntry entry)
    {
        if (entry.frames == null || entry.frames.Length == 0)
        {
            Stop();
            return;
        }

        _frames = entry.frames;
        _fps = Mathf.Max(1, entry.fps);
        _loop = entry.loop;
        _frameIndex = 0;
        _frameTimer = 0f;
        _holding = false;
        _spriteRenderer.enabled = true;
        _spriteRenderer.sprite = _frames[0];
    }

    /// <summary>Stops playback and hides the sprite — used for the pending placeholder, which has
    /// no frames to show.</summary>
    public void Stop()
    {
        _frames = null;
        _spriteRenderer.enabled = false;
    }

    private void Update()
    {
        if (_frames == null || _frames.Length == 0) return;

        if (_holding)
        {
            _holdTimer -= Time.deltaTime;
            if (_holdTimer <= 0f)
            {
                _holding = false;
                _frameIndex = 0;
                _frameTimer = 0f;
                _spriteRenderer.sprite = _frames[0];
            }
            return;
        }

        _frameTimer += Time.deltaTime;
        float frameSeconds = 1f / _fps;
        while (_frameTimer >= frameSeconds)
        {
            _frameTimer -= frameSeconds;
            AdvanceFrame();
            if (_holding) break; // a one-shot just finished — stop consuming this frame's time as more frame ticks.
        }
    }

    private void AdvanceFrame()
    {
        _frameIndex++;
        if (_frameIndex < _frames.Length)
        {
            _spriteRenderer.sprite = _frames[_frameIndex];
            return;
        }

        if (_loop)
        {
            _frameIndex = 0;
            _spriteRenderer.sprite = _frames[0];
        }
        else
        {
            _frameIndex = _frames.Length - 1;
            _holding = true;
            _holdTimer = holdSeconds;
            // Last frame is already showing — nothing new to assign.
        }
    }
}
