using UnityEngine;

/// <summary>
/// Drives the knight's code-only idle on a RigCharacter: a continuous, uneven breathing loop
/// (torso leads; the upper arms, then the head, then the plume follow with increasing lag, purely
/// through exponential smoothing of the torso's own wave — no per-part cycle bookkeeping needed;
/// each pauldron rides its own upper arm's rotation at a fraction of it) plus a randomly-timed
/// blink. No sprite sheets, no Animator clips — everything here is a plain transform edit or a
/// sprite swap. Thighs, shins and the pelvis are never touched, so the feet stay planted; only the
/// torso's own visual child gets a tiny squash (never a stretch), so it never propagates onto
/// whatever is parented under the torso slot (head, arms, sword, shield).
///
/// While a RigAttack plays, its keys and this idle both want to move the torso and rotate the
/// head, plume, upper arms and pauldrons. Rather than both writing the same transform (whichever
/// ran last would silently discard the other's contribution), this idle exposes its own current
/// contribution per slot (<see cref="IdleAngle"/>, <see cref="IdlePositionOffset"/>,
/// <see cref="IdleVisualScale"/>) and simply stops writing a slot itself the instant a playing
/// RigAttack claims it (see <see cref="LateUpdate"/>) — RigAttack is then the one to add this
/// idle's contribution to its own and perform the actual write. Its overall amplitude
/// (<see cref="BlendTo"/>) also fades out when an attack starts and back in once it ends, so its
/// own motion never pops when a slot changes hands.
/// </summary>
[RequireComponent(typeof(RigCharacter))]
public class RigIdle : MonoBehaviour
{
    [Header("Breathing")]
    [Tooltip("Seconds for one full inhale-exhale cycle, before per-cycle jitter.")]
    [SerializeField] private float breathPeriodSeconds = 4f;
    [Tooltip("How much the period varies cycle to cycle, as a fraction (0.1 = +/-10%).")]
    [SerializeField, Range(0f, 0.3f)] private float periodJitter = 0.1f;
    [Tooltip("How much the peak amplitude varies cycle to cycle, as a fraction.")]
    [SerializeField, Range(0f, 0.3f)] private float amplitudeJitter = 0.1f;
    [Tooltip("Fraction of the cycle spent paused at the bottom of the breath.")]
    [SerializeField, Range(0f, 0.5f)] private float holdBottomFraction = 0.15f;
    [Tooltip("Fraction of the cycle spent inhaling (quicker than the exhale).")]
    [SerializeField, Range(0.05f, 0.5f)] private float inhaleFraction = 0.30f;
    [Tooltip("Fraction of the cycle spent paused at the top of the breath.")]
    [SerializeField, Range(0f, 0.5f)] private float holdTopFraction = 0.10f;
    // The exhale gets whatever fraction remains, so the four segments always sum to the full
    // cycle even after the others are re-tuned in the inspector.

    [Tooltip("Torso rise/fall, in world units, at amplitudeMultiplier = 1 (~8px at 256 px/unit).")]
    [SerializeField] private float torsoBreathAmplitude = 0.03125f;
    [Tooltip("How much the torso's own height squashes at the peak of the breath (never stretches).")]
    [SerializeField, Range(0f, 0.02f)] private float torsoSquash = 0.01f;

    [SerializeField, Range(0f, 2f)] private float armLagSeconds = 0.12f;
    [SerializeField, Range(0f, 3f)] private float armMaxRotationDegrees = 1.5f;
    [Tooltip("Each pauldron's rotation as a fraction of its own upper arm's current rotation.")]
    [SerializeField, Range(0f, 1f)] private float pauldronFollowFraction = 0.35f;
    [SerializeField, Range(0f, 2f)] private float headLagSeconds = 0.25f;
    [SerializeField, Range(0f, 2f)] private float headMaxTiltDegrees = 1f;
    [SerializeField, Range(0f, 2f)] private float plumeLagSeconds = 0.4f;
    [SerializeField, Range(0f, 8f)] private float plumeMaxSwayDegrees = 4f;

    [Header("Attack blending")]
    [Tooltip("The component that plays the attack, if any — its own reference is auto-found on this GameObject when left empty.")]
    [SerializeField] private RigAttack attack;
    [Tooltip("How long the breathing's own amplitude takes to fade to zero once an attack starts.")]
    [SerializeField] private float attackFadeOutSeconds = 0.1f;
    [Tooltip("How long the breathing's own amplitude takes to fade back to full once an attack ends.")]
    [SerializeField] private float attackFadeInSeconds = 0.25f;

    [Header("Blink")]
    [Tooltip("The head's normal sprite, restored when a blink ends.")]
    [SerializeField] private Sprite headSprite;
    [Tooltip("The head sprite shown for the duration of a blink.")]
    [SerializeField] private Sprite headBlinkSprite;
    [SerializeField] private Vector2 blinkIntervalSeconds = new Vector2(3f, 7f);
    [SerializeField] private Vector2 blinkDurationSeconds = new Vector2(0.12f, 0.15f);
    [Tooltip("Chance that a blink is immediately followed by a second one, after a short gap.")]
    [SerializeField, Range(0f, 1f)] private float doubleBlinkChance = 0.15f;
    [SerializeField] private float doubleBlinkGapSeconds = 0.1f;

    [Header("Global")]
    [SerializeField, Range(0f, 3f)] private float speedMultiplier = 1f;
    [SerializeField, Range(0f, 3f)] private float amplitudeMultiplier = 1f;

    private enum BlinkState { Open, Closed, Gap }

    // The exhale must always keep a positive share of the cycle — otherwise the top hold can run
    // to the very end of the period and the wave snaps from 1 back to 0 at the wrap instead of
    // easing down. This is the fraction of the cycle guaranteed to the exhale no matter how the
    // three fields above are tuned.
    private const float MaxLeadingFractionsSum = 0.85f;

    private RigCharacter _rig;
    private Transform _torso, _torsoVisual, _upperArmShield, _upperArmSword, _pauldronShield, _pauldronSword, _head, _plume;
    private Vector3 _torsoRestLocalPosition, _torsoVisualRestLocalScale;

    // A single accumulator, advanced every Update by Time.deltaTime * speedMultiplier, drives
    // both the breath cycle and the blink timers — changing speedMultiplier at runtime then
    // changes their rate smoothly instead of snapping (Time.time alone can't be rescaled).
    private float _scaledTime;

    private float _cycleStartTime;
    private float _cyclePeriod;
    private float _cycleAmplitudeScale = 1f;

    private float _armWave, _headWave, _plumeWave;

    // This idle's own current contribution per slot — computed in Update, read by LateUpdate
    // (this component's own, when no attack owns the slot; RigAttack's, when one does) so exactly
    // one write reaches each transform per frame either way.
    private float _headIdleAngle, _plumeIdleAngle, _armIdleAngle, _pauldronIdleAngle;
    private float _torsoIdleOffsetY;
    private float _torsoIdleScaleY = 1f;

    // Scales every idle-driven offset above (not the rest pose itself) — 1 in normal play, ramped
    // to 0 while an attack plays and back to 1 afterward via BlendTo, so the breathing eases out
    // of the way instead of visibly fighting the attack's own motion or snapping off/on.
    private float _breathWeight = 1f;
    private float _breathWeightTarget = 1f;
    private float _breathWeightRate = float.MaxValue;

    private BlinkState _blinkState = BlinkState.Open;
    private float _blinkTimer;
    private float _blinkStateEndTime;
    private bool _doubleBlinkPending;

    private void Awake()
    {
        _rig = GetComponent<RigCharacter>();
        if (attack == null) attack = GetComponent<RigAttack>();
    }

    private void Start()
    {
        _torso = _rig.Slot("torso");
        _torsoVisual = _rig.SlotVisual("torso");
        _upperArmShield = _rig.Slot("upper_arm_shield");
        _upperArmSword = _rig.Slot("upper_arm_sword");
        _pauldronShield = _rig.Slot("pauldron_shield");
        _pauldronSword = _rig.Slot("pauldron_sword");
        _head = _rig.Slot("head");
        _plume = _rig.Slot("plume");

        if (_torso != null) _torsoRestLocalPosition = _torso.localPosition;
        if (_torsoVisual != null) _torsoVisualRestLocalScale = _torsoVisual.localScale;

        StartNewBreathCycle();
        ScheduleNextBlink();
    }

    private void OnDisable()
    {
        // Leave the rig in its rest pose and with both eyes open — never mid-breath or
        // mid-blink — so toggling this component off (or destroying it) doesn't strand the
        // character in whatever transient pose the last enabled frame happened to hit.
        if (_torso != null) _torso.localPosition = _torsoRestLocalPosition;
        if (_torsoVisual != null) _torsoVisual.localScale = _torsoVisualRestLocalScale;
        SetLocalTilt(_upperArmShield, 0f);
        SetLocalTilt(_upperArmSword, 0f);
        SetLocalTilt(_pauldronShield, 0f);
        SetLocalTilt(_pauldronSword, 0f);
        SetLocalTilt(_head, 0f);
        SetLocalTilt(_plume, 0f);
        if (_rig != null && headSprite != null) _rig.SetSlot("head", headSprite);

        // A disabled RigIdle stops running Update, so these would otherwise keep reporting
        // whatever they last held forever — RigAttack reads them through IdleAngle /
        // IdlePositionOffset / IdleVisualScale whenever it owns a shared slot (see there and
        // RigAttack.ApplyPose's idleActive check, which is the other half of this fix).
        _headIdleAngle = _plumeIdleAngle = _armIdleAngle = _pauldronIdleAngle = 0f;
        _torsoIdleOffsetY = 0f;
        _torsoIdleScaleY = 1f;
    }

    /// <summary>Ramps the breathing's overall amplitude toward <paramref name="targetWeight"/>
    /// (0..1), reaching it after <paramref name="overSeconds"/> real seconds regardless of
    /// speedMultiplier — called by RigAttack when it starts and ends playing.</summary>
    public void BlendTo(float targetWeight, float overSeconds)
    {
        _breathWeightTarget = Mathf.Clamp01(targetWeight);
        _breathWeightRate = overSeconds > 0.0001f ? 1f / overSeconds : float.MaxValue;
    }

    /// <summary>Called by RigAttack the instant it starts playing.</summary>
    public void FadeOutForAttack() => BlendTo(0f, attackFadeOutSeconds);

    /// <summary>Called by RigAttack the instant it finishes playing.</summary>
    public void FadeInAfterAttack() => BlendTo(1f, attackFadeInSeconds);

    private void Update()
    {
        float dt = Time.deltaTime * Mathf.Max(0f, speedMultiplier);
        _scaledTime += dt;
        // Unscaled: the fade duration is a fixed real-time span, not something breathing's own
        // speedMultiplier should speed up or slow down.
        _breathWeight = Mathf.MoveTowards(_breathWeight, _breathWeightTarget, Time.deltaTime * _breathWeightRate);
        UpdateBreath(dt);
        UpdateBlink(dt);
    }

    /// <summary>This idle's own current rotation contribution for a slot it can drive — 0 for
    /// every other slot. Read by RigAttack (see there) whenever it owns the slot itself, so its
    /// own keyed angle and this idle's don't fight over the same transform.</summary>
    public float IdleAngle(string slotName)
    {
        if (slotName == "head") return _headIdleAngle;
        if (slotName == "plume") return _plumeIdleAngle;
        if (slotName == "upper_arm_sword" || slotName == "upper_arm_shield") return _armIdleAngle;
        if (slotName == "pauldron_sword" || slotName == "pauldron_shield") return _pauldronIdleAngle;
        return 0f;
    }

    /// <summary>This idle's own current local-position offset (from rest) for a slot it can
    /// drive — only ever non-zero for "torso" (the breathing rise/fall).</summary>
    public Vector2 IdlePositionOffset(string slotName) => slotName == "torso" ? new Vector2(0f, _torsoIdleOffsetY) : Vector2.zero;

    /// <summary>This idle's own current visual-child scale for a slot it can drive — only ever
    /// non-one for "torso" (the breathing squash).</summary>
    public Vector2 IdleVisualScale(string slotName) => slotName == "torso" ? new Vector2(1f, _torsoIdleScaleY) : Vector2.one;

    /// <summary>Writes every slot this idle drives, each skipped for exactly the frames a playing
    /// RigAttack claims it (<see cref="RigAttack.Animates"/>) — RigAttack is then the one adding
    /// this idle's contribution (via the getters above) to its own before writing that slot
    /// itself. Runs in LateUpdate specifically so it always happens after every script's Update —
    /// including RigAttack's, which computes its own current pose in Update — so neither this nor
    /// RigAttack ever overwrites the other with a stale value.</summary>
    private void LateUpdate()
    {
        if (_torso != null && !AttackOwns("torso"))
        {
            Vector2 offset = IdlePositionOffset("torso");
            _torso.localPosition = _torsoRestLocalPosition + new Vector3(offset.x, offset.y, 0f);
            if (_torsoVisual != null)
            {
                Vector2 scale = IdleVisualScale("torso");
                _torsoVisual.localScale = Vector3.Scale(_torsoVisualRestLocalScale, new Vector3(scale.x, scale.y, 1f));
            }
        }

        WriteIfIdleOwns(_head, "head");
        WriteIfIdleOwns(_plume, "plume");
        WriteIfIdleOwns(_upperArmSword, "upper_arm_sword");
        WriteIfIdleOwns(_upperArmShield, "upper_arm_shield");
        WriteIfIdleOwns(_pauldronSword, "pauldron_sword");
        WriteIfIdleOwns(_pauldronShield, "pauldron_shield");
    }

    private void WriteIfIdleOwns(Transform t, string slotName)
    {
        if (t != null && !AttackOwns(slotName)) SetLocalTilt(t, IdleAngle(slotName));
    }

    private bool AttackOwns(string slotName) => attack != null && attack.IsPlaying && attack.Animates(slotName);

    private void StartNewBreathCycle()
    {
        _cycleStartTime = _scaledTime;
        _cyclePeriod = breathPeriodSeconds * (1f + Random.Range(-periodJitter, periodJitter));
        // Slight per-cycle variation so the loop never reads as a perfectly repeating animation.
        _cycleAmplitudeScale = 1f + Random.Range(-amplitudeJitter, amplitudeJitter);
    }

    private void UpdateBreath(float dt)
    {
        if (_torso == null) return;

        float elapsed = _scaledTime - _cycleStartTime;
        if (elapsed >= _cyclePeriod)
        {
            StartNewBreathCycle();
            elapsed = 0f;
        }

        float torsoWave = BreathWave(elapsed) * _cycleAmplitudeScale;

        // Computed only — written in LateUpdate (this idle's own write, or folded into
        // RigAttack's, whichever currently owns "torso" — see there).
        float amplitude = torsoBreathAmplitude * amplitudeMultiplier * _breathWeight;
        _torsoIdleOffsetY = amplitude * torsoWave;
        float squash = torsoSquash * amplitudeMultiplier * _breathWeight * torsoWave;
        _torsoIdleScaleY = 1f - squash;

        // Each follower chases the torso's own wave through exponential smoothing rather than
        // sampling a shifted copy of it — that keeps every part's motion continuous across a
        // breath-cycle boundary, and a longer lag constant alone is what gives the head more lag
        // than the arms and the plume more than the head.
        _armWave = ExponentialFollow(_armWave, torsoWave, armLagSeconds, dt);
        _headWave = ExponentialFollow(_headWave, torsoWave, headLagSeconds, dt);
        _plumeWave = ExponentialFollow(_plumeWave, torsoWave, plumeLagSeconds, dt);

        // Computed only — not written here. Both upper arms, their pauldrons, the head and the
        // plume are slots a RigAttack can also rotate, so the actual write happens once, in
        // LateUpdate, after adding whatever the attack currently wants for the same slot.
        _armIdleAngle = armMaxRotationDegrees * amplitudeMultiplier * _breathWeight * _armWave;
        _pauldronIdleAngle = _armIdleAngle * pauldronFollowFraction;
        _headIdleAngle = headMaxTiltDegrees * amplitudeMultiplier * _breathWeight * _headWave;
        _plumeIdleAngle = plumeMaxSwayDegrees * amplitudeMultiplier * _breathWeight * _plumeWave;
    }

    /// <summary>0..1 breath value for a time offset into the current cycle: paused at the
    /// bottom, eased up through a quicker inhale, paused at the top, then eased back down through
    /// a slower exhale.</summary>
    private float BreathWave(float elapsedInCycle)
    {
        GetEffectiveLeadingFractions(out float bottomFraction, out float effectiveInhaleFraction, out float topFraction);

        float period = _cyclePeriod;
        float bottomEnd = bottomFraction * period;
        float inhaleEnd = bottomEnd + effectiveInhaleFraction * period;
        float topEnd = inhaleEnd + topFraction * period;

        if (elapsedInCycle < bottomEnd) return 0f;
        if (elapsedInCycle < inhaleEnd)
        {
            return Mathf.SmoothStep(0f, 1f, (elapsedInCycle - bottomEnd) / Mathf.Max(inhaleEnd - bottomEnd, 0.0001f));
        }
        if (elapsedInCycle < topEnd) return 1f;
        float exhaleT = (elapsedInCycle - topEnd) / Mathf.Max(period - topEnd, 0.0001f);
        return Mathf.SmoothStep(1f, 0f, exhaleT);
    }

    /// <summary>holdBottomFraction + inhaleFraction + holdTopFraction, scaled down together if
    /// their sum would otherwise leave the exhale no share of the cycle (or a vanishing one) —
    /// tuning any of the three in the inspector can never make the breath snap at the wrap.</summary>
    private void GetEffectiveLeadingFractions(out float bottom, out float inhale, out float top)
    {
        bottom = Mathf.Max(0f, holdBottomFraction);
        inhale = Mathf.Max(0f, inhaleFraction);
        top = Mathf.Max(0f, holdTopFraction);

        float sum = bottom + inhale + top;
        if (sum > MaxLeadingFractionsSum)
        {
            float scale = MaxLeadingFractionsSum / sum;
            bottom *= scale;
            inhale *= scale;
            top *= scale;
        }
    }

    private static float ExponentialFollow(float current, float target, float timeConstant, float dt)
    {
        if (timeConstant <= 0f) return target;
        return Mathf.Lerp(current, target, 1f - Mathf.Exp(-dt / timeConstant));
    }

    private static void SetLocalTilt(Transform t, float angleDegrees)
    {
        if (t != null) t.localRotation = Quaternion.Euler(0f, 0f, angleDegrees);
    }

    private void ScheduleNextBlink()
    {
        _blinkTimer = Random.Range(blinkIntervalSeconds.x, blinkIntervalSeconds.y);
    }

    private void UpdateBlink(float dt)
    {
        if (_head == null || headSprite == null || headBlinkSprite == null) return;

        switch (_blinkState)
        {
            case BlinkState.Open:
                _blinkTimer -= dt;
                if (_blinkTimer <= 0f) StartBlink();
                break;

            case BlinkState.Closed:
                if (_scaledTime >= _blinkStateEndTime)
                {
                    _rig.SetSlot("head", headSprite);
                    if (_doubleBlinkPending)
                    {
                        _doubleBlinkPending = false;
                        _blinkState = BlinkState.Gap;
                        _blinkStateEndTime = _scaledTime + doubleBlinkGapSeconds;
                    }
                    else
                    {
                        _blinkState = BlinkState.Open;
                        ScheduleNextBlink();
                    }
                }
                break;

            case BlinkState.Gap:
                if (_scaledTime >= _blinkStateEndTime) StartBlink();
                break;
        }
    }

    private void StartBlink()
    {
        if (_blinkState == BlinkState.Open) _doubleBlinkPending = Random.value < doubleBlinkChance;
        _blinkState = BlinkState.Closed;
        _rig.SetSlot("head", headBlinkSprite);
        _blinkStateEndTime = _scaledTime + Random.Range(blinkDurationSeconds.x, blinkDurationSeconds.y);
    }
}
