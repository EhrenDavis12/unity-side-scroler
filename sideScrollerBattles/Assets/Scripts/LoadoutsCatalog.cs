using System;
using UnityEngine;

/// <summary>One selectable bow on the Loadouts screen — an id (the sheet file name's suffix) and
/// the label shown to the reviewer.</summary>
[Serializable]
public struct LoadoutOption
{
    public string id;
    public string label;
}

/// <summary>One robe colourway on the Loadouts screen: an id (the body sheet file name's suffix),
/// the label shown to the reviewer, and the id of the frame set whose frames it is drawn with.</summary>
[Serializable]
public struct LoadoutBody
{
    public string id;
    public string label;
    public string frameSet;
}

/// <summary>
/// One animation of one frame set: how it plays, and where its sheets are. A body sheet and a
/// weapon sheet of the same frame set share the same grid, so weapon frame i drawn over body
/// frame i at the same position is the finished character. <see cref="bodySheets"/> is parallel
/// to the owning set's bodies (an empty path is a sheet the JSON names but that is missing or not
/// sliced, or a body that belongs to another frame set); <see cref="weaponSheets"/> is parallel
/// to its weapons (one sheet per weapon, shared by every body in the frame set).
/// <see cref="bodyExtents"/> and <see cref="weaponExtents"/> are parallel to the sheet arrays: the
/// union of the sheet's sliced sprites' tight bounds, in cell units (one cell = 1) measured from
/// the bottom-centre pivot — x from the centre, y up from the cell bottom, so y can be negative
/// where a weapon hangs below the feet. Measured at import so the screen can fit what is really
/// drawn without loading anything; a zero rect is a missing sheet.
/// Sheets are held as asset paths, not Sprite references, so only the animation on screen is
/// ever loaded — twelve uncompressed 6144x5632 sheets would be over a GB together.
/// </summary>
[Serializable]
public struct LoadoutAnimation
{
    public string action;
    public string label;
    public int frameCount;
    public int fps;
    public bool loop;
    public string[] bodySheets;
    public string[] weaponSheets;
    public Rect[] bodyExtents;
    public Rect[] weaponExtents;
}

/// <summary>A render's own animations: bodies that name this frame set play these, with their own
/// frame counts, grids and weapon sheets. A frame set may lack an action another has.</summary>
[Serializable]
public struct LoadoutFrameSet
{
    public string id;
    public LoadoutAnimation[] animations;
}

/// <summary>One character's layered loadouts: its bodies, its bows (weapons) and the frame sets
/// the bodies draw from.</summary>
[Serializable]
public struct LoadoutSet
{
    public string character;
    public LoadoutBody[] bodies;
    public LoadoutOption[] weapons;
    public LoadoutFrameSet[] frameSets;
}

/// <summary>
/// Everything the Loadouts screen shows, for every character. Built by Characters/Import
/// Loadouts (see Assets/Editor/LoadoutsImporter.cs) from every
/// Assets/Characters/&lt;character&gt;_loadouts/loadouts.json — never hand-authored. A new robe,
/// bow or character is a JSON + PNG change followed by a re-import.
/// </summary>
public class LoadoutsCatalog : ScriptableObject
{
    [SerializeField] private LoadoutSet[] sets = Array.Empty<LoadoutSet>();

    public LoadoutSet[] Sets => sets;

#if UNITY_EDITOR
    public void EditorInit(LoadoutSet[] sets)
    {
        this.sets = sets;
    }
#endif
}
