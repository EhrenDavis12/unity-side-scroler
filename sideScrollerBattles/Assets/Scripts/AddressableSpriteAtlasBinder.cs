using System;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.U2D;

/// <summary>
/// Bridges Unity's runtime sprite-atlas binding to Addressables. Each character's atlas has its
/// own "include in build" turned off (Addressables now owns packaging it — see
/// CharacterAddressablesSetup), so Unity's SpriteAtlasManager can no longer find it on its own
/// the moment a packed sprite needs its atlas texture. This registers the standard late-bound
/// hook (the pattern com.unity.2d.common ships as its "LateBoundRuntimeSpriteAtlas" sample) so
/// that request resolves through Addressables instead, keyed by the atlas's own tag — which is
/// why the atlas's Addressable address is set to match its tag (the character id) in
/// CharacterAddressablesSetup.
/// </summary>
public static class AddressableSpriteAtlasBinder
{
    /// <summary>Fires once a requested atlas has finished loading and been handed back to
    /// Unity, keyed by the atlas tag (the character id). ShowcaseScreen uses this to repaint a
    /// thumbnail that was assigned before its atlas page was actually resident.</summary>
    public static event Action<string> AtlasBound;

    // Domain reload is off in this project's Player settings, so a static event's subscriber
    // list and this class's subscription to atlasRequested would otherwise survive into a new
    // run/scene load. SubsystemRegistration runs earliest, before any scene's Awake, so this
    // always starts from a clean slate.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        SpriteAtlasManager.atlasRequested -= OnAtlasRequested;
        AtlasBound = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        SpriteAtlasManager.atlasRequested -= OnAtlasRequested;
        SpriteAtlasManager.atlasRequested += OnAtlasRequested;
    }

    private static void OnAtlasRequested(string tag, Action<SpriteAtlas> callback)
    {
        Addressables.LoadAssetAsync<SpriteAtlas>(tag).Completed += op =>
        {
            SpriteAtlas atlas = op.Status == AsyncOperationStatus.Succeeded ? op.Result : null;
            callback(atlas);
            if (atlas != null) AtlasBound?.Invoke(tag);
        };
    }
}
