using UnityEngine;

/// <summary>
/// Drives the world-space sprite shown in the top half of the Showcase screen. Swapping in a
/// character shows its idle loop immediately; playing an action restarts it from frame 0 even
/// if the same action is already mid-play, and the character's own Animator controller (built
/// by the importer) transitions back to idle on its own once the action finishes.
/// </summary>
[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(SpriteRenderer))]
public class ShowcaseCharacter : MonoBehaviour
{
    private const string IdleState = "idle";

    [SerializeField] private Animator animator;
    [SerializeField] private SpriteRenderer spriteRenderer;

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
    }

    /// <summary>
    /// Plays one non-idle action from frame 0. Calling this again for the same or a
    /// different action restarts from frame 0, since Animator.Play always re-enters the
    /// target state at normalizedTime 0.
    /// </summary>
    public void PlayAction(string actionName)
    {
        if (string.IsNullOrEmpty(actionName)) return;
        animator.Play(actionName, 0, 0f);
    }
}
