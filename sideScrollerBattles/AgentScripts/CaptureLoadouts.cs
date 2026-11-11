using System.Collections;
using System.IO;
using UnityEngine;

/// <summary>Switches the Loadouts screen to one action (the animation buttons' index) and one bow
/// choice (-1, or the "No bow" index, for no bow), then captures one full play of the action's
/// LONGEST frame-set version as a still sequence at the highest fps among the versions (one still
/// per frame, plus one to show the replay start), starting at frame 0 for every archer.
/// Self-contained: run_script compiles one file, so the runner lives here. Play mode must be on
/// and the Loadouts scene running.</summary>
public class CaptureLoadoutsRunner : MonoBehaviour
{
    public int animationIndex;
    public int weaponIndex;
    public string outputRoot = "AgentScripts/captures/loadouts";

    private IEnumerator Start()
    {
        var controller = FindAnyObjectByType<LoadoutsController>();
        if (controller == null || controller.AnimationCount == 0)
        {
            Debug.LogError("CaptureLoadouts: no LoadoutsController with animations — is the Loadouts scene playing and imported?");
            yield break;
        }
        if (animationIndex < 0 || animationIndex >= controller.AnimationCount)
        {
            Debug.LogError($"CaptureLoadouts: animation index {animationIndex} out of range 0..{controller.AnimationCount - 1}");
            yield break;
        }

        // Fixed frame time first, so the sheet-load stall below does not advance the clock; then
        // restart the clock once the new sheets are loaded so f000 is animation frame 0.
        int fps = controller.FpsOf(animationIndex);
        Time.captureFramerate = fps;
        controller.SelectAnimation(animationIndex);
        controller.SelectBow(weaponIndex);
        yield return null;
        controller.RestartClock();

        string name = $"anim{animationIndex}_{controller.WeaponId(controller.BowIndex)}";
        string dir = $"{outputRoot}/{name}";
        Directory.CreateDirectory(dir);

        int totalFrames = Mathf.CeilToInt(controller.CurrentPlayDuration * fps) + 1;
        for (int frame = 0; frame < totalFrames; frame++)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot($"{dir}/f{frame:000}.png");
        }
        Time.captureFramerate = 0;
        Debug.Log($"CaptureLoadouts: captured {totalFrames} frames at {fps} fps to {dir}");
        Destroy(gameObject);
    }
}

public static class CaptureLoadouts
{
    public static string Run(int animationIndex, int weaponIndex)
    {
        var runner = new GameObject("CaptureLoadouts").AddComponent<CaptureLoadoutsRunner>();
        runner.animationIndex = animationIndex;
        runner.weaponIndex = weaponIndex;
        return $"started capture of animation {animationIndex}, weapon index {weaponIndex}";
    }
}
