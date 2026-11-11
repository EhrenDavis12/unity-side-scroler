using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Drives the world-space sprite shown in the top half of the Showcase screen. Swapping in a
/// character shows its idle loop immediately; playing an action restarts it from frame 0 even
/// if the same action is already mid-play, and the character's own Animator controller (built
/// by the importer) transitions back to idle on its own once the action finishes.
///
/// While idle, also plays the character's "fidget_" one-shots at random — see
/// <see cref="Update"/> — and scales idle-only playback speed via <see cref="idleSpeed"/>.
/// </summary>
[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(SpriteRenderer))]
public class ShowcaseCharacter : MonoBehaviour
{
    private const string IdleState = "idle";

    [SerializeField] private Animator animator;
    [SerializeField] private SpriteRenderer spriteRenderer;

    [Header("Idle fidgets")]
    [Tooltip("Random delay, in seconds, before the next fidget one-shot plays while idle.")]
    [SerializeField] private float fidgetIntervalMinSeconds = 5f;
    [SerializeField] private float fidgetIntervalMaxSeconds = 15f;

    [Header("Idle speed")]
    [Tooltip("Playback speed of the idle loop only — actions and fidgets always play at 1x.")]
    [SerializeField, Range(0.25f, 1.5f)] private float idleSpeed = 1f;

    // The current character's fidget state names (may be empty), and the last one played, so
    // the next pick never repeats it while there are 2 or more to choose from.
    private List<string> _fidgetNames = new List<string>();
    private string _lastFidget;
    private float _fidgetTimer;

    // Animator.Play() doesn't take effect until the Animator's own evaluation later in the
    // frame, so the Update() right after PlayAction() would still read the old "idle" state and
    // apply idleSpeed to the action's first step. Set for exactly one Update() by PlayAction();
    // mirrors the same-frame "inIdle = false" the fidget path applies to itself in Update().
    private bool _pendingNonIdle;

    private void Reset()
    {
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    private void Awake()
    {
        if (animator == null) animator = GetComponent<Animator>();
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
    }

    /// <summary>Swaps in a character and starts its idle loop.</summary>
    public void Show(CharacterDefinition character)
    {
        if (character == null) return;
        animator.runtimeAnimatorController = character.Controller;
        animator.Play(IdleState, 0, 0f);
        _fidgetNames = character.FidgetNames.ToList();
        _lastFidget = null;
        ResetFidgetTimer();
    }

    /// <summary>
    /// Plays one non-idle action from frame 0. Calling this again for the same or a
    /// different action restarts from frame 0, since Animator.Play always re-enters the
    /// target state at normalizedTime 0. Also resets the idle fidget timer, since a user
    /// action starting is not the same as idle continuing.
    /// </summary>
    public void PlayAction(string actionName)
    {
        if (string.IsNullOrEmpty(actionName)) return;
        animator.Play(actionName, 0, 0f);
        _pendingNonIdle = true;
        ResetFidgetTimer();
    }

    /// <summary>
    /// Picks a random fidget one-shot every <see cref="fidgetIntervalMinSeconds"/>–
    /// <see cref="fidgetIntervalMaxSeconds"/> while (and only while) the Animator is actually
    /// in the idle state — never mid-action or mid-fidget, and never at all when the character
    /// has no "fidget_" animations. <see cref="idleSpeed"/> scales playback only for that same
    /// idle window, by toggling <c>animator.speed</c>: actions and fidgets always run at 1x.
    /// </summary>
    private void Update()
    {
        // No controller until Show() assigns one (its Addressable definition may still be
        // loading, or may never resolve) — nothing below is meaningful without one.
        if (animator.runtimeAnimatorController == null) return;

        bool inIdle = !_pendingNonIdle && animator.GetCurrentAnimatorStateInfo(0).IsName(IdleState);
        _pendingNonIdle = false;

        if (inIdle && _fidgetNames.Count > 0)
        {
            _fidgetTimer -= Time.deltaTime;
            if (_fidgetTimer <= 0f)
            {
                PlayRandomFidget();
                inIdle = false; // left idle this frame — don't apply idleSpeed to it.
            }
        }

        animator.speed = inIdle ? idleSpeed : 1f;
    }

    private void PlayRandomFidget()
    {
        string next = PickFidgetName();
        animator.Play(next, 0, 0f);
        _lastFidget = next;
        ResetFidgetTimer();
    }

    private string PickFidgetName()
    {
        if (_fidgetNames.Count == 1) return _fidgetNames[0];
        List<string> candidates = _fidgetNames.Where(n => n != _lastFidget).ToList();
        return candidates[Random.Range(0, candidates.Count)];
    }

    private void ResetFidgetTimer()
    {
        _fidgetTimer = Random.Range(fidgetIntervalMinSeconds, fidgetIntervalMaxSeconds);
    }
}
