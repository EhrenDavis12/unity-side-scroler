using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class BuildKnightAvatar
{
    private const string GlbPath = "Assets/Characters/knight_3d/knight_rigged.glb";
    private const string AvatarAssetPath = "Assets/Characters/knight_3d/KnightAvatar.asset";

    // Standard Mixamo -> Humanoid bone name map. Fingers included since the source rig has them.
    private static readonly (string human, string mixamo)[] Map =
    {
        ("Hips", "mixamorig:Hips"),
        ("Spine", "mixamorig:Spine"),
        ("Chest", "mixamorig:Spine1"),
        ("UpperChest", "mixamorig:Spine2"),
        ("Neck", "mixamorig:Neck"),
        ("Head", "mixamorig:Head"),
        ("LeftShoulder", "mixamorig:LeftShoulder"),
        ("LeftUpperArm", "mixamorig:LeftArm"),
        ("LeftLowerArm", "mixamorig:LeftForeArm"),
        ("LeftHand", "mixamorig:LeftHand"),
        ("RightShoulder", "mixamorig:RightShoulder"),
        ("RightUpperArm", "mixamorig:RightArm"),
        ("RightLowerArm", "mixamorig:RightForeArm"),
        ("RightHand", "mixamorig:RightHand"),
        ("LeftUpperLeg", "mixamorig:LeftUpLeg"),
        ("LeftLowerLeg", "mixamorig:LeftLeg"),
        ("LeftFoot", "mixamorig:LeftFoot"),
        ("LeftToes", "mixamorig:LeftToeBase"),
        ("RightUpperLeg", "mixamorig:RightUpLeg"),
        ("RightLowerLeg", "mixamorig:RightLeg"),
        ("RightFoot", "mixamorig:RightFoot"),
        ("RightToes", "mixamorig:RightToeBase"),
        ("Left Thumb Proximal", "mixamorig:LeftHandThumb1"),
        ("Left Thumb Intermediate", "mixamorig:LeftHandThumb2"),
        ("Left Thumb Distal", "mixamorig:LeftHandThumb3"),
        ("Left Index Proximal", "mixamorig:LeftHandIndex1"),
        ("Left Index Intermediate", "mixamorig:LeftHandIndex2"),
        ("Left Index Distal", "mixamorig:LeftHandIndex3"),
        ("Left Middle Proximal", "mixamorig:LeftHandMiddle1"),
        ("Left Middle Intermediate", "mixamorig:LeftHandMiddle2"),
        ("Left Middle Distal", "mixamorig:LeftHandMiddle3"),
        ("Left Ring Proximal", "mixamorig:LeftHandRing1"),
        ("Left Ring Intermediate", "mixamorig:LeftHandRing2"),
        ("Left Ring Distal", "mixamorig:LeftHandRing3"),
        ("Left Little Proximal", "mixamorig:LeftHandPinky1"),
        ("Left Little Intermediate", "mixamorig:LeftHandPinky2"),
        ("Left Little Distal", "mixamorig:LeftHandPinky3"),
        ("Right Thumb Proximal", "mixamorig:RightHandThumb1"),
        ("Right Thumb Intermediate", "mixamorig:RightHandThumb2"),
        ("Right Thumb Distal", "mixamorig:RightHandThumb3"),
        ("Right Index Proximal", "mixamorig:RightHandIndex1"),
        ("Right Index Intermediate", "mixamorig:RightHandIndex2"),
        ("Right Index Distal", "mixamorig:RightHandIndex3"),
        ("Right Middle Proximal", "mixamorig:RightHandMiddle1"),
        ("Right Middle Intermediate", "mixamorig:RightHandMiddle2"),
        ("Right Middle Distal", "mixamorig:RightHandMiddle3"),
        ("Right Ring Proximal", "mixamorig:RightHandRing1"),
        ("Right Ring Intermediate", "mixamorig:RightHandRing2"),
        ("Right Ring Distal", "mixamorig:RightHandRing3"),
        ("Right Little Proximal", "mixamorig:RightHandPinky1"),
        ("Right Little Intermediate", "mixamorig:RightHandPinky2"),
        ("Right Little Distal", "mixamorig:RightHandPinky3"),
    };

    public static string Run()
    {
        var prefabRoot = AssetDatabase.LoadMainAssetAtPath(GlbPath) as GameObject;
        if (prefabRoot == null) return "FAILED: no root GameObject at " + GlbPath;

        GameObject instance = Object.Instantiate(prefabRoot);
        try
        {
            var human = new List<HumanBone>();
            foreach (var (humanName, mixamoName) in Map)
            {
                Transform t = FindDeep(instance.transform, mixamoName);
                if (t == null) return $"FAILED: bone {mixamoName} not found in glb hierarchy";
                human.Add(new HumanBone
                {
                    humanName = humanName,
                    boneName = mixamoName,
                    limit = new HumanLimit { useDefaultValues = true },
                });
            }

            var skeleton = new List<SkeletonBone>();
            CollectSkeleton(instance.transform, skeleton);

            var description = new HumanDescription
            {
                human = human.ToArray(),
                skeleton = skeleton.ToArray(),
                upperArmTwist = 0.5f,
                lowerArmTwist = 0.5f,
                upperLegTwist = 0.5f,
                lowerLegTwist = 0.5f,
                armStretch = 0.05f,
                legStretch = 0.05f,
                feetSpacing = 0f,
                hasTranslationDoF = false,
            };

            Avatar avatar = AvatarBuilder.BuildHumanAvatar(instance, description);
            if (avatar == null) return "FAILED: BuildHumanAvatar returned null";
            avatar.name = "KnightAvatar";
            if (!avatar.isValid) return "FAILED: avatar built but isValid=false";
            if (!avatar.isHuman) return "FAILED: avatar built but isHuman=false";

            AssetDatabase.CreateAsset(avatar, AvatarAssetPath);
            AssetDatabase.SaveAssets();
            return $"OK: avatar valid={avatar.isValid} human={avatar.isHuman} saved to {AvatarAssetPath}";
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform child in root)
        {
            Transform found = FindDeep(child, name);
            if (found != null) return found;
        }
        return null;
    }

    private static void CollectSkeleton(Transform t, List<SkeletonBone> list)
    {
        list.Add(new SkeletonBone
        {
            name = t.name,
            position = t.localPosition,
            rotation = t.localRotation,
            scale = t.localScale,
        });
        foreach (Transform child in t) CollectSkeleton(child, list);
    }
}
