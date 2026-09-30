using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Proves armour swapping on a RigCharacter: toggles the torso slot between its default sprite
/// and the gold variant — press G in Play mode, or use the inspector's context menu. RigIdle
/// keeps driving the torso's transform throughout; only the sprite shown in the slot changes, so
/// the idle must keep running unchanged across the swap. Uses the Input System package (not the
/// legacy Input class) since this project's Active Input Handling is set to it exclusively.
/// </summary>
[RequireComponent(typeof(RigCharacter))]
public class ArmorSwap : MonoBehaviour
{
    [SerializeField] private Sprite torsoSprite;
    [SerializeField] private Sprite torsoGoldSprite;

    private RigCharacter _rig;
    private bool _goldEquipped;

    private void Awake()
    {
        _rig = GetComponent<RigCharacter>();
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.gKey.wasPressedThisFrame) Toggle();
    }

    [ContextMenu("Toggle Gold Torso")]
    public void Toggle()
    {
        // RigCharacter only builds its slot hierarchy in Awake(), which never runs outside Play
        // mode — the context menu entry would otherwise null-ref on _rig in Edit mode.
        if (!Application.isPlaying)
        {
            Debug.LogWarning("ArmorSwap: Toggle Gold Torso only works in Play mode — the rig's slots don't exist until then.");
            return;
        }
        if (_rig == null) _rig = GetComponent<RigCharacter>();
        if (torsoSprite == null || torsoGoldSprite == null) return;
        _goldEquipped = !_goldEquipped;
        _rig.SetSlot("torso", _goldEquipped ? torsoGoldSprite : torsoSprite);
    }
}
