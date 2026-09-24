using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds the Showcase screen's bottom-half UI (character carousel + action buttons) from the
/// CharacterCatalog at runtime, and drives which character/action plays in the top half via
/// ShowcaseCharacter. The Canvas, ScrollRect, viewport and layout containers themselves are
/// built once by the editor (Characters/Build Showcase Scene); this component only populates
/// their content, so nothing here hand-edits scene YAML.
/// </summary>
public class ShowcaseScreen : MonoBehaviour
{
    private const int VisibleTiles = 5;

    // Bigger than the Flutter reference's 96 reference px: this is a desktop-sized view, and
    // the cap is in the CanvasScaler's reference px, not raw screen px.
    private const float MaxTileWidth = 160f;
    private const float LabelHeight = 24f;

    private static readonly Color SelectedOutline = new Color(0x58 / 255f, 0xC4 / 255f, 0xFF / 255f);
    private static readonly Color UnselectedOutline = new Color(0.6f, 0.6f, 0.6f);
    private const float SelectedBorderWidth = 2f;
    private const float UnselectedBorderWidth = 1f;

    [SerializeField] private CharacterCatalog catalog;
    [SerializeField] private ShowcaseCharacter showcaseCharacter;
    [SerializeField] private ScrollRect carouselScrollRect;
    [SerializeField] private RectTransform carouselViewport;
    [SerializeField] private RectTransform carouselContent;
    [SerializeField] private RectTransform actionButtonRow;

    private readonly List<CarouselTile> _tiles = new List<CarouselTile>();
    private int _selectedIndex = -1;
    private float _lastViewportWidth = -1f;

    private void Start()
    {
        if (catalog == null || catalog.Characters.Count == 0) return;
        // The viewport's RectTransform hasn't been through a layout pass yet at Start (it still
        // holds its unlaid-out default rect), so TileWidth() would read a stale size. Force the
        // pending Canvas/layout-group rebuilds through before measuring it.
        Canvas.ForceUpdateCanvases();
        _lastViewportWidth = carouselViewport != null ? carouselViewport.rect.width : -1f;
        BuildCarousel();
        SelectCharacter(0);
    }

    private void Update()
    {
        // The camera fit (ShowcaseCameraFit) already reacts to a resize/rotation; the carousel
        // needs the same treatment, or it goes stale — tile size and centering were computed
        // once in Start against whatever the viewport happened to be then. RectTransforms don't
        // raise a C# event for this, so poll cheaply and only rebuild when the width actually
        // moved.
        if (carouselViewport == null) return;
        float width = carouselViewport.rect.width;
        if (Mathf.Approximately(width, _lastViewportWidth)) return;
        _lastViewportWidth = width;
        RebuildCarousel();
    }

    private float ContentSpacing()
    {
        var layout = carouselContent != null ? carouselContent.GetComponent<HorizontalLayoutGroup>() : null;
        return layout != null ? layout.spacing : 0f;
    }

    /// <summary>
    /// Tile width so exactly <see cref="VisibleTiles"/> tiles plus their gaps fill the viewport
    /// width — the sixth tile then peeks at the edge rather than being invisibly clipped flush.
    /// </summary>
    private float TileWidth()
    {
        float panelWidth = carouselViewport != null ? carouselViewport.rect.width : MaxTileWidth * VisibleTiles;
        float available = panelWidth - (VisibleTiles - 1) * ContentSpacing();
        return Mathf.Min(available / VisibleTiles, MaxTileWidth);
    }

    private void RebuildCarousel()
    {
        foreach (CarouselTile tile in _tiles)
        {
            if (tile != null) Destroy(tile.gameObject);
        }
        _tiles.Clear();
        BuildCarousel();
        // BuildCarousel() doesn't know which character was already selected; SelectCharacter()
        // itself early-returns on an unchanged index, so reapply just the visual highlight.
        if (_selectedIndex >= 0 && _selectedIndex < _tiles.Count) _tiles[_selectedIndex].SetSelected(true);
    }

    private void BuildCarousel()
    {
        float tileWidth = TileWidth();
        for (int i = 0; i < catalog.Characters.Count; i++)
        {
            CharacterDefinition character = catalog.Characters[i];
            CarouselTile tile = CarouselTile.Create(
                carouselContent,
                character,
                tileWidth,
                tileWidth + LabelHeight,
                tileWidth,
                LabelHeight,
                SelectedOutline,
                UnselectedOutline,
                SelectedBorderWidth,
                UnselectedBorderWidth);
            int index = i;
            tile.Button.onClick.AddListener(() => SelectCharacter(index));
            _tiles.Add(tile);
        }
        CenterOrScrollContent(tileWidth);
    }

    /// <summary>
    /// When every tile fits within the viewport, centres the row instead of leaving it pinned
    /// to the left with dead space on the right; when it overflows, leaves it left-aligned and
    /// scrollable (the normal carousel behaviour).
    /// </summary>
    private void CenterOrScrollContent(float tileWidth)
    {
        Canvas.ForceUpdateCanvases();
        float spacing = ContentSpacing();
        int count = catalog.Characters.Count;
        float contentWidth = count * tileWidth + Mathf.Max(0, count - 1) * spacing;
        float viewportWidth = carouselViewport.rect.width;

        if (contentWidth < viewportWidth)
        {
            if (carouselScrollRect != null) carouselScrollRect.horizontal = false;
            carouselContent.anchoredPosition = new Vector2((viewportWidth - contentWidth) / 2f, carouselContent.anchoredPosition.y);
        }
        else
        {
            if (carouselScrollRect != null) carouselScrollRect.horizontal = true;
            carouselContent.anchoredPosition = new Vector2(0f, carouselContent.anchoredPosition.y);
        }
    }

    /// <summary>Picking a character shows its idle immediately and rebuilds its action row.</summary>
    public void SelectCharacter(int index)
    {
        if (index == _selectedIndex) return;
        if (_selectedIndex >= 0 && _selectedIndex < _tiles.Count) _tiles[_selectedIndex].SetSelected(false);
        _selectedIndex = index;
        _tiles[index].SetSelected(true);

        CharacterDefinition character = catalog.Characters[index];
        showcaseCharacter.Show(character);
        BuildActionButtons(character);
    }

    private void BuildActionButtons(CharacterDefinition character)
    {
        for (int i = actionButtonRow.childCount - 1; i >= 0; i--)
        {
            Destroy(actionButtonRow.GetChild(i).gameObject);
        }
        foreach (string actionName in character.ActionNames)
        {
            Button button = CreateActionButton(actionButtonRow, Label(actionName));
            string capturedAction = actionName;
            button.onClick.AddListener(() => showcaseCharacter.PlayAction(capturedAction));
        }
    }

    private static string Label(string key)
    {
        if (string.IsNullOrEmpty(key)) return key;
        return char.ToUpperInvariant(key[0]) + key.Substring(1);
    }

    private static Button CreateActionButton(Transform parent, string label)
    {
        var go = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.color = new Color(0.25f, 0.28f, 0.4f);

        var layout = go.GetComponent<LayoutElement>();
        layout.minWidth = 140;
        layout.minHeight = 44;
        layout.preferredWidth = 140;
        layout.preferredHeight = 44;

        var textGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
        textGo.transform.SetParent(go.transform, false);
        var text = textGo.GetComponent<Text>();
        text.text = label;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 16;
        var textRect = (RectTransform)textGo.transform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        return go.GetComponent<Button>();
    }
}
