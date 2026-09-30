using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class BuildShowcaseScene
{
    private const string ScenePath = "Assets/Scenes/KnightShowcases.unity";

    public static string Run()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Dark background, orthographic camera centered on the origin (where SkinBoneCharacter
        // places the knight — see SkinRigImporter's canvas-center-to-world-origin mapping).
        var cameraGo = new GameObject("Main Camera");
        var camera = cameraGo.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 3f;
        camera.transform.position = new Vector3(0f, 0f, -10f);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.05f, 0.05f, 0.07f);
        cameraGo.tag = "MainCamera";
        cameraGo.AddComponent<AudioListener>();

        // ---- Showcase A: 2D Bones ----
        var showcaseA = new GameObject("Showcase A - 2D Bones");
        var rigDefinition = AssetDatabase.LoadAssetAtPath<SkinRigDefinition>("Assets/Characters/knight_skin/KnightSkinRig.asset");
        var attackKeysJson = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Characters/knight_skin/attack_keys.json");
        var skinBoneCharacter = showcaseA.AddComponent<SkinBoneCharacter>();
        var soA = new SerializedObject(skinBoneCharacter);
        soA.FindProperty("rig").objectReferenceValue = rigDefinition;
        soA.ApplyModifiedPropertiesWithoutUndo();

        var skinIdle = showcaseA.AddComponent<SkinIdle>();
        var skinAttack = showcaseA.AddComponent<SkinAttack>();
        var saA = new SerializedObject(skinAttack);
        saA.FindProperty("attackKeysJson").objectReferenceValue = attackKeysJson;
        saA.ApplyModifiedPropertiesWithoutUndo();

        // ---- Showcase B: empty placeholder (assets still generating — see the dispatch note) ----
        var showcaseB = new GameObject("Showcase B - 3D in 2D (placeholder)");
        showcaseB.SetActive(false);

        // ---- Switcher UI: a Canvas with one label, toggled by Tab or a button ----
        var canvasGo = new GameObject("Canvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGo.AddComponent<CanvasScaler>();
        canvasGo.AddComponent<GraphicRaycaster>();

        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(canvasGo.transform, false);
        var label = labelGo.AddComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 24;
        label.alignment = TextAnchor.UpperLeft;
        label.color = Color.white;
        var labelRect = label.GetComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0f, 1f);
        labelRect.anchorMax = new Vector2(0f, 1f);
        labelRect.pivot = new Vector2(0f, 1f);
        labelRect.anchoredPosition = new Vector2(16f, -16f);
        labelRect.sizeDelta = new Vector2(500f, 40f);

        var buttonGo = new GameObject("SwitchButton");
        buttonGo.transform.SetParent(canvasGo.transform, false);
        var buttonImage = buttonGo.AddComponent<Image>();
        buttonImage.color = new Color(1f, 1f, 1f, 0.15f);
        var button = buttonGo.AddComponent<Button>();
        var buttonRect = buttonGo.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0f, 1f);
        buttonRect.anchorMax = new Vector2(0f, 1f);
        buttonRect.pivot = new Vector2(0f, 1f);
        buttonRect.anchoredPosition = new Vector2(16f, -56f);
        buttonRect.sizeDelta = new Vector2(160f, 32f);

        var buttonLabelGo = new GameObject("Text");
        buttonLabelGo.transform.SetParent(buttonGo.transform, false);
        var buttonLabel = buttonLabelGo.AddComponent<Text>();
        buttonLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        buttonLabel.fontSize = 16;
        buttonLabel.alignment = TextAnchor.MiddleCenter;
        buttonLabel.color = Color.white;
        buttonLabel.text = "Switch";
        var buttonLabelRect = buttonLabel.GetComponent<RectTransform>();
        buttonLabelRect.anchorMin = Vector2.zero;
        buttonLabelRect.anchorMax = Vector2.one;
        buttonLabelRect.offsetMin = Vector2.zero;
        buttonLabelRect.offsetMax = Vector2.zero;

        var switcherGo = new GameObject("ShowcaseSwitcher");
        var switcher = switcherGo.AddComponent<ShowcaseSwitcher>();
        var switcherSo = new SerializedObject(switcher);
        switcherSo.FindProperty("showcaseA").objectReferenceValue = showcaseA;
        switcherSo.FindProperty("showcaseB").objectReferenceValue = showcaseB;
        switcherSo.FindProperty("label").objectReferenceValue = label;
        switcherSo.ApplyModifiedPropertiesWithoutUndo();

        UnityEditor.Events.UnityEventTools.AddPersistentListener(button.onClick, switcher.Toggle);

        EditorSceneManager.SaveScene(scene, ScenePath);
        return $"saved {ScenePath}";
    }
}
