using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Plays a code-driven attack on a RigCharacter from attackKeysJson (see
/// Assets/Characters/knight_rig/attack_keys.json) — no Animator clips. The JSON's "keys" are a
/// sparse list of {t seconds, name, angles{slot: deg}, offset{slot: [dx, dy]}, scale{slot: [sx,
/// sy]}}; a slot missing from a key's channel means rest for that channel (0 degrees, no offset,
/// 1x scale). Every slot named anywhere, in any channel, is discovered from the JSON itself —
/// nothing here hardcodes a slot name — so a new key touching a slot or channel never seen before
/// drops in with no code change. Angles are local Z rotations in degrees about each slot's own
/// joint — positive is counter-clockwise on screen, which is already Unity's own +Z rotation
/// sense for a 2D sprite under the default camera, so no sign flip is needed. Offsets are canvas
/// pixels, y down (like rig.json), added to the slot's rest local position; scale multiplies only
/// the slot's own visual child (see RigCharacter.SlotVisual), never inherited by whatever is
/// parented under it.
///
/// Two interpolation modes, chosen by the JSON's top-level "interp":
/// - "monotone_cubic" (current format) — each channel of each slot (an angle is one component;
///   offset and scale are two each) is its own Fritsch-Carlson monotone cubic Hermite spline over
///   every key's t, matching export3/_reference_smooth.py exactly (see ComputeMonotoneTangents /
///   Hermite). Tangents are computed once per channel at parse time, never per frame. The JSON's
///   optional top-level "lag" ({slot: seconds}) samples that slot's curves at
///   clamp(t - lag[slot], 0, length) instead of t — positive lag samples the past (the slot reads
///   as behind/lagging), negative samples ahead (leading) — for overlapping, staggered motion
///   with only the one interpolation shared everywhere else.
/// - absent (older files) — the original piecewise-lerp-plus-named-easing scheme: "ease_into"
///   names, per destination key, one of "in" (cubic ease-in), "out" (cubic ease-out), "inout"
///   (smoothstep), or "linear".
/// Parsed once, in Awake, from the TextAsset — never re-parsed per frame (except by Play(), which
/// re-parses so tuning the JSON while already in Play mode takes effect on the next trigger).
///
/// RigIdle also drives the torso's position/visual-scale and the head/plume/upper-arm/pauldron
/// rotations. For any slot RigIdle drives, this is the sole writer of its transform while playing
/// (see <see cref="Animates"/> / <see cref="ApplyPose"/>), adding RigIdle's own current
/// contribution (<see cref="RigIdle.IdleAngle"/> etc.) to this attack's before writing — so
/// neither ever silently discards the other's contribution for the same transform.
///
/// During the swing_sort_window named in the JSON, every slot's draw order switches to rig.json's
/// "swing_order" (e.g. so the sword arm draws over the shield mid-swing), then back to normal.
/// </summary>
[RequireComponent(typeof(RigCharacter))]
public class RigAttack : MonoBehaviour
{
    private const float PixelsPerUnit = 256f; // must match RigCharacterImporter's constant

    [SerializeField] private TextAsset attackKeysJson;
    [Tooltip("The RigIdle to blend out/in around this attack, and to read idle contributions from for a shared slot — auto-found on this GameObject when left empty.")]
    [SerializeField] private RigIdle idle;

    private RigCharacter _rig;

    private List<Key> _keys;
    private Dictionary<string, string> _easeInto;
    private bool _hasSwingWindow;
    private float _swingWindowStart, _swingWindowEnd;
    private float _totalLength;
    private readonly HashSet<string> _allAnimatedSlots = new HashSet<string>();

    // "monotone_cubic" mode only, below. Each of these three per-channel slot sets is discovered
    // independently (a slot can have an angle curve but no offset curve, say) — matching
    // export3/_reference_smooth.py's Clip.ch, which is likewise keyed by (channel, slot).
    private bool _useMonotoneCubic;
    private readonly HashSet<string> _angleSlots = new HashSet<string>();
    private readonly HashSet<string> _offsetSlots = new HashSet<string>();
    private readonly HashSet<string> _scaleSlots = new HashSet<string>();
    private readonly Dictionary<string, Curve> _angleCurves = new Dictionary<string, Curve>();
    private readonly Dictionary<string, Curve[]> _offsetCurves = new Dictionary<string, Curve[]>();
    private readonly Dictionary<string, Curve[]> _scaleCurves = new Dictionary<string, Curve[]>();
    private readonly Dictionary<string, float> _lag = new Dictionary<string, float>();

    private readonly Dictionary<string, float> _currentAngles = new Dictionary<string, float>();
    private readonly Dictionary<string, Vector2> _currentOffsets = new Dictionary<string, Vector2>();
    private readonly Dictionary<string, Vector2> _currentScales = new Dictionary<string, Vector2>();

    private bool _playing;
    private float _playTime;

    private class Key
    {
        public float t;
        public string name;
        public Dictionary<string, float> angles;
        public Dictionary<string, Vector2> offsets;
        public Dictionary<string, Vector2> scales;
    }

    /// <summary>One component's monotone cubic Hermite spline over the shared key-time axis —
    /// values[k] is this component's value at _keys[k].t (rest-defaulted for a key that didn't
    /// specify this slot/channel), tangents[k] from ComputeMonotoneTangents.</summary>
    private class Curve
    {
        public float[] t;
        public float[] values;
        public float[] tangents;
    }

    public bool IsPlaying => _playing;

    /// <summary>Whether this attack's JSON ever names this slot, in any channel — the slot is
    /// this attack's to write (not RigIdle's) for every frame it plays.</summary>
    public bool Animates(string slotName) => _allAnimatedSlots.Contains(slotName);

    private void Awake()
    {
        _rig = GetComponent<RigCharacter>();
        if (idle == null) idle = GetComponent<RigIdle>();
        ParseAttackKeys();
        // Evaluate the rest pose once up front so a query before Play() is ever called still
        // gets real (all-rest) values rather than empty dictionaries.
        Evaluate(0f);
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.aKey.wasPressedThisFrame) Play();

        if (!_playing) return;

        _playTime += Time.deltaTime;
        if (_playTime >= _totalLength)
        {
            // Never trust the JSON's last key to already be rest, or "length" to exactly match
            // the last key's own t — either can leave a slot mid-pose otherwise.
            EndPlayback();
            return;
        }
        ApplyPose(_playTime);
    }

    [ContextMenu("Play Attack")]
    public void Play()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("RigAttack: Play Attack only works in Play mode — the rig's slots don't exist until then.");
            return;
        }
        // A second trigger mid-swing would otherwise restart _playTime at 0 and snap every slot
        // straight back to the JSON's first ("rest") key.
        if (_playing) return;

        // Re-read the JSON fresh on every trigger (not just once in Awake) so tuning it while
        // already in Play mode takes effect on the very next press — see ParseAttackKeys for how
        // a slot the new content drops is returned to rest rather than left frozen.
        ParseAttackKeys();
        if (_keys == null || _keys.Count == 0) return;

        _playing = true;
        _playTime = 0f;
        if (idle != null) idle.FadeOutForAttack();
        ApplyPose(0f);
    }

    private void OnDisable() => EndPlayback();

    private void OnDestroy() => EndPlayback();

    /// <summary>Stops playback (if playing) and puts every slot this attack has ever animated
    /// back to its rest local position/rotation/visual scale, turns the swing order off, and
    /// hands breathing back to RigIdle. Called both when playback reaches its natural end and
    /// whenever this component is disabled or destroyed mid-swing — a disabled RigAttack must
    /// never leave the rig frozen mid-pose with RigIdle permanently locked out of slots it
    /// still thinks this attack owns (see RigIdle.AttackOwns).</summary>
    private void EndPlayback()
    {
        RestoreSlotsToRest(_allAnimatedSlots);
        if (_rig != null) _rig.SetSwingOrderActive(false);
        _playing = false;
        if (idle != null) idle.FadeInAfterAttack();
    }

    private void RestoreSlotsToRest(IEnumerable<string> slots)
    {
        if (_rig == null) return;
        foreach (string slot in slots)
        {
            Transform t = _rig.Slot(slot);
            if (t == null) continue;
            Vector2 rest = _rig.RestLocalPosition(slot);
            t.localPosition = new Vector3(rest.x, rest.y, 0f);
            t.localRotation = Quaternion.identity;
            Transform visual = _rig.SlotVisual(slot);
            if (visual != null) visual.localScale = Vector3.one;
        }
    }

    private void ApplyPose(float t)
    {
        // A disabled RigIdle keeps whatever values its fields last held (it stops running its own
        // Update), so reading through it while disabled would fold in a stale offset/scale/angle
        // forever — treat it the same as "no idle" whenever it isn't actually running.
        bool idleActive = idle != null && idle.isActiveAndEnabled;

        Evaluate(t);
        foreach (string slot in _allAnimatedSlots)
        {
            Transform slotTransform = _rig.Slot(slot);
            if (slotTransform == null) continue;

            Vector2 idleOffset = idleActive ? idle.IdlePositionOffset(slot) : Vector2.zero;
            Vector2 offsetWorld = CanvasOffsetToWorld(_currentOffsets.TryGetValue(slot, out Vector2 o) ? o : Vector2.zero);
            Vector2 rest = _rig.RestLocalPosition(slot);
            slotTransform.localPosition = new Vector3(
                rest.x + idleOffset.x + offsetWorld.x, rest.y + idleOffset.y + offsetWorld.y, 0f);

            float idleAngle = idleActive ? idle.IdleAngle(slot) : 0f;
            float ownAngle = _currentAngles.TryGetValue(slot, out float a) ? a : 0f;
            slotTransform.localRotation = Quaternion.Euler(0f, 0f, idleAngle + ownAngle);

            Transform visual = _rig.SlotVisual(slot);
            if (visual != null)
            {
                Vector2 idleScale = idleActive ? idle.IdleVisualScale(slot) : Vector2.one;
                Vector2 ownScale = _currentScales.TryGetValue(slot, out Vector2 s) ? s : Vector2.one;
                visual.localScale = new Vector3(idleScale.x * ownScale.x, idleScale.y * ownScale.y, 1f);
            }
        }

        bool inSwingWindow = _hasSwingWindow && t >= _swingWindowStart && t <= _swingWindowEnd;
        _rig.SetSwingOrderActive(inSwingWindow);
    }

    private static Vector2 CanvasOffsetToWorld(Vector2 canvasPx) => new Vector2(canvasPx.x / PixelsPerUnit, -canvasPx.y / PixelsPerUnit);

    private void Evaluate(float t)
    {
        _currentAngles.Clear();
        _currentOffsets.Clear();
        _currentScales.Clear();
        if (_keys == null || _keys.Count == 0) return;

        if (_useMonotoneCubic)
        {
            EvaluateMonotoneCubic(t);
            return;
        }

        if (t <= _keys[0].t)
        {
            FillFrom(_keys[0]);
            return;
        }
        Key last = _keys[_keys.Count - 1];
        if (t >= last.t)
        {
            FillFrom(last);
            return;
        }

        for (int i = 0; i < _keys.Count - 1; i++)
        {
            Key from = _keys[i];
            Key to = _keys[i + 1];
            if (t < from.t || t > to.t) continue;

            float p = (t - from.t) / Mathf.Max(to.t - from.t, 0.0001f);
            string easeKind = _easeInto.TryGetValue(to.name, out string kind) ? kind : "linear";
            float e = Ease(easeKind, p);
            foreach (string slot in _allAnimatedSlots)
            {
                _currentAngles[slot] = Mathf.Lerp(AngleOrZero(from, slot), AngleOrZero(to, slot), e);
                _currentOffsets[slot] = Vector2.Lerp(OffsetOrZero(from, slot), OffsetOrZero(to, slot), e);
                _currentScales[slot] = Vector2.Lerp(ScaleOrOne(from, slot), ScaleOrOne(to, slot), e);
            }
            return;
        }
    }

    private void FillFrom(Key key)
    {
        foreach (string slot in _allAnimatedSlots)
        {
            _currentAngles[slot] = AngleOrZero(key, slot);
            _currentOffsets[slot] = OffsetOrZero(key, slot);
            _currentScales[slot] = ScaleOrOne(key, slot);
        }
    }

    private static float AngleOrZero(Key key, string slot) =>
        key.angles != null && key.angles.TryGetValue(slot, out float v) ? v : 0f;

    private static Vector2 OffsetOrZero(Key key, string slot) =>
        key.offsets != null && key.offsets.TryGetValue(slot, out Vector2 v) ? v : Vector2.zero;

    private static Vector2 ScaleOrOne(Key key, string slot) =>
        key.scales != null && key.scales.TryGetValue(slot, out Vector2 v) ? v : Vector2.one;

    private static float Ease(string kind, float p)
    {
        switch (kind)
        {
            case "in": return p * p * p;
            case "out":
                float q = 1f - p;
                return 1f - q * q * q;
            case "inout": return Mathf.SmoothStep(0f, 1f, p);
            case "linear":
            default: return p;
        }
    }

    /// <summary>"monotone_cubic" mode: every slot in _allAnimatedSlots is sampled at its own
    /// lagged time (clamp(t - lag[slot], 0, length) — matching Clip.sample in
    /// export3/_reference_smooth.py exactly), reading whichever of the three precomputed curve
    /// dictionaries has an entry for that slot and defaulting to rest for a channel it never
    /// appears in at all.</summary>
    private void EvaluateMonotoneCubic(float t)
    {
        foreach (string slot in _allAnimatedSlots)
        {
            float lag = _lag.TryGetValue(slot, out float lg) ? lg : 0f;
            float sampleTime = Mathf.Clamp(t - lag, 0f, _totalLength);

            if (_angleCurves.TryGetValue(slot, out Curve angle))
            {
                _currentAngles[slot] = SampleCurve(angle, sampleTime);
            }
            if (_offsetCurves.TryGetValue(slot, out Curve[] offset))
            {
                _currentOffsets[slot] = new Vector2(SampleCurve(offset[0], sampleTime), SampleCurve(offset[1], sampleTime));
            }
            if (_scaleCurves.TryGetValue(slot, out Curve[] scale))
            {
                _currentScales[slot] = new Vector2(SampleCurve(scale[0], sampleTime), SampleCurve(scale[1], sampleTime));
            }
        }
    }

    private static float SampleCurve(Curve curve, float x) => Hermite(curve.t, curve.values, curve.tangents, x);

    /// <summary>Fritsch-Carlson monotone cubic tangents: 0 at both ends, and at an interior key
    /// wherever the two neighbouring secant slopes disagree in sign (or either is exactly 0) —
    /// this is what makes the curve come to rest at an extreme pose instead of overshooting past
    /// it — otherwise the weighted harmonic mean of the two secants. Ported directly from
    /// export3/_reference_smooth.py's monotone_tangents.</summary>
    private static float[] ComputeMonotoneTangents(float[] t, float[] y)
    {
        int n = t.Length;
        var m = new float[n]; // m[0] and m[n - 1] stay 0 — start and finish at rest.
        if (n < 2) return m;

        var d = new float[n - 1];
        for (int i = 0; i < n - 1; i++) d[i] = (y[i + 1] - y[i]) / (t[i + 1] - t[i]);

        for (int i = 1; i < n - 1; i++)
        {
            if (d[i - 1] * d[i] <= 0f)
            {
                m[i] = 0f;
                continue;
            }
            float hi = t[i + 1] - t[i];
            float him1 = t[i] - t[i - 1];
            float w1 = 2f * hi + him1;
            float w2 = hi + 2f * him1;
            m[i] = (w1 + w2) / (w1 / d[i - 1] + w2 / d[i]);
        }
        return m;
    }

    /// <summary>Standard cubic Hermite spline evaluation over a non-uniform key-time axis,
    /// clamped to [t[0], t[^1]]. Ported directly from export3/_reference_smooth.py's hermite.
    /// </summary>
    private static float Hermite(float[] t, float[] y, float[] m, float x)
    {
        int n = t.Length;
        x = Mathf.Clamp(x, t[0], t[n - 1]);

        // Equivalent to min(np.searchsorted(t, x, side="right") - 1, n - 2): the segment whose
        // left endpoint is the last key time at or before x.
        int i = 0;
        while (i + 1 < n && t[i + 1] <= x) i++;
        i = Mathf.Min(i, n - 2);

        float h = t[i + 1] - t[i];
        float s = (x - t[i]) / h;
        float s2 = s * s, s3 = s2 * s;
        float h00 = 2f * s3 - 3f * s2 + 1f;
        float h10 = s3 - 2f * s2 + s;
        float h01 = -2f * s3 + 3f * s2;
        float h11 = s3 - s2;
        return h00 * y[i] + h10 * h * m[i] + h01 * y[i + 1] + h11 * h * m[i + 1];
    }

    /// <summary>(Re-)reads attackKeysJson from scratch — called once in Awake and again at the
    /// start of every Play(), so editing the JSON while already in Play mode takes effect on the
    /// next trigger with no need to stop and restart. Any slot the previous parse animated but
    /// this one no longer does is put back to rest immediately, rather than left frozen at
    /// whatever pose it was last written to (it would otherwise never be touched again, since
    /// ApplyPose only ever visits <see cref="_allAnimatedSlots"/>).</summary>
    private void ParseAttackKeys()
    {
        var previouslyAnimatedSlots = new HashSet<string>(_allAnimatedSlots);
        _allAnimatedSlots.Clear();
        _angleSlots.Clear();
        _offsetSlots.Clear();
        _scaleSlots.Clear();
        _lag.Clear();

        _keys = new List<Key>();
        _easeInto = new Dictionary<string, string>();
        _hasSwingWindow = false;
        _useMonotoneCubic = false;
        if (attackKeysJson == null)
        {
            Debug.LogError("RigAttack: no attackKeysJson assigned — this attack can never play.");
            return;
        }

        var root = JObject.Parse(attackKeysJson.text);

        if (root["swing_sort_window"] is JArray window && window.Count == 2)
        {
            _hasSwingWindow = true;
            _swingWindowStart = (float)window[0];
            _swingWindowEnd = (float)window[1];
        }

        if (root["keys"] is JArray keysArray)
        {
            foreach (JToken keyToken in keysArray)
            {
                var key = new Key
                {
                    t = (float)keyToken["t"],
                    name = (string)keyToken["name"],
                    angles = ReadFloatChannel(keyToken["angles"], _angleSlots),
                    offsets = ReadVectorChannel(keyToken["offset"], _offsetSlots),
                    scales = ReadVectorChannel(keyToken["scale"], _scaleSlots),
                };
                _keys.Add(key);
            }
        }
        // The interpolation loop in Evaluate assumes ascending t — never trust key order in the
        // JSON itself to already be sorted.
        _keys.Sort((a, b) => a.t.CompareTo(b.t));

        // Older files have no "interp" at all — keep them on the original ease_into scheme.
        _useMonotoneCubic = (string)root["interp"] == "monotone_cubic";

        if (root["ease_into"] is JObject easeObject)
        {
            foreach (JProperty prop in easeObject.Properties()) _easeInto[prop.Name] = (string)prop.Value;
        }
        if (root["lag"] is JObject lagObject)
        {
            foreach (JProperty prop in lagObject.Properties()) _lag[prop.Name] = (float)prop.Value;
        }

        // "length" is the authoritative total (and may run past the last key, e.g. holding rest
        // a moment longer) — fall back to the last key's own time for an older file without it.
        _totalLength = root["length"] != null
            ? (float)root["length"]
            : (_keys.Count > 0 ? _keys[_keys.Count - 1].t : 0f);

        if (_useMonotoneCubic) BuildMonotoneCurves();

        previouslyAnimatedSlots.ExceptWith(_allAnimatedSlots);
        if (previouslyAnimatedSlots.Count > 0) RestoreSlotsToRest(previouslyAnimatedSlots);
    }

    private Dictionary<string, float> ReadFloatChannel(JToken channel, HashSet<string> channelSlots)
    {
        var result = new Dictionary<string, float>();
        if (channel is JObject obj)
        {
            foreach (JProperty prop in obj.Properties())
            {
                result[prop.Name] = (float)prop.Value;
                _allAnimatedSlots.Add(prop.Name);
                channelSlots.Add(prop.Name);
            }
        }
        return result;
    }

    private Dictionary<string, Vector2> ReadVectorChannel(JToken channel, HashSet<string> channelSlots)
    {
        var result = new Dictionary<string, Vector2>();
        if (channel is JObject obj)
        {
            foreach (JProperty prop in obj.Properties())
            {
                if (prop.Value is JArray pair && pair.Count == 2)
                {
                    result[prop.Name] = new Vector2((float)pair[0], (float)pair[1]);
                    _allAnimatedSlots.Add(prop.Name);
                    channelSlots.Add(prop.Name);
                }
            }
        }
        return result;
    }

    /// <summary>Builds every "monotone_cubic"-mode curve: one per (channel, slot) that
    /// actually appears in that channel anywhere in _keys, each component built from the full,
    /// shared key-time axis with rest-defaulted values for a key that didn't specify this
    /// slot/channel — matching Clip.__init__ in export3/_reference_smooth.py exactly.</summary>
    private void BuildMonotoneCurves()
    {
        _angleCurves.Clear();
        _offsetCurves.Clear();
        _scaleCurves.Clear();

        var t = new float[_keys.Count];
        for (int k = 0; k < _keys.Count; k++) t[k] = _keys[k].t;

        foreach (string slot in _angleSlots)
        {
            var values = new float[_keys.Count];
            for (int k = 0; k < _keys.Count; k++) values[k] = AngleOrZero(_keys[k], slot);
            _angleCurves[slot] = new Curve { t = t, values = values, tangents = ComputeMonotoneTangents(t, values) };
        }
        foreach (string slot in _offsetSlots) _offsetCurves[slot] = BuildVectorCurves(t, slot, OffsetOrZero);
        foreach (string slot in _scaleSlots) _scaleCurves[slot] = BuildVectorCurves(t, slot, ScaleOrOne);
    }

    private Curve[] BuildVectorCurves(float[] t, string slot, System.Func<Key, string, Vector2> restDefault)
    {
        var x = new float[_keys.Count];
        var y = new float[_keys.Count];
        for (int k = 0; k < _keys.Count; k++)
        {
            Vector2 v = restDefault(_keys[k], slot);
            x[k] = v.x;
            y[k] = v.y;
        }
        return new[]
        {
            new Curve { t = t, values = x, tangents = ComputeMonotoneTangents(t, x) },
            new Curve { t = t, values = y, tangents = ComputeMonotoneTangents(t, y) },
        };
    }
}
