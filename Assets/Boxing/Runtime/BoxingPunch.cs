using UnityEngine;

namespace Hapbeat.Boxing
{
    // Head-relative displacement, never summed path length: jitter, walking and
    // rotating the controller cannot accumulate a stronger punch over time.
    public sealed class BoxingPunch
    {
        private Vector3 last, start;
        private float quiet, age, startDistance;
        private bool initialized, ready, moving, spent;
        public bool Moving => moving;
        public void Reset() { initialized = ready = moving = spent = false; quiet = age = 0; }

        public void Sample(Vector3 handRelativeToHead, float dt, BoxingTuning tuning)
        {
            if (dt <= 0 || dt > 0.1f) { Reset(); return; }
            if (!initialized) { last = start = handRelativeToHead; initialized = true; return; }
            float speed = Vector3.Distance(handRelativeToHead, last) / dt;
            float distance = handRelativeToHead.magnitude;
            if (spent)
            {
                // Contact consumes this stroke, including a blocked punch. Withdrawal
                // into the stance zone is required before a new strong stroke.
                if (distance <= tuning.punchReturnDistance) { spent = false; ready = false; quiet = 0; }
                else { last = handRelativeToHead; return; }
            }
            if (speed <= tuning.punchRestSpeed)
            {
                quiet += dt;
                if (quiet >= tuning.punchRestSeconds)
                {
                    moving = false; ready = true; start = handRelativeToHead; startDistance = distance;
                }
            }
            else
            {
                quiet = 0;
                if (!moving && ready && speed >= tuning.punchStartSpeed)
                {
                    moving = true; ready = false; start = last;
                    startDistance = last.magnitude; age = 0;
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
            if (startDistance > tuning.punchReturnDistance) strength = 0;
            moving = ready = false; spent = true; quiet = 0;
            return strength;
        }
    }
}
