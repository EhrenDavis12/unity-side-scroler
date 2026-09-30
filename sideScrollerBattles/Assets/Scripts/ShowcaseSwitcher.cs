using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Toggles between the two knight showcases in KnightShowcases.unity: press Tab, or the on-screen
/// button, to swap which root GameObject is active. Only one is ever active at a time, so only
/// one root's SkinAttack (or its 3D equivalent) ever sees key A — a disabled GameObject's
/// MonoBehaviours simply stop ticking, so no extra gating is needed here.
/// </summary>
public class ShowcaseSwitcher : MonoBehaviour
{
    [SerializeField] private GameObject showcaseA;
    [SerializeField] private GameObject showcaseB;
    [SerializeField] private Text label;

    private bool _showingA = true;

    private void Start()
    {
        Apply();
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame) Toggle();
    }

    public void Toggle()
    {
        _showingA = !_showingA;
        Apply();
    }

    private void Apply()
    {
        if (showcaseA != null) showcaseA.SetActive(_showingA);
        if (showcaseB != null) showcaseB.SetActive(!_showingA);
        if (label != null) label.text = _showingA ? "A: 2D Bones (Tab to switch)" : "B: 3D in 2D (Tab to switch)";
    }
}
