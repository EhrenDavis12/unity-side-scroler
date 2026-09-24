#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Builds Assets/Scenes/Showcase.unity from scratch: camera, world-space character, and the
/// uGUI carousel/action-button scaffolding that ShowcaseScreen populates at runtime. Run
/// Characters/Import Sheets first — this reads the CharacterCatalog asset it produces.
/// </summary>
public static class SceneBuilder
{
    private const string ScenePath = "Assets/Scenes/Showcase.unity";
    private const string CatalogAssetPath = "Assets/Characters/CharacterCatalog.asset";
    // Must match ShowcaseScreen's own constants — used here only to size the Carousel row's
    // LayoutElement before any tiles exist.
    private const float MaxTileWidth = 160f;
    private const float LabelHeight = 24f;

    private static readonly Color BackgroundColor = new Color(0x1B / 255f, 0x1D / 255f, 0x2A / 255f, 1f);

    [MenuItem("Characters/Build Showcase Scene")]
    public static void BuildShowcaseScene()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("Characters/Build Showcase Scene: stop Play Mode before rebuilding the scene.");
            return;
        }

        var catalog = AssetDatabase.LoadAssetAtPath<CharacterCatalog>(CatalogAssetPath);
        if (catalog == null)
        {
            Debug.LogError(
                "Characters/Build Showcase Scene: no CharacterCatalog asset found — run Characters/Import Sheets first.");
            return;
        }

        // NewScene(..., Single) discards the currently open scene; give the user a chance to
        // save or cancel instead of silently losing unrelated edits.
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Camera — solid background colour, no background image, sized by ShowcaseCameraFit.
        var cameraGo = new GameObject("Main Camera", typeof(Camera));
        cameraGo.tag = "MainCamera";
        var camera = cameraGo.GetComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 1f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = BackgroundColor;
        camera.transform.position = new Vector3(0f, 0f, -10f);
        cameraGo.AddComponent<ShowcaseCameraFit>();

        // Character — world-space SpriteRenderer + Animator, feet at the origin.
        var characterGo = new GameObject("ShowcaseCharacter", typeof(SpriteRenderer), typeof(Animator));
        characterGo.transform.position = Vector3.zero;
        var showcaseCharacter = characterGo.AddComponent<ShowcaseCharacter>();

        // Canvas — Screen Space Overlay, scaled with screen size.
        var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = 0.5f;

        // EventSystem — activeInputHandler is the new Input System only, so InputSystemUIInputModule.
        var eventSystemGo = new GameObject("EventSystem", typeof(EventSystem));
        eventSystemGo.AddComponent<InputSystemUIInputModule>();

        // Bottom-half panel: carousel above a centred row of action buttons.
        var bottomPanelGo = new GameObject("BottomPanel", typeof(RectTransform), typeof(VerticalLayoutGroup));
        bottomPanelGo.transform.SetParent(canvasGo.transform, false);
        var bottomPanelRect = (RectTransform)bottomPanelGo.transform;
        bottomPanelRect.anchorMin = new Vector2(0f, 0f);
        bottomPanelRect.anchorMax = new Vector2(1f, 0.5f);
        bottomPanelRect.offsetMin = Vector2.zero;
        bottomPanelRect.offsetMax = Vector2.zero;
        var bottomLayout = bottomPanelGo.GetComponent<VerticalLayoutGroup>();
        bottomLayout.childAlignment = TextAnchor.UpperCenter;
        bottomLayout.childControlWidth = true;
        bottomLayout.childControlHeight = true;
        bottomLayout.childForceExpandWidth = true;
        bottomLayout.childForceExpandHeight = false;
        bottomLayout.spacing = 16;
        bottomLayout.padding = new RectOffset(16, 16, 16, 16);

        // Carousel: ScrollRect > Viewport (masked) > Content (horizontal layout, populated at runtime).
        var carouselGo = new GameObject("Carousel", typeof(RectTransform), typeof(ScrollRect), typeof(LayoutElement));
        carouselGo.transform.SetParent(bottomPanelGo.transform, false);
        var carouselLayoutElement = carouselGo.GetComponent<LayoutElement>();
        carouselLayoutElement.preferredHeight = MaxTileWidth + LabelHeight;
        carouselLayoutElement.flexibleHeight = 0f;

        var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        viewportGo.transform.SetParent(carouselGo.transform, false);
        var viewportRect = (RectTransform)viewportGo.transform;
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = Vector2.zero;
        viewportRect.offsetMax = Vector2.zero;
        // Mask stencil-writes through this graphic's alpha; showMaskGraphic hides it from view,
        // but the alpha itself must stay clear of the UI shader's alpha-clip threshold (a
        // near-zero alpha here got the whole mask discarded, hiding every tile). Full opacity,
        // as Unity's own default Scroll View prefab uses.
        viewportGo.GetComponent<Image>().color = Color.white;
        viewportGo.GetComponent<Mask>().showMaskGraphic = false;

        var contentGo = new GameObject(
            "Content", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
        contentGo.transform.SetParent(viewportGo.transform, false);
        var contentRect = (RectTransform)contentGo.transform;
        contentRect.anchorMin = new Vector2(0f, 0.5f);
        contentRect.anchorMax = new Vector2(0f, 0.5f);
        contentRect.pivot = new Vector2(0f, 0.5f);
        var contentLayout = contentGo.GetComponent<HorizontalLayoutGroup>();
        contentLayout.childAlignment = TextAnchor.MiddleLeft;
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = true;
        contentLayout.childForceExpandWidth = false;
        contentLayout.childForceExpandHeight = true;
        contentLayout.spacing = 8;
        var contentFitter = contentGo.GetComponent<ContentSizeFitter>();
        contentFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        contentFitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

        var scrollRect = carouselGo.GetComponent<ScrollRect>();
        scrollRect.horizontal = true;
        scrollRect.vertical = false;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.viewport = viewportRect;
        scrollRect.content = contentRect;

        // Action button row — populated at runtime for whichever character is selected.
        var buttonRowGo = new GameObject(
            "ActionButtonRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        buttonRowGo.transform.SetParent(bottomPanelGo.transform, false);
        var buttonRowLayout = buttonRowGo.GetComponent<HorizontalLayoutGroup>();
        buttonRowLayout.childAlignment = TextAnchor.MiddleCenter;
        buttonRowLayout.spacing = 12;
        // childControl true so each button's LayoutElement (set in ShowcaseScreen.CreateActionButton)
        // actually drives its RectTransform size, instead of buttons staying at the default 100x100.
        buttonRowLayout.childControlWidth = true;
        buttonRowLayout.childControlHeight = true;
        buttonRowLayout.childForceExpandWidth = false;
        buttonRowLayout.childForceExpandHeight = false;
        var buttonRowLayoutElement = buttonRowGo.GetComponent<LayoutElement>();
        buttonRowLayoutElement.preferredHeight = 44f;
        buttonRowLayoutElement.flexibleHeight = 0f;

        // ShowcaseScreen — wires the catalog and the containers above; builds tiles/buttons on Start.
        // Re-load the catalog reference here rather than reusing the one loaded before NewScene():
        // EditorSceneManager.NewScene() can leave an asset reference loaded before it as a "fake
        // null" (destroyed native object, non-null C# wrapper), which serializes as a silently
        // dropped {fileID: 0} reference instead of throwing.
        var freshCatalog = AssetDatabase.LoadAssetAtPath<CharacterCatalog>(CatalogAssetPath);
        var screenGo = new GameObject("ShowcaseScreen", typeof(ShowcaseScreen));
        var showcaseScreen = screenGo.GetComponent<ShowcaseScreen>();
        var serialized = new SerializedObject(showcaseScreen);
        serialized.FindProperty("catalog").objectReferenceValue = freshCatalog;
        serialized.FindProperty("showcaseCharacter").objectReferenceValue = showcaseCharacter;
        serialized.FindProperty("carouselScrollRect").objectReferenceValue = scrollRect;
        serialized.FindProperty("carouselViewport").objectReferenceValue = viewportRect;
        serialized.FindProperty("carouselContent").objectReferenceValue = contentRect;
        serialized.FindProperty("actionButtonRow").objectReferenceValue = (RectTransform)buttonRowGo.transform;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        EnsureSceneFolder();
        EditorSceneManager.SaveScene(scene, ScenePath);
        AddSceneToBuildSettings(ScenePath);
        Debug.Log("Characters/Build Showcase Scene: built " + ScenePath);
    }

    private static void EnsureSceneFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
        {
            AssetDatabase.CreateFolder("Assets", "Scenes");
        }
    }

    private static void AddSceneToBuildSettings(string path)
    {
        // Also drops any entry whose scene file no longer exists (e.g. the template's deleted
        // Assets/Scenes/SampleScene.unity) rather than just the one being inserted here.
        var scenes = EditorBuildSettings.scenes
            .Where(s => !string.IsNullOrEmpty(s.path) && File.Exists(s.path))
            .Where(s => s.path != path)
            .ToList();
        scenes.Insert(0, new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
#endif
