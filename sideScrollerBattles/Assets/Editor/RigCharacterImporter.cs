#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds the knight rig's RigDefinition asset from Assets/Characters/knight_rig/rig.json: one
/// slot per entry in rig.json's "order" list, its sprite loaded from the already-imported PNG
/// (see KnightRigTexturePostprocessor for the per-sprite pivot and other import settings), its
/// rest-pose local position derived from the joint delta between it and its parent slot (or the
/// rig's root_pivot for a parent-less slot), its sorting order from its position in "order", and
/// its alternate sorting order from its position in the optional "swing_order" (falling back to
/// the same value as "order" when rig.json has none at all — no rig.json changes are required for
/// a rig with nothing that ever needs to redraw — but erroring if it exists and simply omits a
/// slot "order" names, rather than silently falling back to that slot's normal position).
/// Re-running with unchanged input overwrites the same asset in place.
/// </summary>
public static class RigCharacterImporter
{
    private const string RigRoot = "Assets/Characters/knight_rig";
    private const string RigJsonPath = RigRoot + "/rig.json";
    private const string DefinitionAssetPath = RigRoot + "/KnightRig.asset";
    private const float PixelsPerUnit = 256f;

    [MenuItem("Characters/Import Knight Rig")]
    public static void ImportRig()
    {
        if (!File.Exists(RigJsonPath))
        {
            Debug.LogError($"RigCharacterImporter: no rig.json at {RigJsonPath} — aborting.");
            return;
        }

        var root = JObject.Parse(File.ReadAllText(RigJsonPath));
        float[] rootPivot = ((JArray)root["root_pivot"]).Select(v => (float)v).ToArray();
        List<string> order = ((JArray)root["order"]).Select(v => (string)v).ToList();
        List<string> swingOrder = root["swing_order"] is JArray swingArray
            ? swingArray.Select(v => (string)v).ToList()
            : null;
        var parts = (JObject)root["parts"];

        var slots = new List<RigSlotDefinition>();
        for (int i = 0; i < order.Count; i++)
        {
            string slotName = order[i];
            if (!(parts[slotName] is JObject partJson))
            {
                Debug.LogError(
                    $"RigCharacterImporter: rig.json's \"order\" names slot \"{slotName}\", which has no entry " +
                    "in \"parts\" — aborting, no assets touched.");
                return;
            }
            string file = (string)partJson["file"];
            float[] joint = ((JArray)partJson["joint"]).Select(v => (float)v).ToArray();
            string parentSlot = (string)partJson["parent"];

            float parentJointX, parentJointY;
            if (string.IsNullOrEmpty(parentSlot))
            {
                parentJointX = rootPivot[0];
                parentJointY = rootPivot[1];
            }
            else if (parts[parentSlot] is JObject parentPartJson)
            {
                float[] parentJoint = ((JArray)parentPartJson["joint"]).Select(v => (float)v).ToArray();
                parentJointX = parentJoint[0];
                parentJointY = parentJoint[1];
            }
            else
            {
                Debug.LogError(
                    $"RigCharacterImporter: slot \"{slotName}\" declares parent \"{parentSlot}\", which has no " +
                    "entry in \"parts\" — aborting, no assets touched.");
                return;
            }

            if (!string.IsNullOrEmpty(parentSlot) && !order.Contains(parentSlot))
            {
                // A parent that exists in "parts" but never appears in "order" is never built as
                // a slot (RigCharacter only builds what's in rig.Slots, i.e. "order"), so it would
                // silently fail to find its parent transform and stay attached to the rig root.
                Debug.LogError(
                    $"RigCharacterImporter: slot \"{slotName}\" declares parent \"{parentSlot}\", which is not in " +
                    "\"order\" — aborting, no assets touched.");
                return;
            }

            // Canvas y runs down (origin top-left); world y runs up, so the y delta flips sign.
            var localPosition = new Vector2(
                (joint[0] - parentJointX) / PixelsPerUnit,
                -(joint[1] - parentJointY) / PixelsPerUnit);

            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{RigRoot}/{file}");
            if (sprite == null)
            {
                Debug.LogError(
                    $"RigCharacterImporter: no sprite imported at {RigRoot}/{file} for slot \"{slotName}\" — aborting, no assets touched.");
                return;
            }

            int swingSortingOrder;
            if (swingOrder == null)
            {
                // No "swing_order" at all: every slot falls back to its normal order, and that's
                // a legitimate rig with nothing that ever needs an alternate draw order.
                swingSortingOrder = i;
            }
            else
            {
                int swingIndex = swingOrder.IndexOf(slotName);
                if (swingIndex < 0)
                {
                    Debug.LogError(
                        $"RigCharacterImporter: rig.json has a \"swing_order\" but it's missing slot \"{slotName}\" " +
                        "— aborting, no assets touched.");
                    return;
                }
                swingSortingOrder = swingIndex;
            }

            slots.Add(new RigSlotDefinition
            {
                slot = slotName,
                parentSlot = parentSlot,
                sprite = sprite,
                localPosition = localPosition,
                sortingOrder = i,
                swingSortingOrder = swingSortingOrder,
            });
        }

        RigDefinition definition = AssetDatabase.LoadAssetAtPath<RigDefinition>(DefinitionAssetPath);
        if (definition == null)
        {
            definition = ScriptableObject.CreateInstance<RigDefinition>();
            AssetDatabase.CreateAsset(definition, DefinitionAssetPath);
        }
        definition.EditorInit(slots);
        EditorUtility.SetDirty(definition);

        // SaveAssets() alone persists this — no AssetDatabase.Refresh(): CreateAsset already
        // registers a brand-new asset immediately, and this also runs from inside
        // KnightRigTexturePostprocessor.OnPostprocessAllAssets, where a full Refresh() would
        // re-enter an import batch that is still in progress.
        AssetDatabase.SaveAssets();
        Debug.Log($"Characters/Import Knight Rig: built {slots.Count} slot(s) at {DefinitionAssetPath}.");
    }
}
#endif
