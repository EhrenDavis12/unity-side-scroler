using System.Collections;
using UnityEngine;

public class CaptureArcherRotationSeqRunner : MonoBehaviour
{
    public string outputDir;
    public float durationSeconds;

    private IEnumerator Start()
    {
        var rotationPlayer = FindAnyObjectByType<IdleRotationPlayer>();
        // Short debug interval so both fidgets trigger comfortably within the capture window —
        // see IdleRotationPlayer.SetFidgetIntervalForDebug.
        rotationPlayer.SetFidgetIntervalForDebug(2f, 3f);

        var controller = FindAnyObjectByType<AnimationVersionsController>();
        var catalogField = typeof(AnimationVersionsController).GetField("catalog",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var catalog = (AnimationVersionsCatalog)catalogField.GetValue(controller);
        var indexField = typeof(AnimationVersionsController).GetField("_index",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var showMethod = typeof(AnimationVersionsController).GetMethod("Show",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        int archerRotationIndex = -1;
        for (int i = 0; i < catalog.Entries.Count; i++)
        {
            if (catalog.Entries[i].character == "archer" && catalog.Entries[i].isRotation) { archerRotationIndex = i; break; }
        }
        indexField.SetValue(controller, archerRotationIndex);
        showMethod.Invoke(controller, null);
        yield return null;

        Time.captureFramerate = 50;
        float frameSeconds = 1f / 50f;
        int totalFrames = Mathf.RoundToInt(durationSeconds / frameSeconds);
        for (int frame = 0; frame < totalFrames; frame++)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot($"{outputDir}/f{frame:0000}.png");
        }
        Debug.Log($"CaptureArcherRotationSeqRunner: captured {totalFrames} frames to {outputDir}");
    }
}

public static class CaptureArcherRotationSeq
{
    public static string Run()
    {
        var go = new GameObject("CaptureArcherRotationSeq");
        var runner = go.AddComponent<CaptureArcherRotationSeqRunner>();
        runner.outputDir = "AgentScripts/captures/versions_shots/seq_archer_rotation";
        runner.durationSeconds = 18f;
        return "started";
    }
}
