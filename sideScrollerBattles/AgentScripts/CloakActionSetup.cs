using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>TEST setup for the Nightmare Cloak's own actions (branch spike/nightmare-hurt8). For one action it imports
/// Assets/Spikes/CloakActions/{action}_s_body_forest / _s_weapon_wood (Forest's reference, its own frame count and columns)
/// and {action}_s_body_nightmare / _s_weapon_nightmare (the cloak, its own frame count and columns), cells of 512 with the
/// Loadouts sheets' settings, and builds {action}_CloakTest.unity: Forest Robes beside the Nightmare Cloak with its pinned bow,
/// on the Hurt8Test player at the fps meta.json names. Capture with Hurt8Setup.Capture. Errors come back as "ERROR: ...".</summary>
public static class CloakActionSetup
{
    const string Dir = "Assets/Spikes/CloakActions";
    const int Cell = 512;

    static string Name(string a, string kind) => $"{a}_s_{kind}";
    static Sprite[] Load(string s) => AssetDatabase.LoadAllAssetRepresentationsAtPath($"{Dir}/{s}.png").OfType<Sprite>().OrderBy(x => x.name).ToArray();

    static (int, int) PngSize(string path)
    {
        byte[] b = new byte[24]; using (var f = System.IO.File.OpenRead(path)) f.Read(b, 0, 24);
        return ((b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19], (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23]);
    }

    public static string Import(string action, int frames, int cols, int forestFrames, int forestCols)
    {
        AssetDatabase.Refresh();
        var sheets = new (string name, int n, int c)[]
        {
            (Name(action, "body_forest"), forestFrames, forestCols), (Name(action, "weapon_wood"), forestFrames, forestCols),
            (Name(action, "body_nightmare"), frames, cols), (Name(action, "weapon_nightmare"), frames, cols),
        };
        var said = new System.Collections.Generic.List<string>(); var errors = new System.Collections.Generic.List<string>();
        foreach (var (s, n, c) in sheets)
        {
            var imp = AssetImporter.GetAtPath($"{Dir}/{s}.png") as TextureImporter;
            if (imp == null) { errors.Add($"{s}: not imported"); continue; }
            var (w, h) = PngSize($"{Dir}/{s}.png");
            if (w != c * Cell || h % Cell != 0 || h / Cell * c < n) { errors.Add($"{s}: {w}x{h} does not hold {n} cells in {c} columns of {Cell}"); continue; }
            int rows = h / Cell;
            imp.textureType = TextureImporterType.Sprite; imp.spriteImportMode = SpriteImportMode.Multiple; imp.spritePixelsPerUnit = Cell;
            imp.filterMode = FilterMode.Bilinear; imp.mipmapEnabled = false; imp.alphaIsTransparency = true; imp.maxTextureSize = 8192;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            var ts = new TextureImporterSettings(); imp.ReadTextureSettings(ts); ts.spriteMeshType = SpriteMeshType.FullRect; imp.SetTextureSettings(ts);
            var f = new SpriteDataProviderFactories(); f.Init();
            var dp = f.GetSpriteEditorDataProviderFromObject(imp); dp.InitSpriteEditorDataProvider();
            var rects = Enumerable.Range(0, n).Select(i => new SpriteRect
            {
                name = $"frame_{i:D3}", spriteID = GUID.Generate(),
                rect = new Rect(i % c * Cell, (rows - 1 - i / c) * Cell, Cell, Cell),
                alignment = SpriteAlignment.Custom, pivot = new Vector2(0.5f, 0f),
            }).ToArray();
            dp.SetSpriteRects(rects);
            dp.GetDataProvider<ISpriteNameFileIdDataProvider>().SetNameFileIdPairs(rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)).ToList());
            dp.Apply(); imp.SaveAndReimport();
            int got = Load(s).Length; said.Add($"{s}: {got} sprites");
            if (got != n) errors.Add($"{s}: {got} sprites, wanted {n}");
        }
        return errors.Count > 0 ? "ERROR: " + string.Join("; ", errors) : string.Join(", ", said);
    }

    public static string BuildScene(string action, bool loop, int fps)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "ERROR: refused, play mode";
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var cam = new GameObject("Main Camera").AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.105882354f, 0.11372549f, 0.16470589f, 1f);
        cam.orthographic = true; cam.orthographicSize = 0.95f; cam.transform.position = new Vector3(0f, 0f, -10f); cam.tag = "MainCamera";
        var t = new GameObject("Hurt8Test").AddComponent<Hurt8Test>(); var so = new SerializedObject(t);
        string error = null;
        void Set(string field, string sheet)
        {
            Sprite[] sp = Load(sheet); if (sp.Length == 0) error = $"{sheet}: no sprites";
            var p = so.FindProperty(field); p.arraySize = sp.Length;
            for (int i = 0; i < sp.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = sp[i];
        }
        Set("forestBody", Name(action, "body_forest")); Set("woodBow", Name(action, "weapon_wood"));
        Set("nightmareBody", Name(action, "body_nightmare")); Set("nightmareBow", Name(action, "weapon_nightmare"));
        if (error != null) return "ERROR: " + error;
        so.FindProperty("caption").stringValue = $"own {action}"; so.FindProperty("loop").boolValue = loop; so.FindProperty("fps").intValue = fps;
        so.ApplyModifiedPropertiesWithoutUndo();
        string path = $"{Dir}/{action}_CloakTest.unity"; EditorSceneManager.SaveScene(scene, path); return "saved " + path;
    }
}
