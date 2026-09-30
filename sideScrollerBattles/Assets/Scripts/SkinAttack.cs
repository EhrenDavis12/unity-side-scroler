using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Plays a code-driven attack on a SkinBoneCharacter from attackKeysJson (see
/// Assets/Characters/knight_skin/attack_keys.json) — no Animator clips. Every bone this rig has
/// (see SkinRigDefinition) already carries a nonzero rest local rotation of its own (a real
/// skeletal pose, unlike the old cutout rig's zero-rotation slots), so every angle this attack
/// applies is added ON TOP of that bone's own rest rotation, never written in place of it — see
/// <see cref="ApplyPose"/>. The JSON's "keys" are a sparse list of {t seconds, name, angles{bone:
/// local deg}, offset{bone: [dx, dy]}}; a bone missing from a key's channel means rest for that
/// channel. Every bone named anywhere in the JSON is discovered from the JSON itself. Angles are
/// local Z rotations in degrees about each bone's own joint — positive is counter-clockwise on
/// screen, matching Unity's +Z rotation sense for a 2D sprite under the default camera. Offsets
/// are canvas pixels, y down, added to the bone's rest local position — only ever used for "hips"
/// (the skeleton's root, whose local frame is world-aligned, exactly like rig.json's root slot in
/// the old cutout system, so the same pixel-to-world conversion applies unchanged).
///
/// This rig's exporter only ever emits "monotone_cubic" interpolation (Fritsch-Carlson monotone
/// cubic Hermite, identical to export3/_reference_smooth.py and to the old cutout rig's
/// RigAttack), so — unlike RigAttack — this component supports that mode alone; there is no older
/// ease_into format to fall back to. The JSON's "lag" ({bone: seconds}) samples a bone's curves at
/// clamp(t - lag[bone], 0, length) instead of t, for staggered, overlapping motion.
///
/// The thigh/shin/boot bones are never named in this JSON at all — SkinBoneCharacter's 2D IK owns
/// them entirely, keeping the feet planted while "hips" moves.
///
/// SkinIdle also drives "chest", "head", "plume", "upper_arm_sword" and "upper_arm_shield". For
/// any bone SkinIdle drives, this is the sole writer of its transform while playing (see
/// <see cref="Animates"/> / <see cref="ApplyPose"/>), adding SkinIdle's own current contribution
/// before writing — so neither ever silently discards the other's contribution for the same bone.
/// </summary>
[RequireComponent(typeof(SkinBoneCharacter))]
public class SkinAttack : MonoBehaviour
{
    private const float PixelsPerUnit = 256f; // must match SkinRigImporter's constant

    [SerializeField] private TextAsset attackKeysJson;
    [Tooltip("The SkinIdle to blend out/in around this attack, and to read idle contributions from for a shared bone — auto-found on this GameObject when left empty.")]
    [SerializeField] private SkinIdle idle;

    private SkinBoneCharacter _rig;

    private List<Key> _keys;
    private float _totalLength;
    private readonly HashSet<string> _allAnimatedBones = new HashSet<string>();

    private readonly HashSet<string> _angleBones = new HashSet<string>();
    private readonly HashSet<string> _offsetBones = new HashSet<string>();
    private readonly Dictionary<string, Curve> _angleCurves = new Dictionary<string, Curve>();
    private readonly Dictionary<string, Curve[]> _offsetCurves = new Dictionary<string, Curve[]>();
    private readonly Dictionary<string, float> _lag = new Dictionary<string, float>();

    private readonly Dictionary<string, float> _currentAngles = new Dictionary<string, float>();
    private readonly Dictionary<string, Vector2> _currentOffsets = new Dictionary<string, Vector2>();

    private bool _playing;
    private float _playTime;

    private class Key
    {
        public float t;
        public Dictionary<string, float> angles;
        public Dictionary<string, Vector2> offsets;
    }

    /// <summary>One component's monotone cubic Hermite spline over the shared key-time axis.</summary>
    private class Curve
    {
        public float[] t;
        public float[] values;
        public float[] tangents;
    }

    public bool IsPlaying => _playing;

    /// <summary>Whether this attack's JSON ever names this bone, in any channel — the bone is
    /// this attack's to write (not SkinIdle's) for every frame it plays.</summary>
    public bool Animates(string boneName) => _allAnimatedBones.Contains(boneName);

    private void Awake()
    {
        _rig = GetComponent<SkinBoneCharacter>();
        if (idle == null) idle = GetComponent<SkinIdle>();
        ParseAttackKeys();
        Evaluate(0f);
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.aKey.wasPressedThisFrame) Play();

        if (!_playing) return;

        _playTime += Time.deltaTime;
        if (_playTime >= _totalLength)
        {
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
            Debug.LogWarning("SkinAttack: Play Attack only works in Play mode — the rig's bones don't exist until then.");
            return;
        }
        if (_playing) return;

        ParseAttackKeys();
        if (_keys == null || _keys.Count == 0) return;

        _playing = true;
        _playTime = 0f;
        if (idle != null) idle.FadeOutForAttack();
        ApplyPose(0f);
    }

    private void OnDisable() => EndPlayback();

    private void OnDestroy() => EndPlayback();

    /// <summary>Stops playback (if playing) and puts every bone this attack has ever animated back
    /// to its own rest local position/rotation, then hands breathing back to SkinIdle.</summary>
    private void EndPlayback()
    {
        RestoreBonesToRest(_allAnimatedBones);
        _playing = false;
        if (idle != null) idle.FadeInAfterAttack();
    }

    private void RestoreBonesToRest(IEnumerable<string> boneNames)
    {
        if (_rig == null) return;
        foreach (string boneName in boneNames)
        {
            Transform t = _rig.Bone(boneName);
            if (t == null) continue;
            Vector2 restPosition = _rig.RestLocalPosition(boneName);
            t.localPosition = new Vector3(restPosition.x, restPosition.y, 0f);
            t.localRotation = Quaternion.Euler(0f, 0f, _rig.RestLocalRotationDeg(boneName));
        }
    }

    private void ApplyPose(float t)
    {
        bool idleActive = idle != null && idle.isActiveAndEnabled;

        Evaluate(t);
        foreach (string boneName in _allAnimatedBones)
        {
            Transform bone = _rig.Bone(boneName);
            if (bone == null) continue;

            Vector2 idleOffset = idleActive ? idle.IdlePositionOffset(boneName) : Vector2.zero;
            Vector2 offsetWorld = CanvasOffsetToWorld(_currentOffsets.TryGetValue(boneName, out Vector2 o) ? o : Vector2.zero);
            Vector2 rest = _rig.RestLocalPosition(boneName);
            bone.localPosition = new Vector3(rest.x + idleOffset.x + offsetWorld.x, rest.y + idleOffset.y + offsetWorld.y, 0f);

            float idleAngle = idleActive ? idle.IdleAngle(boneName) : 0f;
            float ownAngle = _currentAngles.TryGetValue(boneName, out float a) ? a : 0f;
            bone.localRotation = Quaternion.Euler(0f, 0f, _rig.RestLocalRotationDeg(boneName) + idleAngle + ownAngle);
        }
    }

    private static Vector2 CanvasOffsetToWorld(Vector2 canvasPx) => new Vector2(canvasPx.x / PixelsPerUnit, -canvasPx.y / PixelsPerUnit);

    private void Evaluate(float t)
    {
        _currentAngles.Clear();
        _currentOffsets.Clear();
        if (_keys == null || _keys.Count == 0) return;

        foreach (string boneName in _allAnimatedBones)
        {
            float lag = _lag.TryGetValue(boneName, out float lg) ? lg : 0f;
            float sampleTime = Mathf.Clamp(t - lag, 0f, _totalLength);

            if (_angleCurves.TryGetValue(boneName, out Curve angle))
            {
                _currentAngles[boneName] = SampleCurve(angle, sampleTime);
            }
            if (_offsetCurves.TryGetValue(boneName, out Curve[] offset))
            {
                _currentOffsets[boneName] = new Vector2(SampleCurve(offset[0], sampleTime), SampleCurve(offset[1], sampleTime));
            }
        }
    }

    private static float SampleCurve(Curve curve, float x) => Hermite(curve.t, curve.values, curve.tangents, x);

    /// <summary>Fritsch-Carlson monotone cubic tangents — ported directly from
    /// export3/_reference_smooth.py's monotone_tangents (see RigAttack, whose copy of this exact
    /// function this rig's exporter already validated against).</summary>
    private static float[] ComputeMonotoneTangents(float[] t, float[] y)
    {
        int n = t.Length;
        var m = new float[n];
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

    /// <summary>Standard cubic Hermite spline evaluation over a non-uniform key-time axis, clamped
    /// to [t[0], t[^1]].</summary>
    private static float Hermite(float[] t, float[] y, float[] m, float x)
    {
        int n = t.Length;
        x = Mathf.Clamp(x, t[0], t[n - 1]);

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
    /// start of every Play(). Any bone the previous parse animated but this one no longer does is
    /// put back to rest immediately (see RestoreBonesToRest).</summary>
    private void ParseAttackKeys()
    {
        var previouslyAnimatedBones = new HashSet<string>(_allAnimatedBones);
        _allAnimatedBones.Clear();
        _angleBones.Clear();
        _offsetBones.Clear();
        _lag.Clear();

        _keys = new List<Key>();
        if (attackKeysJson == null)
        {
            Debug.LogError("SkinAttack: no attackKeysJson assigned — this attack can never play.");
            return;
        }

        var root = JObject.Parse(attackKeysJson.text);

        if (root["keys"] is JArray keysArray)
        {
            foreach (JToken keyToken in keysArray)
            {
                var key = new Key
                {
                    t = (float)keyToken["t"],
                    angles = ReadFloatChannel(keyToken["angles"], _angleBones),
                    offsets = ReadVectorChannel(keyToken["offset"], _offsetBones),
                };
                _keys.Add(key);
            }
        }
        _keys.Sort((a, b) => a.t.CompareTo(b.t));

        if (root["lag"] is JObject lagObject)
        {
            foreach (JProperty prop in lagObject.Properties()) _lag[prop.Name] = (float)prop.Value;
        }

        _totalLength = root["length"] != null
            ? (float)root["length"]
            : (_keys.Count > 0 ? _keys[_keys.Count - 1].t : 0f);

        BuildMonotoneCurves();

        previouslyAnimatedBones.ExceptWith(_allAnimatedBones);
        if (previouslyAnimatedBones.Count > 0) RestoreBonesToRest(previouslyAnimatedBones);
    }

    private Dictionary<string, float> ReadFloatChannel(JToken channel, HashSet<string> channelBones)
    {
        var result = new Dictionary<string, float>();
        if (channel is JObject obj)
        {
            foreach (JProperty prop in obj.Properties())
            {
                result[prop.Name] = (float)prop.Value;
                _allAnimatedBones.Add(prop.Name);
                channelBones.Add(prop.Name);
            }
        }
        return result;
    }

    private Dictionary<string, Vector2> ReadVectorChannel(JToken channel, HashSet<string> channelBones)
    {
        var result = new Dictionary<string, Vector2>();
        if (channel is JObject obj)
        {
            foreach (JProperty prop in obj.Properties())
            {
                if (prop.Value is JArray pair && pair.Count == 2)
                {
                    result[prop.Name] = new Vector2((float)pair[0], (float)pair[1]);
                    _allAnimatedBones.Add(prop.Name);
                    channelBones.Add(prop.Name);
                }
            }
        }
        return result;
    }

    private void BuildMonotoneCurves()
    {
        _angleCurves.Clear();
        _offsetCurves.Clear();

        var t = new float[_keys.Count];
        for (int k = 0; k < _keys.Count; k++) t[k] = _keys[k].t;

        foreach (string boneName in _angleBones)
        {
            var values = new float[_keys.Count];
            for (int k = 0; k < _keys.Count; k++) values[k] = AngleOrZero(_keys[k], boneName);
            _angleCurves[boneName] = new Curve { t = t, values = values, tangents = ComputeMonotoneTangents(t, values) };
        }
        foreach (string boneName in _offsetBones) _offsetCurves[boneName] = BuildVectorCurves(t, boneName);
    }

    private Curve[] BuildVectorCurves(float[] t, string boneName)
    {
        var x = new float[_keys.Count];
        var y = new float[_keys.Count];
        for (int k = 0; k < _keys.Count; k++)
        {
            Vector2 v = OffsetOrZero(_keys[k], boneName);
            x[k] = v.x;
            y[k] = v.y;
        }
        return new[]
        {
            new Curve { t = t, values = x, tangents = ComputeMonotoneTangents(t, x) },
            new Curve { t = t, values = y, tangents = ComputeMonotoneTangents(t, y) },
        };
    }

    private static float AngleOrZero(Key key, string boneName) =>
        key.angles != null && key.angles.TryGetValue(boneName, out float v) ? v : 0f;

    private static Vector2 OffsetOrZero(Key key, string boneName) =>
        key.offsets != null && key.offsets.TryGetValue(boneName, out Vector2 v) ? v : Vector2.zero;
}
