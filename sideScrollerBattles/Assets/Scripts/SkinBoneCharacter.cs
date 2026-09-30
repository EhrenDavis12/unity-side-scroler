using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D.Animation;
using UnityEngine.U2D.IK;

/// <summary>
/// Builds the knight's 2D-bone-skinned rig from a SkinRigDefinition: one shared Transform per
/// bone (parented and positioned at its rest pose), a SpriteRenderer + SpriteSkin per skinned
/// mesh layer (body, arm_sword, arm_shield — all three sharing the SAME bone Transform array, so
/// posing one bone deforms every layer it influences), a rigid SpriteRenderer per prop parented
/// directly under the single bone it rides, and a two-bone 2D IK chain per leg so the feet stay
/// planted under the hips (see SkinRigImporter for how the shared skeleton and each layer's own
/// mesh/bind pose were computed at import).
///
/// A prop's local rotation is set to the inverse of its own bone's REST world rotation, so its
/// axis-aligned art (drawn once, upright, in the source canvas) renders upright at rest despite
/// its bone's own rest tilt, while still riding every rotation the bone picks up afterward (idle,
/// attack) as a delta from that rest tilt — the same "rotate on top of rest" contract SkinIdle and
/// SkinAttack use for the bones themselves.
/// </summary>
public class SkinBoneCharacter : MonoBehaviour
{
    [SerializeField] private SkinRigDefinition rig;

    private Transform[] _boneTransforms;
    private readonly Dictionary<string, int> _boneIndexByName = new Dictionary<string, int>();
    private SpriteRenderer _headRenderer;
    private Sprite _headSprite, _headBlinkSprite;

    private void Awake()
    {
        if (rig != null) Build(rig);
    }

    public void Build(SkinRigDefinition definition)
    {
        rig = definition;
        _boneIndexByName.Clear();
        _headRenderer = null;

        // Bones are already listed parent-before-child (see SkinRigImporter.BuildSkeleton), so a
        // single forward pass can parent each one as it's created.
        IReadOnlyList<SkinBoneDef> bones = rig.Bones;
        _boneTransforms = new Transform[bones.Count];
        for (int i = 0; i < bones.Count; i++)
        {
            SkinBoneDef bone = bones[i];
            var go = new GameObject(bone.name);
            Transform parent = bone.parentIndex >= 0 ? _boneTransforms[bone.parentIndex] : transform;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(bone.localPosition.x, bone.localPosition.y, 0f);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, bone.localRotationDeg);
            _boneTransforms[i] = go.transform;
            _boneIndexByName[bone.name] = i;
        }

        foreach (SkinLayerDef layer in rig.SkinnedLayers)
        {
            var go = new GameObject(layer.name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(layer.localPosition.x, layer.localPosition.y, 0f);

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = layer.sprite;
            renderer.sortingOrder = layer.sortingOrder;

            var skin = go.AddComponent<SpriteSkin>();
            skin.SetBoneTransforms(_boneTransforms);
            skin.SetRootBone(FindRootBone());
        }

        foreach (SkinPropDef prop in rig.Props)
        {
            Transform boneTransform = _boneTransforms[prop.boneIndex];
            var go = new GameObject(prop.slot);
            go.transform.SetParent(boneTransform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.Inverse(boneTransform.rotation);

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = prop.sprite;
            renderer.sortingOrder = prop.sortingOrder;

            if (prop.slot == "head")
            {
                _headRenderer = renderer;
                _headSprite = prop.sprite;
                _headBlinkSprite = prop.altSprite;
            }
        }

        BuildLegIk();
    }

    /// <summary>Two-bone 2D IK per leg (thigh → shin → boot), targeting a Transform fixed at that
    /// leg's own rest world position/rotation — read directly off the just-built hierarchy, never
    /// recomputed — so the boot never moves from its rest spot (and never rotates away from its
    /// rest angle, since Solver2D.constrainRotation defaults to true) no matter how the hips
    /// above it move.</summary>
    private void BuildLegIk()
    {
        var ikManager = gameObject.AddComponent<IKManager2D>();
        foreach (SkinLegDef leg in rig.Legs)
        {
            Transform boot = _boneTransforms[leg.bootBoneIndex];

            var targetGo = new GameObject($"{leg.side}_leg_target");
            targetGo.transform.SetParent(transform, false);
            targetGo.transform.position = boot.position;
            targetGo.transform.rotation = boot.rotation;

            var solverGo = new GameObject($"{leg.side}_leg_ik");
            solverGo.transform.SetParent(transform, false);
            var solver = solverGo.AddComponent<LimbSolver2D>();
            solver.flip = leg.flip;
            IKChain2D chain = solver.GetChain(0);
            chain.effector = boot;
            chain.target = targetGo.transform;

            ikManager.AddSolver(solver);
        }
    }

    private Transform FindRootBone()
    {
        IReadOnlyList<SkinBoneDef> bones = rig.Bones;
        for (int i = 0; i < bones.Count; i++)
        {
            if (bones[i].parentIndex < 0) return _boneTransforms[i];
        }
        return null;
    }

    /// <summary>The named bone's transform, or null if the rig has no such bone.</summary>
    public Transform Bone(string boneName) => _boneIndexByName.TryGetValue(boneName, out int i) ? _boneTransforms[i] : null;

    /// <summary>The named bone's authored rest-pose local position/rotation (from skin.json, via
    /// SkinRigDefinition) — never the live value, so callers can always add a delta on top of rest
    /// even after something else has moved the bone away from it.</summary>
    public Vector2 RestLocalPosition(string boneName) => rig.Bones[_boneIndexByName[boneName]].localPosition;
    public float RestLocalRotationDeg(string boneName) => rig.Bones[_boneIndexByName[boneName]].localRotationDeg;

    /// <summary>Swaps the head prop's sprite between its normal and blink art, without touching
    /// its transform.</summary>
    public void SetHeadBlinking(bool blinking)
    {
        if (_headRenderer == null) return;
        _headRenderer.sprite = blinking ? _headBlinkSprite : _headSprite;
    }
}
