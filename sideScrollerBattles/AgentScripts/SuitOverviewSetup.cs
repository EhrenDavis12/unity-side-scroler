using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>TEST overview (branch spike/nightmare-hurt8): every plain-suit action at once, Forest Robes beside the suit with the
/// bow, in a labelled 5 x 2 grid, each pair its own Hurt8Test on its own clock, plus SuitSpeedKeys. Uses the sheets already
/// imported in Assets/Spikes (SuitActions and NightmareHurt8); it imports nothing. Saves Assets/Spikes/SuitActions/AllSuitActions.unity.</summary>
public static class SuitOverviewSetup
{
    static readonly (string action, string ver, bool loop)[] Acts =
    {
        ("idle", "v1", true), ("idle_headturn", "v1", false), ("idle_shift", "v2", false), ("attack", "v3", false), ("defend", "v2", false),
        ("run", "v2", true), ("run_start", "v2", false), ("run_stop", "v3", false), ("death", "v2", false), ("hurt", "v2", false),
    };

    public static string BuildScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "refused: play mode";
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var cam = new GameObject("Main Camera").AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.105882354f, 0.11372549f, 0.16470589f, 1f);
        cam.orthographic = true; cam.orthographicSize = 3.4f; cam.transform.position = new Vector3(0f, 0f, -10f); cam.tag = "MainCamera";
        new GameObject("Speed keys").AddComponent<SuitSpeedKeys>();
        const float dx = 2.35f, dy = 2.0f; var said = new System.Collections.Generic.List<string>();
        for (int i = 0; i < Acts.Length; i++)
        {
            var (a, v, loop) = Acts[i];
            var t = new GameObject($"{a} {v}").AddComponent<Hurt8Test>();
            t.transform.position = new Vector3((i % 5 - 2) * dx, (i < 5 ? 1 : -1) * dy / 2f, 0f);
            var so = new SerializedObject(t);
            int n = 0;
            void Set(string field, string dir, string sheet)
            {
                var sp = AssetDatabase.LoadAllAssetRepresentationsAtPath($"{dir}/{sheet}.png").OfType<Sprite>().OrderBy(x => x.name).ToArray();
                var p = so.FindProperty(field); p.arraySize = sp.Length; if (field == "suitBody") n = sp.Length;
                for (int k = 0; k < sp.Length; k++) p.GetArrayElementAtIndex(k).objectReferenceValue = sp[k];
            }
            if (a == "hurt")
            {
                const string d = "Assets/Spikes/NightmareHurt8";
                Set("forestBody", d, "hurt12_body_forest"); Set("suitBody", d, "hurt12_body_suit"); Set("woodBow", d, "hurt12_weapon_wood");
            }
            else
            {
                const string d = "Assets/Spikes/SuitActions";
                Set("forestBody", d, $"{a}_s_body_forest"); Set("suitBody", d, $"{a}_s_body_suit"); Set("woodBow", d, $"{a}_s_weapon_wood"); Set("suitBow", d, $"{a}_s_weapon_suit");
            }
            so.FindProperty("caption").stringValue = $"{a} {v}"; so.FindProperty("loop").boolValue = loop;
            so.ApplyModifiedPropertiesWithoutUndo(); said.Add($"{a}:{n}");
            var title = new GameObject($"Title {a}").AddComponent<TextMesh>(); var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            title.font = font; title.GetComponent<MeshRenderer>().sharedMaterial = font.material; title.text = $"{a}  {v}";
            title.fontSize = 64; title.characterSize = 0.03f; title.anchor = TextAnchor.MiddleCenter; title.color = new Color(1f, 0.85f, 0.4f);
            title.transform.SetParent(t.transform, false); title.transform.localPosition = new Vector3(0f, 1.02f, 0f);
        }
        const string path = "Assets/Spikes/SuitActions/AllSuitActions.unity";
        EditorSceneManager.SaveScene(scene, path); return "saved " + path + " " + string.Join(" ", said);
    }
}
