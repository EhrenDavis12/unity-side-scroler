using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds a cutout/skeletal character from a RigDefinition: one child GameObject + SpriteRenderer
/// per slot, parented and positioned per the rig's rest pose, sorted back-to-front. Slots are
/// exposed by name (see <see cref="Slot"/> / <see cref="SetSlot"/>) so other components — the
/// idle loop, an armour-swap toggle — can drive or restyle them without knowing the hierarchy.
/// No sprite sheets and no Animator clips are involved; everything here is a plain transform.
/// </summary>
public class RigCharacter : MonoBehaviour
{
    [SerializeField] private RigDefinition rig;

    private readonly Dictionary<string, Transform> _slotTransforms = new Dictionary<string, Transform>();
    private readonly Dictionary<string, SpriteRenderer> _slotRenderers = new Dictionary<string, SpriteRenderer>();
    private readonly Dictionary<string, Transform> _slotVisuals = new Dictionary<string, Transform>();
    private readonly Dictionary<string, Vector2> _slotRestLocalPositions = new Dictionary<string, Vector2>();
    private bool _usingSwingOrder;

    private void Awake()
    {
        if (rig != null) Build(rig);
    }

    /// <summary>Creates the slot hierarchy from a rig definition. Two passes: every slot's
    /// GameObject is created first (so a part may appear before its parent in the list), then
    /// each is reparented and placed at its rest-pose local position.</summary>
    public void Build(RigDefinition definition)
    {
        rig = definition;
        _slotTransforms.Clear();
        _slotRenderers.Clear();
        _slotVisuals.Clear();
        _slotRestLocalPositions.Clear();

        foreach (RigSlotDefinition slot in rig.Slots)
        {
            var go = new GameObject(slot.slot);
            go.transform.SetParent(transform, false);

            // The SpriteRenderer lives on its own child rather than directly on the slot's own
            // transform: anything parented under this slot rides its position and rotation, but a
            // purely visual scale applied to this slot alone (e.g. RigIdle's torso squash) never
            // propagates onto them.
            var visual = new GameObject(slot.slot + "_sprite");
            visual.transform.SetParent(go.transform, false);
            var renderer = visual.AddComponent<SpriteRenderer>();
            renderer.sprite = slot.sprite;
            renderer.sortingOrder = slot.sortingOrder;

            _slotTransforms[slot.slot] = go.transform;
            _slotRenderers[slot.slot] = renderer;
            _slotVisuals[slot.slot] = visual.transform;
            _slotRestLocalPositions[slot.slot] = slot.localPosition;
        }

        foreach (RigSlotDefinition slot in rig.Slots)
        {
            Transform slotTransform = _slotTransforms[slot.slot];
            if (!string.IsNullOrEmpty(slot.parentSlot) && _slotTransforms.TryGetValue(slot.parentSlot, out Transform parent))
            {
                slotTransform.SetParent(parent, false);
            }
            slotTransform.localPosition = slot.localPosition;
        }
    }

    /// <summary>The named slot's transform, or null if the rig has no such slot. Anything
    /// parented under this slot rides this transform's position and rotation.</summary>
    public Transform Slot(string slotName) => _slotTransforms.TryGetValue(slotName, out Transform t) ? t : null;

    /// <summary>The named slot's own visual (sprite-only) transform — the one to scale for a
    /// purely cosmetic effect like a squash, since nothing is ever parented under it.</summary>
    public Transform SlotVisual(string slotName) => _slotVisuals.TryGetValue(slotName, out Transform t) ? t : null;

    /// <summary>The named slot's authored rest-pose local position (from rig.json, via
    /// RigDefinition) — never the live value, so it stays correct even after something has moved
    /// the slot's actual transform away from rest.</summary>
    public Vector2 RestLocalPosition(string slotName) =>
        _slotRestLocalPositions.TryGetValue(slotName, out Vector2 v) ? v : Vector2.zero;

    /// <summary>Swaps the sprite shown in a slot (e.g. an armour variant) without touching its
    /// transform — the joint, and anything already riding it, is unaffected.</summary>
    public void SetSlot(string slotName, Sprite sprite)
    {
        if (_slotRenderers.TryGetValue(slotName, out SpriteRenderer renderer)) renderer.sprite = sprite;
    }

    /// <summary>Switches every slot's draw order between rig.json's normal "order" and its
    /// "swing_order" (the two are identical, slot for slot, when rig.json has no "swing_order" —
    /// so this is a no-op then). Driven by whatever names the window an attack needs an
    /// alternate draw order for, e.g. the sword arm crossing in front of the shield.</summary>
    public void SetSwingOrderActive(bool active)
    {
        if (active == _usingSwingOrder) return;
        _usingSwingOrder = active;
        foreach (RigSlotDefinition slot in rig.Slots)
        {
            if (_slotRenderers.TryGetValue(slot.slot, out SpriteRenderer renderer))
            {
                renderer.sortingOrder = active ? slot.swingSortingOrder : slot.sortingOrder;
            }
        }
    }
}
