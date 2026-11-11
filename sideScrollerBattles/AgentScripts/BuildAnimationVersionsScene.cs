using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class BuildAnimationVersionsScene
{
    private const string ScenePath = "Assets/Scenes/AnimationVersions.unity";

    public static string Run()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Same dark background + orthographic-fit-to-cell framing as Showcase.unity.
        var cameraGo = new GameObject("Main Camera");
        var camera = cameraGo.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.105882354f, 0.11372549f, 0.16470589f, 1f);
        cameraGo.transform.position = new Vector3(0f, 0f, -10f);
        cameraGo.tag = "MainCamera";
        cameraGo.AddComponent<AudioListener>();
        cameraGo.AddComponent<ShowcaseCameraFit>();

        // ---- Character sprite + player (shared by every character on the screen). Feet at
        // y = 0 (matching the Showcase's own character) would put the tallest frames' plume close
        // enough to the top edge to collide with the title/description labels — pulled down so the
        // labels always have clear headroom even in the worst case: a status badge plus a
        // description long enough to wrap to 2 lines (e.g. an "Idle rotation" entry).
        var characterGo = new GameObject("Character");
        characterGo.transform.position = new Vector3(0f, -0.5f, 0f);
        var spriteRenderer = characterGo.AddComponent<SpriteRenderer>();
        var player = characterGo.AddComponent<AnimationVersionPlayer>();

        // Second layer, same transform, for IdleRotationPlayer's crossfade — drawn on top of the
        // base layer (higher sorting order) so its alpha fade-in is what actually shows through.
        var overlayGo = new GameObject("CharacterFidgetOverlay");
        overlayGo.transform.SetParent(characterGo.transform.parent, false);
        overlayGo.transform.position = characterGo.transform.position;
        var overlayRenderer = overlayGo.AddComponent<SpriteRenderer>();
        overlayRenderer.sortingOrder = 1;
        overlayRenderer.enabled = false;
        var rotationPlayer = characterGo.AddComponent<IdleRotationPlayer>();

        // ---- Canvas ----
        var canvasGo = new GameObject("Canvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGo.AddComponent<CanvasScaler>();
        canvasGo.AddComponent<GraphicRaycaster>();

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        Text MakeLabel(string name, int fontSize, TextAnchor anchor, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 pivot, Vector2 anchoredPos, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(canvasGo.transform, false);
            var text = go.AddComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.color = Color.white;
            var rect = text.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = size;
            return text;
        }

        // Title + status badge + description, top of screen. The badge sits on its own row
        // directly under the title rather than beside it — titles vary a lot in length ("Knight
        // · idle · v9" vs "Knight · fidget_glance · v1"), so a fixed horizontal offset "beside"
        // the title text risks colliding with the longest ones; a dedicated row never can.
        Text title = MakeLabel("Title", 36, TextAnchor.UpperCenter,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -24f), new Vector2(900f, 50f));

        var statusBadge = new GameObject("StatusBadge");
        statusBadge.transform.SetParent(canvasGo.transform, false);
        var statusBadgeImage = statusBadge.AddComponent<Image>();
        statusBadgeImage.color = new Color(0.2f, 0.8f, 0.3f); // green — "prod candidate" / "prod"
        var statusBadgeRect = statusBadge.GetComponent<RectTransform>();
        statusBadgeRect.anchorMin = new Vector2(0.5f, 1f);
        statusBadgeRect.anchorMax = new Vector2(0.5f, 1f);
        statusBadgeRect.pivot = new Vector2(0.5f, 1f);
        statusBadgeRect.anchoredPosition = new Vector2(0f, -72f);
        statusBadgeRect.sizeDelta = new Vector2(220f, 28f);
        var statusBadgeLabelGo = new GameObject("Text");
        statusBadgeLabelGo.transform.SetParent(statusBadge.transform, false);
        Text statusBadgeLabel = statusBadgeLabelGo.AddComponent<Text>();
        statusBadgeLabel.font = font;
        statusBadgeLabel.fontSize = 16;
        statusBadgeLabel.fontStyle = FontStyle.Bold;
        statusBadgeLabel.alignment = TextAnchor.MiddleCenter;
        statusBadgeLabel.color = Color.black;
        var statusBadgeLabelRect = statusBadgeLabel.GetComponent<RectTransform>();
        statusBadgeLabelRect.anchorMin = Vector2.zero;
        statusBadgeLabelRect.anchorMax = Vector2.one;
        statusBadgeLabelRect.offsetMin = Vector2.zero;
        statusBadgeLabelRect.offsetMax = Vector2.zero;
        statusBadge.SetActive(false);

        // y is the no-badge default; AnimationVersionsController repositions it on every Show()
        // depending on whether the selected entry's status badge is visible — see there.
        Text description = MakeLabel("Description", 20, TextAnchor.UpperCenter,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -78f), new Vector2(900f, 60f));

        // Counter, bottom centre.
        Text counter = MakeLabel("Counter", 22, TextAnchor.LowerCenter,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 24f), new Vector2(200f, 40f));

        // What IdleRotationPlayer is currently playing ("idle", "→ glance", ...) — empty, and
        // meaningless, for every normal single-version entry.
        Text playingLabel = MakeLabel("PlayingLabel", 18, TextAnchor.LowerCenter,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 54f), new Vector2(300f, 30f));

        // ---- Pending placeholder card, centred, hidden by default ----
        var pendingCard = new GameObject("PendingCard");
        pendingCard.transform.SetParent(canvasGo.transform, false);
        var pendingImage = pendingCard.AddComponent<Image>();
        pendingImage.color = new Color(1f, 1f, 1f, 0.08f);
        var pendingRect = pendingCard.GetComponent<RectTransform>();
        pendingRect.anchorMin = new Vector2(0.5f, 0.5f);
        pendingRect.anchorMax = new Vector2(0.5f, 0.5f);
        pendingRect.pivot = new Vector2(0.5f, 0.5f);
        pendingRect.anchoredPosition = new Vector2(0f, 40f);
        pendingRect.sizeDelta = new Vector2(360f, 240f);
        var pendingTextGo = new GameObject("Text");
        pendingTextGo.transform.SetParent(pendingCard.transform, false);
        var pendingText = pendingTextGo.AddComponent<Text>();
        pendingText.font = font;
        pendingText.fontSize = 24;
        pendingText.alignment = TextAnchor.MiddleCenter;
        pendingText.color = Color.white;
        pendingText.text = "sheet finishing";
        var pendingTextRect = pendingText.GetComponent<RectTransform>();
        pendingTextRect.anchorMin = Vector2.zero;
        pendingTextRect.anchorMax = Vector2.one;
        pendingTextRect.offsetMin = Vector2.zero;
        pendingTextRect.offsetMax = Vector2.zero;
        pendingCard.SetActive(false);

        // ---- Nav buttons: ◀ ▶ (versions) + ▲▼ (actions, current character only) in one row,
        // « » (characters) in a row above it ----
        AnimationVersionsController controller = canvasGo.AddComponent<AnimationVersionsController>();

        Button MakeButton(string name, string label, Vector2 anchoredPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(canvasGo.transform, false);
            var image = go.AddComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0.15f);
            var button = go.AddComponent<Button>();
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = new Vector2(48f, 48f);

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(go.transform, false);
            var text = textGo.AddComponent<Text>();
            text.font = font;
            text.fontSize = 28;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.text = label;
            var textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            return button;
        }

        Button prevVersion = MakeButton("PrevVersionButton", "◀", new Vector2(-160f, 80f));
        Button nextVersion = MakeButton("NextVersionButton", "▶", new Vector2(160f, 80f));
        Button prevAction = MakeButton("PrevActionButton", "▼", new Vector2(-60f, 80f));
        Button nextAction = MakeButton("NextActionButton", "▲", new Vector2(60f, 80f));
        Button prevCharacter = MakeButton("PrevCharacterButton", "«", new Vector2(-60f, 140f)); // «
        Button nextCharacter = MakeButton("NextCharacterButton", "»", new Vector2(60f, 140f)); // »

        // Persistent listeners so these survive scene save/load (runtime AddListener does not).
        var stepMethod = typeof(AnimationVersionsController).GetMethod("Step");
        var stepActionMethod = typeof(AnimationVersionsController).GetMethod("StepAction");
        var stepCharacterMethod = typeof(AnimationVersionsController).GetMethod("StepCharacter");

        AddPersistentIntCall(prevVersion.onClick, controller, stepMethod, -1);
        AddPersistentIntCall(nextVersion.onClick, controller, stepMethod, 1);
        AddPersistentIntCall(prevAction.onClick, controller, stepActionMethod, -1);
        AddPersistentIntCall(nextAction.onClick, controller, stepActionMethod, 1);
        AddPersistentIntCall(prevCharacter.onClick, controller, stepCharacterMethod, -1);
        AddPersistentIntCall(nextCharacter.onClick, controller, stepCharacterMethod, 1);

        // ---- Wire the controller ----
        var catalog = AssetDatabase.LoadAssetAtPath<AnimationVersionsCatalog>(
            "Assets/Characters/AnimationVersionsCatalog.asset");
        var so = new SerializedObject(controller);
        so.FindProperty("catalog").objectReferenceValue = catalog;
        so.FindProperty("player").objectReferenceValue = player;
        so.FindProperty("rotationPlayer").objectReferenceValue = rotationPlayer;
        so.FindProperty("pendingCard").objectReferenceValue = pendingCard;
        so.FindProperty("titleLabel").objectReferenceValue = title;
        so.FindProperty("descriptionLabel").objectReferenceValue = description;
        so.FindProperty("counterLabel").objectReferenceValue = counter;
        so.FindProperty("statusBadge").objectReferenceValue = statusBadge;
        so.FindProperty("statusBadgeLabel").objectReferenceValue = statusBadgeLabel;
        so.ApplyModifiedPropertiesWithoutUndo();

        var rotationSo = new SerializedObject(rotationPlayer);
        rotationSo.FindProperty("baseRenderer").objectReferenceValue = spriteRenderer;
        rotationSo.FindProperty("overlayRenderer").objectReferenceValue = overlayRenderer;
        rotationSo.FindProperty("playingLabel").objectReferenceValue = playingLabel;
        rotationSo.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.SaveScene(scene, ScenePath);
        return $"saved {ScenePath}";
    }

    private static void AddPersistentIntCall(
        UnityEngine.Events.UnityEvent evt, UnityEngine.Object target, System.Reflection.MethodInfo method, int arg)
    {
        UnityEditor.Events.UnityEventTools.AddIntPersistentListener(evt, (UnityEngine.Events.UnityAction<int>)
            System.Delegate.CreateDelegate(typeof(UnityEngine.Events.UnityAction<int>), target, method), arg);
    }
}
