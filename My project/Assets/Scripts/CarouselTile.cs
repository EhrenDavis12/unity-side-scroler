using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One carousel tile: a bordered thumbnail of a character's idle frame plus its name label.
/// Built entirely in code by ShowcaseScreen (no prefab). The border is a solid-colour Image
/// behind an inset "inner" Image, so changing the inset on selection changes the border's
/// apparent thickness (2px selected, 1px otherwise) along with its colour.
/// </summary>
[RequireComponent(typeof(Image))]
public class CarouselTile : MonoBehaviour
{
    private static readonly Color InnerBackground = new Color(0x1B / 255f, 0x1D / 255f, 0x2A / 255f);

    public Button Button { get; private set; }

    private Image _border;
    private RectTransform _innerRect;
    private Text _nameText;
    private Color _selectedColor;
    private Color _unselectedColor;
    private float _selectedBorderWidth;
    private float _unselectedBorderWidth;

    public static CarouselTile Create(
        Transform parent,
        CharacterDefinition character,
        float width,
        float height,
        float thumbnailHeight,
        float labelHeight,
        Color selectedColor,
        Color unselectedColor,
        float selectedBorderWidth,
        float unselectedBorderWidth)
    {
        var root = new GameObject(character.Id + "Tile", typeof(RectTransform), typeof(Image), typeof(Button));
        root.transform.SetParent(parent, false);

        var rootLayout = root.AddComponent<LayoutElement>();
        rootLayout.minWidth = width;
        rootLayout.preferredWidth = width;
        rootLayout.minHeight = height;
        rootLayout.preferredHeight = height;

        var tile = root.AddComponent<CarouselTile>();
        tile._border = root.GetComponent<Image>();
        tile.Button = root.GetComponent<Button>();
        tile._selectedColor = selectedColor;
        tile._unselectedColor = unselectedColor;
        tile._selectedBorderWidth = selectedBorderWidth;
        tile._unselectedBorderWidth = unselectedBorderWidth;

        var inner = new GameObject("Inner", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        inner.transform.SetParent(root.transform, false);
        tile._innerRect = (RectTransform)inner.transform;
        inner.GetComponent<Image>().color = InnerBackground;
        var innerLayout = inner.GetComponent<VerticalLayoutGroup>();
        innerLayout.childAlignment = TextAnchor.UpperCenter;
        innerLayout.childControlWidth = true;
        innerLayout.childControlHeight = true;
        innerLayout.childForceExpandWidth = true;
        innerLayout.childForceExpandHeight = false;

        var thumb = new GameObject("Thumbnail", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        thumb.transform.SetParent(inner.transform, false);
        var thumbImage = thumb.GetComponent<Image>();
        thumbImage.sprite = character.Thumbnail;
        thumbImage.preserveAspect = true;
        thumb.GetComponent<LayoutElement>().preferredHeight = thumbnailHeight;
        thumb.GetComponent<LayoutElement>().flexibleHeight = 1f;

        var label = new GameObject("Name", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
        label.transform.SetParent(inner.transform, false);
        var text = label.GetComponent<Text>();
        text.text = character.DisplayName;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 14;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        label.GetComponent<LayoutElement>().preferredHeight = labelHeight;
        label.GetComponent<LayoutElement>().flexibleHeight = 0f;
        tile._nameText = text;

        tile.SetSelected(false);
        return tile;
    }

    public void SetSelected(bool selected)
    {
        _border.color = selected ? _selectedColor : _unselectedColor;
        _nameText.color = selected ? _selectedColor : Color.white;
        float inset = selected ? _selectedBorderWidth : _unselectedBorderWidth;
        _innerRect.anchorMin = Vector2.zero;
        _innerRect.anchorMax = Vector2.one;
        _innerRect.offsetMin = new Vector2(inset, inset);
        _innerRect.offsetMax = new Vector2(-inset, -inset);
    }
}
