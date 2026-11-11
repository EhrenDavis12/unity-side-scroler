using System.Collections;
using System.IO;
using UnityEngine;

/// <summary>Selects one combination on the Loadouts screen by ids (a body id of "all" shows every
/// body; a weapon id of "nobow" no bow), lets it play for <see cref="seconds"/>, and saves one
/// screenshot to <see cref="outputPath"/>. Self-contained: run_script compiles one file, so the
/// runner lives here. Play mode must be on and the Loadouts scene running.</summary>
public class CaptureLoadoutStillRunner : MonoBehaviour
{
    public string character, body, weapon, action, outputPath;
    public float seconds = 1f;

    private IEnumerator Start()
    {
        var controller = FindAnyObjectByType<LoadoutsController>();
        if (controller == null)
        {
            Debug.LogError("CaptureLoadoutStill: no LoadoutsController — is the Loadouts scene playing?");
            yield break;
        }

        // Fixed frame time so the sheet-load stall does not advance the clock.
        Time.captureFramerate = 16;
        bool built = controller.Select(character, body, weapon, action);
        yield return null;
        controller.RestartClock();
        for (float t = 0f; t < seconds; t += 1f / 16f) yield return null;
        yield return new WaitForEndOfFrame();
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
        ScreenCapture.CaptureScreenshot(outputPath);
        yield return null;
        Time.captureFramerate = 0;
        Debug.Log($"CaptureLoadoutStill: {character}/{body}/{weapon}/{action} built={built} -> {outputPath}");
        Destroy(gameObject);
    }
}

public static class CaptureLoadoutStill
{
    public static string Run(string character, string body, string weapon, string action, string outputPath, float seconds)
    {
        var runner = new GameObject("CaptureLoadoutStill").AddComponent<CaptureLoadoutStillRunner>();
        runner.character = character;
        runner.body = body;
        runner.weapon = weapon;
        runner.action = action;
        runner.outputPath = outputPath;
        runner.seconds = seconds;
        return $"started still of {character}/{body}/{weapon}/{action}";
    }
}
