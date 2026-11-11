using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>TEST setup for the 8-frame Hurt spike (branch spike/nightmare-hurt8). Imports the three
/// sheets in Assets/Spikes/NightmareHurt8 with the Loadouts sheets' settings (FullRect mesh, 512 PPU,
/// bilinear, uncompressed, bottom-centre pivot, 5 x 3 cells of 512, 15 frames) and builds
/// Assets/Spikes/NightmareHurt8/Hurt8Test.unity. Touches nothing outside that folder.</summary>
public static class Hurt8Setup
{
    const string Dir = "Assets/Spikes/NightmareHurt8";
    const int Cell = 512, Cols = 5, Rows = 3, Frames = 15;
    static readonly string[] Sheets = { "hurt8_body_forest", "hurt8_body_nightmare", "hurt8_weapon_wood", "hurt12_body_suit", "hurt12_body_forest", "hurt12_weapon_wood" };

    public static string Import()
    {
        foreach (string s in Sheets)
        {
            string path = $"{Dir}/{s}.png";
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Multiple;
            imp.spritePixelsPerUnit = Cell;
            imp.filterMode = FilterMode.Bilinear;
            imp.mipmapEnabled = false;
            imp.alphaIsTransparency = true;
            imp.maxTextureSize = 8192;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            var ts = new TextureImporterSettings(); imp.ReadTextureSettings(ts);
            ts.spriteMeshType = SpriteMeshType.FullRect; imp.SetTextureSettings(ts);
            var f = new SpriteDataProviderFactories(); f.Init();
            var dp = f.GetSpriteEditorDataProviderFromObject(imp); dp.InitSpriteEditorDataProvider();
            var rects = Enumerable.Range(0, Frames).Select(i => new SpriteRect
            {
                name = $"frame_{i:D2}", spriteID = GUID.Generate(),
                rect = new Rect(i % Cols * Cell, (Rows - 1 - i / Cols) * Cell, Cell, Cell),
                alignment = SpriteAlignment.Custom, pivot = new Vector2(0.5f, 0f),
            }).ToArray();
            dp.SetSpriteRects(rects);
            dp.GetDataProvider<ISpriteNameFileIdDataProvider>().SetNameFileIdPairs(rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)).ToList());
            dp.Apply();
            imp.SaveAndReimport();
        }
        return string.Join(", ", Sheets.Select(s => $"{s}: {Load(s).Length} sprites"));
    }

    static Sprite[] Load(string s) => AssetDatabase.LoadAllAssetRepresentationsAtPath($"{Dir}/{s}.png").OfType<Sprite>().OrderBy(x => x.name).ToArray();

    /// <summary>Hurt8Test.unity: Forest + Nightmare. HurtSuitTest.unity (suit = true): Forest + the plain suit.</summary>
    public static string BuildScene(bool suit = false)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "refused: play mode";
        for (int i = 0; i < SceneManager.sceneCount; i++) if (SceneManager.GetSceneAt(i).isDirty) return "refused: unsaved scene " + SceneManager.GetSceneAt(i).path;
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var cam = new GameObject("Main Camera").AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.105882354f, 0.11372549f, 0.16470589f, 1f);   // the Loadouts screen's background
        cam.orthographic = true; cam.orthographicSize = 0.95f;
        cam.transform.position = new Vector3(0f, 0f, -10f); cam.tag = "MainCamera";
        var t = new GameObject("Hurt8Test").AddComponent<Hurt8Test>();
        var so = new SerializedObject(t);
        void Set(string field, string sheet)
        {
            Sprite[] sp = Load(sheet); var p = so.FindProperty(field); p.arraySize = sp.Length;
            for (int i = 0; i < sp.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = sp[i];
        }
        Set("forestBody", "hurt8_body_forest"); Set("woodBow", "hurt8_weapon_wood");
        if (suit) { Set("forestBody", "hurt12_body_forest"); Set("woodBow", "hurt12_weapon_wood"); Set("suitBody", "hurt12_body_suit"); so.FindProperty("caption").stringValue = "TEST, suit 12-frame Hurt"; }
        else Set("nightmareBody", "hurt8_body_nightmare");
        so.ApplyModifiedPropertiesWithoutUndo();
        string name = suit ? "HurtSuitTest" : "Hurt8Test";
        EditorSceneManager.SaveScene(scene, $"{Dir}/{name}.unity");
        return "saved " + Dir + "/" + name + ".unity";
    }

    /// <summary>Play mode only: drives the test's clock frame-exactly and renders the game camera
    /// to PNGs (1920x1080) in outDir: seconds of footage at captureFps.</summary>
    public static string Capture(string outDir, float seconds, int captureFps)
    {
        if (!Application.isPlaying) return "refused: not in play mode";
        var t = Object.FindAnyObjectByType<Hurt8Test>(); var cam = Camera.main;
        Directory.CreateDirectory(outDir); t.manualClock = true;
        var rt = new RenderTexture(1920, 1080, 24); var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        int n = Mathf.RoundToInt(seconds * captureFps);
        for (int k = 0; k < n; k++)
        {
            t.SetClock(k / (float)captureFps);
            cam.targetTexture = rt; cam.Render(); RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); tex.Apply();
            File.WriteAllBytes(Path.Combine(outDir, $"f{k:D4}.png"), tex.EncodeToPNG());
        }
        cam.targetTexture = null; RenderTexture.active = null; t.manualClock = false;
        return $"wrote {n} frames to {outDir}";
    }
}
