using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One bone in the shared skeleton: its parent (-1 for the root), its rest-pose local
/// position/rotation (relative to the parent bone's own local space — exactly like
/// Transform.localPosition/localRotation — computed once, in the editor, from
/// Assets/Characters/knight_skin/skin.json's "bones" head/tail joints; see
/// Assets/Editor/SkinRigImporter.cs), and its rest length (cosmetic only, in world units).
/// </summary>
[Serializable]
public struct SkinBoneDef
{
    public string name;
    public int parentIndex;
    public Vector2 localPosition;
    public float localRotationDeg;
    public float length;
    /// <summary>This bone's head joint in skin.json's own canvas px (origin top-left, y down) —
    /// editor-only data, carried through only so SkinRigImporter can place a root bone within
    /// each skinned sprite's own rect-relative pixel space without inverting the canvas-to-world
    /// conversion; never read at runtime.</summary>
    public Vector2 canvasHead;
}

/// <summary>One skinned mesh layer (body, arm_sword, arm_shield): the sprite baked with its mesh
/// and bind pose by SkinRigImporter, and the fixed local position its SpriteRenderer sits at
/// forever — see SkinBoneCharacter, which never moves this transform; all deformation comes from
/// the shared bone transforms a SpriteSkin reads.</summary>
[Serializable]
public struct SkinLayerDef
{
    public string name;
    public Sprite sprite;
    public Vector2 localPosition;
    public int sortingOrder;
}

/// <summary>One rigid, non-skinned sprite that rides a single bone (head, plume, sword, shield).
/// altSprite is the head's blink swap (head_blink) — null for every other prop.</summary>
[Serializable]
public struct SkinPropDef
{
    public string slot;
    public Sprite sprite;
    public Sprite altSprite;
    public int boneIndex;
    public int sortingOrder;
}

/// <summary>One leg's 2D IK chain: the three bone indices from root to effector (thigh, shin,
/// boot) and which way LimbSolver2D should bend the knee.</summary>
[Serializable]
public struct SkinLegDef
{
    public string side;
    public int thighBoneIndex;
    public int shinBoneIndex;
    public int bootBoneIndex;
    public bool flip;
}

/// <summary>
/// The knight's 2D bone-skinned rig — one shared skeleton, three skinned mesh layers, five rigid
/// props, and two IK legs. Built by Characters/Import Knight Skin Rig
/// (Assets/Editor/SkinRigImporter.cs) from Assets/Characters/knight_skin/skin.json — never
/// hand-authored. Consumed by SkinBoneCharacter, which builds the transform hierarchy from it at
/// runtime.
/// </summary>
public class SkinRigDefinition : ScriptableObject
{
    [SerializeField] private List<SkinBoneDef> bones = new List<SkinBoneDef>();
    [SerializeField] private List<SkinLayerDef> skinnedLayers = new List<SkinLayerDef>();
    [SerializeField] private List<SkinPropDef> props = new List<SkinPropDef>();
    [SerializeField] private List<SkinLegDef> legs = new List<SkinLegDef>();

    public IReadOnlyList<SkinBoneDef> Bones => bones;
    public IReadOnlyList<SkinLayerDef> SkinnedLayers => skinnedLayers;
    public IReadOnlyList<SkinPropDef> Props => props;
    public IReadOnlyList<SkinLegDef> Legs => legs;

#if UNITY_EDITOR
    public void EditorInit(
        List<SkinBoneDef> bones, List<SkinLayerDef> skinnedLayers, List<SkinPropDef> props, List<SkinLegDef> legs)
    {
        this.bones = bones;
        this.skinnedLayers = skinnedLayers;
        this.props = props;
        this.legs = legs;
    }
#endif
}
