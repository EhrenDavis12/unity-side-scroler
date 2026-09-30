using System.Collections;
using UnityEngine;

public class ShowcaseACaptureRunner : MonoBehaviour
{
    public string outputDir;
    public int totalFrames;
    public float attackStartTime;
    public float frameSeconds;

    private IEnumerator Start()
    {
        Time.captureFramerate = 50;
        var attack = FindFirstObjectByType<SkinAttack>();
        float t = 0f;
        bool triggered = false;
        for (int frame = 0; frame < totalFrames; frame++)
        {
            if (!triggered && t >= attackStartTime - 0.0001f)
            {
                attack.Play();
                triggered = true;
            }
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot($"{outputDir}/f{frame:000}.png");
            t += frameSeconds;
        }
        Debug.Log($"ShowcaseACaptureRunner: captured {totalFrames} frames to {outputDir}");
    }
}

public static class CaptureShowcaseA
{
    public static string Run()
    {
        var go = new GameObject("ShowcaseACapture");
        var runner = go.AddComponent<ShowcaseACaptureRunner>();
        runner.outputDir = "AgentScripts/captures/unity_skin";
        runner.frameSeconds = 1f / 50f;
        runner.totalFrames = Mathf.RoundToInt((0.3f + 1.32f + 0.3f) / runner.frameSeconds);
        runner.attackStartTime = 0.3f;
        return "capture started";
    }
}
