using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.U2D;
using UnityEngine.UIElements;

/// <summary>
/// Drives the Showcase screen's bottom-half UI Toolkit carousel + action buttons, and which
/// character/action plays in the top half via ShowcaseCharacter. The UIDocument (its
/// PanelSettings, Showcase.uxml and Showcase.uss) is built once by the editor
/// (Characters/Build Showcase Scene); this component only queries and populates it, so nothing
/// here hand-edits scene YAML.
///
/// Every catalog entry's <see cref="CharacterDefinition"/> is Addressable, so its thumbnail and
/// controller aren't available until loaded: this component kicks off a load for every entry's
/// atlas *and* definition as soon as the screen opens, atlas first — the definition's thumbnail
/// sprite is a page of that atlas, and in a packed build the atlas can still be in flight when
/// the thumbnail is assigned, which UI Toolkit never repaints on its own once it resolves late.
/// Loading the atlas up front, plus repainting again whenever AddressableSpriteAtlasBinder binds
/// one late, covers both orderings. Reuses the already-loaded definition the instant a tile is
/// selected rather than loading again. All handles are released on teardown.
/// </summary>
public class ShowcaseScreen : MonoBehaviour
{
    private const int VisibleTiles = 5;

    // Bigger than the Flutter reference's 96 reference px: this is a desktop-sized view, and
    // the cap is in the PanelSettings' reference px, not raw screen px.
    private const float MaxTileWidth = 160f;
    private const float LabelHeight = 24f;

    // Must match the "margin-right" on the ".tile" rule in Showcase.uss — USS can't reference a
    // C# constant, so this is the one place that value is kept in sync by hand.
    private const float TileSpacing = 8f;

    [SerializeField] private CharacterCatalog catalog;
    [SerializeField] private ShowcaseCharacter showcaseCharacter;
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private VisualTreeAsset carouselTileTemplate;

    private ScrollView _carousel;
    private VisualElement _actionButtonRow;

    private readonly List<VisualElement> _tiles = new List<VisualElement>();
    private readonly List<AsyncOperationHandle<CharacterDefinition>> _handles =
        new List<AsyncOperationHandle<CharacterDefinition>>();
    private readonly List<AsyncOperationHandle<SpriteAtlas>> _atlasHandles =
        new List<AsyncOperationHandle<SpriteAtlas>>();
    private CharacterDefinition[] _loadedDefinitions;

    private int _selectedIndex = -1;
    // The tile a click is waiting on while its definition is still loading (or just failed) —
    // never a tile whose definition has already resolved. -1 when nothing is pending.
    private int _pendingSelection = -1;
    private float _lastViewportWidth = -1f;

    private void Start()
    {
        if (catalog == null || catalog.Characters.Count == 0) return;

        VisualElement root = uiDocument.rootVisualElement;
        _carousel = root.Q<ScrollView>("carousel");
        _actionButtonRow = root.Q<VisualElement>("action-button-row");
        _carousel.mode = ScrollViewMode.Horizontal;
        _carousel.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        _carousel.verticalScrollerVisibility = ScrollerVisibility.Hidden;

        _loadedDefinitions = new CharacterDefinition[catalog.Characters.Count];

        // The very first GeometryChangedEvent, once the panel's initial layout pass completes,
        // doubles as "build the carousel for the first time" — resolvedStyle sizes aren't valid
        // any earlier than that, and every later resize/rotation needs the same rebuild anyway.
        _carousel.RegisterCallback<GeometryChangedEvent>(OnCarouselGeometryChanged);

        AddressableSpriteAtlasBinder.AtlasBound += OnAtlasBound;
        LoadAllDefinitions();
        SelectCharacter(0); // applied once tile 0's definition resolves — see SelectCharacter().
    }

    private void OnDestroy()
    {
        AddressableSpriteAtlasBinder.AtlasBound -= OnAtlasBound;
        foreach (AsyncOperationHandle<CharacterDefinition> handle in _handles)
        {
            if (handle.IsValid()) Addressables.Release(handle);
        }
        foreach (AsyncOperationHandle<SpriteAtlas> handle in _atlasHandles)
        {
            if (handle.IsValid()) Addressables.Release(handle);
        }
    }

    private void OnCarouselGeometryChanged(GeometryChangedEvent evt)
    {
        float width = _carousel.contentViewport.resolvedStyle.width;
        if (Mathf.Approximately(width, _lastViewportWidth)) return;
        _lastViewportWidth = width;
        RebuildCarousel();
    }

    private void LoadAllDefinitions()
    {
        for (int i = 0; i < catalog.Characters.Count; i++)
        {
            int index = i;
            CharacterCatalogEntry entry = catalog.Characters[i];

            // Atlas first: its tag equals the character id (CharacterAddressablesSetup), and the
            // definition's thumbnail sprite is one of its pages.
            AsyncOperationHandle<SpriteAtlas> atlasHandle = Addressables.LoadAssetAsync<SpriteAtlas>(entry.id);
            _atlasHandles.Add(atlasHandle);
            atlasHandle.Completed += op => OnAtlasLoaded(index, op);

            AsyncOperationHandle<CharacterDefinition> handle = entry.definition.LoadAssetAsync();
            _handles.Add(handle);
            handle.Completed += op => OnDefinitionLoaded(index, op);
        }
    }

    private void OnAtlasLoaded(int index, AsyncOperationHandle<SpriteAtlas> op)
    {
        if (op.Status != AsyncOperationStatus.Succeeded)
        {
            Debug.LogError(
                $"ShowcaseScreen: failed to load the atlas for \"{catalog.Characters[index].id}\": {op.OperationException}");
            return;
        }
        RepaintThumbnail(index);
    }

    /// <summary>AddressableSpriteAtlasBinder's late-bound hook can also resolve a character's
    /// atlas after its thumbnail sprite was already assigned (with no atlas page yet resident);
    /// repaint that tile too so it doesn't stay blank.</summary>
    private void OnAtlasBound(string tag)
    {
        for (int i = 0; i < catalog.Characters.Count; i++)
        {
            if (catalog.Characters[i].id == tag) RepaintThumbnail(i);
        }
    }

    private void RepaintThumbnail(int index)
    {
        if (index < 0 || index >= _tiles.Count) return;
        _tiles[index].Q<Image>("thumbnail").MarkDirtyRepaint();
    }

    private void OnDefinitionLoaded(int index, AsyncOperationHandle<CharacterDefinition> op)
    {
        if (op.Status != AsyncOperationStatus.Succeeded)
        {
            Debug.LogError(
                $"ShowcaseScreen: failed to load the definition for \"{catalog.Characters[index].id}\": {op.OperationException}");
            // Leave the previous selection and content exactly as they were — never move the
            // highlight or action buttons onto a character that isn't actually on screen.
            if (_pendingSelection == index) _pendingSelection = -1;
            return;
        }

        CharacterDefinition definition = op.Result;
        _loadedDefinitions[index] = definition;
        SetTileThumbnail(index, definition.Thumbnail);
        if (_pendingSelection == index) ApplySelection(index);
    }

    private float TileWidth()
    {
        float viewportWidth = _carousel.contentViewport.resolvedStyle.width;
        if (float.IsNaN(viewportWidth) || viewportWidth <= 0f) viewportWidth = MaxTileWidth * VisibleTiles;
        float available = viewportWidth - (VisibleTiles - 1) * TileSpacing;
        return Mathf.Min(available / VisibleTiles, MaxTileWidth);
    }

    private void RebuildCarousel()
    {
        BuildCarousel();
        // BuildCarousel() doesn't know which character was already selected; SelectCharacter()
        // itself early-returns on an unchanged index, so reapply just the visual highlight.
        if (_selectedIndex >= 0 && _selectedIndex < _tiles.Count) SetTileSelected(_selectedIndex, true);
    }

    private void BuildCarousel()
    {
        _carousel.contentContainer.Clear();
        _tiles.Clear();

        float tileWidth = TileWidth();
        for (int i = 0; i < catalog.Characters.Count; i++)
        {
            CharacterCatalogEntry entry = catalog.Characters[i];
            TemplateContainer instance = carouselTileTemplate.Instantiate();
            VisualElement tile = instance.Q<VisualElement>("tile");
            tile.style.width = tileWidth;
            tile.style.height = tileWidth + LabelHeight;
            tile.Q<Label>("name-label").text = entry.displayName;

            int index = i;
            tile.RegisterCallback<ClickEvent>(_ => SelectCharacter(index));

            _carousel.contentContainer.Add(instance);
            _tiles.Add(tile);

            if (_loadedDefinitions[i] != null) SetTileThumbnail(i, _loadedDefinitions[i].Thumbnail);
        }
        UpdateContentAlignment(tileWidth);
    }

    /// <summary>
    /// When every tile fits within the viewport, centres the row instead of leaving it pinned
    /// to the left with dead space on the right; when it overflows, leaves it left-aligned and
    /// scrollable (the normal carousel behaviour).
    /// </summary>
    private void UpdateContentAlignment(float tileWidth)
    {
        int count = catalog.Characters.Count;
        float contentWidth = count * tileWidth + Mathf.Max(0, count - 1) * TileSpacing;
        float viewportWidth = _carousel.contentViewport.resolvedStyle.width;

        _carousel.contentContainer.EnableInClassList("carousel-content-centered", contentWidth < viewportWidth);
    }

    /// <summary>Picking a character moves the highlight and swaps the on-screen character only
    /// once its definition has actually resolved (immediately, if it already has) — never
    /// preemptively, so a failed load can't leave the highlight on a tile whose character never
    /// replaced what's on screen (see OnDefinitionLoaded).</summary>
    public void SelectCharacter(int index)
    {
        if (index == _selectedIndex || index == _pendingSelection) return;

        if (_loadedDefinitions[index] != null)
        {
            ApplySelection(index);
        }
        else
        {
            _pendingSelection = index;
        }
    }

    private void ApplySelection(int index)
    {
        _pendingSelection = -1;
        if (_selectedIndex >= 0 && _selectedIndex < _tiles.Count) SetTileSelected(_selectedIndex, false);
        _selectedIndex = index;
        SetTileSelected(index, true);
        showcaseCharacter.Show(_loadedDefinitions[index]);
        BuildActionButtons(_loadedDefinitions[index]);
    }

    private void SetTileSelected(int index, bool selected)
    {
        if (index < 0 || index >= _tiles.Count) return;
        _tiles[index].EnableInClassList("selected", selected);
    }

    private void SetTileThumbnail(int index, Sprite thumbnail)
    {
        if (index < 0 || index >= _tiles.Count) return;
        _tiles[index].Q<Image>("thumbnail").sprite = thumbnail;
    }

    private void BuildActionButtons(CharacterDefinition character)
    {
        _actionButtonRow.Clear();
        foreach (string actionName in character.ActionNames)
        {
            var button = new Button { text = Label(actionName) };
            button.AddToClassList("action-button");
            string capturedAction = actionName;
            button.clicked += () => showcaseCharacter.PlayAction(capturedAction);
            _actionButtonRow.Add(button);
        }
    }

    private static string Label(string key)
    {
        if (string.IsNullOrEmpty(key)) return key;
        return char.ToUpperInvariant(key[0]) + key.Substring(1);
    }
}
