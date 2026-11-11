using System.Collections;
using UnityEngine;

public class CaptureArcherStillsRunner : MonoBehaviour
{
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

        for (int i = 0; i < catalog.Entries.Count; i++)
        {
            var entry = catalog.Entries[i];
            if (entry.character != "archer" || entry.isRotation) continue;

            indexField.SetValue(controller, i);
            showMethod.Invoke(controller, null);
            yield return null;
            yield return new WaitForEndOfFrame();

            string fileName = $"version_{entry.character}_{entry.action}_v{entry.version}.png";
            ScreenCapture.CaptureScreenshot($"AgentScripts/captures/versions_shots/{fileName}");
            yield return null;
        }
        Debug.Log("CaptureArcherStillsRunner: done");
    }
}

public static class CaptureArcherStills
{
    public static string Run()
    {
        new GameObject("CaptureArcherStillsRunner").AddComponent<CaptureArcherStillsRunner>();
        return "started";
    }
}
