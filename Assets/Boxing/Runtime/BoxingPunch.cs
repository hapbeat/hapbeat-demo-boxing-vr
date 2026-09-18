using UnityEngine;

namespace Hapbeat.Boxing
{
    // HMD-relative speed with a fresh withdrawal or a per-hand one-second recovery.
    // No static hold, long stroke or rear windup is required.
    public sealed class BoxingPunch
    {
        private Vector3 last, contact, velocity;
        private float sinceContact, peakSpeed, peakAge;
        private bool initialized, ready;
        public bool Moving { get; private set; }
        public void Reset()
        {
            initialized=ready=Moving=false; velocity=Vector3.zero;
            sinceContact=1000; peakSpeed=peakAge=0;
        }

        public void Sample(Vector3 relativeHand, float dt, BoxingTuning tuning)
        {
            if(dt<=0 || dt>.1f) { Reset(); return; }
            if(!initialized)
            {
                last=relativeHand; ready=relativeHand.magnitude<=tuning.punchReturnDistance;
                sinceContact=1000; initialized=true; return;
            }
            sinceContact+=dt;
            velocity=Vector3.Lerp(velocity,(relativeHand-last)/dt,1-Mathf.Exp(-dt/.035f));
            float speed=Mathf.Min(8,velocity.magnitude);
            peakAge+=dt;
            if(speed>=peakSpeed || peakAge>.18f) { peakSpeed=speed; peakAge=0; }
            if(!ready && relativeHand.magnitude<=tuning.punchReturnDistance &&
                (contact.magnitude-relativeHand.magnitude>=tuning.punchRetraction ||
                 Vector3.Distance(contact,relativeHand)>=tuning.punchRetraction*1.4f)) ready=true;
            Moving=speed>=tuning.punchMinimumSpeed;
            last=relativeHand;
        }

        public float Consume(Vector3 contactRelativeToHead, BoxingTuning tuning)
        {
            float strength=initialized && (ready || sinceContact>=tuning.punchRepeatSeconds) ?
                tuning.PunchStrengthForSpeed(peakSpeed) : 0;
            contact=contactRelativeToHead; sinceContact=0; ready=Moving=false;
            peakSpeed=peakAge=0; velocity=Vector3.zero;
            return strength;
        }
    }
}
