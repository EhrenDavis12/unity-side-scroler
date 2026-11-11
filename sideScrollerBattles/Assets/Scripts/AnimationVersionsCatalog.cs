using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One row on the Animation Versions screen: a character/action/version triple, its description,
/// playback rate and loop flag, and its frames in playback order — empty and <see cref="pending"/>
/// for a version whose sheet hasn't finished generating yet (shown as a placeholder card instead
/// of played). <see cref="status"/> is an optional, free-form tag (e.g. "prod candidate", "prod")
/// shown as a badge next to the title when set — empty for every version with nothing to call out.
///
/// <see cref="isRotation"/> marks a character's one special "Idle rotation" entry, which plays no
/// sheet of its own — see <see cref="IdleRotationPlayer"/>. It instead names, by action/version,
/// which of this same character's other entries to drive: <see cref="rotationIdleVersion"/> (the
/// action is always "idle") to loop, and <see cref="rotationFidgetActions"/>/
/// <see cref="rotationFidgetVersions"/> (parallel arrays) for the fidgets it may cut to — so adding
/// a rotation for a new character is just another entry naming its own idle/fidget versions, never
/// a code change.
///
/// <see cref="matchedIdleFrames"/>/<see cref="matchedIdleFrameDistances"/> are set only for a
/// fidget entry a rotation can cut into: the indices (into that rotation's own idle entry's frames,
/// ascending by distance — index 0 is the single best match) of the idle frames whose downsampled,
/// alpha-weighted pixel content is closest to this fidget's own first frame — see
/// Assets/Editor/FidgetIdleMatcher.cs, the only place that does any image comparison. Precomputed
/// at import time precisely so the runtime crossfade never has to.
/// </summary>
[Serializable]
public struct AnimationVersionEntry
{
    public string character;
    public string action;
    public int version;
    public string description;
    public int fps;
    public bool loop;
    public bool pending;
    public string status;
    public bool isRotation;
    public int rotationIdleVersion;
    public string[] rotationFidgetActions;
    public int[] rotationFidgetVersions;
    public Sprite[] frames;
    public int[] matchedIdleFrames;
    public float[] matchedIdleFrameDistances;
}

/// <summary>
/// Every known attempt at every action, for every character — what the Animation Versions screen
/// cycles through. Ordered by character, then (within a character) its own "Idle rotation" entry
/// first if it has one, then by action and version — the same order navigation moves through, so
/// switching character always lands on that character's own first entry.
///
/// Built by Characters/Import Animation Versions (see Assets/Editor/AnimationVersionsImporter.cs)
/// from every Assets/Characters/&lt;character&gt;_versions/versions.json it finds — never
/// hand-authored. Adding a version to an existing character means dropping its PNG into that
/// character's own folder and adding one entry to that folder's versions.json; adding a whole new
/// character means adding a new "&lt;character&gt;_versions/" folder the same shape, then
/// re-running the importer.
/// </summary>
public class AnimationVersionsCatalog : ScriptableObject
{
    [SerializeField] private List<AnimationVersionEntry> entries = new List<AnimationVersionEntry>();

    public IReadOnlyList<AnimationVersionEntry> Entries => entries;

#if UNITY_EDITOR
    public void EditorInit(List<AnimationVersionEntry> entries)
    {
        this.entries = entries;
    }
#endif
}
