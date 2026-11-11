using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>Selects EVERY character x body x weapon (each bow, then none) x action on the Loadouts
/// screen through LoadoutsController.Select and writes one line per combination to
/// <see cref="outputPath"/>: "built", "NOT BUILT" or "LOAD ERROR", then the combination's ids and
/// the controller's reason. Self-contained: run_script compiles one file, so the runner lives
/// here. Play mode must be on and the Loadouts scene running. One frame is yielded between
/// combinations so Unity can free the previous sheets.</summary>
public class WalkLoadoutCombosRunner : MonoBehaviour
{
    public string outputPath = "AgentScripts/captures/combos.txt";

    private IEnumerator Start()
    {
        var controller = FindAnyObjectByType<LoadoutsController>();
        if (controller == null || controller.CharacterCount == 0)
        {
            Debug.LogError("WalkLoadoutCombos: no LoadoutsController with characters — is the Loadouts scene playing and imported?");
            yield break;
        }

        var lines = new StringBuilder();
        int built = 0, notBuilt = 0, errors = 0;
        for (int c = 0; c < controller.CharacterCount; c++)
        {
            string characterId = controller.CharacterId(c);
            controller.SelectCharacter(c);
            for (int b = 0; b < controller.BodyCount; b++)
            {
                string bodyId = controller.BodyId(b);
                for (int w = 0; w <= controller.WeaponCount; w++)
                {
                    string weaponId = controller.WeaponId(w);
                    for (int a = 0; a < controller.AnimationCount; a++)
                    {
                        string actionId = controller.ActionId(a);
                        bool ok = controller.Select(characterId, bodyId, weaponId, actionId);
                        string status = ok ? "built" : controller.Problem.Contains("would not load") ? "LOAD ERROR" : "NOT BUILT";
                        if (ok) built++; else if (status == "LOAD ERROR") errors++; else notBuilt++;
                        lines.AppendLine($"{status,-10} {characterId} / {bodyId} / {weaponId} / {actionId}" + (ok ? "" : $"   {controller.Problem}"));
                        yield return null;
                    }
                }
            }
        }

        string summary = $"{built} built, {notBuilt} not built, {errors} load errors, {built + notBuilt + errors} combinations";
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
        File.WriteAllText(outputPath, summary + "\n\n" + lines);
        Debug.Log($"WalkLoadoutCombos: {summary} -> {outputPath}");
        Destroy(gameObject);
    }
}

public static class WalkLoadoutCombos
{
    public static string Run(string outputPath)
    {
        var runner = new GameObject("WalkLoadoutCombos").AddComponent<WalkLoadoutCombosRunner>();
        runner.outputPath = outputPath;
        return $"started walking every combination -> {outputPath}";
    }
}
