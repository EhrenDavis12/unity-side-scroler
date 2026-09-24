using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One playable character: its id, display name, the Animator controller that plays its
/// clips, a thumbnail (idle frame 0) for the carousel, and the ordered list of non-idle
/// action names the Showcase screen turns into buttons.
///
/// Built by <c>Characters/Import Sheets</c> (see Assets/Editor/CharacterImporter.cs) from
/// Assets/Characters/characters.json — never hand-authored.
/// </summary>
public class CharacterDefinition : ScriptableObject
{
    [SerializeField] private string id;
    [SerializeField] private string displayName;
    [SerializeField] private RuntimeAnimatorController controller;
    [SerializeField] private Sprite thumbnail;
    [SerializeField] private List<string> actionNames = new List<string>();

    public string Id => id;
    public string DisplayName => displayName;
    public RuntimeAnimatorController Controller => controller;
    public Sprite Thumbnail => thumbnail;
    public IReadOnlyList<string> ActionNames => actionNames;

#if UNITY_EDITOR
    public void EditorInit(
        string id,
        string displayName,
        RuntimeAnimatorController controller,
        Sprite thumbnail,
        List<string> actionNames)
    {
        this.id = id;
        this.displayName = displayName;
        this.controller = controller;
        this.thumbnail = thumbnail;
        this.actionNames = actionNames;
    }
#endif
}
