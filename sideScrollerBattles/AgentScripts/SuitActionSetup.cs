using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>TEST setup for the plain suit through every action (branch spike/nightmare-hurt8). For one action it imports
/// Assets/Spikes/SuitActions/{action}_s_body_forest / _s_body_suit / _s_weapon_wood (cells of 512, `cols` wide, `frames` cells,
/// the Loadouts sheets' settings) and builds Assets/Spikes/SuitActions/{action}_SuitTest.unity: Forest Robes beside the suit,
/// the wood bow on both, on the Hurt8Test player (12 fps, holds baked). Capture with Hurt8Setup.Capture.</summary>
public static class SuitActionSetup
{
    const string Dir = "Assets/Spikes/SuitActions";
    const int Cell = 512;
    static string[] Sheets(string a) => new[] { $"{a}_s_body_forest", $"{a}_s_body_suit", $"{a}_s_weapon_wood", $"{a}_s_weapon_suit" }.Where(x => System.IO.File.Exists($"{Dir}/{x}.png")).ToArray();

    public static string Import(string action, int frames, int cols)
    {
        AssetDatabase.Refresh(); int rows = (frames + cols - 1) / cols; var said = new System.Collections.Generic.List<string>();
        foreach (string s in Sheets(action))
        {
            string path = $"{Dir}/{s}.png";
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) { said.Add($"{s}: missing"); continue; }
            imp.textureType = TextureImporterType.Sprite; imp.spriteImportMode = SpriteImportMode.Multiple; imp.spritePixelsPerUnit = Cell;
            imp.filterMode = FilterMode.Bilinear; imp.mipmapEnabled = false; imp.alphaIsTransparency = true; imp.maxTextureSize = 8192;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            var ts = new TextureImporterSettings(); imp.ReadTextureSettings(ts); ts.spriteMeshType = SpriteMeshType.FullRect; imp.SetTextureSettings(ts);
            var f = new SpriteDataProviderFactories(); f.Init();
            var dp = f.GetSpriteEditorDataProviderFromObject(imp); dp.InitSpriteEditorDataProvider();
            var rects = Enumerable.Range(0, frames).Select(i => new SpriteRect
            {
                name = $"frame_{i:D3}", spriteID = GUID.Generate(),
                rect = new Rect(i % cols * Cell, (rows - 1 - i / cols) * Cell, Cell, Cell),
                alignment = SpriteAlignment.Custom, pivot = new Vector2(0.5f, 0f),
            }).ToArray();
            dp.SetSpriteRects(rects);
            dp.GetDataProvider<ISpriteNameFileIdDataProvider>().SetNameFileIdPairs(rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)).ToList());
            dp.Apply(); imp.SaveAndReimport(); said.Add($"{s}: {Load(s).Length} sprites");
        }
        return string.Join(", ", said);
    }

    public static string BuildScene(string action, bool loop)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "refused: play mode";
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var cam = new GameObject("Main Camera").AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.105882354f, 0.11372549f, 0.16470589f, 1f);
        cam.orthographic = true; cam.orthographicSize = 0.95f; cam.transform.position = new Vector3(0f, 0f, -10f); cam.tag = "MainCamera";
        var t = new GameObject("Hurt8Test").AddComponent<Hurt8Test>(); var so = new SerializedObject(t);
        void Set(string field, string sheet)
        {
            Sprite[] sp = Load(sheet); var p = so.FindProperty(field); p.arraySize = sp.Length;
            for (int i = 0; i < sp.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = sp[i];
        }
        Set("forestBody", $"{action}_s_body_forest"); Set("suitBody", $"{action}_s_body_suit"); Set("woodBow", $"{action}_s_weapon_wood"); Set("suitBow", $"{action}_s_weapon_suit");
        so.FindProperty("caption").stringValue = $"TEST, suit {action}"; so.FindProperty("loop").boolValue = loop;
        so.ApplyModifiedPropertiesWithoutUndo();
        string path = $"{Dir}/{action}_SuitTest.unity"; EditorSceneManager.SaveScene(scene, path); return "saved " + path;
    }

    static Sprite[] Load(string s) => AssetDatabase.LoadAllAssetRepresentationsAtPath($"{Dir}/{s}.png").OfType<Sprite>().OrderBy(x => x.name).ToArray();
}
