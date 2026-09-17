using UnityEngine;

namespace Hapbeat.Boxing
{
    // Head-relative displacement, never summed path length: jitter, walking and
    // rotating the controller cannot accumulate a stronger punch over time.
    public sealed class BoxingPunch
    {
        private Vector3 last, start;
        private float quiet, age, startDepth;
        private bool initialized, ready, moving, spent;
        public bool Moving => moving;
        public void Reset() { initialized = ready = moving = spent = false; quiet = age = 0; }

        public void Sample(Vector3 handRelativeToHead, Vector3 forward, float dt, BoxingTuning tuning)
        {
            if (dt <= 0 || dt > 0.1f) { Reset(); return; }
            if (!initialized) { last = start = handRelativeToHead; initialized = true; return; }
            float speed = Vector3.Distance(handRelativeToHead, last) / dt;
            float depth = Vector3.Dot(handRelativeToHead, forward);
            if (spent)
            {
                // Contact consumes this stroke, including a blocked punch. Withdrawal
                // into the stance zone is required before a new strong stroke.
                if (depth <= tuning.punchReturnDepth) { spent = false; ready = false; quiet = 0; }
                else { last = handRelativeToHead; return; }
            }
            if (speed <= tuning.punchRestSpeed)
            {
                quiet += dt;
                if (quiet >= tuning.punchRestSeconds)
                {
                    moving = false; ready = true; start = handRelativeToHead; startDepth = depth;
                }
            }
            else
            {
                quiet = 0;
                if (!moving && ready && speed >= tuning.punchStartSpeed)
                {
                    moving = true; ready = false; start = last;
                    startDepth = Vector3.Dot(last, forward); age = 0;
                }
            }
            if (moving)
            {
                age += dt;
                if (age > tuning.punchMaximumSeconds) { moving = false; ready = false; }
            }
            last = handRelativeToHead;
        }

        public float Consume(Vector3 contactRelativeToHead, BoxingTuning tuning)
        {
            float distance = moving ? Vector3.Distance(start, contactRelativeToHead) : 0;
            float strength = tuning.PunchStrength(distance);
            if (startDepth > tuning.punchReturnDepth) strength = 0;
            else if (startDepth < -tuning.punchRearDepth)
                strength = Mathf.Clamp01(strength * tuning.punchRearMultiplier);
            moving = ready = false; spent = true; quiet = 0;
            return strength;
        }
    }
}
