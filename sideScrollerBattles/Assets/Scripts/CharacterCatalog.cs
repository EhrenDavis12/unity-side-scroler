using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Every playable character, in catalog (JSON) order. Built by
/// <c>Characters/Import Sheets</c> at Assets/Characters/CharacterCatalog.asset.
/// </summary>
public class CharacterCatalog : ScriptableObject
{
    [SerializeField] private List<CharacterDefinition> characters = new List<CharacterDefinition>();

    public IReadOnlyList<CharacterDefinition> Characters => characters;

#if UNITY_EDITOR
    public void EditorInit(List<CharacterDefinition> characters)
    {
        this.characters = characters;
    }
#endif
}
