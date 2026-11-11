using System.Collections;
using UnityEngine;

public class CaptureRotationSeqRunner : MonoBehaviour
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
        var indexField = typeof(AnimationVersionsController).GetField("_index",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        indexField.SetValue(controller, 0); // "Idle rotation" is pinned to the top of the catalog.
        var showMethod = typeof(AnimationVersionsController).GetMethod("Show",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
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
        Debug.Log($"CaptureRotationSeqRunner: captured {totalFrames} frames to {outputDir}");
    }
}

public static class CaptureRotationSeq
{
    public static string Run()
    {
        var go = new GameObject("CaptureRotationSeq");
        var runner = go.AddComponent<CaptureRotationSeqRunner>();
        runner.outputDir = "AgentScripts/captures/versions_shots/seq_rotation";
        runner.durationSeconds = 18f;
        return "started";
    }
}
