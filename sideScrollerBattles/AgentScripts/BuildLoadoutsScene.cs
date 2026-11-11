using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Builds Assets/Scenes/Loadouts.unity: dark background, an orthographic camera, an
/// overlay canvas and a LoadoutsController. The archers, labels and buttons are made by the
/// controller at Start from LoadoutsCatalog, so the scene itself never changes when a robe, bow
/// or action is added. Refuses (changing nothing) if play mode is on or the open scene has
/// unsaved changes.</summary>
public static class BuildLoadoutsScene
{
    private const string ScenePath = "Assets/Scenes/Loadouts.unity";
    private const string CatalogPath = "Assets/Characters/LoadoutsCatalog.asset";

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "refused: editor is in play mode";
        // NewScene(Single) closes every loaded scene without prompting, so any dirty one blocks it.
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene open = SceneManager.GetSceneAt(i);
            if (open.isDirty) return $"refused: open scene \"{open.path}\" has unsaved changes";
        }

        if (AssetDatabase.LoadAssetAtPath<LoadoutsCatalog>(CatalogPath) == null)
            EditorApplication.ExecuteMenuItem("Characters/Import Loadouts");

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        // Loaded after NewScene: replacing the scene unloads assets loaded before it, which left
        // this reference null.
        var catalog = AssetDatabase.LoadAssetAtPath<LoadoutsCatalog>(CatalogPath);

        // Same dark background as Animation Versions; the controller fits the ortho size to the row of archers.
        var cameraGo = new GameObject("Main Camera");
        var camera = cameraGo.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.105882354f, 0.11372549f, 0.16470589f, 1f);
        camera.orthographic = true;
        camera.orthographicSize = 2f;
        cameraGo.transform.position = new Vector3(0f, 0f, -10f);
        cameraGo.tag = "MainCamera";
        cameraGo.AddComponent<AudioListener>();

        // The Animation Versions scene has no EventSystem, so its buttons only answer keys;
        // this one needs mouse clicks to work too.
        var eventSystemGo = new GameObject("EventSystem");
        eventSystemGo.AddComponent<EventSystem>();
        eventSystemGo.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();

        var canvasGo = new GameObject("Canvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        var controller = canvasGo.AddComponent<LoadoutsController>();
        var so = new SerializedObject(controller);
        so.FindProperty("catalog").objectReferenceValue = catalog;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.SaveScene(scene, ScenePath);
        return $"saved {ScenePath}";
    }
}
