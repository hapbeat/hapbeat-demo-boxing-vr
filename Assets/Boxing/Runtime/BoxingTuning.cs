using System;
using UnityEngine;

namespace Hapbeat.Boxing
{
    public enum BoxingPhase { Ready, Countdown, Fighting, Results }
    public enum BoxingOutcome { Win, Lose, Tie }
    public enum BoxingInputMode { Controllers, Hands }
    public enum ImpactMode { Continuous, WeakHard }
    public enum ImpactZone { LeftGlove, RightGlove, Head, Body }
    public enum ImpactSurface { Glove, Body }
    // The part that took the contact, for the haptic / sound cue only (scoring uses the surface).
    // A received punch: the player's glove or forearm. An own punch: the opponent's glove or arm (BoxingGuardArm).
    public enum ImpactPart { Glove, Forearm }

    [CreateAssetMenu(menuName = "Hapbeat Boxing/Tuning")]
    public sealed class BoxingTuning : ScriptableObject
    {
        [Header("Round")]
        [Min(10)] public float roundSeconds = 90;
        [Min(1)] public float maximumHealth = 100;
        // Startup: steady tracking at the start position for this long, then the 3-2-1 countdown (no START press).
        [Min(0)] public float autoStartSeconds = 2;
        // Outside a Demo Session: the result stays this long after its presentation, then the next round starts.
        [Min(0)] public float restartAfterResultsSeconds = 4;
        // Demo Session: the completion panel appears this long after the final bell (the bell and voice keep playing).
        [Min(0)] public float resultPanelDelaySeconds = 1;
        // Opponent pace: 1.6 is the reference; larger values slow every Neutral decision proportionally.
        [Min(0.1f)] public float enemyInterval = 1.6f;
        // Reference wind-up; each strike scales it (jab short, hook long, follow-ups and counters shorter).
        [Min(0.1f)] public float telegraphSeconds = 0.42f;
        [Min(0.08f)] public float strikeSeconds = 0.22f;
        [Min(0.1f)] public float recoverSeconds = 0.55f;
        // Reference hold for a covering guard (after an attack, a hit or a Hold choice).
        [Min(0.4f)] public float guardSeconds = 1.2f;
        [Min(0.5f)] public float playRadius = 1.0f;
        // Tracking dropouts shorter than this keep the round running (the lost hand holds still and takes no part in contacts);
        // a longer loss pauses with the tracking warning.
        [Min(0)] public float trackingGraceSeconds = 3;
        [Header("Opponent AI")]
        [Range(0, 1)] public float opponentSkill = 0.6f;
        // Perception delay and the chance to act on a read punch, at skill 0 and at skill 1.
        [Min(0)] public float opponentReactionSlow = 0.30f;
        [Min(0)] public float opponentReactionFast = 0.15f;
        [Range(0, 1)] public float opponentReadChanceLow = 0.35f;
        [Range(0, 1)] public float opponentReadChanceHigh = 0.85f;
        // A glove closing on the opponent faster than this is a threat; it is extrapolated this far ahead.
        [Min(0.1f)] public float opponentThreatSpeed = 1.2f;
        [Min(0)] public float opponentPredictSeconds = 0.15f;
        // Remembered hits per zone halve over this time.
        [Min(0.5f)] public float opponentHeatHalfLife = 6;
        // Player punch launches / landed hits within the window that trigger a counter or a step out.
        [Min(2)] public int opponentFlurryPunches = 3;
        [Min(0.1f)] public float opponentFlurrySeconds = 1.5f;
        [Min(2)] public int opponentFlurryHits = 3;
        [Min(0.1f)] public float opponentFlurryHitSeconds = 2.5f;
        [Header("Opponent motion (critically damped springs: seconds, m/s^2)")]
        [Min(0.02f)] public float opponentGuardSmoothSeconds = 0.12f;
        [Min(0.02f)] public float opponentDefendSmoothSeconds = 0.10f;
        [Min(0.02f)] public float opponentFistReturnSeconds = 0.20f;
        [Min(0.02f)] public float opponentHeadSmoothSeconds = 0.12f;
        [Min(0.02f)] public float opponentRootSmoothSeconds = 0.25f;
        [Min(1)] public float opponentHandMaxAcceleration = 45;
        [Min(1)] public float opponentHeadMaxAcceleration = 25;
        [Min(1)] public float opponentRootMaxAcceleration = 6;
        [Header("Collision (metres / seconds)")]
        [Min(0.01f)] public float gloveRadius = 0.07f;
        [Min(0.01f)] public float enemyGloveRadius = 0.09f;
        [Min(0.01f)] public float headRadius = 0.14f;
        [Min(0.01f)] public float enemyHeadRadius = 0.145f;
        [Min(0.01f)] public float enemyBodyRadius = 0.21f;
        [Min(0)] public float minimumImpactSpeed = 0.25f;
        [Min(0.01f)] public float hitCooldown = 0.22f;
        [Header("Player punch response (HMD-relative motion)")]
        [Min(.1f)] public float punchReturnDistance = .70f;
        [Min(.01f)] public float punchRetraction = .10f;
        [Min(.1f)] public float punchRepeatSeconds = 1f;
        [Min(.1f)] public float punchMinimumSpeed = .45f;
        [Min(.2f)] public float punchFullSpeed = 2.5f;
        [Range(0,1)] public float punchReadyStrength = .72f;
        // A prepared punch at or above this strength is hard (sound, haptics and the HARD label), so the
        // near-full damage punches (0.9 -> 18 of 20) are always hard. 0.9 = a peak hand speed of about 1.8 m/s.
        [Range(0, 1)] public float punchHardStrength = 0.9f;
        [Min(0)] public float tapDamage = 0.5f;
        [Min(1)] public float fullPunchDamage = 20;
        [Header("Impact response")]
        public ImpactMode impactMode = ImpactMode.WeakHard;
        [Min(0.1f)] public float hardHitSpeed = 2.5f;
        [Min(0.1f)] public float fullGainSpeed = 6f;
        [Range(0, 1)] public float minimumGain = 0.12f;
        [Range(0, 2)] public float maximumGain = 1f;
        [Range(0.1f, 3)] public float gainExponent = 0.85f;

        public float Gain(float relativeSpeed)
        {
            if (!float.IsFinite(relativeSpeed) || relativeSpeed < minimumImpactSpeed) return 0;
            float t = Mathf.InverseLerp(minimumImpactSpeed, Mathf.Max(minimumImpactSpeed + 0.01f, fullGainSpeed), relativeSpeed);
            return Mathf.Lerp(minimumGain, maximumGain, Mathf.Pow(t, gainExponent));
        }
        public bool IsHard(float speed) => impactMode == ImpactMode.WeakHard && speed >= hardHitSpeed;
        public float PunchStrengthForSpeed(float speed) => float.IsFinite(speed) && speed>=punchMinimumSpeed ?
            Mathf.Lerp(punchReadyStrength,1,Mathf.InverseLerp(punchMinimumSpeed,Mathf.Max(punchMinimumSpeed+.01f,punchFullSpeed),speed)) : 0;
    }

    public struct BoxerPose
    {
        public Vector3 head, left, right;
        public Quaternion headRotation, leftRotation, rightRotation;
        public bool valid, visualValid, leftClosed, rightClosed;
        // Per-part tracking (valid = all three). Test poses set only valid.
        public bool headTracked, leftTracked, rightTracked;
        public double timestamp;
    }

    public readonly struct BoxingImpact
    {
        public readonly ImpactZone zone;
        public readonly float relativeSpeed, gain, damage;
        public readonly bool hard, attack;
        public readonly ImpactSurface surface;
        public readonly ImpactPart part;
        public readonly Vector3 point;
        public BoxingImpact(ImpactZone zone, float speed, bool attack, Vector3 point, BoxingTuning tuning, ImpactSurface surface, ImpactPart part = ImpactPart.Glove)
        {
            this.zone = zone; relativeSpeed = speed; gain = tuning.Gain(speed);
            hard = tuning.IsHard(speed); this.attack = attack; this.point = point; this.surface = surface; this.part = part;
            damage = hard ? 20 : 10;
        }
        public BoxingImpact(ImpactZone zone, float speed, Vector3 point, BoxingTuning tuning, ImpactSurface surface, float punchStrength, ImpactPart part = ImpactPart.Glove)
        {
            this.zone = zone; relativeSpeed = speed; attack = true; this.point = point; this.surface = surface; this.part = part;
            float strength = Mathf.Clamp01(punchStrength);
            gain = Mathf.Lerp(tuning.minimumGain, tuning.maximumGain, strength);
            // Soft / hard by the punch strength, like the damage: a full-damage punch is always hard and an
            // unprepared tap (strength 0) stays soft. The opponent's punches still switch by the contact speed.
            hard = tuning.impactMode == ImpactMode.WeakHard && strength >= tuning.punchHardStrength;
            damage = Mathf.Lerp(tuning.tapDamage, tuning.fullPunchDamage, strength);
        }
    }

    public static class BoxingCollision
    {
        // Sweep in relative space: both participants can move during the sample.
        public static bool Sweep(Vector3 a0, Vector3 a1, float ar, Vector3 b0, Vector3 b1, float br, out float t)
        {
            Vector3 p = a0 - b0, d = (a1 - a0) - (b1 - b0);
            float r = ar + br, c = p.sqrMagnitude - r * r;
            if (c <= 0) { t = 0; return true; }
            float a = d.sqrMagnitude, b = Vector3.Dot(p, d), discriminant = b * b - a * c;
            if (a < 1e-10f || discriminant < 0) { t = 0; return false; }
            t = (-b - Mathf.Sqrt(discriminant)) / a;
            return t >= 0 && t <= 1;
        }
        public static float RelativeSpeed(Vector3 a0, Vector3 a1, Vector3 b0, Vector3 b1, float dt) =>
            dt > 0 ? ((a1 - a0) - (b1 - b0)).magnitude / dt : 0;
    }

    public sealed class BoxingRound
    {
        public BoxingPhase Phase { get; private set; } = BoxingPhase.Ready;
        public float TimeLeft { get; private set; }
        public float Countdown { get; private set; }
        public int Hits { get; private set; }
        public int Blocks { get; private set; }
        public int EnemyBlocks { get; private set; }
        public int Taken { get; private set; }
        public int Dodges { get; private set; }
        public int Score { get; private set; }
        public float MaximumHealth { get; private set; } = 100;
        public float PlayerHealth { get; private set; } = 100;
        public float EnemyHealth { get; private set; } = 100;
        public BoxingOutcome Outcome => PlayerHealth > EnemyHealth ? BoxingOutcome.Win :
            PlayerHealth < EnemyHealth ? BoxingOutcome.Lose : BoxingOutcome.Tie;
        public void Start(float duration, float maximumHealth = 100)
        {
            MaximumHealth = Mathf.Max(1, maximumHealth);
            PlayerHealth = EnemyHealth = MaximumHealth;
            TimeLeft = duration; Countdown = 3; Hits = Blocks = EnemyBlocks = Taken = Dodges = Score = 0; Phase = BoxingPhase.Countdown;
        }
        public void Tick(float dt, bool paused)
        {
            if (paused || dt <= 0) return;
            if (Phase == BoxingPhase.Countdown)
            {
                Countdown = Mathf.Max(0, Countdown - dt);
                if (Countdown <= 0) Phase = BoxingPhase.Fighting;
            }
            else if (Phase == BoxingPhase.Fighting)
            {
                TimeLeft = Mathf.Max(0, TimeLeft - dt);
                if (TimeLeft <= 0) Phase = BoxingPhase.Results;
            }
        }
        public void Report(BoxingImpact impact)
        {
            if (Phase != BoxingPhase.Fighting) return;
            float damage = impact.damage;
            if (impact.surface == ImpactSurface.Glove)
            {
                if (impact.attack) EnemyBlocks++; else { Blocks++; Score += 15; }
            }
            else if (impact.attack) { Hits++; Score += Mathf.RoundToInt(20 + 80 * Mathf.Clamp01(impact.gain)); EnemyHealth = Mathf.Max(0, EnemyHealth - damage); }
            else if (impact.zone == ImpactZone.Head || impact.zone == ImpactZone.Body) { Taken++; PlayerHealth = Mathf.Max(0, PlayerHealth - damage); }
            if (PlayerHealth <= 0 || EnemyHealth <= 0) Phase = BoxingPhase.Results;
        }
        public void Dodge() { if (Phase == BoxingPhase.Fighting) { Dodges++; Score += 10; } }
    }
}
