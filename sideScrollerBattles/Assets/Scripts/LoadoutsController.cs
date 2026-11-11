using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Drives the Loadouts screen, a review screen for validating that every combination exists and
/// looks right. Four button rows pick, in order, the character, the armor (one body, or "All" to
/// see every body side by side), the weapon (one button per weapon in the JSON, then "No bow";
/// absent for a character with no weapons) and the animation (the union of the shown bodies'
/// frame sets' actions). One combination plays: each shown body draws from its own frame set, so
/// it maps the shared clock to a frame with its own frame count, fps and loop rule (bodies with
/// different counts drift out of phase; that is expected). A combination that is not built — the
/// body's frame set lacks the action, a sheet the JSON names is missing, or a sheet fails to load
/// — draws nothing and says so on screen ("NOT BUILT: ..."), and the buttons that lead to it are
/// dimmed. A status line names exactly what is playing and which sheet files are drawn.
///
/// Keys: 1..9 animation, [ and ] step through the weapons (wrapping; B toggles between "No bow"
/// and the last bow chosen), left/right arrows step through the armors (wrapping, "All" included),
/// C steps to the next character, Space pauses and plays, comma and period step one frame back
/// and forward (pausing first), S cycles the speed (1x, 0.5x, 0.25x). The speed row has the same
/// controls as buttons. Speed scales the shared clock only — never Time.timeScale — so a one-shot's
/// hold slows with it, and it persists across every other selection.
///
/// Everything on screen — archers, labels, buttons — is built from the catalog, so a new
/// character, body, bow or action is a JSON + PNG change, never a code change. Memory: the shown
/// bodies' sheets for the current action plus only the selected bow's sheet per frame set (loaded
/// once even when several bodies share it). Switching bow reloads just the bows; switching action,
/// armor or character reloads everything. Unity frees dropped sheets asynchronously, so for a
/// moment after a switch the old sheets can still be in memory too.
///
/// Loading sheets by asset path is editor-only: this is a review screen, not shipped, and the
/// scene is deliberately not in Build Settings.
/// </summary>
public class LoadoutsController : MonoBehaviour
{
    [SerializeField] private LoadoutsCatalog catalog;
    [Tooltip("Which character's loadouts to show first; empty means the first one in the catalog.")]
    [SerializeField] private string character = "";

    // World layout, one sprite cell = one unit. Archers are packed by where their pixels actually
    // fall, not by the cell box, so cell boxes may overlap while the drawn figures never do. The
    // drawn extent is the one the importer measured (the union of every body and weapon sheet's
    // tight sprite bounds, over every animation, so nothing moves when the bow or animation
    // changes); this is the fallback for a catalog imported before extents were recorded.
    private static readonly Rect FallbackExtent = Rect.MinMaxRect(-0.31f, 0.17f, 0.35f, 0.94f);
    private const float ArcherGap = 0.12f;      // world units kept clear between neighbouring archers
    private const float SideMargin = 0.15f;     // world units beside the outermost figures

    // Fractions of the view height kept clear for the title, status and body labels (top) and the
    // four control rows + hint (bottom); the figures fill what is left between them.
    private const float TopReserve = 0.19f;
    private const float BottomReserve = 0.30f;
    private const float LabelCentre = 0.165f;    // body labels' centre, as a fraction down from the top
    private const float NotAvailableOffset = 0.05f; // the "NOT BUILT" line sits this much of the view height below it

    // Canvas pixels (1920x1080 reference), bottom-anchored. Rows from the bottom: animation,
    // weapon, armor, character.
    private const float ButtonHeight = 48f;
    private const float FirstRowY = 64f;
    private const float RowPitch = 54f;
    private const float RowCaptionWidth = 150f;
    private const float ButtonGap = 12f;
    private const float MaxButtonWidth = 340f;
    private const float ControlsWidth = 1700f;  // widest the control rows may be

    private static readonly float[] SpeedChoices = { 1f, 0.5f, 0.25f };

    private const string NoBowLabel = "No bow";
    private const string NoWeaponLabel = "no weapon";   // for a character with no weapons at all
    private const string AllLabel = "All";
    private const string NotBuiltLabel = "NOT BUILT";
    private const string MissingSuffix = " (n/a)";

    private static readonly Color ButtonColor = new Color(1f, 1f, 1f, 0.15f);
    private static readonly Color ButtonActiveColor = new Color(0.25f, 0.5f, 1f, 0.85f);
    private static readonly Color ButtonMissingColor = new Color(1f, 1f, 1f, 0.05f);
    private static readonly Color LabelColor = Color.white;
    private static readonly Color LabelMissingColor = new Color(1f, 1f, 1f, 0.4f);
    private static readonly Color NotBuiltColor = new Color(1f, 0.45f, 0.4f, 1f);

    private class Cell
    {
        public LoadoutPlayer player;
        public int body;
        public int frameSet;    // index into the set's frameSets; -1 if the body names none
        public Text naLabel;    // "NOT BUILT" under the body's label while its combination is not built
        public Sprite[] bodyFrames;
        public string bodyError;    // set when the body's sheet is named but would not load
    }

    private struct ActionInfo
    {
        public string action;
        public string label;
    }

    private struct WorldLabel
    {
        public RectTransform rect;
        public Vector3 world;
    }

    private readonly List<LoadoutSet> _sets = new List<LoadoutSet>();   // characters with something to show
    private LoadoutSet _set;
    private int _characterIndex;
    private bool _hasSet;
    private readonly List<Cell> _cells = new List<Cell>();   // the shown bodies only
    private readonly List<ActionInfo> _actions = new List<ActionInfo>();   // union over frame sets, first-seen order
    private readonly List<int> _usedFrameSets = new List<int>();           // frame sets some shown body draws from
    private readonly List<WorldLabel> _worldLabels = new List<WorldLabel>();
    private readonly List<GameObject> _archerObjects = new List<GameObject>(); // archers and their labels, rebuilt per armor choice
    private readonly List<GameObject> _rowObjects = new List<GameObject>();    // captions and buttons, rebuilt per character
    private readonly List<Image> _characterButtons = new List<Image>();
    private readonly List<Image> _bodyButtons = new List<Image>();
    private readonly List<Text> _bodyButtonLabels = new List<Text>();
    private readonly List<Image> _bowButtons = new List<Image>();
    private readonly List<Text> _bowButtonLabels = new List<Text>();
    private readonly List<Image> _animationButtons = new List<Image>();
    private readonly List<Text> _animationButtonLabels = new List<Text>();
    private readonly List<Text> _characterButtonLabels = new List<Text>();
    private readonly List<Image> _speedButtons = new List<Image>();   // pause/play, frame back, frame forward, then SpeedChoices
    private readonly List<Text> _speedButtonLabels = new List<Text>();

    private Camera _camera;
    private RectTransform _canvasRect;
    private Font _font;
    private Text _title;
    private Text _subtitle;
    private Text _banner;
    private Text _hint;
    private float _halfWidthNeeded;
    private float _baseHalfHeight;
    private float _labelY;
    private int _screenWidth;
    private int _screenHeight;

    private int _animationIndex = -1;
    private int _bodyIndex;     // 0..BodyCount-1 shows that body alone; BodyCount (AllBodiesIndex) shows every body
    private int _bowIndex;      // 0..WeaponCount-1 is a bow; WeaponCount is "No bow"
    private int _lastBowIndex;  // what B restores after "No bow"
    private readonly Dictionary<int, Sprite[]> _weaponFrames = new Dictionary<int, Sprite[]>(); // by frame set
    private float _speed = 1f;  // how fast the clock runs against real time
    private bool _paused;
    private string _statusTail = "";    // the status lines that do not change while playing
    private string _lastStatus = "";
    private double _clock;  // a double so it never loses a frame's precision; each archer wraps it by its own period

    /// <summary>How many actions the buttons list (the union across frame sets).</summary>
    public int AnimationCount => _hasSet ? _actions.Count : 0;
    public int AnimationIndex => _animationIndex;
    public int WeaponCount => _hasSet ? _set.weapons.Length : 0;
    /// <summary>The bow choice that means no bow — one past the last weapon.</summary>
    public int NoBowIndex => WeaponCount;
    public int BowIndex => _bowIndex;
    public int CharacterCount => _sets.Count;
    public int CharacterIndex => _characterIndex;
    /// <summary>How many bodies the selected character has (not counting "All").</summary>
    public int BodyCount => _hasSet ? _set.bodies.Length : 0;
    /// <summary>The armor choice that shows every body side by side — one past the last body.</summary>
    public int AllBodiesIndex => BodyCount;
    public int BodyIndex => _bodyIndex;

    /// <summary>How fast the shared clock runs: 1 is real time.</summary>
    public float Speed => _speed;
    public bool Paused => _paused;
    /// <summary>How many archers are shown (one, or one per body for "All").</summary>
    public int ShownCount => _cells.Count;
    /// <summary>The 0-based frame index shown archer <paramref name="shownIndex"/> is on, as in the sheet's
    /// cell numbering; -1 when there is none.</summary>
    public int FrameOf(int shownIndex) => shownIndex >= 0 && shownIndex < _cells.Count ? _cells[shownIndex].player.Frame : -1;
    /// <summary>The first shown archer's frame; -1 when none is shown.</summary>
    public int CurrentFrame => FrameOf(0);

    /// <summary>True when the combination on screen is built: at least one body shown and every
    /// shown body draws it.</summary>
    public bool IsBuilt => _cells.Count > 0 && _cells.TrueForAll(c => CellProblem(c) == null);

    /// <summary>Why the combination on screen is not built, one line per shown body that fails;
    /// empty when it is built.</summary>
    public string Problem => string.Join("; ", ProblemLines());

    /// <summary>Seconds one full play of the current action takes in its LONGEST frame-set version
    /// (loop: its frames; one-shot: plus the hold) — what a capture needs to cover.</summary>
    public float CurrentPlayDuration
    {
        get
        {
            float longest = 0f;
            if (_animationIndex < 0) return longest;
            foreach (int f in _usedFrameSets)
            {
                if (!TryFindAnimation(f, _animationIndex, out LoadoutAnimation a)) continue;
                longest = Mathf.Max(longest, LoadoutPlayer.PlayDuration(a.frameCount, a.fps, a.loop));
            }
            return longest;
        }
    }

    /// <summary>The highest fps among the action's frame-set versions, so a capture at that rate
    /// shows every frame of every version.</summary>
    public int FpsOf(int animationIndex)
    {
        int fps = 1;
        foreach (int f in _usedFrameSets)
        {
            if (TryFindAnimation(f, animationIndex, out LoadoutAnimation a)) fps = Mathf.Max(fps, a.fps);
        }
        return fps;
    }

    /// <summary>The weapon's id, or "nobow" for the no-bow choice — used to name capture folders.</summary>
    public string WeaponId(int bowIndex) => bowIndex >= 0 && bowIndex < WeaponCount ? _set.weapons[bowIndex].id : "nobow";

    /// <summary>The selected character's id ("archer"); the id of character <paramref name="index"/>.</summary>
    public string CharacterId(int index) => index >= 0 && index < _sets.Count ? _sets[index].character : "";

    /// <summary>The id of body <paramref name="index"/> of the selected character, or "all" for
    /// <see cref="AllBodiesIndex"/>.</summary>
    public string BodyId(int index) => index >= 0 && index < BodyCount ? _set.bodies[index].id : "all";

    /// <summary>The id of the action behind animation button <paramref name="index"/>.</summary>
    public string ActionId(int index) => index >= 0 && index < _actions.Count ? _actions[index].action : "";

    private void Start()
    {
        _camera = Camera.main;
        _canvasRect = GetComponent<RectTransform>();
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        CollectSets();
        if (_sets.Count == 0)
        {
            MakeText("Message", "No loadouts imported — run Characters/Import Loadouts", 36, TextAnchor.MiddleCenter,
                Vector2.zero, new Vector2(1200f, 80f));
            return;
        }

        BuildTitle();
        int first = _sets.FindIndex(s => s.character == character);
        ShowCharacter(first >= 0 ? first : 0, null);
    }

    private void Update()
    {
        if (!_hasSet) return;

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            Key[] digitKeys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9 };
            for (int i = 0; i < digitKeys.Length && i < AnimationCount; i++)
            {
                if (keyboard[digitKeys[i]].wasPressedThisFrame) SelectAnimation(i);
            }
            if (keyboard.leftBracketKey.wasPressedThisFrame) StepBow(-1);
            if (keyboard.rightBracketKey.wasPressedThisFrame) StepBow(1);
            if (keyboard.bKey.wasPressedThisFrame) ToggleBow();
            if (keyboard.leftArrowKey.wasPressedThisFrame) StepBody(-1);
            if (keyboard.rightArrowKey.wasPressedThisFrame) StepBody(1);
            if (keyboard.cKey.wasPressedThisFrame) StepCharacter(1);
            if (keyboard.spaceKey.wasPressedThisFrame) SetPaused(!_paused);
            if (keyboard.commaKey.wasPressedThisFrame) StepFrame(-1);
            if (keyboard.periodKey.wasPressedThisFrame) StepFrame(1);
            if (keyboard.sKey.wasPressedThisFrame) SetSpeed(SpeedChoices[(System.Array.IndexOf(SpeedChoices, _speed) + 1) % SpeedChoices.Length]);
        }

        // One clock for everyone; each archer sees it wrapped by its own period (play length, plus
        // the hold for a one-shot), which LoadoutPlayer maps to a frame modulo that same period.
        // The clock is in animation seconds (as at 1x), so speed scales the one-shot hold too.
        if (!_paused) _clock += Time.deltaTime * _speed;
        foreach (Cell cell in _cells) cell.player.Evaluate(ClockFor(cell.player));
        RefreshLiveStatus();
    }

    private void LateUpdate()
    {
        if (!_hasSet) return;
        if (Screen.width != _screenWidth || Screen.height != _screenHeight)
        {
            FitCamera();
            PlaceWorldLabels();
        }
    }

    /// <summary>Switches to character <paramref name="index"/>: rebuilds the rows and the archers
    /// from its data, showing its first body alone with its first weapon, and keeps the current
    /// action when the new character has it (else its first).</summary>
    public void SelectCharacter(int index)
    {
        if (!_hasSet || index < 0 || index >= _sets.Count) return;
        ShowCharacter(index, ActionId(_animationIndex));
    }

    /// <summary>Steps to the previous/next character, wrapping.</summary>
    public void StepCharacter(int delta)
    {
        if (_sets.Count == 0) return;
        SelectCharacter(((_characterIndex + delta) % _sets.Count + _sets.Count) % _sets.Count);
    }

    /// <summary>Shows body <paramref name="bodyIndex"/> alone, centred and as large as the layout
    /// allows, or every body side by side for <see cref="AllBodiesIndex"/>. Keeps the action and
    /// bow. Out of range does nothing.</summary>
    public void SelectBody(int bodyIndex)
    {
        if (!_hasSet || bodyIndex < 0 || bodyIndex > BodyCount) return;
        _bodyIndex = bodyIndex;
        RebuildArchers();
        SelectAnimation(Mathf.Max(0, _animationIndex));
    }

    /// <summary>Steps to the previous/next armor, wrapping, "All" included.</summary>
    public void StepBody(int delta)
    {
        int count = BodyCount + 1;
        SelectBody(((_bodyIndex + delta) % count + count) % count);
    }

    /// <summary>Selects one combination by id, for automation: the character's id, a body id (or
    /// "all"), a weapon id (or "nobow" / empty for none), an action id. Returns true when the
    /// combination is built — every shown body draws it with every sheet loaded — and false when
    /// it is not built or an id is unknown. A not-built combination is still selected so its
    /// "NOT BUILT" screen can be captured; an unknown id changes nothing, except that the
    /// character may already have been switched to.</summary>
    public bool Select(string characterId, string bodyId, string weaponId, string actionId)
    {
        if (!_hasSet) return false;
        int c = _sets.FindIndex(s => s.character == characterId);
        if (c < 0) return false;
        if (c != _characterIndex) ShowCharacter(c, null);

        int body = bodyId == "all" ? AllBodiesIndex : System.Array.FindIndex(_set.bodies, b => b.id == bodyId);
        int bow = string.IsNullOrEmpty(weaponId) || weaponId == "nobow" ? NoBowIndex : System.Array.FindIndex(_set.weapons, w => w.id == weaponId);
        int action = _actions.FindIndex(a => a.action == actionId);
        if (body < 0 || bow < 0 || action < 0) return false;

        bool bodyChanged = body != _bodyIndex;
        _bodyIndex = body;
        _bowIndex = bow;
        if (bow < WeaponCount) _lastBowIndex = bow;
        if (bodyChanged) RebuildArchers();
        SelectAnimation(action);
        return IsBuilt;
    }

    /// <summary>Switches every shown archer to action <paramref name="index"/> (an index into the
    /// action buttons), each playing its own frame set's version, restarting the shared clock.
    /// Reloads every sheet (bodies and the selected bow).</summary>
    public void SelectAnimation(int index)
    {
        if (!_hasSet || index < 0 || index >= _actions.Count) return;

        // Drop every reference to the old sheets and ask Unity to free them. UnloadUnusedAssets is
        // asynchronous, so the old sheets are not gone before the new ones load below — peak memory
        // is about two actions' sheets, not one.
        foreach (Cell cell in _cells)
        {
            cell.player.Clear();
            cell.bodyFrames = null;
            cell.bodyError = null;
        }
        _weaponFrames.Clear();
        Resources.UnloadUnusedAssets();

        _animationIndex = index;
        LoadWeaponFrames();
        foreach (Cell cell in _cells)
        {
            if (!TryFindAnimation(cell.frameSet, index, out LoadoutAnimation animation)) continue;
            string path = BodySheet(animation, cell.body);
            if (string.IsNullOrEmpty(path)) continue; // reported by CellProblem
            Sprite[] frames = LoadSheet(path);
            if (SheetComplete(frames, animation.frameCount)) cell.bodyFrames = frames;
            else cell.bodyError = $"{path} would not load ({(frames == null ? 0 : frames.Length)} of {animation.frameCount} frames)";
        }
        ApplyCells(false);
        _clock = 0;

        RefreshButtons();
        RefreshStatus();
    }

    /// <summary>Picks the bow every shown archer holds: 0..WeaponCount-1, or <see cref="NoBowIndex"/>
    /// (any out-of-range value, such as -1) for no bow. Only the bows' sheets are reloaded; the
    /// bodies and the clock carry on. A bow whose sheet is missing for a body's frame set makes
    /// that body "NOT BUILT".</summary>
    public void SelectBow(int bowIndex)
    {
        if (!_hasSet) return;
        _bowIndex = bowIndex >= 0 && bowIndex < WeaponCount ? bowIndex : NoBowIndex;
        if (_bowIndex < WeaponCount) _lastBowIndex = _bowIndex;
        if (_animationIndex < 0) return;

        foreach (Cell cell in _cells) cell.player.SetWeaponFrames(null);
        bool hadBow = _weaponFrames.Count > 0;
        _weaponFrames.Clear();
        if (hadBow) Resources.UnloadUnusedAssets();

        LoadWeaponFrames();
        ApplyCells(true);
        RefreshButtons();
        RefreshStatus();
    }

    /// <summary>Steps to the previous/next bow choice, wrapping, "No bow" included.</summary>
    public void StepBow(int delta)
    {
        int count = WeaponCount + 1;
        SelectBow(((_bowIndex + delta) % count + count) % count);
    }

    /// <summary>Toggles between "No bow" and the last bow chosen.</summary>
    public void ToggleBow() => SelectBow(_bowIndex == NoBowIndex ? _lastBowIndex : NoBowIndex);

    /// <summary>Sets how fast the shared clock runs (1 = real time, 0.25 = a quarter); the screen's
    /// buttons offer 1, 0.5 and 0.25. Keeps the current frame.</summary>
    public void SetSpeed(float speed)
    {
        _speed = Mathf.Max(0.01f, speed);
        RefreshButtons();
        RefreshLiveStatus();
    }

    /// <summary>Freezes or resumes the shared clock; paused, the shown frame stays put.</summary>
    public void SetPaused(bool paused)
    {
        _paused = paused;
        RefreshButtons();
        RefreshLiveStatus();
    }

    /// <summary>Pauses and moves one frame back (-1) or forward (+1), landing exactly on a frame
    /// boundary of the highest-fps version shown and wrapping around the longest play. Archers with
    /// a lower fps hold their frame on the ticks between theirs.</summary>
    public void StepFrame(int delta)
    {
        if (!_hasSet || _animationIndex < 0 || _cells.Count == 0 || CurrentPlayDuration <= 0f) return;
        _paused = true;
        int fps = FpsOf(_animationIndex);
        int ticks = Mathf.Max(1, Mathf.RoundToInt(CurrentPlayDuration * fps));
        int tick = Mathf.FloorToInt((float)(_clock % CurrentPlayDuration) * fps + 0.001f);
        tick = ((tick + delta) % ticks + ticks) % ticks;
        _clock = tick / (double)fps;
        foreach (Cell cell in _cells) cell.player.Evaluate(ClockFor(cell.player));
        RefreshButtons();
        RefreshLiveStatus();
    }

    /// <summary>Restarts the shared clock at animation frame 0 and shows that frame immediately —
    /// for a capture that must begin exactly at the start, after the sheets have loaded.</summary>
    public void RestartClock()
    {
        _clock = 0;
        foreach (Cell cell in _cells) cell.player.Evaluate(0f);
    }

    private float ClockFor(LoadoutPlayer player)
    {
        float period = player.Period;
        return period > 0f ? (float)(_clock % period) : 0f;
    }

    private static string BodySheet(LoadoutAnimation animation, int body) =>
        body >= 0 && body < animation.bodySheets.Length ? animation.bodySheets[body] : "";

    private static string WeaponSheet(LoadoutAnimation animation, int bowIndex) =>
        bowIndex >= 0 && bowIndex < animation.weaponSheets.Length ? animation.weaponSheets[bowIndex] : "";

    /// <summary>The frame set's version of the action at <paramref name="actionIndex"/>; false when
    /// the frame set has none (or the index is -1).</summary>
    private bool TryFindAnimation(int frameSet, int actionIndex, out LoadoutAnimation found)
    {
        found = default;
        if (frameSet < 0 || actionIndex < 0 || actionIndex >= _actions.Count) return false;
        string action = _actions[actionIndex].action;
        foreach (LoadoutAnimation a in _set.frameSets[frameSet].animations)
        {
            if (a.action != action) continue;
            found = a;
            return true;
        }
        return false;
    }

    /// <summary>Why body <paramref name="body"/> cannot draw the action with the bow, judged from
    /// the catalog alone (nothing is loaded); null when every sheet it needs is named and present.</summary>
    private string StaticProblem(int body, int actionIndex, int bow)
    {
        int frameSet = FrameSetIndex(_set.bodies[body].frameSet);
        if (frameSet < 0) return "the body names no frame set";
        if (!TryFindAnimation(frameSet, actionIndex, out LoadoutAnimation a))
            return $"the {_set.frameSets[frameSet].id} frame set has no {_actions[actionIndex].label} animation";
        if (string.IsNullOrEmpty(BodySheet(a, body))) return "the body sheet is missing or not sliced to the frame count";
        if (bow < WeaponCount && string.IsNullOrEmpty(WeaponSheet(a, bow))) return "the weapon sheet is missing or not sliced to the frame count";
        return null;
    }

    /// <summary>Why a shown body is not built for the current action and bow — from the catalog,
    /// then from what actually loaded; null when it is built.</summary>
    private string CellProblem(Cell cell)
    {
        string problem = StaticProblem(cell.body, _animationIndex, _bowIndex);
        if (problem != null) return problem;
        if (cell.bodyError != null) return cell.bodyError;
        if (_bowIndex < WeaponCount && !_weaponFrames.ContainsKey(cell.frameSet))
        {
            TryFindAnimation(cell.frameSet, _animationIndex, out LoadoutAnimation a);
            return $"{WeaponSheet(a, _bowIndex)} would not load";
        }
        return null;
    }

    private IEnumerable<string> ProblemLines()
    {
        foreach (Cell cell in _cells)
        {
            string problem = CellProblem(cell);
            if (problem == null) continue;
            yield return _cells.Count > 1 ? $"{_set.bodies[cell.body].label}: {problem}" : problem;
        }
    }

    /// <summary>True when the sheet loaded with every one of its frames.</summary>
    private static bool SheetComplete(Sprite[] frames, int frameCount)
    {
        if (frames == null || frames.Length < frameCount) return false;
        for (int i = 0; i < frameCount; i++)
        {
            if (frames[i] == null) return false;
        }
        return true;
    }

    /// <summary>Hands each shown archer its frames for the current action and bow — or nothing,
    /// with "NOT BUILT" under its label, when its combination is not built. <paramref name="keepClock"/>
    /// shows the frame the shared clock is already at instead of frame 0.</summary>
    private void ApplyCells(bool keepClock)
    {
        foreach (Cell cell in _cells)
        {
            bool built = CellProblem(cell) == null;
            if (built && TryFindAnimation(cell.frameSet, _animationIndex, out LoadoutAnimation a))
            {
                _weaponFrames.TryGetValue(cell.frameSet, out Sprite[] weapon);
                cell.player.Play(cell.bodyFrames, weapon, a.frameCount, a.fps, a.loop);
            }
            else
            {
                cell.player.Play(null, null, 0, 1, false); // not built: draw nothing
            }
            cell.naLabel.enabled = !built;
            if (keepClock) cell.player.Evaluate(ClockFor(cell.player));
        }
    }

    /// <summary>Loads the selected bow's sheet once per shown frame set that has the current
    /// action — shared by every body in that frame set. A missing or incomplete sheet, or "No
    /// bow", leaves no entry.</summary>
    private void LoadWeaponFrames()
    {
        foreach (int f in _usedFrameSets)
        {
            if (!TryFindAnimation(f, _animationIndex, out LoadoutAnimation animation)) continue;
            Sprite[] frames = LoadSheet(WeaponSheet(animation, _bowIndex));
            if (SheetComplete(frames, animation.frameCount)) _weaponFrames[f] = frames;
        }
    }

    /// <summary>The catalog's characters that have bodies and at least one action.</summary>
    private void CollectSets()
    {
        _sets.Clear();
        if (catalog == null) return;
        foreach (LoadoutSet candidate in catalog.Sets)
        {
            if (candidate.bodies == null || candidate.bodies.Length == 0 || candidate.frameSets == null) continue;
            _set = candidate;
            CollectActions();
            if (_actions.Count > 0) _sets.Add(candidate);
        }
        _actions.Clear();
    }

    /// <summary>The union of every frame set's actions, first-seen order, labelled by the first
    /// frame set that has each.</summary>
    private void CollectActions()
    {
        _actions.Clear();
        if (_set.frameSets == null) return;
        foreach (LoadoutFrameSet frameSet in _set.frameSets)
        {
            foreach (LoadoutAnimation a in frameSet.animations)
            {
                if (_actions.Exists(x => x.action == a.action)) continue;
                _actions.Add(new ActionInfo { action = a.action, label = a.label });
            }
        }
    }

    private int FrameSetIndex(string id)
    {
        for (int i = 0; i < _set.frameSets.Length; i++)
        {
            if (_set.frameSets[i].id == id) return i;
        }
        return -1;
    }

    /// <summary>Makes <paramref name="index"/> the character on screen: its rows, then its first
    /// body alone with its first weapon, playing <paramref name="actionId"/> if it has it (else its
    /// first action).</summary>
    private void ShowCharacter(int index, string actionId)
    {
        _characterIndex = index;
        _set = _sets[index];
        CollectActions();
        _hasSet = true;
        _bodyIndex = 0;
        _bowIndex = 0; // the first bow, or "No bow" when the JSON lists none (WeaponCount is then 0)
        _lastBowIndex = 0;
        _animationIndex = -1;

        BuildControls();
        RebuildArchers();
        SelectAnimation(Mathf.Max(0, _actions.FindIndex(a => a.action == actionId)));
    }

    /// <summary>Replaces the archers with those of the shown bodies (the selected body, or all of
    /// them), refits the camera and re-places the labels. Leaves nothing playing until the next
    /// <see cref="SelectAnimation"/>.</summary>
    private void RebuildArchers()
    {
        foreach (GameObject go in _archerObjects) Destroy(go);
        _archerObjects.Clear();
        _worldLabels.Clear();
        _cells.Clear();
        _usedFrameSets.Clear();
        _weaponFrames.Clear();
        BuildArchers();
        FitCamera();
        PlaceWorldLabels();
    }

    /// <summary>One archer per shown body in a single row, each with its body label above. The
    /// view is sized so the figures' drawn height fills the band between the labels and the
    /// controls (or, with many bodies, so the whole row fits across).</summary>
    private void BuildArchers()
    {
        var shown = new List<int>();
        if (_bodyIndex < _set.bodies.Length) shown.Add(_bodyIndex);
        else for (int i = 0; i < _set.bodies.Length; i++) shown.Add(i);

        int count = shown.Count;
        Rect extent = MeasureDrawnExtent(); // one archer's, in cell units from its bottom-centre pivot
        float pitchX = extent.width + ArcherGap;
        float drawnHeight = extent.height;
        _baseHalfHeight = drawnHeight / (2f * (1f - TopReserve - BottomReserve));
        float bandCentre = _baseHalfHeight * ((1f - 2f * TopReserve) + (1f - 2f * (1f - BottomReserve))) / 2f;
        float feetY = bandCentre + drawnHeight / 2f - extent.yMax; // cell bottom; the figure hangs from its top edge
        _labelY = _baseHalfHeight * (1f - 2f * LabelCentre);
        float naY = _baseHalfHeight * (1f - 2f * (LabelCentre + NotAvailableOffset));

        for (int k = 0; k < count; k++)
        {
            int body = shown[k];
            float x = (k - (count - 1) / 2f) * pitchX;
            var go = new GameObject($"Archer_{_set.bodies[body].id}");
            go.transform.SetParent(transform.parent, false);
            go.transform.position = new Vector3(x, feetY, 0f);
            _archerObjects.Add(go);

            int frameSet = FrameSetIndex(_set.bodies[body].frameSet);
            if (frameSet >= 0 && !_usedFrameSets.Contains(frameSet)) _usedFrameSets.Add(frameSet);

            AddWorldLabel(_set.bodies[body].label, 38, TextAnchor.MiddleCenter, new Vector2(420f, 56f),
                new Vector3(x + extent.center.x, _labelY, 0f), out _);
            AddWorldLabel(NotBuiltLabel, 28, TextAnchor.MiddleCenter, new Vector2(420f, 40f),
                new Vector3(x + extent.center.x, naY, 0f), out Text na);
            na.color = NotBuiltColor;
            na.enabled = false;
            _cells.Add(new Cell { player = go.AddComponent<LoadoutPlayer>(), body = body, frameSet = frameSet, naLabel = na });
        }

        _halfWidthNeeded = (count - 1) * pitchX / 2f + Mathf.Max(-extent.xMin, extent.xMax) + SideMargin;
    }

    /// <summary>Union of every measured body and weapon extent across all frame sets and actions,
    /// so the layout is the same whichever bow or action is showing. Falls back to
    /// <see cref="FallbackExtent"/> when the catalog carries none.</summary>
    private Rect MeasureDrawnExtent()
    {
        bool any = false;
        Rect union = default;
        foreach (LoadoutFrameSet frameSet in _set.frameSets)
        {
            foreach (LoadoutAnimation animation in frameSet.animations)
            {
                foreach (Rect[] extents in new[] { animation.bodyExtents, animation.weaponExtents })
                {
                    if (extents == null) continue;
                    foreach (Rect r in extents)
                    {
                        if (r.width <= 0f || r.height <= 0f) continue;
                        union = any ? Rect.MinMaxRect(Mathf.Min(union.xMin, r.xMin), Mathf.Min(union.yMin, r.yMin),
                            Mathf.Max(union.xMax, r.xMax), Mathf.Max(union.yMax, r.yMax)) : r;
                        any = true;
                    }
                }
            }
        }
        return any ? union : FallbackExtent;
    }

    /// <summary>The headline (what is playing), the status lines under it (frames, fps, loop rule,
    /// sheet files) and the "NOT BUILT" banner that replaces an empty stage.</summary>
    private void BuildTitle()
    {
        _title = MakeText("Title", "", 40, TextAnchor.UpperCenter, new Vector2(0f, -8f), new Vector2(1880f, 50f), topAnchored: true);
        _title.resizeTextForBestFit = true;
        _title.resizeTextMinSize = 24;
        _title.resizeTextMaxSize = 40;
        _subtitle = MakeText("Subtitle", "", 22, TextAnchor.UpperCenter, new Vector2(0f, -60f), new Vector2(1880f, 84f), topAnchored: true);
        _subtitle.color = new Color(1f, 1f, 1f, 0.65f);

        _banner = MakeText("NotBuiltBanner", "", 54, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(1700f, 170f));
        _banner.color = NotBuiltColor;
        float bandCentreFromBottom = 1f - (TopReserve + (1f - BottomReserve)) / 2f;
        _banner.rectTransform.anchorMin = _banner.rectTransform.anchorMax = new Vector2(0.5f, bandCentreFromBottom);
        _banner.enabled = false;

        _hint = MakeText("Hint", "", 22, TextAnchor.LowerCenter, new Vector2(0f, 8f), new Vector2(1880f, 34f));
        _hint.color = new Color(1f, 1f, 1f, 0.55f);
    }

    /// <summary>Four captioned rows, left-aligned to the same edge, bottom to top: animation,
    /// weapon (one button per weapon, then "No bow"; left out when the character has none), armor
    /// (one per body, then "All") and character. Button width comes from the longest row.</summary>
    private void BuildControls()
    {
        foreach (GameObject go in _rowObjects) Destroy(go);
        _rowObjects.Clear();
        foreach (List<Image> images in new[] { _characterButtons, _bodyButtons, _bowButtons, _animationButtons, _speedButtons }) images.Clear();
        foreach (List<Text> labels in new[] { _characterButtonLabels, _bodyButtonLabels, _bowButtonLabels, _animationButtonLabels, _speedButtonLabels }) labels.Clear();

        int animationCount = _actions.Count;
        int bowCount = _set.weapons.Length > 0 ? _set.weapons.Length + 1 : 0;
        int bodyCount = _set.bodies.Length + 1;
        int widest = Mathf.Max(Mathf.Max(animationCount, bowCount), Mathf.Max(bodyCount, _sets.Count));
        float width = Mathf.Min(MaxButtonWidth, (ControlsWidth - RowCaptionWidth - ButtonGap - (widest - 1) * ButtonGap) / widest);
        float left = -(RowCaptionWidth + ButtonGap + widest * width + (widest - 1) * ButtonGap) / 2f;

        BuildRow("Speed", FirstRowY, left, width, 3 + SpeedChoices.Length,
            i => i == 0 ? PauseLabel : i == 1 ? "< frame" : i == 2 ? "frame >" : $"{SpeedChoices[i - 3]}x",
            i => () => { if (i == 0) SetPaused(!_paused); else if (i < 3) StepFrame(i == 1 ? -1 : 1); else SetSpeed(SpeedChoices[i - 3]); },
            _speedButtons, _speedButtonLabels);
        BuildRow("Animation", FirstRowY + RowPitch, left, width, animationCount,
            i => _actions[i].label, i => () => SelectAnimation(i), _animationButtons, _animationButtonLabels);
        if (bowCount > 0)
        {
            BuildRow("Weapon", FirstRowY + 2 * RowPitch, left, width, bowCount,
                i => i < _set.weapons.Length ? _set.weapons[i].label : NoBowLabel, i => () => SelectBow(i), _bowButtons, _bowButtonLabels);
        }
        BuildRow("Armor", FirstRowY + 3 * RowPitch, left, width, bodyCount,
            i => i < _set.bodies.Length ? _set.bodies[i].label : AllLabel, i => () => SelectBody(i), _bodyButtons, _bodyButtonLabels);
        BuildRow("Character", FirstRowY + 4 * RowPitch, left, width, _sets.Count,
            i => Capitalize(_sets[i].character), i => () => SelectCharacter(i), _characterButtons, _characterButtonLabels);

        _hint.text = $"keys: 1-{Mathf.Min(animationCount, 9)} animation" + (bowCount > 0 ? " · [ ] weapon · B bow on/off" : "") +
            " · ← → armor · C character · Space pause · , . frame · S speed";
    }

    private void BuildRow(string caption, float y, float left, float width, int count,
        System.Func<int, string> label, System.Func<int, UnityEngine.Events.UnityAction> onClick,
        List<Image> images, List<Text> labels)
    {
        Text captionText = MakeText($"{caption}Caption", caption, 26, TextAnchor.MiddleLeft,
            new Vector2(left + RowCaptionWidth / 2f, y), new Vector2(RowCaptionWidth, ButtonHeight), bottomAnchored: true);
        captionText.color = new Color(1f, 1f, 1f, 0.65f);
        _rowObjects.Add(captionText.gameObject);

        float x = left + RowCaptionWidth + ButtonGap + width / 2f;
        for (int i = 0; i < count; i++)
        {
            Image button = MakeButton($"{caption}{i}Button", label(i), new Vector2(x, y), new Vector2(width, ButtonHeight), onClick(i), out Text text);
            images.Add(button);
            labels.Add(text);
            _rowObjects.Add(button.gameObject);
            x += width + ButtonGap;
        }
    }

    /// <summary>How many of the bodies <paramref name="bodyChoice"/> shows (one body, or all for
    /// <see cref="AllBodiesIndex"/>) can draw the action with the bow, going by the catalog alone;
    /// <paramref name="total"/> is how many it shows.</summary>
    private int CountBuilt(int bodyChoice, int actionIndex, int bow, out int total)
    {
        int built = 0;
        total = 0;
        for (int body = 0; body < _set.bodies.Length; body++)
        {
            if (bodyChoice < _set.bodies.Length && bodyChoice != body) continue;
            total++;
            if (StaticProblem(body, actionIndex, bow) == null) built++;
        }
        return built;
    }

    /// <summary>Highlights the selected button of each row, and dims the buttons that lead to a
    /// combination with nothing built: an action no shown body can draw, an armor that cannot draw
    /// the current action with the current bow. A weapon button is dimmed "(n/a)" when no shown body
    /// can draw the current action with it, or "(some n/a)" when only some can; all stay clickable —
    /// the stage then says what is missing.</summary>
    private void RefreshButtons()
    {
        for (int i = 0; i < _characterButtons.Count; i++)
        {
            _characterButtons[i].color = i == _characterIndex ? ButtonActiveColor : ButtonColor;
        }
        for (int i = 0; i < _bodyButtons.Count; i++)
        {
            bool missing = CountBuilt(i, _animationIndex, _bowIndex, out _) == 0;
            _bodyButtons[i].color = i == _bodyIndex ? ButtonActiveColor : (missing ? ButtonMissingColor : ButtonColor);
            _bodyButtonLabels[i].color = missing ? LabelMissingColor : LabelColor;
        }
        for (int i = 0; i < _bowButtons.Count; i++)
        {
            int built = CountBuilt(_bodyIndex, _animationIndex, i, out int total);
            bool missing = built == 0;
            bool partly = built > 0 && built < total;
            _bowButtons[i].color = i == _bowIndex ? ButtonActiveColor : (missing ? ButtonMissingColor : ButtonColor);
            _bowButtonLabels[i].color = missing ? LabelMissingColor : LabelColor;
            _bowButtonLabels[i].text = (i < _set.weapons.Length ? _set.weapons[i].label : NoBowLabel) + (missing ? MissingSuffix : partly ? " (some n/a)" : "");
        }
        for (int i = 0; i < _animationButtons.Count; i++)
        {
            bool missing = CountBuilt(_bodyIndex, i, _bowIndex, out _) == 0;
            _animationButtons[i].color = i == _animationIndex ? ButtonActiveColor : (missing ? ButtonMissingColor : ButtonColor);
            _animationButtonLabels[i].color = missing ? LabelMissingColor : LabelColor;
        }
        if (_speedButtons.Count == 3 + SpeedChoices.Length)
        {
            _speedButtons[0].color = _paused ? ButtonActiveColor : ButtonColor;
            _speedButtonLabels[0].text = PauseLabel;
            for (int i = 0; i < SpeedChoices.Length; i++)
            {
                _speedButtons[3 + i].color = Mathf.Approximately(SpeedChoices[i], _speed) ? ButtonActiveColor : ButtonColor;
            }
        }
    }

    private string PauseLabel => _paused ? "Play" : "Pause";

    private string WeaponLabel() =>
        _bowIndex < WeaponCount ? _set.weapons[_bowIndex].label : (WeaponCount > 0 ? NoBowLabel : NoWeaponLabel);

    /// <summary>Writes what is on screen: the headline (character · armor · weapon · animation), the
    /// frame count, fps and loop rule of each frame set drawn, the sheet files drawn, and — when no
    /// shown body is built — the "NOT BUILT: ..." banner with the reasons.</summary>
    private void RefreshStatus()
    {
        string armor = _bodyIndex < BodyCount ? _set.bodies[_bodyIndex].label : "All armors";
        string animationLabel = _actions[_animationIndex].label;
        _title.text = $"{Capitalize(_set.character)} · {armor} · {WeaponLabel()} · {animationLabel}";

        var files = new List<string>();
        foreach (Cell cell in _cells)
        {
            if (CellProblem(cell) != null || !TryFindAnimation(cell.frameSet, _animationIndex, out LoadoutAnimation a)) continue;
            foreach (string path in new[] { BodySheet(a, cell.body), WeaponSheet(a, _bowIndex) })
            {
                string file = System.IO.Path.GetFileName(path);
                if (!string.IsNullOrEmpty(file) && !files.Contains(file)) files.Add(file);
            }
        }

        var lines = new List<string>();
        if (files.Count > 0) lines.Add($"files: {string.Join(", ", files)}");
        List<string> problems = new List<string>(ProblemLines());
        if (problems.Count > 0) lines.Add($"not built: {string.Join("; ", problems)}");
        _statusTail = string.Join("\n", lines);
        RefreshLiveStatus();

        bool built = _cells.Exists(c => CellProblem(c) == null);
        _banner.enabled = !built;
        _banner.text = $"{NotBuiltLabel}: {Capitalize(_set.character)} / {armor} / {WeaponLabel()} / {animationLabel}" +
            $"\n<size=28>{string.Join("; ", problems)}</size>";
    }

    /// <summary>The status line that changes while playing: per distinct frame set drawn, the frame
    /// it is on (0-based, as in the sheet's cell numbering), its frame count, fps and loop rule,
    /// then the speed when not 1x and "PAUSED". Rewrites the subtitle only when the text changed.</summary>
    private void RefreshLiveStatus()
    {
        if (!_hasSet || _animationIndex < 0) return;

        var versions = new List<string>();
        foreach (Cell cell in _cells)
        {
            if (CellProblem(cell) != null || !TryFindAnimation(cell.frameSet, _animationIndex, out LoadoutAnimation a)) continue;
            string version = $"frame {cell.player.Frame} of 0..{a.frameCount - 1} · {a.frameCount} frames · {a.fps} fps · " +
                (a.loop ? "loops" : $"once, holds {LoadoutPlayer.HoldSeconds:0.#}s, repeats");
            if (_cells.Count > 1) version = $"{_set.frameSets[cell.frameSet].id}: {version}";
            if (!versions.Contains(version)) versions.Add(version);
        }

        var lines = new List<string>();
        if (versions.Count > 0)
        {
            string line = string.Join("  |  ", versions);
            if (!Mathf.Approximately(_speed, 1f)) line += $"  ·  {_speed}x";
            if (_paused) line += "  ·  PAUSED";
            lines.Add(line);
        }
        if (_statusTail.Length > 0) lines.Add(_statusTail);
        string status = string.Join("\n", lines);
        if (status == _lastStatus) return;
        _lastStatus = status;
        _subtitle.text = status;
    }

    private void AddWorldLabel(string text, int fontSize, TextAnchor alignment, Vector2 size, Vector3 world, out Text label)
    {
        label = MakeText($"Label_{text}", text, fontSize, alignment, Vector2.zero, size);
        _archerObjects.Add(label.gameObject);
        _worldLabels.Add(new WorldLabel { rect = label.rectTransform, world = world });
    }

    private void FitCamera()
    {
        _screenWidth = Screen.width;
        _screenHeight = Screen.height;
        float aspect = _screenHeight > 0 ? (float)_screenWidth / _screenHeight : 1f;

        // Tall enough for the figures plus title/controls room; a narrower screen grows the view
        // so the whole row of archers still fits across.
        _camera.orthographic = true;
        _camera.orthographicSize = Mathf.Max(_baseHalfHeight, _halfWidthNeeded / aspect);
        _camera.transform.position = new Vector3(0f, 0f, -10f);
    }

    /// <summary>Anchors each label at its world position, converted to canvas space, so the text
    /// stays glued to its archer at any screen size.</summary>
    private void PlaceWorldLabels()
    {
        foreach (WorldLabel label in _worldLabels)
        {
            Vector2 screen = _camera.WorldToScreenPoint(label.world);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screen, null, out Vector2 local);
            label.rect.anchoredPosition = local;
        }
    }

    private Text MakeText(string name, string content, int fontSize, TextAnchor alignment, Vector2 anchoredPos, Vector2 size,
        bool topAnchored = false, bool bottomAnchored = false)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var text = go.AddComponent<Text>();
        text.font = _font;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = Color.white;
        text.text = content;
        text.raycastTarget = false;
        var rect = text.rectTransform;
        // Top- or bottom-anchored when told to (or for a lower alignment), else centred.
        float anchorY = topAnchored ? 1f : (bottomAnchored || alignment == TextAnchor.LowerCenter ? 0f : 0.5f);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, anchorY);
        rect.pivot = new Vector2(0.5f, bottomAnchored ? 0.5f : anchorY);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = size;
        return text;
    }

    private Image MakeButton(string name, string label, Vector2 anchoredPos, Vector2 size, UnityEngine.Events.UnityAction onClick,
        out Text labelText)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var image = go.AddComponent<Image>();
        image.color = ButtonColor;
        var button = go.AddComponent<Button>();
        button.onClick.AddListener(onClick);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = size;

        var textGo = new GameObject("Text");
        textGo.transform.SetParent(go.transform, false);
        labelText = textGo.AddComponent<Text>();
        labelText.font = _font;
        labelText.fontSize = 28;
        labelText.resizeTextForBestFit = true; // long labels or many buttons shrink instead of clipping
        labelText.resizeTextMinSize = 16;
        labelText.resizeTextMaxSize = 28;
        labelText.alignment = TextAnchor.MiddleCenter;
        labelText.color = LabelColor;
        labelText.text = label;
        labelText.raycastTarget = false;
        var textRect = labelText.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(6f, 0f);
        textRect.offsetMax = new Vector2(-6f, 0f);
        return image;
    }

    /// <summary>Loads one sheet's sprites in frame order (frame_00, frame_01, ...). An empty path —
    /// a sheet the importer found missing, or the no-bow choice — gives null.</summary>
    private Sprite[] LoadSheet(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;

        var frames = new List<Sprite>();
#if UNITY_EDITOR
        foreach (Object asset in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (asset is Sprite sprite && sprite.name.StartsWith("frame_")
                && int.TryParse(sprite.name.Substring("frame_".Length), out int frameIndex))
            {
                while (frames.Count <= frameIndex) frames.Add(null);
                frames[frameIndex] = sprite;
            }
        }
#else
        Debug.LogError("LoadoutsController: sheets can only be loaded in the editor.");
#endif
        return frames.ToArray();
    }

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
}
