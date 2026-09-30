using UnityEngine;

/// <summary>
/// Drives the knight's code-only idle on a SkinBoneCharacter: a continuous, uneven breathing loop
/// (chest leads; the upper arms, then the head, then the plume follow with increasing lag, purely
/// through exponential smoothing of the chest's own wave — no per-part cycle bookkeeping needed)
/// plus a randomly-timed blink. No sprite sheets, no Animator clips — every bone touched here gets
/// a plain rotation added on top of its own rest local rotation (see SkinRigDefinition — unlike
/// the old cutout rig's slots, every bone in this skeleton already has a real, nonzero rest
/// rotation of its own). The legs are never touched, so the feet stay planted under
/// SkinBoneCharacter's 2D IK regardless of what this idle or an attack does above the hips.
///
/// While a SkinAttack plays, its keys and this idle both want to rotate "chest", "head", "plume",
/// "upper_arm_sword" and "upper_arm_shield". Rather than both writing the same bone (whichever ran
/// last would silently discard the other's contribution), this idle exposes its own current
/// contribution per bone (<see cref="IdleAngle"/>, <see cref="IdlePositionOffset"/>) and stops
/// writing a bone itself the instant a playing SkinAttack claims it (see <see cref="LateUpdate"/>)
/// — SkinAttack is then the one to add this idle's contribution to its own and perform the actual
/// write. Its overall amplitude (<see cref="BlendTo"/>) also fades out when an attack starts and
/// back in once it ends, so its own motion never pops when a bone changes hands.
/// </summary>
[RequireComponent(typeof(SkinBoneCharacter))]
public class SkinIdle : MonoBehaviour
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

    [SerializeField, Range(0f, 3f)] private float chestMaxTiltDegrees = 1.25f;
    [SerializeField, Range(0f, 2f)] private float armLagSeconds = 0.12f;
    [SerializeField, Range(0f, 3f)] private float armMaxRotationDegrees = 1.5f;
    [SerializeField, Range(0f, 2f)] private float headLagSeconds = 0.25f;
    [SerializeField, Range(0f, 2f)] private float headMaxTiltDegrees = 1f;
    [SerializeField, Range(0f, 2f)] private float plumeLagSeconds = 0.4f;
    [SerializeField, Range(0f, 8f)] private float plumeMaxSwayDegrees = 4f;

    [Header("Attack blending")]
    [Tooltip("The component that plays the attack, if any — its own reference is auto-found on this GameObject when left empty.")]
    [SerializeField] private SkinAttack attack;
    [Tooltip("How long the breathing's own amplitude takes to fade to zero once an attack starts.")]
    [SerializeField] private float attackFadeOutSeconds = 0.1f;
    [Tooltip("How long the breathing's own amplitude takes to fade back to full once an attack ends.")]
    [SerializeField] private float attackFadeInSeconds = 0.25f;

    [Header("Blink")]
    [SerializeField] private Vector2 blinkIntervalSeconds = new Vector2(3f, 7f);
    [SerializeField] private Vector2 blinkDurationSeconds = new Vector2(0.12f, 0.15f);

    [Header("Global")]
    [SerializeField, Range(0f, 3f)] private float speedMultiplier = 1f;
    [SerializeField, Range(0f, 3f)] private float amplitudeMultiplier = 1f;

    // The exhale must always keep a positive share of the cycle — otherwise the top hold can run
    // to the very end of the period and the wave snaps from 1 back to 0 at the wrap instead of
    // easing down. This is the fraction of the cycle guaranteed to the exhale no matter how the
    // three fields above are tuned.
    private const float MaxLeadingFractionsSum = 0.85f;

    private SkinBoneCharacter _rig;
    private Transform _chest, _upperArmSword, _upperArmShield, _head, _plume;

    // A single accumulator, advanced every Update by Time.deltaTime * speedMultiplier, drives
    // both the breath cycle and the blink timer.
    private float _scaledTime;

    private float _cycleStartTime;
    private float _cyclePeriod;
    private float _cycleAmplitudeScale = 1f;

    private float _armWave, _headWave, _plumeWave;

    // This idle's own current contribution per bone — computed in Update, read by LateUpdate
    // (this component's own, when no attack owns the bone; SkinAttack's, when one does).
    private float _chestIdleAngle, _headIdleAngle, _plumeIdleAngle, _armIdleAngle;

    // Scales every idle-driven angle above — 1 in normal play, ramped to 0 while an attack plays
    // and back to 1 afterward via BlendTo.
    private float _breathWeight = 1f;
    private float _breathWeightTarget = 1f;
    private float _breathWeightRate = float.MaxValue;

    private bool _blinking;
    private float _blinkTimer;
    private float _blinkEndTime;

    private void Awake()
    {
        _rig = GetComponent<SkinBoneCharacter>();
        if (attack == null) attack = GetComponent<SkinAttack>();
    }

    private void Start()
    {
        _chest = _rig.Bone("chest");
        _upperArmSword = _rig.Bone("upper_arm_sword");
        _upperArmShield = _rig.Bone("upper_arm_shield");
        _head = _rig.Bone("head");
        _plume = _rig.Bone("plume");

        StartNewBreathCycle();
        ScheduleNextBlink();
    }

    private void OnDisable()
    {
        // Leave every bone this idle drives at its own rest rotation, and both eyes open — never
        // mid-breath or mid-blink — so toggling this component off doesn't strand the character in
        // whatever transient pose the last enabled frame happened to hit.
        SetRestTilt(_chest);
        SetRestTilt(_upperArmSword);
        SetRestTilt(_upperArmShield);
        SetRestTilt(_head);
        SetRestTilt(_plume);
        _rig?.SetHeadBlinking(false);

        // A disabled SkinIdle stops running Update, so these would otherwise keep reporting
        // whatever they last held forever — SkinAttack reads them through IdleAngle whenever it
        // owns a shared bone (see there and SkinAttack.ApplyPose's idleActive check).
        _chestIdleAngle = _headIdleAngle = _plumeIdleAngle = _armIdleAngle = 0f;
    }

    private void SetRestTilt(Transform bone)
    {
        if (bone == null) return;
        bone.localRotation = Quaternion.identity; // overwritten with the real rest angle in LateUpdate's WriteIfIdleOwns path once re-enabled
    }

    /// <summary>Ramps the breathing's overall amplitude toward <paramref name="targetWeight"/>
    /// (0..1), reaching it after <paramref name="overSeconds"/> real seconds regardless of
    /// speedMultiplier — called by SkinAttack when it starts and ends playing.</summary>
    public void BlendTo(float targetWeight, float overSeconds)
    {
        _breathWeightTarget = Mathf.Clamp01(targetWeight);
        _breathWeightRate = overSeconds > 0.0001f ? 1f / overSeconds : float.MaxValue;
    }

    public void FadeOutForAttack() => BlendTo(0f, attackFadeOutSeconds);
    public void FadeInAfterAttack() => BlendTo(1f, attackFadeInSeconds);

    private void Update()
    {
        float dt = Time.deltaTime * Mathf.Max(0f, speedMultiplier);
        _scaledTime += dt;
        _breathWeight = Mathf.MoveTowards(_breathWeight, _breathWeightTarget, Time.deltaTime * _breathWeightRate);
        UpdateBreath(dt);
        UpdateBlink(dt);
    }

    /// <summary>This idle's own current rotation contribution for a bone it can drive — 0 for
    /// every other bone. Read by SkinAttack whenever it owns the bone itself, so its own keyed
    /// angle and this idle's don't fight over the same transform.</summary>
    public float IdleAngle(string boneName)
    {
        if (boneName == "chest") return _chestIdleAngle;
        if (boneName == "head") return _headIdleAngle;
        if (boneName == "plume") return _plumeIdleAngle;
        if (boneName == "upper_arm_sword" || boneName == "upper_arm_shield") return _armIdleAngle;
        return 0f;
    }

    /// <summary>This idle never offsets any bone's position (breathing here is rotation-only —
    /// see class doc) — kept for parity with SkinAttack's own idle-blend lookups.</summary>
    public Vector2 IdlePositionOffset(string boneName) => Vector2.zero;

    /// <summary>Writes every bone this idle drives, each skipped for exactly the frames a playing
    /// SkinAttack claims it — SkinAttack is then the one adding this idle's contribution (via the
    /// getters above) to its own before writing that bone itself. Runs in LateUpdate so it always
    /// happens after every script's Update, including SkinAttack's.</summary>
    private void LateUpdate()
    {
        WriteIfIdleOwns(_chest, "chest");
        WriteIfIdleOwns(_head, "head");
        WriteIfIdleOwns(_plume, "plume");
        WriteIfIdleOwns(_upperArmSword, "upper_arm_sword");
        WriteIfIdleOwns(_upperArmShield, "upper_arm_shield");
    }

    private void WriteIfIdleOwns(Transform bone, string boneName)
    {
        if (bone == null || AttackOwns(boneName)) return;
        bone.localRotation = Quaternion.Euler(0f, 0f, _rig.RestLocalRotationDeg(boneName) + IdleAngle(boneName));
    }

    private bool AttackOwns(string boneName) => attack != null && attack.IsPlaying && attack.Animates(boneName);

    private void StartNewBreathCycle()
    {
        _cycleStartTime = _scaledTime;
        _cyclePeriod = breathPeriodSeconds * (1f + Random.Range(-periodJitter, periodJitter));
        _cycleAmplitudeScale = 1f + Random.Range(-amplitudeJitter, amplitudeJitter);
    }

    private void UpdateBreath(float dt)
    {
        if (_chest == null) return;

        float elapsed = _scaledTime - _cycleStartTime;
        if (elapsed >= _cyclePeriod)
        {
            StartNewBreathCycle();
            elapsed = 0f;
        }

        float chestWave = BreathWave(elapsed) * _cycleAmplitudeScale;

        _armWave = ExponentialFollow(_armWave, chestWave, armLagSeconds, dt);
        _headWave = ExponentialFollow(_headWave, chestWave, headLagSeconds, dt);
        _plumeWave = ExponentialFollow(_plumeWave, chestWave, plumeLagSeconds, dt);

        _chestIdleAngle = chestMaxTiltDegrees * amplitudeMultiplier * _breathWeight * chestWave;
        _armIdleAngle = armMaxRotationDegrees * amplitudeMultiplier * _breathWeight * _armWave;
        _headIdleAngle = headMaxTiltDegrees * amplitudeMultiplier * _breathWeight * _headWave;
        _plumeIdleAngle = plumeMaxSwayDegrees * amplitudeMultiplier * _breathWeight * _plumeWave;
    }

    /// <summary>0..1 breath value for a time offset into the current cycle: paused at the bottom,
    /// eased up through a quicker inhale, paused at the top, then eased back down through a slower
    /// exhale.</summary>
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

    private void ScheduleNextBlink()
    {
        _blinkTimer = Random.Range(blinkIntervalSeconds.x, blinkIntervalSeconds.y);
    }

    private void UpdateBlink(float dt)
    {
        if (_rig == null) return;

        if (!_blinking)
        {
            _blinkTimer -= dt;
            if (_blinkTimer <= 0f)
            {
                _blinking = true;
                _rig.SetHeadBlinking(true);
                _blinkEndTime = _scaledTime + Random.Range(blinkDurationSeconds.x, blinkDurationSeconds.y);
            }
        }
        else if (_scaledTime >= _blinkEndTime)
        {
            _blinking = false;
            _rig.SetHeadBlinking(false);
            ScheduleNextBlink();
        }
    }
}
