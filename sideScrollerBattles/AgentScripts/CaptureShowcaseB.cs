using System.Collections;
using UnityEngine;

public class ShowcaseBCaptureRunner : MonoBehaviour
{
    public string outputDir;
    public int totalFrames;
    public float attackStartTime;
    public float frameSeconds;

    private IEnumerator Start()
    {
        var switcher = FindAnyObjectByType<ShowcaseSwitcher>();
        switcher.Toggle(); // showcase A is the default on Start(); flip to B.

        Time.captureFramerate = 50;
        var animator = FindAnyObjectByType<Animator>();
        float t = 0f;
        bool triggered = false;
        for (int frame = 0; frame < totalFrames; frame++)
        {
            if (!triggered && t >= attackStartTime - 0.0001f)
            {
                animator.SetTrigger("Attack");
                triggered = true;
            }
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot($"{outputDir}/f{frame:000}.png");
            t += frameSeconds;
        }
        Debug.Log($"ShowcaseBCaptureRunner: captured {totalFrames} frames to {outputDir}");
    }
}

public static class CaptureShowcaseB
{
    public static string Run()
    {
        var go = new GameObject("ShowcaseBCapture");
        var runner = go.AddComponent<ShowcaseBCaptureRunner>();
        runner.outputDir = "AgentScripts/captures/unity_3d";
        runner.frameSeconds = 1f / 50f;
        runner.totalFrames = Mathf.RoundToInt((0.3f + 2.94f + 0.3f) / runner.frameSeconds);
        runner.attackStartTime = 0.3f;
        return "capture started";
    }
}
