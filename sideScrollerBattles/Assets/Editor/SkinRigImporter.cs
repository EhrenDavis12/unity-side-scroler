#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.U2D;

/// <summary>
/// Builds the knight's 2D-bone-skinned rig from Assets/Characters/knight_skin/skin.json: one
/// shared skeleton (skin.json's "bones", head/tail joints, canvas px, origin top-left, y down),
/// three mesh-skinned layers (body, arm_sword, arm_shield — each gets its vertices/triangles/
/// weights and the full skeleton's bones written into its Sprite via the Sprite Editor data
/// provider APIs) and five rigid props that ride a single bone each (head, head_blink, plume,
/// sword, shield). Writes Assets/Characters/knight_skin/KnightSkinRig.asset for SkinBoneCharacter
/// to build from at runtime. Re-running with unchanged input overwrites the same asset in place.
///
/// Coordinate spaces, confirmed empirically against this Editor's own Sprite Editor data
/// provider (see the probe scripts this importer's math was derived from) rather than assumed
/// from documentation alone:
/// - A sprite's own SpriteBone.position/Vertex2DMetaData.position are stored in that sprite's own
///   cropped-rect PIXEL space, origin at the rect's bottom-left corner, y UP — but ONLY for a
///   bone with no parent. A bone with a parent stores a pure parent-relative offset in that same
///   pixel scale (still divided by pixelsPerUnit at import, exactly like Transform.localPosition),
///   with no further pivot correction — pivot only ever affects the root.
/// - Pivot is set to Custom (0,0) (the rect's own bottom-left) for the three skinned sprites, so
///   "raw pixel, rect-bottom-left origin, y up" already equals "final sprite-local space × PPU"
///   with no extra offset to track.
/// - Every non-root bone's rest local position/rotation (parent-relative, hence translation-
///   invariant) is therefore identical across all three sprites and computed once, in a shared
///   "world" canvas-pixels-to-world-units space (canvas center → world origin, y flipped) that
///   also places this rig's runtime bone Transforms — so the same numbers drive both.
/// </summary>
public static class SkinRigImporter
{
    private const string RigRoot = "Assets/Characters/knight_skin";
    private const string SkinJsonPath = RigRoot + "/skin.json";
    private const string DefinitionAssetPath = RigRoot + "/KnightSkinRig.asset";
    private const float PixelsPerUnit = 256f;

    private static readonly string[] SkinnedLayerOrder = { "body", "arm_sword", "arm_shield" };

    /// <summary>Thrown to unwind out of deeply nested per-vertex/per-bone validation with a
    /// single "no assets touched" message, matching RigCharacterImporter's abort convention.
    /// </summary>
    private class ImportAbortedException : Exception
    {
        public ImportAbortedException(string message) : base(message) { }
    }

    [MenuItem("Characters/Import Knight Skin Rig")]
    public static void ImportRig()
    {
        if (!File.Exists(SkinJsonPath))
        {
            Debug.LogError($"SkinRigImporter: no skin.json at {SkinJsonPath} — aborting.");
            return;
        }

        try
        {
            var root = JObject.Parse(File.ReadAllText(SkinJsonPath));
            var canvasArray = (JArray)root["canvas"];
            float canvasW = (float)canvasArray[0];
            float canvasH = (float)canvasArray[1];

            Vector2 World(float px, float py) =>
                new Vector2((px - canvasW / 2f) / PixelsPerUnit, (canvasH / 2f - py) / PixelsPerUnit);

            List<SkinBoneDef> bones = BuildSkeleton((JObject)root["bones"], World, out Dictionary<string, int> boneIndexByName);

            var skinnedJson = (JObject)root["skinned"];
            var propsJson = (JObject)root["props"];
            var drawOrder = ((JArray)root["draw_order"]).Select(v => (string)v).ToList();

            // ---- Base texture import settings for every PNG this rig touches, applied first so
            // every sprite exists before the skinned layers' bones/mesh are written below. ----
            foreach (string layerName in SkinnedLayerOrder)
            {
                var layerJson = (JObject)skinnedJson[layerName];
                string file = (string)layerJson["file"];
                ApplyBaseImportSettings($"{RigRoot}/{file}", Vector2.zero, SpriteMeshType.FullRect);
            }

            var propPivots = new Dictionary<string, Vector2>();
            foreach (JProperty propProperty in propsJson.Properties())
            {
                var propJson = (JObject)propProperty.Value;
                string file = (string)propJson["file"];
                var rect = ((JArray)propJson["rect"]).Select(v => (float)v).ToArray();
                float rx = rect[0], ry = rect[1], rw = rect[2], rh = rect[3];
                string boneName = (string)propJson["bone"];
                if (!boneIndexByName.TryGetValue(boneName, out int boneIndex))
                {
                    throw new ImportAbortedException(
                        $"prop \"{propProperty.Name}\" rides bone \"{boneName}\", which isn't in skin.json's \"bones\"");
                }
                float[] boneHead = ((JArray)((JObject)((JObject)root["bones"])[boneName])["head"]).Select(v => (float)v).ToArray();
                Vector2 pivot = new Vector2((boneHead[0] - rx) / rw, 1f - (boneHead[1] - ry) / rh);
                propPivots[propProperty.Name] = pivot;
                ApplyBaseImportSettings($"{RigRoot}/{file}", pivot, SpriteMeshType.Tight);
            }

            // ---- Write bones + mesh into each skinned sprite. ----
            var skinnedLayers = new List<SkinLayerDef>();
            foreach (string layerName in SkinnedLayerOrder)
            {
                var layerJson = (JObject)skinnedJson[layerName];
                string file = (string)layerJson["file"];
                var rect = ((JArray)layerJson["rect"]).Select(v => (float)v).ToArray();
                float rx = rect[0], ry = rect[1], rw = rect[2], rh = rect[3];

                WriteSkinnedMesh($"{RigRoot}/{file}", layerJson, bones, boneIndexByName, rx, ry, rh, layerName);

                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{RigRoot}/{file}");
                if (sprite == null)
                {
                    throw new ImportAbortedException($"no sprite imported at {RigRoot}/{file} for skinned layer \"{layerName}\"");
                }
                int sortingOrder = drawOrder.IndexOf(layerName);
                if (sortingOrder < 0)
                {
                    throw new ImportAbortedException($"\"draw_order\" is missing skinned layer \"{layerName}\"");
                }
                skinnedLayers.Add(new SkinLayerDef
                {
                    name = layerName,
                    sprite = sprite,
                    localPosition = World(rx, ry + rh), // the rect's own bottom-left corner
                    sortingOrder = sortingOrder,
                });
            }

            // ---- Props: group by slot so head_blink becomes head's altSprite instead of its own
            // draw-order entry. ----
            var props = new List<SkinPropDef>();
            var propsBySlot = propsJson.Properties()
                .GroupBy(p => (string)((JObject)p.Value)["slot"])
                .ToList();
            foreach (var slotGroup in propsBySlot)
            {
                string slot = slotGroup.Key;
                int sortingOrder = drawOrder.IndexOf(slot);
                if (sortingOrder < 0)
                {
                    throw new ImportAbortedException($"\"draw_order\" is missing prop slot \"{slot}\"");
                }

                JProperty primary = slotGroup.FirstOrDefault(p => p.Name == slot) ?? slotGroup.First();
                JProperty alt = slotGroup.FirstOrDefault(p => p.Name != primary.Name);

                var primaryJson = (JObject)primary.Value;
                string boneName = (string)primaryJson["bone"];
                boneIndexByName.TryGetValue(boneName, out int boneIndex);

                Sprite primarySprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{RigRoot}/{(string)primaryJson["file"]}");
                Sprite altSprite = alt != null
                    ? AssetDatabase.LoadAssetAtPath<Sprite>($"{RigRoot}/{(string)((JObject)alt.Value)["file"]}")
                    : null;

                props.Add(new SkinPropDef
                {
                    slot = slot,
                    sprite = primarySprite,
                    altSprite = altSprite,
                    boneIndex = boneIndex,
                    sortingOrder = sortingOrder,
                });
            }

            // ---- Legs: thigh → shin → boot, mirrored so the knees bend outward on both sides.
            var legs = new List<SkinLegDef>
            {
                BuildLeg("left", false, boneIndexByName),
                BuildLeg("right", true, boneIndexByName),
            };

            SkinRigDefinition definition = AssetDatabase.LoadAssetAtPath<SkinRigDefinition>(DefinitionAssetPath);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<SkinRigDefinition>();
                AssetDatabase.CreateAsset(definition, DefinitionAssetPath);
            }
            definition.EditorInit(bones, skinnedLayers, props, legs);
            EditorUtility.SetDirty(definition);
            AssetDatabase.SaveAssets();

            Debug.Log(
                $"Characters/Import Knight Skin Rig: built {bones.Count} bone(s), {skinnedLayers.Count} skinned layer(s), " +
                $"{props.Count} prop(s) at {DefinitionAssetPath}.");
        }
        catch (ImportAbortedException e)
        {
            Debug.LogError($"SkinRigImporter: {e.Message} — aborting, no assets touched.");
        }
    }

    private static SkinLegDef BuildLeg(string side, bool flip, Dictionary<string, int> boneIndexByName)
    {
        string thigh = $"thigh_{side}", shin = $"shin_{side}", boot = $"boot_{side}";
        if (!boneIndexByName.TryGetValue(thigh, out int thighIndex) ||
            !boneIndexByName.TryGetValue(shin, out int shinIndex) ||
            !boneIndexByName.TryGetValue(boot, out int bootIndex))
        {
            throw new ImportAbortedException($"skin.json's \"bones\" is missing one of {thigh}/{shin}/{boot}");
        }
        return new SkinLegDef { side = side, thighBoneIndex = thighIndex, shinBoneIndex = shinIndex, bootBoneIndex = bootIndex, flip = flip };
    }

    /// <summary>Walks skin.json's "bones" in file order — every bone's parent is guaranteed to
    /// already have an entry earlier in the object — computing each one's rest-pose local
    /// position/rotation relative to its parent exactly like a Transform hierarchy, by chaining
    /// through a shared "world" (canvas-to-world-units) space that also places this rig's runtime
    /// bone Transforms (see SkinBoneCharacter) — so both use the identical numbers.</summary>
    private static List<SkinBoneDef> BuildSkeleton(
        JObject bonesJson, Func<float, float, Vector2> world, out Dictionary<string, int> boneIndexByName)
    {
        var boneOrder = bonesJson.Properties().Select(p => p.Name).ToList();
        boneIndexByName = new Dictionary<string, int>();
        var bones = new List<SkinBoneDef>();
        var worldHeadByName = new Dictionary<string, Vector2>();
        var worldRotByName = new Dictionary<string, float>();

        for (int i = 0; i < boneOrder.Count; i++)
        {
            string name = boneOrder[i];
            var boneJson = (JObject)bonesJson[name];
            var head = (JArray)boneJson["head"];
            var tail = (JArray)boneJson["tail"];
            string parentName = (string)boneJson["parent"];

            Vector2 worldHead = world((float)head[0], (float)head[1]);
            Vector2 worldTail = world((float)tail[0], (float)tail[1]);
            Vector2 dir = worldTail - worldHead;
            float worldRotationDeg = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            float length = dir.magnitude;

            int parentIndex;
            Vector2 localPosition;
            float localRotationDeg;
            if (string.IsNullOrEmpty(parentName))
            {
                parentIndex = -1;
                localPosition = worldHead;
                localRotationDeg = worldRotationDeg;
            }
            else
            {
                if (!boneIndexByName.TryGetValue(parentName, out parentIndex))
                {
                    throw new ImportAbortedException(
                        $"bone \"{name}\" declares parent \"{parentName}\", which hasn't appeared yet in skin.json's \"bones\"");
                }
                Vector2 parentWorldHead = worldHeadByName[parentName];
                float parentWorldRotationDeg = worldRotByName[parentName];
                Vector2 delta = worldHead - parentWorldHead;
                localPosition = Quaternion.Euler(0f, 0f, -parentWorldRotationDeg) * delta;
                localRotationDeg = worldRotationDeg - parentWorldRotationDeg;
            }

            boneIndexByName[name] = i;
            worldHeadByName[name] = worldHead;
            worldRotByName[name] = worldRotationDeg;

            bones.Add(new SkinBoneDef
            {
                name = name,
                parentIndex = parentIndex,
                localPosition = localPosition,
                localRotationDeg = localRotationDeg,
                length = length,
                canvasHead = new Vector2((float)head[0], (float)head[1]),
            });
        }
        return bones;
    }

    private static void ApplyBaseImportSettings(string path, Vector2 pivotNormalized, SpriteMeshType meshType)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (importer == null)
        {
            throw new ImportAbortedException($"no texture importer at {path}");
        }
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = PixelsPerUnit;
        importer.filterMode = FilterMode.Bilinear;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.ClearPlatformTextureSettings("iPhone");
        importer.ClearPlatformTextureSettings("Android");

        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteAlignment = (int)SpriteAlignment.Custom;
        settings.spritePivot = pivotNormalized;
        settings.spriteMeshType = meshType;
        importer.SetTextureSettings(settings);
        importer.SaveAndReimport();
    }

    /// <summary>Writes the full shared skeleton (every skinned layer carries all of it, so every
    /// SpriteSkin can share one boneTransforms array — see SkinBoneCharacter) and this layer's own
    /// vertices/triangles/weights into its Sprite via the Sprite Editor data provider APIs.
    /// Vertices/root-bone positions are converted from skin.json's canvas px (origin top-left, y
    /// down) into this sprite's own rect-relative px (origin at the rect's bottom-left, y up) —
    /// see the class doc for why non-root bones need no such conversion.</summary>
    private static void WriteSkinnedMesh(
        string path, JObject layerJson, List<SkinBoneDef> bones, Dictionary<string, int> boneIndexByName,
        float rectX, float rectY, float rectH, string layerName)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        var factory = new SpriteDataProviderFactories();
        factory.Init();
        ISpriteEditorDataProvider dataProvider = factory.GetSpriteEditorDataProviderFromObject(importer);
        dataProvider.InitSpriteEditorDataProvider();
        SpriteRect[] spriteRects = dataProvider.GetSpriteRects();
        SpriteRect spriteRect = spriteRects[0];

        var spriteBones = new List<SpriteBone>(bones.Count);
        for (int i = 0; i < bones.Count; i++)
        {
            SkinBoneDef bone = bones[i];
            Vector3 rawPosition = bone.parentIndex == -1
                // Root: this bone's own canvas head, converted to this sprite's own rect-relative
                // px (pivot is Custom (0,0), so no further subtraction is needed here).
                ? new Vector3(
                    bone.canvasHead.x - rectX,
                    rectH - (bone.canvasHead.y - rectY),
                    0f)
                // Non-root: a pure parent-relative offset — identical across every sprite (see
                // class doc) — just scaled back up to the px this sprite's data provider expects.
                : new Vector3(bone.localPosition.x * PixelsPerUnit, bone.localPosition.y * PixelsPerUnit, 0f);

            spriteBones.Add(new SpriteBone
            {
                name = bone.name,
                parentId = bone.parentIndex,
                position = rawPosition,
                rotation = Quaternion.Euler(0f, 0f, bone.localRotationDeg),
                length = bone.length * PixelsPerUnit,
            });
        }
        dataProvider.GetDataProvider<ISpriteBoneDataProvider>().SetBones(spriteRect.spriteID, spriteBones);

        var verticesJson = (JArray)layerJson["vertices"];
        var weightsJson = (JArray)layerJson["weights"];
        if (verticesJson.Count != weightsJson.Count)
        {
            throw new ImportAbortedException($"\"{layerName}\" has {verticesJson.Count} vertices but {weightsJson.Count} weight entries");
        }
        var vertices = new Vertex2DMetaData[verticesJson.Count];
        for (int i = 0; i < verticesJson.Count; i++)
        {
            var vertexCanvas = (JArray)verticesJson[i];
            float canvasX = (float)vertexCanvas[0];
            float canvasY = (float)vertexCanvas[1];
            vertices[i] = new Vertex2DMetaData
            {
                position = new Vector2(canvasX - rectX, rectH - (canvasY - rectY)),
                boneWeight = BuildBoneWeight((JArray)weightsJson[i], boneIndexByName, layerName),
            };
        }
        var meshDataProvider = dataProvider.GetDataProvider<ISpriteMeshDataProvider>();
        meshDataProvider.SetVertices(spriteRect.spriteID, vertices);

        var trianglesJson = (JArray)layerJson["triangles"];
        var indices = new int[trianglesJson.Count * 3];
        for (int i = 0; i < trianglesJson.Count; i++)
        {
            var tri = (JArray)trianglesJson[i];
            indices[i * 3 + 0] = (int)tri[0];
            indices[i * 3 + 1] = (int)tri[1];
            indices[i * 3 + 2] = (int)tri[2];
        }
        meshDataProvider.SetIndices(spriteRect.spriteID, indices);
        meshDataProvider.SetEdges(spriteRect.spriteID, Array.Empty<Vector2Int>());

        dataProvider.SetSpriteRects(spriteRects);
        dataProvider.Apply();
        importer.SaveAndReimport();
    }

    private static BoneWeight BuildBoneWeight(JArray weightPairs, Dictionary<string, int> boneIndexByName, string layerName)
    {
        var boneWeight = new BoneWeight();
        int slot = 0;
        foreach (JToken pairToken in weightPairs)
        {
            if (slot >= 4) break; // skin.json already caps every vertex at 4 influences.
            var pair = (JArray)pairToken;
            string boneName = (string)pair[0];
            float weight = (float)pair[1];
            if (!boneIndexByName.TryGetValue(boneName, out int boneIndex))
            {
                throw new ImportAbortedException($"\"{layerName}\" weights name unknown bone \"{boneName}\"");
            }
            switch (slot)
            {
                case 0: boneWeight.boneIndex0 = boneIndex; boneWeight.weight0 = weight; break;
                case 1: boneWeight.boneIndex1 = boneIndex; boneWeight.weight1 = weight; break;
                case 2: boneWeight.boneIndex2 = boneIndex; boneWeight.weight2 = weight; break;
                case 3: boneWeight.boneIndex3 = boneIndex; boneWeight.weight3 = weight; break;
            }
            slot++;
        }
        return boneWeight;
    }
}
#endif
