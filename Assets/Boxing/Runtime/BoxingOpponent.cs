using UnityEngine;

// The haptic recording (Editor BoxingHapticRecord) drives the opponent through its internal script hooks.
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Hapbeat.Boxing.Editor")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Hapbeat.Boxing.Tests")]

namespace Hapbeat.Boxing
{
    public enum BoxingAttack { HeadStraight, BodyStraight, HeadHook, BodyHook }
    public enum BoxingGuard { HeadStraight, BodyStraight, HeadHook, BodyHook }
    // Where a player punch landed. Left/Right are the opponent head's world -X/+X sides (the Opponent.Left/Right glove sides).
    public enum BoxingHitZone { HeadLeft, HeadRight, HeadCenter, Body }
    public enum BoxingOpponentState { Neutral, Attack, Defend, Stagger, Counter }
    public enum BoxingAction { Jab, Cross, LeadHook, RearHook, BodyStraight, BodyHook, JabCross, JabCrossHook, BodyHead, HookCross, Feint, StepIn, StepOut, Circle, Hold }
    public enum BoxingEvade { None, Slip, Duck, Sway }

    // Hierarchical states with a utility choice in Neutral. The player is read through a delayed,
    // deliberately unreliable perception, and hits are remembered per zone. Collision stays with
    // BoxingGame: guards and dodges only work by moving Head/Body/Left/Right. Motion follows targets
    // through critically damped springs; strikes use minimum-jerk curves. No per-frame allocation.
    public sealed class BoxingOpponent
    {
        public Vector3 Head { get; private set; }
        public Vector3 Body { get; private set; }
        public Vector3 Left { get; private set; }
        public Vector3 Right { get; private set; }
        public Vector3 Root { get; private set; }
        public bool Striking { get; private set; }
        public bool Telegraphing { get; private set; }
        public bool Guarding { get; private set; }
        public BoxingGuard Guard { get; private set; }
        public bool BodyGuard => Guard==BoxingGuard.BodyStraight || Guard==BoxingGuard.BodyHook;
        public bool HookGuard => Guard==BoxingGuard.HeadHook || Guard==BoxingGuard.BodyHook;
        // A hook guard covers one side: -1 = Left (world -X, the Left glove's side), +1 = Right. The other glove stays
        // in the high guard at the chin. Straight guards use both gloves alike.
        public float GuardSide { get; private set; } = 1;
        // 0..1: how closely both gloves have reached the committed guard pose (1 = settled).
        public float GuardWeight { get; private set; }
        public int AttackId { get; private set; }
        public bool AttackLeft { get; private set; }
        public int CompletedAttacks { get; private set; }
        public float StandingHeadHeight => height + .075f;
        public float MotionWeight { get; private set; }
        public float BodyYaw { get; private set; }
        public BoxingAttack Attack { get; private set; }
        public bool Hook => attacking && (Attack == BoxingAttack.HeadHook || Attack == BoxingAttack.BodyHook);
        public bool BodyAttack => attacking && (Attack == BoxingAttack.BodyStraight || Attack == BoxingAttack.BodyHook);
        public bool Counter { get; private set; }
        public BoxingOpponentState State { get; private set; }
        // Last chosen behaviour (Neutral choices and counters) and how many have been chosen since Reset.
        public BoxingAction Action { get; private set; }
        public int Actions { get; private set; }
        // A wind-up that never strikes. Telegraphing stays reserved for real strikes.
        public bool Feinting { get; private set; }
        public BoxingEvade Evade { get; private set; }
        // Haptic recording and the tutorial: no own choices, reads, staggers or covers; strikes only through ScriptedStrike.
        internal bool Scripted { get; set; }

        private const float HomeZ = 1.08f, MaxStep = 1f / 240, OpeningSeconds = 1.2f;
        private const float HeatGain = 1.2f, StanceBonus = 3.5f, GuardTolerance = .2f;
        private static readonly Vector3 RestLeft = new Vector3(-.19f, -.10f, -.23f), RestRight = new Vector3(.18f, -.09f, -.19f);
        private readonly struct Strike
        {
            public readonly BoxingAttack attack; public readonly bool left;
            public Strike(BoxingAttack attack, bool left) { this.attack = attack; this.left = left; }
        }
        private static readonly Strike Jab = new Strike(BoxingAttack.HeadStraight, true), Cross = new Strike(BoxingAttack.HeadStraight, false);
        private static readonly Strike LeadHook = new Strike(BoxingAttack.HeadHook, true);
        // Indexed by BoxingAction for the attacking actions (Jab..HookCross). Left is the lead hand.
        private static readonly Strike[][] Combos = {
            new[] { Jab }, new[] { Cross }, new[] { LeadHook }, new[] { new Strike(BoxingAttack.HeadHook, false) },
            new[] { new Strike(BoxingAttack.BodyStraight, false) }, new[] { new Strike(BoxingAttack.BodyHook, true) },
            new[] { Jab, Cross }, new[] { Jab, Cross, LeadHook },
            new[] { new Strike(BoxingAttack.BodyStraight, true), LeadHook }, new[] { LeadHook, Cross },
        };
        // Close base weights: the situation, the repetition penalty and the noise decide, not a fixed favourite.
        private static readonly float[] BaseWeight = { 1f, .9f, .85f, .8f, .85f, .8f, .95f, .8f, .8f, .8f, .6f, .7f, .6f, .75f, .85f };

        private readonly BoxingTuning tuning;
        private readonly System.Random seeds;
        private System.Random random;
        // The covered side of hook guards: its own stream (seeded with random's seed), so choosing a side leaves the
        // other choices' sequence unchanged.
        private System.Random sideRandom;
        private float height = 1.65f, floorHeight, clock;
        // Root in world space, head relative to Root, gloves relative to Head.
        private Vector3 root, rootVelocity, headOffset, headVelocity, leftOffset, leftVelocity, rightOffset, rightVelocity;
        private Vector2 rootGoal;
        private float goalTime, stateTime, stateDuration, decisionWait, guardTime, feintBoost;
        private bool attacking, retracting, pendingStrike, hitDuringDefense, hitFlurry, punchFlurry;
        private Strike[] combo = Combos[0];
        private int comboIndex;
        private float telegraphScale = 1, strikeScale = 1, phaseTime, phaseDuration, retractFrom;
        private Vector3 aim, strikeStart, strikeVelocity, bulge, evadeOffset;
        private float staggerPush;
        private BoxingGuard coverGuard;
        private float coverSide = 1;
        private readonly float[] heat = new float[4], hitTimes = new float[8], launchTimes = new float[8], guardWeights = new float[4];
        private int hitNext, launchNext;
        private readonly int[] recent = new int[3];
        // Perception: a ring of observed player samples read with the reaction delay.
        private struct Sample { public float time; public Vector3 head, left, right, leftVelocity, rightVelocity; }
        private readonly Sample[] samples = new Sample[128];
        private int sampleNext, sampleCount;
        private float observeClock;
        private bool tracked, leftLaunched, rightLaunched, leftThreat, rightThreat;
        private Vector3 lastLeft, lastRight, observedLeftVelocity, observedRightVelocity;
        private bool stanceKnown, headOpen, bodyOpen, leftOpen, rightOpen;
        private BoxingGuard likelyGuard;

        private float Skill => Mathf.Clamp01(tuning.opponentSkill);
        private float ReactionSeconds => Mathf.Lerp(tuning.opponentReactionSlow, tuning.opponentReactionFast, Skill);
        private float ReadChance => Mathf.Lerp(tuning.opponentReadChanceLow, tuning.opponentReadChanceHigh, Skill);
        private float Pace => tuning.enemyInterval / 1.6f;
        private float Next(float a, float b) => Mathf.Lerp(a, b, (float)random.NextDouble());

        // Without a seed every Reset starts an unpredictable sequence; with one the sequence of sequences is reproducible.
        public BoxingOpponent(BoxingTuning tuning, int? seed = null)
        {
            this.tuning = tuning;
            seeds = seed.HasValue ? new System.Random(seed.Value) : new System.Random();
            Reset(1.65f);
        }
        public void Reset(float playerHeight,float floor=0)
        {
            height = Mathf.Max(.2f,playerHeight); floorHeight=floor; clock = 0;
            int seed = seeds.Next(); random = new System.Random(seed); sideRandom = new System.Random(seed ^ 0x5bd1e995);
            StopCombat(); decisionWait = OpeningSeconds * Pace;
            Action = BoxingAction.Hold; Actions = 0; Attack = BoxingAttack.HeadStraight; AttackLeft = false; AttackId = CompletedAttacks = 0;
            Guard = coverGuard = BoxingGuard.HeadStraight; GuardSide = coverSide = 1;
            for (int i = 0; i < heat.Length; i++) heat[i] = 0;
            for (int i = 0; i < hitTimes.Length; i++) hitTimes[i] = launchTimes[i] = float.NegativeInfinity;
            for (int i = 0; i < recent.Length; i++) recent[i] = -1;
            sampleCount = sampleNext = 0; observeClock = 0; feintBoost = 0;
            tracked = leftLaunched = rightLaunched = leftThreat = rightThreat = stanceKnown = false;
            root = new Vector3(0, floorHeight, HomeZ); rootGoal = Vector2.zero; goalTime = 0;
            headOffset = new Vector3(0, height, 0); leftOffset = RestLeft; rightOffset = RestRight;
            rootVelocity = headVelocity = leftVelocity = rightVelocity = Vector3.zero;
            Apply();
        }
        public static BoxingHitZone Zone(bool head, Vector3 punchVelocity)
        {
            if (!head) return BoxingHitZone.Body;
            if (Mathf.Abs(punchVelocity.x) <= .6f * Mathf.Abs(punchVelocity.z)) return BoxingHitZone.HeadCenter;
            // Travelling towards +X, the glove arrives from the -X (Left) side.
            return punchVelocity.x > 0 ? BoxingHitZone.HeadLeft : BoxingHitZone.HeadRight;
        }
        // Called every frame before Tick. dt = 0 restarts the track (first frame or after a tracking gap).
        public void ObservePlayer(BoxerPose pose, float dt)
        {
            if (!pose.valid) { tracked = false; return; }
            if (tracked && dt > 0)
            {
                float k = 1 - Mathf.Exp(-dt / .03f);
                observedLeftVelocity = Vector3.Lerp(observedLeftVelocity, (pose.left - lastLeft) / dt, k);
                observedRightVelocity = Vector3.Lerp(observedRightVelocity, (pose.right - lastRight) / dt, k);
                observeClock += dt;
            }
            else observedLeftVelocity = observedRightVelocity = Vector3.zero;
            tracked = true; lastLeft = pose.left; lastRight = pose.right;
            bool l = Approaching(pose.left, observedLeftVelocity), r = Approaching(pose.right, observedRightVelocity);
            if (l && !leftLaunched) Launch();
            if (r && !rightLaunched) Launch();
            leftLaunched = l; rightLaunched = r;
            samples[sampleNext] = new Sample { time = observeClock, head = pose.head, left = pose.left, right = pose.right,
                leftVelocity = observedLeftVelocity, rightVelocity = observedRightVelocity };
            sampleNext = (sampleNext + 1) % samples.Length; sampleCount = Mathf.Min(sampleCount + 1, samples.Length);
        }
        public void React(float gain, BoxingHitZone zone)
        {
            if (Scripted) return;
            heat[(int)zone] += 1;
            hitTimes[hitNext] = clock; hitNext = (hitNext + 1) % hitTimes.Length;
            if (State == BoxingOpponentState.Defend) hitDuringDefense = true;
            if (Recent(hitTimes, clock, tuning.opponentFlurryHitSeconds) >= tuning.opponentFlurryHits) hitFlurry = true;
            // A committed strike still lands; anything else breaks into a short stagger.
            if (!Striking) BeginStagger(gain);
        }
        public void Block(Vector3 contact)
        {
            if (!Striking) return;
            EndStrike(contact);
        }
        public void Tick(float dt, Vector3 playerHead, Quaternion playerHeadRotation, bool fighting)
        {
            if (dt <= 0) return;
            clock += dt;
            float decay = Mathf.Pow(.5f, dt / Mathf.Max(.1f, tuning.opponentHeatHalfLife));
            for (int i = 0; i < heat.Length; i++) heat[i] *= decay;
            if (fighting) Think(dt, playerHead, playerHeadRotation);
            else { StopCombat(); decisionWait = OpeningSeconds * Pace; rootGoal = Vector2.zero; goalTime = 0; }
            Move(dt, fighting);
        }

        private void StopCombat()
        {
            State = BoxingOpponentState.Neutral; attacking = retracting = pendingStrike = hitFlurry = punchFlurry = false;
            Striking = Telegraphing = Feinting = Guarding = Counter = false; GuardWeight = 0;
            Evade = BoxingEvade.None; evadeOffset = Vector3.zero; staggerPush = 0; MotionWeight = BodyYaw = 0;
        }
        private void Think(float dt, Vector3 playerHead, Quaternion playerHeadRotation)
        {
            stateTime += dt;
            if (Scripted) { if (State == BoxingOpponentState.Attack) AttackTick(dt, playerHead, playerHeadRotation); return; }
            if (goalTime > 0 && (goalTime -= dt) <= 0) rootGoal = Vector2.zero;
            feintBoost = Mathf.Max(0, feintBoost - dt);
            // The head comes back from a hit slowly (below the impact threshold), not with a second spring snap.
            staggerPush = Mathf.Max(0, staggerPush - .2f * dt);
            ReadPlayer();
            switch (State)
            {
                case BoxingOpponentState.Neutral:
                    if (Guarding && (guardTime -= dt) <= 0) Guarding = false;
                    if (hitFlurry || punchFlurry) { HandleFlurry(); break; }
                    if ((decisionWait -= dt) <= 0) Decide();
                    break;
                case BoxingOpponentState.Attack: case BoxingOpponentState.Counter: AttackTick(dt, playerHead, playerHeadRotation); break;
                case BoxingOpponentState.Defend: if (stateTime >= stateDuration) EndDefend(); break;
                case BoxingOpponentState.Stagger: if (stateTime >= stateDuration) EndStagger(); break;
            }
        }

        // ---- Perception ----
        private bool Approaching(Vector3 glove, Vector3 velocity) => Vector3.Dot(velocity, (Head - glove).normalized) > tuning.opponentThreatSpeed;
        private void Launch() { launchTimes[launchNext] = observeClock; launchNext = (launchNext + 1) % launchTimes.Length; }
        private static int Recent(float[] times, float now, float window)
        {
            int count = 0;
            foreach (float t in times) if (now - t >= 0 && now - t <= window) count++;
            return count;
        }
        private void ReadPlayer()
        {
            if (sampleCount == 0) return;
            float cutoff = observeClock - ReactionSeconds;
            int index = -1;
            for (int i = 1; i <= sampleCount; i++)
            {
                int k = (sampleNext - i + samples.Length) % samples.Length;
                if (samples[k].time <= cutoff) { index = k; break; }
            }
            if (index < 0) return;
            Sample s = samples[index];
            Vector3 l = s.left - s.head, r = s.right - s.head;
            float y = (l.y + r.y) * .5f;
            headOpen = y < -.27f; bodyOpen = y > -.15f;
            leftOpen = l.x < -.26f || l.y < -.3f; rightOpen = r.x > .26f || r.y < -.3f;
            bool low = y < -.27f, wide = (Mathf.Abs(l.x) + Mathf.Abs(r.x)) * .5f > .26f;
            likelyGuard = wide ? (low ? BoxingGuard.BodyHook : BoxingGuard.HeadHook) : (low ? BoxingGuard.BodyStraight : BoxingGuard.HeadStraight);
            stanceKnown = true;
            Threat(s.left, s.leftVelocity, ref leftThreat);
            Threat(s.right, s.rightVelocity, ref rightThreat);
            if (Recent(launchTimes, s.time, tuning.opponentFlurrySeconds) >= tuning.opponentFlurryPunches)
            {
                for (int i = 0; i < launchTimes.Length; i++) launchTimes[i] = float.NegativeInfinity;
                punchFlurry = true;
            }
        }
        // Predict where an incoming glove is heading; a read is never certain.
        private void Threat(Vector3 glove, Vector3 velocity, ref bool active)
        {
            Vector3 to = Head - glove;
            if (Vector3.Dot(velocity, to.normalized) <= tuning.opponentThreatSpeed) { active = false; return; }
            if (active) return;
            active = true;
            bool canDefend = State == BoxingOpponentState.Neutral || State == BoxingOpponentState.Defend ||
                ((State == BoxingOpponentState.Attack || State == BoxingOpponentState.Counter) && !Striking);
            if (!canDefend) return;
            Vector3 ahead = glove + velocity * tuning.opponentPredictSeconds;
            Vector3 flat = new Vector3(to.x, 0, to.z).normalized, planar = new Vector3(velocity.x, 0, velocity.z);
            float forward = Vector3.Dot(planar, flat);
            bool hook = (planar - flat * forward).magnitude > .6f * Mathf.Abs(forward);
            var zone = ahead.y <= Head.y - .25f ? BoxingHitZone.Body : !hook ? BoxingHitZone.HeadCenter :
                velocity.x > 0 ? BoxingHitZone.HeadLeft : BoxingHitZone.HeadRight;
            float chance = ReadChance * (1 + .15f * Mathf.Min(2, heat[(int)zone]));
            // Travelling towards +X, the glove arrives from the Left (-X) side.
            if (random.NextDouble() < chance) BeginDefend(zone, hook, velocity.x > 0 ? -1 : 1);
        }

        // ---- Choices ----
        private void Record(BoxingAction action)
        {
            recent[2] = recent[1]; recent[1] = recent[0]; recent[0] = (int)action;
            Action = action; Actions++;
        }
        private float Utility(int a)
        {
            var action = (BoxingAction)a; float w = BaseWeight[a];
            if (a <= (int)BoxingAction.HookCross)
            {
                bool body = action == BoxingAction.BodyStraight || action == BoxingAction.BodyHook || action == BoxingAction.BodyHead;
                if (body ? bodyOpen : headOpen) w *= body ? 1.8f : 1.6f;
                if (leftOpen && (action == BoxingAction.LeadHook || action == BoxingAction.HookCross || action == BoxingAction.JabCrossHook)) w *= 1.8f;
                if (rightOpen && action == BoxingAction.RearHook) w *= 1.8f;
                if (feintBoost > 0) w *= 1.4f;
                if (rootGoal.y < 0) w *= 1.3f;
            }
            int pressure = Recent(hitTimes, clock, tuning.opponentFlurryHitSeconds);
            if (action == BoxingAction.Hold || action == BoxingAction.StepOut) w *= 1 + .5f * pressure;
            if (action == BoxingAction.StepIn && rootGoal.y < -.01f) w *= .2f;
            if (action == BoxingAction.StepOut && rootGoal.y > .01f) w *= .2f;
            foreach (int r in recent) if (r == a) w *= .35f;
            return w + (float)random.NextDouble() * .5f;
        }
        private void Decide()
        {
            int best = 0; float score = float.MinValue;
            for (int a = 0; a < BaseWeight.Length; a++) { float s = Utility(a); if (s > score) { score = s; best = a; } }
            var action = (BoxingAction)best; Record(action);
            switch (action)
            {
                case BoxingAction.Feint: BeginFeint(); break;
                case BoxingAction.StepIn: Step(new Vector2(rootGoal.x, -.06f)); break;
                case BoxingAction.StepOut: Step(new Vector2(rootGoal.x, .07f)); break;
                case BoxingAction.Circle: Step(new Vector2(root.x > 0 ? -.11f : .11f, rootGoal.y)); break;
                case BoxingAction.Hold: Cover(ChooseGuard(null), tuning.guardSeconds * Next(.7f, 1.1f), ChooseSide()); decisionWait = guardTime; break;
                default: BeginAttack(action, false); break;
            }
        }
        private void Step(Vector2 goal) { rootGoal = goal; goalTime = 1.5f; decisionWait = Next(.3f, .55f) * Pace; }
        private void Cover(BoxingGuard guard, float seconds, float side) { Guarding = true; Guard = guard; GuardSide = side; guardTime = seconds; }
        // Without a read: the side the player's wide (likely hooking) glove threatens, else the side hit more, else either.
        // The player's left glove (world -X) hooks towards +X and arrives on the Left side.
        private float ChooseSide()
        {
            float left = heat[(int)BoxingHitZone.HeadLeft] + (stanceKnown && leftOpen ? 1 : 0);
            float right = heat[(int)BoxingHitZone.HeadRight] + (stanceKnown && rightOpen ? 1 : 0);
            if (Mathf.Abs(left - right) > .01f) return left > right ? -1 : 1;
            return sideRandom.NextDouble() < .5 ? -1 : 1;
        }
        private BoxingGuard ChooseGuard(BoxingGuard? read)
        {
            float total = 0;
            for (int g = 0; g < guardWeights.Length; g++)
            {
                float w = read.HasValue ? ((int)read.Value == g ? 6f : .2f) : 1f;
                if (!read.HasValue && stanceKnown && (int)likelyGuard == g) w *= 1 + StanceBonus * (.5f + .5f * Skill);
                guardWeights[g] = w * Mathf.Exp(HeatGain * CoverHeat((BoxingGuard)g)); total += guardWeights[g];
            }
            float pick = (float)random.NextDouble() * total;
            for (int g = 0; g < guardWeights.Length - 1; g++) { pick -= guardWeights[g]; if (pick < 0) return (BoxingGuard)g; }
            return (BoxingGuard)(guardWeights.Length - 1);
        }
        private float CoverHeat(BoxingGuard guard) => guard == BoxingGuard.HeadStraight ? heat[(int)BoxingHitZone.HeadCenter] :
            guard == BoxingGuard.HeadHook ? heat[(int)BoxingHitZone.HeadLeft] + heat[(int)BoxingHitZone.HeadRight] : heat[(int)BoxingHitZone.Body];
        private void HandleFlurry()
        {
            bool counter = hitFlurry || random.NextDouble() < .65;
            hitFlurry = punchFlurry = false;
            if (counter) { StartCounter(); return; }
            State = BoxingOpponentState.Neutral; Record(BoxingAction.StepOut); Step(new Vector2(rootGoal.x, .07f));
            Cover(ChooseGuard(null), tuning.guardSeconds * .6f, ChooseSide());
        }

        // ---- Defence ----
        private void CancelAttack()
        {
            if (retracting) CompletedAttacks++;
            attacking = retracting = pendingStrike = false; Telegraphing = Feinting = Counter = false;
            Evade = BoxingEvade.None; evadeOffset = Vector3.zero;
        }
        private void BeginDefend(BoxingHitZone zone, bool hook, float side)
        {
            CancelAttack();
            State = BoxingOpponentState.Defend; stateTime = 0; stateDuration = .5f; hitDuringDefense = false;
            var read = zone == BoxingHitZone.Body ? (hook ? BoxingGuard.BodyHook : BoxingGuard.BodyStraight) :
                zone == BoxingHitZone.HeadCenter ? BoxingGuard.HeadStraight : BoxingGuard.HeadHook;
            // A read hook covers its side; a guess at another guard still picks one side.
            Cover(ChooseGuard(read), stateDuration, hook ? side : ChooseSide());
            if (zone == BoxingHitZone.Body || random.NextDouble() >= .15f + .35f * Skill) return;
            if (zone != BoxingHitZone.HeadCenter) { Evade = BoxingEvade.Duck; evadeOffset = new Vector3(0, -Next(.12f, .16f), 0); }
            else if (random.NextDouble() < .6)
            { Evade = BoxingEvade.Slip; evadeOffset = new Vector3((random.NextDouble() < .5 ? -1 : 1) * Next(.10f, .14f), -.03f, 0); }
            else { Evade = BoxingEvade.Sway; evadeOffset = new Vector3(0, -.01f, Next(.08f, .10f)); }
        }
        private void EndDefend()
        {
            Evade = BoxingEvade.None; evadeOffset = Vector3.zero;
            State = BoxingOpponentState.Neutral; guardTime = .3f; decisionWait = .25f * Pace;
            if (hitFlurry || punchFlurry) { HandleFlurry(); return; }
            // A clean block or dodge opens a short counter window.
            if (!hitDuringDefense && random.NextDouble() < Mathf.Lerp(.5f, .9f, Skill)) StartCounter();
        }
        private void BeginStagger(float gain)
        {
            CancelAttack(); Guarding = false;
            State = BoxingOpponentState.Stagger; stateTime = 0;
            stateDuration = .12f + .12f * Mathf.Clamp01(gain); staggerPush = .05f + .08f * Mathf.Clamp01(gain);
        }
        private void EndStagger()
        {
            State = BoxingOpponentState.Neutral;
            if (hitFlurry || punchFlurry) { HandleFlurry(); return; }
            Cover(ChooseGuard(null), tuning.guardSeconds * Next(.5f, .8f), ChooseSide());
            decisionWait = guardTime * Mathf.Lerp(1, .6f, Skill);
        }

        // ---- Attacks ----
        private void StartCounter()
        {
            int best = 0; float score = float.MinValue;
            for (int a = 0; a <= (int)BoxingAction.HookCross; a++)
            {
                if (a == (int)BoxingAction.JabCrossHook) continue;
                float s = Utility(a); if (s > score) { score = s; best = a; }
            }
            Record((BoxingAction)best); BeginAttack((BoxingAction)best, true);
        }
        // The first strike starts on the next tick, never in the tick that completed an earlier one.
        private void BeginAttack(BoxingAction action, bool counter)
        {
            State = counter ? BoxingOpponentState.Counter : BoxingOpponentState.Attack;
            combo = Combos[(int)action]; comboIndex = 0; telegraphScale = counter ? .6f : 1;
            Guarding = false; attacking = pendingStrike = true; retracting = false; strikeScale = 1;
        }
        // Haptic recording: one strike of this kind, its strike phase strikeScale times the usual length (> 1 = slower).
        internal void ScriptedStrike(BoxingAttack attack, bool left, float strikeScale)
        {
            State = BoxingOpponentState.Attack; combo = new[] { new Strike(attack, left) }; comboIndex = 0; telegraphScale = 1;
            Guarding = false; attacking = pendingStrike = true; retracting = false; this.strikeScale = strikeScale;
        }
        // Tutorial: the scripted opponent holds this guard (no timeout) until ScriptedRelease or a strike.
        internal void ScriptedGuard(BoxingGuard guard, float side = 1) { Guarding = true; Guard = guard; GuardSide = side; }
        internal void ScriptedRelease() => Guarding = false;
        private void BeginFeint()
        {
            State = BoxingOpponentState.Attack; combo = Combos[0]; comboIndex = 0;
            Attack = random.NextDouble() < .5 ? BoxingAttack.HeadStraight : BoxingAttack.HeadHook; AttackLeft = random.NextDouble() < .6;
            Guarding = false; attacking = Feinting = true; retracting = pendingStrike = false;
            phaseTime = 0; phaseDuration = tuning.telegraphSeconds * .7f;
        }
        private static float TelegraphFactor(Strike s) => s.attack == BoxingAttack.HeadStraight ? (s.left ? .55f : .75f) :
            s.attack == BoxingAttack.HeadHook ? 1.1f : s.attack == BoxingAttack.BodyStraight ? .85f : 1.05f;
        private static float StrikeFactor(Strike s) => s.attack == BoxingAttack.HeadStraight ? (s.left ? .9f : 1f) :
            s.attack == BoxingAttack.BodyStraight ? 1.05f : 1.15f;
        private void StartStrike(Vector3 playerHead, Quaternion playerHeadRotation)
        {
            var s = combo[comboIndex];
            AttackId++; Attack = s.attack; AttackLeft = s.left; Counter = State == BoxingOpponentState.Counter;
            Telegraphing = true; Striking = retracting = false; phaseTime = 0;
            phaseDuration = tuning.telegraphSeconds * TelegraphFactor(s) * telegraphScale * (comboIndex > 0 ? .55f : 1);
            // Aim is committed at the start of the wind-up, so moving the head afterwards dodges.
            aim = BodyAttack ? BoxingPlayerBody.Aim(playerHead, playerHeadRotation) : playerHead;
        }
        private void AttackTick(float dt, Vector3 playerHead, Quaternion playerHeadRotation)
        {
            phaseTime += dt;
            if (pendingStrike) { pendingStrike = false; StartStrike(playerHead, playerHeadRotation); return; }
            if (Feinting)
            {
                if (phaseTime < phaseDuration) return;
                Feinting = attacking = false; State = BoxingOpponentState.Neutral;
                feintBoost = 1; decisionWait = Next(.1f, .3f) * Pace;
                return;
            }
            if (Telegraphing) { if (phaseTime >= phaseDuration) BeginStrikeMotion(); return; }
            if (Striking) { if (phaseTime >= phaseDuration) EndStrike(aim); return; }
            if (!retracting) return;
            if (hitFlurry) { BeginStagger(0); return; }
            if (phaseTime < phaseDuration) return;
            retracting = false; CompletedAttacks++;
            if (++comboIndex < combo.Length) pendingStrike = true; else FinishAttack();
        }
        private void BeginStrikeMotion()
        {
            Telegraphing = false; Striking = true; phaseTime = 0;
            phaseDuration = tuning.strikeSeconds * StrikeFactor(combo[comboIndex]) * strikeScale;
            strikeStart = AttackLeft ? Left : Right;
            strikeVelocity = rootVelocity + headVelocity + (AttackLeft ? leftVelocity : rightVelocity);
            float side = AttackLeft ? -1 : 1;
            // A hook is one curve: it swings wide and arrives sideways, with continuous velocity.
            bulge = Attack == BoxingAttack.HeadHook ? new Vector3(side * .30f, 0, .06f) :
                Attack == BoxingAttack.BodyHook ? new Vector3(side * .26f, -.04f, .04f) : Vector3.zero;
        }
        // Stops the fist where it is (end of the curve or the guard contact); a spring returns it from rest.
        private void EndStrike(Vector3 fist)
        {
            retractFrom = Extension();
            Striking = Telegraphing = false; retracting = true; phaseTime = 0;
            bool last = comboIndex >= combo.Length - 1;
            phaseDuration = last ? tuning.recoverSeconds : .16f;
            if (last) { coverGuard = ChooseGuard(null); coverSide = ChooseSide(); }
            Vector3 still = -(rootVelocity + headVelocity);
            if (AttackLeft) { Left = fist; leftOffset = fist - Head; leftVelocity = still; }
            else { Right = fist; rightOffset = fist - Head; rightVelocity = still; }
        }
        private void FinishAttack()
        {
            attacking = false; Counter = false; State = BoxingOpponentState.Neutral;
            if (Scripted) return;
            if (combo.Length > 1) { rootGoal = new Vector2(rootGoal.x, .05f); goalTime = .8f; }
            if (hitFlurry || punchFlurry) { HandleFlurry(); return; }
            Cover(coverGuard, tuning.guardSeconds * Next(.5f, .9f), coverSide);
            decisionWait = guardTime * Mathf.Lerp(1, .55f, Skill);
        }

        // ---- Motion ----
        private static float MinimumJerk(float t) => t * t * t * (10 + t * (-15 + 6 * t));
        // Zero position/velocity/acceleration at both ends except unit velocity at the start.
        private static float StartVelocityTerm(float t) => t + t * t * t * (-6 + t * (8 - 3 * t));
        private static float Swing(float t) => 16 * t * t * (1 - t) * (1 - t);
        private float Extension()
        {
            if (!attacking) return 0;
            float t = phaseDuration > 0 ? Mathf.Clamp01(phaseTime / phaseDuration) : 1;
            if (Feinting) return -.18f * Mathf.Sin(t * Mathf.PI);
            if (Telegraphing) return -.18f * Mathf.Sin(t * Mathf.PI * .5f);
            if (Striking) return Mathf.Lerp(-.18f, 1, MinimumJerk(t));
            if (retracting) return retractFrom * (1 - Mathf.SmoothStep(0, 1, t));
            return 0;
        }
        // side: this glove's side (-1 Left, +1 Right); cover: the side a hook guard covers. The covering glove of a hook guard
        // sits at the temple (head) or beside the cheek with its elbow dropped onto the flank (body, BoxingOpponentAvatar.Pole);
        // the other glove keeps the high guard at the chin.
        internal static Vector3 GuardOffset(BoxingGuard guard, float side, float cover)
        {
            switch (guard)
            {
                case BoxingGuard.HeadHook: if (side == cover) return new Vector3(side * .165f, .01f, -.065f); break;  // glove at the temple
                case BoxingGuard.BodyStraight: return new Vector3(side * .06f, -.35f, -.21f);                       // gloves before the solar plexus
                case BoxingGuard.BodyHook: if (side == cover) return new Vector3(side * .12f, -.17f, -.17f); break;   // glove before the jaw, elbow on the flank
            }
            return new Vector3(side * .065f, -.015f, -.20f);                                                       // high guard before the face
        }
        private Vector3 HandTarget(bool left, out float smooth)
        {
            float side = left ? -1 : 1; Vector3 rest = left ? RestLeft : RestRight;
            smooth = tuning.opponentGuardSmoothSeconds;
            if (Guarding)
            {
                if (State == BoxingOpponentState.Defend) smooth = tuning.opponentDefendSmoothSeconds;
                return GuardOffset(Guard, side, GuardSide);
            }
            if (!attacking) return rest;
            bool lastRetract = retracting && comboIndex >= combo.Length - 1;
            if (AttackLeft != left) return lastRetract ? GuardOffset(coverGuard, side, coverSide) : new Vector3(side * .075f, -.085f, -.20f); // other glove stays at the chin
            if (retracting) { smooth = tuning.opponentFistReturnSeconds; return lastRetract ? GuardOffset(coverGuard, side, coverSide) : rest; }
            if (Telegraphing || Feinting) return rest + new Vector3(side * (Hook ? .10f : .025f), BodyAttack ? -.18f : -.02f, .06f);
            return rest;
        }
        // Critically damped spring with an acceleration limit, sub-stepped so a long frame cannot diverge.
        private static void Follow(ref Vector3 value, ref Vector3 velocity, Vector3 target, float smoothTime, float maxAcceleration, float dt)
        {
            float omega = 2 / Mathf.Max(.01f, smoothTime);
            int steps = Mathf.Max(1, Mathf.CeilToInt(dt / MaxStep)); float h = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                Vector3 a = omega * omega * (target - value) - 2 * omega * velocity;
                float magnitude = a.magnitude;
                if (magnitude > maxAcceleration) a *= maxAcceleration / magnitude;
                velocity += a * h; value += velocity * h;
            }
        }
        private void Move(float dt, bool fighting)
        {
            Vector3 goal = new Vector3(0, floorHeight, HomeZ);
            if (fighting)
            {
                // Small rhythmic shuffle, a step in for attacks and the footwork goal.
                float stepX = (int)(clock / .42f) % 2 == 0 ? .02f : -.02f, stepZ = (int)(clock / .63f) % 2 == 0 ? .012f : -.012f;
                bool attack = State == BoxingOpponentState.Attack || State == BoxingOpponentState.Counter;
                goal.x = Mathf.Clamp(rootGoal.x + stepX, -.14f, .14f);
                goal.z = Mathf.Clamp(HomeZ + rootGoal.y + stepZ + (attack && !Feinting ? -.07f : 0), .985f, 1.175f);
            }
            Follow(ref root, ref rootVelocity, goal, tuning.opponentRootSmoothSeconds, tuning.opponentRootMaxAcceleration, dt);
            if (root.x < -.155f || root.x > .155f) { root.x = Mathf.Clamp(root.x, -.155f, .155f); rootVelocity.x = 0; }
            if (root.z < .975f || root.z > 1.185f) { root.z = Mathf.Clamp(root.z, .975f, 1.185f); rootVelocity.z = 0; }

            float extension = Extension();
            MotionWeight = Mathf.Max(0, extension);
            BodyYaw = (AttackLeft ? -1 : 1) * (Hook ? 42 : 24) * extension;
            Vector3 headGoal = new Vector3(0, height, staggerPush) + evadeOffset;
            if (attacking) headGoal += new Vector3(0, BodyAttack ? -.10f : 0, -(Hook ? .2f : .1f) * MotionWeight);
            Follow(ref headOffset, ref headVelocity, headGoal, tuning.opponentHeadSmoothSeconds, tuning.opponentHeadMaxAcceleration, dt);

            bool strikeLeft = Striking && AttackLeft, strikeRight = Striking && !AttackLeft;
            if (!strikeLeft) Follow(ref leftOffset, ref leftVelocity, HandTarget(true, out float ls), ls, tuning.opponentHandMaxAcceleration, dt);
            if (!strikeRight) Follow(ref rightOffset, ref rightVelocity, HandTarget(false, out float rs), rs, tuning.opponentHandMaxAcceleration, dt);
            Apply();
            if (Striking)
            {
                float t = Mathf.Clamp01(phaseTime / phaseDuration);
                Vector3 fist = strikeStart + (aim - strikeStart) * MinimumJerk(t) + strikeVelocity * (phaseDuration * StartVelocityTerm(t)) + bulge * Swing(t);
                if (AttackLeft) { Left = fist; leftOffset = fist - Head; } else { Right = fist; rightOffset = fist - Head; }
            }
            GuardWeight = 0;
            if (Guarding)
            {
                float error = Mathf.Max((leftOffset - GuardOffset(Guard, -1, GuardSide)).magnitude, (rightOffset - GuardOffset(Guard, 1, GuardSide)).magnitude);
                GuardWeight = Mathf.Clamp01(1 - error / GuardTolerance);
            }
        }
        private void Apply()
        {
            Root = root;
            Head = root + headOffset + new Vector3(Mathf.Sin(clock * 1.5f) * .025f, (Mathf.Cos(clock * 3) - 1) * .018f, 0);
            Body = Head + new Vector3(0, -.43f, .015f);
            Left = Head + leftOffset; Right = Head + rightOffset;
        }
    }
}
