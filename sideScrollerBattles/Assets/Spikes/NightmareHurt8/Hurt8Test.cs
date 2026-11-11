using UnityEngine;

/// <summary>
/// TEST scene for the Hurt spikes (branch spike/nightmare-hurt8): Forest Robes beside the Nightmare
/// Cloak and/or the plain suit (each body array left empty is skipped), side by side, each a <see cref="LoadoutPlayer"/> (body + the same wood-bow
/// sheet) on one shared clock. The holds from timing.json are baked into the sheets (15 cells,
/// 12 fps), so it is a plain one-shot: the player plays it, holds the last frame for
/// LoadoutPlayer.HoldSeconds (the short rest) and replays. Background and scale are the
/// Loadouts screen's: its dark clear colour, one 512 px cell = one world unit.
/// </summary>
public class Hurt8Test : MonoBehaviour
{
    public Sprite[] forestBody;
    public Sprite[] nightmareBody;
    public Sprite[] suitBody;
    public string caption = "TEST, 12-frame Hurt";
    public Sprite[] woodBow;
    public Sprite[] suitBow;            // optional: the bow re-pinned to the suit's own fist (else woodBow)
    public Sprite[] nightmareBow;       // optional: the bow pinned to the cloak's own fist (else woodBow)
    public int fps = 12;
    public bool loop;                   // a looping action (idle, run): wraps with no rest hold
    public float pitch = 1.1f;          // world units between the two archers
    public float figureCentre = 0.55f;  // world units from the cell's bottom pivot to the figure's middle

    /// <summary>When true Update leaves the clock alone and a capture drives it with SetClock.</summary>
    public bool manualClock;

    private readonly System.Collections.Generic.List<LoadoutPlayer> _players = new System.Collections.Generic.List<LoadoutPlayer>();
    private float _clock;

    private void Start()
    {
        var panels = new System.Collections.Generic.List<(string, Sprite[])> { ("Forest Robes", forestBody) };
        if (nightmareBody != null && nightmareBody.Length > 0) panels.Add(("Nightmare Cloak", nightmareBody));
        if (suitBody != null && suitBody.Length > 0) panels.Add(("Plain suit", suitBody));
        for (int i = 0; i < panels.Count; i++)
            _players.Add(MakeArcher(panels[i].Item1, (i - (panels.Count - 1) / 2f) * pitch, panels[i].Item2));
        SetClock(0f);
    }

    private LoadoutPlayer MakeArcher(string label, float x, Sprite[] body)
    {
        var go = new GameObject(label);
        go.transform.SetParent(transform, false);   // the overview grid offsets each pair by its own position
        go.transform.localPosition = new Vector3(x, -figureCentre, 0f);
        var player = go.AddComponent<LoadoutPlayer>();
        Sprite[] own = body == suitBody ? suitBow : body == nightmareBody ? nightmareBow : null;
        var bow = own != null && own.Length > 0 ? own : woodBow;
        player.Play(body, bow != null && bow.Length > 0 ? bow : null, body.Length, fps, loop);

        var text = new GameObject($"Label {label}").AddComponent<TextMesh>();
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.font = font;
        text.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        text.text = label + "  ·  " + caption;
        text.fontSize = 48;
        text.characterSize = 0.012f;
        text.anchor = TextAnchor.MiddleCenter;
        text.color = new Color(0.85f, 0.86f, 0.9f);
        text.transform.SetParent(transform, false);
        text.transform.localPosition = new Vector3(x, 0.82f, 0f);
        return player;
    }

    public void SetClock(float seconds)
    {
        _clock = seconds;
        foreach (var p in _players) p.Evaluate(_clock);
    }

    private void Update()
    {
        if (!manualClock) SetClock(_clock + Time.deltaTime);
    }
}
