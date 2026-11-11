using System.Collections;
using UnityEngine;

/// <summary>Plays archer fidget_glance v2 on the Animation Versions screen and captures one full
/// play (161 frames at 24 fps = 6.7 s) plus the replay start, at 50 capture fps. Self-contained:
/// run_script compiles one file, so the runner is duplicated here rather than shared.</summary>
public class CaptureArcherGlanceV3Runner : MonoBehaviour
{
    public string action = "fidget_glance";
    public int version = 3;
    public string outputDir = "AgentScripts/captures/versions_shots/seq_archer_glance_v3";
    public float durationSeconds = 3.5f;

    private IEnumerator Start()
    {
        var controller = FindAnyObjectByType<AnimationVersionsController>();
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var catalog = (AnimationVersionsCatalog)typeof(AnimationVersionsController).GetField("catalog", flags).GetValue(controller);
        var indexField = typeof(AnimationVersionsController).GetField("_index", flags);
        var showMethod = typeof(AnimationVersionsController).GetMethod("Show", flags);

        int index = -1;
        for (int i = 0; i < catalog.Entries.Count; i++)
            if (catalog.Entries[i].character == "archer" && catalog.Entries[i].action == action && catalog.Entries[i].version == version) { index = i; break; }
        if (index < 0) { Debug.LogError($"CaptureArcherGlanceV3: no entry for archer {action} v{version}"); yield break; }
        indexField.SetValue(controller, index);
        showMethod.Invoke(controller, null);
        yield return null;

        Time.captureFramerate = 50;
        int totalFrames = Mathf.RoundToInt(durationSeconds * 50f);
        for (int frame = 0; frame < totalFrames; frame++)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot($"{outputDir}/f{frame:000}.png");
        }
        Debug.Log($"CaptureArcherGlanceV3: captured {totalFrames} frames to {outputDir}");
    }
}

public static class CaptureArcherGlanceV3
{
    public static string Run()
    {
        new GameObject("CaptureArcherGlanceV3").AddComponent<CaptureArcherGlanceV3Runner>();
        return "started archer fidget_glance v2";
    }
}
