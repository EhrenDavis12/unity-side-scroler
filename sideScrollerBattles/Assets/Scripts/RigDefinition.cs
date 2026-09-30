using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One rig slot: which slot it parents under (empty for a slot that hangs directly off the rig
/// root), the sprite it starts with, its rest-pose local position relative to that parent, its
/// back-to-front sorting order, and its sorting order for the brief window an attack needs a
/// different draw order (e.g. the sword arm drawing over the shield mid-swing) — see rig.json's
/// "swing_order" and RigCharacter.SetSwingOrderActive. Positions are computed once, in the
/// editor, from Assets/Characters/knight_rig/rig.json's per-part joints — see
/// Assets/Editor/RigCharacterImporter.cs — so nothing at runtime needs to parse that JSON.
/// </summary>
[Serializable]
public struct RigSlotDefinition
{
    public string slot;
    public string parentSlot; // empty/null => direct child of the RigCharacter's own transform
    public Sprite sprite;
    public Vector2 localPosition;
    public int sortingOrder;
    public int swingSortingOrder; // same as sortingOrder when rig.json has no "swing_order"
}

/// <summary>
/// A cutout/skeletal character rig — one slot per body part, in draw order. Built by
/// Characters/Import Knight Rig (Assets/Editor/RigCharacterImporter.cs) from
/// Assets/Characters/knight_rig/rig.json — never hand-authored. Consumed by RigCharacter, which
/// builds the transform hierarchy from it at runtime.
/// </summary>
public class RigDefinition : ScriptableObject
{
    [SerializeField] private List<RigSlotDefinition> slots = new List<RigSlotDefinition>();

    public IReadOnlyList<RigSlotDefinition> Slots => slots;

#if UNITY_EDITOR
    public void EditorInit(List<RigSlotDefinition> slots)
    {
        this.slots = slots;
    }
#endif
}
