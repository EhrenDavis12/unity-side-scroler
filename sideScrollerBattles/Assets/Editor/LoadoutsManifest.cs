#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// One Assets/Characters/&lt;character&gt;_loadouts/loadouts.json (contract v2), parsed — shared by
/// LoadoutsTexturePostprocessor (which slices each sheet it names) and LoadoutsImporter (which
/// builds the catalog from it), so the sheet-naming rule and the grid live in one place.
///
/// A body names the frame set it is drawn with. Body sheets are
/// "&lt;action&gt;_body_&lt;bodyId&gt;.png" (grid from the body's frame set); weapon sheets are
/// "&lt;frameSetId&gt;_&lt;action&gt;_weapon_&lt;weaponId&gt;.png", one per frame set, shared by every
/// body in it, with exactly that frame set's grid.
/// </summary>
public class LoadoutsManifest
{
    public class Animation
    {
        public string action;
        public string label;
        public int columns;
        public int rows;
        public int frameCount;
        public int fps;
        public bool loop;
        public int cellSize;
    }

    public class FrameSet
    {
        public string id;
        public List<Animation> animations = new List<Animation>();
    }

    public string character;
    public List<LoadoutBody> bodies = new List<LoadoutBody>();
    public List<LoadoutOption> weapons = new List<LoadoutOption>();
    public List<FrameSet> frameSets = new List<FrameSet>();

    public static string BodyFile(Animation animation, string bodyId) => $"{animation.action}_body_{bodyId}.png";
    public static string WeaponFile(FrameSet frameSet, Animation animation, string weaponId) =>
        $"{frameSet.id}_{animation.action}_weapon_{weaponId}.png";

    /// <summary>Parses a loadouts.json; null (after logging) if it is unreadable or malformed.</summary>
    public static LoadoutsManifest Read(string jsonPath)
    {
        try
        {
            var root = JObject.Parse(File.ReadAllText(jsonPath));
            if (root["frameSets"] == null)
            {
                Debug.LogError($"LoadoutsManifest: {jsonPath} has no \"frameSets\" — only the v2 contract (bodies name a frameSet) is read.");
                return null;
            }
            var manifest = new LoadoutsManifest { character = (string)root["character"] };
            foreach (JObject fs in root["frameSets"].OfType<JObject>())
            {
                var frameSet = new FrameSet { id = (string)fs["id"] };
                foreach (JObject o in fs["animations"].OfType<JObject>())
                {
                    var animation = new Animation
                    {
                        action = (string)o["action"],
                        label = (string)o["label"],
                        columns = (int)o["columns"],
                        rows = (int)o["rows"],
                        frameCount = (int)o["frameCount"],
                        fps = (int)o["fps"],
                        loop = (bool)o["loop"],
                        cellSize = (int?)o["cellSize"] ?? 512,
                    };
                    string problem = Validate(animation);
                    if (problem != null)
                    {
                        Debug.LogError($"LoadoutsManifest: {jsonPath} frame set \"{frameSet.id}\" animation \"{animation.action}\" {problem} — ignoring it.");
                        continue;
                    }
                    frameSet.animations.Add(animation);
                }
                manifest.frameSets.Add(frameSet);
            }
            foreach (JObject o in root["bodies"].OfType<JObject>())
            {
                var body = new LoadoutBody { id = (string)o["id"], label = (string)o["label"], frameSet = (string)o["frameSet"] };
                if (!manifest.frameSets.Any(f => f.id == body.frameSet))
                {
                    Debug.LogError($"LoadoutsManifest: {jsonPath} body \"{body.id}\" names frame set \"{body.frameSet}\", which is not listed — ignoring it.");
                    continue;
                }
                manifest.bodies.Add(body);
            }
            foreach (JObject o in root["weapons"].OfType<JObject>())
                manifest.weapons.Add(new LoadoutOption { id = (string)o["id"], label = (string)o["label"] });
            return manifest;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"LoadoutsManifest: could not read {jsonPath}: {e.Message}");
            return null;
        }
    }

    /// <summary>Null if the grid is usable; otherwise what is wrong with it. Guards the slicing
    /// maths against a divide by zero or negative rects.</summary>
    public static string Validate(Animation a)
    {
        if (a.columns <= 0 || a.rows <= 0 || a.cellSize <= 0)
            return $"has columns {a.columns}, rows {a.rows}, cellSize {a.cellSize} (all must be > 0)";
        if (a.frameCount <= 0 || a.frameCount > a.columns * a.rows)
            return $"has frameCount {a.frameCount} outside 1..{a.columns * a.rows} (columns * rows)";
        return null;
    }

    /// <summary>Finds the animation a sheet file name belongs to; false if the JSON does not
    /// name this file.</summary>
    public bool TryFindAnimation(string fileName, out Animation found)
    {
        found = null;
        foreach (LoadoutBody body in bodies)
        {
            FrameSet frameSet = frameSets.First(f => f.id == body.frameSet);
            found = frameSet.animations.FirstOrDefault(a => BodyFile(a, body.id) == fileName);
            if (found != null) return true;
        }
        foreach (FrameSet frameSet in frameSets)
        {
            found = frameSet.animations.FirstOrDefault(a => weapons.Any(w => WeaponFile(frameSet, a, w.id) == fileName));
            if (found != null) return true;
        }
        return false;
    }
}
#endif
