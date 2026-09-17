using UnityEngine;

namespace Hapbeat.Boxing
{
    // Fixed, readable combinations. Aim is committed before the strike, so leaning works.
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
        public bool BodyGuard { get; private set; }
        public float GuardWeight { get; private set; }
        public int AttackId { get; private set; }
        public bool AttackLeft { get; private set; }
        public int CompletedAttacks { get; private set; }
        public float StandingHeadHeight => height + .075f;
        public float MotionWeight { get; private set; }
        public float BodyYaw { get; private set; }
        public bool Hook => attacking && pattern % 6 >= 4;
        private float clock, attackTime, wait, reaction;
        private int pattern;
        private bool attacking, blocked;
        private Vector3 aim, strikeStart, blockedAt;
        private float height = 1.65f;
        private float guardWait, guardTime;
        private System.Random random;
        private readonly BoxingTuning tuning;
        public BoxingOpponent(BoxingTuning tuning) { this.tuning = tuning; Reset(1.65f); }
        public void Reset(float playerHeight)
        {
            height = Mathf.Clamp(playerHeight, .8f, 2.2f); clock = attackTime = reaction = 0;
            MotionWeight = BodyYaw = 0;
            pattern = AttackId = CompletedAttacks = 0; wait = 0.7f; attacking = Striking = Telegraphing = blocked = false;
            random = new System.Random(1701); guardWait = tuning.guardInterval; guardTime = 0; Guarding = BodyGuard = false; GuardWeight = 0;
            SetRestPose();
        }
        public void React(float gain) => reaction = Mathf.Max(reaction, 0.10f + gain * 0.15f);
        public void Block(Vector3 contact)
        {
            if (!Striking) return;
            blocked = true; blockedAt = contact;
            attackTime = tuning.telegraphSeconds + tuning.strikeSeconds;
            Striking = Telegraphing = false;
            if (AttackLeft) Left = contact; else Right = contact;
        }
        public void Tick(float dt, Vector3 playerHead, bool fighting)
        {
            if (dt <= 0) return;
            clock += dt; reaction = Mathf.Max(0, reaction - dt);
            float preDuration = tuning.telegraphSeconds, strikeDuration = tuning.strikeSeconds;
            float extension = !attacking ? 0 : attackTime < preDuration ? -.18f * Mathf.Sin(Mathf.PI * .5f * attackTime / preDuration) :
                attackTime < preDuration + strikeDuration ? Mathf.SmoothStep(0, 1, (attackTime - preDuration) / strikeDuration) :
                1 - Mathf.SmoothStep(0, 1, (attackTime - preDuration - strikeDuration) / tuning.recoverSeconds);
            MotionWeight = Mathf.Max(0, extension);
            BodyYaw = (AttackLeft ? -1 : 1) * (Hook ? 42 : 24) * extension;
            SetRestPose(); Striking = Telegraphing = Guarding = false; GuardWeight = 0;
            if (!fighting) return;
            if (!attacking)
            {
                guardWait -= dt;
                if (guardTime > 0 || guardWait <= 0)
                {
                    if (guardTime == 0) BodyGuard = !BodyGuard;
                    guardTime += dt; Guarding = true;
                    GuardWeight = Mathf.SmoothStep(0, 1, Mathf.Clamp01(Mathf.Min(guardTime, tuning.guardSeconds - guardTime) / 0.18f));
                    Vector3 guardTarget = BodyGuard ? Body + new Vector3(0,.02f,-.26f) : Head + new Vector3(0,-.025f,-.25f);
                    Left = Vector3.Lerp(Left, guardTarget + Vector3.left*.105f, GuardWeight);
                    Right = Vector3.Lerp(Right, guardTarget + Vector3.right*.105f, GuardWeight);
                    if (guardTime >= tuning.guardSeconds) { guardTime = 0; guardWait = tuning.guardInterval * (0.65f + (float)random.NextDouble()); Guarding = false; }
                    return;
                }
                wait -= dt;
                if (wait > 0) return;
                attacking = true; blocked = false; attackTime = 0; AttackId++; AttackLeft = pattern % 3 != 1;
                aim = playerHead; strikeStart = AttackLeft ? Left : Right;
            }
            attackTime += dt;
            float pre = tuning.telegraphSeconds, strike = tuning.strikeSeconds, recover = tuning.recoverSeconds;
            Vector3 rest = AttackLeft ? Left : Right;
            Vector3 fist;
            bool hook = pattern % 6 >= 4;
            if (attackTime < pre)
            {
                Telegraphing = true;
                fist = rest + new Vector3(AttackLeft ? -0.025f : 0.025f, 0, 0.045f) * Mathf.Sin(attackTime / pre * Mathf.PI * 0.5f);
                strikeStart = fist;
            }
            else if (attackTime < pre + strike)
            {
                Striking = true; float t = (attackTime - pre) / strike;
                t = Mathf.SmoothStep(0, 1, t);
                Vector3 end = aim;
                if (hook)
                {
                    var control = new Vector3(Head.x + (AttackLeft ? -.48f : .48f), aim.y, aim.z + .12f);
                    fist = (1-t)*(1-t)*strikeStart + 2*(1-t)*t*control + t*t*end;
                }
                else fist = Vector3.Lerp(strikeStart, end, t);
            }
            else
            {
                float t = Mathf.Clamp01((attackTime - pre - strike) / recover);
                fist = Vector3.Lerp(blocked ? blockedAt : aim, rest, t * t * (3 - 2 * t));
                if (t >= 1) { attacking = false; pattern++; CompletedAttacks++; wait = pattern % 3 == 1 ? 0.18f : tuning.enemyInterval; }
            }
            if (AttackLeft) Left = fist; else Right = fist;
        }
        private void SetRestPose()
        {
            Root = new Vector3(0, 0, 1.08f);
            float bob = (Mathf.Cos(clock * 3) - 1) * .018f;
            Head = Root + new Vector3(Mathf.Sin(clock * 1.5f) * .025f, height + bob, reaction * .24f - MotionWeight * (Hook ? .25f : .12f));
            Body = Head + new Vector3(0, -.43f, .015f);
            Left = Head + new Vector3(-.19f, -.10f, -.23f);
            Right = Head + new Vector3(.18f, -.09f, -.19f);
        }
    }
}
