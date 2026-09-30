using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Plays the 3D knight's attack on key A, same trigger key as SkinAttack uses for the 2D
/// showcase — only one showcase is ever active at a time (see ShowcaseSwitcher), so only one of
/// the two ever sees the key press.</summary>
[RequireComponent(typeof(Animator))]
public class KnightAttackTrigger : MonoBehaviour
{
    private Animator _animator;

    private void Awake() => _animator = GetComponent<Animator>();

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.aKey.wasPressedThisFrame)
        {
            _animator.SetTrigger("Attack");
        }
    }
}
