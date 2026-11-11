using System.Collections;
using System.Linq;
using UnityEngine;

public class CaptureVersionSequenceRunner : MonoBehaviour
{
    public string action;
    public int version;
    public string outputDir;
    public float durationSeconds;

    private IEnumerator Start()
    {
        var controller = FindAnyObjectByType<AnimationVersionsController>();
        var catalogField = typeof(AnimationVersionsController).GetField("catalog",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var catalog = (AnimationVersionsCatalog)catalogField.GetValue(controller);
        var indexField = typeof(AnimationVersionsController).GetField("_index",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var showMethod = typeof(AnimationVersionsController).GetMethod("Show",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        int index = -1;
        for (int i = 0; i < catalog.Entries.Count; i++)
        {
            if (catalog.Entries[i].action == action && catalog.Entries[i].version == version) { index = i; break; }
        }
        if (index < 0)
        {
            Debug.LogError($"CaptureVersionSequenceRunner: no entry for {action} v{version}");
            yield break;
        }
        indexField.SetValue(controller, index);
        showMethod.Invoke(controller, null);
        yield return null;

        Time.captureFramerate = 50;
        float frameSeconds = 1f / 50f;
        int totalFrames = Mathf.RoundToInt(durationSeconds / frameSeconds);
        for (int frame = 0; frame < totalFrames; frame++)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot($"{outputDir}/f{frame:000}.png");
        }
        Debug.Log($"CaptureVersionSequenceRunner: captured {totalFrames} frames for {action} v{version} to {outputDir}");
    }
}

public static class CaptureVersionSequence
{
    public static string RunIdleV5()
    {
        var go = new GameObject("CaptureIdleV5");
        var runner = go.AddComponent<CaptureVersionSequenceRunner>();
        runner.action = "idle";
        runner.version = 5;
        runner.outputDir = "AgentScripts/captures/versions_shots/seq_idle_v5";
        runner.durationSeconds = 2f; // a couple of loop cycles at fps 32
        return "started idle v5";
    }

    public static string RunAttackV1()
    {
        var go = new GameObject("CaptureAttackV1");
        var runner = go.AddComponent<CaptureVersionSequenceRunner>();
        runner.action = "attack";
        runner.version = 1;
        runner.outputDir = "AgentScripts/captures/versions_shots/seq_attack_v1";
        runner.durationSeconds = 3.5f; // one full 49-frame @16fps play (~3.06s) + hold + replay start
        return "started attack v1";
    }
}
