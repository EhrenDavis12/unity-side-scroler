using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;

/// <summary>
/// One catalog row: enough to build a carousel tile (id, display name) without loading
/// anything, plus an Addressable reference to the rest (controller, thumbnail, action names) —
/// loaded on demand by ShowcaseScreen.
/// </summary>
[Serializable]
public struct CharacterCatalogEntry
{
    public string id;
    public string displayName;
    public AssetReferenceT<CharacterDefinition> definition;
}

/// <summary>
/// Every playable character, in catalog (JSON) order. Built by
/// <c>Characters/Import Sheets</c> at Assets/Characters/CharacterCatalog.asset. A plain scene
/// reference (not itself Addressable) — only each entry's <see cref="CharacterDefinition"/> is.
/// </summary>
public class CharacterCatalog : ScriptableObject
{
    [SerializeField] private List<CharacterCatalogEntry> characters = new List<CharacterCatalogEntry>();

    public IReadOnlyList<CharacterCatalogEntry> Characters => characters;

#if UNITY_EDITOR
    public void EditorInit(List<CharacterCatalogEntry> characters)
    {
        this.characters = characters;
    }
#endif
}
