using UnityEngine;

/// <summary>TEST speed control for the suit scenes: keys 1 = full speed, 2 = quarter speed, 3 = pause; the same as buttons
/// top-left. IMGUI, so it needs no input package.</summary>
public class SuitSpeedKeys : MonoBehaviour
{
    private void OnGUI()
    {
        var e = Event.current;
        if (e.type == EventType.KeyDown)
        {
            if (e.keyCode == KeyCode.Alpha1 || e.keyCode == KeyCode.Keypad1) Time.timeScale = 1f;
            if (e.keyCode == KeyCode.Alpha2 || e.keyCode == KeyCode.Keypad2) Time.timeScale = 0.25f;
            if (e.keyCode == KeyCode.Alpha3 || e.keyCode == KeyCode.Keypad3) Time.timeScale = Time.timeScale == 0f ? 1f : 0f;
        }
        GUI.skin.button.fontSize = 22; GUI.skin.label.fontSize = 22;
        GUILayout.BeginArea(new Rect(10, 10, 560, 50)); GUILayout.BeginHorizontal();
        if (GUILayout.Button("1  Full", GUILayout.Height(40))) Time.timeScale = 1f;
        if (GUILayout.Button("2  Quarter", GUILayout.Height(40))) Time.timeScale = 0.25f;
        if (GUILayout.Button("3  Pause", GUILayout.Height(40))) Time.timeScale = Time.timeScale == 0f ? 1f : 0f;
        GUILayout.Label($"speed {Time.timeScale:0.##}x", GUILayout.Height(40));
        GUILayout.EndHorizontal(); GUILayout.EndArea();
    }
}
